using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Tests.Services;

[Trait("Category", "Integration")]
public class RoleManagementMongoTests : IDisposable
{
    private readonly MongoClient _client = new("mongodb://localhost:27017/?serverSelectionTimeoutMS=3000");
    private readonly string _databaseName = "pd_role_audit_it_" + Guid.NewGuid().ToString("N");
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<AppUser> _users;
    private readonly IMongoCollection<RoleAssignmentHistory> _history;
    private readonly RoleManagementService _service;

    public RoleManagementMongoTests()
    {
        _database = _client.GetDatabase(_databaseName);
        _users = _database.GetCollection<AppUser>("appUsers");
        _history = _database.GetCollection<RoleAssignmentHistory>("roleAssignmentHistory");
        _service = new RoleManagementService(_database, NullLogger<RoleManagementService>.Instance);
    }

    [Fact]
    public async Task LegacyUserCanBeUpdatedWithoutTransactions()
    {
        await SeedActor();
        await _database.GetCollection<BsonDocument>("appUsers").InsertOneAsync(new BsonDocument
        {
            { "_id", "target" }, { "FirstName", "Mary" },
            { "UserRoles", new BsonArray { (int)Roles.User } }
        });
        Assert.True(await _service.UpdateUserRoles(new UserRoleDto
        {
            UserId = "target", AddRoles = new() { Roles.Finance }, RemoveRoles = new() { Roles.User }
        }, "actor"));
        var user = await _users.Find(u => u.Id == "target").SingleAsync();
        Assert.Equal(new[] { Roles.Finance }, user.UserRoles);
        Assert.Equal(1, user.RoleRevision);
        Assert.Empty(user.PendingRoleChanges);
        var history = await _history.Find(FilterDefinition<RoleAssignmentHistory>.Empty).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.Role == Roles.Finance && h.Action == "Added");
        Assert.Contains(history, h => h.Role == Roles.User && h.Action == "Removed");
        Assert.All(history, h => Assert.Equal("actor", h.AssignedById));
    }

    [Fact]
    public async Task ExportFailureSurvivesServiceRestartAndIsRecovered()
    {
        await SeedActor();
        await _users.InsertOneAsync(new AppUser { Id = "target" });
        var failedHistory = new Mock<IMongoCollection<RoleAssignmentHistory>>();
        failedHistory.Setup(h => h.UpdateOneAsync(It.IsAny<FilterDefinition<RoleAssignmentHistory>>(),
                It.IsAny<UpdateDefinition<RoleAssignmentHistory>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated history outage"));
        var databaseDuringOutage = new Mock<IMongoDatabase>();
        databaseDuringOutage.Setup(d => d.GetCollection<AppUser>("appUsers", null)).Returns(_users);
        databaseDuringOutage.Setup(d => d.GetCollection<RoleAssignmentHistory>("roleAssignmentHistory", null)).Returns(failedHistory.Object);
        var serviceDuringOutage = new RoleManagementService(databaseDuringOutage.Object, NullLogger<RoleManagementService>.Instance);
        Assert.True(await serviceDuringOutage.UpdateUserRoles(new UserRoleDto
        {
            UserId = "target", AddRoles = new() { Roles.Finance, Roles.Staff }
        }, "actor"));
        var user = await _users.Find(u => u.Id == "target").SingleAsync();
        Assert.Contains(Roles.Finance, user.UserRoles);
        var persistedIds = user.PendingRoleChanges.Select(h => h.Id).OrderBy(id => id).ToList();
        Assert.Equal(2, persistedIds.Count);
        Assert.Equal(0, await _history.CountDocumentsAsync(FilterDefinition<RoleAssignmentHistory>.Empty));

        var restartedService = new RoleManagementService(_database, NullLogger<RoleManagementService>.Instance);
        await restartedService.RetryPendingRoleHistoryAsync();
        await restartedService.RetryPendingRoleHistoryAsync();
        user = await _users.Find(u => u.Id == "target").SingleAsync();
        Assert.Empty(user.PendingRoleChanges);
        var history = await _history.Find(FilterDefinition<RoleAssignmentHistory>.Empty).ToListAsync();
        Assert.Equal(persistedIds, history.Select(h => h.Id).OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task AlreadyExportedEventIsNotDuplicatedOrOverwrittenOnReplay()
    {
        var pending = new RoleAssignmentHistory
        {
            Id = "persisted-event", AssignedById = "actor", AssignedToId = "target",
            AssignedByName = "Original admin", Role = Roles.Staff, Action = "Added", OccurredAt = DateTime.UtcNow
        };
        await _history.InsertOneAsync(pending);
        // Simulate history saved before pending cleanup, then a later replay of that event ID.
        pending.AssignedByName = "Must not overwrite original history";
        await _users.InsertOneAsync(new AppUser { Id = "target", PendingRoleChanges = new() { pending } });
        await _service.RetryPendingRoleHistoryAsync();
        await _service.RetryPendingRoleHistoryAsync();
        Assert.Equal(1, await _history.CountDocumentsAsync(FilterDefinition<RoleAssignmentHistory>.Empty));
        Assert.Equal("Original admin", (await _history.Find(h => h.Id == pending.Id).SingleAsync()).AssignedByName);
        Assert.Empty((await _users.Find(u => u.Id == "target").SingleAsync()).PendingRoleChanges);
    }

    [Fact]
    public async Task ConcurrentDistinctRoleChangesPreserveBothRolesAndAuditEvents()
    {
        await SeedActor();
        await _users.InsertOneAsync(new AppUser { Id = "target", UserRoles = new() { Roles.User } });
        var results = await Task.WhenAll(
            _service.UpdateUserRoles(new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Finance } }, "actor"),
            _service.UpdateUserRoles(new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Staff } }, "actor"));
        Assert.All(results, result => Assert.True(result));
        var user = await _users.Find(u => u.Id == "target").SingleAsync();
        Assert.Equal(new[] { Roles.User, Roles.Staff, Roles.Finance }, user.UserRoles.OrderBy(r => r));
        Assert.Empty(user.PendingRoleChanges);
        Assert.Equal(2, await _history.CountDocumentsAsync(FilterDefinition<RoleAssignmentHistory>.Empty));
    }

    [Fact]
    public async Task ConcurrentDuplicateRoleRequestsCreateOneAuditEvent()
    {
        await SeedActor();
        await _users.InsertOneAsync(new AppUser { Id = "target" });
        var request = new UserRoleDto { UserId = "target", AddRoles = new() { Roles.Staff } };
        var results = await Task.WhenAll(_service.UpdateUserRoles(request, "actor"), _service.UpdateUserRoles(request, "actor"));
        Assert.All(results, result => Assert.True(result));
        Assert.Equal(1, await _history.CountDocumentsAsync(FilterDefinition<RoleAssignmentHistory>.Empty));
        Assert.Equal(new[] { Roles.Staff }, (await _users.Find(u => u.Id == "target").SingleAsync()).UserRoles);
    }

    private Task SeedActor() => _users.InsertOneAsync(new AppUser
    {
        Id = "actor", FirstName = "Admin", UserRoles = new() { Roles.SuperAdmin }
    });

    public void Dispose() => _client.DropDatabase(_databaseName);
}
