using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Services;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace patentdesign.Controllers
{

    //[Authorize]
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

            return BadRequest(new { message = "Failed to change status" });
        }

        [HttpPost("CreateApplicationHistory")]
        public async Task<IActionResult> CreateApplicationHistory(
            [FromBody] ApplicationHistoryDto applicationHistoryDto)
        {
            var result = await adminServices.CreateApplicationHistory(applicationHistoryDto);
            if (result)
            {
                return Ok(result);
            }

            return BadRequest(new { message = "Failed to create application history" });
        }

        [HttpPatch("ApplicationHistory")]
        public async Task<IActionResult> UpdateApplicationHistory([FromBody] UpdateApplicationHistoryDto dto)
        {
            var updated = await adminServices.UpdateApplicationHistory(dto);
            if (updated) return Ok(updated);
            return NotFound(new { message = "Application history not updated" });
        }

        [HttpDelete("ApplicationHistory")]
        public async Task<IActionResult> DeleteApplicationHistory([FromBody] DeleteApplicationHistoryDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FileNumber) || string.IsNullOrWhiteSpace(dto.ApplicationId))
                return BadRequest(new { message = "FileNumber and ApplicationId are required" });

            var deleted = await adminServices.DeleteApplicationHistory(dto);
            if (deleted) return Ok(new { message = "Application history entry deleted successfully" });
            return NotFound(new { message = "Application history entry not found or could not be deleted" });
        }

        [HttpPost("SendAnnouncement")]
        public async Task<IActionResult> SendAnnouncement([FromBody] AnnouncementMailDto dto)
        {
            var mail = await adminServices.SendAnnouncementMail(dto);
            if (!mail) return BadRequest(new { message = "Failed to Send mail" });
            return Ok(new { message = "Bulk Email Sent" });
        }

        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetUserPassword(string email)
        {
            var reset = await adminServices.ResetUserPassword(email);
            if (!reset) return BadRequest(new { message = "Failed to Reset Password" });
            return Ok(new { message = "Reset Password" });
        }

        [HttpPost("UploadSignature")]
        public async Task<IActionResult> UploadSignature([FromForm] SignatoryDto dto)
        {
            var result = await adminServices.UploadSignature(dto);
            if (result) return Ok(new { message = "Signature Uploaded" });
            return BadRequest(new { message = "Failed to Upload Signature" });
        }

        [HttpGet("GetUserByEmail")]
        public async Task<IActionResult> GetUserByEmail([FromQuery] string email)
        {
            var user = await adminServices.GetUserByEmail(email);
            if (user == null) return BadRequest(new{message = "User not found"});
            return Ok(user);
        }

    }
}
