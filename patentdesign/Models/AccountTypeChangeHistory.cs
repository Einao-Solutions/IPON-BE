using MongoDB.Bson.Serialization.Attributes;
using patentdesign.Enums;

namespace patentdesign.Models;

public class AccountTypeChangeHistory
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChangedById { get; set; } = "";
    public string ChangedByName { get; set; } = "";
    public string ChangedByEmail { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public AccountType? OldAccountType { get; set; }
    public AccountType NewAccountType { get; set; }
    public DateTime OccurredAt { get; set; }
}
