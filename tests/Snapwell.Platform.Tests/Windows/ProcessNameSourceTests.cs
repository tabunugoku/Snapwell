using Snapwell.Platform.Windows;

namespace Snapwell.Platform.Tests.Windows;

/// <summary>プロセス ID の再利用、失敗、ハンドル解放と並行呼び出しを偽の API で検証する。</summary>
public sealed class ProcessNameSourceTests
{
    /// <summary>同じ開始時刻だけキャッシュを使い、別プロセスの名前を返さない。</summary>
    [Fact]
    public void ReusedProcessIdDoesNotReturnPreviousName()
    {
        var api = new Api();
        var source = new Win32ProcessNameSource(api);
        Assert.Equal("first.exe", source.GetName(123));
        Assert.Equal("first.exe", source.GetName(123));
        Assert.Equal(1, api.ImageReads);
        api.Start = 2;
        api.Path = @"C:\Apps\second.exe";
        Assert.Equal("second.exe", source.GetName(123));
        Assert.Equal(2, api.ImageReads);
        Assert.Equal(3, api.Closes);
    }

    /// <summary>開始時刻の失敗はキャッシュせず、名前の失敗でも必ずハンドルを閉じる。</summary>
    [Fact]
    public void MissingStartTimeIsNotCachedAndFailedQueriesCloseHandles()
    {
        var api = new Api { Start = null };
        var source = new Win32ProcessNameSource(api);
        Assert.Equal("first.exe", source.GetName(123));
        api.Path = @"C:\Apps\second.exe";
        Assert.Equal("second.exe", source.GetName(123));
        api.Path = null;
        Assert.Null(source.GetName(123));
        Assert.Equal(3, api.Closes);
        api.CanOpen = false;
        Assert.Null(source.GetName(123));
        Assert.Equal(3, api.Closes);
    }

    /// <summary>並行呼び出しでもキャッシュ更新とハンドルの寿命を守る。</summary>
    [Fact]
    public void ConcurrentCallsShareCachedNameAndCloseEveryHandle()
    {
        var api = new Api();
        var source = new Win32ProcessNameSource(api);
        Parallel.For(0, 100, _ => Assert.Equal("first.exe", source.GetName(123)));
        Assert.Equal(1, api.ImageReads);
        Assert.Equal(100, api.Closes);
    }

    private sealed class Api : IProcessNameApi
    {
        internal bool CanOpen = true;
        internal ulong? Start = 1;
        internal string? Path = @"C:\Apps\first.exe";
        internal int ImageReads;
        internal int Closes;
        public nint Open(uint processId) => CanOpen ? (nint)processId : 0;
        public ulong? GetStartTime(nint handle) => Start;
        public string? GetImagePath(nint handle) { ImageReads++; return Path; }
        public void Close(nint handle) => Closes++;
    }
}
