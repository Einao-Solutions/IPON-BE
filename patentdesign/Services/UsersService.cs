using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Controllers;
using patentdesign.Models;

namespace patentdesign.Services;

public class UsersService
{
    private MongoClient _mongoClient;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";
    private static IMongoCollection<UserCreateType> _userCollection;
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
        _userCollection = pdDb.GetCollection<UserCreateType>(patentDesignDbSettings.Value.UsersCollectionName);
        _attachmentCollection =
            pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
        _fillingCollection = pdDb.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
        _performanceCollection = pdDb.GetCollection<PerformanceMarker>("performance");
    }

    public async Task<CorrespondenceType?> LoadDefaultCorrespondence(UserCreateType user)
    {
        var corr = _userCollection.Find(x => x.id == user.id)
            .Project(y => y.DefaultCorrespondence).FirstOrDefault();
        if (corr == null)
        {
            corr = _fillingCollection
                .Find(x => x.CreatorAccount == user.id && x.Correspondence != null && x.Correspondence.name != "-")
                .Project(x => x.Correspondence).FirstOrDefault();
            // if user doesnt exist, create and save
            if (corr != null)
            {
                var userFound = _userCollection.Find(x => x.id == user.id).FirstOrDefault();
                if (userFound != null)
                {
                    await _userCollection.FindOneAndUpdateAsync(
                        Builders<UserCreateType>.Filter.Eq(x => x.id, user.id),
                        Builders<UserCreateType>.Update.Set(y => y.DefaultCorrespondence, corr),
                        new FindOneAndUpdateOptions<UserCreateType>()
                        {
                            ReturnDocument = ReturnDocument.After
                        }
                    );
                }

                else
                {
                    _ = SaveNewCorrespondence(corr, user);
                }

            }

            return corr;
        }
        else
        {
            return corr;
        }
    }

    public async Task<CorrespondenceType?> SaveNewCorrespondence(CorrespondenceType? newCorr, UserCreateType? userInfo)
    {
        var updated=await _userCollection.FindOneAndUpdateAsync(
            Builders<UserCreateType>.Filter.Eq(x=>x.id, userInfo.id),
            Builders<UserCreateType>.Update.Set(x => x.DefaultCorrespondence, newCorr), new FindOneAndUpdateOptions<UserCreateType>()
            {
                ReturnDocument = ReturnDocument.After
            } 
            );
        if (updated == null)
        {
            userInfo.DefaultCorrespondence = newCorr;
            await _userCollection.InsertOneAsync(userInfo);
            return newCorr;
        }
        return updated.DefaultCorrespondence;
    }

    public async Task<string> UpdateUserSig(UpdateSigReq sigInfo)
    {
        var url = await UploadSignature(sigInfo);
        if (_userCollection.Find(x => x.id == sigInfo.UserId).FirstOrDefault()!=null)
        {
            var response = await _userCollection.FindOneAndUpdateAsync(
                Builders<UserCreateType>.Filter.Eq(x => x.id, sigInfo.UserId),
                Builders<UserCreateType>.Update.Set(x => x.Signature, url),
                new FindOneAndUpdateOptions<UserCreateType, UserCreateType>()
                {
                    ReturnDocument = ReturnDocument.After
                });
            return url;
        }
        else
        {
            await _userCollection.InsertOneAsync(new UserCreateType()
            {
                id = sigInfo.UserId,
                Signature = url
            });
            return url;
        }
    }



    private async Task<string> UploadSignature(UpdateSigReq item)
    {
        if (item.data == null) return "";
        var extention = item.fileName.Split(".").Last();
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".{extention}";

        await _attachmentCollection.InsertOneAsync(new AttachmentInfo
        {
            Id = trustedFileName,
            ContentType = item.contentType,
            Data = item.data
        });
        var uri =
            $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        return uri;
    }

    public string? GetSignature(string userId)
    {
        return _userCollection.Find(x => x.id == userId).FirstOrDefault().Signature??"-";
    }

    public async Task<List<UserCreateType>> SearchUsersByNameId(string text)
    {
        var roles = Enum.GetValues<UserRoles>().Where(x => x.ToString().ToLower().Contains(text.ToLower()));
        return await _userCollection.Find(
            Builders<UserCreateType>.Filter.Or([
                Builders<UserCreateType>.Filter.Regex(f => f.name, new BsonRegularExpression(text, "i")),
                Builders<UserCreateType>.Filter.Regex(f => f.id, new BsonRegularExpression(text, "i")),
                Builders<UserCreateType>.Filter.AnyIn(f => f.Roles, roles)
            ])
        ).ToListAsync();
    }

    public async Task<dynamic> GetPerformances(FinanceQueryType data)
    {
        var applicationsCount = _performanceCollection.AsQueryable()
            .Where(x => x.Date >= data.startDate && x.Date <= data.endDate && x.Type == PerformanceType.Application)
            .GroupBy(x => new { x.ApplicationType, x.fileType })
            .Select(t => new { applicationType = t.Key.ApplicationType, fileType = t.Key.fileType, amount = t.Count() })
            .OrderByDescending(x => x.amount)
            .ToList();
        
        var treatedCount = _performanceCollection.AsQueryable()
            .Where(x => x.Date >= data.startDate && x.Date <= data.endDate && x.Type == PerformanceType.Staff)
            .GroupBy(x => new { x.fileType, x.beforeStatus, x.afterStatus, x.user })
            .Select(t => new
            {
                fileType = t.Key.fileType, before = t.Key.beforeStatus, t.Key.user, after = t.Key.afterStatus,
                amount = t.Count()
            })
            .OrderByDescending(x => x.amount)
            .ToList();
        return new
        {
            applicationsCount, treatedCount
        };
    }

    public async Task<UserCreateType?> VerifyUser(string userId)
    {
        try
        {
            var result=await _userCollection.FindOneAndUpdateAsync(Builders<UserCreateType>.Filter.Eq(x=>x.id, userId),
                Builders<UserCreateType>.Update.Set(x => x.verified, true), new FindOneAndUpdateOptions<UserCreateType, UserCreateType>()
                {
                    ReturnDocument = ReturnDocument.After
                });
            return result;
        }
        catch
        {
            return null;
        }

    }
    public async Task<UserCreateType?> GetUser(string uuId, UsersController.UserLogin user)
    {
        try
        {
            var result = _userCollection.Find(Builders<UserCreateType>.Filter.Eq(x => x.uuid, uuId))
                .FirstOrDefault();
            Console.WriteLine(uuId);
            if (result != null)
            {
                await _userCollection.FindOneAndUpdateAsync(x => x.uuid == uuId,
                    Builders<UserCreateType>.Update.Set(x => x.password, user.password));
            }
            return result;
        }
        catch
        {
            return null;
        }

    }
public async Task<UserCreateType?> GetUserById(string id)
    {
        try
        {
            var result = _userCollection.Find(Builders<UserCreateType>.Filter.Eq(x => x.id, id))
                .FirstOrDefault();
            return result;
        }
        catch
        {
            return null;
        }

    }

    public async Task<List<UserCreateType>?> FetchAll()
    {
        var allUsers=await _userCollection.Find(x => x.id != "").ToListAsync();
        return allUsers;
    }

    public async Task AddIds(List<UsersController.AddIDS> ids)
    {
        foreach (var user in ids)
        {
            await _userCollection.FindOneAndUpdateAsync(x => x.id == user.id, Builders<UserCreateType>.Update.Set(x=>x.uuid, user.uuid));
        }

        Console.WriteLine("DONE");
        

    }

    public async Task<bool?> CreateUser(UserCreateType user)
    {
        try
        {
            await _userCollection.InsertOneAsync(user);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<dynamic?> LoadUsers(GetUsersRequest user)
    {
        try
        {
            var filter=Builders<UserCreateType>.Filter;
            var filters = Builders<UserCreateType>.Filter.And([
                filter.Or([
                user.name == null ? filter.Empty : filter.Regex(f => f.name, new BsonRegularExpression(user.name, "i")),
                user.name == null ? filter.Empty : filter.Regex(f => f.uuid, new BsonRegularExpression(user.name, "i")),
                user.name == null ? filter.Empty : filter.Regex(f => f.id, new BsonRegularExpression(user.name, "i")),
                user.name == null
                    ? filter.Empty
                    : filter.Regex(f => f.email, new BsonRegularExpression(user.name, "i")),
                user.name == null
                    ? filter.Empty
                    : filter.Regex(f => f.firstName, new BsonRegularExpression(user.name, "i")),
                user.name == null
                    ? filter.Empty
                    : filter.Regex(f => f.lastName, new BsonRegularExpression(user.name, "i")),
                user.name == null
                    ? filter.Empty
                    : filter.Regex(f => f.middleName, new BsonRegularExpression(user.name, "i")),
                ]),
                user.Roles == null ? filter.Empty : filter.AnyIn(f => f.Roles, user.Roles)
            ]);
            var result=await _userCollection.Find(filters).Project(x=>new
            {
                x.id,
                x.email,
                x.name
            }). Skip(user.skip).Limit(user.take).ToListAsync();

            var count = _userCollection.CountDocuments(filters);
            return  new {result, count};
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool?> UpdateUser(UserCreateType user)
    {
        await _userCollection.ReplaceOneAsync(Builders<UserCreateType>.Filter.Eq(x=>x.id, user.id), user);
        return true;
    }
}