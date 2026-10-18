using Snapwell.App.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>Win32 に触れず、表示時の最前面の維持と失敗の隔離を検証する。</summary>
public sealed class OverlayZOrderTests
{
    /// <summary>初回と Hide 後の表示では状態にかかわらず昇格し、通常のログは残さない。</summary>
    [Fact]
    public void EveryDisplayStartRaisesWithoutLogging()
    {
        var api = new FakeApi { IsTopmost = false, HasWindowAbove = true };
        var lines = new List<string>();
        var order = Create(api, lines);

        for (var index = 0; index < 3; index++)
        {
            order.Show();
            order.Hide();
        }

        Assert.Equal(3, api.Raises);
        Assert.Equal(0, api.Checks);
        Assert.Empty(lines);
    }

    /// <summary>TOPMOST の喪失または別の窓の割り込みを一度だけ修復して一行に残す。</summary>
    [Theory]
    [InlineData(false, false, "zorder-topmost-lost")]
    [InlineData(true, true, "zorder-window-above")]
    [InlineData(false, true, "zorder-topmost-lost")]
    public void DisplacementRaisesAndLogsOnce(bool topmost, bool above, string reason)
    {
        var api = new FakeApi();
        var lines = new List<string>();
        var order = Create(api, lines);
        order.Show();
        // 対象の活性化などが表示開始の後に起きた場合を再現する。
        api.IsTopmost = topmost;
        api.HasWindowAbove = above;
        order.Show();
        order.Show();

        Assert.Equal(2, api.Raises);
        var line = Assert.Single(lines);
        Assert.StartsWith("1970-01-01T00:00:00", line);
        Assert.Contains($"generation=42 state={reason}", line);
        Assert.Equal(1, line.Count(character => character == '\n'));
    }

    /// <summary>通常の描画更新では安い確認だけで戻り、昇格と記録を繰り返さない。</summary>
    [Fact]
    public void HealthyFramesDoNotRaiseOrLog()
    {
        var api = new FakeApi();
        var lines = new List<string>();
        var order = Create(api, lines);
        order.Show();
        for (var index = 0; index < 120; index++)
        {
            order.Show();
        }

        Assert.Equal(1, api.Raises);
        Assert.Equal(120, api.Checks);
        Assert.Empty(lines);
    }

    /// <summary>失敗の戻り値と例外を隔離し、更新ごとの再試行を避けてから再度修復する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RaiseFailureDoesNotEscapeAndCanRecover(bool throws)
    {
        var api = new FakeApi();
        var lines = new List<string>();
        long now = 0;
        var order = Create(api, lines, () => now);
        order.Show();
        api.IsTopmost = false;
        api.FailRaise = true;
        api.ThrowOnRaise = throws;
        order.Show();
        order.Show();
        Assert.Equal(2, api.Raises);
        Assert.Empty(lines);

        api.FailRaise = false;
        api.ThrowOnRaise = false;
        now = 250;
        order.Show();
        Assert.Equal(3, api.Raises);
        Assert.Single(lines);
    }

    /// <summary>初回昇格の失敗後でも、次の表示開始は待ち時間なしで昇格する。</summary>
    [Fact]
    public void NewDisplayBypassesFailureCooldown()
    {
        var api = new FakeApi { FailRaise = true };
        var lines = new List<string>();
        var order = Create(api, lines, () => 0);
        order.Show();
        order.Hide();
        api.FailRaise = false;
        order.Show();
        Assert.Equal(2, api.Raises);
        Assert.Empty(lines);
    }

    /// <summary>状態の確認または診断の例外も描画の外へ漏らさない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckAndDiagnosticFailuresDoNotEscape(bool diagnosticFails)
    {
        var api = new FakeApi();
        var order = new OverlayZOrder(api, 42, (_, _, _) => throw new IOException());
        order.Show();
        api.HasWindowAbove = true;
        api.ThrowOnCheck = !diagnosticFails;
        order.Show();
        Assert.Equal(diagnosticFails ? 2 : 1, api.Raises);
    }

    private static OverlayZOrder Create(FakeApi api, List<string> lines, Func<long>? milliseconds = null) =>
        new(api, 42, (generation, state, exception) =>
            lines.Add(LauncherLog.Format(DateTimeOffset.UnixEpoch, generation, state, exception)), milliseconds);

    private sealed class FakeApi : IOverlayZOrderApi
    {
        private bool _isTopmost = true;
        internal int Raises { get; private set; }
        internal int Checks { get; private set; }
        internal bool FailRaise { get; set; }
        internal bool ThrowOnRaise { get; set; }
        internal bool ThrowOnCheck { get; set; }

        public bool IsTopmost
        {
            get
            {
                Checks++;
                return ThrowOnCheck ? throw new InvalidOperationException() : _isTopmost;
            }
            set => _isTopmost = value;
        }

        public bool HasWindowAbove { get; set; }

        public bool TryRaise()
        {
            Raises++;
            if (ThrowOnRaise)
            {
                throw new InvalidOperationException();
            }

            if (FailRaise)
            {
                return false;
            }

            _isTopmost = true;
            HasWindowAbove = false;
            return true;
        }
    }
}
