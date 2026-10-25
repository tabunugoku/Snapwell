using System.Text;
using System.Text.Json;

namespace Snapwell.Platform.Updates;

/// <summary>ファイルの差し替えを完了したかを表す。</summary>
public enum ApplyStatus { Success, Failed }

/// <summary>巻き戻しを完了したかを表す。</summary>
public enum RollbackStatus { RolledBack, RollbackIncomplete }

/// <summary>巻き戻せなかった操作と英語の診断。</summary>
public sealed record RollbackFailure(string Kind, string RelativePath, string Diagnostic);

/// <summary>巻き戻しの状態と失敗した項目。</summary>
public sealed record RollbackResult(RollbackStatus Status, IReadOnlyList<RollbackFailure> Failures);

/// <summary>差し替えの状態、英語の診断、および失敗時の巻き戻し結果。</summary>
public sealed record ApplyResult(ApplyStatus Status, string? Diagnostic = null, RollbackResult? Rollback = null);

/// <summary>検証済みの新版をコピーし、旧版の退避とジャーナルによる巻き戻しを行う。</summary>
public sealed class UpdateApplier(IUpdateFileSystem fs)
{
    private const string RenamedOld = "renamed-old";
    private const string CopiedNew = "copied-new";
    private const string CreatedDir = "created-dir";

    /// <summary>既存のジャーナルを使わず、展開済みのファイルを適用する。</summary>
    public ApplyResult Apply(string installDir, string stagingDir, IReadOnlyList<string> newFiles, string journalPath)
    {
        var plans = new List<JournalEntry>();
        string installRoot;
        string stagingRoot;
        string[] currentFiles;
        string[] files;
        try
        {
            installRoot = Path.GetFullPath(installDir);
            stagingRoot = Path.GetFullPath(stagingDir);
            journalPath = Path.GetFullPath(journalPath);
            if (Contains(installRoot, stagingRoot) || Contains(stagingRoot, installRoot)
                || string.Equals(installRoot, stagingRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Install and staging directories must not overlap.");
            }
            files = newFiles.ToArray();
            var uniqueFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relative in files)
            {
                var destination = Resolve(installRoot, relative);
                Resolve(stagingRoot, relative);
                if (!uniqueFiles.Add(destination) || IsOld(relative)
                    || string.Equals(destination, journalPath, StringComparison.OrdinalIgnoreCase)
                    || Contains(destination, journalPath))
                {
                    throw new ArgumentException(
                        "New files must be unique and must not conflict with backups or journal.");
                }
            }
            currentFiles = fs.EnumerateFiles(installRoot).ToArray();
            foreach (var relative in currentFiles)
            {
                var path = Resolve(installRoot, relative);
                if (string.Equals(path + ".old", journalPath, StringComparison.OrdinalIgnoreCase)
                    || Contains(path, journalPath))
                {
                    throw new ArgumentException("The journal must not conflict with installed files or backups.");
                }
            }
            if (currentFiles.Any(IsOld))
            {
                throw new IOException("Previous .old files must be resolved before applying an update.");
            }
            if (fs.Exists(journalPath))
            {
                throw new IOException("An existing journal must be resolved before applying an update.");
            }
            if (!fs.Exists(Path.GetDirectoryName(journalPath)!))
            {
                throw new DirectoryNotFoundException("The journal directory must already exist.");
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return new(ApplyStatus.Failed, "Update preflight failed: " + exception.Message,
                new(RollbackStatus.RolledBack, []));
        }

        try
        {
            foreach (var relative in currentFiles)
            {
                var path = Resolve(installRoot, relative);
                Execute(new(RenamedOld, relative), () => fs.RenameInPlace(path, Path.GetFileName(path) + ".old"));
            }
            foreach (var relative in files)
            {
                var destination = Resolve(installRoot, relative);
                EnsureParent(relative);
                Execute(new(CopiedNew, relative), () => fs.CopyFile(Resolve(stagingRoot, relative), destination));
            }
            return new(ApplyStatus.Success);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return new(ApplyStatus.Failed, "Update apply failed: " + exception.Message,
                RollbackCore(installRoot, plans));
        }

        void Execute(JournalEntry entry, Action operation)
        {
            // 追記が途中で失敗しても、この呼び出し中は計画を使って元に戻せる。
            plans.Add(entry);
            WriteEntry(journalPath, entry, "planned");
            operation();
            WriteEntry(journalPath, entry, "completed");
        }

        void EnsureParent(string relative)
        {
            var segments = relative.Split('/');
            var parent = "";
            for (var index = 0; index < segments.Length - 1; index++)
            {
                parent = parent.Length == 0 ? segments[index] : parent + "/" + segments[index];
                var path = Resolve(installRoot, parent);
                if (!fs.Exists(path))
                {
                    Execute(new(CreatedDir, parent), () => fs.CreateDirectory(path));
                }
            }
        }
    }

    /// <summary>旧版のファイルを削除し、削除に失敗した場合はジャーナルを残す。</summary>
    public void CommitCleanup(string installDir, string journalPath)
    {
        try
        {
            var root = Path.GetFullPath(installDir);
            var failed = false;
            foreach (var relative in fs.EnumerateFiles(root).Where(IsOld).ToArray())
            {
                try
                {
                    fs.DeleteFile(Resolve(root, relative));
                }
                catch (Exception exception) when (IsFileFailure(exception))
                {
                    failed = true;
                }
            }
            if (!failed)
            {
                fs.DeleteFile(journalPath);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            // 次の起動で再試行できるように、消せないファイルは残す。
        }
    }

    /// <summary>ジャーナルの計画だけから逆順に戻し、戻せない項目があっても続行する。</summary>
    public RollbackResult Rollback(string installDir, string journalPath)
    {
        try
        {
            var root = Path.GetFullPath(installDir);
            var failures = new List<RollbackFailure>();
            var plans = ReadPlans(root, journalPath, failures);
            failures.AddRange(RollbackCore(root, plans).Failures);
            return new(failures.Count == 0 ? RollbackStatus.RolledBack : RollbackStatus.RollbackIncomplete, failures);
        }
        catch (Exception exception) when (IsFileFailure(exception) || exception is JsonException)
        {
            return new(RollbackStatus.RollbackIncomplete,
                [new("journal", journalPath, "Cannot read rollback journal: " + exception.Message)]);
        }
    }

    private RollbackResult RollbackCore(string root, IReadOnlyList<JournalEntry> plans)
    {
        var failures = new List<RollbackFailure>();
        var renamedPaths = plans.Where(entry => entry.Kind == RenamedOld)
            .Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = plans.Count - 1; index >= 0; index--)
        {
            var entry = plans[index];
            try
            {
                var path = Resolve(root, entry.Path);
                switch (entry.Kind)
                {
                    case CopiedNew:
                        // 再試行では、退避がすでに戻った旧版を新版と誤認して削除しない。
                        if ((!renamedPaths.Contains(entry.Path) || fs.Exists(path + ".old")) && fs.Exists(path))
                        {
                            fs.DeleteFile(path);
                        }
                        break;
                    case CreatedDir:
                        fs.DeleteDirectoryIfEmpty(path);
                        break;
                    case RenamedOld:
                        if (fs.Exists(path + ".old"))
                        {
                            fs.RenameInPlace(path + ".old", Path.GetFileName(path));
                        }
                        else if (!fs.Exists(path))
                        {
                            throw new FileNotFoundException("Both the original file and its backup are missing.");
                        }
                        break;
                }
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                failures.Add(new(entry.Kind, entry.Path, "Rollback operation failed: " + exception.Message));
            }
        }
        return new(failures.Count == 0 ? RollbackStatus.RolledBack : RollbackStatus.RollbackIncomplete, failures);
    }

    private List<JournalEntry> ReadPlans(string root, string journalPath, List<RollbackFailure> failures)
    {
        var lines = fs.ReadLines(journalPath).ToArray();
        var plans = new List<JournalEntry>();
        for (var index = 0; index < lines.Length; index++)
        {
            try
            {
                using var document = JsonDocument.Parse(lines[index]);
                var value = document.RootElement;
                if (value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty("state", out var stateValue)
                    || !value.TryGetProperty("kind", out var kindValue)
                    || !value.TryGetProperty("path", out var pathValue)
                    || stateValue.ValueKind != JsonValueKind.String
                    || kindValue.ValueKind != JsonValueKind.String
                    || pathValue.ValueKind != JsonValueKind.String)
                {
                    throw new IOException("Invalid journal entry.");
                }
                var state = stateValue.GetString();
                var kind = kindValue.GetString()!;
                var relative = pathValue.GetString()!;
                Resolve(root, relative);
                if (kind is not (RenamedOld or CopiedNew or CreatedDir) || state is not ("planned" or "completed"))
                {
                    throw new IOException("Unknown journal operation or state.");
                }
                var entry = new JournalEntry(kind, relative);
                if (state == "planned")
                {
                    if (plans.Contains(entry))
                    {
                        throw new IOException("Duplicate journal plan.");
                    }
                    plans.Add(entry);
                }
                else if (!plans.Contains(entry))
                {
                    throw new IOException("Journal completion has no plan.");
                }
            }
            catch (JsonException) when (index == lines.Length - 1)
            {
                // 最後の追記だけは、異常終了によって行の途中で切れることがある。
                break;
            }
            catch (Exception exception) when (IsFileFailure(exception) || exception is JsonException)
            {
                failures.Add(new("journal", journalPath,
                    $"Invalid journal line {index + 1}: {exception.Message}"));
            }
        }
        return plans;
    }

    private void WriteEntry(string journalPath, JournalEntry entry, string state)
    {
        // リフレクションを使わず、NativeAOT でも同じ JSON Lines を書く。
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("state", state);
            writer.WriteString("kind", entry.Kind);
            writer.WriteString("path", entry.Path);
            writer.WriteEndObject();
        }
        fs.AppendLine(journalPath, Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
            || relative.IndexOfAny(['\\', ':', '\0']) >= 0
            || relative.Split('/').Any(segment => segment is "" or "." or ".." || segment.EndsWith(' ')
                || segment.EndsWith('.')))
        {
            throw new ArgumentException("Update paths must be relative file paths without traversal.");
        }
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!Contains(root, fullPath))
        {
            throw new ArgumentException("Update path escapes its root directory.");
        }
        return fullPath;
    }

    private static bool Contains(string root, string path) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsOld(string path) => path.EndsWith(".old", StringComparison.OrdinalIgnoreCase);

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private sealed record JournalEntry(string Kind, string Path);
}
