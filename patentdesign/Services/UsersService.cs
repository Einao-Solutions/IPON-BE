using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Controllers;
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

    public UsersService(IOptions<PatentDesignDBSettings> patentDesignDbSettings)
    {
        
        var useSandbox = patentDesignDbSettings.Value.UseSandbox;

       // string digitalOcean = useSandbox != "Y" ? @"mongodb+srv://doadmin:72mY9T1sI360HU8d@db-mongodb-lon1-93952-8f46b05e.mongo.ondigitalocean.com/admin?tls=true&authSource=admin" : patentDesignDbSettings.Value.ConnectionString;
        string digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;


        MongoClientSettings settings = MongoClientSettings.FromUrl(
            new MongoUrl(digitalOcean)
        );
        settings.SslSettings =
            new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
        _mongoClient = new MongoClient(settings);
        // _mongoClient = new MongoClient(patentDesignDbSettings.Value.ConnectionString);
        var pdDb = _mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _userCollection = pdDb.GetCollection<AppUser>("appUsers");
        _attachmentCollection =
            pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
        _fillingCollection = pdDb.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
        _performanceCollection = pdDb.GetCollection<PerformanceMarker>("performance");
    }
    
    public async Task<List<AppUser>> SearchUsersByNameId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<AppUser>();

        // Escape user input so special regex chars don't break matching
        var escaped = Regex.Escape(text);

        var filter = Builders<AppUser>.Filter.Or(
            Builders<AppUser>.Filter.Regex(f => f.FirstName, new BsonRegularExpression(escaped, "i")),
            Builders<AppUser>.Filter.Regex(f => f.LastName, new BsonRegularExpression(escaped, "i")),
            Builders<AppUser>.Filter.Regex(f => f.CreatorId, new BsonRegularExpression(escaped, "i")),
            Builders<AppUser>.Filter.Regex(f => f.Id, new BsonRegularExpression(escaped, "i"))
        );

        Console.WriteLine("Search filter: " + filter.ToJson());
        var result = await _userCollection.Find(filter).ToListAsync();
        Console.WriteLine("Search result count: " + result.Count);
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
}