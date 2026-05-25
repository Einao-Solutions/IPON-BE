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
        [FromQuery] string? paymentId,
        [FromBody] PaymentUpdateDto? dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(paymentId))
                return BadRequest(new { success = false, error = "paymentId is required" });

            // Accept both: frontend with no body, or frontend that sends { "status": "success" }
            if (dto != null && dto.Status != null && dto.Status.ToLower() != "success")
                return BadRequest(new { success = false, message = "Payment was not successful" });

            bool result = await oppositionService.UpdateOppositionPaymentStatus(paymentId);
            return Ok(new { success = true, message = "Payment confirmed" });
        }
        catch (Exception e)
        {
            if (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { success = false, error = e.Message });
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
        [FromQuery] int? type = null,
        [FromQuery] string? userId = null)
    {
        ApplicationStatuses? tt = type != null ? (ApplicationStatuses)type.Value : null;
        var result = await oppositionService.LoadSummary(quantity, skip, tt, userId);
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

    // ─── Generate Payment (RRR) for Opposition-related flows ───────────────────
    [HttpPost("generate")]
    public async Task<IActionResult> GeneratePayment([FromBody] GenerateOppositionPaymentDto dto)
    {
        try
        {
            var result = await oppositionService.GenerateOppositionPayment(dto);
            return Ok(result);
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Statutory Declaration Search ──────────────────────────────────────────
    [HttpGet("StatutoryDeclarationSearch")]
    public async Task<IActionResult> StatutoryDeclarationSearch([FromQuery] string? oppositionId, [FromQuery] string? fileNumber)
    {
        try
        {
            var result = await oppositionService.StatutoryDeclarationSearch(oppositionId, fileNumber);
            return Ok(new { success = true, data = result });
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new { success = false, message = e.Message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Submit Statutory Declaration ────────────────────────────────────────
    [HttpPost("NewStatutoryDeclaration")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> NewStatutoryDeclaration([FromForm] StatutoryDeclarationRequestDto dto)
    {
        try
        {
            var (success, invoice, message) = await oppositionService.SubmitStatutoryDeclaration(dto);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, data = invoice });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Update Statutory Declaration Payment ────────────────────────────────
    [HttpPost("UpdateStatutoryDeclarationPayment")]
    public async Task<IActionResult> UpdateStatutoryDeclarationPayment(
        [FromQuery] string paymentId,
        [FromBody] PaymentUpdateDto dto)
    {
        try
        {
            if (dto?.Status != "success")
                return BadRequest(new { success = false, message = "Payment was not successful" });

            var (success, message) = await oppositionService.UpdateStatutoryDeclarationPayment(paymentId);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, message });
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
            if (string.IsNullOrWhiteSpace(oppositionId) && string.IsNullOrWhiteSpace(fileNumber))
                return BadRequest(new { success = false, message = "Either oppositionId or fileNumber is required" });

            var result = await oppositionService.GetOppositionDetail(oppositionId, fileNumber);
            if (result == null)
                return NotFound(new { message = "Opposition not found" });

            // result is a list — return the first item as a single object so the UI can read fields directly
            var list = result as System.Collections.IList;
            var single = (list != null && list.Count > 0) ? list[0] : result;
            return Ok(new { success = true, data = single });
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

    // ─── Backfill PaymentId into ApplicationHistory for existing oppositions ──
    [HttpPost("backfillPaymentIds")]
    public async Task<IActionResult> BackfillPaymentIds()
    {
        try
        {
            var count = await oppositionService.BackfillOppositionPaymentIds();
            return Ok(new { success = true, message = $"Updated {count} file(s) with opposition PaymentId." });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Backfill SD statuses for existing paid declarations ─────────────────
    [HttpPost("backfillSdStatuses")]
    public async Task<IActionResult> BackfillSdStatuses()
    {
        try
        {
            var count = await oppositionService.BackfillStatutoryDeclarationStatuses();
            return Ok(new { success = true, message = $"Updated {count} opposition(s) to AwaitingOfficeProcess." });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Opposition Acknowledgement Letter ─────────────────────────────────────
    [HttpGet("oppositionAcknowledgementLetter")]
    public async Task<IActionResult> GetOppositionAcknowledgementLetter([FromQuery] string? oppositionId, [FromQuery] string? paymentId)
    {
        try
        {
            byte[] pdf;
            if (!string.IsNullOrEmpty(oppositionId))
                pdf = await oppositionService.GenerateOppositionAcknowledgementLetter(oppositionId);
            else if (!string.IsNullOrEmpty(paymentId))
                pdf = await oppositionService.GenerateOppositionAcknowledgementLetterByPaymentId(paymentId);
            else
                return BadRequest(new { success = false, message = "Provide oppositionId or paymentId" });

            return File(pdf, "application/pdf", "OppositionAcknowledgement.pdf");
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new { success = false, message = e.Message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Counter Statement Acknowledgement Letter ─────────────────────────────
    [HttpGet("counterStatementLetter")]
    public async Task<IActionResult> GetCounterStatementLetter([FromQuery] string? counterStatementId, [FromQuery] string? paymentId)
    {
        try
        {
            byte[] pdf;
            if (!string.IsNullOrEmpty(counterStatementId))
                pdf = await oppositionService.GenerateCounterStatementLetter(counterStatementId);
            else if (!string.IsNullOrEmpty(paymentId))
                pdf = await oppositionService.GenerateCounterStatementLetterByPaymentId(paymentId);
            else
                return BadRequest(new { success = false, message = "Provide counterStatementId or paymentId" });

            return File(pdf, "application/pdf", "CounterStatementAcknowledgement.pdf");
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new { success = false, message = e.Message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Statutory Declaration Acknowledgement Letter ─────────────────────────
    [HttpGet("statutoryDeclarationLetter")]
    public async Task<IActionResult> GetStatutoryDeclarationLetter([FromQuery] string? statutoryDeclarationId, [FromQuery] string? paymentId)
    {
        try
        {
            byte[] pdf;
            if (!string.IsNullOrEmpty(statutoryDeclarationId))
                pdf = await oppositionService.GenerateStatutoryDeclarationLetter(statutoryDeclarationId);
            else if (!string.IsNullOrEmpty(paymentId))
                pdf = await oppositionService.GenerateStatutoryDeclarationLetterByPaymentId(paymentId);
            else
                return BadRequest(new { success = false, message = "Provide statutoryDeclarationId or paymentId" });

            return File(pdf, "application/pdf", "StatutoryDeclarationAcknowledgement.pdf");
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new { success = false, message = e.Message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── New Opposition Withdrawal ────────────────────────────────────────────
    [HttpPost("NewOppositionWithdrawal")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> NewOppositionWithdrawal([FromForm] OppositionWithdrawalRequestDto dto)
    {
        try
        {
            var (success, invoice, message) = await oppositionService.SubmitOppositionWithdrawal(dto);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, data = invoice });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Update Opposition Withdrawal Payment ─────────────────────────────────
    [HttpPost("UpdateOppositionWithdrawalPayment")]
    public async Task<IActionResult> UpdateOppositionWithdrawalPayment(
        [FromQuery] string paymentId,
        [FromBody] PaymentUpdateDto dto)
    {
        try
        {
            if (dto?.Status != "success")
                return BadRequest(new { success = false, message = "Payment was not successful" });
            var (success, message) = await oppositionService.UpdateOppositionWithdrawalPayment(paymentId);
            if (!success)
                return BadRequest(new { success = false, message });
            return Ok(new { success = true, message = "Withdrawal payment confirmed" });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    [HttpPost("OppositionAmendmentCost")]
    public async Task<IActionResult> GetOppositionAmendmentCost(OppositionAmendmentReq req)
    {
        var result = await oppositionService.TrademarkAmendmentCost(req);
        if (result == null)
        {
            return BadRequest(new { message = "Failed to get cost" });
        }
        return Ok(result);
    }
    [HttpPost("OppositionAmendment")]
    public async Task<IActionResult> SaveAmendment(OppositionAmendmentDto req)
    {
        var result = await oppositionService.TrademarkAmendment(req);
        if (result == null)
        {
            return BadRequest( new { message = "Failed to save amendment application"});
        }
        return Ok(result);
    }
    [HttpPost("TreatAmendment")]
    public async Task<IActionResult> ApproveTrademarkAmendment([FromBody] TreatRecordalDto dto)
    {
        try
        {
            var (success, message) = await oppositionService.ApproveTrademarkAmendment(dto);
            if (!success)
                return BadRequest(new { success, message });

            return Ok(new { success, message });
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new { success = false, message = e.Message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    [HttpPost("TreatWithdrawal")]
    public async Task<IActionResult> TreatWithdrawal([FromBody] TreatWithdrawalDto? dto)
    {
        if (dto == null)
            return BadRequest(new { success = false, message = "Request body is required" });

        // Role check: TrademarkOpposition=4, Tech=20, SuperAdmin=21
        var allowedRoles = new[] { 4, 20, 21 };
        if (dto.Role == null || !allowedRoles.Contains(dto.Role.Value))
            return StatusCode(403, new { success = false, message = "Access denied. Insufficient role." });

        var action = dto.Action?.Trim().ToLower();
        if (action != "approve" && action != "refuse")
            return BadRequest(new { success = false, message = "Action must be 'approve' or 'refuse'" });
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { success = false, message = "Reason is required" });

        try
        {
            var (success, message) = await oppositionService.TreatWithdrawal(dto);
            if (!success)
                return BadRequest(new { success, message });
            return Ok(new { success, message });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Backfill: fix files stuck on Opposition after withdrawal was approved ─
    [HttpPost("backfillWithdrawnFiles")]
    public async Task<IActionResult> BackfillWithdrawnFiles()
    {
        try
        {
            var count = await oppositionService.BackfillWithdrawnFileStatuses();
            return Ok(new { success = true, message = $"Fixed {count} file(s) stuck on Opposition status." });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }

    // ─── Debug: inspect withdrawn oppositions and their linked files ─────────
    [HttpGet("debugWithdrawnOppositions")]
    public async Task<IActionResult> DebugWithdrawnOppositions()
    {
        var result = await oppositionService.DebugWithdrawnOppositions();
        return Ok(result);
    }

    // ─── One-shot: patch ApplicationHistory.CurrentStatus for already-backfilled files ─
    [HttpPost("backfillWithdrawnApplicationHistory")]
    public async Task<IActionResult> BackfillWithdrawnApplicationHistory()
    {
        try
        {
            var count = await oppositionService.BackfillWithdrawnApplicationHistory();
            return Ok(new { success = true, message = $"Fixed ApplicationHistory.CurrentStatus on {count} file(s)." });
        }
        catch (Exception e)
        {
            return BadRequest(new { success = false, message = e.Message });
        }
    }
}