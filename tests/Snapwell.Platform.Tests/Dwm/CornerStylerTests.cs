using Snapwell.Platform.Dwm;

namespace Snapwell.Platform.Tests.Dwm;

/// <summary>角設定の記録と復元をフェイクで検証する。</summary>
public sealed class CornerStylerTests
{
    /// <summary>変更した二つのウィンドウだけを既定へ戻す。</summary>
    [Fact]
    public void RestoreAllResetsModifiedHandlesAndClearsTracking()
    {
        var api = new FakeCornerApi();
        var styler = new CornerStyler(api);
        Assert.True(styler.Apply(1, CornerStyle.Round));
        Assert.True(styler.Apply(2, CornerStyle.RoundSmall));
        Assert.True(styler.Apply(3, CornerStyle.Default));
        Assert.Equal(2, styler.TrackedCount);
        api.Calls.Clear();
        styler.RestoreAll();
        Assert.Equal(2, api.Calls.Count);
        Assert.Contains(((nint)1, CornerStyle.Default), api.Calls);
        Assert.Contains(((nint)2, CornerStyle.Default), api.Calls);
        Assert.Equal(0, styler.TrackedCount);
        styler.RestoreAll();
        Assert.Equal(2, api.Calls.Count);
    }

    /// <summary>既定・失敗・閉じたウィンドウを記録に残さない。</summary>
    [Fact]
    public void DefaultFailureAndForgetRemoveTracking()
    {
        var api = new FakeCornerApi();
        var styler = new CornerStyler(api);
        styler.Apply(1, CornerStyle.Round);
        styler.Apply(1, CornerStyle.Default);
        Assert.Equal(0, styler.TrackedCount);
        styler.Apply(1, CornerStyle.Round);
        api.Success = false;
        Assert.False(styler.Apply(1, CornerStyle.RoundSmall));
        Assert.False(styler.Apply(2, CornerStyle.Round));
        Assert.Equal(0, styler.TrackedCount);
        api.Success = true;
        styler.Apply(3, CornerStyle.DoNotRound);
        styler.Forget(3);
        Assert.Equal(0, styler.TrackedCount);
        styler.Apply(4, CornerStyle.Round);
        api.Success = false;
        styler.RestoreAll();
        Assert.Equal(0, styler.TrackedCount);
    }

    private sealed class FakeCornerApi : ICornerApi
    {
        internal bool Success { get; set; } = true;
        internal List<(nint, CornerStyle)> Calls { get; } = [];

        public bool TrySet(nint handle, CornerStyle style)
        {
            Calls.Add((handle, style));
            return Success;
        }
    }
}
