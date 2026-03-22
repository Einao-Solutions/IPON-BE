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
        public AuthServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings, IConfiguration config, EmailServices emailServices)
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
            _fillingCollection = pdDb.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
            _emailServices = emailServices;
        }

        public async Task<bool> CreateUser(RegisterDto req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return false;

            try
            {
                Console.WriteLine("Creating user...");
                var emailNormalized = req.Email.Trim().ToLowerInvariant();
                var existing = await _users.Find(u => u.Email == emailNormalized).FirstOrDefaultAsync();
                if (existing != null) return false;

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(req.Password);

                var user = new AppUser
                {
                    Id = Guid.NewGuid().ToString(),
                    CreatorId = Guid.NewGuid().ToString(),
                    Email = emailNormalized,
                    FirstName = req.FirstName?.Trim() ?? string.Empty,
                    LastName = req.LastName?.Trim() ?? string.Empty,
                    PhoneNumber = req.Phone?.Trim() ?? string.Empty,
                    AccountType = AccountType.Individual,
                    PasswordHash = hashedPassword,
                    UserRoles = new List<Roles> { Roles.User },
                    CreatedAt = DateTime.UtcNow,
                    Name = req.FirstName + " " + req.LastName,
                };

                await _users.InsertOneAsync(user);
                return true;
            }
            catch (MongoWriteException)
            {
                // Insert failed (duplicate key or write error)
                return false;
            }
            catch (Exception e)
            {
                // Unexpected error - log if you have logging, then return false
                Console.WriteLine(e);
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
                Console.WriteLine("Token: " + token);
                return authUser;
            }
            catch (Exception)
            {
                throw;
            }
        }
        private NigerianStates MapToNigerianState(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return NigerianStates.None;

            // Trim and lower for comparisons
            var s = raw.Trim();

            // Remove the word "state" if present (e.g. "Lagos State" -> "Lagos")
            s = Regex.Replace(s, @"\bstate\b", "", RegexOptions.IgnoreCase).Trim();

            // Common aliases
            var alias = s.ToLowerInvariant();
            switch (alias)
            {
                case "abuja":
                case "fct":
                case "federal capital territory":
                case "federal capital":
                    return NigerianStates.FederalCapitalTerritory;
            }

            // Normalize: remove non-letters, collapse spaces, then remove spaces to match enum naming
            var cleaned = Regex.Replace(s, @"[^A-Za-z\s]", " ").Trim();
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            var key = cleaned.Replace(" ", ""); // e.g. "Cross River" -> "CrossRiver", "Akwa Ibom" -> "AkwaIbom"

            if (Enum.TryParse<NigerianStates>(key, true, out var parsed))
                return parsed;

            // As fallback, try parsing the cleaned string directly (some enums match single-word names)
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
            // If no roles provided, fall back to user type mapping or default to Individual
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

            // If roles explicitly indicate individual-level users
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
                Console.WriteLine("Creator id:" + dto._id);
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.password);
                // var filings = await _fillingCollection.Find(f => f.CreatorAccount == dto._id).ToListAsync();
                // var files = new List<string>();
                // if (filings != null && filings.Count > 0)
                // {
                //     files = filings
                //         .Select(f => f.FileId)
                //         .Where(id => !string.IsNullOrWhiteSpace(id))
                //         .ToList();
                // }
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
                return true;
            }
            catch (MongoWriteException)
            {
                return false;
            }
        }
        public async Task<bool> ChangePassword(ChangePasswordDto dto)
        {
            try
            {
                var user = await _users.Find(u => u.Email == dto.Email).FirstOrDefaultAsync();
                if (user == null)
                    return false;
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
                var update = Builders<AppUser>.Update.Set(u => u.PasswordHash, hashedPassword);
                var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception)
            {
                throw;
            }
        }
        public async Task<bool> RequestPasswordReset(string email)
        {
            var user = await _users.Find(u => u.Email == email).FirstOrDefaultAsync();
            if (user == null)
                return false;

            // Generate secure token
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

            var update = Builders<AppUser>.Update
                .Set(u => u.PasswordResetToken, token)
                .Set(u => u.PasswordResetTokenExpiry, DateTime.UtcNow.AddHours(1));

            await _users.UpdateOneAsync(u => u.Id == user.Id, update);

            // Build reset link
            var resetLink = $"https://portal.iponigeria.com/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
            Console.WriteLine($"Reset Link: {resetLink}");

            var mail = new EmailDto
            {
                EmailType = EmailType.ResetPassword,
                ResetPasswordMail =
                {
                    ResetLink = resetLink,
                    UserName = user.Name,
                },
                To = email,
                Subject = "Password Reset",
            };
            var mailSent = await _emailServices.SendMail(mail);
            if (!mailSent) throw new ApplicationException("Failed to Send Mail");


            return true;
        }
        public async Task<bool> ResetPassword(ResetPasswordDto dto)
        {
            var user = await _users.Find(u =>
                u.Email == dto.Email &&
                u.PasswordResetToken == dto.Token &&
                u.PasswordResetTokenExpiry > DateTime.UtcNow
            ).FirstOrDefaultAsync();

            if (user == null)
                return false;

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

            var update = Builders<AppUser>.Update
                .Set(u => u.PasswordHash, hashedPassword)
                .Unset(u => u.PasswordResetToken)
                .Unset(u => u.PasswordResetTokenExpiry);

            var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);

            return result.ModifiedCount > 0;
        }
        public async Task<bool> UpdateUserProfile(ProfileDto dto)
        {
            try
            {
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
                    return false;

                updateDefinitions.Add(Builders<AppUser>.Update.Set(u => u.LastUpdatedAt, DateTime.Now));

                var combinedUpdate = Builders<AppUser>.Update.Combine(updateDefinitions);
                var filter = Builders<AppUser>.Filter.Eq(u => u.Id, dto.UserId);
                var result = await _users.UpdateOneAsync(filter, combinedUpdate);

                return result.ModifiedCount > 0;
            }
            catch (Exception)
            {
                throw;
            }
        }
        public async Task<ProfileDto> GetUser(string userId)
        {
            try
            {
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
                    Name = user.FirstName +" "+ user.LastName,
                    UserRoles = user.UserRoles
                };
                return userDeets;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
