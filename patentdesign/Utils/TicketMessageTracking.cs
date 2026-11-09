using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using patentdesign.Models;

namespace patentdesign.Utils;

public static class TicketMessageTracking
{
    public const int CurrentVersion = 1;

    public static string ReaderKey(string userId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));

    public static long LastReadSequence(TicketInfo ticket, TicketViewer viewer)
    {
        if (ticket.MessageTrackingVersion != CurrentVersion) return ticket.Correspondences?.Count ?? 0;
        var read = ticket.ReadMessageSequences?.GetValueOrDefault(ReaderKey(viewer.UserId)) ?? 0;
        return Math.Max(ticket.UnreadBaselineSequence, read);
    }

    public static TicketInfo Apply(TicketInfo ticket, TicketViewer? viewer)
    {
        ticket.UnreadCount = 0;
        if (ticket.MessageTrackingVersion != CurrentVersion)
        {
            ticket.LatestMessageSequence = ticket.Correspondences?.Count ?? 0;
        }
        else if (viewer != null)
        {
            var read = LastReadSequence(ticket, viewer);
            ticket.UnreadCount = ticket.Correspondences?.LongCount(message =>
                message.MessageSequence > read && message.SenderId != viewer.UserId) ?? 0;
        }
        ticket.HasUnreadMessages = ticket.UnreadCount > 0;
        return ticket;
    }

    public static void InitializeNewTicket(TicketInfo ticket)
    {
        ticket.MessageTrackingVersion = CurrentVersion;
        ticket.UnreadBaselineSequence = 0;
        ticket.ReadMessageSequences = new();
        ticket.LatestMessageSequence = 0;
        foreach (var message in ticket.Correspondences ?? [])
        {
            message.MessageSequence = checked(++ticket.LatestMessageSequence);
        }
        ticket.HasUnreadMessages = false;
        ticket.UnreadCount = 0;
    }

    private static BsonDocument MessagesExpression() => new("$ifNull", new BsonArray { "$Correspondences", new BsonArray() });

    public static BsonValue LatestSequenceExpression() => new BsonDocument("$cond", new BsonArray
    {
        new BsonDocument("$eq", new BsonArray { "$MessageTrackingVersion", CurrentVersion }),
        new BsonDocument("$ifNull", new BsonArray { "$LatestMessageSequence", 0L }),
        new BsonDocument("$toLong", new BsonDocument("$size", MessagesExpression()))
    });

    public static BsonValue UnreadCountExpression(TicketViewer? viewer)
    {
        if (viewer == null) return new BsonInt64(0);
        var read = new BsonDocument("$max", new BsonArray
        {
            new BsonDocument("$ifNull", new BsonArray { "$UnreadBaselineSequence", 0L }),
            new BsonDocument("$ifNull", new BsonArray { "$ReadMessageSequences." + ReaderKey(viewer.UserId), 0L })
        });
        var unread = new BsonDocument("$size", new BsonDocument("$filter", new BsonDocument
        {
            { "input", MessagesExpression() },
            { "as", "message" },
            { "cond", new BsonDocument("$and", new BsonArray
                {
                    new BsonDocument("$gt", new BsonArray { "$$message.MessageSequence", read }),
                    new BsonDocument("$ne", new BsonArray { "$$message.SenderId", new BsonDocument("$literal", viewer.UserId) })
                })
            }
        }));
        return new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$MessageTrackingVersion", CurrentVersion }),
            unread,
            0L
        });
    }

    public static BsonDocument SummaryProjection(TicketViewer? viewer)
    {
        var projection = new BsonDocument
        {
            { "_id", 0 },
            { "TicketId", "$_id" },
            { "Creator", new BsonDocument { { "Name", "$creatorName" }, { "_id", "$creatorId" } } },
            { "DateCreated", "$Created" },
            { "LastInteraction", new BsonDocument("$ifNull", new BsonArray
                { new BsonDocument("$arrayElemAt", new BsonArray { "$Correspondences.DateAdded", -1 }), "$Created" }) },
            { "Resolution", "$resolution" },
            { "LatestMessageSequence", LatestSequenceExpression() },
            { "UnreadCount", UnreadCountExpression(viewer) },
            { "HasUnreadMessages", new BsonDocument("$gt", new BsonArray { UnreadCountExpression(viewer), 0L }) }
        };
        foreach (var field in new[]
        {
            "Status", "StatusBeforeResolution", "Title", "TicketNumber", "Category", "RegistryCategory", "TicketType", "ApplicationType",
            "RecordalType", "FileNumber", "RaisedByRegistryStaff", "IsEscalated", "EscalatedFromCategory",
            "IsEscalatedToDev", "EscalatedToDevAt", "ResolvedAt", "ClosedAt"
        })
        {
            projection[field] = 1;
        }
        return projection;
    }

    public static BsonDocument[] LegacyInitializationStages()
    {
        var count = new BsonDocument("$size", MessagesExpression());
        return
        [
            new BsonDocument("$set", new BsonDocument
            {
                { "Correspondences", new BsonDocument("$map", new BsonDocument
                    {
                        { "input", new BsonDocument("$range", new BsonArray { 0, count }) },
                        { "as", "index" },
                        { "in", new BsonDocument("$mergeObjects", new BsonArray
                            {
                                new BsonDocument("$arrayElemAt", new BsonArray { MessagesExpression(), "$$index" }),
                                new BsonDocument("MessageSequence", new BsonDocument("$toLong", new BsonDocument("$add", new BsonArray { "$$index", 1 })))
                            }) }
                    }) },
                { "LatestMessageSequence", new BsonDocument("$toLong", count) },
                { "UnreadBaselineSequence", new BsonDocument("$toLong", count) },
                { "ReadMessageSequences", new BsonDocument("$literal", new BsonDocument()) },
                { "MessageTrackingVersion", CurrentVersion }
            })
        ];
    }
}
