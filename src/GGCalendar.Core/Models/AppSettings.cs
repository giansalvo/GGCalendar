namespace GGCalendar.Core.Models;

public sealed class AppSettings
{
    public int DayStartHour { get; set; } = 7;
    public int DayEndHour { get; set; } = 22;
    public bool HideOutsideHours { get; set; }
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;
    public bool ShowWeekNumbers { get; set; } = true;
    public bool Use24HourClock { get; set; } = true;
    public int DefaultEventMinutes { get; set; } = 60;
    public int SnapMinutes { get; set; } = 15;
    public string PrimaryTimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string? SecondaryTimeZoneId { get; set; }
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3.6:latest";
    public string? OllamaFallbackModel { get; set; } = "gemma4:latest";
}
