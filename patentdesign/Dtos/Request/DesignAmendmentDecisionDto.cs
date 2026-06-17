namespace patentdesign.Dtos.Request
{
    public class DesignAmendmentDecisionDto
    {
        public string FileId { get; set; }
        public string AppId { get; set; }
        public bool Approve { get; set; }
        public string Reason { get; set; }
        public string? UserId { get; set; }
    }
}
