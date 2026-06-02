using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Ocsp;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace patentdesign.Services
{
    public class AuthServices
    {
        private readonly IConfiguration _config;
        private static IMongoCollection<AppUser> _users;
        private static IMongoCollection<Filling> _fillingCollection;
        private MongoClient _mongoClient;
        private EmailServices _emailServices;
        private readonly ILogger<AuthServices> _log;
        public AuthServices(IMongoDatabase db, IOptions<PatentDesignDBSettings> patentDesignDbSettings, IConfiguration config, EmailServices emailServices, ILogger<AuthServices> log)
        {
            _config = config;
            _log = log;

            var s = patentDesignDbSettings.Value;
            _users = db.GetCollection<AppUser>("appUsers");
            _fillingCollection = db.GetCollection<Filling>(s.FilesCollectionName);
            _emailServices = emailServices;
        }

        public async Task<bool> CreateUser(RegisterDto req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return false;

            try
            {
                _log.LogInformation("Creating user with email {Email}", req.Email);
                var emailNormalized = req.Email.Trim().ToLowerInvariant();
                var existing = await _users.Find(u => u.Email == emailNormalized).FirstOrDefaultAsync();
                if (existing != null)
                {
                    _log.LogWarning("User creation failed — email {Email} already exists", emailNormalized);
                    return false;
                }

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(req.Password);

                var user = new AppUser
                {
                    Id = Guid.NewGuid().ToString(),
                    CreatorId = Guid.NewGuid().ToString(),
                    Email = emailNormalized,
                    FirstName = req.FirstName?.Trim() ?? string.Empty,
                    LastName = req.LastName?.Trim() ?? string.Empty,
                    PhoneNumber = req.Phone?.Trim() ?? string.Empty,
                    AccountType = (AccountType)req.AccountType,
                    PasswordHash = hashedPassword,
                    UserRoles = new List<Roles> { Roles.User },
                    CreatedAt = DateTime.UtcNow,
                    Name = req.BusinessName ?? req.FirstName + " " + req.LastName,
                };

                await _users.InsertOneAsync(user);
                _log.LogInformation("User {Email} created successfully with Id {UserId}", emailNormalized, user.Id);
                return true;
            }
            catch (MongoWriteException ex)
            {
                _log.LogError(ex, "MongoDB write error creating user {Email}", req.Email);
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error creating user {Email}", req.Email);
                return false;
            }
        }

        private string GenerateJwtToken(AppUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email),
            };

            if (user.UserRoles != null)
            {
                claims.AddRange(user.UserRoles
                    .Distinct()
                    .Select(role => new Claim(ClaimTypes.Role, role.ToString())));
            }

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
                _log.LogInformation("Login attempt for {Email}", req.Email);
                var user = await _users.Find(u => u.Email == req.Email).FirstOrDefaultAsync();
                if (user == null)
                {
                    _log.LogWarning("Login failed — user {Email} not found", req.Email);
                    return null;
                }
                var validPassword = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
                if (!validPassword)
                {
                    _log.LogWarning("Login failed — invalid password for {Email}", req.Email);
                    return null;
                }
                LoggedInUserDto dto = new LoggedInUserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    UserRoles = user.UserRoles,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    PhoneNumber = user.PhoneNumber,
                    AccountType = user.AccountType,
                    CreatedAt = user.CreatedAt,
                    CreatorId = user.CreatorId,
                    LastUpdatedAt = user?.LastUpdatedAt,

                };
                var token = GenerateJwtToken(user);
                AuthUserDto authUser = new AuthUserDto
                {
                    Token = token,
                    User = dto
                };
                _log.LogInformation("User {Email} logged in successfully", req.Email);
                return authUser;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error during login for {Email}", req.Email);
                throw;
            }
        }
        private NigerianStates MapToNigerianState(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return NigerianStates.None;

            var s = raw.Trim();

            s = Regex.Replace(s, @"\bstate\b", "", RegexOptions.IgnoreCase).Trim();

            var alias = s.ToLowerInvariant();
            switch (alias)
            {
                case "abuja":
                case "fct":
                case "federal capital territory":
                case "federal capital":
                    return NigerianStates.FederalCapitalTerritory;
            }

            var cleaned = Regex.Replace(s, @"[^A-Za-z\s]", " ").Trim();
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            var key = cleaned.Replace(" ", "");

            if (Enum.TryParse<NigerianStates>(key, true, out var parsed))
                return parsed;

            if (Enum.TryParse<NigerianStates>(cleaned, true, out parsed))
                return parsed;

            return NigerianStates.None;
        }
        private List<Roles> MapToRole(List<UserRoles> roles)
        {
            if (roles == null || roles.Count == 0) return new List<Roles> { Roles.User };
            var mappedRoles = new List<Roles>();
            foreach (var role in roles)
            {
                mappedRoles.Add(role switch
                {
                    UserRoles.PatentExaminer => Roles.PatentExaminer,
                    UserRoles.PatentSearch => Roles.PatentSearch,
                    UserRoles.TrademarkExaminer => Roles.TrademarkExaminer,
                    UserRoles.TrademarkSearch => Roles.TrademarkSearch,
                    UserRoles.DesignSearch => Roles.DesignSearch,
                    UserRoles.DesignExaminer => Roles.DesignExaminer,
                    UserRoles.TrademarkOpposition => Roles.TrademarkOpposition,
                    UserRoles.TrademarkCertification => Roles.TrademarkCertification,
                    UserRoles.Finance => Roles.Finance,
                    UserRoles.Tickets => Roles.Tech,
                    UserRoles.Users => Roles.User,
                    UserRoles.Agent => Roles.User,
                    UserRoles.Productivity => Roles.Staff,
                    UserRoles.Support => Roles.Tech,
                    UserRoles.PublicationMenu => Roles.TrademarkAcceptance,
                    UserRoles.OppositionMenu => Roles.TrademarkOpposition,
                    UserRoles.StaffMenu => Roles.Staff,
                    UserRoles.BackOffice => Roles.Staff,
                    UserRoles.AppealExaminer => Roles.TrademarkAcceptance,
                    UserRoles.SuperAdmin => Roles.SuperAdmin,
                    UserRoles.TrademarkAcceptance => Roles.TrademarkAcceptance,
                    _ => Roles.User,
                });
            }
            return mappedRoles;
        }
        private AccountType MapAccountType(List<UserRoles> roles, UserTypes? type)
        {
            if (type.HasValue)
            {
                return type switch
                {
                    UserTypes.User => AccountType.Individual,
                    UserTypes.Search_Patent => AccountType.Officer,
                    UserTypes.Search_Design => AccountType.Officer,
                    UserTypes.Advanced => AccountType.Tech,
                    UserTypes.All => AccountType.Tech,
                    UserTypes.Admin => AccountType.Tech,
                    UserTypes.design_examiner => AccountType.Officer,
                    UserTypes.patent_examiner => AccountType.Officer,
                    UserTypes.AppealExaminer => AccountType.Officer,
                    _ => AccountType.Individual,
                };
            }
            if (roles == null || roles.Count == 0)
            {
                if (type.HasValue)
                {
                    return type switch
                    {
                        UserTypes.User => AccountType.Individual,
                        UserTypes.Search_Patent => AccountType.Officer,
                        UserTypes.Search_Design => AccountType.Officer,
                        UserTypes.Advanced => AccountType.Tech,
                        UserTypes.All => AccountType.Tech,
                        UserTypes.Admin => AccountType.Tech,
                        UserTypes.design_examiner => AccountType.Officer,
                        UserTypes.patent_examiner => AccountType.Officer,
                        UserTypes.AppealExaminer => AccountType.Officer,
                        _ => AccountType.Individual,
                    };
                }

                return AccountType.Individual;
            }

            if (roles.Exists(r => r == UserRoles.Users || r == UserRoles.Agent))
            {
                return AccountType.Individual;
            }
            else if (roles.Exists(r => r == UserRoles.Tickets || r == UserRoles.Support || r == UserRoles.SuperAdmin))
            {
                return AccountType.Tech;
            }
            else
            {
                return AccountType.Officer;
            }
        }
        public async Task<bool> TransferUser(MigrateUserDto dto)
        {
            try
            {
                _log.LogInformation("Transferring user {UserId} with email {Email}", dto._id, dto.email);
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.password);
                var corr = dto.DefaultCorrespondence;
                var state = MapToNigerianState(corr?.state);
                var roles = MapToRole(dto.UserRoles);
                var accType = MapAccountType(dto.UserRoles, dto.UserType);
                var newUser = new AppUser
                {
                    Id = dto.uuid,
                    CreatorId = dto?._id,
                    FirstName = dto.firstName,
                    LastName = dto.lastName,
                    Email = dto.email,
                    PhoneNumber = corr?.phone ?? "",
                    Address = corr?.address ?? "",
                    AccountType = accType,
                    UserRoles = roles,
                    CreatedAt = DateTime.Now,
                    isVerified = dto.verified ?? false,
                    PasswordHash = hashedPassword,
                    Nationality = "",
                    State = state,
                    Signature = dto.Signature,
                    VerificationDocs = new List<string>(),
                    Files = new List<string>(),
                };
                await _users.InsertOneAsync(newUser);
                _log.LogInformation("User {Email} transferred successfully with Id {UserId}", dto.email, dto.uuid);
                return true;
            }
            catch (MongoWriteException ex)
            {
                _log.LogError(ex, "MongoDB write error transferring user {UserId}", dto._id);
                return false;
            }
        }
        public async Task<bool> ChangePassword(ChangePasswordDto dto)
        {
            try
            {
                _log.LogInformation("Password change requested for {Email}", dto.Email);
                var user = await _users.Find(u => u.Email == dto.Email).FirstOrDefaultAsync();
                if (user == null)
                {
                    _log.LogWarning("Password change failed — user {Email} not found", dto.Email);
                    return false;
                }
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
                var update = Builders<AppUser>.Update.Set(u => u.PasswordHash, hashedPassword);
                var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);
                _log.LogInformation("Password changed for {Email}, ModifiedCount: {Count}", dto.Email, result.ModifiedCount);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error changing password for {Email}", dto.Email);
                throw;
            }
        }
        public async Task<bool> RequestPasswordReset(string email)
        {
            _log.LogInformation("Password reset requested for {Email}", email);
            var user = await _users.Find(u => u.Email == email).FirstOrDefaultAsync();
            if (user == null)
            {
                _log.LogWarning("Password reset failed — user {Email} not found", email);
                return false;
            }

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

            var update = Builders<AppUser>.Update
                .Set(u => u.PasswordResetToken, token)
                .Set(u => u.PasswordResetTokenExpiry, DateTime.UtcNow.AddHours(1));

            await _users.UpdateOneAsync(u => u.Id == user.Id, update);

            var resetLink = $"https://portal.iponigeria.com/auth/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
            _log.LogDebug("Reset link generated for {Email}", email);

            var mail = new EmailDto
            {
                EmailType = EmailType.ResetPassword,
                ResetPasswordMail = new ResetPasswordMail
                    {
                        ResetLink = resetLink,
                        UserName = user.Name ?? user.FirstName,
                    },
                To = email,
                Subject = "Password Reset",
            };
            await _emailServices.SendMail(mail);
            _log.LogInformation("Password reset email sent to {Email}", email);

            return true;
        }
        public async Task<bool> ResetPassword(ResetPasswordDto dto)
        {
            _log.LogInformation("Processing password reset for {Email}", dto.Email);
            var user = await _users.Find(u =>
                u.Email == dto.Email &&
                u.PasswordResetToken == dto.Token &&
                u.PasswordResetTokenExpiry > DateTime.UtcNow
            ).FirstOrDefaultAsync();

            if (user == null)
            {
                _log.LogWarning("Password reset failed — invalid or expired token for {Email}", dto.Email);
                return false;
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

            var update = Builders<AppUser>.Update
                .Set(u => u.PasswordHash, hashedPassword)
                .Unset(u => u.PasswordResetToken)
                .Unset(u => u.PasswordResetTokenExpiry);

            var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);
            _log.LogInformation("Password reset completed for {Email}, ModifiedCount: {Count}", dto.Email, result.ModifiedCount);

            return result.ModifiedCount > 0;
        }
        public async Task<bool> UpdateUserProfile(ProfileDto dto)
        {
            try
            {
                _log.LogInformation("Updating profile for user {UserId}", dto.UserId);
                var user = await _users.Find(u => u.Id == dto.UserId).FirstOrDefaultAsync();
                if (user == null) throw new KeyNotFoundException("User not found");
                var fullName = dto?.FirstName + " " + dto?.LastName;
                var updateDefinitions = new List<UpdateDefinition<AppUser>>();


                if (!string.IsNullOrEmpty(dto.FirstName))
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.FirstName, dto.FirstName));

                if (!string.IsNullOrEmpty(dto.LastName))
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.LastName, dto.LastName));

                if (!string.IsNullOrEmpty(dto.PhoneNumber))
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.PhoneNumber, dto.PhoneNumber));

                if (dto.AccountType is not null)
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.AccountType, dto.AccountType));

                if (!string.IsNullOrEmpty(dto.Address))
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.Address, dto.Address));

                if (!string.IsNullOrEmpty(dto.Email))
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.Email, dto.Email));

                if (!string.IsNullOrEmpty(dto.Nationality))
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.Nationality, dto.Nationality));

                if (dto.State is not null)
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.State, dto.State));

                if (dto.AccountType is not AccountType.Corporate)
                {
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.Name, fullName));
                }
                else
                {
                    updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.Name, dto.Name));
                }
                if (updateDefinitions.Count == 0)
                {
                    _log.LogDebug("No profile fields to update for user {UserId}", dto.UserId);
                    return false;
                }

                updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.LastUpdatedAt, DateTime.Now));

                var combinedUpdate = Builders<AppUser>.Update.Combine(updateDefinitions);
                var filter = Builders<AppUser>.Filter.Eq(u => u.Id, dto.UserId);
                var result = await _users.UpdateOneAsync(filter, combinedUpdate);
                _log.LogInformation("Profile updated for user {UserId}, ModifiedCount: {Count}", dto.UserId, result.ModifiedCount);

                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error updating profile for user {UserId}", dto.UserId);
                throw;
            }
        }
        public async Task<ProfileDto> GetUser(string userId)
        {
            try
            {
                _log.LogDebug("Fetching user profile for {UserId}", userId);
                var user = await _users.Find(u => u.Id == userId).FirstOrDefaultAsync() ??
                    throw new KeyNotFoundException("User not found");

                var userDeets = new ProfileDto
                {
                    AccountType = user.AccountType,
                    Address = user.Address,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Nationality = user.Nationality,
                    PhoneNumber = user.PhoneNumber,
                    State = user.State,
                    Name = user.FirstName + " " + user.LastName,
                    UserRoles = user.UserRoles
                };
                return userDeets;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error fetching user profile for {UserId}", userId);
                throw;
            }
        }
    }
}
