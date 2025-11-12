using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class PatentClericalUpdateDto
    {
        public string? UpdateType { get; set; }
        public string? FileId { get; set; }
        public string? PaymentRRR { get; set; }
        public ApplicationStatuses? FileStatus { get; set; }
        public string? Cost { get; set; }
        public string? ServiceFee { get; set; }
        public List<ApplicantInfo> Applicants { get; set; } = new(); // <-- All applicants
        public List<ApplicantInfo> Inventors { get; set; } = new(); // <-- All applicants
        public int? FileClass { get; set; }
        public string? ClassDescription { get; set; }
        public string? FileTitle { get; set; }
        public FileTypes FileType { get; set; }
        public IFormFile? Representation { get; set; }
        public TradeMarkLogo? TrademarkLogo { get; set; }
        public IFormFile? PowerOfAttorney { get; set; }
        public IFormFile? OtherAttachment { get; set; }
        public string? Disclaimer { get; set; }
        public string? CorrespondenceName { get; set; }
        public string? CorrespondenceAddress { get; set; }
        public string? CorrespondenceEmail { get; set; }
        public string? CorrespondencePhone { get; set; }
        public string? CorrespondenceNationality { get; set; }
        public PatentApplicationTypes? PatentApplicationType { get; set; }
        public string? TitleOfInvention { get; set; }
        public string? FileOrigin { get; set; }
        public PatentTypes? PatentType { get; set; }
        public string? PatentAbstract { get; set; }
    }
}
