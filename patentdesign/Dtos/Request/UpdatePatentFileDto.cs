using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class UpdatePatentFileDto
    {
        public string FileId { get; set; } = null!;
        public string? FileOrigin { get; set; }
        public string? FilingCountry { get; set; }
        public List<ApplicantInfo>? Applicants { get; set; } 
        public PatentApplicationTypes? PatentApplicationType { get; set; }
        public List<ApplicantInfo>? Inventors { get; set; } 
        public string? CorrespondenceNationality { get; set; }
        public List<PriorityInfo> FirstPriorityInfo { get; set; } = new();
    }
}
