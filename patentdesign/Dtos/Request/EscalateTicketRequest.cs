using patentdesign.Enums;

namespace patentdesign.Dtos.Request
{
    public record EscalateTicketRequest
    {
        public string? TicketId { get; set; }
        public TicketCategory EscalateToCategory { get; set; }
        public string? EscalatedById { get; set; }
        public string? EscalatedByName { get; set; }
        public string? AutoMessage { get; set; }
    }
}
