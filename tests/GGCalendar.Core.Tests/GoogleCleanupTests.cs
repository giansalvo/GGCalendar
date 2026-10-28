using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Removes from the test account (and from the app) what an interrupted run of the real Google tests left. Every real test does it first anyway; this one only does that.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class GoogleCleanupTests
{
    [Fact]
    public Task CleanUpTheTestAccount() => InAppAsync(_ => Task.CompletedTask);
}
