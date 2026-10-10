namespace GGCalendar.Core.Layout;

public readonly record struct PlacedItem<T>(T Item, int Column, int ColumnCount);

public static class OverlapLayout
{
    /// <summary>
    /// Assigns each interval a column inside its cluster of mutually overlapping items,
    /// so that overlapping events are drawn side by side (as in Google Calendar).
    /// </summary>
    public static IReadOnlyList<PlacedItem<T>> Place<T>(IEnumerable<T> items, Func<T, DateTime> start, Func<T, DateTime> end)
    {
        var sorted = items.OrderBy(start).ThenByDescending(i => end(i) - start(i)).ToList();
        var result = new List<PlacedItem<T>>(sorted.Count);
        var cluster = new List<(T Item, int Column)>();
        var columnEnds = new List<DateTime>();
        var clusterEnd = DateTime.MinValue;

        void Flush()
        {
            var count = columnEnds.Count;
            foreach (var (item, column) in cluster)
                result.Add(new PlacedItem<T>(item, column, count));
            cluster.Clear();
            columnEnds.Clear();
        }

        foreach (var item in sorted)
        {
            var s = start(item);
            var e = end(item);
            if (cluster.Count > 0 && s >= clusterEnd)
                Flush();

            var col = columnEnds.FindIndex(ce => ce <= s);
            if (col < 0)
            {
                columnEnds.Add(e);
                col = columnEnds.Count - 1;
            }
            else
            {
                columnEnds[col] = e;
            }

            cluster.Add((item, col));
            if (cluster.Count == 1 || e > clusterEnd)
                clusterEnd = e;
        }

        Flush();
        return result;
    }
}
