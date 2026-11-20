using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Tests.Services;

[Trait("Category", "Integration")]
public class UserAccountMongoTests : IDisposable
{
    private readonly MongoClient _client = new("mongodb://localhost:27017/?serverSelectionTimeoutMS=3000");
    private readonly string _databaseName = "pd_account_type_it_" + Guid.NewGuid().ToString("N");
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<AppUser> _users;
    private readonly IMongoCollection<AccountTypeChangeHistory> _history;
    private readonly UserAccountService _service;

    public UserAccountMongoTests()
    {
        _database = _client.GetDatabase(_databaseName);
        _users = _database.GetCollection<AppUser>("appUsers");
        _history = _database.GetCollection<AccountTypeChangeHistory>("accountTypeChangeHistory");
        _service = new UserAccountService(_database, NullLogger<UserAccountService>.Instance);
    }

    [Fact]
    public async Task LegacyStandaloneUpdatePreservesRolesAndOtherUserDetails()
    {
        await SeedActor();
        await _database.GetCollection<BsonDocument>("appUsers").InsertOneAsync(new BsonDocument
        {
            { "_id", "target" }, { "FirstName", "Mary" }, { "Email", "mary@example.com" },
            { "isVerified", true }, { "UserRoles", new BsonArray { (int)Roles.User, (int)Roles.Finance } }
        });
        var result = await _service.UpdateAccountType("target", new UpdateAccountTypeDto { AccountType = AccountType.Officer }, "actor");
        Assert.True(result!.Changed);
        var user = await _users.Find(u => u.Id == "target").SingleAsync();
        Assert.Equal(AccountType.Officer, user.AccountType);
        Assert.Equal(new[] { Roles.User, Roles.Finance }, user.UserRoles);
        Assert.Equal("Mary", user.FirstName);
        Assert.Equal("mary@example.com", user.Email);
        Assert.True(user.isVerified);
        Assert.NotNull(user.LastUpdatedAt);
        Assert.Empty(user.PendingAccountTypeChanges);
        var history = await _history.Find(FilterDefinition<AccountTypeChangeHistory>.Empty).SingleAsync();
        Assert.Null(history.OldAccountType);
        Assert.Equal(AccountType.Officer, history.NewAccountType);
        Assert.Equal("actor", history.ChangedById);
        var current = await _service.GetAccountType("target");
        Assert.Equal("Officer", current!.AccountTypeName);
        Assert.False((await _service.UpdateAccountType("target", new UpdateAccountTypeDto { AccountType = AccountType.Officer }, "actor"))!.Changed);
        Assert.Equal(1, await _history.CountDocumentsAsync(FilterDefinition<AccountTypeChangeHistory>.Empty));
    }

    [Fact]
    public async Task AuditOutageSurvivesServiceRestartAndExportsWithoutDuplicates()
    {
        await SeedActor();
        await _users.InsertOneAsync(new AppUser { Id = "target", AccountType = AccountType.Corporate, UserRoles = new() { Roles.User } });
        var failedHistory = new Mock<IMongoCollection<AccountTypeChangeHistory>>();
        failedHistory.Setup(h => h.UpdateOneAsync(It.IsAny<FilterDefinition<AccountTypeChangeHistory>>(),
                It.IsAny<UpdateDefinition<AccountTypeChangeHistory>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated history outage"));
        var outageDatabase = new Mock<IMongoDatabase>();
        outageDatabase.Setup(d => d.GetCollection<AppUser>("appUsers", null)).Returns(_users);
        outageDatabase.Setup(d => d.GetCollection<AccountTypeChangeHistory>("accountTypeChangeHistory", null)).Returns(failedHistory.Object);
        var serviceDuringOutage = new UserAccountService(outageDatabase.Object, NullLogger<UserAccountService>.Instance);
        Assert.True((await serviceDuringOutage.UpdateAccountType("target", new UpdateAccountTypeDto { AccountType = AccountType.Tech }, "actor"))!.Changed);
        var user = await _users.Find(u => u.Id == "target").SingleAsync();
        var pendingId = Assert.Single(user.PendingAccountTypeChanges).Id;
        Assert.Equal(AccountType.Tech, user.AccountType);
        Assert.Equal(new[] { Roles.User }, user.UserRoles);

        var restartedService = new UserAccountService(_database, NullLogger<UserAccountService>.Instance);
        await restartedService.RetryPendingAccountTypeHistoryAsync();
        await restartedService.RetryPendingAccountTypeHistoryAsync();
        var history = await _history.Find(FilterDefinition<AccountTypeChangeHistory>.Empty).SingleAsync();
        Assert.Equal(pendingId, history.Id);
        Assert.Equal(AccountType.Corporate, history.OldAccountType);
        Assert.Equal(AccountType.Tech, history.NewAccountType);
        Assert.Empty((await _users.Find(u => u.Id == "target").SingleAsync()).PendingAccountTypeChanges);
    }

    private Task SeedActor() => _users.InsertOneAsync(new AppUser { Id = "actor", UserRoles = new() { Roles.SuperAdmin } });

    public void Dispose() => _client.DropDatabase(_databaseName);
}
