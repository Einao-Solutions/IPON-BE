using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Response;
using patentdesign.Services;

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
    }
}
