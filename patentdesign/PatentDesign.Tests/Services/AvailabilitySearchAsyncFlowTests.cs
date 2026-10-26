using MongoDB.Driver;
using patentdesign.Models;
using Xunit;

namespace patentdesign.Tests.Services;

public class AvailabilitySearchAsyncFlowTests(AvsFlowFixture f) : IClassFixture<AvsFlowFixture>
{
    [Fact]
    public async Task ExistingPendingRrr_IsReused()
    {
        var user = await f.CreateUser();
        var first = await f.Files.AvailabilitySearchCost("n", "e@x.com", user, "reuse");
        var second = await f.Files.AvailabilitySearchCost("n", "e@x.com", user, "reuse");
        Assert.Equal(first.rrr, second.rrr);
        Assert.Equal(first.AppId, second.AppId);
    }

    [Fact]
    public async Task ConcurrentDuplicates_CreateSingleApplication()
    {
        var user = await f.CreateUser();
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => f.Files.AvailabilitySearchCost("n", "e@x.com", user, "dup"))));
        Assert.Single(results.Select(r => r.rrr).Distinct());
        var stored = await f.Db.GetCollection<AppUser>("appUsers").Find(x => x.Id == user).FirstAsync();
        Assert.Single(stored.OtherApplications!);
    }

    [Fact]
    public async Task AsyncJob_ReachesSucceeded_WithRealRrr_AndIsOwnerScoped()
    {
        var user = await f.CreateUser();
        var job = f.Files.StartAvailabilitySearch("n", "e@x.com", user, "async");
        var again = f.Files.StartAvailabilitySearch("n", "e@x.com", user, "async");
        Assert.True(job.RequestId == again.RequestId || again.Status != "pending");

        for (var i = 0; i < 100 && job.Status == "pending"; i++) await Task.Delay(50);

        Assert.Equal("succeeded", job.Status);
        Assert.False(string.IsNullOrWhiteSpace(job.Result?.rrr));
        Assert.Null(f.Files.GetAvailabilitySearchJob(job.RequestId, "someone-else"));
        Assert.NotNull(f.Files.GetAvailabilitySearchJob(job.RequestId, user));
    }
}
