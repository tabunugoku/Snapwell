using Snapwell.Core.Rules;

namespace Snapwell.Platform.Windows;

/// <summary>公開 API による調査を使い、ルールの条件になり得るウィンドウを列挙する。</summary>
public sealed class Win32OpenWindowSource(IWindowInspector inspector, IProcessNameSource processNames,
    uint ownProcessId) : IOpenWindowSource
{
    /// <summary>常に対象外のウィンドウと空のタイトルを除き、現在の識別情報を返す。</summary>
    public IReadOnlyList<OpenWindowInfo> List()
    {
        var windows = new List<OpenWindowInfo>();
        foreach (var handle in inspector.GetTopLevelWindows().Distinct())
        {
            if (!inspector.TryInspect(handle, out var window) || string.IsNullOrEmpty(window.Title) ||
                WindowClassifier.Classify(window, ownProcessId) == WindowKind.Excluded)
            {
                continue;
            }

            string? processName;
            try
            {
                processName = processNames.GetName(window.ProcessId);
            }
            catch (Exception)
            {
                processName = null;
            }

            windows.Add(new(window.Title, processName, window.ClassName));
        }

        return windows;
    }
}
