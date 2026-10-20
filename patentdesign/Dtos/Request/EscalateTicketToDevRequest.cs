using System.ComponentModel.DataAnnotations;

namespace patentdesign.Dtos.Request;

public record EscalateTicketToDevRequest
{
    [Required]
    public string TicketId { get; set; } = string.Empty;
    public string? EscalatedById { get; set; }
    public string? EscalatedByName { get; set; }
    public string? AutoMessage { get; set; }
}
