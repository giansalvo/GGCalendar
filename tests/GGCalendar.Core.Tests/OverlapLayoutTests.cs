using GGCalendar.Core.Layout;

namespace GGCalendar.Core.Tests;

public class OverlapLayoutTests
{
    private static readonly DateTime D = new(2026, 10, 9);
    private static (DateTime S, DateTime E) I(double h1, double h2) => (D.AddHours(h1), D.AddHours(h2));

    private static IReadOnlyList<PlacedItem<(DateTime S, DateTime E)>> Run(params (DateTime, DateTime)[] items)
        => OverlapLayout.Place(items, i => i.Item1, i => i.Item2)
            .Select(p => new PlacedItem<(DateTime S, DateTime E)>(p.Item, p.Column, p.ColumnCount)).ToList();

    [Fact]
    public void Disjoint_events_use_a_single_column()
    {
        var r = Run(I(9, 10), I(10, 11));
        Assert.All(r, p => Assert.Equal((0, 1), (p.Column, p.ColumnCount)));
    }

    [Fact]
    public void Overlapping_events_are_side_by_side()
    {
        var r = Run(I(16.75, 18.25), I(16.75, 18.25));
        Assert.Equal(new[] { 0, 1 }, r.Select(p => p.Column).OrderBy(c => c));
        Assert.All(r, p => Assert.Equal(2, p.ColumnCount));
    }

    [Fact]
    public void Column_is_reused_inside_cluster_and_clusters_are_independent()
    {
        var r = Run(I(9, 12), I(9, 10), I(10, 11), I(14, 15));
        Assert.Equal(2, r.Single(p => p.Item.S == D.AddHours(9) && p.Item.E == D.AddHours(10)).ColumnCount);
        Assert.Equal(1, r.Single(p => p.Item.S == D.AddHours(14)).ColumnCount);
    }
}
