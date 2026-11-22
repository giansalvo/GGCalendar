using System.Net.Sockets;
using System.Security.Authentication;
using GGCalendar.App.Services;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Providers.Google;
using static GGCalendar.Core.Tests.Support.AppDriver;
using static GGCalendar.Core.Tests.GoogleCalendarUiTests;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T397-T400: without a network (the name of Google not resolved, no route, a timeout) the sync is not an error: the person is told, calmly,
/// that the PC is offline and that the changes wait. A certificate problem or a refusal of the credentials stay errors.
/// </summary>
[Collection(CollectionName)]
public class OfflineSyncTests
{
    [Fact]
    [Trait("TestCase", "T397")]
    public void T397_OnlyTheRequestsThatDidNotReachGoogleCountAsOffline()
    {
        // The case of the screenshot: "No such host is known (oauth2.googleapis.com:443)".
        Assert.True(GoogleSyncService.IsNetworkDown(new HttpRequestException("Host sconosciuto.", new SocketException((int)SocketError.HostNotFound))));
        Assert.True(GoogleSyncService.IsNetworkDown(new HttpRequestException("no route", new SocketException((int)SocketError.NetworkUnreachable))));
        Assert.True(GoogleSyncService.IsNetworkDown(new HttpRequestException("Nessuna connessione.")));
        Assert.True(GoogleSyncService.IsNetworkDown(new TaskCanceledException("timeout")));

        // These are not "offline": the person must see them.
        Assert.False(GoogleSyncService.IsNetworkDown(new HttpRequestException("ssl", new AuthenticationException("bad certificate"))));
        Assert.False(GoogleSyncService.IsNetworkDown(new HttpRequestException("ssl", new IOException("x", new AuthenticationException("bad certificate")))));
        Assert.False(GoogleSyncService.IsNetworkDown(new GoogleAuthException("credenziali rifiutate")));
        Assert.False(GoogleSyncService.IsNetworkDown(new InvalidOperationException("altro")));
    }

    [Fact]
    [Trait("TestCase", "T398")]
    public async Task T398_WithoutNetworkTheSyncReportsOfflineNotAnErrorAndTheChangesWait()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        var ev = rig.AddLocal(SyncRig.NewEvent(Guid.NewGuid().ToString("N"), "Scritto offline"));
        rig.Google.Offline = true;

        var offline = await rig.SyncAsync();

        Assert.True(offline.Offline);
        Assert.Empty(offline.Errors);
        Assert.Equal(1, offline.Pending);
        Assert.Null(rig.Google.Ids.FirstOrDefault(i => i == ev.Id));
        Assert.NotNull(rig.Local("Scritto offline")); // nothing lost on the PC

        rig.Google.Offline = false;
        var back = await rig.SyncOkAsync();

        Assert.False(back.Offline);
        Assert.Equal(0, back.Pending);
        Assert.Contains(ev.Id, rig.Google.Ids);
    }

    [Fact]
    [Trait("TestCase", "T399")]
    public Task T399_TheStatusBarSaysOfflineInGreyAndThenSyncedWhenTheNetworkIsBack()
    {
        var fake = new FakeGoogle();
        fake.Seed("Su Google", new DateTime(2026, 12, 20, 9, 0, 0, DateTimeKind.Utc));
        return InFakeGoogleAppAsync(fake, async window =>
        {
            var vm = Vm(window);
            await SyncNowAsync(window);
            fake.Offline = true;

            await WaitForQuietAsync(window);
            Click(FindButton(window, "Main.SyncNow"));
            await WaitForAsync(() => !vm.IsSyncing && vm.Status?.Is("status.offline") == true ? vm : null, "il messaggio offline", SyncTimeout);

            Assert.False(vm.SyncIsError); // grey, not red
            Assert.Equal(GGCalendar.Core.Localization.L.T("status.offline"), vm.SyncStatus);
            Assert.DoesNotContain("oauth2", vm.SyncStatus);

            fake.Offline = false;
            await SyncNowAsync(window);
            Assert.False(vm.SyncIsError);
        });
    }

    [Fact]
    [Trait("TestCase", "T400")]
    public async Task T400_TheTechnicalTextOfTheNetworkErrorGoesToTheLogFileNotToTheStatusBar()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        File.Delete(SyncLog.FilePath);
        rig.Google.Offline = true;

        var result = await rig.SyncAsync();

        Assert.True(result.Offline);
        var log = File.ReadAllText(SyncLog.FilePath);
        Assert.Contains("offline [" + FakeGoogle.Account + "]", log);
        Assert.Contains("HttpRequestException: Nessuna connessione.", log);
    }

    [Fact]
    [Trait("TestCase", "T400")]
    public void T400_TheLogDescribesTheWholeChainAndIsRenamedWhenItGrowsTooMuch()
    {
        var text = SyncLog.Describe(new HttpRequestException("Host sconosciuto.", new SocketException((int)SocketError.HostNotFound)));
        Assert.StartsWith("HttpRequestException: Host sconosciuto. > SocketException(HostNotFound):", text);

        File.WriteAllText(SyncLog.FilePath, new string('x', (int)SyncLog.MaxBytes + 1));
        SyncLog.Write("nuova riga");

        Assert.Contains("nuova riga", File.ReadAllText(SyncLog.FilePath));
        Assert.True(File.Exists(Path.ChangeExtension(SyncLog.FilePath, ".old.log")));
        Assert.True(new FileInfo(SyncLog.FilePath).Length < 1000);
    }
}
