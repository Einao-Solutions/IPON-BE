using MongoDB.Bson.Serialization.Attributes;
using patentdesign.Enums;

namespace patentdesign.Models;

public class RoleAssignmentHistory
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AssignedById { get; set; } = "";
    public string AssignedByName { get; set; } = "";
    public string AssignedByEmail { get; set; } = "";
    public string AssignedToId { get; set; } = "";
    public string AssignedToName { get; set; } = "";
    public string AssignedToEmail { get; set; } = "";
    public Roles Role { get; set; }
    public string Action { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}
