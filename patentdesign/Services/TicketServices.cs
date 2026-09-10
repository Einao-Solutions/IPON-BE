using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly IMongoCollection<Filling>? _fillingCollection;
    private readonly ILogger<TicketServices> _logger;

    private sealed record TicketStatusCount
    {
        public TicketState Status { get; init; }
        public long Count { get; init; }
    }

    #region Constructor
    public TicketServices(
        IMongoDatabase db,
        IOptions<PatentDesignDBSettings> patentDesignDbSettings,
        ILogger<TicketServices> logger)
    {
        _logger = logger;
        var s = patentDesignDbSettings.Value;
        _ticketsCollection = db.GetCollection<TicketInfo>(patentDesignDbSettings.Value.TicketCollectionName);
        if (!string.IsNullOrWhiteSpace(s.FilesCollectionName))
        {
            _fillingCollection = db.GetCollection<Filling>(s.FilesCollectionName);
        }

        _logger.LogInformation("TicketServices initialized. TicketCollection: {TicketCollection}, FilesCollectionConfigured: {FilesCollectionConfigured}",
            patentDesignDbSettings.Value.TicketCollectionName,
            !string.IsNullOrWhiteSpace(s.FilesCollectionName));
    }
    #endregion

    private static void NormalizeTicketAttachments(TicketInfo? ticket)
    {
        if (ticket?.Correspondences == null || ticket.Correspondences.Count == 0)
        {
            return;
        }

        foreach (var correspondence in ticket.Correspondences)
        {
            correspondence.Attachment = NormalizeAttachmentLink(correspondence.Attachment);
        }
    }

    private static string? NormalizeAttachmentLink(string? attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment))
        {
            return attachment;
        }

        var value = attachment.Trim();
        var fileId = TryExtractFileId(value);

        if (!string.IsNullOrWhiteSpace(fileId))
        {
            return $"/api/files/GetAttachment?fileId={Uri.EscapeDataString(fileId)}";
        }

        if (value.StartsWith("api/files/GetAttachment", StringComparison.OrdinalIgnoreCase))
        {
            return $"/{value}";
        }

        return value;
    }

    private static string? TryExtractFileId(string attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment))
        {
            return null;
        }

        if (attachment.IndexOf("/api/files/getattachment", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var marker = "fileId=";
            var markerIndex = attachment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var valueStart = markerIndex + marker.Length;
            var ampersandIndex = attachment.IndexOf('&', valueStart);
            var rawFileId = ampersandIndex >= 0
                ? attachment[valueStart..ampersandIndex]
                : attachment[valueStart..];

            return string.IsNullOrWhiteSpace(rawFileId)
                ? null
                : Uri.UnescapeDataString(rawFileId);
        }

        var hasPathSeparators = attachment.Contains('/') || attachment.Contains('\\');
        var hasQuery = attachment.Contains('?') || attachment.Contains('&') || attachment.Contains('=');
        if (!hasPathSeparators && !hasQuery)
        {
            return attachment;
        }

        return null;
    }

    #region Ticket lifecycle
    public async Task CreateTicketAsync(TicketInfo ticket)
    {
        _logger.LogInformation("Creating ticket for CreatorId: {CreatorId}, FileNumber: {FileNumber}", ticket.creatorId, ticket.FileNumber);

        NormalizeTicketAttachments(ticket);

        var count = await _ticketsCollection.EstimatedDocumentCountAsync();
        ticket.TicketNumber = $"TKT-{(count + 1):D5}";
        await _ticketsCollection.InsertOneAsync(ticket);

        _logger.LogInformation("Ticket created with Id: {TicketId}, TicketNumber: {TicketNumber}", ticket.id, ticket.TicketNumber);

        if (!string.IsNullOrWhiteSpace(ticket.FileNumber))
        {
            var fileNumber = ticket.FileNumber.Trim();

            _logger.LogInformation("Linking ticket {TicketId} to file {FileNumber}", ticket.id, fileNumber);

            await _fillingCollection.UpdateOneAsync(
                x => x.FileId == fileNumber,
                Builders<Filling>.Update.Push(x => x.Tickets, new FileTicketRef
                {
                    TicketId = ticket.id,
                    TicketNumber = ticket.TicketNumber,
                    Created = ticket.Created
                })
            );

            _logger.LogInformation("Ticket {TicketId} linked to file {FileNumber}", ticket.id, fileNumber);
        }
        else
        {
            _logger.LogInformation("Ticket {TicketId} has no file number. Skipping file linkage.", ticket.id);
        }
    }

    public async Task<bool> CloseTicketsAsync(ResolveTicketType res)
    {
        _logger.LogInformation("Closing tickets. Count: {Count}", res.ticketId?.Count ?? 0);

        if (res.ticketId == null || res.ticketId.Count == 0)
        {
            _logger.LogInformation("Close tickets skipped. No ticket ids provided.");
            return true;
        }

        var filter = Builders<TicketInfo>.Filter.In(f => f.id, res.ticketId);
        List<UpdateDefinition<TicketInfo>> updates =
        [
            Builders<TicketInfo>.Update.Set(f => f.resolution, res.resolution),
            Builders<TicketInfo>.Update.Set(f => f.Status, TicketState.Closed)
        ];
        var result = await _ticketsCollection.UpdateManyAsync(
            filter,
            Builders<TicketInfo>.Update.Combine(updates));

        _logger.LogInformation("Close tickets completed. Acknowledged: {Acknowledged}, Matched: {Matched}, Modified: {Modified}",
            result.IsAcknowledged,
            result.MatchedCount,
            result.ModifiedCount);

        return result.IsAcknowledged;
    }
    
    public async Task DeleteTicketAsync()
    {
        _logger.LogInformation("DeleteTicketAsync called. No implementation available.");
    }
    #endregion

    #region Ticket communication
    public async Task<TicketInfo> AddMessageAsync(NewCorrespondenceType correspondence)
    {
        _logger.LogInformation("Adding message to ticket {TicketId} with new status {NewStatus}", correspondence.ticketId, correspondence.newStatus);

        correspondence.correspondence.DateAdded = DateTime.UtcNow;
        correspondence.correspondence.Attachment = NormalizeAttachmentLink(correspondence.correspondence.Attachment);

        var filter = Builders<TicketInfo>.Filter.Eq(f => f.id, correspondence.ticketId);
        List<UpdateDefinition<TicketInfo>> updates = [
            Builders<TicketInfo>.Update.Push(f => f.Correspondences, correspondence.correspondence),
        Builders<TicketInfo>.Update.Set(f => f.Status, correspondence.newStatus),
    ];
        var options = new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After };
        var result = await _ticketsCollection.FindOneAndUpdateAsync(filter, Builders<TicketInfo>.Update.Combine(updates), options);

        _logger.LogInformation("Add message completed for ticket {TicketId}. Found: {Found}", correspondence.ticketId, result != null);

        return result;
    }
    #endregion

    #region Ticket retrieval and reporting
    public async Task<TicketInfo> GetTicketAsync(string id)
    {
        _logger.LogInformation("Fetching ticket by Id: {TicketId}", id);

        var result = await _ticketsCollection.Find(x => x.id == id).FirstOrDefaultAsync();
        NormalizeTicketAttachments(result);

        _logger.LogInformation("Fetch ticket completed for Id: {TicketId}. Found: {Found}", id, result != null);

        return result;
    }

    public async Task<List<TicketSummary>> GetTicketsSummariesAsync(TicketsSummariesType info)
    {
        _logger.LogInformation("Fetching ticket summaries. CreatorId: {CreatorId}, Status: {Status}, Category: {Category}, RegistryCategory: {RegistryCategory}, StartIndex: {StartIndex}, Amount: {Amount}",
            info.creatorId,
            info.status,
            info.category,
            info.registryCategory,
            info.startIndex,
            info.amount);

        var filter = Builders<TicketInfo>.Filter;

        var creatorFilter = info.creatorId == "null" ? filter.Empty : filter.Eq(x => x.creatorId, info.creatorId);
        var statusFilter = info.status == null ? filter.Empty : filter.Eq(x => x.Status, info.status);
        var titleFilter = info.title == null
            ? filter.Empty
            : filter.Regex(f => f.Title, new BsonRegularExpression(info.title, "i"));
        var categoryFilter = info.category == null ? filter.Empty : filter.Eq(x => x.Category, info.category);
        var registryCategoryFilter = info.registryCategory == null
            ? filter.Empty
            : filter.Eq(x => x.RegistryCategory, info.registryCategory);
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
            categoryFilter, registryCategoryFilter,
            escalatedFilter, registryStaffFilter,
            ticketNumberFilter, fileNumberFilter, appTypeFilter,
            startDateFilter, endDateFilter
        );

        var projection = Builders<TicketInfo>.Projection.Expression(x => new TicketSummary
        {
            Status = x.Status,
            Title = x.Title,
            Creator = new TicketCreator { Name = x.creatorName, Id = x.creatorId },
            LastInteraction = x.Correspondences.Any()
                ? x.Correspondences.Last().DateAdded
                : x.Created,
            DateCreated = x.Created,
            TicketId = x.id,
            TicketNumber = x.TicketNumber,
            Category = x.Category,
            RegistryCategory = x.RegistryCategory,
            TicketType = x.TicketType,
            ApplicationType = x.ApplicationType,
            RecordalType = x.RecordalType,
            FileNumber = x.FileNumber,
            RaisedByRegistryStaff = x.RaisedByRegistryStaff,
            IsEscalated = x.IsEscalated,
            EscalatedFromCategory = x.EscalatedFromCategory,
            Resolution = x.resolution
        });

        var tickets = await _ticketsCollection
            .Find(combined)
            .Project(projection)
            .Skip(info.startIndex ?? 0)
            .Limit(info.amount ?? 50)
            .ToListAsync();

        _logger.LogInformation("Ticket summaries fetched. Count: {Count}", tickets.Count);

        return tickets;
    }

    public async Task<TicketStatsReturnType> TicketStats(
        string? creatorId,
        int? category = null,
        int? registryCategory = null,
        bool? raisedByRegistryStaff = null)
    {
        _logger.LogInformation("Calculating ticket stats. CreatorId: {CreatorId}, Category: {Category}, RegistryCategory: {RegistryCategory}, RaisedByRegistryStaff: {RaisedByRegistryStaff}",
            creatorId,
            category,
            registryCategory,
            raisedByRegistryStaff);

        var filter = Builders<TicketInfo>.Filter;
        var baseFilter = filter.Empty;

        if (!string.IsNullOrWhiteSpace(creatorId))
        {
            baseFilter = filter.And(
                baseFilter,
                filter.Eq(x => x.creatorId, creatorId)
            );
        }

        if (category.HasValue)
        {
            baseFilter = filter.And(
                baseFilter,
                filter.Eq(x => x.Category, (TicketCategory)category.Value)
            );
        }

        if (registryCategory.HasValue)
        {
            baseFilter = filter.And(
                baseFilter,
                filter.Eq(x => x.RegistryCategory, (TicketCategory)registryCategory.Value)
            );
        }

        if (raisedByRegistryStaff.HasValue)
        {
            baseFilter = filter.And(
                baseFilter,
                filter.Eq(x => x.RaisedByRegistryStaff, raisedByRegistryStaff.Value)
            );
        }

        var statusCounts = await _ticketsCollection
            .Aggregate()
            .Match(baseFilter)
            .Group(
                x => x.Status,
                g => new TicketStatusCount
                {
                    Status = g.Key,
                    Count = g.LongCount()
                })
            .ToListAsync();

        var total = statusCounts.Sum(x => x.Count);
        var awaitingStaff = statusCounts.FirstOrDefault(x => x.Status == TicketState.AwaitingStaff)?.Count ?? 0;
        var awaitingUser = statusCounts.FirstOrDefault(x => x.Status == TicketState.AwaitingUser)?.Count ?? 0;
        var closed = statusCounts.FirstOrDefault(x => x.Status == TicketState.Closed)?.Count ?? 0;

        return new TicketStatsReturnType
        {
            total = total,
            staff = awaitingStaff,
            user = awaitingUser,
            closed = closed
        };
    }
    #endregion

    #region Ticket escalation
    public async Task<TicketInfo> EscalateTicketAsync(EscalateTicketRequest request)
    {
        _logger.LogInformation("Escalating ticket {TicketId} to category {EscalateToCategory} by {EscalatedById}",
            request.TicketId,
            request.EscalateToCategory,
            request.EscalatedById);

        var ticket = await _ticketsCollection.Find(x => x.id == request.TicketId).FirstOrDefaultAsync();
        if (ticket == null)
        {
            _logger.LogWarning("Escalation failed. Ticket not found: {TicketId}", request.TicketId);
            return null;
        }

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
        var result = await _ticketsCollection.FindOneAndUpdateAsync(
            filter, Builders<TicketInfo>.Update.Combine(updates), options);

        _logger.LogInformation("Escalation completed for ticket {TicketId}. Updated: {Updated}", request.TicketId, result != null);

        return result;
    }
    #endregion

    #region Ticket search
    public async Task<List<TicketSummary>> SearchTicketsAsync(TicketSearchRequest request)
    {
        _logger.LogInformation("Searching tickets. TicketNumber: {TicketNumber}, FileNumber: {FileNumber}, IsTech: {IsTech}, RequesterId: {RequesterId}, SupportRegistryCategory: {SupportRegistryCategory}",
            request.ticketNumber,
            request.fileNumber,
            request.isTech,
            request.requesterId,
            request.supportRegistryCategory);

        var filter = Builders<TicketInfo>.Filter;

        var searchFilter = filter.Empty;

        if (!string.IsNullOrWhiteSpace(request.ticketNumber))
        {
            searchFilter = filter.Regex(
                x => x.TicketNumber,
                new BsonRegularExpression(request.ticketNumber.Trim(), "i")
            );
        }
        else if (!string.IsNullOrWhiteSpace(request.fileNumber))
        {
            searchFilter = filter.Regex(
                x => x.FileNumber,
                new BsonRegularExpression(request.fileNumber.Trim(), "i")
            );
        }

        var accessFilter = filter.Empty;

        if (request.isTech)
        {
            accessFilter = filter.Empty;
        }
        else if (request.supportRegistryCategory.HasValue)
        {
            var normalRegistryTickets = filter.Eq(
                x => x.Category,
                request.supportRegistryCategory.Value
            );

            var registryTechnicalTickets = filter.And(
                filter.Eq(x => x.Category, TicketCategory.TechnicalSupport),
                filter.Eq(x => x.RegistryCategory, request.supportRegistryCategory.Value),
                filter.Eq(x => x.RaisedByRegistryStaff, true)
            );

            accessFilter = filter.Or(normalRegistryTickets, registryTechnicalTickets);
        }
        else
        {
            accessFilter = filter.Eq(x => x.creatorId, request.requesterId);
        }

        var combined = filter.And(searchFilter, accessFilter);

        var projection = Builders<TicketInfo>.Projection.Expression(x => new TicketSummary
        {
            Status = x.Status,
            Title = x.Title,
            Creator = new TicketCreator { Name = x.creatorName, Id = x.creatorId },
            LastInteraction = x.Correspondences.Any()
                ? x.Correspondences.Last().DateAdded
                : x.Created,
            DateCreated = x.Created,
            TicketId = x.id,
            TicketNumber = x.TicketNumber,
            Category = x.Category,
            RegistryCategory = x.RegistryCategory,
            TicketType = x.TicketType,
            ApplicationType = x.ApplicationType,
            RecordalType = x.RecordalType,
            FileNumber = x.FileNumber,
            RaisedByRegistryStaff = x.RaisedByRegistryStaff,
            IsEscalated = x.IsEscalated,
            EscalatedFromCategory = x.EscalatedFromCategory,
            Resolution = x.resolution
        });

        var results = await _ticketsCollection
            .Find(combined)
            .Project(projection)
            .SortByDescending(x => x.Created)
            .Limit(100)
            .ToListAsync();

        _logger.LogInformation("Ticket search completed. Result count: {Count}", results.Count);

        return results;
    }
    #endregion

}