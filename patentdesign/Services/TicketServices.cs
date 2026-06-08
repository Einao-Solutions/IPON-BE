using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;
using System.Security.Authentication;

namespace patentdesign.Services;

public class TicketServices
{
    private PaymentUtils _remitaPaymentUtils;
    private MongoClient _mongoClient;
    private static IMongoCollection<TicketInfo> _ticketsCollection;
    public TicketServices(IMongoDatabase db, IOptions<PatentDesignDBSettings> patentDesignDbSettings)
    {
        _ticketsCollection = db.GetCollection<TicketInfo>(patentDesignDbSettings.Value.TicketCollectionName);
    }
    //public async Task CreateTicketAsync(TicketInfo ticket)
    //{
    //    await _ticketsCollection.InsertOneAsync(ticket);
    //}

    public async Task CreateTicketAsync(TicketInfo ticket)
    {
        var count = await _ticketsCollection.CountDocumentsAsync(FilterDefinition<TicketInfo>.Empty);
        ticket.TicketNumber = $"TKT-{(count + 1):D5}";
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

    //public async Task<TicketInfo?> AddMessageAsync(NewCorrespondenceType correspondence)
    //{
    //    var filter = Builders<TicketInfo>.Filter.Eq(f => f.id, correspondence.ticketId);
    //    List<UpdateDefinition<TicketInfo>> updates = [
    //        Builders<TicketInfo>.Update.Push(f=>f.Correspondences, correspondence.correspondence),
    //        Builders<TicketInfo>.Update.Set(f=>f.Status, correspondence.newStatus),
    //    ];
    //    var options = new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After };
    //    var result=await _ticketsCollection.FindOneAndUpdateAsync<TicketInfo>(filter, Builders<TicketInfo>.Update.Combine(updates), options);
    //    return result;
    //}
    public async Task<TicketInfo> AddMessageAsync(NewCorrespondenceType correspondence)
    {
        correspondence.correspondence.DateAdded = DateTime.UtcNow;

        var filter = Builders<TicketInfo>.Filter.Eq(f => f.id, correspondence.ticketId);
        List<UpdateDefinition<TicketInfo>> updates = [
            Builders<TicketInfo>.Update.Push(f => f.Correspondences, correspondence.correspondence),
        Builders<TicketInfo>.Update.Set(f => f.Status, correspondence.newStatus),
    ];
        var options = new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After };
        return await _ticketsCollection.FindOneAndUpdateAsync(filter, Builders<TicketInfo>.Update.Combine(updates), options);
    }

    public async Task<TicketInfo> GetTicketAsync(string id)
    {
        return await _ticketsCollection.Find(x => x.id == id).FirstOrDefaultAsync();
    }

    //public async Task<List<TicketSummary>> GetTicketsSummariesAsync(TicketsSummariesType info)
    //{
    //    var filter = Builders<TicketInfo>.Filter;
    //    var creatorFilter = info.creatorId == "null" ? filter.Empty : filter.Eq(x => x.creatorId, info.creatorId);
    //    var statusFilter = info.status == null ? filter.Empty : filter.Eq(x => x.Status, info.status);
    //    var titleFilter = info.title == null
    //        ? filter.Empty
    //        : filter.Regex(f => f.Title, new BsonRegularExpression(info.title, "i"));
    //    var projection = Builders<TicketInfo>.Projection.Expression(x => new TicketSummary()
    //    {
    //        Status = x.Status,
    //        Title = x.Title,
    //        Creator =
    //            new TicketCreator()
    //            {
    //                Name = x.creatorName,
    //                Id = x.creatorId,
    //            },
    //        LastInteraction = x.Correspondences.Last().DateAdded,
    //        TicketId = x.id,
    //        Resolution = x.resolution
    //    });
    //    var tickets=await _ticketsCollection.Find(Builders<TicketInfo>.Filter.And([creatorFilter, statusFilter, titleFilter])).Project(projection)
    //        .Skip(info.startIndex??0).Limit(info.amount).ToListAsync();
    //    return tickets;
    //}

    public async Task<List<TicketSummary>> GetTicketsSummariesAsync(TicketsSummariesType info)
    {
        var filter = Builders<TicketInfo>.Filter;

        var creatorFilter = info.creatorId == "null" ? filter.Empty : filter.Eq(x => x.creatorId, info.creatorId);
        var statusFilter = info.status == null ? filter.Empty : filter.Eq(x => x.Status, info.status);
        var titleFilter = info.title == null
            ? filter.Empty
            : filter.Regex(f => f.Title, new BsonRegularExpression(info.title, "i"));
        var categoryFilter = info.category == null ? filter.Empty : filter.Eq(x => x.Category, info.category);
        var escalatedFilter = info.isEscalated == null ? filter.Empty : filter.Eq(x => x.IsEscalated, info.isEscalated.Value);
        var registryStaffFilter = info.raisedByRegistryStaff == null ? filter.Empty : filter.Eq(x => x.RaisedByRegistryStaff, info.raisedByRegistryStaff.Value);
        var ticketNumberFilter = info.ticketNumber == null
            ? filter.Empty
            : filter.Regex(x => x.TicketNumber, new BsonRegularExpression(info.ticketNumber, "i"));
        var fileNumberFilter = info.fileNumber == null
            ? filter.Empty
            : filter.Regex(x => x.FileNumber, new BsonRegularExpression(info.fileNumber, "i"));
        var appTypeFilter = info.applicationType == null ? filter.Empty : filter.Eq(x => x.ApplicationType, info.applicationType);
        var startDateFilter = info.startDate == null ? filter.Empty : filter.Gte(x => x.Created, info.startDate.Value);
        var endDateFilter = info.endDate == null ? filter.Empty : filter.Lte(x => x.Created, info.endDate.Value);

        var combined = filter.And(
            creatorFilter, statusFilter, titleFilter,
            categoryFilter, escalatedFilter, registryStaffFilter,
            ticketNumberFilter, fileNumberFilter, appTypeFilter,
            startDateFilter, endDateFilter
        );

        var projection = Builders<TicketInfo>.Projection.Expression(x => new TicketSummary
        {
            Status = x.Status,
            Title = x.Title,
            Creator = new TicketCreator { Name = x.creatorName, Id = x.creatorId },
            LastInteraction = x.Correspondences.Last().DateAdded,
            DateCreated = x.Created,
            TicketId = x.id,
            TicketNumber = x.TicketNumber,
            Category = x.Category,
            TicketType = x.TicketType,       
            IsEscalated = x.IsEscalated,
            EscalatedFromCategory = x.EscalatedFromCategory,
            Resolution = x.resolution
        });

        var tickets = await _ticketsCollection
            .Find(combined)
            .Project(projection)
            .Skip(info.startIndex ?? 0)
            .Limit(info.amount)
            .ToListAsync();

        return tickets;
    }

    //public async Task<TicketStatsReturnType> TicketStats(string? creatorId)
    //{
    //  var iscreator = 
    //      creatorId == null
    //          ? Builders<TicketInfo>.Filter.Empty
    //          : Builders<TicketInfo>.Filter.Eq(x=>x.creatorId, creatorId);
    //  var creatorDocs=_ticketsCollection.CountDocuments(iscreator);
    //  var awaitinStaff =
    //      _ticketsCollection.CountDocuments(Builders<TicketInfo>.Filter.And(
    //          [
    //              Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.AwaitingStaff),
    //              iscreator
    //          ]
    //          ));
    //  var awaitingUser =
    //      _ticketsCollection.CountDocuments(
    //          Builders<TicketInfo>.Filter.And(
    //              [
    //                  Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.AwaitingUser),
    //                  iscreator
    //              ]
    //          ));
    //  var closed =
    //      _ticketsCollection.CountDocuments(
    //          Builders<TicketInfo>.Filter.And(
    //              [
    //                  Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.Closed),
    //                  iscreator
    //              ]
    //          )
    //  );
    //  var result=new TicketStatsReturnType()
    //  {
    //      total= creatorDocs, staff= awaitinStaff, user= awaitingUser, closed=closed

    //  };
    //  return result;
    //}

    public async Task<TicketStatsReturnType> TicketStats(string? creatorId, int? category = null)
    {
        var baseFilter = creatorId == null
            ? Builders<TicketInfo>.Filter.Empty
            : Builders<TicketInfo>.Filter.Eq(x => x.creatorId, creatorId);

        if (category.HasValue)
            baseFilter = Builders<TicketInfo>.Filter.And(
                baseFilter,
                Builders<TicketInfo>.Filter.Eq(x => x.Category, (TicketCategory)category.Value)
            );

        var total = _ticketsCollection.CountDocuments(baseFilter);
        var awaitingStaff = _ticketsCollection.CountDocuments(Builders<TicketInfo>.Filter.And(
            baseFilter, Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.AwaitingStaff)));
        var awaitingUser = _ticketsCollection.CountDocuments(Builders<TicketInfo>.Filter.And(
            baseFilter, Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.AwaitingUser)));
        var closed = _ticketsCollection.CountDocuments(Builders<TicketInfo>.Filter.And(
            baseFilter, Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.Closed)));

        return new TicketStatsReturnType
        {
            total = total,
            staff = awaitingStaff,
            user = awaitingUser,
            closed = closed
        };
    }

    public async Task<TicketInfo> EscalateTicketAsync(EscalateTicketRequest request)
    {
        var ticket = await _ticketsCollection.Find(x => x.id == request.TicketId).FirstOrDefaultAsync();
        if (ticket == null) return null;

        var systemMsg = new TicketCorrespondence
        {
            SenderId = "system",
            SenderName = "System",
            Message = request.AutoMessage,
            DateAdded = DateTime.UtcNow
        };

        var filter = Builders<TicketInfo>.Filter.Eq(f => f.id, request.TicketId);
        List<UpdateDefinition<TicketInfo>> updates = [
            Builders<TicketInfo>.Update.Set(f => f.Category, request.EscalateToCategory),
        Builders<TicketInfo>.Update.Set(f => f.IsEscalated, true),
        Builders<TicketInfo>.Update.Set(f => f.EscalatedFromCategory, ticket.Category),
        Builders<TicketInfo>.Update.Set(f => f.EscalatedAt, DateTime.UtcNow),
        Builders<TicketInfo>.Update.Set(f => f.EscalatedById, request.EscalatedById),
        Builders<TicketInfo>.Update.Set(f => f.EscalatedByName, request.EscalatedByName),
        Builders<TicketInfo>.Update.Push(f => f.Correspondences, systemMsg),
    ];
        var options = new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After };
        return await _ticketsCollection.FindOneAndUpdateAsync(
            filter, Builders<TicketInfo>.Update.Combine(updates), options);
    }
}