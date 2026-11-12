using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;

namespace patentdesign.Controllers;

[Authorize]
[TicketAccess]
[ApiController] [Route("api/tickets")]
public class TicketController(TicketServices ticketService) :ControllerBase
{
    private TicketViewer Viewer => HttpContext.Features.Get<TicketViewer>()
        ?? throw new InvalidOperationException("The authenticated ticket viewer has not been resolved.");

    #region Ticket lifecycle endpoints
    /// <summary>
    /// Creates a new support ticket.
    /// </summary>
    /// <param name="ticket">Ticket payload containing creator, category, and correspondence details.</param>
    /// <returns>The created ticket resource and location route.</returns>
    [HttpPost("Create")]
    public async Task<ActionResult> CreateNewTicket([FromBody] TicketInfo ticket)
    {
        try
        {
            await ticketService.CreateTicketAsync(ticket, Viewer);
            return CreatedAtAction(nameof(GetTicket), new { id = ticket.id }, ticket);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Gets a support ticket by id.
    /// </summary>
    /// <param name="id">Ticket identifier.</param>
    /// <returns>The requested ticket if found.</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<TicketInfo?>> GetTicket(string id)
    {
        var ticket = await ticketService.GetTicketAsync(id, Viewer);
        if (ticket == null) return NotFound();
        return Ok(ticket);
    }

    /// <summary>
    /// Retrieves filtered ticket summaries.
    /// </summary>
    /// <param name="info">Filter and pagination settings for summary retrieval.</param>
    /// <returns>List of ticket summaries that match the supplied filters.</returns>
    [HttpPost("TicketSummaries")]
    public async Task<ActionResult<List<TicketSummary>?>> GetTicketSummaries([FromBody] TicketsSummariesType info)
    {
        var res= await ticketService.GetTicketsSummariesAsync(info, Viewer);
        return Ok(res);
    }

    /// <summary>
    /// Resolves one or more tickets. Unanswered tickets close automatically after 24 hours.
    /// </summary>
    /// <param name="res">Ticket ids with resolution text.</param>
    /// <returns>Operation acknowledgement status.</returns>
    [HttpPost("CloseTicket")]
    public async Task<ActionResult> CloseTickets([FromBody] ResolveTicketType res)
    {
        try
        {
            var result = await ticketService.CloseTicketsAsync(res, Viewer);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
    
    /// <summary>
    /// Triggers ticket deletion workflow.
    /// </summary>
    /// <returns>Deletion operation response.</returns>
    [HttpPost("DeleteTicket")]
    public async Task<ActionResult> DeleteTicket()
    {
        await ticketService.DeleteTicketAsync();
        return Ok("wow");

    }
    #endregion
    
    #region Ticket communication endpoints
    /// <summary>
    /// Marks messages through the supplied sequence as read for the authenticated viewer.
    /// </summary>
    /// <param name="ticketId">Ticket identifier.</param>
    /// <param name="request">Last message sequence actually displayed to the viewer.</param>
    /// <returns>The viewer's updated read progress and unread counts.</returns>
    [HttpPost("{ticketId}/Read")]
    public async Task<ActionResult<TicketReadResult>> MarkTicketRead(string ticketId, [FromBody] ReadTicketRequest request)
    {
        if (!request.ThroughMessageSequence.HasValue || request.ThroughMessageSequence.Value < 0)
        {
            return BadRequest("throughMessageSequence is required and must be nonnegative.");
        }
        var result = await ticketService.MarkTicketReadAsync(ticketId, request.ThroughMessageSequence.Value, Viewer);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Adds a correspondence message to an existing ticket.
    /// </summary>
    /// <param name="newMessageInfo">Message content and target ticket metadata.</param>
    /// <returns>The updated ticket after message insertion.</returns>
    [HttpPost("AddMessage")]
    public async Task<ActionResult> AddMessageToTicket([FromBody] NewCorrespondenceType newMessageInfo)
    {
        try
        {
            var res = await ticketService.AddMessageAsync(newMessageInfo, Viewer);
            if (res == null) return NotFound();
            return Ok(res);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }
    #endregion

    #region Ticket analytics endpoints
    /// <summary>
    /// Retrieves ticket statistics for a requester and optional support filters.
    /// </summary>
    /// <param name="userId">Optional creator id used to scope results.</param>
    /// <param name="category">Optional support category filter.</param>
    /// <param name="registryCategory">Optional registry category filter.</param>
    /// <param name="raisedByRegistryStaff">Optional registry staff origin filter.</param>
    /// <param name="status">Optional ticket status filter.</param>
    /// <param name="isEscalated">Optional category escalation filter.</param>
    /// <param name="isEscalatedToDev">Optional developer escalation filter.</param>
    /// <returns>Ticket counts by status for the selected scope.</returns>
    [HttpGet("GetStats")]
    public async Task<ActionResult> GetTicketStats(
        [FromQuery] string? userId,
        [FromQuery] int? category = null,
        [FromQuery] int? registryCategory = null,
        [FromQuery] bool? raisedByRegistryStaff = null,
        [FromQuery] TicketState? status = null,
        [FromQuery] bool? isEscalated = null,
        [FromQuery] bool? isEscalatedToDev = null)
    {
        var tickets = await ticketService.TicketStats(
            userId,
            category,
            registryCategory,
            raisedByRegistryStaff,
            Viewer,
            status,
            isEscalated,
            isEscalatedToDev
        );

        return Ok(tickets);
    }
    #endregion

    #region Ticket escalation and search endpoints
    /// <summary>
    /// Escalates an active technical ticket to developers and marks it In Progress.
    /// </summary>
    /// <param name="request">Ticket identifier and escalation actor details.</param>
    /// <returns>The updated ticket, or a validation or conflict response.</returns>
    [HttpPost("EscalateToDev")]
    public async Task<ActionResult<TicketInfo>> EscalateTicketToDev([FromBody] EscalateTicketToDevRequest request)
    {
        try
        {
            var result = await ticketService.EscalateTicketToDevAsync(request, Viewer);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Escalates a ticket to another support category.
    /// </summary>
    /// <param name="request">Escalation details including target category and actor.</param>
    /// <returns>The updated ticket after escalation.</returns>
    [HttpPost("Escalate")]
    public async Task<ActionResult<TicketInfo>> EscalateTicket([FromBody] EscalateTicketRequest request)
    {
        try
        {
            var result = await ticketService.EscalateTicketAsync(request, Viewer);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Searches tickets by ticket number or file number with access rules.
    /// </summary>
    /// <param name="request">Search criteria and caller context.</param>
    /// <returns>A list of matching ticket summaries.</returns>
    [HttpPost("Search")]
    public async Task<ActionResult<List<TicketSummary>>> SearchTickets([FromBody] TicketSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ticketNumber) &&
            string.IsNullOrWhiteSpace(request.fileNumber))
        {
            return BadRequest("Provide either ticketNumber or fileNumber.");
        }

        if (!string.IsNullOrWhiteSpace(request.ticketNumber) &&
            !string.IsNullOrWhiteSpace(request.fileNumber))
        {
            return BadRequest("Search by either ticketNumber or fileNumber, not both.");
        }

        var result = await ticketService.SearchTicketsAsync(request, Viewer);
        return Ok(result);
    }
    #endregion

}