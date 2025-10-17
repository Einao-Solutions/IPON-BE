using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public record FileAttachmentDto
    {
        public string FileId { get; set; }
        public FileTypes FileType { get; set; }
        public PatentTypes? PatentType { get; set; }
        public string? TitleOfInvention { get; set; }
        public ApplicationStatuses FileStatus { get; set; }
        public string? FileOrigin { get; set; }
        public ApplicantInfo? Applicant { get; set; }   //only first applicant
        public string? TitleOfDesign { get; set; }
        public DesignTypes? DesignType { get; set; }
        public string? StatementOfNovelty { get; set; }
        public List<AttachmentType> Attachments { get; set; } = new();
    }
}
