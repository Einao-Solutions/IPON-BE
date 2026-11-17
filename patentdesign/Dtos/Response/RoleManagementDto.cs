using System.ComponentModel.DataAnnotations;
using patentdesign.Enums;

namespace patentdesign.Dtos.Response;

public class RolePageQuery
{
    [Range(0, int.MaxValue)]
    public int Skip { get; set; }
    [Range(1, 100)]
    public int Take { get; set; } = 20;
    [StringLength(200)]
    public string? Search { get; set; }
}

public class RoleHistoryQuery : RolePageQuery, IValidatableObject
{
    public string? AssignedById { get; set; }
    public string? AssignedToId { get; set; }
    [EnumDataType(typeof(Roles))]
    public Roles? Role { get; set; }
    [RegularExpression("^(Added|Removed)$")]
    public string? Action { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From.HasValue && To.HasValue && From > To)
            yield return new ValidationResult("From must not be later than To.", new[] { nameof(From), nameof(To) });
    }
}

public class RolePageDto<T>
{
    public List<T> Items { get; set; } = new();
    public long TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RoleUserDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public bool IsVerified { get; set; }
    public List<Roles> UserRoles { get; set; } = new();
}

public class RoleAssignerDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public long AssignmentCount { get; set; }
    public DateTime LastAssignedAt { get; set; }
}
