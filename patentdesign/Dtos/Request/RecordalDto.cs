using patentdesign.Enums;
using patentdesign.Models;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class RecordalDto
    {
        public string? FileTitle { get; set; }
        public int? TrademarkClass { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantEmail { get; set; }
        public string? ApplicantPhone { get; set; }
        public string? ApplicantNationality { get; set; }
        public string? ApplicantAddress { get; set; }
        public string? ApplicantState { get; set; }
        public string? ApplicantCity { get; set; }
        public string? FileId { get; set; }
        public string? FileType { get; set; }
        public string? FileOrigin { get; set; }
        public PatentTypes? PatentType { get; set; }
        public PatentApplicationTypes? PatentApplicationType { get; set; }
        public DesignTypes? DesignType { get; set; }
        public string? DesignTypeDescription { get; set; }
        public string? TitleOfInvention { get; set; }
        public string? TitleOfDesign { get; set; }
        public List<ApplicantInfo> DesignCreators { get; set; } = new();
        public string? StatementOfNovelty { get; set; }
        public string? DataChangeType { get; set; }
        public string? rrr { get; set; }
        public string? Amount { get; set; }
        public string? ServiceFee { get; set; }
        public string? RtmNumber { get; set; }

        // patent cost re‑use / guard fields
        public bool HasExistingApplication { get; set; }
        public string? ExistingApplicationId { get; set; }
        public string? ExistingRRR { get; set; }

        // NEW: for CTC – all file attachments
        public List<AttachmentType>? Attachments { get; set; }
        public List<ApplicantInfo>? Applicants { get; set; }
        public List<ApplicantInfo>? Inventors { get; set; }
        public List<PriorityInfo>? PriorityInfo { get; set; }
        public List<PriorityInfo>? FirstPriorityInfo { get; set; }
        public string? PatentAbstract { get; set; }
        public CorrespondenceType? Correspondence { get; set; }
    }
    public class MergerApplicationDto
    {
        public string? FileId { get; set; }
        public string? rrr { get; set; }
        public string? OldName { get; set; }
        public string? Name { get; set; }
        public string? OldEmail { get; set; }
        public string? Email { get; set; }
        public string? OldPhone { get; set; }
        public string? Phone { get; set; }
        public string? MergerDate { get; set; }
        public string? OldNationality { get; set; }
        public string? Nationality { get; set; }
        public string? OldAddress { get; set; }
        public string? Address { get; set; }
        public string? FileOrigin { get; set; }
        public DesignTypes? DesignType { get; set; }
        public string? DesignTypeDescription { get; set; }
        public string? TitleOfDesign { get; set; }
        public List<TT>? DeedOfMerger { get; set; }

        [JsonPropertyName("DesignMergerSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }

        public IFormFile? document { get; set; }
        public AttachmentInfo? documentInfo { get; set; }
        public string? documentUrl { get; set; }
        public string? userId { get; set; }
    }
    public class DesignMergerDecisionDto
    {
        public string FileId { get; set; }
        public string AppId { get; set; }
        public bool Approve { get; set; }
        public string Reason { get; set; }
        public ApplicantInfo? MergedEntity { get; set; }
        public string? UserId { get; set; }
    }
    public class RegisteredUserDto
    {
        public string? FileId { get; set; }
        public string? rrr { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Nationality { get; set; }
        public string? Address { get; set; }
        public IFormFile? document { get; set; }
        public AttachmentInfo? documentInfo { get; set; }
        public string? documentUrl { get; set; }
    }
    public class ChangeDataRecordalDto
    {
        public string? FileId { get; set; }
        public string? rrr { get; set; }
        public string? OldName { get; set; }
        public string? NewName { get; set; }
        public string? ChangeType { get; set; }
        public string? OldAddress { get; set; }
        public string? NewAddress { get; set; }
        public int? OldClass { get; set; }
        public string? OldClassDescription { get; set; }
        public int? NewClass { get; set; }
        public string? NewClassDescription { get; set; }
        public IFormFile? document { get; set; }
        public AttachmentInfo? documentInfo { get; set; }
        public string? documentUrl { get; set; }
        public string? userId { get; set; } 
    }
    public class TreatRecordalDto
    {
        public string fileId { get; set; }
        public string appId { get; set; }
        public string reason { get; set; }
        public string userId { get; set; }
    }                       
    public class RenewalAppDto
    {
        public string? Cost { get; set; }
        public string? rrr { get; set; }
        public string? FileId { get; set; }
        public bool? IsLateRenewal { get; set; }
        public string? LateRenewalCost { get; set; }
        public string? ServiceFee { get; set; }
        public int? MissedYearsCount { get; set; }
        public int? LateYearsCount { get; set; }
        public FileTypes? FileTypes { get; set; }
        public string? ApplicantName { get; set; }
    }
    public class  AssignmentAppDto
    {
        public string? FileId { get; set; }
        public string? rrr { get; set; }
        public IFormFile? AssignmentDeed { get; set; }
        public IFormFile? AuthorizationLetter { get; set; }
        public string? AssignmentDeedUrl { get; set; }
        public string? AuthorizationLetterUrl { get; set; }
        public string? AssignorName { get; set; }
        public string? AssigneeName { get; set; }
        public string? AssignorPhone { get; set; }
        public string? AssigneePhone { get; set; }
        public string? AssignorEmail { get; set; }
        public string? AssigneeEmail { get; set; }
        public string? AssignorAddress { get; set; }
        public string? AssigneeAddress { get; set; }
        public string? AssignorNationality { get; set; }
        public string? AssigneeNationality { get; set; }
        public string? userId { get; set; }
    }

    
}
