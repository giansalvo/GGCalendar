namespace GGCalendar.Core.Ics;

/// <summary>The sentences the user reads after an import: one for each kind of thing the file had that the app could not take as it was.</summary>
public static class IcsMessages
{
    public static IReadOnlyList<string> Describe(IcsReport report)
    {
        var messages = new List<string>();
        if (report.Fatal == IcsIssueCode.NotACalendar) return ["Il file non è un calendario .ics: non ha nessun evento da importare."];
        if (report.Fatal == IcsIssueCode.TooLarge) return ["Il file supera il limite di 10 MB e non è stato letto."];

        if (string.Equals(report.Method, "CANCEL", StringComparison.Ordinal))
            messages.Add("Il file annulla degli eventi (METHOD:CANCEL): nessun evento è stato importato.");
        if (report.CancelledEvents > 0) messages.Add($"{report.CancelledEvents} eventi annullati nel file non sono stati importati.");
        if (report.CancelledOccurrences > 0) messages.Add($"{report.CancelledOccurrences} occorrenze annullate di una serie sono state escluse dalla serie.");
        if (report.OrphanOccurrences > 0)
            messages.Add($"{report.OrphanOccurrences} modifiche di una serie che non è nel file sono state importate come eventi singoli.");
        if (report.IgnoredComponents.Count > 0)
            messages.Add("Parti del file che non sono eventi e sono state ignorate: " + string.Join(", ", report.IgnoredComponents.Select(kv => $"{Name(kv.Key)} ({kv.Value})")) + ".");

        foreach (var issue in report.Issues)
        {
            if (Describe(issue) is { } text) messages.Add(text);
        }

        return messages;
    }

    private static string Name(string component) => component switch
    {
        "VTODO" => "attività",
        "VJOURNAL" => "note di diario",
        "VFREEBUSY" => "disponibilità",
        _ => component,
    };

    private static string? Describe(IcsIssue issue)
    {
        var n = issue.Count;
        return issue.Code switch
        {
            IcsIssueCode.Truncated => "Il file è incompleto (termina prima della fine del calendario): sono stati letti gli eventi completi.",
            IcsIssueCode.MalformedLine => $"{n} righe del file non sono valide e sono state saltate.",
            IcsIssueCode.LineTooLong => $"{n} righe troppo lunghe (oltre 64 KB) sono state saltate.",
            IcsIssueCode.TooManyComponents => "Il file ha troppi elementi (oltre 100000): la lettura si è fermata.",
            IcsIssueCode.TooDeep => $"{n} blocchi annidati troppo in profondità sono stati saltati.",
            IcsIssueCode.MismatchedEnd => "Alcuni blocchi del file non sono chiusi correttamente.",
            IcsIssueCode.MissingStart => $"{n} eventi senza data di inizio sono stati saltati.",
            IcsIssueCode.InvalidDate => $"{n} date non valide: gli eventi corrispondenti sono stati saltati o le date ignorate.",
            IcsIssueCode.EndBeforeStart => $"{n} eventi finivano prima di iniziare: la fine è stata corretta.",
            IcsIssueCode.UnknownTimeZone => $"Fuso orario sconosciuto ({issue.Sample}): gli orari corrispondenti sono letti con l'ora di questo PC.",
            IcsIssueCode.InvalidRule => $"{n} eventi con una ripetizione non supportata: l'evento è importato senza ripetizione.",
            IcsIssueCode.RdateIgnored => $"{n} eventi con date aggiuntive (RDATE): le date aggiuntive vengono ignorate.",
            IcsIssueCode.ThisAndFuture => $"{n} modifiche \"questa e le successive\" sono importate come modifica della sola occorrenza.",
            IcsIssueCode.AbsoluteAlarm => $"{n} promemoria a data fissa non sono stati importati.",
            IcsIssueCode.InvalidAlarm => $"{n} promemoria non validi non sono stati importati.",
            IcsIssueCode.AlarmAfterStart => $"{n} promemoria dopo l'inizio dell'evento non sono stati importati.",
            IcsIssueCode.AlarmTooEarly => $"{n} promemoria con più di 28 giorni di anticipo non sono stati importati.",
            IcsIssueCode.TooManyAlarms => $"{n} promemoria oltre il quinto non sono stati importati.",
            IcsIssueCode.FieldTruncated => $"{n} testi troppo lunghi sono stati accorciati.",
            IcsIssueCode.MissingUid => $"{n} eventi senza identificativo (UID): ne è stato assegnato uno.",
            IcsIssueCode.DuplicateUid => $"{n} eventi ripetuti con lo stesso identificativo: è stato letto uno solo.",
            IcsIssueCode.AttachIgnored => "Alcuni eventi hanno allegati: gli allegati vengono ignorati.",
            IcsIssueCode.CategoriesIgnored => "Alcuni eventi hanno categorie: le categorie vengono ignorate.",
            _ => null,
        };
    }
}
