using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Views;
using GGCalendar.Core.Hover;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Issue #91: the card with the details of an event opens when the mouse rests on it.</summary>
[Collection(CollectionName)]
public class HoverDetailsTests
{
    /// <summary>A clock that moves only when the test says so.</summary>
    private sealed class FakeClock : IHoverClock
    {
        private sealed class Timer(long due, Action action)
        {
            public long Due { get; } = due;
            public Action Action { get; } = action;
            public bool Cancelled { get; set; }
        }

        private readonly List<Timer> _timers = [];
        private long _now;

        public int Pending => _timers.Count(t => !t.Cancelled);

        public IDisposable After(TimeSpan delay, Action action)
        {
            var timer = new Timer(_now + (long)delay.TotalMilliseconds, action);
            _timers.Add(timer);
            return new Cancel(timer);
        }

        public void Advance(int ms)
        {
            var target = _now + ms;
            while (_timers.Where(t => !t.Cancelled && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault() is { } next)
            {
                next.Cancelled = true;
                _now = next.Due;
                next.Action();
            }

            _now = target;
        }

        private sealed class Cancel(Timer timer) : IDisposable
        {
            public void Dispose() => timer.Cancelled = true;
        }
    }

    private sealed class Rig
    {
        public FakeClock Clock { get; } = new();
        public List<string> Shown { get; } = [];
        public int Hidden { get; set; }
        public int OpenMs { get; set; } = 100;
        public int CloseMs { get; set; } = 100;
        public HoverIntent<string> Intent { get; }

        public Rig() => Intent = new HoverIntent<string>(() => TimeSpan.FromMilliseconds(OpenMs), () => TimeSpan.FromMilliseconds(CloseMs), Clock, Shown.Add, () => Hidden++);
    }

    [Fact]
    [Trait("TestCase", "T388")]
    public void T388_TheCardOpensOnlyAfterTheMouseHasRestedForTheOpeningDelay()
    {
        var r = new Rig();
        r.Intent.Enter("a", "A");
        r.Clock.Advance(99);
        Assert.Empty(r.Shown);

        r.Clock.Advance(1);
        Assert.Equal(["A"], r.Shown);
        Assert.True(r.Intent.IsShown);
        Assert.False(r.Intent.IsPinned);
    }

    [Fact]
    [Trait("TestCase", "T388")]
    public void T388_AMouseThatPassesOverAnEventDoesNotOpenTheCard()
    {
        var r = new Rig();
        r.Intent.Enter("a", "A");
        r.Clock.Advance(60);
        r.Intent.Leave();
        r.Clock.Advance(1000);

        Assert.Empty(r.Shown);
        Assert.Equal(0, r.Hidden); // nothing was open, nothing to close
        Assert.Equal(0, r.Clock.Pending);
    }

    [Fact]
    [Trait("TestCase", "T389")]
    public void T389_TheCardClosesAfterTheClosingDelayOnceTheMouseIsOnNeitherTheEventNorTheCard()
    {
        var r = new Rig();
        r.Intent.Enter("a", "A");
        r.Clock.Advance(100);
        r.Intent.Leave();
        r.Clock.Advance(99);
        Assert.Equal(0, r.Hidden);

        r.Clock.Advance(1);
        Assert.Equal(1, r.Hidden);
        Assert.False(r.Intent.IsShown);
    }

    [Fact]
    [Trait("TestCase", "T389")]
    public void T389_MovingTheMouseOntoTheCardKeepsItOpenSoItsButtonsCanBeReached()
    {
        var r = new Rig();
        r.Intent.Enter("a", "A");
        r.Clock.Advance(100);
        r.Intent.Leave();     // leaves the event...
        r.Clock.Advance(40);
        r.Intent.EnterCard(); // ...and reaches the card before the card goes away
        r.Clock.Advance(5000);
        Assert.Equal(0, r.Hidden);
        Assert.True(r.Intent.IsShown);

        r.Intent.LeaveCard(); // then leaves the card
        r.Clock.Advance(100);
        Assert.Equal(1, r.Hidden);
    }

    [Fact]
    [Trait("TestCase", "T389")]
    public void T389_GoingBackToTheEventFromTheCardKeepsTheCardAndOpensNothingNew()
    {
        var r = new Rig();
        r.Intent.Enter("a", "A");
        r.Clock.Advance(100);
        r.Intent.Leave();
        r.Intent.EnterCard();
        r.Intent.LeaveCard();
        r.Clock.Advance(50);
        r.Intent.Enter("a", "A"); // the same event again
        r.Clock.Advance(5000);

        Assert.Equal(["A"], r.Shown);
        Assert.Equal(0, r.Hidden);
    }

    [Fact]
    [Trait("TestCase", "T390")]
    public void T390_AnotherEventReplacesTheCardAfterTheOpeningDelayWithoutLeavingTheFirstOne()
    {
        var r = new Rig();
        r.Intent.Enter("a", "A");
        r.Clock.Advance(100);

        r.Intent.Leave(); // from A straight to B
        r.Intent.Enter("b", "B");
        r.Clock.Advance(99);
        Assert.Equal(["A"], r.Shown); // still the first one for a moment
        Assert.Equal(0, r.Hidden);

        r.Clock.Advance(1);
        Assert.Equal(["A", "B"], r.Shown);
        Assert.Equal(0, r.Hidden); // replaced, not closed and opened
        Assert.Equal("b", r.Intent.ShownKey);
        r.Clock.Advance(5000);
        Assert.Equal(0, r.Hidden);
    }

    [Fact]
    [Trait("TestCase", "T391")]
    public void T391_ACardOpenedByAClickStaysWhateverTheMouseDoes()
    {
        var r = new Rig();
        r.Intent.Pin("a");
        Assert.True(r.Intent.IsShown);
        Assert.True(r.Intent.IsPinned);

        r.Intent.Enter("b", "B");   // the mouse goes over another event
        r.Intent.Leave();
        r.Intent.EnterCard();
        r.Intent.LeaveCard();
        r.Clock.Advance(5000);

        Assert.Empty(r.Shown);
        Assert.Equal(0, r.Hidden);
        Assert.Equal("a", r.Intent.ShownKey);
    }

    [Fact]
    [Trait("TestCase", "T391")]
    public void T391_ClosingTheCardFromOutsideForgetsEverythingThatWasPending()
    {
        var r = new Rig();
        r.Intent.Enter("a", "A");
        r.Clock.Advance(100);
        r.Intent.Leave();            // a closing is pending...
        r.Intent.Dismissed();        // ...and the card is closed by Esc
        r.Clock.Advance(5000);
        Assert.Equal(0, r.Hidden);
        Assert.False(r.Intent.IsShown);
        Assert.Null(r.Intent.ShownKey);

        r.Intent.Pin("b");
        r.Intent.Dismissed();        // a click elsewhere closed the pinned card
        r.Intent.Enter("c", "C");    // the hover works again
        r.Clock.Advance(100);
        Assert.Equal(["A", "C"], r.Shown);
    }

    [Theory]
    [Trait("TestCase", "T392")]
    [InlineData(0)]
    [InlineData(-50)]
    public void T392_ADelayOfZeroOrLessSwitchesTheHoverOff(int openMs)
    {
        var r = new Rig { OpenMs = openMs };
        Assert.False(r.Intent.Enabled);
        r.Intent.Enter("a", "A");
        r.Clock.Advance(5000);

        Assert.Empty(r.Shown);
        Assert.Equal(0, r.Clock.Pending);

        r.Intent.Pin("a"); // a click still works
        Assert.True(r.Intent.IsShown);
    }

    [Fact]
    [Trait("TestCase", "T392")]
    public void T392_AClosingDelayOfZeroClosesAtOnce()
    {
        var r = new Rig { CloseMs = 0 };
        r.Intent.Enter("a", "A");
        r.Clock.Advance(100);
        r.Intent.Leave();
        r.Clock.Advance(0);

        Assert.Equal(1, r.Hidden);
    }

    [Fact]
    [Trait("TestCase", "T393")]
    public void T393_TheDelaysAre100AndAMissingKeyOfTheSettingsFileMeansTheDefault()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Assert.Equal(100, new AppSettings().HoverDetailsDelayMs);
            Assert.Equal(100, new AppSettings().HoverDetailsCloseDelayMs);

            var file = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(file, "{ \"DayStartHour\": 8 }"); // an old file without the new keys
            var old = new SettingsStore(file).Load();
            Assert.Equal(100, old.HoverDetailsDelayMs);
            Assert.Equal(100, old.HoverDetailsCloseDelayMs);
            Assert.Equal(TimeSpan.FromMilliseconds(100), old.HoverDetailsDelay);

            File.WriteAllText(file, "{ \"HoverDetailsDelayMs\": 350, \"HoverDetailsCloseDelayMs\": 20 }");
            var set = new SettingsStore(file).Load();
            Assert.Equal(TimeSpan.FromMilliseconds(350), set.HoverDetailsDelay);
            Assert.Equal(TimeSpan.FromMilliseconds(20), set.HoverDetailsCloseDelay);

            new SettingsStore(file).Save(new AppSettings()); // saved with the defaults: the keys are in the file to be edited
            var text = File.ReadAllText(file);
            Assert.Contains("\"HoverDetailsDelayMs\": 100", text);
            Assert.Contains("\"HoverDetailsCloseDelayMs\": 100", text);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    [Trait("TestCase", "T393")]
    public void T393_ANegativeOrHugeValueIsKeptInItsLimits()
    {
        Assert.Equal(TimeSpan.Zero, new AppSettings { HoverDetailsDelayMs = -5 }.HoverDetailsDelay); // off
        Assert.Equal(TimeSpan.Zero, new AppSettings { HoverDetailsCloseDelayMs = -5 }.HoverDetailsCloseDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), new AppSettings { HoverDetailsDelayMs = 99999 }.HoverDetailsDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), new AppSettings { HoverDetailsCloseDelayMs = 99999 }.HoverDetailsCloseDelay);
    }

    [Fact]
    [Trait("TestCase", "T394")]
    public Task T394_InTheAgendaTheCardOpensOnTheMouseOverInTheFreeLaneAtTheRightAndClosesWhenTheMouseLeaves() => HeadlessApp.RunAsync(async () =>
    {
        var window = OpenApp();
        Dictionary<string, bool>? states = null;
        var hoverDelay = 100;
        try
        {
            await Task.Yield();
            var vm = Vm(window);
            hoverDelay = vm.Settings.HoverDetailsDelayMs;
            states = vm.Calendars.ToDictionary(c => c.Id, c => c.IsVisible);
            var calendar = vm.CreateLocalCalendar(NewTitle());
            foreach (var c in vm.Calendars) c.IsVisible = c.Id == calendar;

            var day = DateTime.Today.AddDays(2);
            var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, NewTitle(), day.AddHours(10), day.AddHours(11), Location: "Aula 4", Description: "Portare il portatile");
            vm.AddEvent(ev);
            var row = await FindInAgendaAsync(window, ev.Title, day);
            var popup = window.FindControl<Popup>("DetailsPopup")!;
            var center = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            Dispatcher.UIThread.RunJobs();

            // the mouse rests on the event: the card opens by itself, with what the click shows
            window.MouseMove(center);
            await WaitForAsync(() => popup.IsOpen ? popup : null, "la scheda al passaggio del mouse");
            var card = Assert.IsType<EventDetailsCard>(popup.Child);
            Assert.False(popup.IsLightDismissEnabled); // it goes away by itself, a click is not needed
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == ev.Title);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Aula 4");

            // beside the list, in the lane left free for it: never over the events
            var list = window.FindControl<ItemsControl>("AgendaList")!;
            var scroll = window.FindControl<ScrollViewer>("AgendaScroll")!;
            Assert.True(list.Bounds.Right + 8 + EventDetailsCard.CardWidth <= scroll.Bounds.Width + 0.5,
                $"La scheda non entra a destra dell'elenco: elenco fino a {list.Bounds.Right}, scheda {EventDetailsCard.CardWidth}, spazio {scroll.Bounds.Width}.");
            Assert.Equal(list.Bounds.Width, popup.PlacementRect!.Value.Right, 1);

            // the mouse leaves: it closes after the closing delay
            window.MouseMove(new Point(2, 2));
            await WaitForAsync(() => popup.IsOpen ? null : popup, "la chiusura della scheda");
            Assert.Null(popup.Child);

            // a click opens it at once and it stays (as before) until a click elsewhere or Esc
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.True(popup.IsLightDismissEnabled);
            window.MouseMove(new Point(2, 2));
            await PauseAsync(TimeSpan.FromMilliseconds(400));
            Assert.True(popup.IsOpen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);

            // with the delay at 0 the mouse over does nothing
            vm.Settings.HoverDetailsDelayMs = 0;
            window.MouseMove(new Point(2, 2));
            window.MouseMove(center);
            await PauseAsync(TimeSpan.FromMilliseconds(400));
            Assert.False(popup.IsOpen);
        }
        finally
        {
            var vm = Vm(window);
            vm.Settings.HoverDetailsDelayMs = hoverDelay;
            foreach (var c in vm.Calendars.Where(c => c.IsLocal && c.Name.StartsWith(TestPrefix, StringComparison.Ordinal)).ToList()) vm.DeleteLocalCalendar(c.Id);
            foreach (var c in vm.Calendars.Where(c => states?.ContainsKey(c.Id) == true).ToList()) c.IsVisible = states![c.Id];
            window.Close();
        }
    });
}
