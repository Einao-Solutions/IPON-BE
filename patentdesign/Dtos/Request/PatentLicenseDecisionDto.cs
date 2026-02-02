using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class PatentLicenseDecisionDto
    {

        public string? FileId { get; set; }

        public string? AppId { get; set; }

        public bool Approve { get; set; }

        public string? Reason { get; set; }

        public ApplicantInfo? NewLicensee { get; set; }
    }
}
