using Microsoft.AspNetCore.Mvc;
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

namespace patentdesign.Tests.Services;

public class TicketWorkflowTests
{
    private readonly Mock<IMongoCollection<TicketInfo>> _collection = new();
    private readonly TicketServices _service;
    private TicketInfo? _ticket;
    private BsonDocument? _filter;
    private BsonDocument? _update;
    private CancellationToken _cancellationToken;

    public TicketWorkflowTests()
    {
        var database = new Mock<IMongoDatabase>();
        database.Setup(x => x.GetCollection<TicketInfo>("tickets", It.IsAny<MongoCollectionSettings>()))
            .Returns(_collection.Object);
        _service = new TicketServices(database.Object,
            Options.Create(new PatentDesignDBSettings { TicketCollectionName = "tickets" }),
            NullLogger<TicketServices>.Instance);

        _collection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<TicketInfo>>(),
                It.IsAny<FindOptions<TicketInfo, TicketInfo>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var cursor = new Mock<IAsyncCursor<TicketInfo>>();
                cursor.SetupGet(x => x.Current).Returns(_ticket == null ? Array.Empty<TicketInfo>() : new[] { _ticket });
                cursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true).ReturnsAsync(false);
                return Task.FromResult(cursor.Object);
            });
        _collection.Setup(x => x.UpdateManyAsync(
                It.IsAny<FilterDefinition<TicketInfo>>(), It.IsAny<UpdateDefinition<TicketInfo>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<TicketInfo>, UpdateDefinition<TicketInfo>, UpdateOptions, CancellationToken>(
                (filter, update, _, token) => Capture(filter, update, token))
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        _collection.Setup(x => x.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<TicketInfo>>(), It.IsAny<UpdateDefinition<TicketInfo>>(),
                It.IsAny<FindOneAndUpdateOptions<TicketInfo, TicketInfo>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<TicketInfo> filter, UpdateDefinition<TicketInfo> update,
                FindOneAndUpdateOptions<TicketInfo, TicketInfo> _, CancellationToken token) =>
            {
                Capture(filter, update, token);
                var document = _ticket!.ToBsonDocument();
                foreach (var field in _update!["$set"].AsBsonDocument)
                {
                    document[field.Name] = field.Value;
                }
                _ticket = BsonSerializer.Deserialize<TicketInfo>(document);
                return Task.FromResult(_ticket);
            });
    }

    private void Capture(FilterDefinition<TicketInfo> filter, UpdateDefinition<TicketInfo> update, CancellationToken token)
    {
        var serializer = BsonSerializer.LookupSerializer<TicketInfo>();
        _filter = filter.Render(serializer, BsonSerializer.SerializerRegistry);
        _update = update.Render(serializer, BsonSerializer.SerializerRegistry).AsBsonDocument;
        _cancellationToken = token;
    }

    private static BsonValue State(TicketState state) => new TicketInfo { Status = state }.ToBsonDocument()["Status"];

    private static NewCorrespondenceType Message(string sender = "creator", TicketState status = TicketState.AwaitingStaff) => new()
    {
        ticketId = "ticket",
        newStatus = status,
        correspondence = new TicketCorrespondence { SenderId = sender, SenderName = sender, Message = "Reply" }
    };

    private void Resolved(double hoursAgo = 1)
    {
        _ticket = new TicketInfo
        {
            id = "ticket", creatorId = "creator", Category = TicketCategory.TechnicalSupport,
            Status = TicketState.Resolved, ResolvedAt = DateTime.UtcNow.AddHours(-hoursAgo),
            resolution = new ResolveInfo { StaffId = "staff" }, IsEscalatedToDev = true
        };
    }

    [Fact]
    public void ExistingStatusValuesRemainCompatible()
    {
        Assert.Equal(0, (int)TicketState.AwaitingUser);
        Assert.Equal(1, (int)TicketState.AwaitingStaff);
        Assert.Equal(2, (int)TicketState.Closed);
    }

    [Fact]
    public async Task CloseResolvesUsingServerTimeInsteadOfClosingImmediately()
    {
        var before = DateTime.UtcNow;
        Assert.True(await _service.CloseTicketsAsync(new ResolveTicketType
        {
            ticketId = ["ticket"], resolution = new ResolveInfo { Date = DateTime.UtcNow.AddDays(-10), StaffId = "staff" }
        }));
        var set = _update!["$set"];
        Assert.Equal(State(TicketState.Resolved), set["Status"]);
        Assert.InRange(set["ResolvedAt"].ToUniversalTime(), before.AddMilliseconds(-1), DateTime.UtcNow);
        Assert.Equal(set["ResolvedAt"], set["resolution"]["Date"]);
        Assert.True(set["ClosedAt"].IsBsonNull);
        var excluded = _filter!["Status"]["$nin"].AsBsonArray;
        Assert.Contains(State(TicketState.Resolved), excluded);
        Assert.Contains(State(TicketState.Closed), excluded);
    }

    [Fact]
    public async Task EmptyCloseDoesNotWrite()
    {
        Assert.True(await _service.CloseTicketsAsync(new ResolveTicketType { ticketId = [], resolution = new ResolveInfo() }));
        Assert.Null(_update);
    }

    [Fact]
    public async Task AutoCloseTargetsOnlyResolvedTicketsAtLeast24HoursOld()
    {
        using var cancellation = new CancellationTokenSource();
        var before = DateTime.UtcNow;
        Assert.Equal(1, await _service.CloseResolvedTicketsAsync(cancellation.Token));
        Assert.Equal(State(TicketState.Resolved), _filter!["Status"]);
        Assert.True(_filter["ResolvedAt"]["$ne"].IsBsonNull);
        Assert.InRange(_filter["ResolvedAt"]["$lte"].ToUniversalTime(),
            before.AddHours(-24).AddMilliseconds(-1), DateTime.UtcNow.AddHours(-24));
        Assert.Equal(State(TicketState.Closed), _update!["$set"]["Status"]);
        Assert.InRange(_update["$set"]["ClosedAt"].ToUniversalTime(), before.AddMilliseconds(-1), DateTime.UtcNow);
        Assert.Equal(cancellation.Token, _cancellationToken);
    }

    [Theory]
    [InlineData(TicketState.AwaitingStaff)]
    [InlineData(TicketState.Resolved)]
    [InlineData(TicketState.Closed)]
    public async Task CreatorReplyReopensResolvedTicketAndClearsResolution(TicketState clientStatus)
    {
        Resolved();
        var resolvedAt = _ticket!.ResolvedAt;
        var result = await _service.AddMessageAsync(Message(status: clientStatus));
        Assert.Equal(TicketState.InProgress, result!.Status);
        Assert.Null(result.ResolvedAt);
        Assert.Null(result.resolution);
        Assert.Null(result.ClosedAt);
        Assert.True(result.IsEscalatedToDev);
        Assert.Equal(State(TicketState.Resolved), _filter!["Status"]);
        Assert.Equal(new BsonDateTime(resolvedAt!.Value), _filter["ResolvedAt"]);
        Assert.Equal("Reply", _update!["$push"]["Correspondences"]["Message"].AsString);
    }

    [Fact]
    public async Task StaffReplyDoesNotReopenOrResetResolutionDeadline()
    {
        Resolved();
        var resolvedAt = _ticket!.ResolvedAt;
        var result = await _service.AddMessageAsync(Message("staff"));
        Assert.Equal(TicketState.Resolved, result!.Status);
        Assert.Equal(resolvedAt!.Value.Ticks / TimeSpan.TicksPerMillisecond,
            result.ResolvedAt!.Value.Ticks / TimeSpan.TicksPerMillisecond);
        Assert.NotNull(result.resolution);
        Assert.False(_update!["$set"].AsBsonDocument.Contains("ResolvedAt"));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    public async Task ReplyAtOrAfterDeadlineIsRejected(double hoursAgo)
    {
        Resolved(hoursAgo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddMessageAsync(Message()));
        Assert.Null(_update);
    }

    [Fact]
    public async Task ClosedTicketCannotBeReopenedByMessage()
    {
        _ticket = new TicketInfo { id = "ticket", Status = TicketState.Closed };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddMessageAsync(Message()));
        Assert.Null(_update);
    }

    [Fact]
    public async Task MissingTicketReturnsNull()
    {
        Assert.Null(await _service.AddMessageAsync(Message()));
        Assert.Null(await _service.EscalateTicketToDevAsync(new EscalateTicketToDevRequest { TicketId = "missing" }));
    }

    [Theory]
    [InlineData(TicketState.Closed)]
    [InlineData(TicketState.Resolved)]
    [InlineData((TicketState)99)]
    public async Task MessageCannotBypassResolutionWorkflow(TicketState status)
    {
        _ticket = new TicketInfo { id = "ticket", Status = TicketState.InProgress };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddMessageAsync(Message(status: status)));
        Assert.Null(_update);
    }

    [Theory]
    [InlineData(TicketState.AwaitingUser)]
    [InlineData(TicketState.AwaitingStaff)]
    [InlineData(TicketState.InProgress)]
    public async Task ActiveTicketMessagesPreserveSupportedStatusSelection(TicketState status)
    {
        _ticket = new TicketInfo { id = "ticket", Status = TicketState.InProgress };
        var result = await _service.AddMessageAsync(Message(status: status));
        Assert.Equal(status, result!.Status);
    }

    [Fact]
    public async Task DeveloperEscalationMarksTechnicalTicketInProgressWithoutChangingCategory()
    {
        _ticket = new TicketInfo { id = "ticket", Category = TicketCategory.TechnicalSupport, Status = TicketState.AwaitingStaff };
        var result = await _service.EscalateTicketToDevAsync(new EscalateTicketToDevRequest
        {
            TicketId = "ticket", EscalatedById = "staff", EscalatedByName = "Support"
        });
        Assert.Equal(TicketState.InProgress, result!.Status);
        Assert.True(result.IsEscalatedToDev);
        Assert.Equal(TicketCategory.TechnicalSupport, result.Category);
        Assert.False(result.IsEscalated);
        Assert.Equal("staff", result.EscalatedToDevById);
        Assert.NotNull(result.EscalatedToDevAt);
        Assert.False(_update!["$set"].AsBsonDocument.Contains("Category"));
        Assert.Equal((int)TicketCategory.TechnicalSupport, _filter!["Category"].AsInt32);
        Assert.Contains(State(TicketState.Closed), _filter["Status"]["$nin"].AsBsonArray);
        Assert.Contains(State(TicketState.Resolved), _filter["Status"]["$nin"].AsBsonArray);
        Assert.Equal("system", _update["$push"]["Correspondences"]["SenderId"].AsString);
    }

    [Theory]
    [InlineData(TicketCategory.TrademarkRegistry)]
    [InlineData(TicketCategory.PatentDesignRegistry)]
    public async Task NonTechnicalTicketCannotBeEscalatedToDevelopers(TicketCategory category)
    {
        _ticket = new TicketInfo { id = "ticket", Category = category };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.EscalateTicketToDevAsync(new EscalateTicketToDevRequest { TicketId = "ticket" }));
        Assert.Null(_update);
    }

    [Theory]
    [InlineData(TicketState.Resolved)]
    [InlineData(TicketState.Closed)]
    public async Task InactiveTicketCannotBeEscalatedToDevelopers(TicketState status)
    {
        _ticket = new TicketInfo { id = "ticket", Category = TicketCategory.TechnicalSupport, Status = status };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.EscalateTicketToDevAsync(new EscalateTicketToDevRequest { TicketId = "ticket" }));
        Assert.Null(_update);
    }

    [Fact]
    public async Task RepeatedDeveloperEscalationDoesNotDuplicateHistory()
    {
        _ticket = new TicketInfo { id = "ticket", Category = TicketCategory.TechnicalSupport, Status = TicketState.InProgress, IsEscalatedToDev = true };
        Assert.Same(_ticket, await _service.EscalateTicketToDevAsync(new EscalateTicketToDevRequest { TicketId = "ticket" }));
        Assert.Null(_update);
    }

    [Theory]
    [InlineData(TicketState.Closed)]
    [InlineData(TicketState.Resolved)]
    [InlineData((TicketState)99)]
    public async Task CreationCannotStartInTerminalOrInvalidStatus(TicketState status)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateTicketAsync(new TicketInfo { Status = status }));
    }

    [Fact]
    public async Task ConcurrentClosurePreventsReplyFromReopeningClosedTicket()
    {
        Resolved();
        _collection.Setup(x => x.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<TicketInfo>>(), It.IsAny<UpdateDefinition<TicketInfo>>(),
                It.IsAny<FindOneAndUpdateOptions<TicketInfo, TicketInfo>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<TicketInfo> filter, UpdateDefinition<TicketInfo> update,
                FindOneAndUpdateOptions<TicketInfo, TicketInfo> _, CancellationToken token) =>
            {
                Capture(filter, update, token);
                _ticket!.Status = TicketState.Closed;
                return Task.FromResult<TicketInfo>(null!);
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddMessageAsync(Message()));
        Assert.Equal(TicketState.Closed, _ticket!.Status);
        Assert.Equal(State(TicketState.Resolved), _filter!["Status"]);
        _collection.Verify(x => x.FindOneAndUpdateAsync(
            It.IsAny<FilterDefinition<TicketInfo>>(), It.IsAny<UpdateDefinition<TicketInfo>>(),
            It.IsAny<FindOneAndUpdateOptions<TicketInfo, TicketInfo>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ControllerReturnsBadRequestForNonTechnicalDeveloperEscalation()
    {
        _ticket = new TicketInfo { id = "ticket", Category = TicketCategory.TrademarkRegistry };
        var result = await new TicketController(_service).EscalateTicketToDev(new EscalateTicketToDevRequest { TicketId = "ticket" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ControllerReturnsConflictForExpiredReply()
    {
        Resolved(25);
        Assert.IsType<ConflictObjectResult>(await new TicketController(_service).AddMessageToTicket(Message()));
    }

    [Fact]
    public async Task ControllerReturnsBadRequestForMissingResolutionDetails()
    {
        var result = await new TicketController(_service).CloseTickets(new ResolveTicketType
        {
            ticketId = ["ticket"], resolution = null!
        });
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(_update);
    }

    [Fact]
    public async Task ServiceInstancesDoNotShareCollections()
    {
        _ = new TicketWorkflowTests();
        Resolved();
        Assert.NotNull(await _service.AddMessageAsync(Message()));
    }
}
