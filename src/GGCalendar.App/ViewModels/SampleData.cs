using System;
using System.Collections.Generic;
using GGCalendar.Core.Models;

namespace GGCalendar.App.ViewModels;

/// <summary>Placeholder data used until the Google provider is wired in.</summary>
internal static class SampleData
{
    public static IReadOnlyList<CalendarInfo> Calendars() =>
    [
        new("work", "mario@gmail.com", "Lavoro", "#D50000"),
        new("personal", "mario@gmail.com", "Personale", "#33B679"),
        new("school", "scuola@example.com", "Scuola", "#3F51B5"),
        new("hobby", "scuola@example.com", "Hobby", "#8E24AA"),
    ];

    public static List<CalendarEvent> Events(DateTime monday)
    {
        CalendarEvent E(int day, double h1, double h2, string title, string cal) =>
            new($"{title}-{day}", cal, title, monday.AddDays(day).AddHours(h1), monday.AddDays(day).AddHours(h2));

        return
        [
            E(0, 8, 9, "Sopralluogo", "work"),
            E(0, 12, 13, "Riunione", "school"),
            E(0, 18, 20, "Consiglio", "school"),
            E(1, 9, 10, "Code week", "work"),
            E(1, 17.5, 18.5, "Colloquio", "personal"),
            E(2, 17, 18, "Coordinamento", "school"),
            E(2, 20, 21, "Lezione", "hobby"),
            E(2, 20.5, 21.5, "Riunione tecnica", "work"),
            E(3, 10.5, 11.5, "Workshop", "work"),
            E(3, 12, 13, "Spesa", "personal"),
            E(3, 15.25, 16.75, "Classe 3E", "school"),
            E(3, 16.75, 18.25, "Classe 4F", "school"),
            E(4, 15.25, 16.5, "CdC", "school"),
            E(4, 16.75, 18.25, "Classe 1E", "school"),
            E(4, 16.75, 18.25, "Classe 5E", "school"),
            E(4, 21, 22, "Meditazione", "hobby"),
            E(5, 16, 18, "Corte", "personal"),
            E(6, 12.5, 16.5, "Grigliata", "personal"),
        ];
    }
}
