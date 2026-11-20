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
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Tests.Services;

public class UserAccountServiceTests
{
    private readonly Mock<IMongoDatabase> _database = new();
    private readonly Mock<IMongoCollection<AppUser>> _users = new();
    private readonly Mock<IMongoCollection<AccountTypeChangeHistory>> _history = new();
    private readonly Mock<IMongoClient> _client = new(MockBehavior.Strict);
    private readonly UserAccountService _service;
    private readonly List<BsonDocument> _updates = new();
    private readonly List<BsonDocument> _filters = new();
    private readonly List<BsonDocument> _cleanups = new();
    private readonly Dictionary<string, AccountTypeChangeHistory> _exported = new();
    private readonly Queue<UpdateResult> _updateResults = new();
    private bool _failHistory;
    private bool _failCleanup;
    private bool _failUpdate;
    private int _exportAttempts;

    public UserAccountServiceTests()
    {
        _database.Setup(d => d.GetCollection<AppUser>("appUsers", null)).Returns(_users.Object);
        _database.Setup(d => d.GetCollection<AccountTypeChangeHistory>("accountTypeChangeHistory", null)).Returns(_history.Object);
        _database.SetupGet(d => d.Client).Returns(_client.Object);
        _users.Setup(u => u.UpdateOneAsync(It.IsAny<FilterDefinition<AppUser>>(), It.IsAny<UpdateDefinition<AppUser>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<AppUser> filter, UpdateDefinition<AppUser> update, UpdateOptions options, CancellationToken token) =>
            {
                var registry = BsonSerializer.SerializerRegistry;
                var document = update.Render(registry.GetSerializer<AppUser>(), registry).AsBsonDocument;
                if (document.Contains("$push"))
                {
                    if (_failUpdate)
                        return Task.FromException<UpdateResult>(new InvalidOperationException("user write failed"));
                    _updates.Add(document);
                    _filters.Add(filter.Render(registry.GetSerializer<AppUser>(), registry));
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
        _history.Setup(h => h.UpdateOneAsync(It.IsAny<FilterDefinition<AccountTypeChangeHistory>>(),
                It.IsAny<UpdateDefinition<AccountTypeChangeHistory>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<AccountTypeChangeHistory> filter, UpdateDefinition<AccountTypeChangeHistory> update, UpdateOptions options, CancellationToken token) =>
            {
                _exportAttempts++;
                if (_failHistory)
                    return Task.FromException<UpdateResult>(new InvalidOperationException("history write failed"));
                var registry = BsonSerializer.SerializerRegistry;
                var document = update.Render(registry.GetSerializer<AccountTypeChangeHistory>(), registry).AsBsonDocument;
                Assert.True(options.IsUpsert);
                Assert.Single(document);
                var change = BsonSerializer.Deserialize<AccountTypeChangeHistory>(document["$setOnInsert"].AsBsonDocument);
                Assert.Equal(change.Id, filter.Render(registry.GetSerializer<AccountTypeChangeHistory>(), registry)["_id"].AsString);
                _exported.TryAdd(change.Id, change);
                return Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 0, null));
            });
        _service = new UserAccountService(_database.Object, NullLogger<UserAccountService>.Instance);
    }

    [Fact]
    public async Task UpdateAtomicallyAuditsOldAndNewTypesWithoutChangingRoles()
    {
        Seed(new AppUser { Id = "target", FirstName = "Mary", AccountType = AccountType.Individual, UserRoles = new() { Roles.User, Roles.Finance } });
        var result = await Update(AccountType.Officer);
        Assert.NotNull(result);
        Assert.True(result.Changed);
        Assert.Equal(AccountType.Officer, result.AccountType);
        Assert.Equal("Officer", result.AccountTypeName);
        var update = Assert.Single(_updates);
        Assert.Equal((int)AccountType.Officer, update["$set"]["AccountType"].AsInt32);
        Assert.Equal(1, update["$set"]["AccountTypeRevision"].ToInt64());
        Assert.True(update["$set"].AsBsonDocument.Contains("LastUpdatedAt"));
        Assert.DoesNotContain("UserRoles", update.ToJson());
        Assert.DoesNotContain("RoleRevision", update.ToJson());
        var change = Assert.Single(_exported.Values);
        Assert.Equal(AccountType.Individual, change.OldAccountType);
        Assert.Equal(AccountType.Officer, change.NewAccountType);
        Assert.Equal("actor", change.ChangedById);
        Assert.Equal("Super Admin", change.ChangedByName);
        Assert.Equal("admin@example.com", change.ChangedByEmail);
        Assert.Equal("target", change.UserId);
        Assert.Equal("Mary", change.UserName);
        Assert.Equal(DateTimeKind.Utc, change.OccurredAt.Kind);
        Assert.Equal(change.Id, PendingChange().Id);
        Assert.Single(_cleanups);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IndividualZeroIsAValidUpdateFromUnsetType()
    {
        Seed(new AppUser { Id = "target" });
        var result = await Update(AccountType.Individual);
        Assert.True(result!.Changed);
        Assert.Null(Assert.Single(_exported.Values).OldAccountType);
        Assert.True(Assert.Single(_filters)["AccountTypeRevision"].IsBsonNull);
        Assert.True(_filters[0]["AccountType"].IsBsonNull);
    }

    [Fact]
    public async Task SameAccountTypeIsNoOpWithoutHistoryOrTimestampUpdate()
    {
        Seed(new AppUser { Id = "target", AccountType = AccountType.Corporate });
        var result = await Update(AccountType.Corporate);
        Assert.False(result!.Changed);
        Assert.Empty(_updates);
        Assert.Empty(_exported);
        Assert.Empty(_cleanups);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task MissingAndUndefinedAccountTypesAreRejectedBeforeDatabaseAccess(int? value)
    {
        var request = new UpdateAccountTypeDto { AccountType = value.HasValue ? (AccountType)value.Value : null };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAccountType("target", request, "actor"));
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TechAccountTypeDoesNotGrantSuperAdminPermission()
    {
        Seed(new AppUser { Id = "target" }, new AppUser { Id = "actor", AccountType = AccountType.Tech, UserRoles = new() { Roles.Tech } });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Update(AccountType.Officer));
        Assert.Empty(_updates);
    }

    [Fact]
    public async Task MissingUserReturnsNullWithoutWritingHistory()
    {
        Seed(null);
        Assert.Null(await Update(AccountType.Officer));
        Assert.Empty(_updates);
        Assert.Empty(_exported);
    }

    [Fact]
    public async Task ConcurrentAccountChangeIsRereadAndAuditUsesLatestOldValue()
    {
        Seed(new AppUser { Id = "target", AccountType = AccountType.Individual }, null,
            new AppUser { Id = "target", AccountType = AccountType.Corporate, AccountTypeRevision = 1 });
        _updateResults.Enqueue(new UpdateResult.Acknowledged(0, 0, null));
        Assert.True((await Update(AccountType.Officer))!.Changed);
        Assert.Equal(2, _updates.Count);
        Assert.Equal((int)AccountType.Corporate, _filters[1]["AccountType"].AsInt32);
        Assert.Equal(1, _filters[1]["AccountTypeRevision"].ToInt64());
        Assert.Equal(AccountType.Corporate, Assert.Single(_exported.Values).OldAccountType);
    }

    [Fact]
    public async Task PersistentConcurrencyReturnsConflictAndExportsNothing()
    {
        var target = new AppUser { Id = "target" };
        Seed(target, null, target, target);
        for (var i = 0; i < 3; i++)
            _updateResults.Enqueue(new UpdateResult.Acknowledged(0, 0, null));
        var result = await Controller("actor").UpdateAccountType("target", new UpdateAccountTypeDto { AccountType = AccountType.Officer }, _service);
        Assert.IsType<ConflictObjectResult>(result);
        Assert.Empty(_exported);
    }

    [Fact]
    public async Task FailedAtomicWriteCannotExportHistory()
    {
        Seed(new AppUser { Id = "target" });
        _failUpdate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Update(AccountType.Officer));
        Assert.Equal(0, _exportAttempts);
    }

    [Fact]
    public async Task HistoryFailureLeavesPendingEventAndRetryExportsIt()
    {
        Seed(new AppUser { Id = "target" });
        _failHistory = true;
        Assert.True((await Update(AccountType.Officer))!.Changed);
        var pending = PendingChange();
        Assert.Empty(_cleanups);
        Assert.Empty(_exported);
        _failHistory = false;
        SeedPending(pending);
        await _service.RetryPendingAccountTypeHistoryAsync();
        Assert.Equal(pending.Id, Assert.Single(_exported.Values).Id);
        Assert.Single(_cleanups);
    }

    [Fact]
    public async Task CleanupFailureCanReplayWithoutDuplicateHistory()
    {
        Seed(new AppUser { Id = "target" });
        _failCleanup = true;
        Assert.True((await Update(AccountType.Officer))!.Changed);
        var pending = PendingChange();
        _failCleanup = false;
        SeedPending(pending);
        await _service.RetryPendingAccountTypeHistoryAsync();
        Assert.Equal(2, _exportAttempts);
        Assert.Equal(pending.Id, Assert.Single(_exported.Values).Id);
        Assert.Equal(pending.Id, _cleanups.Last()["$pull"]["PendingAccountTypeChanges"]["_id"]["$in"].AsBsonArray[0].AsString);
    }

    [Fact]
    public async Task SafeCurrentTypeLookupExcludesSensitiveUserFields()
    {
        FindOptions<AppUser, UserAccountTypeDto>? options = null;
        _users.Setup(u => u.FindAsync(It.IsAny<FilterDefinition<AppUser>>(), It.IsAny<FindOptions<AppUser, UserAccountTypeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<AppUser>, FindOptions<AppUser, UserAccountTypeDto>, CancellationToken>((_, value, _) => options = value)
            .ReturnsAsync(Cursor(new[] { new UserAccountTypeDto { UserId = "target", AccountType = AccountType.Corporate } }));
        var result = await _service.GetAccountType("target");
        Assert.Equal("Corporate", result!.AccountTypeName);
        var registry = BsonSerializer.SerializerRegistry;
        var projection = options!.Projection.Render(registry.GetSerializer<AppUser>(), registry).Document.ToJson();
        Assert.DoesNotContain("Password", projection);
        Assert.DoesNotContain("Token", projection);
        Assert.DoesNotContain("PendingAccountTypeChanges", projection);
    }

    [Fact]
    public void InternalAuditFieldsAreBsonPersistedButJsonHiddenAndLegacySafe()
    {
        var user = new AppUser { Id = "target", AccountTypeRevision = 1, PendingAccountTypeChanges = new() { new AccountTypeChangeHistory() } };
        var bson = user.ToBsonDocument();
        Assert.Single(bson["PendingAccountTypeChanges"].AsBsonArray);
        var json = JsonSerializer.Serialize(user);
        Assert.DoesNotContain("AccountTypeRevision", json);
        Assert.DoesNotContain("PendingAccountTypeChanges", json);
        var legacy = BsonSerializer.Deserialize<AppUser>(new BsonDocument("_id", "legacy"));
        Assert.Null(legacy.AccountTypeRevision);
        Assert.Empty(legacy.PendingAccountTypeChanges);
    }

    [Theory]
    [InlineData(nameof(UsersController.GetAccountTypes))]
    [InlineData(nameof(UsersController.GetAccountType))]
    [InlineData(nameof(UsersController.UpdateAccountType))]
    public void AccountEndpointsRequireSuperAdmin(string method) =>
        Assert.Contains(typeof(UsersController).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>(), a => a.Roles == "SuperAdmin");

    [Fact]
    public async Task EndpointRequiresCallerAndReportsMissingUser()
    {
        Assert.IsType<UnauthorizedResult>(await Controller(null).UpdateAccountType("target", new UpdateAccountTypeDto { AccountType = AccountType.Officer }, _service));
        Seed(null);
        Assert.IsType<NotFoundObjectResult>(await Controller("actor").UpdateAccountType("target", new UpdateAccountTypeDto { AccountType = AccountType.Officer }, _service));
    }

    [Fact]
    public void OptionsReflectExistingEnum()
    {
        var response = Assert.IsType<OkObjectResult>(Controller(null).GetAccountTypes());
        var options = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(Enum.GetValues<AccountType>().Length, options.GetArrayLength());
        Assert.Equal(0, options[0].GetProperty("id").GetInt32());
        Assert.Equal("Individual", options[0].GetProperty("name").GetString());
    }

    private Task<AccountTypeUpdateResultDto?> Update(AccountType type) => _service.UpdateAccountType("target", new UpdateAccountTypeDto { AccountType = type }, "actor");

    private AccountTypeChangeHistory PendingChange() => BsonSerializer.Deserialize<AccountTypeChangeHistory>(
        _updates.Last()["$push"]["PendingAccountTypeChanges"].AsBsonDocument);

    private void Seed(AppUser? target, AppUser? actor = null, params AppUser[] retries)
    {
        actor ??= new AppUser { Id = "actor", FirstName = "Super", LastName = "Admin", Email = "admin@example.com", UserRoles = new() { Roles.SuperAdmin } };
        var reads = new Queue<AppUser?>(new[] { actor, target }.Concat(retries));
        _users.Setup(u => u.FindAsync(It.IsAny<FilterDefinition<AppUser>>(), It.IsAny<FindOptions<AppUser, AppUser>>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Cursor(reads.Dequeue() is { } user ? new[] { user } : Array.Empty<AppUser>())));
    }

    private void SeedPending(AccountTypeChangeHistory pending) => _users.Setup(u => u.FindAsync(It.IsAny<FilterDefinition<AppUser>>(),
            It.IsAny<FindOptions<AppUser, AppUser>>(), It.IsAny<CancellationToken>()))
        .Returns(() => Task.FromResult(Cursor(new[] { new AppUser { Id = "target", PendingAccountTypeChanges = new() { pending } } })));

    private UsersController Controller(string? actorId) => new(null!, null!)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(actorId == null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, actorId) }, "test"))
            }
        }
    };

    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> items)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(c => c.Current).Returns(items);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }
}
