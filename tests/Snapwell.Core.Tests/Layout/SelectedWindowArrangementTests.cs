using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Layout;

/// <summary>選択集合と選択だけの整列計画を検証する。</summary>
public sealed class SelectedWindowArrangementTests
{
    private static readonly MonitorArea _monitor = new("left", new(-1920, -200, 1920, 1080), 1.5);

    /// <summary>再選択は末尾へ入り、スナップショットは後の操作に影響されない。</summary>
    [Fact]
    public void TogglePreservesInsertionOrderAndClearRemovesAll()
    {
        var selection = new WindowSelection();
        selection.Toggle(3);
        selection.Toggle(1);
        var snapshot = selection.Snapshot();
        selection.Toggle(3);
        Assert.False(selection.Contains(3));
        selection.Toggle(3);
        selection.Toggle(0);
        Assert.Equal(new nint[] { 1, 3 }, selection.Snapshot());
        Assert.Equal(new nint[] { 3, 1 }, snapshot);
        Assert.Equal(2, selection.Count);
        selection.Clear();
        Assert.Empty(selection.Snapshot());
    }

    /// <summary>空の選択では調査も移動計画も生じない。</summary>
    [Fact]
    public void EmptySelectionDoesNotInspectOrPlanWindows()
    {
        var plan = SelectedWindowArrangement.Plan(ArrangeLayout.Grid, [], _monitor, new(),
            _ => throw new InvalidOperationException());
        Assert.Empty(plan.Targets);
        Assert.Equal(0, plan.ExcludedCount);
    }

    /// <summary>一件では作業領域全体を使う。</summary>
    [Fact]
    public void SingleSelectionFillsDestinationWorkArea()
    {
        var plan = SelectedWindowArrangement.Plan(ArrangeLayout.Grid, [7], _monitor, new(),
            _ => new(100, 100, 300, 200));
        Assert.Equal(new ArrangementTarget(7, _monitor.WorkArea), Assert.Single(plan.Targets));
    }

    /// <summary>全方式で二〜六件の選択だけが負座標の対象内に重ならず収まる。</summary>
    [Theory]
    [InlineData(ArrangeLayout.Grid)]
    [InlineData(ArrangeLayout.Columns)]
    [InlineData(ArrangeLayout.Rows)]
    [InlineData(ArrangeLayout.MainAndStack)]
    public void TwoToSixSelectionsUseOnlySelectedHandlesAndDestinationDpi(ArrangeLayout layout)
    {
        for (var count = 2; count <= 6; count++)
        {
            var handles = Enumerable.Range(1, count).Select(value => (nint)value).ToArray();
            var inspected = new List<nint>();
            var plan = SelectedWindowArrangement.Plan(layout, handles, _monitor, new(8, 8), handle =>
            {
                inspected.Add(handle);
                return new(100 + (int)handle * 10, 100, 300, 200);
            });
            Assert.Equal(handles, inspected);
            Assert.Equal(handles.Order(), plan.Targets.Select(target => target.Handle).Order());
            Assert.Equal(0, plan.ExcludedCount);
            Assert.Equal(-1908, plan.Targets.Min(target => target.Bounds.X));
            Assert.Equal(-188, plan.Targets.Min(target => target.Bounds.Y));
            foreach (var target in plan.Targets)
            {
                Assert.True(_monitor.WorkArea.Contains(target.Bounds));
                Assert.All(plan.Targets.Where(other => other.Handle != target.Handle),
                    other => Assert.False(target.Bounds.Intersects(other.Bounds)));
            }
        }
    }

    /// <summary>無効な対象を数え、残る最初の選択をメインにする。</summary>
    [Fact]
    public void UnavailableWindowsAreCountedAndFirstRemainingSelectionIsMain()
    {
        var plan = SelectedWindowArrangement.Plan(ArrangeLayout.MainAndStack, [9, 5, 2], _monitor, new(),
            handle => handle == 9 ? null : new PixelRect((int)handle * 100, 0, 300, 200));
        Assert.Equal(1, plan.ExcludedCount);
        Assert.Equal((nint)5, plan.Targets[0].Handle);
        Assert.Equal(_monitor.WorkArea.X, plan.Targets[0].Bounds.X);
    }

    /// <summary>一画素の区画を作れないときは移動しない。</summary>
    [Fact]
    public void InsufficientWorkAreaProducesNoTargets()
    {
        var plan = SelectedWindowArrangement.Plan(ArrangeLayout.Columns, [1, 2],
            new("tiny", new(0, 0, 1, 1), 1), new(), _ => new(0, 0, 100, 100));
        Assert.Empty(plan.Targets);
    }
}
