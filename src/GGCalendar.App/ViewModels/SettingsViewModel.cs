using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGCalendar.Core.Models;
using GGCalendar.Core.Ollama;

namespace GGCalendar.App.ViewModels;

public sealed record WeekdayOption(DayOfWeek Day, string Name);
public sealed record TimeZoneOption(string Id, string Name);

public sealed partial class SettingsViewModel : ViewModelBase
{
    private static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.GetCultureInfo("it-IT");

    public SettingsViewModel(AppSettings s)
    {
        DayStartHour = s.DayStartHour;
        DayEndHour = s.DayEndHour;
        HideOutsideHours = s.HideOutsideHours;
        ShowWeekNumbers = s.ShowWeekNumbers;
        Use24HourClock = s.Use24HourClock;
        DefaultEventMinutes = s.DefaultEventMinutes;
        SnapMinutes = s.SnapMinutes;
        OllamaUrl = s.OllamaUrl;

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
            DefaultEventMinutes = DefaultEventMinutes,
            SnapMinutes = SnapMinutes,
            PrimaryTimeZoneId = PrimaryTimeZone.Id,
            SecondaryTimeZoneId = string.IsNullOrEmpty(SecondaryTimeZone.Id) || SecondaryTimeZone.Id == PrimaryTimeZone.Id ? null : SecondaryTimeZone.Id,
            OllamaUrl = OllamaUrl.Trim(),
            OllamaModel = OllamaModel!,
            OllamaFallbackModel = OllamaFallbackModel == NoFallback ? null : OllamaFallbackModel,
        };
    }
}
