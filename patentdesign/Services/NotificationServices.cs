using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;
using Serilog;

namespace patentdesign.Services
{
    public class NotificationServices
    {
        private static IMongoCollection<Notification> _notifications;
        private static IMongoCollection<Filling> _files;
        private static IMongoCollection<AppUser> _users;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<NotificationServices> _logger;
        private readonly EmailServices _emailServices;

        public NotificationServices(IHubContext<NotificationHub> hubContext, IMongoDatabase db, ILogger<NotificationServices> logger, EmailServices emailServices)
        {
            _hubContext = hubContext;
            _notifications = db.GetCollection<Notification>("notifications");
            _files = db.GetCollection<Filling>("files");
            _logger = logger;
            _users = db.GetCollection<AppUser>("appUsers");
            _emailServices = emailServices;
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
        public async Task<int> RenewalNotifications()
        {
            _logger.LogInformation("Renewal Notifications");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var cutoffDate = today.AddDays(90);

            var filter = Builders<Filling>.Filter.And(
                Builders<Filling>.Filter.Eq(p => p.FileStatus, ApplicationStatuses.Active),
                Builders<Filling>.Filter.Gte("ApplicationHistory.0.ExpiryDate", today),
                Builders<Filling>.Filter.Lte("ApplicationHistory.0.ExpiryDate", cutoffDate));

            var files = await _files.Find(filter).ToListAsync();

            if (files.Count == 0)
            {
                _logger.LogInformation("No trademarks found eligible for publishing");
                return 0;
            }

            var sentCount = 0;

            foreach (var file in files) 
            {
                var expiryDate = file.ApplicationHistory?.FirstOrDefault()?.ExpiryDate;
                if (!expiryDate.HasValue)
                {
                    _logger.LogWarning("Skipping renewal notification for file {FileId} because no expiry date was found", file.Id);
                    continue;
                }

                var daysUntilExpiry = expiryDate.Value.DayNumber - today.DayNumber;
                if (daysUntilExpiry != 90 && daysUntilExpiry != 0)
                {
                    continue;
                }

                var recipient = await ResolveRecipientAsync(file.CreatorAccount);
                if (string.IsNullOrWhiteSpace(recipient))
                {
                    _logger.LogWarning("Skipping renewal notification for file {FileId} because no recipient could be resolved", file.Id);
                    continue;
                }

                var notificationDto = BuildRenewalNotificationDto(file, recipient, expiryDate.Value, daysUntilExpiry == 0);

                var wasSent = await HasRenewalReminderBeenSentAsync(file.FileId, recipient, notificationDto.Title);
                if (wasSent)
                {
                    _logger.LogDebug("Skipping duplicate renewal reminder for file {FileId} and recipient {Recipient}", file.FileId, recipient);
                    continue;
                }

                await CreateNotificationAsync(notificationDto);

                var emailRecipient = await ResolveEmailAsync(file.CreatorAccount);
                if (!string.IsNullOrWhiteSpace(emailRecipient))
                {
                    var emailDto = BuildRenewalReminderEmailDto(file, emailRecipient, expiryDate.Value, daysUntilExpiry == 0);
                    await _emailServices.SendMail(emailDto);
                }
                else
                {
                    _logger.LogWarning("Renewal reminder email skipped for file {FileId} because no email could be resolved", file.Id);
                }

                sentCount++;
            }

            return sentCount;

        }
        private async Task<bool> HasRenewalReminderBeenSentAsync(string fileNumber, string recipientId, string title)
        {
            var sentFilter = Builders<Notification>.Filter.And(
                Builders<Notification>.Filter.Eq(x => x.Category, NotificationCategory.Renewal),
                Builders<Notification>.Filter.Eq(x => x.FileNumber, fileNumber),
                Builders<Notification>.Filter.Eq(x => x.RecipientId, recipientId),
                Builders<Notification>.Filter.Eq(x => x.Title, title));

            return await _notifications.Find(sentFilter).AnyAsync();
        }
        private async Task<string?> ResolveEmailAsync(string? creatorAccount)
        {
            if (string.IsNullOrWhiteSpace(creatorAccount))
            {
                return null;
            }

            var fileCreator = await _users.Find(u => u.CreatorId == creatorAccount).FirstOrDefaultAsync();
            if (fileCreator is null || string.IsNullOrWhiteSpace(fileCreator.Email))
            {
                return null;
            }

            return fileCreator.Email;
        }
        private async Task<string?> ResolveRecipientAsync(string? creatorAccount)
        {
            if (string.IsNullOrWhiteSpace(creatorAccount))
            {
                return null;
            }

            var fileCreator = await _users.Find(u => u.CreatorId == creatorAccount).FirstOrDefaultAsync();
            if (fileCreator is null)
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(fileCreator.Email)
                ? fileCreator.Email
                : fileCreator.Id;
        }
        private static CreateNotificationDto BuildRenewalNotificationDto(Filling file, string recipient, DateOnly expiryDate, bool isExpiryDay)
        {
            return new CreateNotificationDto
            {
                Audience = NotificationAudience.User,
                RecipientId = recipient,
                Title = isExpiryDay ? "Trademark Renewal Due Today" : "Trademark Renewal Reminder (90 Days)",
                Message = isExpiryDay
                    ? $"Your trademark with File Number {file.FileId} is due for renewal today ({expiryDate:MMMM dd, yyyy}). Please take necessary action immediately."
                    : $"Your trademark with File Number {file.FileId} is due for renewal on {expiryDate:MMMM dd, yyyy}. This is your 90-day reminder.",
                Category = NotificationCategory.Renewal,
                Priority = NotificationPriority.High,
                CreatedBy = "System",
                FileNumber = file.FileId,
                ActionUrl = $"https://yourdomain.com/trademarks/{file.FileId}/renewal"
            };
        }
        private static EmailDto BuildRenewalReminderEmailDto(Filling file, string recipientEmail, DateOnly expiryDate, bool isExpiryDay)
        {
            return new EmailDto
            {
                To = recipientEmail,
                Subject = isExpiryDay ? "Trademark Renewal Due Today" : "Trademark Renewal Reminder (90 Days)",
                EmailType = EmailType.RenewalReminder,
                RenewalReminder = new RenewalReminder
                {
                    ApplicantName = file.applicants?.FirstOrDefault()?.Name ?? "Applicant",
                    FileNumber = file.FileId,
                    Title = file.TitleOfTradeMark ?? file.TitleOfDesign ?? file.TitleOfInvention ?? "Trademark",
                    RenewalDue = expiryDate.ToDateTime(TimeOnly.MinValue)
                }
            };
        }
    }
}
