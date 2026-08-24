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

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost("ApplicationHistory")]
        [HttpPost("CreateApplicationHistory")]
        public async Task<IActionResult> CreateApplicationHistory(
            [FromBody] ApplicationHistoryDto applicationHistoryDto)
        {
            try
            {
                var result = await adminServices.CreateApplicationHistory(applicationHistoryDto);
                if (result != null)
                {
                    return Ok(new { success = true, data = result, message = "Application history created successfully" });
                }

                return BadRequest(new { success = false, message = "Failed to create application history" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Returns a single application history entry (<c>hist</c>) shaped so the SuperAdmin
        /// UI can pre-fill its recordal forms directly. See <see cref="ApplicationHistoryResponseDto"/>.
        /// </summary>
        [Authorize(Roles = "SuperAdmin")]
        [HttpGet("ApplicationHistory/{applicationId}")]
        public async Task<IActionResult> GetApplicationHistory([FromRoute] string applicationId)
        {
            if (string.IsNullOrWhiteSpace(applicationId))
                return BadRequest(new { message = "applicationId is required" });

            var hist = await adminServices.GetApplicationHistoryAsync(applicationId);
            if (hist == null) return NotFound(new { message = "Application history entry not found" });
            return Ok(hist);
        }

        /// <summary>
        /// TEMP DIAGNOSTIC: dumps the raw stored assignor/assignee data for every assignment
        /// entry in a file's application history, so we can see exactly what the SuperAdmin
        /// form has to work with. Open in a browser:
        /// <c>/api/admin/diag/assignment?fileNumber=TM/2024/00001</c>
        /// </summary>
        [AllowAnonymous]
        [HttpGet("diag/assignment")]
        public async Task<IActionResult> DiagAssignment([FromQuery] string fileNumber)
        {
            if (string.IsNullOrWhiteSpace(fileNumber))
                return BadRequest(new { message = "fileNumber is required" });

            var dump = await adminServices.DiagnoseAssignmentHistory(fileNumber);
            if (dump == null) return NotFound(new { message = "File not found for that fileNumber" });
            return Ok(dump);
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPatch("ApplicationHistory")]
        public async Task<IActionResult> UpdateApplicationHistory([FromBody] UpdateApplicationHistoryDto dto)
        {
            try
            {
                var updated = await adminServices.UpdateApplicationHistory(dto);
                if (updated != null)
                    return Ok(new { success = true, data = updated, message = "Application history updated successfully" });
                return NotFound(new { success = false, message = "Application history not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpDelete("ApplicationHistory")]
        public async Task<IActionResult> DeleteApplicationHistory([FromBody] DeleteApplicationHistoryDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FileNumber) || string.IsNullOrWhiteSpace(dto.ApplicationId))
                return BadRequest(new { message = "FileNumber and ApplicationId are required" });

            var updatedFile = await adminServices.DeleteApplicationHistory(dto);
            if (updatedFile != null) return Ok(updatedFile);
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
