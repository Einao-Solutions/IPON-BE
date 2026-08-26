using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Controllers;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Services;

public class UsersService
{
    private MongoClient _mongoClient;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";
    private static IMongoCollection<AppUser> _userCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private static IMongoCollection<PerformanceMarker> _performanceCollection;
    private static IMongoCollection<Filling> _fillingCollection;
    private readonly ILogger<UsersService> _log;

    public UsersService(IMongoDatabase db, IOptions<PatentDesignDBSettings> patentDesignDbSettings, ILogger<UsersService> log)
    {
        var s = patentDesignDbSettings.Value;
        _userCollection = db.GetCollection<AppUser>("appUsers");
        _attachmentCollection = db.GetCollection<AttachmentInfo>(s.AttachmentCollectionName);
        _fillingCollection = db.GetCollection<Filling>(s.FilesCollectionName);
        _performanceCollection = db.GetCollection<PerformanceMarker>("performance");
        _log = log;
    }
    
    public async Task<List<AppUser>> SearchUsersByNameId(string nameId = null, string email = null)
    {
        var hasNameId = !string.IsNullOrWhiteSpace(nameId);
        var hasEmail = !string.IsNullOrWhiteSpace(email);

        if (!hasNameId && !hasEmail)
            return new List<AppUser>();

        var clauses = new List<FilterDefinition<AppUser>>();

        if (hasNameId)
        {
            var escaped = Regex.Escape(nameId);
            clauses.Add(Builders<AppUser>.Filter.Regex(f => f.FirstName,  new BsonRegularExpression(escaped, "i")));
            clauses.Add(Builders<AppUser>.Filter.Regex(f => f.LastName,   new BsonRegularExpression(escaped, "i")));
            clauses.Add(Builders<AppUser>.Filter.Regex(f => f.CreatorId,  new BsonRegularExpression(escaped, "i")));
            clauses.Add(Builders<AppUser>.Filter.Regex(f => f.Id,         new BsonRegularExpression(escaped, "i")));
            clauses.Add(Builders<AppUser>.Filter.Regex(f => f.Email,      new BsonRegularExpression(escaped, "i")));
            clauses.Add(Builders<AppUser>.Filter.Regex(f => f.Name,       new BsonRegularExpression(escaped, "i")));
        }

        if (hasEmail)
        {
            var escapedEmail = Regex.Escape(email);
            clauses.Add(Builders<AppUser>.Filter.Regex(f => f.Email, new BsonRegularExpression(escapedEmail, "i")));
        }

        var filter = Builders<AppUser>.Filter.Or(clauses);
        var result = await _userCollection.Find(filter).ToListAsync();
        return result;
    }
    
    public async Task<dynamic?> LoadUsers(GetUsersRequest user)
    {
        try
        {
            var filter=Builders<AppUser>.Filter;
            var filters = Builders<AppUser>.Filter.And([
                filter.Or([
                user.name == null ? filter.Empty : filter.Regex(f => f.FirstName, new BsonRegularExpression(user.name, "i")),
                user.name == null ? filter.Empty : filter.Regex(f => f.Id, new BsonRegularExpression(user.name, "i")),
                user.name == null ? filter.Empty : filter.Regex(f => f.CreatorId, new BsonRegularExpression(user.name, "i")),
                user.name == null
                    ? filter.Empty
                    : filter.Regex(f => f.Email, new BsonRegularExpression(user.name, "i")),
                user.name == null
                    ? filter.Empty
                    : filter.Regex(f => f.FirstName, new BsonRegularExpression(user.name, "i")),
                user.name == null
                    ? filter.Empty
                    : filter.Regex(f => f.LastName, new BsonRegularExpression(user.name, "i")),
                ]),
                user.Roles == null ? filter.Empty : filter.AnyIn(f => f.UserRoles, user.Roles)
            ]);
            var result=await _userCollection.Find(filters).Project(x=>new
            {
                x.CreatorId,
                x.Email,
                x.FirstName
            }). Skip(user.skip).Limit(user.take).ToListAsync();

            var count = _userCollection.CountDocuments(filters);
            return  new {result, count};
        }
        catch
        {
            return null;
        }
    }

    public async Task<Dictionary<string, string>> GetAllUserEmails()
    {
        var emails = await _userCollection
            .Find(Builders<AppUser>.Filter.Empty)
            .Project(u => new { u.Email, u.Name })
            .ToListAsync();

        return emails.ToDictionary(e => e.Name ?? "", e => e.Email ?? "");
    }

    public async Task<PaginatedUsersDto> GetAllUsers(GetUsersDto dto)
    {
        _log.LogInformation("Getting users with skip={Skip} and take={Take}", dto.Skip, dto.Take);
        try
        {
            var filter = Builders<AppUser>.Filter;
            var filters = dto.Roles is { Count: > 0 }
                ? filter.AnyIn(f => f.UserRoles, dto.Roles)
                : filter.Empty;
            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                var nameRegex = new BsonRegularExpression(Regex.Escape(dto.Name), "i");
                var nameFilter = filter.Or(
                    filter.Regex(u => u.FirstName, nameRegex),
                    filter.Regex(u => u.LastName, nameRegex),
                    filter.Regex(u => u.Email, nameRegex)
                );
                filters &= nameFilter;
            }
            var users = await _userCollection.Find(filters)
                .Skip(dto.Skip)
                .Limit(dto.Take)
                .ToListAsync();

            var count = await _userCollection.CountDocumentsAsync(filters);

            return new PaginatedUsersDto
            {
                Users = users.Select(u => new UserDto
                {
                    Id = u.Id,
                    Name = $"{u.FirstName} {u.LastName}",
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    UserRoles = u.UserRoles
                }).ToList(),
                TotalCount = count
            };
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<bool> UpdateUserRoles(UserRoleDto request)
    {
        _log.LogInformation("Updating roles for user {UserId}", request.UserId);

        var filter = Builders<AppUser>.Filter.Eq(u => u.Id, request.UserId);
        var matched = false;

        if (request.RemoveRoles is { Count: > 0 })
        {
            var removeUpdate = Builders<AppUser>.Update
                .PullAll(u => u.UserRoles, request.RemoveRoles)
                .Set(u => u.LastUpdatedAt, DateTime.Now);

            var removeResult = await _userCollection.UpdateOneAsync(filter, removeUpdate);
            matched |= removeResult.MatchedCount > 0;
        }

        if (request.AddRoles is { Count: > 0 })
        {
            var addUpdate = Builders<AppUser>.Update
                .AddToSetEach(u => u.UserRoles, request.AddRoles)
                .Set(u => u.LastUpdatedAt, DateTime.Now);

            var addResult = await _userCollection.UpdateOneAsync(filter, addUpdate);
            matched |= addResult.MatchedCount > 0;
        }

        if (!matched && request.AddRoles is not { Count: > 0 } && request.RemoveRoles is not { Count: > 0 })
            return false;

        return matched;
    }

    public async Task<AppUser> GetUserById(string id)
    {
        _log.LogInformation($"Getting User information for {id} ");
        try
        {
            var user = await _userCollection.Find(u => u.Id == id).FirstOrDefaultAsync();
            if (user is null)
            {
                _log.LogError("User not found");
                throw new KeyNotFoundException("User not found");
            }

            return user;
        }
        catch (Exception e)
        {
            _log.LogError(e,"User not found");
            throw;
        }
    }

    public async Task<List<ApplicationInfo>> FetchOtherApplications(string userId)
    {
        _log.LogInformation($"Fetching other applications for user {userId}");
        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user is null)
        {
            _log.LogError("User not found");
            throw new KeyNotFoundException("User not found");
        }
        var otherApplications = user.OtherApplications ?? new List<ApplicationInfo>();
        return otherApplications;
    }

}