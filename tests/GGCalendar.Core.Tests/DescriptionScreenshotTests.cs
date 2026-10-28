using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GGCalendar.App.Views;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T273: draws the card of an event for every description of <see cref="DescriptionSamples"/> and saves the pictures (four cards
/// in each, light and dark theme) with a text file of what each card must show, to look at them. Nothing of the app or of Google is touched.
/// It draws for real, so it runs only with GGCALENDAR_TEST_SCREENSHOTS=1 (otherwise it does nothing):
/// <c>$env:GGCALENDAR_TEST_SCREENSHOTS="1"; dotnet test tests/GGCalendar.Core.Tests --filter "TestCase=T273"</c>.
/// </summary>
public class DescriptionScreenshotTests
{
    private const int PerSheet = 4;

    public static string Folder => Path.Combine(Path.GetTempPath(), "GGCalendar_T273");

    private static CalendarEvent Ev(DescriptionSample sample) =>
        new("e1", "c1", "[T273] " + sample.Name, DateTime.Today.AddHours(10), DateTime.Today.AddHours(11), Description: sample.Source);

    [Fact]
    [Trait("TestCase", "T273")]
    public Task DrawsTheCardOfEveryDescription() => HeadlessApp.RunAsync(async () =>
    {
        if (Environment.GetEnvironmentVariable("GGCALENDAR_TEST_SCREENSHOTS") != "1") return;
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.GetFiles(Folder)) File.Delete(old);

        var report = new StringBuilder();
        foreach (var sample in DescriptionSamples.All)
        {
            report.AppendLine($"=== [T273] {sample.Name}");
            report.AppendLine("Scritto nella descrizione:");
            report.AppendLine(sample.Source);
            report.AppendLine("La scheda deve mostrare:");
            report.AppendLine(sample.Expected);
            report.AppendLine("Aspetto: " + sample.Look);
            report.AppendLine();
        }

        File.WriteAllText(Path.Combine(Folder, "attese.txt"), report.ToString(), new UTF8Encoding(true));

        foreach (var dark in new[] { false, true })
        {
            for (var first = 0; first < DescriptionSamples.All.Count; first += PerSheet)
            {
                var panel = new WrapPanel { Width = 960 };
                foreach (var sample in DescriptionSamples.All.Skip(first).Take(PerSheet))
                {
                    var card = new EventDetailsCard(Ev(sample), null, "#1A73E8", "Calendario", "Account", use24Hour: true, dark: dark);
                    var cell = new StackPanel { Margin = new Thickness(10) };
                    cell.Children.Add(new TextBlock { Text = sample.Name, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4), Foreground = dark ? Brushes.White : Brushes.Black });
                    cell.Children.Add(card);
                    panel.Children.Add(cell);
                }

                var window = new Window
                {
                    Width = 960, SizeToContent = SizeToContent.Height, Content = panel,
                    Background = new SolidColorBrush(Color.Parse(dark ? "#1E1F20" : "#E8EAED")),
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                };
                window.Show();
                try
                {
                    await Task.Delay(300);
                    Dispatcher.UIThread.RunJobs();
                    var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(Folder, $"{(dark ? "scuro" : "chiaro")}_{first / PerSheet + 1:D2}.png"));
                }
                finally
                {
                    window.Close();
                }
            }
        }
    });

    /// <summary>The folder app_planning of the repository (found from the folder of the tests).</summary>
    private static string PlanningFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "GGCalendar.sln"))) return Path.Combine(dir.FullName, "app_planning");
        throw new DirectoryNotFoundException("GGCalendar.sln non trovato.");
    }

    /// <summary>
    /// T273: writes the descriptions of the samples in app_planning, to try them on Google Calendar: an .ics file to import in a
    /// test calendar (the descriptions are the code as it is) and a text file with the code of each one and what the app must show.
    /// Runs only with GGCALENDAR_TEST_EXPORT set, because it writes in the repository.
    /// </summary>
    [Fact]
    [Trait("TestCase", "T273")]
    public void ExportsTheDescriptionsForGoogleCalendar()
    {
        // GGCALENDAR_TEST_EXPORT=1 writes in app_planning; any other value is the folder to write in (when the tests run outside the repository).
        var export = Environment.GetEnvironmentVariable("GGCALENDAR_TEST_EXPORT");
        if (string.IsNullOrEmpty(export)) return;
        var folder = export == "1" ? PlanningFolder() : export;
        var first = new DateTime(2026, 11, 2, 6, 0, 0);
        var events = DescriptionSamples.All.Select((sample, i) =>
            new CalendarEvent($"t273-{i + 1:D2}", "c1", "[T273] " + sample.Name, first.AddMinutes(30 * i), first.AddMinutes(30 * i + 25), Description: sample.Source)).ToList();
        File.WriteAllText(Path.Combine(folder, "T273_descrizioni.ics"), IcsWriter.Write(events), new UTF8Encoding(false));

        var text = new StringBuilder();
        text.AppendLine($"T273: le {DescriptionSamples.All.Count} descrizioni di prova (codice HTML esattamente come è scritto nel campo descrizione).");
        text.AppendLine();
        text.AppendLine("Per provarle su Google Calendar: crea un calendario di prova, poi Impostazioni > Importa ed esporta > Importa e scegli");
        text.AppendLine($"T273_descrizioni.ics ({DescriptionSamples.All.Count} eventi dal 2/11/2026 alle 06:00, uno ogni 30 minuti, con titolo \"[T273] NN nome\").");
        text.AppendLine("Oppure incolla il codice di ogni caso (tra le righe CODICE e FINE) nella descrizione di un evento.");
        text.AppendLine("Gli a capo e i rientri tra CODICE e FINE fanno parte del codice; <TAB> è una tabulazione e <CRLF> un a capo di Windows.");
        text.AppendLine("In GGCalendar la scheda di ogni evento deve mostrare quanto scritto sotto \"GGCalendar mostra\".");
        text.AppendLine();
        foreach (var sample in DescriptionSamples.All)
        {
            text.AppendLine("=== [T273] " + sample.Name);
            text.AppendLine("----- CODICE -----");
            text.AppendLine(sample.Source.Replace("\t", "<TAB>").Replace("\r\n", "<CRLF>\n").Replace("\r", "<CR>"));
            text.AppendLine("----- FINE -----");
            text.AppendLine("GGCalendar mostra:");
            text.AppendLine(sample.Expected.Length > 400 ? sample.Expected[..400] + "… (descrizione lunga: " + sample.Expected.Length + " caratteri)" : sample.Expected);
            text.AppendLine("Aspetto: " + sample.Look);
            text.AppendLine();
        }

        File.WriteAllText(Path.Combine(folder, "T273_descrizioni.txt"), text.ToString(), new UTF8Encoding(true));
    }
}
