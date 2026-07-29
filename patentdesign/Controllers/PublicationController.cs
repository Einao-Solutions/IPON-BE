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
            var data = await publicationServices.GetTrademarkJournal(batchVolume);
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
        public async Task<IActionResult> GetJournals()
        {
            var data = await publicationServices.GetJournals();
            return Ok(data);
        }
    }
}