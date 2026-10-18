using Snapwell.Core.Geometry;
using Snapwell.Core.Input;

namespace Snapwell.Core.Tests.Input;

/// <summary>OS に触れずに子ウィンドウのタイトルバー判定と対象の境界を検証する。</summary>
public sealed class CaptionHitTestTests
{
    private static readonly Vec2 _position = new(-1200, -30);

    /// <summary>トップレベルがクライアントでも、その子のタイトルバーなら成功する。</summary>
    [Fact]
    public void ChildCaptionSucceedsWhenRootIsClient()
    {
        var api = new FakeApi();

        Assert.True(new CaptionHitTest(api).IsCaption(1, _position));
        Assert.Equal(new (nint, Vec2)[] { (1, _position), (2, _position) }, api.Hits);
        Assert.Equal(_position, api.LookupPosition);
    }

    /// <summary>従来のタイトルバーでは子の取得も問い合わせも行わない。</summary>
    [Fact]
    public void RootCaptionSucceedsWithoutLookingForChild()
    {
        var api = new FakeApi { RootHit = 2 };

        Assert.True(new CaptionHitTest(api).IsCaption(1, _position));
        Assert.Equal(new (nint, Vec2)[] { (1, _position) }, api.Hits);
        Assert.Null(api.LookupPosition);
    }

    /// <summary>タブ・ボタン・枠・問い合わせ失敗では子があっても成功しない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(-1)]
    [InlineData(null)]
    public void ChildNonCaptionOrTimeoutFails(int? childHit)
    {
        var api = new FakeApi { ChildHit = childHit };

        Assert.False(new CaptionHitTest(api).IsCaption(1, _position));
        Assert.Equal(2, api.Hits.Count);
    }

    /// <summary>対象の外や別のトップレベルの子には問い合わせない。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    public void MissingOrForeignChildIsRejected(int child, int root)
    {
        var api = new FakeApi { Child = child, ChildRoot = root };

        Assert.False(new CaptionHitTest(api).IsCaption(1, _position));
        Assert.Single(api.Hits);
    }

    /// <summary>トップレベルの送信失敗でも子の応答が得られれば判定できる。</summary>
    [Fact]
    public void RootTimeoutCanFallBackToChild()
    {
        var api = new FakeApi { RootHit = null };

        Assert.True(new CaptionHitTest(api).IsCaption(1, _position));
    }

    /// <summary>無効な対象をブロードキャスト先として使わない。</summary>
    [Fact]
    public void MissingTargetDoesNotQueryWindows()
    {
        var api = new FakeApi();

        Assert.False(new CaptionHitTest(api).IsCaption(0, _position));
        Assert.Empty(api.Hits);
        Assert.Null(api.LookupPosition);
    }

    private sealed class FakeApi : ICaptionHitTestApi
    {
        internal int? RootHit = 1;
        internal int? ChildHit = 2;
        internal nint Child = 2;
        internal nint ChildRoot = 1;
        internal Vec2? LookupPosition;
        internal List<(nint Handle, Vec2 _position)> Hits { get; } = [];

        public nint GetWindowAt(Vec2 position)
        {
            LookupPosition = position;
            return Child;
        }

        public nint GetRootWindow(nint handle)
        {
            Assert.Equal(Child, handle);
            return ChildRoot;
        }

        public int? HitTest(nint handle, Vec2 position)
        {
            Hits.Add((handle, position));
            return handle == 1 ? RootHit : ChildHit;
        }
    }
}
