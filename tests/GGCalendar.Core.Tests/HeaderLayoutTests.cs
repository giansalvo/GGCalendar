using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>T180: the buttons of the bar above the calendar show their texts whole, in every language, whatever the view, at the narrowest window.</summary>
[Collection(CollectionName)]
public class HeaderLayoutTests
{
    [Theory]
    [Trait("TestCase", "T180")]
    [InlineData("it", false)]
    [InlineData("it", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    [InlineData("fr", true)]
    [InlineData("de", false)]
    [InlineData("de", true)]
    [InlineData("es", false)]
    [InlineData("es", true)]
    public Task T180_TheViewButtonShowsItsTextWholeAtTheNarrowestWindow(string language, bool searchOpen) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        var before = L.Language;
        try
        {
            L.Apply(language);
            Dispatcher.UIThread.RunJobs(); // the texts bound to the language are written
            window.Width = window.MinWidth;
            window.Height = 700;
            var vm = Vm(window);
            var failures = new List<string>();
            for (var view = 0; view < 6; view++)
            {
                vm.ViewModeIndex = view;
                // The title of the view is the longest in some views: a week, a day with its month name.
                vm.SelectedDate = new DateTime(2026, 9, 28);
                vm.Search.IsOpen = searchOpen; // the box of the search is in the bar: the buttons must stay in the window with it too (choosing a view closes it)
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var button = FindButton(window, "Main.ViewMode");
                var text = button.GetVisualDescendants().OfType<TextBlock>().First();
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), window)!.Value.X;
                if (text.Bounds.Width + 0.5 < text.DesiredSize.Width) failures.Add($"view {view}: text {text.Bounds.Width:F1} of {text.DesiredSize.Width:F1}");
                if (right > window.ClientSize.Width + 0.5) failures.Add($"view {view}: the button ends at {right:F0}, the window at {window.ClientSize.Width:F0}");
                foreach (var id in new[] { "Main.Search", "Main.SyncNow", "Main.Settings" })
                {
                    var b = FindButton(window, id);
                    var edge = b.TranslatePoint(new Point(b.Bounds.Width, 0), window)!.Value.X;
                    if (edge > window.ClientSize.Width + 0.5) failures.Add($"view {view}: {id} out of the window");
                }
            }

            Assert.Empty(failures);
            await Task.CompletedTask;
        }
        finally
        {
            L.Apply(before);
            window.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });
}
