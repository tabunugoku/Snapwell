using System.Diagnostics;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>専用コピーのプロセスだけを停止し、配置先の外の同名プロセスを保護する。</summary>
public sealed class UpdateResidentStopperTests
{
    /// <summary>終了要求に応じないコピーを強制終了し、外部と退避済みイメージには触れない。</summary>
    [Fact]
    public void StopsOnlyCurrentImagesInsideInstallDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "snapwell-stop-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "install");
        var outside = install + "-outside";
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(outside);
        var image = Path.Combine(install, "ping.exe");
        var otherImage = Path.Combine(outside, "ping.exe");
        var oldImage = image + ".old";
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), image);
        File.Copy(image, otherImage);
        File.Copy(image, oldImage);
        Process? resident = null;
        Process? other = null;
        Process? old = null;
        try
        {
            resident = Start(image);
            other = Start(otherImage);
            old = Start(oldImage);
            var names = new UpdateSignalNames("S-1-5-21-1", @"Local\Snapwell.StopTest." + Guid.NewGuid().ToString("N"));
            using var quit = UpdateSignalNames.Create(names.Quit);
            Assert.True(new UpdateResidentStopper(names).StopResidents(install, TimeSpan.FromSeconds(4)));
            Assert.True(quit.WaitOne(0));
            Assert.True(resident.HasExited);
            Assert.False(other.HasExited);
            Assert.False(old.HasExited);
        }
        finally
        {
            StopAndDispose(resident);
            StopAndDispose(other);
            StopAndDispose(old);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>配置先に常駐が無ければ、終了信号が無くても成功する。</summary>
    [Fact]
    public void NoResidentsSucceeds()
    {
        var root = Path.Combine(Path.GetTempPath(), "snapwell-no-resident-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var names = new UpdateSignalNames("S-1-5-21-1", @"Local\Snapwell.StopTest." + Guid.NewGuid().ToString("N"));
            Assert.True(new UpdateResidentStopper(names).StopResidents(root, TimeSpan.Zero));
        }
        finally { Directory.Delete(root); }
    }

    private static Process Start(string image)
    {
        var process = Process.Start(new ProcessStartInfo(image, "-n 60 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        Assert.False(process.HasExited);
        return process;
    }

    private static void StopAndDispose(Process? process)
    {
        if (process is null) { return; }
        try
        {
            if (!process.HasExited) { process.Kill(); }
            Assert.True(process.WaitForExit(5000));
        }
        finally { process.Dispose(); }
    }
}
