using Microsoft.AspNetCore.Mvc;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers;


[ApiController] [Route("api/tickets")]
public class TicketController(TicketServices ticketService) :ControllerBase
{
    [HttpPost("Create")]
    public async Task<ActionResult> CreateNewTicket([FromBody] TicketInfo ticket)
    {
        await ticketService.CreateTicketAsync(ticket);
        return CreatedAtAction(nameof(GetTicket), new { id = ticket.id }, ticket);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TicketInfo?>> GetTicket(string id)
    {
        return await ticketService.GetTicketAsync(id);
    }
    [HttpPost("TicketSummaries")]
    public async Task<ActionResult<List<TicketSummary>?>> GetTicketSummaries([FromBody] TicketsSummariesType info)
    {
        var res= await ticketService.GetTicketsSummariesAsync(info);
        return Ok(res);
    }

    [HttpPost("CloseTicket")]
    public async Task<ActionResult> CloseTickets([FromBody] ResolveTicketType res)
    {
        var result=await ticketService.CloseTicketsAsync(res);
        return Ok(result);
    }
    
    [HttpPost("DeleteTicket")]
    public async Task<ActionResult> DeleteTicket()
    {
        await ticketService.DeleteTicketAsync();
        return Ok("wow");

    }
    
    [HttpPost("AddMessage")]
    public async Task<ActionResult> AddMessageToTicket([FromBody] NewCorrespondenceType newMessageInfo)
    {
        var res= await ticketService.AddMessageAsync(newMessageInfo);
        return Ok(res);
    }
    
    [HttpGet("GetStats")]
    public async Task<ActionResult> GetTicketStats([FromQuery] string? userId)
    {
        var tickets=await ticketService.TicketStats(userId);
        return Ok(tickets);

    }
    
    
}