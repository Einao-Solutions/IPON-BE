using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers
{
    [ApiController]
    [Route("api/publication")]
    public class PublicationController(PublicationServices publicationServices) : ControllerBase
    {

        [HttpGet("GetPublication")]
        public async Task<IActionResult> GetJournal([FromQuery] string batchVolume)
        {
            var data = await publicationServices.GetTrademarkJournal(batchVolume, 0, 0, null);
            Response.Headers.Add("Content-Disposition", "attachment; filename=journal.pdf");
            return File(data, "application/pdf", "journal.pdf");
        }

        [HttpGet("GetTrademarkPublication")]
        public async Task<IActionResult> GetTrademarkPublication([FromQuery] string? text = null,
            [FromQuery] int? index = null, [FromQuery] int? quantity = null)
        {
            var data = await publicationServices.GetTrademarkPublication(text, index, quantity);
            return Ok(data);
        }

        [HttpPost("SavePublication")]
        public async Task<IActionResult> SavePublication([FromBody] PublicationDto publication)
        {
            try
            {
                await publicationServices.SavePublication(publication);
                return Ok();
            }
            catch (Exception e)
            {
                return BadRequest(new { message = e.Message });
            }
        }

        [Authorize(Roles = nameof(Roles.TrademarkRegistrar) + "," + nameof(Roles.TrademarkPublication) + "," + nameof(Roles.SuperAdmin)+ "," + nameof(Roles.ActingTrademarkRegistrar))]
        [HttpPost("BatchJournal")]
        public async Task<IActionResult> BatchPublications([FromBody] StaffBatchRequest dto)
        {
            try
            {
                await publicationServices.BatchJournal(dto);
                return Ok();
            }
            catch (Exception e)
            {
                return BadRequest(new { message = e.Message });
            }
        }

        [HttpGet("GetJournals")]
        public async Task<IActionResult> GetJournals([FromQuery] int year)
        {
            var data = await publicationServices.GetJournals(year);
            return Ok(data);
        }

        [HttpGet("GetJournalCost")]
        public async Task<IActionResult> GetJournalCost([FromQuery] string userId, string batch)
        {
            var data = await publicationServices.GetJournalCost(userId, batch);
            return Ok(data);
        }

        [HttpPost("UpdateRequestStatus")]
        public async Task<IActionResult> UpdateRequestStatus([FromBody] JournalRequestStatusDto dto)
        {
            try
            {
                var result = await publicationServices.UpdateJournalRequestStatus(dto.AppId, dto.UserId);
                return Ok(new { success = result.Item1, message = result.Item2 });
            }
            catch (Exception e)
            {
                return BadRequest(new { message = e.Message });
            } 
        }
    }
}