using System.ComponentModel.DataAnnotations;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using patentdesign.Controllers;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;

namespace patentdesign.Tests.Services;

public class TicketUnreadTests
{
    private readonly Mock<IMongoCollection<TicketInfo>> _collection = new();
    private readonly TicketServices _service;
    private TicketInfo? _ticket;
    private BsonDocument? _filter;
    private BsonValue? _update;
    private int _writes;
    private Action? _beforeWrite;
    private bool _failNextWrite;
    private BsonDocument? _projection;

    public TicketUnreadTests()
    {
        var db = new Mock<IMongoDatabase>();
        db.Setup(x => x.GetCollection<TicketInfo>("tickets", It.IsAny<MongoCollectionSettings>())).Returns(_collection.Object);
        _service = new TicketServices(db.Object,
            Options.Create(new PatentDesignDBSettings { TicketCollectionName = "tickets" }), NullLogger<TicketServices>.Instance);
        _collection.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<TicketInfo>>(),
                It.IsAny<FindOptions<TicketInfo, TicketInfo>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<TicketInfo> filter, FindOptions<TicketInfo, TicketInfo> _, CancellationToken token) =>
            {
                _filter = Render(filter);
                var ticket = _ticket != null && Matches(_filter, _ticket.ToBsonDocument())
                    ? BsonSerializer.Deserialize<TicketInfo>(_ticket.ToBsonDocument()) : null;
                return Task.FromResult(Cursor(ticket == null ? Array.Empty<TicketInfo>() : new[] { ticket }));
            });
        _collection.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<TicketInfo>>(),
                It.IsAny<FindOptions<TicketInfo, TicketSummary>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<TicketInfo> filter, FindOptions<TicketInfo, TicketSummary> options, CancellationToken token) =>
            {
                _filter = Render(filter);
                _projection = options.Projection.Render(BsonSerializer.LookupSerializer<TicketInfo>(), BsonSerializer.SerializerRegistry).Document;
                return Task.FromResult(Cursor(Array.Empty<TicketSummary>()));
            });
        _collection.Setup(x => x.FindOneAndUpdateAsync(It.IsAny<FilterDefinition<TicketInfo>>(),
                It.IsAny<UpdateDefinition<TicketInfo>>(), It.IsAny<FindOneAndUpdateOptions<TicketInfo, TicketInfo>>(),
                It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<TicketInfo> filter, UpdateDefinition<TicketInfo> update,
                FindOneAndUpdateOptions<TicketInfo, TicketInfo> _, CancellationToken token) =>
            {
                _writes++;
                _filter = Render(filter);
                _update = update.Render(BsonSerializer.LookupSerializer<TicketInfo>(), BsonSerializer.SerializerRegistry);
                var action = _beforeWrite;
                _beforeWrite = null;
                action?.Invoke();
                if (_failNextWrite)
                {
                    _failNextWrite = false;
                    return Task.FromResult<TicketInfo>(null!);
                }
                if (_ticket == null || !Matches(_filter, _ticket.ToBsonDocument())) return Task.FromResult<TicketInfo>(null!);
                if (_update.IsBsonArray)
                {
                    _ticket.MessageTrackingVersion = TicketMessageTracking.CurrentVersion;
                    _ticket.LatestMessageSequence = 0;
                    foreach (var message in _ticket.Correspondences ?? []) message.MessageSequence = ++_ticket.LatestMessageSequence;
                    _ticket.UnreadBaselineSequence = _ticket.LatestMessageSequence;
                    _ticket.ReadMessageSequences = new();
                }
                else
                {
                    ApplyUpdate(_update.AsBsonDocument);
                }
                return Task.FromResult(BsonSerializer.Deserialize<TicketInfo>(_ticket.ToBsonDocument()));
            });
    }

    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> values)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(x => x.Current).Returns(values);
        cursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }

    private static BsonDocument Render(FilterDefinition<TicketInfo> filter) =>
        filter.Render(BsonSerializer.LookupSerializer<TicketInfo>(), BsonSerializer.SerializerRegistry);

    private static bool Matches(BsonDocument filter, BsonDocument document)
    {
        foreach (var field in filter)
        {
            if (field.Name == "$and" && !field.Value.AsBsonArray.All(x => Matches(x.AsBsonDocument, document))) return false;
            if (field.Name == "$or" && !field.Value.AsBsonArray.Any(x => Matches(x.AsBsonDocument, document))) return false;
            if (field.Name.StartsWith('$')) continue;
            var actual = document.GetValue(field.Name, BsonNull.Value);
            if (field.Value is BsonDocument operators)
            {
                foreach (var op in operators)
                {
                    if (op.Name == "$ne" && actual == op.Value) return false;
                    if (op.Name == "$nin" && op.Value.AsBsonArray.Contains(actual)) return false;
                    if (op.Name == "$in" && !op.Value.AsBsonArray.Contains(actual)) return false;
                }
            }
            else if (actual != field.Value) return false;
        }
        return true;
    }

    private void ApplyUpdate(BsonDocument update)
    {
        var document = _ticket!.ToBsonDocument();
        if (update.TryGetValue("$set", out var set))
            foreach (var field in set.AsBsonDocument) document[field.Name] = field.Value;
        if (update.TryGetValue("$inc", out var inc))
            foreach (var field in inc.AsBsonDocument) document[field.Name] = document.GetValue(field.Name, 0L).ToInt64() + field.Value.ToInt64();
        if (update.TryGetValue("$push", out var push))
            foreach (var field in push.AsBsonDocument)
            {
                if (!document.TryGetValue(field.Name, out var array) || array.IsBsonNull) document[field.Name] = array = new BsonArray();
                array.AsBsonArray.Add(field.Value);
            }
        _ticket = BsonSerializer.Deserialize<TicketInfo>(document);
        if (update.TryGetValue("$max", out var max))
            foreach (var field in max.AsBsonDocument)
            {
                var key = field.Name.Split('.')[1];
                _ticket.ReadMessageSequences[key] = Math.Max(_ticket.ReadMessageSequences.GetValueOrDefault(key), field.Value.ToInt64());
            }
    }

    private static ClaimsPrincipal Principal(string id, params Roles[] roles) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Name, id) }
            .Concat(roles.Select(role => new Claim(ClaimTypes.Role, role.ToString()))), "test"));

    private static TicketViewer Viewer(string id = "owner", params Roles[] roles) => TicketViewer.FromPrincipal(Principal(id, roles));

    private void TrackedTicket()
    {
        _ticket = new TicketInfo { id = "ticket", creatorId = "owner", Category = TicketCategory.TechnicalSupport, Status = TicketState.InProgress };
        TicketMessageTracking.InitializeNewTicket(_ticket);
    }

    private void Append(string sender)
    {
        _ticket!.Correspondences ??= [];
        _ticket.Correspondences.Add(new TicketCorrespondence
        {
            SenderId = sender, SenderName = sender, Message = "Reply", MessageSequence = ++_ticket.LatestMessageSequence
        });
    }

    private static NewCorrespondenceType Reply(string sender = "spoofed") => new()
    {
        ticketId = "ticket", newStatus = TicketState.InProgress,
        correspondence = new TicketCorrespondence { SenderId = sender, SenderName = sender, Message = "Reply", MessageSequence = 999 }
    };

    [Fact]
    public void UnauthenticatedAndIdentifierlessPrincipalsAreRejected()
    {
        Assert.Throws<AuthenticationException>(() => TicketViewer.FromPrincipal(new ClaimsPrincipal()));
        Assert.Throws<AuthenticationException>(() => TicketViewer.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity("test"))));
    }

    [Fact]
    public async Task AuthorizationFilterReturns401BeforeExecutingAction()
    {
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(context, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var called = false;
        await new TicketAccessAttribute().OnActionExecutionAsync(executing, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), new object()));
        });
        Assert.IsType<UnauthorizedResult>(executing.Result);
        Assert.False(called);
    }

    [Fact]
    public void RegistryAccessIncludesOnlyOwnAndAssignedQueueTickets()
    {
        var viewer = Viewer("support", Roles.TrademarkSupport);
        Assert.True(TicketAccessRules.CanAccess(new TicketInfo { Category = TicketCategory.TrademarkRegistry }, viewer));
        Assert.True(TicketAccessRules.CanAccess(new TicketInfo { Category = TicketCategory.TechnicalSupport,
            RegistryCategory = TicketCategory.TrademarkRegistry, RaisedByRegistryStaff = true }, viewer));
        Assert.False(TicketAccessRules.CanAccess(new TicketInfo { Category = TicketCategory.PatentDesignRegistry }, viewer));
        Assert.False(TicketAccessRules.CanAccess(new TicketInfo { Category = TicketCategory.TechnicalSupport,
            RegistryCategory = TicketCategory.TrademarkRegistry, RaisedByRegistryStaff = false }, viewer));
        Assert.True(TicketAccessRules.CanAccess(new TicketInfo { creatorId = "support" }, viewer));
    }

    [Theory]
    [InlineData(Roles.Tech)]
    [InlineData(Roles.SuperAdmin)]
    public void GlobalSupportRolesCanAccessAllQueues(Roles role)
    {
        Assert.True(TicketAccessRules.CanAccess(new TicketInfo { creatorId = "other" }, Viewer("staff", role)));
    }

    [Fact]
    public void OwnMessagesAreExcludedAndReadersRemainIndependent()
    {
        TrackedTicket();
        Append("owner");
        Append("staff");
        Append("owner");
        TicketMessageTracking.Apply(_ticket!, Viewer());
        Assert.Equal(1, _ticket!.UnreadCount);
        TicketMessageTracking.Apply(_ticket, Viewer("staff", Roles.Tech));
        Assert.Equal(2, _ticket.UnreadCount);
        _ticket.ReadMessageSequences[TicketMessageTracking.ReaderKey("owner")] = 3;
        TicketMessageTracking.Apply(_ticket, Viewer());
        Assert.False(_ticket.HasUnreadMessages);
        TicketMessageTracking.Apply(_ticket, Viewer("staff", Roles.Tech));
        Assert.Equal(2, _ticket.UnreadCount);
    }

    [Fact]
    public async Task MarkingReadDoesNotMarkNewerConcurrentReplyReadOrChangeLifecycle()
    {
        TrackedTicket();
        Append("staff");
        _ticket!.IsEscalatedToDev = true;
        _beforeWrite = () => Append("staff");
        var result = await _service.MarkTicketReadAsync("ticket", 1, Viewer());
        Assert.Equal(1, result!.LastReadMessageSequence);
        Assert.Equal(2, result.LatestMessageSequence);
        Assert.Equal(1, result.UnreadCount);
        Assert.True(result.HasUnreadMessages);
        Assert.Equal(TicketState.InProgress, _ticket.Status);
        Assert.True(_ticket.IsEscalatedToDev);
        Assert.Single(_update!.AsBsonDocument);
        Assert.True(_update.AsBsonDocument.Contains("$max"));
    }

    [Fact]
    public async Task FutureSequenceIsClampedAndStaleRequestsCannotMoveReadProgressBackward()
    {
        TrackedTicket();
        Append("staff");
        Append("staff");
        _beforeWrite = () => Append("staff");
        var first = await _service.MarkTicketReadAsync("ticket", long.MaxValue, Viewer());
        Assert.Equal(2, first!.LastReadMessageSequence);
        Assert.Equal(1, first.UnreadCount);
        var second = await _service.MarkTicketReadAsync("ticket", 1, Viewer());
        Assert.Equal(2, second!.LastReadMessageSequence);
        Assert.Equal(1, second.UnreadCount);
    }

    [Fact]
    public async Task ReadMarkersAreSeparateForDifferentOfficers()
    {
        TrackedTicket();
        Append("owner");
        await _service.MarkTicketReadAsync("ticket", 1, Viewer("one", Roles.Tech));
        var other = await _service.GetTicketAsync("ticket", Viewer("two", Roles.Tech));
        Assert.True(other!.HasUnreadMessages);
        Assert.Equal(1, other.UnreadCount);
    }

    [Fact]
    public async Task ReadAndDetailsDenyUnrelatedUsersWithoutWriting()
    {
        TrackedTicket();
        Assert.Null(await _service.MarkTicketReadAsync("ticket", 1, Viewer("other")));
        Assert.Null(await _service.GetTicketAsync("ticket", Viewer("other")));
        Assert.Equal(0, _writes);
    }

    [Fact]
    public async Task PostingMessageCannotSpoofSenderOrSequenceAndOwnSendDoesNotReadEarlierReplies()
    {
        TrackedTicket();
        Append("staff");
        var result = await _service.AddMessageAsync(Reply("staff"), Viewer());
        var message = result!.Correspondences!.Last();
        Assert.Equal("owner", message.SenderId);
        Assert.Equal(2, message.MessageSequence);
        Assert.Equal(2, result.LatestMessageSequence);
        Assert.Equal(1, result.UnreadCount);
        Assert.Empty(result.ReadMessageSequences);
        Assert.Equal(1, _filter!["LatestMessageSequence"].ToInt64());
    }

    [Fact]
    public async Task ContendingAppendRetriesWithNextSequence()
    {
        TrackedTicket();
        _beforeWrite = () => Append("staff");
        _failNextWrite = true;
        var result = await _service.AddMessageAsync(Reply(), Viewer());
        Assert.Equal(new long[] { 1, 2 }, result!.Correspondences!.Select(x => x.MessageSequence));
        Assert.Equal(2, result.LatestMessageSequence);
        Assert.Equal(2, _writes);
    }

    [Fact]
    public async Task LegacyHistoryGetsSequencesAndStartsReadForEveryViewer()
    {
        _ticket = new TicketInfo { id = "ticket", creatorId = "owner", Status = TicketState.InProgress,
            Correspondences = [new TicketCorrespondence { SenderId = "staff", SenderName = "staff" }] };
        var result = await _service.GetTicketAsync("ticket", Viewer());
        Assert.Equal(1, result!.LatestMessageSequence);
        Assert.Equal(1, result.Correspondences![0].MessageSequence);
        Assert.False(result.HasUnreadMessages);
        Assert.Equal(1, result.UnreadBaselineSequence);
        Assert.Equal(1, _writes);
        Append("staff");
        var next = await _service.GetTicketAsync("ticket", Viewer());
        Assert.Equal(1, next!.UnreadCount);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public async Task AppendInitializesLegacyBaselineBeforeAddingNewUnreadMessage()
    {
        _ticket = new TicketInfo { id = "ticket", creatorId = "owner", Status = TicketState.InProgress,
            Correspondences = [new TicketCorrespondence { SenderId = "owner", SenderName = "owner" }] };
        var result = await _service.AddMessageAsync(Reply(), Viewer("staff", Roles.Tech));
        Assert.Equal(1, result!.UnreadBaselineSequence);
        Assert.Equal(2, result.LatestMessageSequence);
        var owner = await _service.GetTicketAsync("ticket", Viewer());
        Assert.Equal(1, owner!.UnreadCount);
    }

    [Fact]
    public void LegacyInitializationIsAtomicAndDoesNotWriteLifecycleFields()
    {
        var stages = TicketMessageTracking.LegacyInitializationStages();
        var set = Assert.Single(stages)["$set"].AsBsonDocument;
        Assert.Equal(TicketMessageTracking.CurrentVersion, set["MessageTrackingVersion"].AsInt32);
        Assert.True(set.Contains("Correspondences"));
        Assert.True(set.Contains("UnreadBaselineSequence"));
        Assert.False(set.Contains("Status"));
        Assert.False(set.Contains("ResolvedAt"));
        Assert.False(set.Contains("ClosedAt"));
        Assert.False(set.Contains("IsEscalatedToDev"));
    }

    [Fact]
    public async Task SearchIgnoresSpoofedIdentityAndPrivilegeFlags()
    {
        await _service.SearchTicketsAsync(new TicketSearchRequest
        {
            ticketNumber = "TKT", requesterId = "someone-else", isTech = true,
            supportRegistryCategory = TicketCategory.PatentDesignRegistry
        }, Viewer());
        Assert.Equal("owner", _filter!["creatorId"].AsString);
        Assert.NotNull(_projection);
        Assert.True(_projection!.Contains("UnreadCount"));
        Assert.True(_projection.Contains("HasUnreadMessages"));
        Assert.True(_projection.Contains("LatestMessageSequence"));
    }

    [Fact]
    public async Task SummariesApplyQueueFiltersAndAuthenticatedAccessTogether()
    {
        await _service.GetTicketsSummariesAsync(new TicketsSummariesType
        {
            category = TicketCategory.TechnicalSupport, isEscalatedToDev = true, status = TicketState.InProgress
        }, Viewer());
        Assert.Equal("owner", _filter!["creatorId"].AsString);
        Assert.True(_filter["IsEscalatedToDev"].AsBoolean);
        Assert.Equal((int)TicketCategory.TechnicalSupport, _filter["Category"].AsInt32);
        Assert.NotNull(_projection);
    }

    [Fact]
    public void MongoUnreadExpressionUsesViewerWatermarkAndExcludesOwnMessages()
    {
        var expression = TicketMessageTracking.UnreadCountExpression(Viewer()).ToJson();
        Assert.Contains("$ReadMessageSequences." + TicketMessageTracking.ReaderKey("owner"), expression);
        Assert.Contains("$UnreadBaselineSequence", expression);
        Assert.Contains("$$message.SenderId", expression);
        Assert.Contains("$literal", expression);
        Assert.Contains("$MessageTrackingVersion", expression);
        var projection = TicketMessageTracking.SummaryProjection(Viewer());
        Assert.Equal("$creatorId", projection["Creator"]["_id"].AsString);
    }

    [Fact]
    public async Task StatisticsCountsUnreadTicketsInsideTheSameAuthorizedQueueAggregation()
    {
        IReadOnlyList<BsonDocument>? stages = null;
        _collection.Setup(x => x.AggregateAsync(It.IsAny<PipelineDefinition<TicketInfo, BsonDocument>>(),
                It.IsAny<AggregateOptions>(), It.IsAny<CancellationToken>()))
            .Returns((PipelineDefinition<TicketInfo, BsonDocument> pipeline, AggregateOptions _, CancellationToken token) =>
            {
                stages = pipeline.Render(BsonSerializer.LookupSerializer<TicketInfo>(), BsonSerializer.SerializerRegistry).Documents.ToArray();
                return Task.FromResult(Cursor(new[]
                {
                    new BsonDocument
                    {
                        { "_id", new TicketInfo { Status = TicketState.InProgress }.ToBsonDocument()["Status"] },
                        { "Count", 4L }, { "UnreadTickets", 2L }
                    }
                }));
            });

        var result = await _service.TicketStats(null, (int)TicketCategory.TechnicalSupport,
            (int)TicketCategory.TrademarkRegistry, true, Viewer(), TicketState.InProgress, false, true);
        Assert.Equal(4, result.total);
        Assert.Equal(4, result.inProgress);
        Assert.Equal(2, result.unreadTickets);
        var match = stages![0]["$match"];
        Assert.Equal("owner", match["creatorId"].AsString);
        Assert.Equal((int)TicketCategory.TechnicalSupport, match["Category"].AsInt32);
        Assert.Equal((int)TicketCategory.TrademarkRegistry, match["RegistryCategory"].AsInt32);
        Assert.True(match["RaisedByRegistryStaff"].AsBoolean);
        Assert.True(match["IsEscalatedToDev"].AsBoolean);
        Assert.Equal(new TicketInfo { Status = TicketState.InProgress }.ToBsonDocument()["Status"], match["Status"]);
        var count = stages[1]["$group"]["UnreadTickets"]["$sum"]["$cond"].AsBsonArray;
        Assert.Equal(1L, count[1].ToInt64());
        Assert.Equal(0L, count[2].ToInt64());
        Assert.Contains(TicketMessageTracking.ReaderKey("owner"), count[0].ToJson());
    }

    [Fact]
    public void InternalReadStateIsNotExposedOrAcceptedThroughJson()
    {
        TrackedTicket();
        _ticket!.ReadMessageSequences[TicketMessageTracking.ReaderKey("owner")] = 5;
        var json = JsonSerializer.Serialize(_ticket, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("readMessageSequences", json);
        Assert.DoesNotContain("unreadBaselineSequence", json);
        Assert.DoesNotContain("messageTrackingVersion", json);
        var input = JsonSerializer.Deserialize<TicketInfo>("{\"readMessageSequences\":{\"forged\":999},\"messageTrackingVersion\":1}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Empty(input!.ReadMessageSequences);
        Assert.Equal(0, input.MessageTrackingVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public async Task ReadEndpointRejectsMissingOrNegativeSequence(long? sequence)
    {
        var request = new ReadTicketRequest { ThroughMessageSequence = sequence };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true));
        var result = await new TicketController(_service).MarkTicketRead("ticket", request);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void ReaderKeysCannotInjectMongoPaths()
    {
        var key = TicketMessageTracking.ReaderKey("$user.with.dots");
        Assert.Equal(64, key.Length);
        Assert.DoesNotContain(".", key);
        Assert.DoesNotContain("$", key);
        Assert.Equal(key, TicketMessageTracking.ReaderKey("$user.with.dots"));
    }
}
