using System.Net;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T377-T379 (issue #37, again in 1.0.4): Google also writes the limit of requests per minute in the format of Google Cloud quotas:
/// 403 "Quota exceeded for quota metric 'Queries'...", with no "errors" list and the reason only in "details". It is still "not now":
/// the app must give the clear message and try again, not show the raw text.
/// </summary>
public class SyncCloudQuotaTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [Trait("TestCase", "T377")]
    [InlineData("rateLimitExceeded")]
    [InlineData("quotaMessageOnly")]
    public async Task T377_TheLimitInTheCloudFormatWhileDownloadingGivesTheClearMessageAndARetry(string reason)
    {
        using var rig = new SyncRig();
        rig.Service.Clock = () => Now;
        rig.Google.CloudErrorFormat = true;
        await rig.SyncOkAsync();
        rig.Google.QuotaOnEventList.Add(FakeGoogle.RemoteCalendar);
        rig.Google.QuotaReason = reason;

        var result = await rig.SyncAsync();

        var error = Assert.Single(result.Errors);
        Assert.Equal("sync.account_limited", error.Key);
        Assert.DoesNotContain("Quota exceeded", error.Text); // not the raw text of Google
        Assert.NotNull(result.RetryAt);
        Assert.True(result.RetryAt > Now);

        rig.Google.QuotaOnEventList.Clear();
        await rig.SyncOkAsync(); // later it goes through
    }

    [Theory]
    [Trait("TestCase", "T378")]
    [InlineData("rateLimitExceeded")]
    [InlineData("quotaMessageOnly")]
    public async Task T378_TheLimitInTheCloudFormatWhileSendingKeepsTheChangeInTheQueue(string reason)
    {
        using var rig = new SyncRig();
        var now = Now;
        rig.Service.Clock = () => now;
        rig.Google.CloudErrorFormat = true;
        rig.AddLocal(SyncRig.NewEvent(Guid.NewGuid().ToString("N"), "Evento che aspetta"));
        rig.Google.NextWriteFails = (HttpStatusCode.Forbidden, reason);

        var result = await rig.SyncAsync();

        Assert.Empty(result.Errors); // no raw message: the change waits
        Assert.Equal(1, result.Pending);
        Assert.NotNull(result.RetryAt);

        now = result.RetryAt!.Value.AddSeconds(5); // the time Google was asked to wait has passed
        await rig.SyncOkAsync(); // the next try sends it
        Assert.NotNull(rig.Google.FindBySummary("Evento che aspetta"));
    }

    [Fact]
    [Trait("TestCase", "T379")]
    public async Task T379_AForbiddenThatIsNotALimitStaysADefinitiveRefusalInTheCloudFormat()
    {
        using var rig = new SyncRig();
        rig.Google.CloudErrorFormat = true; // only the limits change format: "forbidden" is not one of them
        rig.AddLocal(SyncRig.NewEvent(Guid.NewGuid().ToString("N"), "Evento rifiutato"));
        rig.Google.NextWriteFails = (HttpStatusCode.Forbidden, "forbiddenForNonOrganizer");

        var result = await rig.SyncAsync();

        Assert.Single(result.Errors);
        Assert.Equal(0, result.Pending);
    }
}
