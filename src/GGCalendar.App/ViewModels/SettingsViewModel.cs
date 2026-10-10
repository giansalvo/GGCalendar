using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Ollama;
using GGCalendar.Providers.Google;

namespace GGCalendar.App.ViewModels;

public sealed record WeekdayOption(DayOfWeek Day, string Name);
public sealed record TimeZoneOption(string Id, string Name);

public sealed partial class SettingsViewModel : ViewModelBase
{
    private static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.GetCultureInfo("it-IT");

    public SettingsViewModel(AppSettings s, SoundService? sound = null, GoogleSyncService? google = null)
    {
        _sound = sound;
        _google = google;
        GoogleClientId = s.GoogleClientId ?? "";
        GoogleClientSecret = s.GoogleClientSecret ?? "";
        SyncIntervalIndex = Math.Max(0, Array.IndexOf(SyncIntervalOptions, s.SyncIntervalMinutes));
        if (google is not null) foreach (var a in google.AccountEmails) GoogleAccounts.Add(a);
        NotificationsEnabled = s.NotificationsEnabled;
        ReminderVolumePercent = s.ReminderVolumePercent;
        if (Directory.Exists(SoundService.MediaFolder))
        {
            foreach (var f in Directory.EnumerateFiles(SoundService.MediaFolder, "*.wav").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                Sounds.Add(new SoundOption("Windows: " + Path.GetFileNameWithoutExtension(f), f));
        }

        DefaultSound = Sounds.FirstOrDefault(o => o.Path == s.DefaultReminderSound) ?? AddCustomSound(s.DefaultReminderSound);
        DayStartHour = s.DayStartHour;
        DayEndHour = s.DayEndHour;
        HideOutsideHours = s.HideOutsideHours;
        ShowWeekNumbers = s.ShowWeekNumbers;
        Use24HourClock = s.Use24HourClock;
        DefaultEventMinutes = s.DefaultEventMinutes;
        SnapMinutes = s.SnapMinutes;
        OllamaUrl = s.OllamaUrl;
        ThemeIndex = Math.Clamp(s.Theme, 0, 2);
        DimPastEvents = s.DimPastEvents;
        DefaultGuestsCanModify = s.DefaultGuestsCanModify;
        DefaultGuestsCanInvite = s.DefaultGuestsCanInvite;
        DefaultGuestsCanSeeList = s.DefaultGuestsCanSeeList;

        Weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Sunday, DayOfWeek.Saturday }
            .Select(d => new WeekdayOption(d, Culture.TextInfo.ToTitleCase(Culture.DateTimeFormat.GetDayName(d)))).ToList();
        FirstDayOfWeek = Weekdays.First(w => w.Day == s.FirstDayOfWeek);

        TimeZones = TimeZoneInfo.GetSystemTimeZones().Select(z => new TimeZoneOption(z.Id, z.DisplayName)).ToList();
        PrimaryTimeZone = FindZone(s.PrimaryTimeZoneId) ?? FindZone(TimeZoneInfo.Local.Id);
        SecondaryTimeZone = s.SecondaryTimeZoneId is null ? NoSecondaryZone : FindZone(s.SecondaryTimeZoneId) ?? NoSecondaryZone;
        SecondaryTimeZones = [NoSecondaryZone, .. TimeZones];

        foreach (var m in new[] { s.OllamaModel, s.OllamaFallbackModel }.Where(m => !string.IsNullOrEmpty(m)).Distinct())
            Models.Add(m!);
        OllamaModel = s.OllamaModel;
        OllamaFallbackModel = s.OllamaFallbackModel ?? NoFallback;
        Models.Add(NoFallback);
    }

    private readonly SoundService? _sound;
    private readonly GoogleSyncService? _google;
    private CancellationTokenSource? _signIn;

    public ObservableCollection<string> GoogleAccounts { get; } = [];
    [ObservableProperty] public partial string GoogleClientId { get; set; }
    [ObservableProperty] public partial string GoogleClientSecret { get; set; }

    public static readonly int[] SyncIntervalOptions = [0, 1, 2, 5, 10, 15, 30, 60];
    public string[] SyncIntervalLabels { get; } =
        ["Mai (solo manuale)", "Ogni minuto", "Ogni 2 minuti", "Ogni 5 minuti", "Ogni 10 minuti", "Ogni 15 minuti", "Ogni 30 minuti", "Ogni 60 minuti"];
    [ObservableProperty] public partial int SyncIntervalIndex { get; set; }
    [ObservableProperty] public partial string GoogleStatus { get; set; } = "";
    [ObservableProperty] public partial bool IsSigningIn { get; set; }

    public bool AccountsChanged { get; private set; }

    [RelayCommand]
    private async Task AddGoogleAccountAsync()
    {
        if (_google is null) return;
        _google.Configure(GoogleClientConfig.Resolve(GoogleClientId, GoogleClientSecret));
        if (!_google.IsConfigured)
        {
            GoogleStatus = "Credenziali Google non ancora configurate: inseriscile in \"Avanzate\".";
            return;
        }

        IsSigningIn = true;
        GoogleStatus = "Completa l'accesso nel browser...";
        _signIn = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var email = await _google.AddAccountAsync(_signIn.Token);
            if (!GoogleAccounts.Contains(email)) GoogleAccounts.Add(email);
            AccountsChanged = true;
            GoogleStatus = $"Account {email} collegato.";
        }
        catch (Exception ex) when (ex is GoogleAuthException or System.Net.Http.HttpRequestException or OperationCanceledException)
        {
            GoogleStatus = ex is OperationCanceledException ? "Accesso annullato o scaduto." : ex.Message;
        }
        finally
        {
            IsSigningIn = false;
            _signIn.Dispose();
            _signIn = null;
        }
    }

    [RelayCommand]
    private void CancelSignIn() => _signIn?.Cancel();

    [RelayCommand]
    private void RemoveGoogleAccount(string? email)
    {
        if (_google is null || email is null) return;
        _google.RemoveAccount(email);
        GoogleAccounts.Remove(email);
        AccountsChanged = true;
        GoogleStatus = $"Account {email} scollegato.";
    }

    public ObservableCollection<SoundOption> Sounds { get; } = [];

    [ObservableProperty] public partial bool NotificationsEnabled { get; set; }
    [ObservableProperty] public partial int ReminderVolumePercent { get; set; }
    [ObservableProperty] public partial SoundOption DefaultSound { get; set; }

    public SoundOption AddCustomSound(string path)
    {
        var existing = Sounds.FirstOrDefault(o => o.Path == path);
        if (existing is not null) return existing;
        var option = new SoundOption("File: " + Path.GetFileName(path), path);
        Sounds.Add(option);
        return option;
    }

    [RelayCommand]
    private void PreviewDefaultSound() => _sound?.Play(DefaultSound.Path, ReminderVolumePercent);

    public void StopSound() => _sound?.Stop();

    public const string NoFallback = "(nessuno)";
    public static TimeZoneOption NoSecondaryZone { get; } = new("", "(nessun fuso secondario)");

    public IReadOnlyList<WeekdayOption> Weekdays { get; }
    public IReadOnlyList<TimeZoneOption> TimeZones { get; }
    public IReadOnlyList<TimeZoneOption> SecondaryTimeZones { get; }
    public ObservableCollection<string> Models { get; } = [];
    public IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 25).ToList();
    public IReadOnlyList<int> SnapOptions { get; } = [5, 10, 15, 30, 60];

    [ObservableProperty] public partial int DayStartHour { get; set; }
    [ObservableProperty] public partial int DayEndHour { get; set; }
    [ObservableProperty] public partial bool HideOutsideHours { get; set; }
    [ObservableProperty] public partial bool ShowWeekNumbers { get; set; }
    [ObservableProperty] public partial bool Use24HourClock { get; set; }
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial bool DimPastEvents { get; set; }
    [ObservableProperty] public partial bool DefaultGuestsCanModify { get; set; }
    [ObservableProperty] public partial bool DefaultGuestsCanInvite { get; set; }
    [ObservableProperty] public partial bool DefaultGuestsCanSeeList { get; set; }
    [ObservableProperty] public partial int DefaultEventMinutes { get; set; }
    [ObservableProperty] public partial int SnapMinutes { get; set; }
    [ObservableProperty] public partial WeekdayOption FirstDayOfWeek { get; set; }
    [ObservableProperty] public partial TimeZoneOption? PrimaryTimeZone { get; set; }
    [ObservableProperty] public partial TimeZoneOption SecondaryTimeZone { get; set; }
    [ObservableProperty] public partial string OllamaUrl { get; set; }
    [ObservableProperty] public partial string? OllamaModel { get; set; }
    [ObservableProperty] public partial string OllamaFallbackModel { get; set; }
    [ObservableProperty] public partial string OllamaStatus { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";

    private TimeZoneOption? FindZone(string id) => TimeZones.FirstOrDefault(z => z.Id == id);

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        OllamaStatus = "Connessione in corso...";
        try
        {
            var found = await new OllamaClient(OllamaUrl).ListModelsAsync();
            var model = OllamaModel;
            var fallback = OllamaFallbackModel;
            Models.Clear();
            foreach (var m in found) Models.Add(m);
            Models.Add(NoFallback);
            OllamaModel = model is not null && Models.Contains(model) ? model : found.FirstOrDefault();
            OllamaFallbackModel = Models.Contains(fallback) ? fallback : NoFallback;
            OllamaStatus = $"Connesso: {found.Count} modelli disponibili.";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or UriFormatException or System.Text.Json.JsonException)
        {
            OllamaStatus = "Ollama non raggiungibile: " + ex.Message;
        }
    }

    public AppSettings? TryBuild()
    {
        Error = "";
        if (DayEndHour <= DayStartHour) { Error = "La fine della giornata deve essere dopo l'inizio."; return null; }
        if (PrimaryTimeZone is null) { Error = "Seleziona il fuso orario principale."; return null; }
        if (string.IsNullOrWhiteSpace(OllamaModel)) { Error = "Seleziona un modello Ollama."; return null; }

        return new AppSettings
        {
            DayStartHour = DayStartHour,
            DayEndHour = DayEndHour,
            HideOutsideHours = HideOutsideHours,
            FirstDayOfWeek = FirstDayOfWeek.Day,
            ShowWeekNumbers = ShowWeekNumbers,
            Use24HourClock = Use24HourClock,
            Theme = ThemeIndex,
            DimPastEvents = DimPastEvents,
            DefaultGuestsCanModify = DefaultGuestsCanModify,
            DefaultGuestsCanInvite = DefaultGuestsCanInvite,
            DefaultGuestsCanSeeList = DefaultGuestsCanSeeList,
            SyncIntervalMinutes = SyncIntervalOptions[Math.Clamp(SyncIntervalIndex, 0, SyncIntervalOptions.Length - 1)],
            GoogleClientId = string.IsNullOrWhiteSpace(GoogleClientId) ? null : GoogleClientId.Trim(),
            GoogleClientSecret = string.IsNullOrWhiteSpace(GoogleClientSecret) ? null : GoogleClientSecret.Trim(),
            DefaultEventMinutes = DefaultEventMinutes,
            NotificationsEnabled = NotificationsEnabled,
            ReminderVolumePercent = ReminderVolumePercent,
            DefaultReminderSound = DefaultSound.Path ?? new AppSettings().DefaultReminderSound,
            SnapMinutes = SnapMinutes,
            PrimaryTimeZoneId = PrimaryTimeZone.Id,
            SecondaryTimeZoneId = string.IsNullOrEmpty(SecondaryTimeZone.Id) || SecondaryTimeZone.Id == PrimaryTimeZone.Id ? null : SecondaryTimeZone.Id,
            OllamaUrl = OllamaUrl.Trim(),
            OllamaModel = OllamaModel!,
            OllamaFallbackModel = OllamaFallbackModel == NoFallback ? null : OllamaFallbackModel,
        };
    }
}
