using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using patentdesign.Controllers;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Tests.Services;

public class RoleManagementServiceTests
{
    private readonly Mock<IMongoDatabase> _database = new();
    private readonly Mock<IMongoClient> _client = new();
    private readonly Mock<IMongoCollection<AppUser>> _users = new();
    private readonly Mock<IMongoCollection<RoleAssignmentHistory>> _history = new();
    private readonly RoleManagementService _service;
    private readonly List<RoleAssignmentHistory> _changes = new();
    private readonly List<BsonDocument> _roleWrites = new();
    private readonly List<BsonDocument> _roleFilters = new();
    private readonly List<BsonDocument> _cleanups = new();
    private readonly Queue<UpdateResult> _updateResults = new();
    private bool _failCleanup;

    public RoleManagementServiceTests()
    {
        _database.Setup(d => d.GetCollection<AppUser>("appUsers", null)).Returns(_users.Object);
        _database.Setup(d => d.GetCollection<RoleAssignmentHistory>("roleAssignmentHistory", null)).Returns(_history.Object);
        _database.SetupGet(d => d.Client).Returns(_client.Object);
        _users.Setup(u => u.UpdateOneAsync(It.IsAny<FilterDefinition<AppUser>>(),
                It.IsAny<UpdateDefinition<AppUser>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<AppUser> filter, UpdateDefinition<AppUser> update, UpdateOptions options, CancellationToken token) =>
            {
                var registry = BsonSerializer.SerializerRegistry;
                var document = update.Render(registry.GetSerializer<AppUser>(), registry).AsBsonDocument;
                if (document.Contains("$push"))
                {
                    _roleWrites.Add(document);
                    _roleFilters.Add(filter.Render(registry.GetSerializer<AppUser>(), registry));
                    if (_updateResults.Count > 0)
                        return Task.FromResult(_updateResults.Dequeue());
                }
                else
                {
                    _cleanups.Add(document);
                    if (_failCleanup)
                        return Task.FromException<UpdateResult>(new InvalidOperationException("cleanup failed"));
                }
                return Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 1, null));
            });
        SetupHistoryWrites();
        _service = new RoleManagementService(_database.Object, NullLogger<RoleManagementService>.Instance);
    }

    [Fact]
    public async Task UpdateRecordsOnlyActualChangesWithActorAndRecipientSnapshots()
    {
        Seed(new AppUser { Id = "target", FirstName = "Mary", Email = "mary@example.com", UserRoles = new() { Roles.User, Roles.Staff } });
        var before = DateTime.UtcNow;
        var result = await _service.UpdateUserRoles(new UserRoleDto
        {
            UserId = "target",
            AddRoles = new() { Roles.User, Roles.Finance, Roles.Finance },
            RemoveRoles = new() { Roles.Staff, Roles.Tech }
        }, "actor");

        Assert.True(result);
        Assert.Equal(2, _changes.Count);
        Assert.Contains(_changes, h => h.Role == Roles.Finance && h.Action == "Added");
        Assert.Contains(_changes, h => h.Role == Roles.Staff && h.Action == "Removed");
        Assert.All(_changes, h =>
        {
            Assert.Equal("actor", h.AssignedById);
            Assert.Equal("Admin User", h.AssignedByName);
            Assert.Equal("admin@example.com", h.AssignedByEmail);
            Assert.Equal("target", h.AssignedToId);
            Assert.Equal("Mary", h.AssignedToName);
            Assert.Equal("mary@example.com", h.AssignedToEmail);
            Assert.InRange(h.OccurredAt, before.AddMilliseconds(-1), DateTime.UtcNow);
            Assert.Equal(DateTimeKind.Utc, h.OccurredAt.Kind);
        });
        var write = Assert.Single(_roleWrites);
        Assert.Equal(1, write["$set"]["RoleRevision"].ToInt64());
        Assert.Equal(2, write["$push"]["PendingRoleChanges"]["$each"].AsBsonArray.Count);
        Assert.Equal(new[] { (int)Roles.User, (int)Roles.Finance }, write["$set"]["UserRoles"].AsBsonArray.Select(r => r.AsInt32));
        Assert.True(Assert.Single(_roleFilters)["RoleRevision"].IsBsonNull);
        Assert.Single(_cleanups);
        _client.Verify(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoOpDoesNotWriteHistoryOrUser()
    {
        Seed(new AppUser { Id = "target", UserRoles = new() { Roles.User } });
        Assert.True(await _service.UpdateUserRoles(new UserRoleDto
        {
            UserId = "target", AddRoles = new() { Roles.User }, RemoveRoles = new() { Roles.Finance }
        }, "actor"));
        Assert.Empty(_changes);
        _users.Verify(u => u.UpdateOneAsync(It.IsAny<FilterDefinition<AppUser>>(),
            It.IsAny<UpdateDefinition<AppUser>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_roleWrites);
        Assert.Empty(_cleanups);
    }

    [Fact]
    public async Task MissingTargetReturnsFalse()
    {
        Seed(null);
        Assert.False(await _service.UpdateUserRoles(new UserRoleDto { UserId = "missing", AddRoles = new() { Roles.Staff } }, "actor"));
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task ActorMustStillBeSuperAdminInDatabase()
    {
        Seed(new AppUser { Id = "target" }, new AppUser { Id = "actor", UserRoles = new() { Roles.User } });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.UpdateUserRoles(
            new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Staff } }, "actor"));
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task AuditFailureLeavesDurablePendingEventAndDoesNotFailSuccessfulRoleUpdate()
    {
        Seed(new AppUser { Id = "target" });
        FailHistoryWrites();
        Assert.True(await _service.UpdateUserRoles(
            new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Staff } }, "actor"));
        var pending = Assert.Single(PendingChanges());
        Assert.Equal(Roles.Staff, pending.Role);
        Assert.Empty(_cleanups);

        SetupHistoryWrites();
        SeedPendingUsers(new AppUser { Id = "target", PendingRoleChanges = new() { pending } });
        await _service.RetryPendingRoleHistoryAsync();
        Assert.Equal(pending.Id, Assert.Single(_changes).Id);
        Assert.Single(_cleanups);
    }

    [Fact]
    public async Task InvalidChangesAreRejectedBeforeOpeningSession()
    {
        var requests = new[]
        {
            new UserRoleDto { UserId = "" },
            new UserRoleDto { UserId = "target" },
            new UserRoleDto { UserId = "target", AddRoles = new() { (Roles)999 } },
            new UserRoleDto { UserId = "target", AddRoles = new() { Roles.User }, RemoveRoles = new() { Roles.User } }
        };
        foreach (var request in requests)
            await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateUserRoles(request, "actor"));
        _client.Verify(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(nameof(UsersController.UpdateUserRoles))]
    [InlineData(nameof(UsersController.GetRoles))]
    [InlineData(nameof(UsersController.GetUsersByRole))]
    [InlineData(nameof(UsersController.GetRoleAssigners))]
    [InlineData(nameof(UsersController.GetRoleAssignmentHistory))]
    public void RoleEndpointsRequireSuperAdmin(string method)
    {
        var attributes = typeof(UsersController).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>();
        Assert.Contains(attributes, a => a.Roles == "SuperAdmin");
    }

    [Fact]
    public async Task UpdateEndpointRequiresAuthenticatedActorIdentifier()
    {
        var controller = new UsersController(null!, _service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "SuperAdmin") }, "test")) }
            }
        };
        Assert.IsType<UnauthorizedResult>(await controller.UpdateUserRoles(new UserRoleDto { UserId = "target" }));
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public void PaginationRejectsInvalidBounds(int skip, int take)
    {
        var query = new RolePageQuery { Skip = skip, Take = take };
        Assert.False(Validator.TryValidateObject(query, new ValidationContext(query), new List<ValidationResult>(), true));
    }

    [Fact]
    public void HistoryRejectsReversedDatesAndUndefinedRoles()
    {
        var query = new RoleHistoryQuery { From = DateTime.UtcNow, To = DateTime.UtcNow.AddDays(-1) };
        Assert.False(Validator.TryValidateObject(query, new ValidationContext(query), new List<ValidationResult>(), true));
        query = new RoleHistoryQuery { Role = (Roles)999 };
        Assert.False(Validator.TryValidateObject(query, new ValidationContext(query), new List<ValidationResult>(), true));
    }

    [Fact]
    public async Task RoleQueryRendersLiteralSearchAndSafePaginatedProjection()
    {
        FilterDefinition<AppUser>? capturedFilter = null;
        FindOptions<AppUser, RoleUserDto>? capturedOptions = null;
        _users.Setup(u => u.FindAsync(It.IsAny<FilterDefinition<AppUser>>(),
                It.IsAny<FindOptions<AppUser, RoleUserDto>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<AppUser>, FindOptions<AppUser, RoleUserDto>, CancellationToken>((filter, options, _) =>
            {
                capturedFilter = filter;
                capturedOptions = options;
            }).ReturnsAsync(ResultCursor(Array.Empty<RoleUserDto>()));
        var result = await _service.GetUsersByRole(Roles.Finance, new RolePageQuery { Search = ".*", Skip = 10, Take = 5 });
        var registry = BsonSerializer.SerializerRegistry;
        var filter = capturedFilter!.Render(registry.GetSerializer<AppUser>(), registry);
        Assert.Equal((int)Roles.Finance, filter["UserRoles"].AsInt32);
        var regex = filter["$or"].AsBsonArray[0]["FirstName"].AsBsonRegularExpression;
        Assert.Equal(@"\.\*", regex.Pattern);
        Assert.Equal("i", regex.Options);
        Assert.Equal(10, capturedOptions!.Skip);
        Assert.Equal(5, capturedOptions.Limit);
        var projection = capturedOptions.Projection.Render(registry.GetSerializer<AppUser>(), registry).Document.ToJson();
        Assert.DoesNotContain("Password", projection);
        Assert.DoesNotContain("Token", projection);
        Assert.Equal(10, result.Skip);
        Assert.Equal(5, result.Take);
    }

    [Fact]
    public async Task HistoryQueryRendersActorRecipientRoleActionAndDates()
    {
        FilterDefinition<RoleAssignmentHistory>? captured = null;
        _history.Setup(h => h.FindAsync(It.IsAny<FilterDefinition<RoleAssignmentHistory>>(),
                It.IsAny<FindOptions<RoleAssignmentHistory, RoleAssignmentHistory>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<RoleAssignmentHistory>, FindOptions<RoleAssignmentHistory, RoleAssignmentHistory>, CancellationToken>((filter, _, _) => captured = filter)
            .ReturnsAsync(ResultCursor(Array.Empty<RoleAssignmentHistory>()));
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow;
        await _service.GetHistory(new RoleHistoryQuery
        {
            AssignedById = "actor", AssignedToId = "target", Role = Roles.Staff, Action = "Added", From = from, To = to
        });
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = captured!.Render(registry.GetSerializer<RoleAssignmentHistory>(), registry);
        Assert.Equal("actor", rendered["AssignedById"].AsString);
        Assert.Equal("target", rendered["AssignedToId"].AsString);
        Assert.Equal((int)Roles.Staff, rendered["Role"].AsInt32);
        Assert.Equal("Added", rendered["Action"].AsString);
        Assert.True(rendered["OccurredAt"].AsBsonDocument.Contains("$gte"));
        Assert.True(rendered["OccurredAt"].AsBsonDocument.Contains("$lte"));
    }

    [Fact]
    public async Task AssignerAggregationGroupsBeforeSearchAndPagination()
    {
        PipelineDefinition<RoleAssignmentHistory, RoleAssignerDto>? captured = null;
        var registry = BsonSerializer.SerializerRegistry;
        _history.SetupGet(h => h.DocumentSerializer).Returns(registry.GetSerializer<RoleAssignmentHistory>());
        _history.SetupGet(h => h.Settings).Returns(new MongoCollectionSettings());
        _history.Setup(h => h.AggregateAsync(It.IsAny<PipelineDefinition<RoleAssignmentHistory, AggregateCountResult>>(),
                It.IsAny<AggregateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultCursor(Array.Empty<AggregateCountResult>()));
        _history.Setup(h => h.AggregateAsync(It.IsAny<PipelineDefinition<RoleAssignmentHistory, RoleAssignerDto>>(),
                It.IsAny<AggregateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineDefinition<RoleAssignmentHistory, RoleAssignerDto>, AggregateOptions, CancellationToken>((pipeline, _, _) => captured = pipeline)
            .ReturnsAsync(ResultCursor(Array.Empty<RoleAssignerDto>()));
        var result = await _service.GetAssigners(new RolePageQuery { Search = "admin", Skip = 5, Take = 10 });
        var stages = captured!.Render(registry.GetSerializer<RoleAssignmentHistory>(), registry).Documents.ToList();
        Assert.Equal("Added", stages[0]["$match"]["Action"].AsString);
        var groupIndex = stages.FindIndex(s => s.Contains("$group"));
        var searchIndex = stages.FindLastIndex(s => s.Contains("$match"));
        Assert.True(groupIndex > 0);
        Assert.True(searchIndex > groupIndex);
        Assert.Contains(stages, s => s.Contains("$skip") && s["$skip"].ToInt32() == 5);
        Assert.Contains(stages, s => s.Contains("$limit") && s["$limit"].ToInt32() == 10);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private static IAsyncCursor<T> ResultCursor<T>(IEnumerable<T> items)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(c => c.Current).Returns(items);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }

    [Fact]
    public async Task ConcurrentEditRereadsRolesInsteadOfOverwritingAnotherChange()
    {
        Seed(new AppUser { Id = "target", UserRoles = new() { Roles.User } }, null,
            new AppUser { Id = "target", RoleRevision = 1, UserRoles = new() { Roles.User, Roles.Staff } });
        _updateResults.Enqueue(new UpdateResult.Acknowledged(0, 0, null));
        Assert.True(await _service.UpdateUserRoles(new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Finance } }, "actor"));
        Assert.Equal(2, _roleWrites.Count);
        Assert.Equal(1, _roleFilters[1]["RoleRevision"].ToInt64());
        Assert.Equal(2, _roleWrites[1]["$set"]["RoleRevision"].ToInt64());
        Assert.Equal(new[] { (int)Roles.User, (int)Roles.Staff, (int)Roles.Finance },
            _roleWrites[1]["$set"]["UserRoles"].AsBsonArray.Select(r => r.AsInt32));
        Assert.Single(_changes);
        Assert.Equal(Roles.Finance, _changes[0].Role);
    }

    [Fact]
    public async Task PersistentContentionProducesConflictWithoutExportingUncommittedEvents()
    {
        var target = new AppUser { Id = "target" };
        Seed(target, null, target, target);
        for (var i = 0; i < 3; i++)
            _updateResults.Enqueue(new UpdateResult.Acknowledged(0, 0, null));
        var controller = new UsersController(null!, _service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "actor") }, "test"))
                }
            }
        };
        Assert.IsType<ConflictObjectResult>(await controller.UpdateUserRoles(
            new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Staff } }));
        Assert.Equal(3, _roleWrites.Count);
        Assert.Empty(_changes);
        Assert.Empty(_cleanups);
    }

    [Fact]
    public async Task FailedAtomicUpdateCannotCreateHistory()
    {
        Seed(new AppUser { Id = "target" });
        _users.Setup(u => u.UpdateOneAsync(It.IsAny<FilterDefinition<AppUser>>(), It.IsAny<UpdateDefinition<AppUser>>(),
            It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("write failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateUserRoles(
            new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Staff } }, "actor"));
        Assert.Empty(_changes);
        Assert.Empty(_cleanups);
    }

    [Fact]
    public async Task CleanupFailureReplaysSameEventIdWithInsertOnlyUpsert()
    {
        Seed(new AppUser { Id = "target" });
        _failCleanup = true;
        Assert.True(await _service.UpdateUserRoles(new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Staff } }, "actor"));
        var pending = Assert.Single(PendingChanges());
        _failCleanup = false;
        SeedPendingUsers(new AppUser { Id = "target", PendingRoleChanges = new() { pending } });
        await _service.RetryPendingRoleHistoryAsync();
        Assert.Equal(2, _changes.Count);
        Assert.Single(_changes.Select(h => h.Id).Distinct());
        var cleanup = _cleanups.Last()["$pull"]["PendingRoleChanges"]["_id"]["$in"].AsBsonArray;
        Assert.Equal(pending.Id, Assert.Single(cleanup).AsString);
    }

    [Fact]
    public async Task RetryFailureForOneUserDoesNotBlockAnotherUsersHistory()
    {
        var first = new RoleAssignmentHistory { Id = "first", AssignedToId = "one" };
        var second = new RoleAssignmentHistory { Id = "second", AssignedToId = "two" };
        SeedPendingUsers(new AppUser { Id = "one", PendingRoleChanges = new() { first } },
            new AppUser { Id = "two", PendingRoleChanges = new() { second } });
        _history.SetupSequence(h => h.UpdateOneAsync(It.IsAny<FilterDefinition<RoleAssignmentHistory>>(),
                It.IsAny<UpdateDefinition<RoleAssignmentHistory>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("first user failed"))
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 0, null));
        await _service.RetryPendingRoleHistoryAsync();
        Assert.Single(_cleanups);
        Assert.Equal("second", _cleanups[0]["$pull"]["PendingRoleChanges"]["_id"]["$in"].AsBsonArray[0].AsString);
    }

    [Fact]
    public void PendingAuditMetadataIsPersistedButNeverSerializedToJson()
    {
        var user = new AppUser { Id = "target", RoleRevision = 4, PendingRoleChanges = new() { new RoleAssignmentHistory() } };
        var bson = user.ToBsonDocument();
        Assert.Equal(4, bson["RoleRevision"].ToInt64());
        Assert.Single(bson["PendingRoleChanges"].AsBsonArray);
        var json = JsonSerializer.Serialize(user);
        Assert.DoesNotContain("RoleRevision", json);
        Assert.DoesNotContain("PendingRoleChanges", json);
        var legacy = BsonSerializer.Deserialize<AppUser>(new BsonDocument("_id", "legacy"));
        Assert.Null(legacy.RoleRevision);
        Assert.Empty(legacy.PendingRoleChanges);
    }

    private List<RoleAssignmentHistory> PendingChanges()
    {
        var pushed = _roleWrites.Last()["$push"]["PendingRoleChanges"].AsBsonDocument;
        var changes = pushed.TryGetValue("$each", out var each) ? each.AsBsonArray : new BsonArray { pushed };
        return changes.Select(change => BsonSerializer.Deserialize<RoleAssignmentHistory>(change.AsBsonDocument)).ToList();
    }

    private void SetupHistoryWrites()
    {
        _history.Setup(h => h.UpdateOneAsync(It.IsAny<FilterDefinition<RoleAssignmentHistory>>(),
                It.IsAny<UpdateDefinition<RoleAssignmentHistory>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<RoleAssignmentHistory>, UpdateDefinition<RoleAssignmentHistory>, UpdateOptions, CancellationToken>((filter, update, options, _) =>
            {
                var registry = BsonSerializer.SerializerRegistry;
                var document = update.Render(registry.GetSerializer<RoleAssignmentHistory>(), registry).AsBsonDocument;
                Assert.True(options.IsUpsert);
                Assert.Single(document);
                var change = BsonSerializer.Deserialize<RoleAssignmentHistory>(document["$setOnInsert"].AsBsonDocument);
                Assert.Equal(change.Id, filter.Render(registry.GetSerializer<RoleAssignmentHistory>(), registry)["_id"].AsString);
                _changes.Add(change);
            }).ReturnsAsync(new UpdateResult.Acknowledged(1, 0, null));
    }

    private void FailHistoryWrites() => _history.Setup(h => h.UpdateOneAsync(It.IsAny<FilterDefinition<RoleAssignmentHistory>>(),
        It.IsAny<UpdateDefinition<RoleAssignmentHistory>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("history write failed"));

    private void SeedPendingUsers(params AppUser[] users) => _users.Setup(u => u.FindAsync(It.IsAny<FilterDefinition<AppUser>>(),
        It.IsAny<FindOptions<AppUser, AppUser>>(), It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(ResultCursor(users)));

    private void Seed(AppUser? target, AppUser? actor = null, params AppUser[] retries)
    {
        actor ??= new AppUser { Id = "actor", FirstName = "Admin", LastName = "User", Email = "admin@example.com", UserRoles = new() { Roles.SuperAdmin } };
        var reads = new Queue<AppUser?>(new[] { actor, target }.Concat(retries));
        _users.Setup(u => u.FindAsync(It.IsAny<FilterDefinition<AppUser>>(),
                It.IsAny<FindOptions<AppUser, AppUser>>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Cursor(reads.Dequeue())));
    }

    private static IAsyncCursor<AppUser> Cursor(AppUser? user)
    {
        var cursor = new Mock<IAsyncCursor<AppUser>>();
        cursor.SetupGet(c => c.Current).Returns(user == null ? Array.Empty<AppUser>() : new[] { user });
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }
}
