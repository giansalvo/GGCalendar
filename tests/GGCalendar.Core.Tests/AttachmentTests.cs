using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Core.Search;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests;

/// <summary>The attachments of an event (issue of the 1.0.3): links to files, https only, read and written on Google, in the .ics, in the search.</summary>
public class AttachmentTests
{
    private const string Image = "https://profgusinu.altervista.org/img/youtube_logo.jpg";
    private const string Feeds = "https://profgusinu.altervista.org/GGCalendarAndroid/feeds/";
    private const string Drive = "https://drive.google.com/open?id=1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe&authuser=5";
    private static readonly DateTime Start = new(2026, 10, 14, 9, 0, 0);

    private static CalendarEvent Ev(params EventAttachment[] attachments) =>
        new("e1", "c1", "Riunione", Start, Start.AddHours(1)) { Reminders = [], Attachments = attachments };

    // ---- T299: the link ----

    [Theory]
    [Trait("TestCase", "T299")]
    [InlineData("https://profgusinu.altervista.org/img/youtube_logo.jpg", true)]
    [InlineData("https://profgusinu.altervista.org/GGCalendarAndroid/feeds/ggcal_basic.ics", true)]
    [InlineData("https://drive.google.com/open?id=1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe&authuser=5", true)]
    [InlineData("https://drive.google.com/file/d/1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe/view", true)]
    [InlineData("HTTPS://Example.ORG/Doc.PDF", true)]
    [InlineData("https://example.org:8443/a%20b.txt?x=1#top", true)]
    [InlineData("http://profgusinu.altervista.org/img/youtube_logo.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("ftp://example.org/a.txt", false)]
    [InlineData("data:text/plain;base64,QQ==", false)]
    [InlineData("mailto:a@b.it", false)]
    [InlineData("//example.org/a.txt", false)]
    [InlineData("example.org/a.txt", false)]
    [InlineData("https://", false)]
    [InlineData("https:///a.txt", false)]
    [InlineData("https://user:pass@example.org/a.txt", false)]
    [InlineData("https://example.org/a b.txt", false)]
    [InlineData("https://example.org/a\tb.txt", false)]
    [InlineData(" https://example.org/a.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAnAbsoluteHttpsAddressIsValid(string? url, bool expected) => Assert.Equal(expected, AttachmentLink.IsValid(url));

    [Fact]
    [Trait("TestCase", "T299")]
    public void AnAddressLongerThan2048CharactersIsNotValid()
    {
        var ok = "https://example.org/" + new string('a', AttachmentLink.MaxUrlLength - "https://example.org/".Length);
        Assert.Equal(AttachmentLink.MaxUrlLength, ok.Length);
        Assert.True(AttachmentLink.IsValid(ok));
        Assert.False(AttachmentLink.IsValid(ok + "a"));
    }

    [Fact]
    [Trait("TestCase", "T299")]
    public void CreateKeepsTheAddressAsItWasPasted()
    {
        var created = AttachmentLink.Create("  " + Drive + "  ", null)!;
        Assert.Equal(Drive, created.Url); // no conversion: only the spaces around it are removed
        Assert.Null(AttachmentLink.Create("http://example.org/a.txt"));
        Assert.Null(AttachmentLink.Create("   "));
    }

    [Fact]
    [Trait("TestCase", "T299")]
    public void ALinkThatIsNotHttpsCannotBeOpened()
    {
        Assert.True(new EventAttachment(Image, "x").CanOpen);
        Assert.False(new EventAttachment("http://example.org/a.png", "x").CanOpen);
        Assert.False(new EventAttachment("javascript:alert(1)", "x").CanOpen);
    }

    // ---- T300: the title and the type ----

    [Theory]
    [Trait("TestCase", "T300")]
    [InlineData(Image, "youtube_logo.jpg", "image/jpeg")]
    [InlineData(Feeds + "ggcal_basic.ics", "ggcal_basic.ics", "text/calendar")]
    [InlineData(Feeds + "ggcal_not_calendar.html", "ggcal_not_calendar.html", "text/html")]
    [InlineData("https://example.org/doc/Relazione.PDF", "Relazione.PDF", "application/pdf")]
    [InlineData("https://example.org/a/foto%20di%20gruppo.png?v=2#x", "foto di gruppo.png", "image/png")]
    [InlineData("https://example.org/a/note.txt", "note.txt", "text/plain")]
    [InlineData("https://example.org/a/lettera.docx", "lettera.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("https://example.org/a/vecchio.doc", "vecchio.doc", "application/msword")]
    [InlineData("https://example.org/a/dati.xlsx", "dati.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("https://example.org/a/senza_estensione", "senza_estensione", null)]
    [InlineData("https://example.org/a/strano.xyz", "strano.xyz", null)]
    [InlineData("https://example.org/", "", null)]
    public void TheNameAndTheTypeOfTheFileComeFromTheAddress(string url, string name, string? type)
    {
        Assert.Equal(name, AttachmentLink.FileName(url));
        Assert.Equal(type, AttachmentLink.MimeType(url));
    }

    [Theory]
    [Trait("TestCase", "T300")]
    [InlineData(Drive)]
    [InlineData("https://drive.google.com/file/d/1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe/view")]
    [InlineData("https://docs.google.com/document/d/abc/edit")]
    public void APageOfDriveHasNoNameInItsAddress(string url) => Assert.Equal("", AttachmentLink.FileName(url));

    [Fact]
    [Trait("TestCase", "T300")]
    public void TheTitleIsOptionalAndDefaultsToTheNameOfTheFile()
    {
        Assert.Equal("youtube_logo.jpg", AttachmentLink.Create(Image, "")!.Title);
        Assert.Equal("youtube_logo.jpg", AttachmentLink.Create(Image, null)!.Title);
        Assert.Equal("Il logo", AttachmentLink.Create(Image, "  Il logo ")!.Title);
        Assert.Equal("image/jpeg", AttachmentLink.Create(Image)!.MimeType);
        Assert.Equal("", AttachmentLink.Create(Drive)!.Title); // a Drive page without a title has none
        Assert.Equal(Drive, AttachmentLink.Create(Drive)!.DisplayName); // shown by its address
    }

    [Fact]
    [Trait("TestCase", "T300")]
    public void ALongTitleIsCutAt256Characters() =>
        Assert.Equal(AttachmentLink.MaxTitleLength, AttachmentLink.Create(Image, new string('t', 400))!.Title.Length);

    // ---- T301: Google ----

    private static CalendarEvent FromGoogle(string json) =>
        GoogleEventMapper.FromGoogle(JsonDocument.Parse(json).RootElement, "e1", "c1", [])!;

    private const string Head = "{\"id\":\"r1\",\"summary\":\"x\",\"start\":{\"dateTime\":\"2026-10-14T07:00:00Z\"},\"end\":{\"dateTime\":\"2026-10-14T08:00:00Z\"}";

    [Fact]
    [Trait("TestCase", "T301")]
    public void GoogleAttachmentsAreRead()
    {
        var ev = FromGoogle(Head + ",\"attachments\":[" +
            "{\"fileUrl\":\"" + Image + "\",\"title\":\"youtube_logo.jpg\",\"mimeType\":\"image/jpeg\",\"iconLink\":\"https://drive-thirdparty.googleusercontent.com/16/type/image/jpeg\"}," +
            "{\"fileUrl\":\"" + Drive + "\",\"title\":\"file drive\",\"mimeType\":\"application/octet-stream\",\"iconLink\":\"\",\"fileId\":\"1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe\"}]}");

        Assert.Equal(2, ev.Attachments!.Count);
        Assert.Equal(new EventAttachment(Image, "youtube_logo.jpg", "image/jpeg", null, "https://drive-thirdparty.googleusercontent.com/16/type/image/jpeg"), ev.Attachments[0]);
        Assert.Equal(("1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe", null), (ev.Attachments[1].FileId, ev.Attachments[1].IconUrl));
    }

    [Fact]
    [Trait("TestCase", "T301")]
    public void AnEventWithoutTheFieldHasNoAttachmentsAndTheListIsKnown() => Assert.Empty(FromGoogle(Head + "}").Attachments!);

    [Fact]
    [Trait("TestCase", "T301")]
    public void AnAttachmentWithoutAddressIsSkippedAndAMissingTitleIsEmpty()
    {
        var ev = FromGoogle(Head + ",\"attachments\":[{\"title\":\"senza link\"},{\"fileUrl\":\"" + Image + "\"}]}");
        var only = Assert.Single(ev.Attachments!);
        Assert.Equal("", only.Title);
    }

    [Fact]
    [Trait("TestCase", "T301")]
    public void AttachmentsAreWrittenWithTheAddressTheTitleAndTheType()
    {
        var body = GoogleEventMapper.ToGoogle(Ev(new EventAttachment(Image, "youtube_logo.jpg", "image/jpeg", "ignored-file-id", "ignored-icon"), new EventAttachment(Drive, "", null)), false);
        var list = body["attachments"]!.AsArray();
        Assert.Equal(2, list.Count);
        Assert.Equal(Image, list[0]!["fileUrl"]!.GetValue<string>());
        Assert.Equal("image/jpeg", list[0]!["mimeType"]!.GetValue<string>());
        Assert.Equal("youtube_logo.jpg", list[0]!["title"]!.GetValue<string>());
        Assert.Null(list[0]!["fileId"]); // Google's own (read only): never sent
        Assert.Null(list[0]!["iconLink"]);
        Assert.Null(list[1]!["mimeType"]);
    }

    [Fact]
    [Trait("TestCase", "T301")]
    public void UnreadAttachmentsAreNotSentSoGoogleKeepsThem()
    {
        var unread = new CalendarEvent("e1", "c1", "x", Start, Start.AddHours(1)) { Reminders = [] };
        Assert.Null(unread.Attachments);
        Assert.False(GoogleEventMapper.ToGoogle(unread, false).ContainsKey("attachments"));
    }

    [Fact]
    [Trait("TestCase", "T301")]
    public void NoAttachmentsAreSentAsAnEmptyListWhichRemovesThem() =>
        Assert.Empty(GoogleEventMapper.ToGoogle(Ev(), false)["attachments"]!.AsArray());

    [Fact]
    [Trait("TestCase", "T301")]
    public void AtMost25AttachmentsAreSent()
    {
        var many = Enumerable.Range(0, 30).Select(i => new EventAttachment($"https://example.org/{i}.txt", $"n{i}")).ToArray();
        Assert.Equal(25, GoogleEventMapper.ToGoogle(Ev(many), false)["attachments"]!.AsArray().Count);
    }

    // ---- T302: sync ----

    private static string Attachment(JsonNode? list, int i, string field) => list!.AsArray()[i]![field]!.GetValue<string>();

    [Fact]
    [Trait("TestCase", "T302")]
    public async Task AnEventWithAttachmentsReachesGoogleWithSupportsAttachments()
    {
        using var rig = new SyncRig();
        rig.AddLocal(SyncRig.NewEvent("e1", "Con allegati") with { Attachments = [new EventAttachment(Image, "youtube_logo.jpg", "image/jpeg"), new EventAttachment(Drive, "drive")] });

        await rig.SyncOkAsync();

        var id = Assert.Single(rig.Google.Ids);
        var stored = rig.Google.AttachmentsOf(id)!;
        Assert.Equal(2, stored.Count);
        Assert.Equal(Image, Attachment(stored, 0, "fileUrl"));
        Assert.Equal("1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe", Attachment(stored, 1, "fileId"));
        Assert.Contains(rig.Google.Requests, r => r.StartsWith("POST") && r.Contains("supportsAttachments=true"));
    }

    [Fact]
    [Trait("TestCase", "T302")]
    public async Task ChangingTheTitleOfAnEventWhoseAttachmentsWereNeverReadKeepsTheOnesOnGoogle()
    {
        using var rig = new SyncRig();
        var original = rig.AddLocal(SyncRig.NewEvent("e1", "Vecchio")); // as an event synced by the version before: attachments unread (null)
        await rig.SyncOkAsync();
        var id = Assert.Single(rig.Google.Ids);
        rig.Google.SetAttachments(id, (Image, "youtube_logo.jpg"), (Feeds + "ggcal_basic.ics", "ggcal_basic.ics"));

        var stale = original with { Attachments = null };
        rig.Edit(stale, stale with { Title = "Nuovo titolo" });
        await rig.SyncOkAsync();

        Assert.Equal("Nuovo titolo", rig.Google.Summary(id));
        Assert.Equal(2, rig.Google.AttachmentsOf(id)!.Count); // the edit did not send an empty list
    }

    [Fact]
    [Trait("TestCase", "T302")]
    public async Task TheAttachmentsOfGoogleComeToTheAppWithTheNextSync()
    {
        using var rig = new SyncRig();
        rig.AddLocal(SyncRig.NewEvent("e1", "Da Google"));
        await rig.SyncOkAsync();
        var id = Assert.Single(rig.Google.Ids);

        rig.Google.SetAttachments(id, (Image, "youtube_logo.jpg"), (Drive, "file drive"));
        await rig.SyncOkAsync();

        var local = rig.Local("Da Google")!;
        Assert.Equal([Image, Drive], local.Attachments!.Select(a => a.Url));
        Assert.Equal("1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe", local.Attachments![1].FileId); // Google added it
    }

    [Fact]
    [Trait("TestCase", "T302")]
    public async Task RemovingAnAttachmentSendsTheOthersAndRemovingAllSendsAnEmptyList()
    {
        using var rig = new SyncRig();
        rig.AddLocal(SyncRig.NewEvent("e1", "Elenco"));
        await rig.SyncOkAsync();
        var id = Assert.Single(rig.Google.Ids);
        rig.Google.SetAttachments(id, (Image, "a"), (Feeds + "ggcal_basic.ics", "b"));
        await rig.SyncOkAsync();

        var withTwo = rig.Local("Elenco")!;
        Assert.Equal(2, withTwo.Attachments!.Count);
        var withOne = rig.Edit(withTwo, withTwo with { Attachments = [withTwo.Attachments[1]] });
        await rig.SyncOkAsync();
        Assert.Equal(Feeds + "ggcal_basic.ics", Attachment(rig.Google.AttachmentsOf(id), 0, "fileUrl"));
        Assert.Single(rig.Google.AttachmentsOf(id)!);

        rig.Edit(withOne, withOne with { Attachments = [] });
        await rig.SyncOkAsync();
        Assert.Null(rig.Google.AttachmentsOf(id));
    }

    [Fact]
    [Trait("TestCase", "T302")]
    public async Task GoogleKeepsAtMost25AndTheAppSeesTheTruth()
    {
        using var rig = new SyncRig();
        var many = Enumerable.Range(0, 30).Select(i => new EventAttachment($"https://example.org/{i}.txt", $"n{i}")).ToArray();
        rig.AddLocal(SyncRig.NewEvent("e1", "Troppi") with { Attachments = many });

        await rig.SyncOkAsync();
        await rig.SyncOkAsync();

        Assert.Equal(25, rig.Google.AttachmentsOf(Assert.Single(rig.Google.Ids))!.Count);
    }

    [Fact]
    [Trait("TestCase", "T302")]
    public void TheListIsPartOfWhatMakesTwoVersionsDifferent()
    {
        var one = Ev(new EventAttachment(Image, "a"));
        Assert.True(EventContent.Same(one, one with { Attachments = [new EventAttachment(Image, "a", "image/jpeg", null, "icon")] })); // the type and the icon are Google's
        Assert.False(EventContent.Same(one, Ev(new EventAttachment(Image, "altro titolo"))));
        Assert.False(EventContent.Same(one, Ev()));
        Assert.True(EventContent.Same(Ev(), Ev() with { Attachments = null })); // unread is the same as none
        Assert.True(EventContent.Same(one, Ev(new EventAttachment(Drive, "b")) with { Attachments = null })); // unread says nothing: it is not a difference
        Assert.True(EventContent.Same(Ev(new EventAttachment(Image, "a"), new EventAttachment(Drive, "b")), Ev(new EventAttachment(Drive, "b"), new EventAttachment(Image, "a"))));
    }

    // ---- T305: .ics, writing ----

    [Fact]
    [Trait("TestCase", "T305")]
    public void TheAttachmentsAreWrittenAsAttach()
    {
        var ics = IcsWriter.Write([Ev(new EventAttachment(Image, "youtube_logo.jpg", "image/jpeg"), new EventAttachment(Feeds + "ggcal_basic.ics", "ggcal_basic.ics", "text/calendar"))]);

        var unfolded = ics.Replace("\r\n ", "");
        Assert.Contains("ATTACH;FMTTYPE=image/jpeg;FILENAME=youtube_logo.jpg:" + Image + "\r\n", unfolded);
        Assert.Contains("ATTACH;FMTTYPE=text/calendar;FILENAME=ggcal_basic.ics:" + Feeds + "ggcal_basic.ics\r\n", unfolded);
    }

    [Fact]
    [Trait("TestCase", "T305")]
    public void NoAttachmentsNoAttachLines()
    {
        Assert.DoesNotContain("ATTACH", IcsWriter.Write([Ev()]));
        Assert.DoesNotContain("ATTACH", IcsWriter.Write([Ev() with { Attachments = null }]));
    }

    [Fact]
    [Trait("TestCase", "T305")]
    public void ATitleWithSpecialCharactersIsQuotedAndAQuoteIsReplaced()
    {
        var ics = IcsWriter.Write([Ev(new EventAttachment(Image, "Foto: \"gita\", 2026; prova"))]).Replace("\r\n ", "");
        Assert.Contains("ATTACH;FILENAME=\"Foto: 'gita', 2026; prova\":" + Image, ics);
    }

    [Fact]
    [Trait("TestCase", "T305")]
    public void AttachmentsWithoutATitleOrATypeHaveNoParameters() =>
        Assert.Contains("ATTACH:" + Drive + "\r\n", IcsWriter.Write([Ev(new EventAttachment(Drive))]).Replace("\r\n ", ""));

    [Fact]
    [Trait("TestCase", "T305")]
    public void OnlyHttpsLinksAreExportedAndNoMoreThan25()
    {
        var items = new[] { new EventAttachment("http://example.org/a.txt", "http"), new EventAttachment("javascript:alert(1)", "js") }
            .Concat(Enumerable.Range(0, 30).Select(i => new EventAttachment($"https://example.org/{i}.txt", $"n{i}"))).ToArray();
        var ics = IcsWriter.Write([Ev(items)]).Replace("\r\n ", "");

        Assert.DoesNotContain("http://example.org", ics);
        Assert.DoesNotContain("javascript", ics);
        Assert.Equal(25, ics.Split("ATTACH").Length - 1);
    }

    [Fact]
    [Trait("TestCase", "T305")]
    public void ALongAddressIsFoldedAt75BytesAndReadsBackWhole()
    {
        var url = "https://example.org/" + new string('a', 300) + ".pdf";
        var ics = IcsWriter.Write([Ev(new EventAttachment(url, "lungo"))]);
        Assert.All(ics.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.Equal(url, IcsParser.Analyze(ics, "c1").Events.Single().Attachments!.Single().Url);
    }

    // ---- T306: .ics, reading ----

    private static IcsImport Read(params string[] attach) => IcsParser.Analyze(
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:a@x\r\nDTSTART:20261014T080000\r\nDTEND:20261014T090000\r\nSUMMARY:Con allegati\r\n"
        + string.Concat(attach.Select(a => a + "\r\n")) + "END:VEVENT\r\nEND:VCALENDAR\r\n", "c1");

    [Fact]
    [Trait("TestCase", "T306")]
    public void AnAttachWithAnHttpsAddressIsRead()
    {
        var read = Read("ATTACH;FMTTYPE=image/jpeg;FILENAME=logo.jpg:" + Image);
        var a = Assert.Single(Assert.Single(read.Events).Attachments!);
        Assert.Equal(new EventAttachment(Image, "logo.jpg", "image/jpeg"), a);
        Assert.Empty(read.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T306")]
    public void TheFormatGoogleWritesIsRead()
    {
        var read = Read("ATTACH;FILENAME=\"Scheda, 2026.pdf\";FMTTYPE=application/pdf:" + Drive);
        var a = Assert.Single(Assert.Single(read.Events).Attachments!);
        Assert.Equal(("Scheda, 2026.pdf", "application/pdf", Drive), (a.Title, a.MimeType, a.Url));
    }

    [Fact]
    [Trait("TestCase", "T306")]
    public void WithoutParametersTheNameAndTheTypeComeFromTheAddress()
    {
        var a = Assert.Single(Assert.Single(Read("ATTACH:" + Feeds + "ggcal_basic.ics").Events).Attachments!);
        Assert.Equal(("ggcal_basic.ics", "text/calendar"), (a.Title, a.MimeType));
    }

    [Theory]
    [Trait("TestCase", "T306")]
    [InlineData("ATTACH:http://example.org/a.pdf")]
    [InlineData("ATTACH:ftp://example.org/a.pdf")]
    [InlineData("ATTACH:javascript:alert(1)")]
    [InlineData("ATTACH:file:///C:/a.pdf")]
    [InlineData("ATTACH:data:text/plain;base64,QQ==")]
    [InlineData("ATTACH:cid:part1@example.org")]
    [InlineData("ATTACH;ENCODING=BASE64;VALUE=BINARY:QUJD")]
    [InlineData("ATTACH:")]
    [InlineData("ATTACH:https://user:pw@example.org/a.pdf")]
    public void AnAttachThatIsNotAnHttpsLinkIsIgnoredAndTheUserIsTold(string line)
    {
        var read = Read(line);
        Assert.Empty(Assert.Single(read.Events).Attachments!);
        Assert.Contains(read.Report.Issues, i => i.Code == IcsIssueCode.AttachIgnored);
        Assert.NotEmpty(read.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T306")]
    public void OnlyTheValidOnesAreKeptAndTheSameAddressTwiceCountsOnce()
    {
        var read = Read("ATTACH:" + Image, "ATTACH:http://example.org/a.pdf", "ATTACH:" + Image, "ATTACH:" + Feeds + "ggcal_apple.ics");
        Assert.Equal([Image, Feeds + "ggcal_apple.ics"], Assert.Single(read.Events).Attachments!.Select(a => a.Url));
        Assert.Contains(read.Report.Issues, i => i.Code == IcsIssueCode.AttachIgnored);
    }

    [Fact]
    [Trait("TestCase", "T306")]
    public void MoreThan25AreCutAndTheUserIsTold()
    {
        var read = Read(Enumerable.Range(0, 27).Select(i => $"ATTACH:https://example.org/{i}.txt").ToArray());
        Assert.Equal(25, Assert.Single(read.Events).Attachments!.Count);
        Assert.Contains(read.Report.Issues, i => i.Code == IcsIssueCode.AttachIgnored);
    }

    [Fact]
    [Trait("TestCase", "T306")]
    public void ExportingAndImportingAgainGivesTheSameAttachments()
    {
        var items = new[]
        {
            new EventAttachment(Image, "youtube_logo.jpg", "image/jpeg"),
            new EventAttachment(Feeds + "ggcal_basic.ics", "Calendario, base: prova", "text/calendar"),
            new EventAttachment(Drive, "File di Drive"),
        };

        var back = IcsParser.Analyze(IcsWriter.Write([Ev(items)]), "c1");

        Assert.Equal(items, Assert.Single(back.Events).Attachments!.Select(a => a with { MimeType = a.MimeType }).ToArray());
        Assert.Empty(back.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T306")]
    public void AnEventWithoutAttachLinesHasAKnownEmptyList() => Assert.Empty(Assert.Single(Read().Events).Attachments!);

    // ---- T307: stored ----

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    [Trait("TestCase", "T307")]
    public void AnEventStoredBeforeTheAttachmentsHasThemUnread()
    {
        var old = JsonSerializer.Serialize(new CalendarEvent("e1", "c1", "x", Start, Start.AddHours(1)), Json);
        var node = JsonNode.Parse(old)!.AsObject();
        node.Remove("Attachments");

        var read = JsonSerializer.Deserialize<CalendarEvent>(node.ToJsonString(), Json)!;

        Assert.Null(read.Attachments);
    }

    [Fact]
    [Trait("TestCase", "T307")]
    public void TheAttachmentsSurviveTheStoreAndTheQueue()
    {
        var path = Path.Combine(Path.GetTempPath(), "ggcal-att-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new CalendarStore(path);
            store.UpsertCalendar(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"));
            var ev = Ev(new EventAttachment(Image, "youtube_logo.jpg", "image/jpeg", null, "icon"), new EventAttachment(Drive, "drive", null, "1RYr"));
            store.UpsertEvent(ev);
            store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Update, ev);

            Assert.Equal(ev.Attachments, store.GetEvent("e1")!.Attachments);
            Assert.Equal(ev.Attachments, CalendarStore.ReadSnapshot(Assert.Single(store.GetPending()))!.Attachments);
            Assert.Equal(ev.Attachments, new CalendarStore(path).GetAllEvents().Single().Attachments);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) { try { File.Delete(f); } catch (IOException) { } }
        }
    }

    // ---- T311: search ----

    private static CalendarEvent WithAttachments(string id, string title, params EventAttachment[] attachments) =>
        new(id, "c1", title, new DateTime(2026, 10, 26, 10, 0, 0), new DateTime(2026, 10, 26, 11, 0, 0)) { Reminders = [], Attachments = attachments };

    private static readonly DateTime Today = new(2026, 10, 25);

    private static SearchResult Find(IEnumerable<CalendarEvent> events, SearchQuery q) => EventSearch.Search(events, q, Today);

    private static string[] Ids(SearchResult r) => r.Hits.Select(h => h.Event.Id).Distinct().ToArray();

    [Fact]
    [Trait("TestCase", "T311")]
    public void TheSimpleBoxFindsTheTitleOfAnAttachment()
    {
        var events = new[] { WithAttachments("a", "Riunione", new EventAttachment(Image, "Logo del canale")), WithAttachments("b", "Altro") };
        var found = Find(events, new SearchQuery { Text = "canale" });
        Assert.Equal(["a"], Ids(found));
        Assert.Contains(SearchField.Attachments, found.Hits[0].Fields);
        Assert.Equal("Logo del canale", found.Hits[0].Snippet);
    }

    [Fact]
    [Trait("TestCase", "T311")]
    public void TheNameOfTheFileIsFoundEvenWithAnotherTitle()
    {
        var events = new[] { WithAttachments("a", "Riunione", new EventAttachment(Image, "Il logo")) };
        Assert.Equal(["a"], Ids(Find(events, new SearchQuery { Text = "youtube_logo" })));
        Assert.Equal(["a"], Ids(Find(events, new SearchQuery { Text = "youtube_logo.jpg" })));
    }

    [Theory]
    [Trait("TestCase", "T311")]
    [InlineData("altervista")]
    [InlineData("https")]
    [InlineData("GGCalendarAndroid")]
    [InlineData("1RYr0t3ysAVsezZRuy")]
    public void TheAddressOfTheLinkIsNotSearched(string word)
    {
        var events = new[] { WithAttachments("a", "Riunione", new EventAttachment(Feeds + "ggcal_basic.ics", "Base"), new EventAttachment(Drive, "Drive")) };
        Assert.Empty(Ids(Find(events, new SearchQuery { Text = word })));
    }

    [Fact]
    [Trait("TestCase", "T311")]
    public void WhatAndNotLookInTheAttachmentsToo()
    {
        var events = new[] { WithAttachments("a", "Riunione", new EventAttachment(Image, "Planimetria")), WithAttachments("b", "Altro") };
        Assert.Equal(["a"], Ids(Find(events, new SearchQuery { What = "planimetria" })));
        var both = new[] { events[0], WithAttachments("c", "Riunione due") };
        Assert.Equal(["c"], Ids(Find(both, new SearchQuery { Text = "riunione", Not = "planimetria" })));
    }

    [Fact]
    [Trait("TestCase", "T311")]
    public void TheBoxOfTheTitleAndTheOneOfTheDescriptionDoNotLookInTheAttachments()
    {
        var events = new[] { WithAttachments("a", "Riunione", new EventAttachment(Image, "Planimetria")) };
        Assert.Empty(Ids(Find(events, new SearchQuery { Title = "planimetria" })));
        Assert.Empty(Ids(Find(events, new SearchQuery { Description = "planimetria" })));
    }

    [Fact]
    [Trait("TestCase", "T311")]
    public void AccentsAndCapitalsDoNotCountInTheAttachments()
    {
        var events = new[] { WithAttachments("a", "Riunione", new EventAttachment("https://example.org/Città.PDF", "Pianta della CITTÀ")) };
        Assert.Equal(["a"], Ids(Find(events, new SearchQuery { Text = "citta" })));
    }

    [Fact]
    [Trait("TestCase", "T311")]
    public void TheDescriptionIsShownInTheSnippetWhenTheWordIsThereToo()
    {
        var ev = WithAttachments("a", "Riunione", new EventAttachment(Image, "Planimetria")) with { Description = "La planimetria è allegata" };
        var hit = Find([ev], new SearchQuery { Text = "planimetria" }).Hits[0];
        Assert.Equal([SearchField.Description, SearchField.Attachments], hit.Fields);
        Assert.Contains("allegata", hit.Snippet);
    }

    [Fact]
    [Trait("TestCase", "T311")]
    public void UnreadAttachmentsAreNotAProblemForTheSearch()
    {
        var ev = new CalendarEvent("a", "c1", "Riunione", new DateTime(2026, 10, 26, 10, 0, 0), new DateTime(2026, 10, 26, 11, 0, 0)) { Attachments = null };
        Assert.Equal(["a"], Ids(Find([ev], new SearchQuery { Text = "riunione" })));
    }
}
