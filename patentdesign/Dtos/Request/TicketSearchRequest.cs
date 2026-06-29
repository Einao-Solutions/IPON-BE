using patentdesign.Enums;

namespace patentdesign.Dtos.Request
{
    public record TicketSearchRequest
    {
        public string? ticketNumber { get; set; }
        public string? fileNumber { get; set; }
        public string? requesterId { get; set; }
        public bool isTech { get; set; }
        public TicketCategory? supportRegistryCategory { get; set; }
        public bool isRegistryOfficer { get; set; }
    }
}
