namespace patentdesign.Dtos.Request
{
    public class DeleteApplicationHistoryDto
    {
        public string FileNumber { get; set; } = null!;
        public string ApplicationId { get; set; } = null!;
        /// <summary>Id of the user performing the delete — used for audit logging.</summary>
        public string? UserId { get; set; }
        /// <summary>Display name of the user performing the delete — used for audit logging.</summary>
        public string? UserName { get; set; }
    }
}
