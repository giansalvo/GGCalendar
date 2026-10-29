using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The attachments in the editor of an event (T303) and in its card (T304): no Google, no data of the app.</summary>
public class AttachmentUiTests
{
    private const string Image = "https://profgusinu.altervista.org/img/youtube_logo.jpg";
    private const string Feeds = "https://profgusinu.altervista.org/GGCalendarAndroid/feeds/";
    private static readonly DateTime Start = new(2026, 10, 14, 9, 0, 0);

    private static CalendarEvent Ev(IReadOnlyList<EventAttachment>? attachments) =>
        new("e1", "c1", "Riunione", Start, Start.AddHours(1)) { Reminders = [], Attachments = attachments };

    private static EventEditViewModel Editor(CalendarEvent ev) =>
        new(ev, [new CalendarItemViewModel(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"), () => { })], false, new AppSettings());

    // ---- T303: the editor ----

    [Fact]
    [Trait("TestCase", "T303")]
    public void APastedLinkIsAddedWithTheNameOfTheFileAsTitle()
    {
        var editor = Editor(Ev([]));
        editor.NewAttachmentUrl = "  " + Image + " ";
        editor.AddAttachmentCommand.Execute(null);

        var added = Assert.Single(editor.Attachments);
        Assert.Equal(new EventAttachment(Image, "youtube_logo.jpg", "image/jpeg"), added);
        Assert.Equal(("", ""), (editor.NewAttachmentUrl, editor.NewAttachmentTitle));
        Assert.True(editor.HasAttachments);
        Assert.Equal("", editor.AttachmentError);
        Assert.True(editor.CanAddAttachment);
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public void TheTitleTypedIsKept()
    {
        var editor = Editor(Ev([]));
        editor.NewAttachmentUrl = Feeds + "ggcal_basic.ics";
        editor.NewAttachmentTitle = "Il calendario di prova";
        editor.AddAttachmentCommand.Execute(null);
        Assert.Equal("Il calendario di prova", Assert.Single(editor.Attachments).Title);
    }

    [Theory]
    [Trait("TestCase", "T303")]
    [InlineData("http://profgusinu.altervista.org/img/youtube_logo.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("www.example.org/a.pdf")]
    [InlineData("https://user:pw@example.org/a.pdf")]
    [InlineData("https://example.org/a b.pdf")]
    public void AnAddressThatIsNotHttpsIsRefusedWithAMessageAndKeptInTheBox(string url)
    {
        var editor = Editor(Ev([]));
        editor.NewAttachmentUrl = url;
        editor.AddAttachmentCommand.Execute(null);

        Assert.Empty(editor.Attachments);
        Assert.NotEqual("", editor.AttachmentError);
        Assert.Equal(url, editor.NewAttachmentUrl);
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public void TheSameAddressTwiceIsOneAttachment()
    {
        var editor = Editor(Ev([]));
        foreach (var _ in new[] { 1, 2 })
        {
            editor.NewAttachmentUrl = Image;
            editor.AddAttachmentCommand.Execute(null);
        }

        Assert.Single(editor.Attachments);
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public void TheLimitIs25()
    {
        var editor = Editor(Ev(Enumerable.Range(0, 25).Select(i => new EventAttachment($"https://example.org/{i}.txt", $"n{i}")).ToList()));
        editor.NewAttachmentUrl = Image;
        editor.AddAttachmentCommand.Execute(null);

        Assert.Equal(25, editor.Attachments.Count);
        Assert.False(editor.CanAddAttachment);
        Assert.NotEqual("", editor.AttachmentError);
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public void AnAttachmentIsRemoved()
    {
        var editor = Editor(Ev([new EventAttachment(Image, "a"), new EventAttachment(Feeds + "ggcal_basic.ics", "b")]));
        editor.RemoveAttachmentCommand.Execute(editor.Attachments[0]);

        Assert.Equal("b", Assert.Single(editor.Attachments).Title);
        Assert.Equal(["b"], editor.TryBuild()!.Attachments!.Select(a => a.Title));
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public void SavingWithoutTouchingTheAttachmentsLeavesThemAsTheyWere()
    {
        var kept = new[] { new EventAttachment(Image, "a", "image/jpeg", null, "icon"), new EventAttachment("https://drive.google.com/open?id=abc", "b", null, "abc") };
        Assert.Equal(kept, Editor(Ev(kept)).TryBuild()!.Attachments);
        Assert.Empty(Editor(Ev([])).TryBuild()!.Attachments!);
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public void AnEventWhoseAttachmentsWereNeverReadStaysUnreadUntilThePersonChangesThem()
    {
        var editor = Editor(Ev(null));
        Assert.Null(editor.TryBuild()!.Attachments); // saving the title must not send an empty list to Google

        editor.NewAttachmentUrl = Image;
        editor.AddAttachmentCommand.Execute(null);
        Assert.Equal([Image], editor.TryBuild()!.Attachments!.Select(a => a.Url));
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public void AnAddressLeftInTheBoxIsAddedWhenTheEventIsSaved()
    {
        var editor = Editor(Ev([]));
        editor.NewAttachmentUrl = Image;
        Assert.Equal([Image], editor.TryBuild()!.Attachments!.Select(a => a.Url));

        var wrong = Editor(Ev([]));
        wrong.NewAttachmentUrl = "http://example.org/a.txt";
        Assert.Null(wrong.TryBuild()); // not saved: the person is told
        Assert.NotEqual("", wrong.Error);
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public Task TheWindowAddsRemovesAndSavesTheAttachments() => HeadlessApp.RunAsync(async () =>
    {
        var owner = new Window();
        owner.Show();
        var dialog = new EventEditWindow { DataContext = Editor(Ev([new EventAttachment(Feeds + "ggcal_basic.ics", "ggcal_basic.ics", "text/calendar")])) };
        var closed = dialog.ShowDialog<EventEditResult?>(owner);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var editor = (EventEditViewModel)dialog.DataContext!;
            Assert.Single(dialog.GetVisualDescendants().OfType<Button>().Where(b => AutomationIdOf(b) == "Edit.Attachment.Remove"));

            Text(dialog, "Edit.AttachmentUrl").Text = Image;
            Click(FindButton(dialog, "Edit.AttachmentAdd"));
            Dispatcher.UIThread.RunJobs();
            var box = Text(dialog, "Edit.AttachmentUrl").Text;
            Assert.True(editor.Attachments.Count == 2, $"Error='{editor.Error}' Url='{editor.NewAttachmentUrl}' Box='{box}'");
            Assert.Equal(2, dialog.GetVisualDescendants().OfType<Button>().Count(b => AutomationIdOf(b) == "Edit.Attachment.Remove"));

            Text(dialog, "Edit.AttachmentUrl").Text = "http://example.org/a.png";
            Click(FindButton(dialog, "Edit.AttachmentAdd"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, editor.Attachments.Count);
            Assert.NotEqual("", editor.AttachmentError);
            Assert.True(Error(dialog).IsVisible); // the reason is shown under the boxes, not only at the bottom of the window

            Text(dialog, "Edit.AttachmentUrl").Text = "";
            var first = dialog.GetVisualDescendants().OfType<Button>().First(b => AutomationIdOf(b) == "Edit.Attachment.Remove");
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Image, Assert.Single(editor.Attachments).Url);

            Click(FindButton(dialog, "Edit.Save"));
            var result = await WaitForAsync(() => closed.IsCompleted ? closed.Result : null, "la chiusura dell'editor");
            Assert.Equal([Image], result.Event!.Attachments!.Select(a => a.Url));
        }
        finally
        {
            owner.Close();
        }
    });

    private static string? AutomationIdOf(Control c) => Avalonia.Automation.AutomationProperties.GetAutomationId(c);

    private static TextBlock Error(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().First(t => AutomationIdOf(t) == "Edit.AttachmentError");

    private static List<Button> Buttons(Visual root, string automationId) =>
        root.GetVisualDescendants().OfType<Button>().Where(b => AutomationIdOf(b) == automationId).ToList();

    private static async Task InEditorAsync(CalendarEvent ev, Func<Window, EventEditWindow, EventEditViewModel, Task> test)
    {
        var owner = new Window();
        owner.Show();
        var dialog = new EventEditWindow { DataContext = Editor(ev) };
        _ = dialog.ShowDialog<EventEditResult?>(owner);
        Dispatcher.UIThread.RunJobs();
        try
        {
            await test(owner, dialog, (EventEditViewModel)dialog.DataContext!);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [Fact]
    [Trait("TestCase", "T303")]
    public Task AddingAnAttachmentShowsANewRowAndKeepsARowToAddAnotherOne() => HeadlessApp.RunAsync(() => InEditorAsync(Ev([]), async (_, dialog, editor) =>
    {
        Assert.Empty(Buttons(dialog, "Edit.Attachment.Remove"));
        var add = FindButton(dialog, "Edit.AttachmentAdd");
        Assert.True(add.IsEffectivelyVisible);

        Text(dialog, "Edit.AttachmentUrl").Text = Image;
        Click(add);
        await PauseAsync(TimeSpan.FromMilliseconds(100));

        Assert.Single(Buttons(dialog, "Edit.Attachment.Remove")); // the new row
        Assert.Equal("youtube_logo.jpg", ((TextBlock)Assert.Single(Buttons(dialog, "Edit.Attachment.Open")).Content!).Text);
        Assert.True(FindButton(dialog, "Edit.AttachmentAdd").IsEffectivelyVisible); // the row for the next one is still there
        Assert.Equal("", Text(dialog, "Edit.AttachmentUrl").Text ?? "");
        Assert.True(Text(dialog, "Edit.AttachmentUrl").IsFocused); // ready to type the next address

        Text(dialog, "Edit.AttachmentUrl").Text = Feeds + "ggcal_basic.ics";
        Click(FindButton(dialog, "Edit.AttachmentAdd"));
        Assert.Equal(2, Buttons(dialog, "Edit.Attachment.Remove").Count);
        Assert.Equal(2, editor.Attachments.Count);
    }));

    [Fact]
    [Trait("TestCase", "T303")]
    public Task AtTheLimitThereIsNoRowToAddAnotherOne() => HeadlessApp.RunAsync(() => InEditorAsync(
        Ev(Enumerable.Range(0, 24).Select(i => new EventAttachment($"https://example.org/{i}.txt", $"n{i}")).ToList()), async (_, dialog, editor) =>
    {
        Assert.True(FindButton(dialog, "Edit.AttachmentAdd").IsEffectivelyVisible);
        Text(dialog, "Edit.AttachmentUrl").Text = Image;
        Click(FindButton(dialog, "Edit.AttachmentAdd"));
        await PauseAsync(TimeSpan.FromMilliseconds(100));

        Assert.Equal(25, Buttons(dialog, "Edit.Attachment.Remove").Count);
        Assert.False(FindButton(dialog, "Edit.AttachmentAdd").IsEffectivelyVisible);
        Assert.False(Text(dialog, "Edit.AttachmentUrl").IsEffectivelyVisible);

        Click(Buttons(dialog, "Edit.Attachment.Remove")[0]); // room again
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        Assert.True(FindButton(dialog, "Edit.AttachmentAdd").IsEffectivelyVisible);
        Assert.Equal(24, editor.Attachments.Count);
    }));

    [Fact]
    [Trait("TestCase", "T303")]
    public Task AnAttachmentAlreadyAddedCanBeClickedAndAsksBeforeOpening() => HeadlessApp.RunAsync(() => InEditorAsync(
        Ev([new EventAttachment(Image, "Il logo"), new EventAttachment("http://example.org/vecchio.pdf", "Vecchio")]), async (_, dialog, _) =>
    {
        var open = Buttons(dialog, "Edit.Attachment.Open");
        Assert.Equal(2, open.Count);
        Assert.Equal("Il logo", ((TextBlock)open[0].Content!).Text);
        Assert.Equal(Image, ToolTip.GetTip(open[0]));

        Click(open[1]); // not https: nothing happens
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        Assert.Empty(dialog.OwnedWindows);

        Click(open[0]);
        var question = await WaitForAsync(() => dialog.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la domanda prima di aprire il link");
        Assert.Contains(question.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } text && text.Contains(Image));
        question.Close(false); // the person says no: the browser is not opened
    }));

    private static TextBox Text(Visual root, string automationId) =>
        root.GetVisualDescendants().OfType<TextBox>().First(t => AutomationIdOf(t) == automationId);

    // ---- T304: the card ----

    private static Task InCardAsync(CalendarEvent ev, Func<EventDetailsCard, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var card = new EventDetailsCard(ev, null, "#1A73E8", "Calendario", "Account", use24Hour: true, dark: false);
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

    private static List<Button> Links(EventDetailsCard card) =>
        card.GetVisualDescendants().OfType<Button>().Where(b => AutomationIdOf(b) == "Details.Attachment").ToList();

    [Fact]
    [Trait("TestCase", "T304")]
    public Task TheCardListsTheAttachmentsByTitle() => InCardAsync(
        Ev([new EventAttachment(Image, "Il logo"), new EventAttachment(Feeds + "ggcal_basic.ics", "ggcal_basic.ics")]), card =>
        {
            Assert.Single(card.GetVisualDescendants().OfType<StackPanel>().Where(p => AutomationIdOf(p) == "Details.Attachments"));
            var links = Links(card);
            Assert.Equal(2, links.Count);
            Assert.Equal(["Il logo", "ggcal_basic.ics"], links.Select(b => ((TextBlock)b.Content!).Text));
            Assert.Equal(Image, ToolTip.GetTip(links[0]));
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T304")]
    public Task AClickAsksToOpenTheRealAddress() => InCardAsync(
        Ev([new EventAttachment(Image, "Il logo"), new EventAttachment(Feeds + "ggcal_basic.ics", "Base")]), card =>
        {
            var asked = new List<Uri>();
            card.LinkRequested += asked.Add;

            Links(card)[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Links(card)[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal([Feeds + "ggcal_basic.ics", Image], asked.Select(u => u.AbsoluteUri));
            return Task.CompletedTask;
        });

    [Theory]
    [Trait("TestCase", "T304")]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnEventWithoutAttachmentsOrUnreadShowsNoSection(bool unread) => InCardAsync(Ev(unread ? null : []), card =>
    {
        Assert.Empty(Links(card));
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<StackPanel>(), p => AutomationIdOf(p) == "Details.Attachments");
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T304")]
    public Task ALinkThatIsNotHttpsIsShownButCannotBeClicked() => InCardAsync(
        Ev([new EventAttachment("http://example.org/vecchio.pdf", "Vecchio"), new EventAttachment(Image, "Nuovo")]), card =>
        {
            var links = Links(card);
            Assert.Equal("Nuovo", ((TextBlock)Assert.Single(links).Content!).Text);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Vecchio");
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T304")]
    public Task AnAttachmentWithoutTitleIsShownByTheNameOfTheFile() => InCardAsync(
        Ev([new EventAttachment(Image, "")]), card => { Assert.Equal("youtube_logo.jpg", ((TextBlock)Assert.Single(Links(card)).Content!).Text); return Task.CompletedTask; });
}
