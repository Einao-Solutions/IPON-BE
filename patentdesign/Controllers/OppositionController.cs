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
                return NotFound(new { message = e.Message });
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpPost("NewOpposition")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> NewOpposition([FromForm] OppositionRequestDto req)
    {
        try
        {
            var oppositionId = await oppositionService.SubmitOpposition(req);
            return Ok(new { success = true, oppositionId, message = "Opposition submitted successfully" });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
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
    public async Task<IActionResult> UpdateOppositionPayment(
        [FromQuery] string paymentId,
        [FromBody] PaymentUpdateDto dto)
    {
        try
        {
            if (dto?.Status != "success")
                return BadRequest(new { success = false, message = "Payment was not successful" });

            bool result = await oppositionService.UpdateOppositionPaymentStatus(paymentId);
            return Ok(new { success = true, message = "Payment confirmed" });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
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
        [FromQuery] int quantity = 50,
        [FromQuery] int skip = 0,
        [FromQuery] int? type = null)
    {
        ApplicationStatuses? tt = type != null ? Enum.GetValues<ApplicationStatuses>()[type ?? 0] : null;
        var result = await oppositionService.LoadSummary(quantity, skip, tt);
        return Ok(result);
    }

    [HttpGet("get")]
    public async Task<ActionResult<OppositionType>> GetOpposition([FromQuery] string id)
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

    // ─── Counter Statement Search ────────────────────────────────────────────
    [HttpGet("csSearchFile")]
    [HttpGet("CounterStatementSearch")]
    public async Task<IActionResult> CsSearchFile([FromQuery] string fileNumber)
    {
        try
        {
            var result = await oppositionService.CsSearchFile(fileNumber);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Message });
            return Ok(new { success = true, data = result });
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    // ─── Counter Statement Fee ───────────────────────────────────────────────
    [HttpGet("getCounterStatementFee")]
    public IActionResult GetCounterStatementFee()
    {
        return Ok(oppositionService.GetCounterStatementFee());
    }

    // ─── Submit Counter Statement ────────────────────────────────────────────
    [HttpPost("NewCounterStatement")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitCounterStatement([FromForm] CounterStatementRequestDto dto)
    {
        try
        {
            var (success, invoice, message) = await oppositionService.SubmitCounterStatement(dto);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(invoice);
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Update Counter Statement Payment (status → StatutoryDeclaration) ───
    [HttpPost("UpdateCounterStatementPayment")]
    public async Task<IActionResult> UpdateCounterStatementPayment([FromQuery] string paymentId)
    {
        try
        {
            var (success, message) = await oppositionService.UpdateCounterStatementPayment(paymentId);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Submit Statutory Declaration ────────────────────────────────────────
    [HttpPost("submitStatutoryDeclaration")]
    public async Task<IActionResult> SubmitStatutoryDeclaration([FromForm] StatutoryDeclarationRequestDto dto)
    {
        try
        {
            var (success, id, message) = await oppositionService.SubmitStatutoryDeclaration(dto);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, declarationId = id, message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Get Full Opposition Detail ──────────────────────────────────────────
    [HttpGet("getOppositionDetail")]
    public async Task<IActionResult> GetOppositionDetail([FromQuery] string? oppositionId, [FromQuery] string? fileNumber)
    {
        try
        {
            var result = await oppositionService.GetOppositionDetail(oppositionId, fileNumber);
            if (result == null)
                return NotFound(new { message = "Opposition not found" });
            return Ok(new { success = true, opposition = result });
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    // ─── Decline Opposition (trademark owner wins) ───────────────────────────
    [HttpPost("decline")]
    public async Task<IActionResult> DeclineOpposition([FromQuery] string oppositionId)
    {
        try
        {
            var (success, message) = await oppositionService.DeclineOpposition(oppositionId);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Uphold Opposition (opposer wins) ────────────────────────────────────
    [HttpPost("uphold")]
    public async Task<IActionResult> UpholdOpposition([FromQuery] string oppositionId)
    {
        try
        {
            var (success, message) = await oppositionService.UpholdOpposition(oppositionId);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Resolve Opposition (unified uphold/decline with decision) ───────────
    [HttpPost("resolve")]
    public async Task<IActionResult> ResolveOpposition([FromBody] ResolveOppositionDto dto)
    {
        try
        {
            var (success, message) = await oppositionService.ResolveOpposition(dto);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }
}