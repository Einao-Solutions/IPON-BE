using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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
    private readonly IMongoCollection<TicketInfo> _ticketsCollection;
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

    private async Task<TicketInfo?> FindTrackedTicketAsync(string id, TicketViewer? viewer)
    {
        var access = Builders<TicketInfo>.Filter.Eq(x => x.id, id) & TicketAccessRules.Filter(viewer);
        var ticket = await _ticketsCollection.Find(access).FirstOrDefaultAsync();
        if (ticket == null || ticket.MessageTrackingVersion == TicketMessageTracking.CurrentVersion) return ticket;

        var filter = access & Builders<TicketInfo>.Filter.Ne(x => x.MessageTrackingVersion, TicketMessageTracking.CurrentVersion);
        PipelineDefinition<TicketInfo, TicketInfo> pipeline = TicketMessageTracking.LegacyInitializationStages();
        var initialized = await _ticketsCollection.FindOneAndUpdateAsync(filter,
            Builders<TicketInfo>.Update.Pipeline(pipeline),
            new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After });
        return initialized ?? await _ticketsCollection.Find(access).FirstOrDefaultAsync();
    }

    private static FilterDefinition<TicketInfo> SequenceFilter(TicketInfo ticket) =>
        Builders<TicketInfo>.Filter.Eq(x => x.LatestMessageSequence, ticket.LatestMessageSequence)
        & Builders<TicketInfo>.Filter.Eq(x => x.MessageTrackingVersion, TicketMessageTracking.CurrentVersion);

    private static TicketInfo? ForViewer(TicketInfo? ticket, TicketViewer? viewer)
    {
        if (ticket == null) return null;
        NormalizeTicketAttachments(ticket);
        return TicketMessageTracking.Apply(ticket, viewer);
    }

    public async Task<TicketReadResult?> MarkTicketReadAsync(string id, long throughMessageSequence, TicketViewer viewer)
    {
        if (throughMessageSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughMessageSequence));
        var ticket = await FindTrackedTicketAsync(id, viewer);
        if (ticket == null) return null;

        var through = Math.Min(throughMessageSequence, ticket.LatestMessageSequence);
        var readerField = nameof(TicketInfo.ReadMessageSequences) + "." + TicketMessageTracking.ReaderKey(viewer.UserId);
        var filter = Builders<TicketInfo>.Filter.Eq(x => x.id, id) & TicketAccessRules.Filter(viewer);
        var updated = await _ticketsCollection.FindOneAndUpdateAsync(filter,
            Builders<TicketInfo>.Update.Max(readerField, through),
            new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After });
        if (updated == null) return null;
        TicketMessageTracking.Apply(updated, viewer);
        return new TicketReadResult
        {
            TicketId = updated.id,
            LastReadMessageSequence = TicketMessageTracking.LastReadSequence(updated, viewer),
            HasUnreadMessages = updated.HasUnreadMessages,
            UnreadCount = updated.UnreadCount,
            LatestMessageSequence = updated.LatestMessageSequence
        };
    }

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
    public async Task CreateTicketAsync(TicketInfo ticket, TicketViewer? viewer = null)
    {
        _logger.LogInformation("Creating ticket for CreatorId: {CreatorId}, FileNumber: {FileNumber}", ticket.creatorId, ticket.FileNumber);

        if (!Enum.IsDefined(ticket.Status) || ticket.Status is TicketState.Closed or TicketState.Resolved)
        {
            throw new ArgumentException("New tickets must have an active status.");
        }

        ticket.ResolvedAt = null;
        ticket.ClosedAt = null;
        ticket.resolution = null;
        ticket.StatusBeforeResolution = null;
        ticket.IsEscalatedToDev = false;
        ticket.EscalatedToDevAt = null;
        ticket.EscalatedToDevById = null;
        ticket.EscalatedToDevByName = null;
        if (viewer != null)
        {
            ticket.creatorId = viewer.UserId;
            ticket.creatorName = viewer.DisplayName;
            foreach (var message in ticket.Correspondences ?? [])
            {
                message.SenderId = viewer.UserId;
                message.SenderName = viewer.DisplayName;
            }
        }
        TicketMessageTracking.InitializeNewTicket(ticket);
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

    public async Task<bool> CloseTicketsAsync(ResolveTicketType res, TicketViewer? viewer = null)
    {
        _logger.LogInformation("Resolving tickets. Count: {Count}", res.ticketId?.Count ?? 0);

        if (res.ticketId == null || res.ticketId.Count == 0)
        {
            _logger.LogInformation("Close tickets skipped. No ticket ids provided.");
            return true;
        }

        ArgumentNullException.ThrowIfNull(res.resolution);
        if (viewer != null)
        {
            var ids = res.ticketId.Distinct().ToList();
            var accessible = await _ticketsCollection.CountDocumentsAsync(
                Builders<TicketInfo>.Filter.In(x => x.id, ids) & TicketAccessRules.Filter(viewer));
            if (accessible != ids.Count) throw new UnauthorizedAccessException("One or more tickets are not accessible.");
        }
        var now = DateTime.UtcNow;
        var filter = Builders<TicketInfo>.Filter.In(f => f.id, res.ticketId)
            & Builders<TicketInfo>.Filter.Nin(f => f.Status, new[] { TicketState.Resolved, TicketState.Closed })
            & TicketAccessRules.Filter(viewer);
        var resolution = res.resolution with
        {
            Date = now,
            StaffId = viewer?.UserId ?? res.resolution.StaffId,
            StaffName = viewer?.DisplayName ?? res.resolution.StaffName
        };
        List<UpdateDefinition<TicketInfo>> updates =
        [
            Builders<TicketInfo>.Update.Set(f => f.resolution, resolution),
            Builders<TicketInfo>.Update.Set(f => f.Status, TicketState.Resolved),
            Builders<TicketInfo>.Update.Set(f => f.ResolvedAt, now),
            Builders<TicketInfo>.Update.Set(f => f.ClosedAt, null)
        ];
        var values = Builders<TicketInfo>.Update.Combine(updates)
            .Render(BsonSerializer.LookupSerializer<TicketInfo>(), BsonSerializer.SerializerRegistry)["$set"].AsBsonDocument;
        var fields = new BsonDocument();
        foreach (var value in values)
        {
            fields[value.Name] = new BsonDocument("$literal", value.Value);
        }
        fields[nameof(TicketInfo.StatusBeforeResolution)] = "$Status";
        PipelineDefinition<TicketInfo, TicketInfo> pipeline = new[] { new BsonDocument("$set", fields) };
        var result = await _ticketsCollection.UpdateManyAsync(
            filter,
            Builders<TicketInfo>.Update.Pipeline(pipeline));

        _logger.LogInformation("Close tickets completed. Acknowledged: {Acknowledged}, Matched: {Matched}, Modified: {Modified}",
            result.IsAcknowledged,
            result.MatchedCount,
            result.ModifiedCount);

        return result.IsAcknowledged;
    }

    public async Task<long> CloseResolvedTicketsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<TicketInfo>.Filter.Eq(x => x.Status, TicketState.Resolved)
            & Builders<TicketInfo>.Filter.Ne(x => x.ResolvedAt, null)
            & Builders<TicketInfo>.Filter.Lte(x => x.ResolvedAt, now.AddHours(-24));
        var update = Builders<TicketInfo>.Update
            .Set(x => x.Status, TicketState.Closed)
            .Set(x => x.ClosedAt, now);

        var result = await _ticketsCollection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount;
    }
    
    public async Task DeleteTicketAsync()
    {
        _logger.LogInformation("DeleteTicketAsync called. No implementation available.");
    }
    #endregion

    #region Ticket communication
    public async Task<TicketInfo?> AddMessageAsync(NewCorrespondenceType correspondence, TicketViewer? viewer = null)
    {
        _logger.LogInformation("Adding message to ticket {TicketId} with new status {NewStatus}", correspondence.ticketId, correspondence.newStatus);

        if (!Enum.IsDefined(correspondence.newStatus))
        {
            throw new ArgumentException("Invalid ticket status.");
        }

        ArgumentNullException.ThrowIfNull(correspondence.correspondence);
        if (viewer != null)
        {
            correspondence.correspondence.SenderId = viewer.UserId;
            correspondence.correspondence.SenderName = viewer.DisplayName;
        }
        correspondence.correspondence.Attachment = NormalizeAttachmentLink(correspondence.correspondence.Attachment);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var ticket = await FindTrackedTicketAsync(correspondence.ticketId, viewer);
            if (ticket == null) return null;

            var now = DateTime.UtcNow;
            if (ticket.Status == TicketState.Closed ||
                (ticket.Status == TicketState.Resolved &&
                 (!ticket.ResolvedAt.HasValue || ticket.ResolvedAt.Value <= now.AddHours(-24))))
            {
                throw new InvalidOperationException("This ticket is closed or its reply window has expired.");
            }

            if (ticket.Status != TicketState.Resolved &&
                correspondence.newStatus is TicketState.Closed or TicketState.Resolved)
            {
                throw new ArgumentException("Messages must use an active status. Use CloseTicket to resolve a ticket.");
            }

            var isCreator = !string.IsNullOrWhiteSpace(ticket.creatorId) &&
                ticket.creatorId == correspondence.correspondence.SenderId;
            var reopen = ticket.Status == TicketState.Resolved && isCreator;
            var previouslyInProgress = ticket.StatusBeforeResolution.HasValue
                ? ticket.StatusBeforeResolution == TicketState.InProgress
                : ticket.IsEscalatedToDev;
            var status = ticket.Status switch
            {
                TicketState.Resolved when !reopen => TicketState.Resolved,
                TicketState.Resolved => previouslyInProgress ? TicketState.InProgress : TicketState.AwaitingStaff,
                TicketState.InProgress => TicketState.InProgress,
                _ => isCreator ? TicketState.AwaitingStaff : TicketState.AwaitingUser
            };

            correspondence.correspondence.DateAdded = now;
            correspondence.correspondence.MessageSequence = checked(ticket.LatestMessageSequence + 1);
            var filter = Builders<TicketInfo>.Filter.Eq(x => x.id, correspondence.ticketId)
                & Builders<TicketInfo>.Filter.Eq(x => x.Status, ticket.Status)
                & Builders<TicketInfo>.Filter.Eq(x => x.ResolvedAt, ticket.ResolvedAt)
                & SequenceFilter(ticket)
                & TicketAccessRules.Filter(viewer);
            var update = Builders<TicketInfo>.Update
                .Push(x => x.Correspondences, correspondence.correspondence)
                .Inc(x => x.LatestMessageSequence, 1L)
                .Set(x => x.Status, status);
            if (reopen)
            {
                update = update.Set(x => x.ResolvedAt, null)
                    .Set(x => x.ClosedAt, null)
                    .Set(x => x.resolution, null);
            }

            var options = new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After };
            var result = await _ticketsCollection.FindOneAndUpdateAsync(filter, update, options);
            if (result != null) return ForViewer(result, viewer);
        }

        throw new InvalidOperationException("The ticket changed while adding your message. Please retry.");
    }
    #endregion

    #region Ticket retrieval and reporting
    public async Task<TicketInfo?> GetTicketAsync(string id, TicketViewer? viewer = null)
    {
        _logger.LogInformation("Fetching ticket by Id: {TicketId}", id);

        var result = await FindTrackedTicketAsync(id, viewer);

        _logger.LogInformation("Fetch ticket completed for Id: {TicketId}. Found: {Found}", id, result != null);

        return ForViewer(result, viewer);
    }

    public async Task<List<TicketSummary>> GetTicketsSummariesAsync(TicketsSummariesType info, TicketViewer? viewer = null)
    {
        _logger.LogInformation("Fetching ticket summaries. CreatorId: {CreatorId}, Status: {Status}, Category: {Category}, RegistryCategory: {RegistryCategory}, StartIndex: {StartIndex}, Amount: {Amount}",
            info.creatorId,
            info.status,
            info.category,
            info.registryCategory,
            info.startIndex,
            info.amount);

        var filter = Builders<TicketInfo>.Filter;

        var creatorFilter = string.IsNullOrWhiteSpace(info.creatorId) || info.creatorId == "null"
            ? filter.Empty : filter.Eq(x => x.creatorId, info.creatorId);
        var statusFilter = info.status == null ? filter.Empty : filter.Eq(x => x.Status, info.status);
        var titleFilter = info.title == null
            ? filter.Empty
            : filter.Regex(f => f.Title, new BsonRegularExpression(info.title, "i"));
        var categoryFilter = info.category == null ? filter.Empty : filter.Eq(x => x.Category, info.category);
        var registryCategoryFilter = info.registryCategory == null
            ? filter.Empty
            : filter.Eq(x => x.RegistryCategory, info.registryCategory);
        var escalatedFilter = info.isEscalated == null ? filter.Empty : filter.Eq(x => x.IsEscalated, info.isEscalated.Value);
        var devEscalatedFilter = info.isEscalatedToDev == null
            ? filter.Empty
            : info.isEscalatedToDev.Value
                ? filter.Eq(x => x.IsEscalatedToDev, true)
                : filter.Ne(x => x.IsEscalatedToDev, true);
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
            escalatedFilter, devEscalatedFilter, registryStaffFilter,
            ticketNumberFilter, fileNumberFilter, appTypeFilter,
            startDateFilter, endDateFilter, TicketAccessRules.Filter(viewer)
        );

        var tickets = await _ticketsCollection
            .Find(combined)
            .Project<TicketSummary>(TicketMessageTracking.SummaryProjection(viewer))
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
        bool? raisedByRegistryStaff = null,
        TicketViewer? viewer = null,
        TicketState? status = null,
        bool? isEscalated = null,
        bool? isEscalatedToDev = null)
    {
        _logger.LogInformation("Calculating ticket stats. CreatorId: {CreatorId}, Category: {Category}, RegistryCategory: {RegistryCategory}, RaisedByRegistryStaff: {RaisedByRegistryStaff}",
            creatorId,
            category,
            registryCategory,
            raisedByRegistryStaff);

        var filter = Builders<TicketInfo>.Filter;
        var baseFilter = TicketAccessRules.Filter(viewer);

        if (!string.IsNullOrWhiteSpace(creatorId) && creatorId != "null")
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

        if (status.HasValue) baseFilter &= filter.Eq(x => x.Status, status.Value);
        if (isEscalated.HasValue) baseFilter &= isEscalated.Value
            ? filter.Eq(x => x.IsEscalated, true) : filter.Ne(x => x.IsEscalated, true);
        if (isEscalatedToDev.HasValue) baseFilter &= isEscalatedToDev.Value
            ? filter.Eq(x => x.IsEscalatedToDev, true) : filter.Ne(x => x.IsEscalatedToDev, true);

        var groupedCounts = await _ticketsCollection
            .Aggregate()
            .Match(baseFilter)
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", "$Status" },
                { "Count", new BsonDocument("$sum", 1L) },
                { "UnreadTickets", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$gt", new BsonArray { TicketMessageTracking.UnreadCountExpression(viewer), 0L }),
                    1L,
                    0L
                })) }
            })
            .ToListAsync();
        var statusCounts = groupedCounts.Select(group => BsonSerializer.Deserialize<TicketStatusCount>(new BsonDocument
        {
            { "Status", group["_id"] },
            { "Count", group["Count"] }
        })).ToList();

        var total = statusCounts.Sum(x => x.Count);
        var awaitingStaff = statusCounts.FirstOrDefault(x => x.Status == TicketState.AwaitingStaff)?.Count ?? 0;
        var awaitingUser = statusCounts.FirstOrDefault(x => x.Status == TicketState.AwaitingUser)?.Count ?? 0;
        var closed = statusCounts.FirstOrDefault(x => x.Status == TicketState.Closed)?.Count ?? 0;
        var inProgress = statusCounts.FirstOrDefault(x => x.Status == TicketState.InProgress)?.Count ?? 0;
        var resolved = statusCounts.FirstOrDefault(x => x.Status == TicketState.Resolved)?.Count ?? 0;
        var unreadTickets = groupedCounts.Sum(group => group["UnreadTickets"].ToInt64());

        return new TicketStatsReturnType
        {
            total = total,
            staff = awaitingStaff,
            user = awaitingUser,
            closed = closed,
            inProgress = inProgress,
            resolved = resolved,
            unreadTickets = unreadTickets
        };
    }
    #endregion

    #region Ticket escalation
    public async Task<TicketInfo?> EscalateTicketToDevAsync(EscalateTicketToDevRequest request, TicketViewer? viewer = null)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var ticket = await FindTrackedTicketAsync(request.TicketId!, viewer);
            if (ticket == null) return null;
            if (ticket.Category != TicketCategory.TechnicalSupport)
            {
                throw new ArgumentException("Only technical support tickets can be escalated to developers.");
            }
            if (ticket.Status is TicketState.Closed or TicketState.Resolved)
            {
                throw new InvalidOperationException("Only active tickets can be escalated to developers.");
            }
            if (ticket.IsEscalatedToDev) return ForViewer(ticket, viewer);

            var now = DateTime.UtcNow;
            var filter = Builders<TicketInfo>.Filter.Eq(x => x.id, request.TicketId)
                & Builders<TicketInfo>.Filter.Eq(x => x.Category, TicketCategory.TechnicalSupport)
                & Builders<TicketInfo>.Filter.Nin(x => x.Status, new[] { TicketState.Resolved, TicketState.Closed })
                & Builders<TicketInfo>.Filter.Ne(x => x.IsEscalatedToDev, true)
                & SequenceFilter(ticket)
                & TicketAccessRules.Filter(viewer);
            var update = Builders<TicketInfo>.Update
                .Set(x => x.IsEscalatedToDev, true)
                .Set(x => x.Status, TicketState.InProgress)
                .Set(x => x.EscalatedToDevAt, now)
                .Set(x => x.EscalatedToDevById, viewer?.UserId ?? request.EscalatedById)
                .Set(x => x.EscalatedToDevByName, viewer?.DisplayName ?? request.EscalatedByName)
                .Inc(x => x.LatestMessageSequence, 1L)
                .Push(x => x.Correspondences, new TicketCorrespondence
                {
                    SenderId = "system",
                    SenderName = "System",
                    Message = string.IsNullOrWhiteSpace(request.AutoMessage) ? "Escalated to developers." : request.AutoMessage,
                    DateAdded = now,
                    MessageSequence = checked(ticket.LatestMessageSequence + 1)
                });
            var result = await _ticketsCollection.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After });
            if (result != null) return ForViewer(result, viewer);
        }
        throw new InvalidOperationException("The ticket changed during escalation. Please retry.");
    }

    public async Task<TicketInfo?> EscalateTicketAsync(EscalateTicketRequest request, TicketViewer? viewer = null)
    {
        _logger.LogInformation("Escalating ticket {TicketId} to category {EscalateToCategory} by {EscalatedById}",
            request.TicketId,
            request.EscalateToCategory,
            request.EscalatedById);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var ticket = await FindTrackedTicketAsync(request.TicketId!, viewer);
            if (ticket == null) return null;
            var now = DateTime.UtcNow;
            var systemMsg = new TicketCorrespondence
            {
                SenderId = "system",
                SenderName = "System",
                Message = request.AutoMessage,
                DateAdded = now,
                MessageSequence = checked(ticket.LatestMessageSequence + 1)
            };
            var filter = Builders<TicketInfo>.Filter.Eq(x => x.id, request.TicketId)
                & SequenceFilter(ticket) & TicketAccessRules.Filter(viewer);
            var update = Builders<TicketInfo>.Update
                .Set(x => x.Category, request.EscalateToCategory)
                .Set(x => x.IsEscalated, true)
                .Set(x => x.EscalatedFromCategory, ticket.Category)
                .Set(x => x.EscalatedAt, now)
                .Set(x => x.EscalatedById, viewer?.UserId ?? request.EscalatedById)
                .Set(x => x.EscalatedByName, viewer?.DisplayName ?? request.EscalatedByName)
                .Inc(x => x.LatestMessageSequence, 1L)
                .Push(x => x.Correspondences, systemMsg);
            var result = await _ticketsCollection.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<TicketInfo> { ReturnDocument = ReturnDocument.After });
            if (result != null) return ForViewer(result, viewer);
        }
        throw new InvalidOperationException("The ticket changed during escalation. Please retry.");
    }
    #endregion

    #region Ticket search
    public async Task<List<TicketSummary>> SearchTicketsAsync(TicketSearchRequest request, TicketViewer viewer)
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

        var combined = filter.And(searchFilter, TicketAccessRules.Filter(viewer));

        var results = await _ticketsCollection
            .Find(combined)
            .Project<TicketSummary>(TicketMessageTracking.SummaryProjection(viewer))
            .SortByDescending(x => x.Created)
            .Limit(100)
            .ToListAsync();

        _logger.LogInformation("Ticket search completed. Result count: {Count}", results.Count);

        return results;
    }
    #endregion

}