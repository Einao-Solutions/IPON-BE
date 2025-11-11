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
            _fillingCollection = pdDb.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
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
                    Email = emailNormalized,
                    FirstName = req.FirstName?.Trim() ?? string.Empty,
                    LastName = req.LastName?.Trim() ?? string.Empty,
                    PhoneNumber = req.Phone?.Trim() ?? string.Empty,
                    AccountType = AccountType.Individual,
                    PasswordHash = hashedPassword,
                    UserRoles = new List<Roles> { Roles.User },
                    CreatedAt = DateTime.UtcNow
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

                var token = GenerateJwtToken(user);
                AuthUserDto authUser = new AuthUserDto
                {
                    Token = token,
                    User = user
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
                var filings = await _fillingCollection.Find(f => f.CreatorAccount == dto._id).ToListAsync();
                var files = new List<string>();
                if (filings != null && filings.Count > 0)
                {
                    files = filings
                        .Select(f => f.FileId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToList();
                }
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
                    Files = files,
                };
                await _users.InsertOneAsync(newUser);
                return true;
            }
            catch (MongoWriteException)
            {
                return false;
            }
        }
    }
}
