namespace patentdesign.Dtos.Request
{
    public class OfflineRenewalDecisionDto
    {
        public string? RequestId { get; set; } = string.Empty;
        public bool? Approve { get; set; }
        public string? Reason { get; set; } = string.Empty;
        public string?   UserId { get; set; } = string.Empty;
    }
}
