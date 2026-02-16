using CloudinaryDotNet.Actions;
using patentdesign.Enums;

namespace patentdesign.Dtos.Response
{
    public class UserDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public List<Roles> UserRoles { get; set; }
    }
}
