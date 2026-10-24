using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>T17: the app without a network. Semi-automatic: the person switches the network off and on when the test asks.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class NetworkUiTests
{
    private static async Task<bool> CanReachGoogleAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await http.GetAsync("https://www.google.com/generate_204");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task WaitForNetworkAsync(bool wanted, string what)
    {
        var until = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        while (DateTime.UtcNow < until)
        {
            if (await CanReachGoogleAsync() == wanted) return;
            await PauseAsync(TimeSpan.FromSeconds(1));
        }

        throw new Xunit.Sdk.XunitException($"Tempo scaduto in attesa di: {what}.");
    }

    [Fact]
    [Trait("TestCase", "T17")]
    public Task T17_WithoutANetworkNothingIsLostAndTheChangesArriveWhenItIsBack() => InAppAsync(async window =>
    {
        TellThePerson("Test della rete. DISATTIVA ORA la connessione a Internet (Wi-Fi o cavo). Il test prosegue da solo quando la rete non c'è più.");
        await WaitForNetworkAsync(false, "la disattivazione della rete");

        // Offline: the user creates an event and syncs.
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title));
        var vm = Vm(window);
        await WaitForQuietAsync(window);
        Click(FindButton(window, b => Avalonia.Controls.ToolTip.GetTip(b) as string == "Sincronizza ora"));
        await WaitForQuietAsync(window);
        Assert.StartsWith("Errore", vm.SyncStatus);
        Assert.Contains(vm.AllEvents, e => e.Title == title); // the event is still there
        Assert.Contains(new GGCalendar.Data.CalendarStore().GetPending(), p => p.EventId == created.Id); // and still waits to be sent

        TellThePerson("Ora RIATTIVA la connessione a Internet. Il test prosegue da solo.");
        await WaitForNetworkAsync(true, "la riattivazione della rete");

        // Back online: the event reaches Google, once.
        await SyncAsync(window);
        Assert.DoesNotContain(new GGCalendar.Data.CalendarStore().GetPending(), p => p.EventId == created.Id);
        using var google = await TestGoogle.ConnectAsync();
        Assert.Single(await google.FindAsync(title));
    });
}
