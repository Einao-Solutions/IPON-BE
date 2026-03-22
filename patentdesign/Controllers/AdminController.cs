using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Response;
using patentdesign.Services;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace patentdesign.Controllers
{

    [Authorize]
    [ApiController]
    [Route("api/admin")]
    public class AdminController(AdminServices adminServices) : ControllerBase
    {
        [HttpPost("ChangeStatus")]
        public async Task<IActionResult> ChangeApplicationStatus([FromBody] StatusChangeDto statusChangeDto)
        {
            var result = await adminServices.ChangeFileStatus(statusChangeDto);
            if (result != null)
            {
                return Ok(result);
            }
            return BadRequest(new {message = "Failed to change status"});
        }
        [HttpPost("CreateApplicationHistory")]
        public async Task<IActionResult> CreateApplicationHistory([FromBody] ApplicationHistoryDto applicationHistoryDto)
        {
            var result = await adminServices.CreateApplicationHistory(applicationHistoryDto);
            if (result)
            {
                return Ok(result);
            }
            return BadRequest(new {message = "Failed to create application history"});
        }
        [HttpPatch("ApplicationHistory")]
        public async Task<IActionResult> UpdateApplicationHistory([FromBody] UpdateApplicationHistoryDto dto)
        {
            var updated = await adminServices.UpdateApplicationHistory(dto);
            if (updated) return Ok(updated);
            return NotFound(new { message = "Application history not updated" });
        }

        [HttpPost("SendAnnouncement")]
        public async Task<IActionResult> SendAnnouncement([FromBody] AnnouncementMailDto dto)
        {
            var mail = await adminServices.SendAnnouncementMail(dto);
            if (!mail) return BadRequest(new { message = "Failed to Send mail"});
            return Ok(new { message = "Bulk Email Sent" });
        }

    }
}
