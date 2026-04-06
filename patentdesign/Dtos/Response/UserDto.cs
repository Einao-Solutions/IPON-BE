using CloudinaryDotNet.Actions;
using patentdesign.Enums;

namespace patentdesign.Dtos.Response
{
    public class UserDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public List<Roles> UserRoles { get; set; }
    
    }
    public class PaginatedUsersDto
    {
        public List<UserDto> Users { get; set; } = new();
        public long TotalCount { get; set; }
    }

    public class UserRoleDto
    {
        public string UserId { get; set; }
        public List<Roles>? AddRoles { get; set; }
        public List<Roles>? RemoveRoles { get; set; }
    }
    public record GetUsersDto
    {
        public string? Name { get; set; }
        public List<Roles>? Roles { get; set; }
        public int? Skip { get; set; } = 0;
        public int? Take { get; set; } = 10;

    }
}
