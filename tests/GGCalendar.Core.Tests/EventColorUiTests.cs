using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T01, the color of an event, driven like a user would against the real installed data and the test Google account that is
/// already connected. Run it on the developer's PC with the app closed; CI leaves it out (Category "RealGoogle").
/// </summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class EventColorUiTests
{
    // The color the user picks, and what Google calls it ("Pomodoro" is Google's color number 11).
    private const string PickedHex = "#D50000";
    private const string PickedOnGoogle = "11";

    [Fact]
    [Trait("TestCase", "T01")]
    public Task T01_TheColorChosenForAnEventStaysAfterSyncingAndReopeningTheApp() => InAppAsync(async window =>
    {
        // The user creates an event and picks a color different from the calendar's.
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title) { ColorHex = PickedHex });
        var day = created.Start.Date;
        AssertColor(window, title, day, "subito dopo Salva");

        // "Sincronizza ora", twice: the color must still be the chosen one after each, and Google must hold it too.
        await SyncAsync(window);
        AssertColor(window, title, day, "dopo la prima sincronizzazione");
        await SyncAsync(window);
        AssertColor(window, title, day, "dopo la seconda sincronizzazione");
        using (var google = await TestGoogle.ConnectAsync())
        {
            var remote = await google.GetAsync(TestGoogle.RemoteIdOf(created.Id) ?? throw new Xunit.Sdk.XunitException("L'evento non è stato inviato a Google."));
            Assert.Equal(PickedOnGoogle, remote.TryGetProperty("colorId", out var id) ? id.GetString() : null);
        }

        // The user closes the app and opens it again.
        window = await ReopenAsync(window);
        AssertColor(window, title, day, "dopo aver riaperto l'app");
    });

    private static void AssertColor(MainWindow window, string title, DateTime day, string when)
    {
        var seen = ColorSeenByTheUser(window, title, day);
        Assert.True(string.Equals(seen, PickedHex, StringComparison.OrdinalIgnoreCase),
            $"Colore dell'evento {when}: atteso {PickedHex}, visto {seen} (il colore del calendario è quello di default).");
    }

    /// <summary>
    /// The part of T01 that needs no data and no Google: the editor opened on its own, the color picked and Save pressed
    /// like a user would. It shows that the windows can be driven, and that Save hands back the picked color.
    /// </summary>
    [Fact]
    [Trait("TestCase", "T01")]
    public Task T01_TheEditorSavesTheColorPickedByTheUser() => HeadlessApp.RunAsync(async () =>
    {
        var owner = new Window();
        owner.Show();
        var calendar = new CalendarItemViewModel(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"), () => { });
        var start = new DateTime(2026, 10, 14, 9, 0, 0);
        var dialog = new EventEditWindow
        {
            DataContext = new EventEditViewModel(new CalendarEvent("e1", "c1", "", start, start.AddHours(1)), [calendar], true, new AppSettings(), new SoundService()),
        };
        var closed = dialog.ShowDialog<EventEditResult?>(owner);
        Dispatcher.UIThread.RunJobs();

        var editor = (EventEditViewModel)dialog.DataContext!;
        dialog.FindControl<TextBox>("TitleBox")!.Text = "Prova colore";
        var colors = dialog.GetVisualDescendants().OfType<ComboBox>().First(c => c.ItemsSource is IReadOnlyList<ColorOption>);
        colors.SelectedItem = editor.ColorOptions.First(c => string.Equals(c.Hex, PickedHex, StringComparison.OrdinalIgnoreCase));
        Click(FindButton(dialog, "Salva"));

        var result = await WaitForAsync(() => closed.IsCompleted ? closed.Result : null, "la chiusura dell'editor");
        Assert.Equal("Prova colore", result.Event!.Title);
        Assert.Equal(PickedHex, result.Event.ColorHex, ignoreCase: true);
        owner.Close();
    });
}
