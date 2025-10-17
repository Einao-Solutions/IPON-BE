using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class FileUpdateDto
    {
        public string FileId { get; set; } = null!;
        public string? UpdatedBy { get; set; } 
        public DateTime? LastRequestDate { get; set; }
        public string? CreatorAccount { get; set; }
        public ApplicationStatuses? FileStatus { get; set; }
        public FileTypes? Type { get; set; }
        public string? FileOrigin {  get; set; }
        public string? FilingCountry { get; set; }
        public string? TitleOfInvention { get; set; }
        public string? PatentAbstract { get; set; }
        public CorrespondenceType? Correspondence { get; set; }
        public DateTime? LastRequest { get; set; }
        public List<ApplicantInfo>? applicants { get; set; }
        public PatentApplicationTypes? PatentApplicationType { get; set; }
        public List<Revision>? Revisions { get; set; }
        public PatentTypes? PatentType { get; set; }
        public List<ApplicantInfo>? Inventors { get; set; }
        public List<PriorityInfo>? PriorityInfo { get; set; }
        public List<PriorityInfo>? FirstPriorityInfo { get; set; }
        public DesignTypes? DesignType { get; set; }
        public string? TitleOfDesign { get; set; }
        public string? StatementOfNovelty { get; set; }
        public List<ApplicantInfo>? DesignCreators { get; set; }
        public List<AttachmentType>? Attachments { get; set; }
        public Dictionary<string, ApplicationStatuses>? FieldStatus { get; set; }
        public string? TitleOfTradeMark { get; set; }
        public int? TrademarkClass { get; set; }
        public string? TrademarkClassDescription { get; set; }
        public TradeMarkLogo? TrademarkLogo { get; set; }
        public TradeMarkType? TrademarkType { get; set; }
        public string? TrademarkDisclaimer { get; set; }
        public string? RtmNumber { get; set; }
        public string? Comment { get; set; }
        public List<RegisteredUser>? Registered_Users { get; set; }
        public List<RegisteredUser>? RegisteredUsers { get; set; }
        public List<Assignee>? Assignees { get; set; }
        public List<PostRegistrationApp>? PostRegApplications { get; set; }
        public List<ClericalUpdate>? ClericalUpdates { get; set; }
        public string? MigratedPCTNo { get; set; }
    }
}
