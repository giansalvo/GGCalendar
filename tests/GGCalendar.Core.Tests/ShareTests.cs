using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Sharing;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T320-T324: sharing an event from its card (issue #45): the text, the addresses of WhatsApp, Telegram and mail, what each way does and
/// says, and the share button with its row of icons. The share sheet of Windows and the real apps are tried by hand (T325).
/// </summary>
[Collection(AppDriver.CollectionName)]
public class ShareTests
{
    private static readonly DateTime Thursday = new(2026, 10, 29, 11, 0, 0);

    /// <summary>Runs <paramref name="body"/> in the language <paramref name="language"/> and puts the language back as it was.</summary>
    private static Task InLanguage(string language, Action body) => HeadlessApp.RunAsync(() =>
    {
        var before = L.Language;
        try
        {
            L.Apply(language);
            body();
        }
        finally
        {
            L.Apply(before);
        }

        return Task.CompletedTask;
    });

    // ---- T320: the text ----

    [Fact]
    [Trait("TestCase", "T320")]
    public Task AnEventWithAPlaceIsWrittenInFourLines() => InLanguage("it", () =>
    {
        var ev = new CalendarEvent("e1", "c1", "Verifica 3E", Thursday, Thursday.AddHours(1), Location: "Aula 12, sede centrale");
        Assert.Equal(
            "Verifica 3E\nGiovedì 29 ottobre 2026, 11:00 – 12:00\nLuogo: Aula 12, sede centrale\nCalendario: Il mio calendario",
            EventShareText.Build(ev, null, "Il mio calendario", use24Hour: true));
    });

    [Fact]
    [Trait("TestCase", "T320")]
    public Task TheTextFollowsTheLanguageAndTheClock() => InLanguage("en", () =>
    {
        var ev = new CalendarEvent("e1", "c1", "Test", Thursday, Thursday.AddHours(1));
        var text = EventShareText.Build(ev, null, "Work", use24Hour: false);
        Assert.Equal("Test\nThursday 29 October 2026, 11:00 AM – 12:00 PM\nCalendar: Work", text);
    });

    [Fact]
    [Trait("TestCase", "T320")]
    public Task MeetDescriptionAndGuestsAreWrittenButNotTheAddressesOfTheGuests() => InLanguage("it", () =>
    {
        var ev = new CalendarEvent("e1", "c1", "Riunione", Thursday, Thursday.AddHours(1),
            Description: "Ordine del giorno\n<a href=\"https://esempio.it/verbale\">verbale</a>")
        {
            MeetLink = "https://meet.google.com/abc-defg-hij",
            Attendees = [new EventAttendee("anna@example.com"), new EventAttendee("luca@example.com"), new EventAttendee("sara@example.com", true)],
        };
        var text = EventShareText.Build(ev, null, "Il mio calendario", true);
        Assert.Equal(
            "Riunione\nGiovedì 29 ottobre 2026, 11:00 – 12:00\nGoogle Meet: https://meet.google.com/abc-defg-hij\n\n" +
            "Ordine del giorno\nverbale (https://esempio.it/verbale)\n\nInvitati: 3\nCalendario: Il mio calendario", text);
        Assert.DoesNotContain("@", text);
    });

    [Fact]
    [Trait("TestCase", "T320")]
    public Task AnAddressAlreadyWrittenInTheDescriptionIsNotRepeated() => InLanguage("it", () =>
    {
        var ev = new CalendarEvent("e1", "c1", "Link", Thursday, Thursday.AddHours(1),
            Description: "<a href=\"https://esempio.it/x\">https://esempio.it/x</a>");
        Assert.Contains("\nhttps://esempio.it/x\n", EventShareText.Build(ev, null, "C", true) + "\n");
        Assert.DoesNotContain("(https", EventShareText.Build(ev, null, "C", true));
    });

    [Fact]
    [Trait("TestCase", "T320")]
    public Task AllDayEventsHaveNoTimeAndALongOneSaysFromAndTo() => InLanguage("it", () =>
    {
        var one = new CalendarEvent("e1", "c1", "Tutti i Santi", new DateTime(2026, 11, 1), new DateTime(2026, 11, 2), IsAllDay: true);
        Assert.Equal("Tutti i Santi\nDomenica 1 novembre 2026", EventShareText.Build(one, null, "", true));

        var trip = new CalendarEvent("e2", "c1", "Gita", new DateTime(2026, 11, 9), new DateTime(2026, 11, 12), IsAllDay: true);
        Assert.Equal("Gita\nDa lunedì 9 novembre 2026 a mercoledì 11 novembre 2026", EventShareText.Build(trip, null, "", true));
    });

    [Fact]
    [Trait("TestCase", "T320")]
    public Task AnEventOverMoreDaysWritesBothEnds() => InLanguage("it", () =>
    {
        var ev = new CalendarEvent("e1", "c1", "Notte", new DateTime(2026, 10, 30, 22, 0, 0), new DateTime(2026, 10, 31, 6, 0, 0));
        Assert.Equal("Notte\nVenerdì 30 ottobre 2026, 22:00 – Sabato 31 ottobre 2026, 06:00", EventShareText.Build(ev, null, "", true));
    });

    [Fact]
    [Trait("TestCase", "T320")]
    public Task TheRuleOfASeriesAndTheSecondZoneHaveTheirLine() => InLanguage("it", () =>
    {
        var ev = new CalendarEvent("e1", "c1", "Colloquio", Thursday, Thursday.AddHours(1));
        var text = EventShareText.Build(ev, "Ogni settimana il giovedì", "Il mio calendario", true, secondZoneText: "New York: 06:00 – 07:00");
        Assert.Equal(
            "Colloquio\nGiovedì 29 ottobre 2026, 11:00 – 12:00\nNew York: 06:00 – 07:00\nSi ripete: Ogni settimana il giovedì\nCalendario: Il mio calendario", text);
    });

    [Fact]
    [Trait("TestCase", "T320")]
    public Task AnEventWithoutTitleAndCalendarStillMakesSense() => InLanguage("it", () =>
    {
        var ev = new CalendarEvent("e1", "c1", "  ", Thursday, Thursday.AddHours(1));
        var lines = EventShareText.Build(ev, null, "", true).Split('\n');
        Assert.Equal(L.T("common.untitled"), lines[0]);
        Assert.Equal(2, lines.Length);
    });

    // ---- T321: the addresses ----

    private const string Awkward = "Cena & festa #1 – è già l'ora?\nPiazza «Roma» 100%";

    [Fact]
    [Trait("TestCase", "T321")]
    public void WhatsAppGetsTheTextInTheAddress()
    {
        var uri = ShareLinks.WhatsApp(Awkward);
        Assert.StartsWith("https://wa.me/?text=", uri.AbsoluteUri);
        Assert.Equal(Awkward, Uri.UnescapeDataString(uri.AbsoluteUri["https://wa.me/?text=".Length..]));
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void TheAppsOpenDirectlyWithTheTextInTheAddress()
    {
        var whatsapp = ShareLinks.WhatsAppApp(Awkward);
        Assert.StartsWith("whatsapp://send?text=", whatsapp.OriginalString);
        Assert.Equal(Awkward, Uri.UnescapeDataString(whatsapp.OriginalString["whatsapp://send?text=".Length..]));
        var telegram = ShareLinks.TelegramApp(Awkward);
        Assert.StartsWith("tg://msg?text=", telegram.OriginalString);
        Assert.Equal(Awkward, Uri.UnescapeDataString(telegram.OriginalString["tg://msg?text=".Length..]));
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void TheMailToTheGuestsCanHaveABodyThatIsCutWhenTooLong()
    {
        var mailto = ShareLinks.MailTo(["anna@example.com", "luca@example.com"], "Titolo", string.Join("\n", Enumerable.Range(1, 300).Select(i => $"Riga {i} è")), out var cut);
        Assert.True(cut);
        Assert.StartsWith("mailto:anna@example.com,luca@example.com?subject=Titolo&body=", mailto);
        Assert.True(mailto!.Length <= ShareLinks.MailtoLimit + 12);
        Assert.Null(ShareLinks.MailTo(["nessuno"], "T", "corpo", out _));
        Assert.Equal("mailto:?subject=T&body=corpo", ShareLinks.MailTo([], "T", "corpo", out _));
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void TelegramGetsTheTextInTheAddress()
    {
        var uri = ShareLinks.Telegram(Awkward);
        Assert.StartsWith("https://t.me/share/url?url=%20&text=", uri.AbsoluteUri);
        Assert.Equal(Awkward, Uri.UnescapeDataString(uri.AbsoluteUri["https://t.me/share/url?url=%20&text=".Length..]));
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void MailHasTheTitleAsSubjectTheTextAsBodyAndNoRecipient()
    {
        var uri = ShareLinks.Mail("Cena & festa", Awkward, out var cut);
        Assert.False(cut);
        Assert.Equal("mailto", uri.Scheme);
        var text = uri.OriginalString;
        Assert.StartsWith("mailto:?subject=Cena%20%26%20festa&body=", text);
        Assert.Equal(Awkward.Replace("\n", "\r\n"), Uri.UnescapeDataString(text[(text.IndexOf("&body=", StringComparison.Ordinal) + 6)..]));
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void ALongTextIsCutForTheMailAndSaysSo()
    {
        var text = string.Join("\n", Enumerable.Range(1, 300).Select(i => $"Riga {i} con qualche parola in più è"));
        var uri = ShareLinks.Mail("Titolo", text, out var cut);
        Assert.True(cut);
        Assert.True(uri.OriginalString.Length <= ShareLinks.MailtoLimit + 12, uri.OriginalString.Length.ToString());
        var body = Uri.UnescapeDataString(uri.OriginalString[(uri.OriginalString.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        Assert.EndsWith("…", body);
        Assert.StartsWith("Riga 1 con qualche parola in più è\r\nRiga 2", body);
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void TheCutNeverSplitsACharacterMadeOfTwoParts()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 600));
        var uri = ShareLinks.Mail("Titolo", text, out var cut);
        Assert.True(cut);
        var body = Uri.UnescapeDataString(uri.OriginalString[(uri.OriginalString.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        Assert.DoesNotContain('�', body);
        Assert.All(body.TrimEnd('…').Where(char.IsSurrogate).Chunk(2), pair => Assert.True(char.IsSurrogatePair(pair[0], pair[1])));
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void WritingToTheGuestsKeepsTheAtSignAndTheTitleAsSubject()
    {
        var uri = ShareLinks.MailTo(["anna@example.com", " luca@example.com "], "Cena & festa");
        Assert.Equal("mailto:anna@example.com,luca@example.com?subject=Cena%20%26%20festa", uri);
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void WritingToTheGuestsLeavesOutWhatIsNotAnAddressAndNeverThrows()
    {
        var uri = ShareLinks.MailTo(["anna@example.com", "senza chiocciola", "due@@example.com", "a b@example.com", "x,y@example.com", ""], "T");
        Assert.Equal("mailto:anna@example.com?subject=T", uri);
        Assert.Null(ShareLinks.MailTo(["nessuno", "@", "a@"], "T"));
        Assert.Equal("mailto:?subject=T", ShareLinks.MailTo([], "T")); // nobody given: no recipient
    }

    [Fact]
    [Trait("TestCase", "T321")]
    public void TheAtSignWrittenAsPercent40BreaksTheAddressWhichIsWhatClosedTheApp()
    {
        // The old code escaped the whole address: this is the exception that ended the program. Two addresses do not parse either.
        Assert.Throws<UriFormatException>(() => new Uri($"mailto:{Uri.EscapeDataString("anna@example.com")}?subject=x"));
        Assert.False(Uri.TryCreate("mailto:anna@example.com,luca@example.com?subject=x", UriKind.Absolute, out _));
    }

    // ---- T322: what each way does and says ----

    private sealed class Calls
    {
        public List<Uri> Launched { get; } = [];
        public List<string> MailTos { get; } = [];
        public List<string> Copied { get; } = [];
        public List<CalendarEvent> Saved { get; } = [];
        public List<(CalendarEvent Event, string Text)> Sheet { get; } = [];
        public bool SaveResult { get; set; } = true;
        public Exception? Fail { get; set; }

        /// <summary>The apps (whatsapp:, tg:) are not installed: no program handles their address.</summary>
        public bool AppsMissing { get; set; }

        /// <summary>Starting the app fails with an exception (the system has the address but cannot open it).</summary>
        public bool AppsThrow { get; set; }

        public EventSharer Sharer() => new()
        {
            Launch = uri =>
            {
                if (Fail is not null) throw Fail;
                var isApp = uri.Scheme is "whatsapp" or "tg";
                if (isApp && AppsThrow) throw new InvalidOperationException("no handler");
                Launched.Add(uri);
                return Task.FromResult(!(isApp && AppsMissing));
            },
            OpenMailTo = mailto => { if (Fail is not null) throw Fail; MailTos.Add(mailto); return Task.CompletedTask; },
            Copy = text => { if (Fail is not null) throw Fail; Copied.Add(text); return Task.CompletedTask; },
            SaveIcs = ev => { if (Fail is not null) throw Fail; Saved.Add(ev); return Task.FromResult(SaveResult); },
            ShowShareSheet = (ev, text) => { if (Fail is not null) throw Fail; Sheet.Add((ev, text)); return Task.CompletedTask; },
        };
    }

    private static readonly CalendarEvent Plain = new("e1", "c1", "Riunione", Thursday, Thursday.AddHours(1));

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task WhatsAppAndTelegramOpenTheirAppDirectlyWithTheText()
    {
        var calls = new Calls();
        Assert.Equal("status.share_whatsapp", (await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.WhatsApp))!.Key);
        Assert.Equal("status.share_telegram", (await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.Telegram))!.Key);
        // Only the address of the app: the browser is not opened.
        Assert.Equal([ShareLinks.WhatsAppApp("testo"), ShareLinks.TelegramApp("testo")], calls.Launched);
        Assert.Empty(calls.Copied);
    }

    [Theory]
    [Trait("TestCase", "T322")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutTheAppTheWebAddressIsTheWayLeft(bool throws)
    {
        var calls = new Calls { AppsMissing = !throws, AppsThrow = throws };
        Assert.Equal("status.share_whatsapp", (await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.WhatsApp))!.Key);
        Assert.Equal("status.share_telegram", (await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.Telegram))!.Key);
        Assert.Equal(throws ? [ShareLinks.WhatsApp("testo"), ShareLinks.Telegram("testo")]
            : [ShareLinks.WhatsAppApp("testo"), ShareLinks.WhatsApp("testo"), ShareLinks.TelegramApp("testo"), ShareLinks.Telegram("testo")], calls.Launched);
    }

    private static readonly CalendarEvent WithGuests = Plain with
    {
        Attendees = [new EventAttendee("anna@example.com"), new EventAttendee("luca@example.com"), new EventAttendee("non valido")],
    };

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task WritingToTheGuestsPutsTheTextOfTheEventInTheBody()
    {
        var calls = new Calls();
        var text = "Riunione\nGiovedì 29 ottobre 2026, 11:00 – 12:00\n\nOrdine del giorno\nverbale (https://esempio.it/verbale)";
        var message = await calls.Sharer().MailGuestsAsync(WithGuests, text);
        Assert.Equal("status.share_mail", message!.Key);
        var mailto = Assert.Single(calls.MailTos);
        Assert.StartsWith("mailto:anna@example.com,luca@example.com?subject=Riunione&body=", mailto);
        Assert.Equal(text.Replace("\n", "\r\n"), Uri.UnescapeDataString(mailto[(mailto.IndexOf("&body=", StringComparison.Ordinal) + 6)..]));
        Assert.Empty(calls.Copied);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task TheGuestsAreAWayOfSharingToo()
    {
        var calls = new Calls();
        Assert.Equal("status.share_mail", (await calls.Sharer().ShareAsync(WithGuests, "corpo", ShareTarget.MailGuests))!.Key);
        Assert.StartsWith("mailto:anna@example.com,luca@example.com?subject=Riunione&body=corpo", Assert.Single(calls.MailTos));
        Assert.Empty(calls.Launched);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task ALongTextToTheGuestsGoesWholeToTheClipboard()
    {
        var calls = new Calls();
        var text = new string('a', 5000);
        Assert.Equal("status.share_mail_cut", (await calls.Sharer().MailGuestsAsync(WithGuests, text))!.Key);
        Assert.Equal(text, Assert.Single(calls.Copied));
        Assert.Single(calls.MailTos);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task WithNoUsableGuestAddressNothingOpensAndNothingFails()
    {
        var calls = new Calls();
        var noGuests = Plain with { Attendees = [new EventAttendee("non valido")] };
        Assert.Null(await calls.Sharer().MailGuestsAsync(noGuests, "testo"));
        Assert.Empty(calls.MailTos);
        calls.Fail = new InvalidOperationException("boom");
        Assert.Equal("error.share_open", (await calls.Sharer().MailGuestsAsync(WithGuests, "testo"))!.Key);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task MailOpensTheDraftWithTheTitleAsSubject()
    {
        var calls = new Calls();
        var message = await calls.Sharer().ShareAsync(Plain, "corpo", ShareTarget.Mail);
        Assert.Equal("status.share_mail", message!.Key);
        Assert.Equal(ShareLinks.Mail("Riunione", "corpo", out _), Assert.Single(calls.Launched));
        Assert.Empty(calls.Copied);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task ALongTextGoesWholeToTheClipboardWhenTheMailGetsOnlyTheBeginning()
    {
        var calls = new Calls();
        var text = new string('a', 5000);
        var message = await calls.Sharer().ShareAsync(Plain, text, ShareTarget.Mail);
        Assert.Equal("status.share_mail_cut", message!.Key);
        Assert.Equal(text, Assert.Single(calls.Copied));
        Assert.Single(calls.Launched);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task TheClipboardGetsTheText()
    {
        var calls = new Calls();
        Assert.Equal("status.share_clipboard", (await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.Clipboard))!.Key);
        Assert.Equal("testo", Assert.Single(calls.Copied));
        Assert.Empty(calls.Launched);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task TheDiskSavesTheFileAndSaysSoOnlyWhenItWasSaved()
    {
        var calls = new Calls();
        Assert.Equal("status.share_saved", (await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.Disk))!.Key);
        calls.SaveResult = false; // the user closed the file picker
        Assert.Null(await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.Disk));
        Assert.Equal([Plain, Plain], calls.Saved);
        Assert.Empty(calls.Copied);
    }

    [Fact]
    [Trait("TestCase", "T322")]
    public async Task OtherAppsGetsTheEventAndTheText()
    {
        var calls = new Calls();
        Assert.Null(await calls.Sharer().ShareAsync(Plain, "testo", ShareTarget.Apps));
        Assert.Equal((Plain, "testo"), Assert.Single(calls.Sheet));
    }

    [Theory]
    [Trait("TestCase", "T322")]
    [InlineData(ShareTarget.WhatsApp, "error.share_open")]
    [InlineData(ShareTarget.Telegram, "error.share_open")]
    [InlineData(ShareTarget.Mail, "error.share_open")]
    [InlineData(ShareTarget.Clipboard, "error.share_open")]
    [InlineData(ShareTarget.Disk, "error.export")]
    [InlineData(ShareTarget.Apps, "error.share_apps")]
    public async Task AFailureBecomesAnErrorMessageAndNeverAnException(ShareTarget target, string key)
    {
        var calls = new Calls { Fail = new InvalidOperationException("boom") };
        var message = await calls.Sharer().ShareAsync(Plain, "testo", target);
        Assert.Equal(key, message!.Key);
        Assert.Contains("boom", message.Args.Select(a => a?.ToString()));
    }

    // ---- T323: the texts exist in every language ----

    [Fact]
    [Trait("TestCase", "T323")]
    public Task EveryLanguageHasTheTextsOfSharing() => HeadlessApp.RunAsync(() =>
    {
        var before = L.Language;
        try
        {
            string[] keys =
            [
                "details.share", "share.hint.whatsapp", "share.hint.telegram", "share.hint.mail", "share.hint.mail_guests", "share.hint.disk", "share.hint.clipboard", "share.hint.apps",
                "share.location", "share.meet", "share.repeats", "share.from_to", "share.guests.one", "share.guests.other", "share.calendar",
                "status.share_whatsapp", "status.share_telegram", "status.share_mail", "status.share_mail_cut", "status.share_clipboard", "status.share_saved",
                "error.share_open", "error.share_apps",
            ];
            foreach (var language in L.Languages.Select(l => l.Code))
            {
                foreach (var key in keys) Assert.True(L.Has(language, key), $"{language}: {key}");
            }
        }
        finally
        {
            L.Apply(before);
        }

        return Task.CompletedTask;
    });

    // ---- T324: the share button and its row of icons ----

    private static string? AutomationIdOf(Control c) => Avalonia.Automation.AutomationProperties.GetAutomationId(c);

    private static Task InCardAsync(CalendarEvent ev, bool readOnly, Func<EventDetailsCard, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var card = new EventDetailsCard(ev, null, "#1A73E8", "Calendario", "Account", use24Hour: true, dark: false, readOnly: readOnly);
        var window = new Window { Width = 600, Height = 600, Content = card };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await test(card);
        }
        finally
        {
            window.Close();
        }
    });

    private static Button ShareButton(EventDetailsCard card) =>
        card.GetVisualDescendants().OfType<Button>().Single(b => AutomationIdOf(b) == "Details.Share");

    private static List<Button> Choices(EventDetailsCard card) =>
        ((StackPanel)((Avalonia.Controls.Flyout)ShareButton(card).Flyout!).Content!).Children.OfType<Button>().ToList();

    [Fact]
    [Trait("TestCase", "T324")]
    public Task TheCardHasTheShareButtonAlsoForAnEventThatCannotBeChanged() => InCardAsync(Plain, readOnly: true, card =>
    {
        Assert.Equal(["Details.Share"], card.GetVisualDescendants().OfType<Button>().Select(AutomationIdOf).Where(id => id is { Length: > 0 }).ToList());
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T324")]
    public Task TheShareButtonComesAfterDeleteAndTheMailToTheGuestsIsNoLongerOnTheCard() => InCardAsync(
        Plain with { Attendees = [new EventAttendee("anna@example.com")] }, readOnly: false, card =>
    {
        // Edit, Delete, Share, Close: the mail to the guests is a choice of the row of Share (a second icon of mail on the card was confusing).
        var buttons = card.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(4, buttons.Count);
        var share = buttons.IndexOf(ShareButton(card));
        Assert.Equal(L.T("edit.delete_event"), (string?)ToolTip.GetTip(buttons[share - 1]));
        Assert.Equal(L.T("common.close"), (string?)ToolTip.GetTip(buttons[share + 1]));
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T324")]
    public Task TheRowHasTheMailToTheGuestsRightAfterTheMailOnlyForAnEventWithGuests() => InCardAsync(
        Plain with { Attendees = [new EventAttendee("anna@example.com")] }, readOnly: false, card =>
    {
        Assert.Equal(["Share.WhatsApp", "Share.Telegram", "Share.Mail", "Share.MailGuests", "Share.Disk", "Share.Clipboard", "Share.Apps"], Choices(card).Select(AutomationIdOf).ToList());
        var chosen = new List<ShareTarget>();
        card.ShareRequested += chosen.Add;
        Choices(card).Single(b => AutomationIdOf(b) == "Share.MailGuests").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([ShareTarget.MailGuests], chosen);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T324")]
    public Task TheCardHasNoMenuOfThreeDotsAnymoreBecauseTheDiskSavesTheFile() => InCardAsync(
        Plain with { Attendees = [new EventAttendee("anna@example.com")] }, readOnly: false, card =>
    {
        // Edit, Delete, Share, Close: none opens a menu of items.
        var buttons = card.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(4, buttons.Count);
        Assert.DoesNotContain(buttons, b => b.Flyout is MenuFlyout);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T324")]
    public Task TheRowHasSixChoicesInTheOrderOfTheMockup() => InCardAsync(Plain, readOnly: false, card =>
    {
        Assert.Equal(["Share.WhatsApp", "Share.Telegram", "Share.Mail", "Share.Disk", "Share.Clipboard", "Share.Apps"], Choices(card).Select(AutomationIdOf).ToList());
        // The two brands show their own logo; none of the choices has a tooltip (it would hide the others).
        Assert.Equal(2, Choices(card).Count(b => b.Content is Avalonia.Controls.Image));
        Assert.All(Choices(card), b => Assert.Null(ToolTip.GetTip(b)));
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T324")]
    public Task EveryChoiceRaisesItsWay() => InCardAsync(Plain, readOnly: false, card =>
    {
        var chosen = new List<ShareTarget>();
        card.ShareRequested += chosen.Add;
        foreach (var choice in Choices(card)) choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([ShareTarget.WhatsApp, ShareTarget.Telegram, ShareTarget.Mail, ShareTarget.Disk, ShareTarget.Clipboard, ShareTarget.Apps], chosen);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T324")]
    public Task TheStatusBarTextOfAChoiceShowsWhileItIsFocusedAndGoesWhenItLeaves() => InCardAsync(Plain, readOnly: false, async card =>
    {
        var hints = new List<string?>();
        card.ShareHint += hints.Add;
        var share = ShareButton(card);
        share.Flyout!.ShowAt(share);
        Dispatcher.UIThread.RunJobs();
        var mail = Choices(card).Single(b => AutomationIdOf(b) == "Share.Mail");
        await WaitForAsync(() => mail.IsEffectivelyVisible && mail.Focus() ? mail : null, "l'icona dell'email");
        Assert.Equal("share.hint.mail", hints.Last());
        Choices(card).Single(b => AutomationIdOf(b) == "Share.Disk").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("share.hint.disk", hints.Last());
        share.Flyout.Hide();
    });

    [Fact]
    [Trait("TestCase", "T324")]
    public Task OnTheScreenTheChoiceCopiesTheTextAndTheStatusBarSaysSo() => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var copies = new List<string>();
        MainWindow.SharerFactory = _ => new EventSharer
        {
            Launch = _ => Task.FromResult(true),
            OpenMailTo = _ => Task.CompletedTask,
            Copy = text => { copies.Add(text); return Task.CompletedTask; },
            SaveIcs = _ => Task.FromResult(true),
            ShowShareSheet = (_, _) => Task.CompletedTask,
        };
        var window = OpenApp();
        try
        {
            var show = typeof(MainWindow).GetMethod("ShowDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var ev = new CalendarEvent("share-1", "no-calendar", "Verifica di prova", Thursday, Thursday.AddHours(1), Location: "Aula 12");
            show.Invoke(window, [ev, window.FindControl<Control>("Grid")!, null]);
            Dispatcher.UIThread.RunJobs();

            var card = (EventDetailsCard)window.FindControl<Avalonia.Controls.Primitives.Popup>("DetailsPopup")!.Child!;
            Choices(card).Single(b => AutomationIdOf(b) == "Share.Clipboard").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => copies.Count > 0 ? copies : null, "la copia negli appunti");

            Assert.Contains("Verifica di prova", copies[0]);
            Assert.Contains("Aula 12", copies[0], StringComparison.Ordinal);
            await WaitForAsync(() => ((MainViewModel)window.DataContext!).Status?.Key == "status.share_clipboard" ? window : null, "il messaggio nella barra di stato");
            Assert.Null(window.FindControl<Avalonia.Controls.Primitives.Popup>("DetailsPopup")!.Child);
        }
        finally
        {
            MainWindow.SharerFactory = null;
            window.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });
}
