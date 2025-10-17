using System.Security.Authentication;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Models;
using patentdesign.Utils;

namespace patentdesign.Services;

public class TicketServices
{
    private PaymentUtils _remitaPaymentUtils;
    private MongoClient _mongoClient;
    private static IMongoCollection<TicketInfo> _ticketsCollection;
    public TicketServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings)
    {
        
        var useSandbox = patentDesignDbSettings.Value.UseSandbox;

        //string digitalOcean = useSandbox != "Y" ? @"mongodb+srv://doadmin:72mY9T1sI360HU8d@db-mongodb-lon1-93952-8f46b05e.mongo.ondigitalocean.com/admin?tls=true&authSource=admin" : patentDesignDbSettings.Value.ConnectionString;
        string digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;


        MongoClientSettings settings = MongoClientSettings.FromUrl(
        new MongoUrl(digitalOcean)
        );
        settings.SslSettings =
        new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
              _mongoClient = new MongoClient(settings);
        // _mongoClient = new MongoClient(patentDesignDbSettings.Value.ConnectionString);
        var pdDb=_mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _ticketsCollection = pdDb.GetCollection<TicketInfo>(patentDesignDbSettings.Value.TicketCollectionName);
    }
    public async Task CreateTicketAsync(TicketInfo ticket)
    {
        await _ticketsCollection.InsertOneAsync(ticket);
    }

    public async Task<bool> CloseTicketsAsync(ResolveTicketType res)
    {
        var filter = Builders<TicketInfo>.Filter.In(f => f.id, res.ticketId);
        List<UpdateDefinition<TicketInfo>> updates =
        [
            Builders<TicketInfo>.Update.Set(f => f.resolution, res.resolution),
            Builders<TicketInfo>.Update.Set(f => f.Status, TicketState.Closed)
        ];
        var result=await _ticketsCollection.UpdateManyAsync<TicketInfo>((x=>res.ticketId.Contains(x.id)),
            Builders<TicketInfo>.Update.Combine(updates));
        return result.IsAcknowledged;
    }
    
    public async Task DeleteTicketAsync(){}

    public async Task<TicketInfo?> AddMessageAsync(NewCorrespondenceType correspondence)
    {
        var filter = Builders<TicketInfo>.Filter.Eq(f => f.id, correspondence.ticketId);
        List<UpdateDefinition<TicketInfo>> updates = [
            Builders<TicketInfo>.Update.Push(f=>f.Correspondences, correspondence.correspondence),
            Builders<TicketInfo>.Update.Set(f=>f.Status, correspondence.newStatus),
        ];
        var options = new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After };
        var result=await _ticketsCollection.FindOneAndUpdateAsync<TicketInfo>(filter, Builders<TicketInfo>.Update.Combine(updates), options);
        return result;
    }
    
    public async Task<TicketInfo> GetTicketAsync(string id)
    {
        return await _ticketsCollection.Find(x => x.id == id).FirstOrDefaultAsync();
    }

    public async Task<List<TicketSummary>> GetTicketsSummariesAsync(TicketsSummariesType info)
    {
        var filter = Builders<TicketInfo>.Filter;
        var creatorFilter = info.creatorId == "null" ? filter.Empty : filter.Eq(x => x.creatorId, info.creatorId);
        var statusFilter = info.status == null ? filter.Empty : filter.Eq(x => x.Status, info.status);
        var titleFilter = info.title == null
            ? filter.Empty
            : filter.Regex(f => f.Title, new BsonRegularExpression(info.title, "i"));
        var projection = Builders<TicketInfo>.Projection.Expression(x => new TicketSummary()
        {
            Status = x.Status,
            Title = x.Title,
            Creator =
                new TicketCreator()
                {
                    Name = x.creatorName,
                    Id = x.creatorId,
                },
            LastInteraction = x.Correspondences.Last().DateAdded,
            TicketId = x.id,
            Resolution = x.resolution
        });
        var tickets=await _ticketsCollection.Find(Builders<TicketInfo>.Filter.And([creatorFilter, statusFilter, titleFilter])).Project(projection)
            .Skip(info.startIndex??0).Limit(info.amount).ToListAsync();
        return tickets;
    }
    public async Task<TicketStatsReturnType> TicketStats(string? creatorId)
    {
      var iscreator = 
          creatorId == null
              ? Builders<TicketInfo>.Filter.Empty
              : Builders<TicketInfo>.Filter.Eq(x=>x.creatorId, creatorId);
      var creatorDocs=_ticketsCollection.CountDocuments(iscreator);
      var awaitinStaff =
          _ticketsCollection.CountDocuments(Builders<TicketInfo>.Filter.And(
              [
                  Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.AwaitingStaff),
                  iscreator
              ]
              ));
      var awaitingUser =
          _ticketsCollection.CountDocuments(
              Builders<TicketInfo>.Filter.And(
                  [
                      Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.AwaitingUser),
                      iscreator
                  ]
              ));
      var closed =
          _ticketsCollection.CountDocuments(
              Builders<TicketInfo>.Filter.And(
                  [
                      Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.Closed),
                      iscreator
                  ]
              )
      );
      var result=new TicketStatsReturnType()
      {
          total= creatorDocs, staff= awaitinStaff, user= awaitingUser, closed=closed
          
      };
      return result;
    }
}