using MongoDB.Bson.Serialization.Attributes;
using patentdesign.Enums;
using patentdesign.Models;
using System.ComponentModel.DataAnnotations;

namespace patentdesign.Dtos.Request
{
    public class RegisterDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; } 
        public string? BusinessName { get; set; }
        public AccountType AccountType { get; set; }
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
        public LoggedInUserDto User { get; set; }

    }

    public class LoggedInUserDto
    {
        [BsonId]
        public string? Id { get; set; }
        public string? CreatorId { get; set; }
        public string? FirstName { get; set; } 
        public string? LastName { get; set; } 
        public string? Email { get; set; } 
        public string? PhoneNumber { get; set; }
        public List<Roles>? UserRoles { get; set; }
        public AccountType? AccountType { get; set; }
        public DateTime? CreatedAt { get; set; } 
        public DateTime? LastUpdatedAt {  get; set; }
    }
    public class ChangePasswordDto
    {
        public string Email { get; set; }
        public string NewPassword { get; set; }
        [Compare("NewPassword")]
        public string ConfirmPassword { get; set; }
    }

    public class ResetPasswordDto
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }

    public class ProfileDto
    {
        public string? UserId { get; set; }
        public string? FirstName { get; set; } 
        public string? LastName { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; } 
        public string? Nationality { get; set; } 
        public NigerianStates? State { get; set; } = NigerianStates.None;
        public AccountType? AccountType { get; set; }
        public List<Roles>? UserRoles { get; set; }
    }

}
