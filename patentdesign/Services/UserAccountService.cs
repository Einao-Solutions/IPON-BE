using System.Data;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Services;

public class UserAccountService(IMongoDatabase database, ILogger<UserAccountService> logger)
{
    private readonly IMongoCollection<AppUser> _users = database.GetCollection<AppUser>("appUsers");
    private readonly IMongoCollection<AccountTypeChangeHistory> _history = database.GetCollection<AccountTypeChangeHistory>("accountTypeChangeHistory");

    public Task<UserAccountTypeDto?> GetAccountType(string userId, CancellationToken cancellationToken = default) =>
        _users.Find(u => u.Id == userId)
            .Project(u => new UserAccountTypeDto { UserId = u.Id, AccountType = u.AccountType })
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AccountTypeUpdateResultDto?> UpdateAccountType(string userId, UpdateAccountTypeDto request,
        string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.");
        if (!request.AccountType.HasValue || !Enum.IsDefined(request.AccountType.Value))
            throw new ArgumentException("A valid accountType is required.");
        if (string.IsNullOrWhiteSpace(actorId))
            throw new UnauthorizedAccessException("An authenticated SuperAdmin is required.");

        var actor = await _users.Find(u => u.Id == actorId).FirstOrDefaultAsync(cancellationToken);
        if (actor?.UserRoles?.Contains(Roles.SuperAdmin) != true)
            throw new UnauthorizedAccessException("Only SuperAdmin can update account types.");

        var accountType = request.AccountType.Value;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var target = await _users.Find(u => u.Id == userId).FirstOrDefaultAsync(cancellationToken);
            if (target == null)
                return null;
            if (target.AccountType == accountType)
                return Result(target.Id, accountType, false);

            var now = DateTime.UtcNow;
            var change = new AccountTypeChangeHistory
            {
                ChangedById = actor.Id,
                ChangedByName = DisplayName(actor),
                ChangedByEmail = actor.Email,
                UserId = target.Id,
                UserName = DisplayName(target),
                UserEmail = target.Email,
                OldAccountType = target.AccountType,
                NewAccountType = accountType,
                OccurredAt = now
            };
            var filter = Builders<AppUser>.Filter.Eq(u => u.Id, target.Id)
                & Builders<AppUser>.Filter.Eq(u => u.AccountTypeRevision, target.AccountTypeRevision)
                & Builders<AppUser>.Filter.Eq(u => u.AccountType, target.AccountType);
            // Store the audit intent atomically with the account type, including on standalone MongoDB.
            var update = Builders<AppUser>.Update.Set(u => u.AccountType, accountType)
                .Set(u => u.LastUpdatedAt, now)
                .Set(u => u.AccountTypeRevision, checked((target.AccountTypeRevision ?? 0) + 1))
                .Push(u => u.PendingAccountTypeChanges, change);
            var saved = await _users.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            if (!saved.IsAcknowledged)
                throw new InvalidOperationException("The account type update was not acknowledged by MongoDB.");
            if (saved.MatchedCount == 0)
                continue;

            await TryExportHistory(target.Id, new List<AccountTypeChangeHistory> { change }, cancellationToken);
            return Result(target.Id, accountType, true);
        }

        throw new DBConcurrencyException("The account type changed concurrently. Refresh the user and retry.");
    }

    public async Task RetryPendingAccountTypeHistoryAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<AppUser>.Filter.Exists($"{nameof(AppUser.PendingAccountTypeChanges)}.0");
        using var cursor = await _users.Find(filter)
            .Project(u => new AppUser { Id = u.Id, PendingAccountTypeChanges = u.PendingAccountTypeChanges })
            .ToCursorAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var user in cursor.Current)
                await TryExportHistory(user.Id, user.PendingAccountTypeChanges, cancellationToken);
        }
    }

    private async Task TryExportHistory(string userId, List<AccountTypeChangeHistory> changes, CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
            return;
        try
        {
            foreach (var change in changes)
            {
                var update = new BsonDocument("$setOnInsert", change.ToBsonDocument());
                var saved = await _history.UpdateOneAsync(h => h.Id == change.Id, update,
                    new UpdateOptions { IsUpsert = true }, cancellationToken);
                if (!saved.IsAcknowledged)
                    throw new InvalidOperationException("The account type history write was not acknowledged by MongoDB.");
            }

            var exportedIds = changes.Select(c => c.Id).ToList();
            await _users.UpdateOneAsync(u => u.Id == userId,
                Builders<AppUser>.Update.PullFilter(u => u.PendingAccountTypeChanges, change => exportedIds.Contains(change.Id)),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Account type history export failed for user {UserId}; pending events will be retried", userId);
        }
    }

    private static AccountTypeUpdateResultDto Result(string userId, AccountType accountType, bool changed) => new()
    {
        UserId = userId,
        AccountType = accountType,
        Changed = changed
    };

    private static string DisplayName(AppUser user) =>
        string.IsNullOrWhiteSpace(user.FirstName + user.LastName) ? user.Name ?? "" : $"{user.FirstName} {user.LastName}".Trim();
}
