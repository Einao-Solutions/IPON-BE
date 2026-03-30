using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class PatentMortgageDecisionDto
    {
        public string? FileId { get; set; }

        public string? AppId { get; set; }

        public bool Approve { get; set; }

        public string? Reason { get; set; }

        public ApplicantInfo? NewMortgagee { get; set; }
        public string? AppUserId { get; set; }
    }
}
