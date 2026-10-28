using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

/// <summary>
/// The description of an event, read only: bold, italic and underlined text, lists and links (see <see cref="DescriptionText"/>).
/// A click on a link raises <see cref="LinkRequested"/>; the window asks before opening it.
/// </summary>
public sealed class DescriptionBlock : TextBlock
{
    private static readonly Cursor Hand = new(StandardCursorType.Hand);
    private readonly IReadOnlyList<DescriptionRun> _runs;

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public event Action<Uri>? LinkRequested;

    public DescriptionBlock(string? description, IBrush foreground, IBrush link)
    {
        _runs = DescriptionText.Parse(description);
        Foreground = foreground;
        TextWrapping = TextWrapping.Wrap;
        MaxLines = 8;
        TextTrimming = TextTrimming.CharacterEllipsis;
        FontFeatures = FontFeatureCollection.Parse("-calt"); // the text is shown as it is written: no arrow for "-->"
        foreach (var r in _runs)
        {
            var run = new Run(r.Text);
            if (r.Style.HasFlag(DescriptionStyle.Bold)) run.FontWeight = FontWeight.Bold;
            if (r.Style.HasFlag(DescriptionStyle.Italic)) run.FontStyle = FontStyle.Italic;
            if (r.Style.HasFlag(DescriptionStyle.Underline)) run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
            if (r.Link is not null) run.Foreground = link;
            // Titles: bold (already) and bigger; the size of the text of the card is 14.
            if (r.Style.HasFlag(DescriptionStyle.Heading1)) run.FontSize = 28;
            else if (r.Style.HasFlag(DescriptionStyle.Heading2)) run.FontSize = 21;
            else if (r.Style.HasFlag(DescriptionStyle.Heading3)) run.FontSize = 17;
            Inlines!.Add(run);
        }
    }

    /// <summary>
    /// The link under a point of the block (in its own coordinates), or null. It looks at where the letters of each link are
    /// (the layout is asked for the boxes of the text of the link), not at which letter is under the point: after a line break
    /// the second answer is not reliable.
    /// </summary>
    public Uri? LinkAt(Point point)
    {
        var start = 0;
        foreach (var run in _runs)
        {
            if (run.Link is not null && run.Text.Length > 0 && TextLayout.HitTestTextRange(start, run.Text.Length).Any(box => box.Contains(point)))
                return run.Link;
            start += run.Text.Length;
        }

        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = LinkAt(e.GetPosition(this)) is null ? null : Hand;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || LinkAt(e.GetPosition(this)) is not { } uri) return;
        e.Handled = true;
        LinkRequested?.Invoke(uri);
    }
}
