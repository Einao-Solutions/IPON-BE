using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers;


[ApiController] [Route("api/tickets")]
public class TicketController(TicketServices ticketService) :ControllerBase
{
    #region Ticket lifecycle endpoints
    /// <summary>
    /// Creates a new support ticket.
    /// </summary>
    /// <param name="ticket">Ticket payload containing creator, category, and correspondence details.</param>
    /// <returns>The created ticket resource and location route.</returns>
    [HttpPost("Create")]
    public async Task<ActionResult> CreateNewTicket([FromBody] TicketInfo ticket)
    {
        await ticketService.CreateTicketAsync(ticket);
        return CreatedAtAction(nameof(GetTicket), new { id = ticket.id }, ticket);
    }

    /// <summary>
    /// Gets a support ticket by id.
    /// </summary>
    /// <param name="id">Ticket identifier.</param>
    /// <returns>The requested ticket if found.</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<TicketInfo?>> GetTicket(string id)
    {
        return await ticketService.GetTicketAsync(id);
    }

    /// <summary>
    /// Retrieves filtered ticket summaries.
    /// </summary>
    /// <param name="info">Filter and pagination settings for summary retrieval.</param>
    /// <returns>List of ticket summaries that match the supplied filters.</returns>
    [HttpPost("TicketSummaries")]
    public async Task<ActionResult<List<TicketSummary>?>> GetTicketSummaries([FromBody] TicketsSummariesType info)
    {
        var res= await ticketService.GetTicketsSummariesAsync(info);
        return Ok(res);
    }

    /// <summary>
    /// Closes one or more tickets and records a resolution.
    /// </summary>
    /// <param name="res">Ticket ids with resolution text.</param>
    /// <returns>Operation acknowledgement status.</returns>
    [HttpPost("CloseTicket")]
    public async Task<ActionResult> CloseTickets([FromBody] ResolveTicketType res)
    {
        var result=await ticketService.CloseTicketsAsync(res);
        return Ok(result);
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
    /// Adds a correspondence message to an existing ticket.
    /// </summary>
    /// <param name="newMessageInfo">Message content and target ticket metadata.</param>
    /// <returns>The updated ticket after message insertion.</returns>
    [HttpPost("AddMessage")]
    public async Task<ActionResult> AddMessageToTicket([FromBody] NewCorrespondenceType newMessageInfo)
    {
        var res= await ticketService.AddMessageAsync(newMessageInfo);
        return Ok(res);
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
    /// <returns>Ticket counts by status for the selected scope.</returns>
    [HttpGet("GetStats")]
    public async Task<ActionResult> GetTicketStats(
        [FromQuery] string? userId,
        [FromQuery] int? category = null,
        [FromQuery] int? registryCategory = null,
        [FromQuery] bool? raisedByRegistryStaff = null)
    {
        var tickets = await ticketService.TicketStats(
            userId,
            category,
            registryCategory,
            raisedByRegistryStaff
        );

        return Ok(tickets);
    }
    #endregion

    #region Ticket escalation and search endpoints
    /// <summary>
    /// Escalates a ticket to another support category.
    /// </summary>
    /// <param name="request">Escalation details including target category and actor.</param>
    /// <returns>The updated ticket after escalation.</returns>
    [HttpPost("Escalate")]
    public async Task<ActionResult<TicketInfo>> EscalateTicket([FromBody] EscalateTicketRequest request)
    {
        var result = await ticketService.EscalateTicketAsync(request);
        if (result == null) return NotFound();
        return Ok(result);
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

        var result = await ticketService.SearchTicketsAsync(request);
        return Ok(result);
    }
    #endregion

}