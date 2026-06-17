namespace patentdesign.Dtos.Request
{
    public class PatentAmendmentDecisionDto
    {
        public string? fileId { get; set; }
        public string? appId { get; set; }
        public bool approve { get; set; }
        public string? reason { get; set; }
        public string? appUserId { get; set; }
    }
}
