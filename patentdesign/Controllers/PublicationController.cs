using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Response;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers
{
    [ApiController]
    [Route("api/publication")]
    public class PublicationController(PublicationServices publicationServices) : ControllerBase
    {
        [HttpGet("GetPublication")]
        public async Task<IActionResult> GetJournal([FromQuery] int type, [FromQuery] DateTime start,
            [FromQuery] DateTime end)
        {
            var data = await publicationServices.GetBatchPublications(start, end, Enum.GetValues<FileTypes>()[type]);
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
        public async Task<IActionResult> SavePublication([FromBody]PublicationDto publication)
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
    }
}