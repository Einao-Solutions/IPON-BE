using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Org.BouncyCastle.Crypto.Generators;
using patentdesign.Dtos.Request;
using patentdesign.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text;

namespace patentdesign.Services
{
    public class AuthServices
    {
        private readonly IConfiguration _config;
        private static IMongoCollection<AppUser> _users;
        private MongoClient _mongoClient;

        public AuthServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings, IConfiguration config)
        {
            _config = config;

            var useSandbox = patentDesignDbSettings.Value.UseSandbox;

            string digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;

            MongoClientSettings settings = MongoClientSettings.FromUrl(
                new MongoUrl(digitalOcean)
            );
            settings.SslSettings =
                new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            _mongoClient = new MongoClient(settings);
            var pdDb = _mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
            _users = pdDb.GetCollection<AppUser>("appUsers");
        }

        public async Task<bool> CreateUser(RegisterDto req)
        {
            try
            {
                var existing = await _users.Find(u => u.Email == req.Email).FirstOrDefaultAsync();

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(req.Password);

                var user = new AppUser
                {
                    Email = req.Email,
                    FirstName = req.FirstName,
                    LastName = req.LastName,
                    AccountType = req.AccountType,
                    UserRoles = req.UserRoles,
                    isVerified = req.isVerified,
                    Signature = req.Signature,
                    PasswordHash = hashedPassword
                };

                await _users.InsertOneAsync(user);
                return true;
            }
            catch (MongoWriteException)
            {
                return false;
            }
        }

        private string GenerateJwtToken(AppUser user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<AuthUserDto> LoginUser(LoginDto req)
        {
            try
            {
                var user = await _users.Find(u => u.Email == req.Email).FirstOrDefaultAsync();
                if (user == null)
                    return null;
                var validPassword = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
                if (!validPassword)
                    return null;

                var token = GenerateJwtToken(user);
                AuthUserDto authUser = new AuthUserDto
                {
                    Token = token,
                    User = user
                };
                return authUser;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
