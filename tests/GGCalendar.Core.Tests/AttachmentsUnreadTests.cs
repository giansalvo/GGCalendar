using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// An event of Google whose attachments were never read (issue #50): the editor does not let a link be added, because it would be sent alone and
/// replace the ones Google has. An event of this PC, a new one and one that was read are not affected.
/// </summary>
public class AttachmentsUnreadTests
{
    private const string Image = "https://profgusinu.altervista.org/img/youtube_logo.jpg";
    private const string GoogleCalendarId = "me@example.com|me@example.com";
    private static readonly DateTime Start = new(2026, 10, 14, 9, 0, 0);

    private static CalendarEvent Ev(string calendarId, IReadOnlyList<EventAttachment>? attachments) =>
        new("e1", calendarId, "Riunione", Start, Start.AddHours(1)) { Reminders = [], Attachments = attachments };

    private static EventEditViewModel Editor(CalendarEvent ev, bool isNew = false) =>
        new(ev, [new CalendarItemViewModel(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"), () => { }),
                 new CalendarItemViewModel(new CalendarInfo(GoogleCalendarId, "me@example.com", "Principale", "#1A73E8"), () => { })], isNew, new AppSettings());

    [Fact]
    [Trait("TestCase", "T351")]
    public void AnUnreadEventOfGoogleCannotGetALink()
    {
        var editor = Editor(Ev(GoogleCalendarId, null));
        Assert.True(editor.AttachmentsUnread);
        Assert.False(editor.CanAddAttachment);

        editor.NewAttachmentUrl = Image;
        editor.AddAttachmentCommand.Execute(null);

        Assert.Empty(editor.Attachments);
        Assert.Null(editor.TryBuild()!.Attachments); // saved without attachments: nothing is sent to Google about them
    }

    [Theory]
    [Trait("TestCase", "T352")]
    [InlineData("c1", null, false)]            // an event of this PC has nothing to lose
    [InlineData(GoogleCalendarId, 0, false)]   // read, with none
    [InlineData(GoogleCalendarId, 2, false)]   // read, with some
    public void AnEventThatIsNotUnreadOnGoogleCanGetALink(string calendar, int? count, bool unread)
    {
        var attachments = count is null ? null : Enumerable.Range(0, count.Value).Select(i => new EventAttachment($"https://example.org/{i}.txt", $"n{i}")).ToList();
        var editor = Editor(Ev(calendar, attachments));
        Assert.Equal(unread, editor.AttachmentsUnread);
        Assert.True(editor.CanAddAttachment);

        editor.NewAttachmentUrl = Image;
        editor.AddAttachmentCommand.Execute(null);
        Assert.Equal((count ?? 0) + 1, editor.Attachments.Count);
    }

    [Fact]
    [Trait("TestCase", "T352")]
    public void ANewEventCanGetALink()
    {
        var editor = Editor(Ev(GoogleCalendarId, null), isNew: true);
        Assert.False(editor.AttachmentsUnread);
        Assert.True(editor.CanAddAttachment);
    }

    [Theory]
    [Trait("TestCase", "T351")]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheWindowShowsTheMessageAndHidesTheBoxesOnlyForAnUnreadEvent(bool unread) => HeadlessApp.RunAsync(async () =>
    {
        var language = L.Language;
        var owner = new Window();
        owner.Show();
        var editor = Editor(Ev(GoogleCalendarId, unread ? null : []));
        var dialog = new EventEditWindow { DataContext = editor };
        _ = dialog.ShowDialog<EventEditResult?>(owner);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var message = dialog.GetVisualDescendants().OfType<TextBlock>().First(t => Avalonia.Automation.AutomationProperties.GetAutomationId(t) == "Edit.AttachmentsUnread");
            Assert.Equal(unread, message.IsEffectivelyVisible);
            Assert.Equal(L.T("edit.attachments_unread"), message.Text);
            var url = dialog.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => Avalonia.Automation.AutomationProperties.GetAutomationId(t) == "Edit.AttachmentUrl");
            Assert.Equal(!unread, url?.IsEffectivelyVisible ?? false);
            var add = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Edit.AttachmentAdd");
            Assert.Equal(!unread, add?.IsEffectivelyVisible ?? false);
            await PauseAsync(TimeSpan.FromMilliseconds(50));
        }
        finally
        {
            dialog.Close();
            owner.Close();
            L.Apply(language);
        }
    });

    [Fact]
    [Trait("TestCase", "T353")]
    public async Task EditingAnUnreadEventOnGoogleThroughTheEditorKeepsTheAttachmentsOfGoogle()
    {
        using var rig = new SyncRig();
        var original = rig.AddLocal(SyncRig.NewEvent("e1", "Vecchio"));
        await rig.SyncOkAsync();
        var id = Assert.Single(rig.Google.Ids);
        rig.Google.SetAttachments(id, (Image, "youtube_logo.jpg"), ("https://example.org/verbale.pdf", "Verbale"));
        var unread = original with { Attachments = null }; // as it was stored before the attachments existed
        rig.Store.UpsertEvent(unread);

        var editor = Editor(unread);
        Assert.True(editor.AttachmentsUnread);
        editor.NewAttachmentUrl = "https://example.org/nuovo.pdf"; // the person tries to add one...
        editor.AddAttachmentCommand.Execute(null);
        editor.Title = "Nuovo titolo"; // ...and edits the title
        var saved = editor.TryBuild()!;
        rig.Edit(unread, saved);
        await rig.SyncOkAsync();

        Assert.Equal("Nuovo titolo", rig.Google.Summary(id));
        Assert.Equal(2, rig.Google.AttachmentsOf(id)!.Count); // the two of Google are still there
    }

    [Fact]
    [Trait("TestCase", "T351")]
    public void TheMessageIsInEveryLanguage()
    {
        // Read from the tables, not through the language of the app: other tests change it while this one runs.
        var texts = new[] { "it", "en", "de", "es", "fr" }.Select(code => L.Texts(code)["edit.attachments_unread"]).ToList();
        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Equal(5, texts.Distinct().Count());
    }
}
