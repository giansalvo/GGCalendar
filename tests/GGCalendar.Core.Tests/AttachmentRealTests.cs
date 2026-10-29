using System.Text.Json;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The attachments of an event, from the editor of the app to the test Google account and back (T308). The links are only stored: Google never opens them.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class AttachmentRealTests
{
    private const string Image = "https://profgusinu.altervista.org/img/youtube_logo.jpg";
    private const string Feeds = "https://profgusinu.altervista.org/GGCalendarAndroid/feeds/";
    private const string Drive = "https://drive.google.com/open?id=1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe&authuser=5";

    private static List<(string Url, string Title, string? FileId)> OnGoogle(JsonElement e) =>
        e.TryGetProperty("attachments", out var list)
            ? list.EnumerateArray().Select(a => (a.GetProperty("fileUrl").GetString()!, a.GetProperty("title").GetString()!,
                a.TryGetProperty("fileId", out var id) ? id.GetString() : null)).ToList()
            : [];

    /// <summary>The attachments on Google once they are the ones expected: Google may take a few seconds to show a change, so it asks again (at most 30 seconds).</summary>
    private static async Task<List<(string Url, string Title, string? FileId)>> OnGoogleAsync(TestGoogle google, string remoteId, Func<List<(string Url, string Title, string? FileId)>, bool> expected)
    {
        var found = OnGoogle(await google.GetAsync(remoteId));
        for (var i = 0; i < 10 && !expected(found); i++)
        {
            await PauseAsync(TimeSpan.FromSeconds(3));
            found = OnGoogle(await google.GetAsync(remoteId));
        }

        return found;
    }

    private static async Task SaveAsync(MainWindow window, EventEditWindow dialog)
    {
        Click(FindButton(dialog, "Edit.Save"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura dell'editor");
    }

    [Fact]
    [Trait("TestCase", "T308")]
    public Task T308_AttachmentsGoToGoogleStayAfterAnEditAndComeBackFromGoogle() => InAppAsync(async window =>
    {
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title)
        {
            Attachments = [(Image, ""), (Feeds + "ggcal_basic.ics", "Calendario di prova"), (Drive, "File di Drive")],
        });
        var day = created.Start.Date;
        Assert.Equal(3, created.Attachments!.Count);
        await SyncAsync(window);
        var remoteId = TestGoogle.RemoteIdOf(created.Id) ?? throw new Xunit.Sdk.XunitException("L'evento non è stato inviato a Google.");

        // 1. The three links are on Google: the title is the name of the file when none was typed, and Google knows the file of Drive.
        using var google = await TestGoogle.ConnectAsync();
        var sent = await OnGoogleAsync(google, remoteId, l => l.Count == 3);
        Assert.Equal([(Image, "youtube_logo.jpg", (string?)null), (Feeds + "ggcal_basic.ics", "Calendario di prova", null), (Drive, "File di Drive", "1RYr0t3ysAVsezZRuy_gJPsohtNvzVUYe")], sent);

        // 2. The person only changes the title: the attachments are not touched, on Google nor in the editor, also after closing and opening the app.
        var (dialog, editor) = await OpenEditorAsync(window, title, day);
        Assert.Equal(3, editor.Attachments.Count);
        await RetitleAsync(window, dialog, title + " B");
        await SyncAsync(window);
        Assert.Equal(sent.Select(a => a.Url), (await OnGoogleAsync(google, remoteId, l => l.Count == 3)).Select(a => a.Url));
        window = await ReopenAsync(window);
        (dialog, editor) = await OpenEditorAsync(window, title + " B", day);
        Assert.Equal(sent.Select(a => a.Url), editor.Attachments.Select(a => a.Url));

        // 3. One attachment is removed in the editor: Google keeps the other two.
        editor.RemoveAttachmentCommand.Execute(editor.Attachments[0]);
        await SaveAsync(window, dialog);
        await SyncAsync(window);
        Assert.Equal([Feeds + "ggcal_basic.ics", Drive], (await OnGoogleAsync(google, remoteId, l => l.Count == 2)).Select(a => a.Url));

        // 4. Attachments put on Google (as the web page does) come to the app with the next sync.
        await google.SetAttachmentsAsync(remoteId, (Feeds + "ggcal_apple.ics", "File in stile Apple"), (Image, "Logo"));
        await SyncAsync(window);
        (dialog, editor) = await OpenEditorAsync(window, title + " B", day);
        Assert.Equal(new[] { Feeds + "ggcal_apple.ics", Image }.Order(), editor.Attachments.Select(a => a.Url).Order()); // Google may list them in another order

        // 5. Removing them all removes them on Google.
        foreach (var a in editor.Attachments.ToList()) editor.RemoveAttachmentCommand.Execute(a);
        await SaveAsync(window, dialog);
        await SyncAsync(window);
        Assert.Empty(await OnGoogleAsync(google, remoteId, l => l.Count == 0));
    });
}
