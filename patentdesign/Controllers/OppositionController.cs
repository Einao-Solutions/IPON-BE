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
    // [HttpPost("create")]
    // public async Task<ActionResult> CreateOpposition([FromBody] OppostionCreateType type)
    // {
    //     var data = await oppositionService.AddNewOpposition(
    //         type.description,
    //         type.name,
    //         type.email,
    //         type.number,
    //         type.address,
    //         type.fileUrl,
    //         type.fileID,
    //         type.title,
    //         type.userId,
    //         type.userName
    //     );
    //     return Ok(data);
    // }
    //
    // [HttpPost("respond")]
    // public async Task<ActionResult<List<TicketSummary>?>> Respond([FromBody] OppResReq data)
    // {
    //     var res = await oppositionService.AddResponse(data);
    //     return Ok(res);
    // }
    // [HttpPost("generate")]
    // public async Task<ActionResult<Object>> Generate([FromBody] GenerateOpReq data)
    // {
    //     var res = await oppositionService.Generate(data);
    //     return Ok(res);
    // }
    // [HttpPost("resolution")]
    // public async Task<ActionResult<List<TicketSummary>?>> Resolution([FromBody] OppResReq data)
    // {
    //     var res = await oppositionService.AddResolution(data);
    //     return Ok(res);
    // }
    //
    // [HttpPost("resolve")]
    // public async Task<ActionResult<OppositionType?>> Resolve([FromBody] AssUpdateReq data)
    // {
    //     var res = await oppositionService.UpdateOppositionStatus(data);
    //     if (res != null)
    //         return Ok(res);
    //     else return NotFound("omo....");
    // }
    // [HttpPost("payment")]
    // public async Task<ActionResult<OppositionType?>> Payment([FromBody] AssUpdateReq data)
    // {
    //     var res = await oppositionService.UpdateOppositionStatus(data);
    //     if (res != null)
    //         return Ok(res);
    //     else return NotFound("omo....");
    // }
    //
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
    // [HttpGet("count")]
    // public async Task<ActionResult> Count([FromQuery]string?userId=null)
    // {
    //     var result = await oppositionService.Count(userId);
    //     return Ok(result);
    //     
    // }
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
    // [HttpGet("getHistory")]
    // public async Task<ActionResult<List<ApplicationHistory>>> GetOppositionHistory([FromQuery]string id)
    // {
    //     var result = await oppositionService.GetOppositionHistory(id);
    //     return Ok(result);
    //     
    // }
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var result = await oppositionService.GetStats();
        return Ok(result);
    }
}