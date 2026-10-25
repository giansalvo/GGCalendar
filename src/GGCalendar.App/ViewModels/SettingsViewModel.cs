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
using GGCalendar.Core.Zones;
using GGCalendar.Core.Ollama;
using GGCalendar.Providers.Google;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.ViewModels;

public sealed record WeekdayOption(DayOfWeek Day, string Name);
public sealed record TimeZoneOption(string Id, string Name);
public sealed record LanguageOption(string Code, string Name);

public sealed partial class SettingsViewModel : ViewModelBase
{
    private static System.Globalization.CultureInfo Culture => L.Culture;

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
                Sounds.Add(new SoundOption(L.T("sound.windows", Path.GetFileNameWithoutExtension(f)), f));
        }

        DefaultSound = Sounds.FirstOrDefault(o => o.Path == s.DefaultReminderSound) ?? AddCustomSound(s.DefaultReminderSound);
        DayStartHour = s.DayStartHour;
        DayEndHour = s.DayEndHour;
        HideOutsideHours = s.HideOutsideHours;
        ShowWeekNumbers = s.ShowWeekNumbers;
        Use24HourClock = s.Use24HourClock;
        LanguageOptions = [new("system", L.T("settings.language_system")), .. L.Languages.Select(l => new LanguageOption(l.Code, l.NativeName))];
        SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == s.Language) ?? LanguageOptions[0];
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
        SecondaryTimeZone = s.SecondaryTimeZoneId is null ? NoSecondaryZone : FindZone(s.SecondaryTimeZoneId) ?? NoSecondaryZone;
        SecondaryTimeZones = [NoSecondaryZone, .. TimeZones];
        SecondaryTimeZoneLabel = s.SecondaryTimeZoneLabel ?? "";

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
        [L.T("settings.sync_never"), .. SyncIntervalOptions.Skip(1).Select(m => L.N("settings.sync_every", m))];
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
            GoogleStatus = L.T("settings.credentials_missing");
            return;
        }

        IsSigningIn = true;
        GoogleStatus = L.T("settings.complete_sign_in");
        _signIn = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var email = await _google.AddAccountAsync(_signIn.Token);
            if (!GoogleAccounts.Contains(email)) GoogleAccounts.Add(email);
            AccountsChanged = true;
            GoogleStatus = L.T("settings.account_connected", email);
        }
        catch (Exception ex) when (ex is GoogleAuthException or System.Net.Http.HttpRequestException or OperationCanceledException)
        {
            GoogleStatus = ex is OperationCanceledException ? L.T("settings.sign_in_cancelled") : ex.Message;
        }
        finally
        {
            IsSigningIn = false;
            _signIn.Dispose();
            _signIn = null;
        }
    }

    /// <summary>Clears the sync tokens; the main window runs a full sync when the dialog closes.</summary>
    public void ResyncAll()
    {
        if (_google is null) return;
        _google.ResetSync();
        AccountsChanged = true;
        GoogleStatus = L.T("settings.resync_scheduled");
    }

    [RelayCommand]
    private void CancelSignIn() => _signIn?.Cancel();

    [RelayCommand]
    private async Task RemoveGoogleAccount(string? email)
    {
        if (_google is null || email is null) return;
        GoogleStatus = L.T("settings.disconnecting", email);
        var revoked = await _google.RemoveAccountAsync(email, CancellationToken.None);
        GoogleAccounts.Remove(email);
        AccountsChanged = true;
        GoogleStatus = revoked
            ? L.T("settings.disconnected_revoked", email)
            : L.T("settings.disconnected_not_revoked", email);
    }

    public ObservableCollection<SoundOption> Sounds { get; } = [];

    [ObservableProperty] public partial bool NotificationsEnabled { get; set; }
    [ObservableProperty] public partial int ReminderVolumePercent { get; set; }
    [ObservableProperty] public partial SoundOption DefaultSound { get; set; }

    public SoundOption AddCustomSound(string path)
    {
        var existing = Sounds.FirstOrDefault(o => o.Path == path);
        if (existing is not null) return existing;
        var option = new SoundOption(L.T("sound.file", Path.GetFileName(path)), path);
        Sounds.Add(option);
        return option;
    }

    [RelayCommand]
    private void PreviewDefaultSound() => _sound?.Play(DefaultSound.Path, ReminderVolumePercent);

    public void StopSound() => _sound?.Stop();

    public static string NoFallback => L.T("settings.none");
    public static TimeZoneOption NoSecondaryZone { get; } = new("", L.T("settings.no_secondary_zone"));

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }
    [ObservableProperty] public partial LanguageOption SelectedLanguage { get; set; }

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecondaryZone), nameof(SecondaryLabelHint), nameof(SecondaryLabelPlaceholder))]
    public partial TimeZoneOption SecondaryTimeZone { get; set; }

    /// <summary>What the user typed as the name of the second column (up to <see cref="SecondZones.MaxLabel"/> characters).</summary>
    [ObservableProperty] public partial string SecondaryTimeZoneLabel { get; set; }

    public bool HasSecondaryZone => !string.IsNullOrEmpty(SecondaryTimeZone.Id);

    /// <summary>The name the column has when the box is left empty.</summary>
    public string SecondaryLabelPlaceholder => HasSecondaryZone ? SecondZones.DefaultLabel(SecondaryTimeZone.Id) : "";

    public string SecondaryLabelHint => L.T("settings.second_zone_label_hint", SecondZones.MaxLabel, SecondaryLabelPlaceholder);

    // Only the length is cut while typing: trimming would eat the space between two words.
    partial void OnSecondaryTimeZoneLabelChanged(string value)
    {
        var cut = string.Concat(value.EnumerateRunes().Take(SecondZones.MaxLabel).Select(r => r.ToString()));
        if (cut.Length < value.Length) SecondaryTimeZoneLabel = cut;
    }
    [ObservableProperty] public partial string OllamaUrl { get; set; }
    [ObservableProperty] public partial string? OllamaModel { get; set; }
    [ObservableProperty] public partial string OllamaFallbackModel { get; set; }
    [ObservableProperty] public partial string OllamaStatus { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";

    private TimeZoneOption? FindZone(string id) => TimeZones.FirstOrDefault(z => z.Id == id);

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        OllamaStatus = L.T("settings.ollama_connecting");
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
            OllamaStatus = L.N("settings.ollama_connected", found.Count);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or UriFormatException or System.Text.Json.JsonException)
        {
            OllamaStatus = L.T("settings.ollama_unreachable", ex.Message);
        }
    }

    public AppSettings? TryBuild()
    {
        Error = "";
        if (DayEndHour <= DayStartHour) { Error = L.T("settings.error_day_end"); return null; }
        // Ollama is optional: without a model (not installed, or no models) the other settings must still save.

        return new AppSettings
        {
            DayStartHour = DayStartHour,
            DayEndHour = DayEndHour,
            HideOutsideHours = HideOutsideHours,
            FirstDayOfWeek = FirstDayOfWeek.Day,
            ShowWeekNumbers = ShowWeekNumbers,
            Use24HourClock = Use24HourClock,
            Language = SelectedLanguage.Code,
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
            SecondaryTimeZoneId = string.IsNullOrEmpty(SecondaryTimeZone.Id) || SecondaryTimeZone.Id == TimeZoneInfo.Local.Id ? null : SecondaryTimeZone.Id,
            SecondaryTimeZoneLabel = SecondZones.ClampLabel(SecondaryTimeZoneLabel) is { Length: > 0 } label ? label : null,
            OllamaUrl = OllamaUrl.Trim(),
            OllamaModel = OllamaModel ?? "",
            OllamaFallbackModel = OllamaFallbackModel == NoFallback ? null : OllamaFallbackModel,
        };
    }
}
