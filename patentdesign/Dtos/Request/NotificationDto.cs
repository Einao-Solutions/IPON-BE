using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class CreateNotificationDto
    {
        public NotificationAudience Audience { get; set; }
        public NotificationCategory Category { get; set; }
        public NotificationPriority Priority { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string? RecipientId { get; set; }
        public string? ActionUrl { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? FileNumber { get; set; }

    }
    public class GetNotificationsDto
    {
        public List<Notification> Notifications { get; set; }
        public int UnreadCount { get; set; } = 0;

    }
}
