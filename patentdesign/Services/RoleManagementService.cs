using System.Data;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Services;

public class RoleManagementService(IMongoDatabase database, ILogger<RoleManagementService> logger)
{
    private readonly IMongoCollection<AppUser> _users = database.GetCollection<AppUser>("appUsers");
    private readonly IMongoCollection<RoleAssignmentHistory> _history = database.GetCollection<RoleAssignmentHistory>("roleAssignmentHistory");

    public async Task<RolePageDto<RoleUserDto>> GetUsersByRole(Roles role, RolePageQuery query, CancellationToken cancellationToken = default)
    {
        var builder = Builders<AppUser>.Filter;
        var filter = builder.AnyEq(u => u.UserRoles, role);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var regex = SearchRegex(query.Search);
            filter &= builder.Or(builder.Regex(u => u.FirstName, regex), builder.Regex(u => u.LastName, regex),
                builder.Regex(u => u.Name, regex), builder.Regex(u => u.Email, regex),
                builder.Regex(u => u.Id, regex), builder.Regex(u => u.CreatorId, regex));
        }

        var total = await _users.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var items = await _users.Find(filter).SortBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
            .Skip(query.Skip).Limit(query.Take)
            .Project(u => new RoleUserDto
            {
                Id = u.Id,
                Name = u.FirstName + " " + u.LastName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                IsVerified = u.isVerified,
                UserRoles = u.UserRoles
            }).ToListAsync(cancellationToken);
        return Page(items, total, query);
    }

    public async Task<RolePageDto<RoleAssignerDto>> GetAssigners(RolePageQuery query, CancellationToken cancellationToken = default)
    {
        // Group before searching so counts include all assignments by each matching actor.
        var grouped = _history.Aggregate()
            .Match(h => h.Action == "Added")
            .SortByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id)
            .Group(h => h.AssignedById, g => new RoleAssignerDto
            {
                Id = g.Key,
                Name = g.First().AssignedByName,
                Email = g.First().AssignedByEmail,
                AssignmentCount = g.Sum(h => 1L),
                LastAssignedAt = g.Max(h => h.OccurredAt)
            });
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var regex = SearchRegex(query.Search);
            var builder = Builders<RoleAssignerDto>.Filter;
            grouped = grouped.Match(builder.Or(builder.Regex(a => a.Id, regex),
                builder.Regex(a => a.Name, regex), builder.Regex(a => a.Email, regex)));
        }

        var count = await grouped.Count().FirstOrDefaultAsync(cancellationToken);
        var items = await grouped.SortByDescending(a => a.LastAssignedAt).ThenBy(a => a.Id)
            .Skip(query.Skip).Limit(query.Take).ToListAsync(cancellationToken);
        return Page(items, count?.Count ?? 0, query);
    }

    public async Task<RolePageDto<RoleAssignmentHistory>> GetHistory(RoleHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var builder = Builders<RoleAssignmentHistory>.Filter;
        var filter = builder.Empty;
        if (!string.IsNullOrWhiteSpace(query.AssignedById))
            filter &= builder.Eq(h => h.AssignedById, query.AssignedById);
        if (!string.IsNullOrWhiteSpace(query.AssignedToId))
            filter &= builder.Eq(h => h.AssignedToId, query.AssignedToId);
        if (query.Role.HasValue)
            filter &= builder.Eq(h => h.Role, query.Role.Value);
        if (!string.IsNullOrWhiteSpace(query.Action))
            filter &= builder.Eq(h => h.Action, query.Action);
        if (query.From.HasValue)
            filter &= builder.Gte(h => h.OccurredAt, query.From.Value.ToUniversalTime());
        if (query.To.HasValue)
            filter &= builder.Lte(h => h.OccurredAt, query.To.Value.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var regex = SearchRegex(query.Search);
            filter &= builder.Or(builder.Regex(h => h.AssignedByName, regex), builder.Regex(h => h.AssignedByEmail, regex),
                builder.Regex(h => h.AssignedToName, regex), builder.Regex(h => h.AssignedToEmail, regex));
        }

        var total = await _history.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var items = await _history.Find(filter).SortByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id)
            .Skip(query.Skip).Limit(query.Take).ToListAsync(cancellationToken);
        return Page(items, total, query);
    }

    public async Task<bool> UpdateUserRoles(UserRoleDto request, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new ArgumentException("UserId is required.");
        var added = request.AddRoles?.Distinct().ToList() ?? new List<Roles>();
        var removed = request.RemoveRoles?.Distinct().ToList() ?? new List<Roles>();
        if (added.Count == 0 && removed.Count == 0)
            throw new ArgumentException("Provide at least one role to add or remove.");
        if (added.Concat(removed).Any(r => !Enum.IsDefined(r)))
            throw new ArgumentException("An invalid role was provided.");
        if (added.Intersect(removed).Any())
            throw new ArgumentException("A role cannot be added and removed in the same request.");

        var actor = await _users.Find(u => u.Id == actorId).FirstOrDefaultAsync(cancellationToken);
        if (actor == null || !actor.UserRoles.Contains(Roles.SuperAdmin))
            throw new UnauthorizedAccessException("Only SuperAdmin can update user roles.");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var target = await _users.Find(u => u.Id == request.UserId).FirstOrDefaultAsync(cancellationToken);
            if (target == null)
                return false;

            var currentRoles = target.UserRoles ?? new List<Roles>();
            var actualAdded = added.Except(currentRoles).ToList();
            var actualRemoved = removed.Intersect(currentRoles).ToList();
            if (actualAdded.Count == 0 && actualRemoved.Count == 0)
                return true;

            var now = DateTime.UtcNow;
            var changes = actualAdded.Select(r => History(actor, target, r, "Added", now))
                .Concat(actualRemoved.Select(r => History(actor, target, r, "Removed", now))).ToList();
            var roles = currentRoles.Except(actualRemoved).Union(actualAdded).ToList();
            var filter = Builders<AppUser>.Filter.Eq(u => u.Id, target.Id)
                & Builders<AppUser>.Filter.Eq(u => u.RoleRevision, target.RoleRevision);
            // Persist the audit intent in the same document/write as the roles, without a transaction.
            var update = Builders<AppUser>.Update.Set(u => u.UserRoles, roles)
                .Set(u => u.LastUpdatedAt, now)
                .Set(u => u.RoleRevision, checked((target.RoleRevision ?? 0) + 1))
                .PushEach(u => u.PendingRoleChanges, changes);
            var result = await _users.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            if (!result.IsAcknowledged)
                throw new InvalidOperationException("The role update was not acknowledged by MongoDB.");
            if (result.MatchedCount == 0)
                continue;

            await TryExportHistory(target.Id, changes, cancellationToken);
            return true;
        }

        throw new DBConcurrencyException("User roles changed concurrently. Refresh the user and retry.");
    }

    public async Task RetryPendingRoleHistoryAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<AppUser>.Filter.Exists($"{nameof(AppUser.PendingRoleChanges)}.0");
        using var cursor = await _users.Find(filter)
            .Project(u => new AppUser { Id = u.Id, PendingRoleChanges = u.PendingRoleChanges })
            .ToCursorAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var user in cursor.Current)
                await TryExportHistory(user.Id, user.PendingRoleChanges, cancellationToken);
        }
    }

    private async Task TryExportHistory(string userId, List<RoleAssignmentHistory> changes, CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
            return;
        try
        {
            foreach (var change in changes)
            {
                // Reuse the persisted event ID: retries and concurrent workers cannot duplicate history.
                var update = new BsonDocument("$setOnInsert", change.ToBsonDocument());
                var result = await _history.UpdateOneAsync(h => h.Id == change.Id, update,
                    new UpdateOptions { IsUpsert = true }, cancellationToken);
                if (!result.IsAcknowledged)
                    throw new InvalidOperationException("The role history write was not acknowledged by MongoDB.");
            }

            var exportedIds = changes.Select(c => c.Id).ToList();
            await _users.UpdateOneAsync(u => u.Id == userId,
                Builders<AppUser>.Update.PullFilter(u => u.PendingRoleChanges, change => exportedIds.Contains(change.Id)),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Role history export failed for user {UserId}; pending events will be retried", userId);
        }
    }

    private static RoleAssignmentHistory History(AppUser actor, AppUser target, Roles role, string action, DateTime now) => new()
    {
        AssignedById = actor.Id,
        AssignedByName = DisplayName(actor),
        AssignedByEmail = actor.Email,
        AssignedToId = target.Id,
        AssignedToName = DisplayName(target),
        AssignedToEmail = target.Email,
        Role = role,
        Action = action,
        OccurredAt = now
    };

    private static string DisplayName(AppUser user) =>
        string.IsNullOrWhiteSpace(user.FirstName + user.LastName) ? user.Name ?? "" : $"{user.FirstName} {user.LastName}".Trim();

    private static BsonRegularExpression SearchRegex(string search) => new(Regex.Escape(search.Trim()), "i");

    private static RolePageDto<T> Page<T>(List<T> items, long total, RolePageQuery query) => new()
    {
        Items = items,
        TotalCount = total,
        Skip = query.Skip,
        Take = query.Take
    };
}
