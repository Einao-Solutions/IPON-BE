using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Models;
using patentdesign.Services;
using static QuestPDF.Helpers.Colors;

namespace patentdesign.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    public class NotificationController(NotificationServices notificationServices) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery]string userId) 
        {
            var notifs = await notificationServices.GetNotificationsAsync(userId);
            return Ok(notifs);
        }
        [HttpGet("UnreadCount")]
        public async Task<IActionResult> GetUnreadCount([FromQuery]string userId)
        {
            var count = await notificationServices.GetUnreadCount(userId);
            return Ok(new { unreadCount = count });
        }
        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkAsRead(string id)
        {
            await notificationServices.MarkAsReadAsync(id);
            return Ok(new { message = "Notification marked as read" });
        }
        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllAsRead([FromQuery] string userId)
        {
            await notificationServices.MarkAllAsReadAsync(userId);
            return Ok(new { message = "All notifications marked as read" });
        }
        [HttpPost]
        public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationDto dto)
        {
            await notificationServices.CreateNotificationAsync(dto);
            return Ok(new { message = "Notification created" });
        }
    }
}
