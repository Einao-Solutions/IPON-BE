using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;

namespace patentdesign.Services
{
    public class NotificationServices
    {
        private static IMongoCollection<Notification> _notifications;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<NotificationServices> _logger;

        public NotificationServices(IHubContext<NotificationHub> hubContext, IMongoDatabase db, ILogger<NotificationServices> logger)
        {
            _hubContext = hubContext;
            _notifications = db.GetCollection<Notification>("notifications");
            _logger = logger;
        }

        public async Task CreateNotificationAsync(CreateNotificationDto dto)
        {
            if (dto is null)
            {
                _logger.LogWarning("CreateNotificationAsync was called with a null payload");
                throw new ArgumentNullException(nameof(dto));
            }

            _logger.LogInformation("Creating notification for recipient {RecipientId} with title {Title}", dto.RecipientId, dto.Title);

            var notification = new Notification
            {
                Id = Guid.NewGuid().ToString(),
                Audience = dto.Audience,
                RecipientId = dto.RecipientId,
                Title = dto.Title,
                Message = dto.Message,
                Category = dto.Category,
                Priority = dto.Priority,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = dto.CreatedBy,
                FileNumber = dto?.FileNumber,
                ActionUrl = dto?.ActionUrl,
            };

            if (notification.Audience == NotificationAudience.User && string.IsNullOrWhiteSpace(notification.RecipientId))
            {
                _logger.LogWarning("User notification {NotificationId} was rejected because RecipientId is missing", notification.Id);
                throw new ArgumentException("RecipientId is required for user notifications", nameof(dto));
            }

            if (notification.Audience == NotificationAudience.System)
            {
                notification.RecipientId = null;
            }

            await _notifications.InsertOneAsync(notification);
            _logger.LogDebug("Notification {NotificationId} inserted into Notifications collection", notification.Id);
            
            await SendNotification(notification);
            _logger.LogInformation("Notification {NotificationId} created and sent to recipient {RecipientId}", notification.Id, notification.RecipientId);
        }
        private async Task SendNotification(Notification notification)
        {
            _logger.LogDebug("Sending notification {NotificationId} to recipient {RecipientId}", notification?.Id, notification?.RecipientId);

            await _hubContext.Clients
                .User(notification?.RecipientId)
                .SendAsync(
                    "ReceiveNotification",
                    notification);

            _logger.LogDebug("Notification {NotificationId} delivered to SignalR client for recipient {RecipientId}", notification?.Id, notification?.RecipientId);
        }
        public async Task<List<Notification>> GetNotificationsAsync(string userId)
        {
            _logger.LogDebug("Retrieving notifications for user {UserId}", userId);

            var audienceFilter = Builders<Notification>.Filter.Or(
                Builders<Notification>.Filter.Eq(x => x.RecipientId, userId),
                Builders<Notification>.Filter.Eq(x => x.Audience, NotificationAudience.System),
                Builders<Notification>.Filter.Eq("Audience", NotificationAudience.System.ToString()),
                Builders<Notification>.Filter.Eq("Audience", (int)NotificationAudience.System));

            var notifications = await _notifications
                .Find(audienceFilter)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();

            _logger.LogInformation("Retrieved {NotificationCount} notifications for user {UserId}", notifications.Count, userId);

            return notifications;
        }
        public async Task<long> GetUnreadCount(string userId)
        {
            _logger.LogDebug("Retrieving unread notification count for user {UserId}", userId);

            var audienceFilter = Builders<Notification>.Filter.Or(
                Builders<Notification>.Filter.Eq(x => x.RecipientId, userId),
                Builders<Notification>.Filter.Eq(x => x.Audience, NotificationAudience.System),
                Builders<Notification>.Filter.Eq("Audience", NotificationAudience.System.ToString()),
                Builders<Notification>.Filter.Eq("Audience", (int)NotificationAudience.System));

            var unreadFilter = Builders<Notification>.Filter.And(
                Builders<Notification>.Filter.Eq(x => x.IsRead, false),
                audienceFilter);

            var count = await _notifications
                .Find(unreadFilter)
                .CountDocumentsAsync();

            _logger.LogInformation("Unread notification count for user {UserId} is {UnreadCount}", userId, count);

            return count;
        }
        public async Task MarkAsReadAsync(string id)
        {
            _logger.LogInformation("Marking notification {NotificationId} as read", id);

            var result = await _notifications.UpdateOneAsync(
                x => x.Id == id,
                Builders<Notification>.Update
                    .Set(x => x.IsRead, true)
                    .Set(x => x.ReadAt, DateTime.UtcNow)
            );

            if (result.MatchedCount == 0)
            {
                _logger.LogWarning("Notification {NotificationId} was not found while marking as read", id);
                return;
            }

            _logger.LogInformation("Notification {NotificationId} marked as read", id);
        }

        public async Task MarkAllAsReadAsync(string userId)
        {
            _logger.LogInformation("Marking all unread notifications as read for user {UserId}", userId);

            var audienceFilter = Builders<Notification>.Filter.Or(
                Builders<Notification>.Filter.Eq(x => x.RecipientId, userId),
                Builders<Notification>.Filter.Eq(x => x.Audience, NotificationAudience.System),
                Builders<Notification>.Filter.Eq("Audience", NotificationAudience.System.ToString()),
                Builders<Notification>.Filter.Eq("Audience", (int)NotificationAudience.System));

            var unreadFilter = Builders<Notification>.Filter.And(
                Builders<Notification>.Filter.Eq(x => x.IsRead, false),
                audienceFilter);

            var result = await _notifications.UpdateManyAsync(
                unreadFilter,
                Builders<Notification>.Update
                    .Set(x => x.IsRead, true)
                    .Set(x => x.ReadAt, DateTime.UtcNow)
            );

            _logger.LogInformation("Marked {ModifiedCount} notifications as read for user {UserId}", result.ModifiedCount, userId);
        }
    }
}
