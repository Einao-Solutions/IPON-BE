using MongoDB.Bson.Serialization.Attributes;
using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class RegisterDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; } 
        public string Phone { get; set; }
        public string Password { get; set; } 
    }
    public class LoginDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class AuthUserDto
    {
        public string Token { get; set; }
        public AppUser User { get; set; }

    }
}
