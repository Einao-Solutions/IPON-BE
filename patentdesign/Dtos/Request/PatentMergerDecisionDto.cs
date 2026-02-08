using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class PatentMergerDecisionDto
    {
        public string FileId { get; set; }

        public string AppId { get; set; }

        public bool Approve { get; set; }

        public string Reason { get; set; }

        public ApplicantInfo? NewMergedParty { get; set; }
    }
}
