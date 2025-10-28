using MongoDB.Bson.Serialization.Attributes;
using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class RegisterDto
    {
        public string Id { get; set; } 
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; } 
        public string Password { get; set; } 
        public string Salt { get; set; } 
        public UserTypes UserType { get; set; }
        public List<UserRoles> UserRoles { get; set; } 
        public bool isVerified { get; set; } = false;
        public string? Signature { get; set; }
        public AccountType AccountType { get; set; }
    }
    public class LoginDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }
}
