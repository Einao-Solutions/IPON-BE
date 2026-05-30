using Microsoft.AspNetCore.SignalR;
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
        private ILogger<NotificationServices> _logger;

        public NotificationServices(IHubContext<NotificationHub> hubContext, IMongoDatabase db, ILogger<NotificationServices> logger)
        {
            _hubContext = hubContext;
            _notifications = db.GetCollection<Notification>("Notifications");
            _logger = logger;
        }

        public async Task CreateNotificationAsync(CreateNotificationDto dto)
        {
            _logger.LogInformation("Creating notification for recipient {RecipientId} with title {Title}", dto.RecipientId, dto.Title);

            var notification = new Notification
            {
                Id = Guid.NewGuid().ToString(),
                Audience = NotificationAudience.User,
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

            await _notifications.InsertOneAsync(notification);
            
            await SendNotification(notification);
            _logger.LogInformation("Notification {NotificationId} created and sent to recipient {RecipientId}", notification.Id, notification.RecipientId);
        }
        private async Task SendNotification(Notification notification)
        {
            await _hubContext.Clients
                .User(notification?.RecipientId)
                .SendAsync(
                    "ReceiveNotification",
                    notification);
        }
        public async Task<List<Notification>> GetNotificationsAsync(string userId)
        {
            return await _notifications
                .Find(x =>
                    x.RecipientId == userId ||
                    x.Audience == NotificationAudience.System)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();
        }
        public async Task<long> GetUnreadCount(string userId)
        {
            var count = await _notifications
                .Find(n => !n.IsRead && (n.RecipientId == userId || n.Audience == NotificationAudience.System))
                .CountDocumentsAsync();

            return count;
        }
        public async Task MarkAsReadAsync(string id)
        {
            await _notifications.UpdateOneAsync(
                x => x.Id == id,
                Builders<Notification>.Update
                    .Set(x => x.IsRead, true)
                    .Set(x => x.ReadAt, DateTime.UtcNow)
            );
        }

        public async Task MarkAllAsReadAsync(string userId)
        {
            await _notifications.UpdateManyAsync(
                x => !x.IsRead && (x.RecipientId == userId || x.Audience == NotificationAudience.System),
                Builders<Notification>.Update
                    .Set(x => x.IsRead, true)
                    .Set(x => x.ReadAt, DateTime.UtcNow)
            );
        }
    }
}
