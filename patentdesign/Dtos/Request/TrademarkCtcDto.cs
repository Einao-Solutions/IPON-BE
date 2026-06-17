namespace patentdesign.Dtos.Request
{
    public class TrademarkCtcDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public List<string>? AttachmentIds { get; set; } // Names of attachments to certify
        public DateTime? CtcRequestDate { get; set; }
        public string? UserId { get; set; }
    }
}
