using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>ブラウザーで開く固定のリンクが、従来の値のままで、更新のリンクの許可条件を満たすことを確かめる。</summary>
public sealed class RepositoryLinksTests
{
    /// <summary>URL はまとめる前と同じ値で、末尾のスラッシュも増えない。</summary>
    [Fact]
    public void LinksKeepTheirOriginalValues()
    {
        Assert.Equal("https://github.com/tabunugoku/Snapwell", RepositoryLinks.Repository.AbsoluteUri);
        Assert.Equal("https://github.com/tabunugoku/Snapwell/releases", RepositoryLinks.Releases.AbsoluteUri);
    }

    /// <summary>どちらのリンクも、更新のリンクの許可条件（絶対 HTTPS・ホストあり・認証情報なし）を通る。</summary>
    [Fact]
    public void LinksPassTheUpdateLinkPolicy()
    {
        Assert.True(UpdateLinks.IsAllowed(RepositoryLinks.Repository));
        Assert.True(UpdateLinks.IsAllowed(RepositoryLinks.Releases));
    }
}
