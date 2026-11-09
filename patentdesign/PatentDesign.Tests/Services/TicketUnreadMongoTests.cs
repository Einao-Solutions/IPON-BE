using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;

namespace patentdesign.Tests.Services;

[Trait("Category", "Integration")]
public class TicketUnreadMongoTests : IDisposable
{
    private readonly MongoClient _client = new("mongodb://localhost:27017/?serverSelectionTimeoutMS=3000");
    private readonly string _databaseName = "pd_ticket_unread_it_" + Guid.NewGuid().ToString("N");
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<TicketInfo> _tickets;
    private readonly TicketServices _service;

    public TicketUnreadMongoTests()
    {
        _database = _client.GetDatabase(_databaseName);
        _tickets = _database.GetCollection<TicketInfo>("tickets");
        _service = new TicketServices(_database,
            Options.Create(new PatentDesignDBSettings { TicketCollectionName = "tickets" }), NullLogger<TicketServices>.Instance);
    }

    private static TicketViewer Viewer(string id = "owner", params Roles[] roles) => TicketViewer.FromPrincipal(
        new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }
            .Concat(roles.Select(role => new Claim(ClaimTypes.Role, role.ToString()))), "test")));

    private static TicketInfo Ticket(string id, string owner = "owner", bool dev = true, string sender = "agent")
    {
        var ticket = new TicketInfo
        {
            id = id, TicketNumber = "TKT-" + id, creatorId = owner, creatorName = owner,
            Status = TicketState.InProgress, Category = TicketCategory.TechnicalSupport,
            IsEscalatedToDev = dev,
            Correspondences = [new TicketCorrespondence { SenderId = sender, SenderName = sender, Message = "Reply" }]
        };
        TicketMessageTracking.InitializeNewTicket(ticket);
        return ticket;
    }

    private static NewCorrespondenceType Reply(string ticketId) => new()
    {
        ticketId = ticketId, newStatus = TicketState.InProgress,
        correspondence = new TicketCorrespondence { SenderId = "forged", SenderName = "forged", Message = "New reply" }
    };

    [Fact]
    public async Task ResolutionCapturesEachStatusAtomicallyAndReturnsMetadataWithoutChangingUnread()
    {
        var awaitingUser = Ticket("awaiting-user");
        awaitingUser.Status = TicketState.AwaitingUser;
        awaitingUser.ReadMessageSequences[TicketMessageTracking.ReaderKey("owner")] = 1;
        var awaitingStaff = Ticket("awaiting-staff");
        awaitingStaff.Status = TicketState.AwaitingStaff;
        var inProgress = Ticket("in-progress", dev: false);
        var alreadyResolved = Ticket("already-resolved");
        alreadyResolved.Status = TicketState.Resolved;
        alreadyResolved.StatusBeforeResolution = TicketState.AwaitingUser;
        alreadyResolved.ResolvedAt = DateTime.UtcNow.AddHours(-1);
        var resolvedAt = new BsonDateTime(alreadyResolved.ResolvedAt.Value).ToUniversalTime();
        var closed = Ticket("closed");
        closed.Status = TicketState.Closed;
        closed.StatusBeforeResolution = TicketState.InProgress;
        var tickets = new[] { awaitingUser, awaitingStaff, inProgress, alreadyResolved, closed };
        await _tickets.InsertManyAsync(tickets);
        var viewer = TicketViewer.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Name, "$Status")], "test")));
        var request = new ResolveTicketType { ticketId = tickets.Select(x => x.id).ToList(), resolution = new ResolveInfo() };

        await _service.CloseTicketsAsync(request, viewer);
        await _service.CloseTicketsAsync(request, viewer);
        foreach (var original in new[] { awaitingUser, awaitingStaff, inProgress })
        {
            var result = await _service.GetTicketAsync(original.id, viewer);
            Assert.Equal(TicketState.Resolved, result!.Status);
            Assert.Equal(original.Status, result.StatusBeforeResolution);
            Assert.Equal("$Status", result.resolution!.StaffName);
            Assert.Equal(original.LatestMessageSequence, result.LatestMessageSequence);
            Assert.Equal(original.ReadMessageSequences, result.ReadMessageSequences);
        }
        var untouched = await _service.GetTicketAsync("already-resolved", viewer);
        Assert.Equal(TicketState.AwaitingUser, untouched!.StatusBeforeResolution);
        Assert.Equal(resolvedAt, untouched.ResolvedAt);
        Assert.Equal(TicketState.Closed, (await _service.GetTicketAsync("closed", viewer))!.Status);

        var summaries = await _service.GetTicketsSummariesAsync(new TicketsSummariesType(), viewer);
        Assert.Equal(TicketState.AwaitingUser, summaries.Single(x => x.TicketId == awaitingUser.id).StatusBeforeResolution);
        var search = await _service.SearchTicketsAsync(new TicketSearchRequest { ticketNumber = "TKT-in-progress" }, viewer);
        Assert.Equal(TicketState.InProgress, Assert.Single(search).StatusBeforeResolution);

        var reopened = await _service.AddMessageAsync(Reply(awaitingUser.id), viewer);
        Assert.Equal(TicketState.AwaitingStaff, reopened!.Status);
        Assert.Equal(TicketState.AwaitingUser, reopened.StatusBeforeResolution);
        Assert.Null(reopened.resolution);
        Assert.Null(reopened.ResolvedAt);
        Assert.Null(reopened.ClosedAt);
        Assert.Equal(2, reopened.LatestMessageSequence);
        Assert.Equal(1, reopened.ReadMessageSequences[TicketMessageTracking.ReaderKey("owner")]);
        var previouslyInProgress = await _service.AddMessageAsync(Reply(inProgress.id), viewer);
        Assert.Equal(TicketState.InProgress, previouslyInProgress!.Status);
        Assert.False(previouslyInProgress.IsEscalatedToDev);
        var previouslyAwaitingStaff = await _service.AddMessageAsync(Reply(awaitingStaff.id), viewer);
        Assert.Equal(TicketState.AwaitingStaff, previouslyAwaitingStaff!.Status);
        await _service.CloseTicketsAsync(new ResolveTicketType { ticketId = [awaitingUser.id], resolution = new ResolveInfo() }, viewer);
        Assert.Equal(TicketState.AwaitingStaff, (await _service.GetTicketAsync(awaitingUser.id, viewer))!.StatusBeforeResolution);
    }

    [Theory]
    [InlineData(false, TicketState.AwaitingStaff)]
    [InlineData(true, TicketState.InProgress)]
    public async Task LegacyResolvedTicketsUseDeveloperFlagOnlyWhenPreviousStatusIsMissing(bool dev, TicketState expected)
    {
        var ticket = Ticket("legacy-resolved", dev: dev);
        ticket.Status = TicketState.Resolved;
        ticket.ResolvedAt = DateTime.UtcNow.AddHours(-1);
        ticket.resolution = new ResolveInfo();
        var document = ticket.ToBsonDocument();
        document.Remove(nameof(TicketInfo.StatusBeforeResolution));
        await _database.GetCollection<BsonDocument>("tickets").InsertOneAsync(document);
        var result = await _service.AddMessageAsync(Reply(ticket.id), Viewer());
        Assert.Equal(expected, result!.Status);
        Assert.Null(result.StatusBeforeResolution);
        Assert.Null(result.resolution);
        Assert.Null(result.ResolvedAt);
        Assert.Null(result.ClosedAt);
    }

    [Fact]
    public async Task RealMongoProjectionsAndStatisticsRespectUnreadAndQueueFilters()
    {
        await _tickets.InsertManyAsync(new[]
        {
            Ticket("one"), Ticket("other-queue", dev: false), Ticket("inaccessible", owner: "other"), Ticket("own-only", sender: "owner")
        });
        var viewer = Viewer();
        var summaries = await _service.GetTicketsSummariesAsync(new TicketsSummariesType
        {
            status = TicketState.InProgress, category = TicketCategory.TechnicalSupport, isEscalatedToDev = true
        }, viewer);
        Assert.Equal(2, summaries.Count);
        var unread = Assert.Single(summaries.Where(x => x.HasUnreadMessages));
        Assert.Equal("one", unread.TicketId);
        Assert.Equal("owner", unread.Creator.Id);
        Assert.Equal(1, unread.UnreadCount);
        Assert.Equal(1, unread.LatestMessageSequence);
        var stats = await _service.TicketStats(null, (int)TicketCategory.TechnicalSupport,
            viewer: viewer, status: TicketState.InProgress, isEscalatedToDev: true);
        Assert.Equal(2, stats.total);
        Assert.Equal(1, stats.unreadTickets);
        var search = await _service.SearchTicketsAsync(new TicketSearchRequest { ticketNumber = "TKT", isTech = true }, viewer);
        Assert.Equal(3, search.Count);
        Assert.DoesNotContain(search, x => x.TicketId == "inaccessible");
        Assert.Equal(2, search.Count(x => x.HasUnreadMessages));
    }

    [Fact]
    public async Task RealMongoInitializesLegacyHistoryAsReadBeforeTrackingNewReplies()
    {
        var legacy = new BsonDocument
        {
            { "_id", "legacy" }, { "creatorId", "owner" }, { "creatorName", "Owner" }, { "TicketNumber", "TKT-legacy" },
            { "Status", new TicketInfo { Status = TicketState.InProgress }.ToBsonDocument()["Status"] },
            { "Created", DateTime.UtcNow },
            { "Correspondences", new BsonArray
                {
                    new BsonDocument { { "SenderId", "agent" }, { "SenderName", "Agent" }, { "Message", "Old reply" }, { "DateAdded", DateTime.UtcNow } }
                } }
        };
        await _database.GetCollection<BsonDocument>("tickets").InsertOneAsync(legacy);
        var viewer = Viewer();
        var before = Assert.Single(await _service.GetTicketsSummariesAsync(new TicketsSummariesType(), viewer));
        Assert.False(before.HasUnreadMessages);
        Assert.Equal(1, before.LatestMessageSequence);
        Assert.Equal(0, (await _service.TicketStats(null, viewer: viewer)).unreadTickets);
        var initialized = await _service.GetTicketAsync("legacy", viewer);
        Assert.Equal(1, initialized!.Correspondences![0].MessageSequence);
        Assert.Equal(1, initialized.UnreadBaselineSequence);
        Assert.False(initialized.HasUnreadMessages);
        await _service.AddMessageAsync(Reply("legacy"), Viewer("agent", Roles.Tech));
        var after = await _service.GetTicketAsync("legacy", viewer);
        Assert.Equal(2, after!.LatestMessageSequence);
        Assert.Equal(1, after.UnreadCount);
        Assert.Equal(1, (await _service.TicketStats(null, viewer: viewer)).unreadTickets);
    }

    [Fact]
    public async Task RealMongoKeepsReadProgressMonotonicAndSeparateBetweenReaders()
    {
        var ticket = Ticket("read");
        ticket.Correspondences!.Add(new TicketCorrespondence
        {
            SenderId = "agent", SenderName = "Agent", Message = "Second reply", MessageSequence = 2
        });
        ticket.LatestMessageSequence = 2;
        await _tickets.InsertOneAsync(ticket);
        var owner = Viewer();
        await Task.WhenAll(_service.MarkTicketReadAsync("read", 2, owner), _service.MarkTicketReadAsync("read", 1, owner));
        var read = await _service.GetTicketAsync("read", owner);
        Assert.False(read!.HasUnreadMessages);
        var officer = await _service.GetTicketAsync("read", Viewer("second-officer", Roles.Tech));
        Assert.Equal(2, officer!.UnreadCount);
        await _service.AddMessageAsync(Reply("read"), Viewer("agent", Roles.Tech));
        var stale = await _service.MarkTicketReadAsync("read", 1, owner);
        Assert.Equal(2, stale!.LastReadMessageSequence);
        Assert.Equal(3, stale.LatestMessageSequence);
        Assert.Equal(1, stale.UnreadCount);
        var stored = await _tickets.Find(x => x.id == "read").FirstAsync();
        Assert.Equal(TicketState.InProgress, stored.Status);
        Assert.True(stored.IsEscalatedToDev);
    }

    [Fact]
    public async Task RealMongoConcurrentAppendsAllocateDistinctIncreasingSequences()
    {
        var ticket = Ticket("concurrent");
        ticket.Correspondences = [];
        TicketMessageTracking.InitializeNewTicket(ticket);
        await _tickets.InsertOneAsync(ticket);
        await Task.WhenAll(
            _service.AddMessageAsync(Reply("concurrent"), Viewer("one", Roles.Tech)),
            _service.AddMessageAsync(Reply("concurrent"), Viewer("two", Roles.Tech)));
        var result = await _service.GetTicketAsync("concurrent", Viewer());
        Assert.Equal(new long[] { 1, 2 }, result!.Correspondences!.Select(x => x.MessageSequence));
        Assert.Equal(2, result.LatestMessageSequence);
        Assert.Equal(2, result.UnreadCount);
    }

    [Fact]
    public async Task RealMongoRegistryQueueAndReadAuthorizationCannotBeSpoofed()
    {
        var trademark = Ticket("trademark");
        trademark.Category = TicketCategory.TrademarkRegistry;
        var patent = Ticket("patent");
        patent.Category = TicketCategory.PatentDesignRegistry;
        var technical = Ticket("registry-tech");
        technical.RegistryCategory = TicketCategory.TrademarkRegistry;
        technical.RaisedByRegistryStaff = true;
        var publicTechnical = Ticket("public-tech");
        publicTechnical.RegistryCategory = TicketCategory.TrademarkRegistry;
        await _tickets.InsertManyAsync(new[] { trademark, patent, technical, publicTechnical });
        var viewer = Viewer("registry-support", Roles.TrademarkSupport);
        var summaries = await _service.GetTicketsSummariesAsync(new TicketsSummariesType(), viewer);
        Assert.Equal(2, summaries.Count);
        Assert.Equal(2, (await _service.TicketStats(null, viewer: viewer)).unreadTickets);
        Assert.Equal(1, (await _service.TicketStats(null, (int)TicketCategory.TechnicalSupport,
            (int)TicketCategory.TrademarkRegistry, true, viewer)).unreadTickets);
        Assert.Null(await _service.MarkTicketReadAsync("patent", 1, viewer));
        Assert.Null(await _service.AddMessageAsync(Reply("patent"), viewer));
        var stranger = Viewer("stranger");
        Assert.Empty(await _service.SearchTicketsAsync(new TicketSearchRequest
        {
            ticketNumber = "TKT", isTech = true, requesterId = "owner", supportRegistryCategory = TicketCategory.PatentDesignRegistry
        }, stranger));
        var stored = await _tickets.Find(x => x.id == "patent").FirstAsync();
        Assert.Empty(stored.ReadMessageSequences);
        Assert.Equal(1, stored.LatestMessageSequence);
    }

    public void Dispose() => _client.DropDatabase(_databaseName);
}
