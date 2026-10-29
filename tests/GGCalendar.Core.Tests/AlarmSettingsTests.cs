using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T326-T329: how long an alarm rings and which sound it plays, chosen in the settings (issue #32): the limits of the time, the sound of the alarms apart from
/// the one of the popups, the checks on the file the user chooses and the sound of Windows used when a file is gone. T330 (the alarm that rings) is in AlarmsUiTests.
/// </summary>
[Collection(CollectionName)]
public class AlarmSettingsTests
{
    // ---- T326: what is stored ----

    [Fact]
    [Trait("TestCase", "T326")]
    public void AnAlarmRingsTwoMinutesAndHasItsOwnSoundUnlessChosenOtherwise()
    {
        var settings = new AppSettings();
        Assert.Equal(2, settings.AlarmDurationMinutes);
        Assert.Equal(TimeSpan.FromMinutes(2), settings.AlarmDuration);
        Assert.EndsWith("Alarm01.wav", settings.DefaultAlarmSound);
        Assert.NotEqual(settings.DefaultReminderSound, settings.DefaultAlarmSound);

        // a settings file written before the options existed has the same values
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":1}")!;
        Assert.Equal(2, old.AlarmDurationMinutes);
        Assert.Equal(settings.DefaultAlarmSound, old.DefaultAlarmSound);
    }

    [Theory]
    [Trait("TestCase", "T326")]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(7, 7)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(600, 10)]
    public void TheTimeStaysBetweenOneAndTenMinutes(int stored, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), new AppSettings { AlarmDurationMinutes = stored }.AlarmDuration);

    [Fact]
    [Trait("TestCase", "T326")]
    public void TheChoicesAreKeptBetweenStarts()
    {
        var file = Path.Combine(Path.GetTempPath(), "ggcal-alarm-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(file);
            var settings = store.Load();
            settings.AlarmDurationMinutes = 7;
            settings.DefaultAlarmSound = @"C:\Windows\Media\Alarm05.wav";
            store.Save(settings);

            var again = new SettingsStore(file).Load();
            Assert.Equal(7, again.AlarmDurationMinutes);
            Assert.Equal(@"C:\Windows\Media\Alarm05.wav", again.DefaultAlarmSound);
            Assert.DoesNotContain("AlarmDuration\"", File.ReadAllText(file)); // only the minutes are written, not the time computed from them
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- T327: which sound a reminder plays ----

    private static readonly AppSettings Defaults = new() { DefaultReminderSound = "popup.wav", DefaultAlarmSound = "alarm.wav" };

    [Theory]
    [Trait("TestCase", "T327")]
    [InlineData(ReminderMethod.Popup, null, "popup.wav")]
    [InlineData(ReminderMethod.Alarm, null, "alarm.wav")]
    [InlineData(ReminderMethod.Popup, EventReminder.NoSound, null)]
    [InlineData(ReminderMethod.Alarm, EventReminder.NoSound, "alarm.wav")] // an alarm cannot be silent
    [InlineData(ReminderMethod.Popup, "mio.mp3", "mio.mp3")]
    [InlineData(ReminderMethod.Alarm, "mio.mp3", "mio.mp3")]
    public void TheSoundOfTheReminderOrTheDefaultOfItsKind(ReminderMethod method, string? own, string? expected) =>
        Assert.Equal(expected, ReminderSounds.Choose(new EventReminder(method, 10, own), Defaults));

    [Fact]
    [Trait("TestCase", "T327")]
    public void AFileThatIsGoneIsReplacedByTheSoundOfWindowsOfItsKind()
    {
        Func<string, bool> all = _ => true;
        Func<string, bool> onlyWindows = p => p == ReminderSounds.SystemAlarmSound || p == ReminderSounds.SystemReminderSound;
        Func<string, bool> nothing = _ => false;

        Assert.Equal("mio.mp3", ReminderSounds.Playable("mio.mp3", isAlarm: true, all));
        Assert.Equal(ReminderSounds.SystemAlarmSound, ReminderSounds.Playable("sparito.mp3", isAlarm: true, onlyWindows));
        Assert.Equal(ReminderSounds.SystemReminderSound, ReminderSounds.Playable("sparito.mp3", isAlarm: false, onlyWindows));
        Assert.Null(ReminderSounds.Playable("sparito.mp3", isAlarm: true, nothing)); // not even the sound of Windows: silence, no error
        Assert.Null(ReminderSounds.Playable(null, isAlarm: true, all)); // no sound wanted
        Assert.Null(ReminderSounds.Playable("  ", isAlarm: false, all));
        Assert.EndsWith("Alarm01.wav", ReminderSounds.SystemAlarmSound);
        Assert.EndsWith("Windows Notify Calendar.wav", ReminderSounds.SystemReminderSound);
    }

    // ---- T328: the file the user chooses ----

    /// <summary>A .wav of silence that lasts <paramref name="seconds"/> (8 bit, mono, 8000 samples a second).</summary>
    private static string Wav(string name, double seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), "ggcal-" + Guid.NewGuid().ToString("N")[..8] + "-" + name);
        var samples = (int)(seconds * 8000);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + samples);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(8000);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write("data"u8.ToArray());
        writer.Write(samples);
        writer.Write(Enumerable.Repeat((byte)128, samples).ToArray());
        return path;
    }

    private static string FileWith(string name, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), "ggcal-" + Guid.NewGuid().ToString("N")[..8] + "-" + name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    [Trait("TestCase", "T328")]
    public void AGoodFileIsAccepted()
    {
        var wav = Wav("buono.wav", 1);
        try
        {
            Assert.Equal(SoundFileProblem.None, SoundFileCheck.Check(wav));
            Assert.Equal(SoundFileProblem.None, SoundFileCheck.Check(Path.Combine(SoundService.MediaFolder, "Alarm01.wav"))); // the sounds of Windows too
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [Fact]
    [Trait("TestCase", "T328")]
    public void AMissingFileAnotherKindOfFileAndOneThatIsNotAudioAreRefused()
    {
        var wav = Wav("suono.wav", 1);
        var renamed = Path.ChangeExtension(wav, ".ogg");
        File.Copy(wav, renamed);
        var junk = FileWith("rotto.mp3", [1, 2, 3, 4, 5, 6, 7, 8]);
        var empty = FileWith("vuoto.wav", []);
        var noAudio = Wav("zero.wav", 0); // a valid header, nothing to play
        try
        {
            Assert.Equal(SoundFileProblem.Missing, SoundFileCheck.Check(null));
            Assert.Equal(SoundFileProblem.Missing, SoundFileCheck.Check(""));
            Assert.Equal(SoundFileProblem.Missing, SoundFileCheck.Check(Path.Combine(Path.GetTempPath(), "non-esiste-" + Guid.NewGuid().ToString("N") + ".mp3")));
            Assert.Equal(SoundFileProblem.NotAudio, SoundFileCheck.Check(renamed)); // only mp3 and wav
            Assert.Equal(SoundFileProblem.NotAudio, SoundFileCheck.Check(junk));
            Assert.Equal(SoundFileProblem.NotAudio, SoundFileCheck.Check(empty));
            Assert.Equal(SoundFileProblem.NotAudio, SoundFileCheck.Check(noAudio));
        }
        finally
        {
            foreach (var f in new[] { wav, renamed, junk, empty, noAudio }) File.Delete(f);
        }
    }

    [Fact]
    [Trait("TestCase", "T328")]
    public void AFileOverTenMegabytesIsRefusedAndOneAtTheLimitIsNot()
    {
        var big = FileWith("grande.wav", new byte[ReminderSounds.MaxFileBytes + 1]);
        try
        {
            Assert.Equal(10L * 1024 * 1024, ReminderSounds.MaxFileBytes);
            Assert.Equal(SoundFileProblem.TooBig, SoundFileCheck.Check(big));
        }
        finally
        {
            File.Delete(big);
        }

        // exactly 10 MB of a valid file: accepted (the limit is "no more than")
        var path = Wav("limite.wav", 0.5);
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            {
                stream.SetLength(ReminderSounds.MaxFileBytes); // the rest is silence the header does not count: the file still opens
            }

            Assert.NotEqual(SoundFileProblem.TooBig, SoundFileCheck.Check(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- T329: the settings window ----

    [Fact]
    [Trait("TestCase", "T329")]
    public void TheModelOfTheSettingsHasTheValuesAndKeepsTheLimits()
    {
        var vm = new SettingsViewModel(new AppSettings());
        Assert.Equal(2, vm.AlarmDurationMinutes);
        Assert.Equal((1, 10), (vm.MinAlarmMinutes, vm.MaxAlarmMinutes));
        Assert.EndsWith("Alarm01.wav", vm.AlarmSound.Path);
        Assert.EndsWith("Windows Notify Calendar.wav", vm.DefaultSound.Path);

        vm.AlarmDurationMinutes = 6;
        vm.AlarmSound = vm.Sounds.First(s => s.Path!.EndsWith("Alarm05.wav", StringComparison.OrdinalIgnoreCase));
        var built = vm.TryBuild()!;
        Assert.Equal(6, built.AlarmDurationMinutes);
        Assert.EndsWith("Alarm05.wav", built.DefaultAlarmSound);
        Assert.EndsWith("Windows Notify Calendar.wav", built.DefaultReminderSound); // the sound of the popups did not change

        vm.AlarmDurationMinutes = 99; // never beyond the limits, whatever put it there
        Assert.Equal(10, vm.TryBuild()!.AlarmDurationMinutes);
        vm.AlarmDurationMinutes = -3;
        Assert.Equal(1, vm.TryBuild()!.AlarmDurationMinutes);
    }

    [Fact]
    [Trait("TestCase", "T329")]
    public Task TheLabelNextToTheSliderSaysTheMinutesInTheLanguageOfTheApp() => HeadlessApp.RunAsync(() =>
    {
        var before = L.Language;
        try
        {
            L.Apply("it");
            var vm = new SettingsViewModel(new AppSettings());
            Assert.Equal("2 minuti", vm.AlarmDurationLabel);
            vm.AlarmDurationMinutes = 1;
            Assert.Equal("1 minuto", vm.AlarmDurationLabel);
            L.Apply("en");
            Assert.Equal("1 minute", vm.AlarmDurationLabel);
            vm.AlarmDurationMinutes = 10;
            Assert.Equal("10 minutes", vm.AlarmDurationLabel);
        }
        finally
        {
            L.Apply(before);
        }

        return Task.CompletedTask;
    });

    private static async Task<SettingsWindow> OpenSettingsAsync(MainWindow window)
    {
        Click(FindButton(window, "Main.Settings"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "la finestra delle impostazioni");
    }

    [Fact]
    [Trait("TestCase", "T329")]
    public Task TheWindowHasTheSoundAndTheTimeOfTheAlarmsAndSavingKeepsThem() => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        try
        {
            // Cancel: nothing changes.
            var dialog = await OpenSettingsAsync(window);
            var sound = (ComboBox)FindById(dialog, "Settings.AlarmSound");
            var duration = (Slider)FindById(dialog, "Settings.AlarmDuration");
            Assert.Equal((1d, 10d), (duration.Minimum, duration.Maximum));
            Assert.Equal(2d, duration.Value);
            Assert.EndsWith("Alarm01.wav", ((SoundOption)sound.SelectedItem!).Path);
            duration.Value = 9;
            Click(FindButton(dialog, "Settings.Cancel"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
            Assert.Equal(2, new SettingsStore().Load().AlarmDurationMinutes);

            // Save: the time and the sound are kept, and the app uses them at once.
            dialog = await OpenSettingsAsync(window);
            duration = (Slider)FindById(dialog, "Settings.AlarmDuration");
            sound = (ComboBox)FindById(dialog, "Settings.AlarmSound");
            duration.Value = 5;
            sound.SelectedItem = ((IEnumerable<SoundOption>)sound.ItemsSource!).First(s => s.Path!.EndsWith("Alarm03.wav", StringComparison.OrdinalIgnoreCase));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("5 " + (L.Language == "it" ? "minuti" : "minutes"), ((TextBlock)FindById(dialog, "Settings.AlarmDurationLabel")).Text);
            Click(FindButton(dialog, "Settings.Save"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");

            var stored = new SettingsStore().Load();
            Assert.Equal(5, stored.AlarmDurationMinutes);
            Assert.EndsWith("Alarm03.wav", stored.DefaultAlarmSound);
            Assert.EndsWith("Windows Notify Calendar.wav", stored.DefaultReminderSound);
            Assert.Equal(TimeSpan.FromMinutes(5), ((MainViewModel)window.DataContext!).Settings.AlarmDuration);
        }
        finally
        {
            window.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    [Fact]
    [Trait("TestCase", "T329")]
    public Task TheEventEditorPreviewsTheAlarmSoundForAnAlarmAndTheOtherForAPopup() => HeadlessApp.RunAsync(() =>
    {
        // "Default sound" of a reminder means the default of its kind: this is what the editor lets the user hear.
        var settings = new AppSettings { DefaultReminderSound = "popup.wav", DefaultAlarmSound = "alarm.wav" };
        var ev = new CalendarEvent("e1", "c1", "Prova", new DateTime(2026, 11, 2, 10, 0, 0), new DateTime(2026, 11, 2, 11, 0, 0))
        {
            Reminders = [new EventReminder(ReminderMethod.Popup, 10), new EventReminder(ReminderMethod.Alarm, 10)],
        };
        var vm = new EventEditViewModel(ev, [new CalendarItemViewModel(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"), () => { })], false, settings);
        Assert.Equal(["popup.wav", "alarm.wav"], vm.Reminders.Select(vm.PreviewPathOf).ToArray());
        return Task.CompletedTask;
    });
}
