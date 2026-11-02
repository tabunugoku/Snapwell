using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>ブラウザーで開く固定のリンクが、更新のリンクの許可条件を満たすことを確かめる。</summary>
public sealed class RepositoryLinksTests
{
    /// <summary>どちらのリンクも、更新のリンクと同じ許可条件（HTTPS・GitHub）を通る。</summary>
    [Fact]
    public void LinksPassTheUpdateLinkPolicy()
    {
        Assert.True(UpdateLinks.IsAllowed(RepositoryLinks.Repository));
        Assert.True(UpdateLinks.IsAllowed(RepositoryLinks.Releases));
    }
}
