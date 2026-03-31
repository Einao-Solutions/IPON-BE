using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Models;
using patentdesign.Services;
using ZstdSharp.Unsafe;

namespace patentdesign.Controllers;


[ApiController] [Route("api/opposition")]
public class OppositionController(OppositionService oppositionService) :ControllerBase
{
    [HttpGet("OppositionSearch")]
    public async Task<IActionResult> SearchOpposition(string fileNumber)
    {
        try
        {
            var opp = await oppositionService.OppositionSearch(fileNumber);
            return Ok(opp);
        }
        catch (Exception e)
        {
            if (e.Message == "File not found")
            {
                return NotFound(new { message = e.Message });
            }
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpPost("NewOpposition")]
    public async Task<IActionResult> NewOpposition(OppositionRequestDto req)
    {
        try
        {
            bool result = await oppositionService.SubmitOpposition(req);
            return Ok();
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message });
        }
    }
    [HttpPost("StaffOpposition")]

    public async Task<IActionResult> StaffOpposition([FromBody]OppositionRequestDto req)
    {
        try
        {
            bool result = await oppositionService.StaffOpposition(req);
            return Ok();
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message });
        }
    }
    [HttpPost("UpdateOppositionPayment")]
    public async Task<IActionResult> UpdateOppositionPayment([FromQuery] string paymentId)
    {
        try
        {
            bool result = await oppositionService.UpdateOppositionPaymentStatus(paymentId);
            return Ok();
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpGet("GetAllOpposition")]
    public async Task<IActionResult> GetAllOpposition()
    {
        var opps = await oppositionService.GetOppositionRequests();
        
        return Ok(opps);
    }

    [HttpGet("count")]
    public async Task<IActionResult> GetOppositionCount()
    {
        var count = await oppositionService.GetOppositionCount();
        return Ok(count);
    }
   
    [HttpGet("loadSummary")]
    public async Task<ActionResult> LoadSummary(
        [FromQuery] int quantity, 
        [FromQuery] int skip,
        [FromQuery] int? type)
    {
        ApplicationStatuses? tt = type != null ? Enum.GetValues<ApplicationStatuses>()[type??0] : null;
        var result=await oppositionService.LoadSummary(quantity, skip, tt);
        return Ok(result);
    }
   
    [HttpGet("get")]
    public async Task<ActionResult<OppositionType>> GetOpposition([FromQuery]string id)
    {
        var result = await oppositionService.GetOpposition(id);
        return Ok(result);
        
    }

    [HttpPost("notify")]
    public async Task<IActionResult> Notify([FromQuery] string oppId)
    {
        bool result = await oppositionService.NotifyApplicant(oppId);
        return Ok(result);
    }
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var result = await oppositionService.GetStats();
        return Ok(result);
    }
}