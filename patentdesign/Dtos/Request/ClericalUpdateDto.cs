using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class ClericalUpdateDto
    {
        public string? UpdateType { get; set; }
        public string? FileId { get; set; }
        public string? PaymentRRR { get; set; }
        public ApplicationStatuses? FileStatus { get; set; }
        public string? Cost { get; set; }
        public string? ServiceFee { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantAddress { get; set; }
        public string? ApplicantNationality { get; set; }
        public string? ApplicantEmail { get; set; }
        public string? ApplicantPhone { get; set; }

        public int? FileClass { get; set; }
        public string? ClassDescription { get; set; }
        public string? FileTitle { get; set; }
        public FileTypes FileType { get; set; }
        public IFormFile? Representation { get; set; }
        public TradeMarkLogo? TrademarkLogo { get; set; } 
        public string? WordMark { get; set; } = string.Empty;
        public string? RepresentationUrl { get; set; }
        public string? PowerOfAttorneyUrl { get; set; }
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

        // For AddApplicant case: a list of applicants to add
        public List<ApplicantInfo>? NewApplicants { get; set; }
        // For RemoveApplicants  case
        public List<string>? RemoveApplicantIds { get; set; }
        public List<string>? ApplicantNames { get; set; }
        public List<string>? ApplicantAddresses { get; set; }
        public List<string>? ApplicantNationalities { get; set; }
        public List<string>? ApplicantEmails { get; set; }
        public List<string>? ApplicantPhones { get; set; }
        public List<string>? ApplicantStates { get; set; }
        public List<string>? ApplicantCities { get; set; }
        // Patent Inventors
        public List<ApplicantInfo>? NewInventors { get; set; }
        public List<string>? RemoveInventorIds { get; set; }
        // For Inventors audit/history
        public List<string>? OldInventorNames { get; set; }
        public List<string>? NewInventorNames { get; set; }
        public List<string>? OldInventorPhones { get; set; }
        public List<string>? NewInventorPhones { get; set; }
        public List<string>? OldInventorAddresses { get; set; }
        public List<string>? NewInventorAddresses { get; set; }
        public List<string>? OldInventorEmails { get; set; }
        public List<string>? NewInventorEmails { get; set; }
        public List<string>? OldInventorNationalities { get; set; }
        public List<string>? NewInventorNationalities { get; set; }
        public List<string>? OldInventorStates { get; set; }
        public List<string>? NewInventorStates { get; set; }
        public List<string>? OldInventorCities { get; set; }
        public List<string>? NewInventorCities { get; set; }
    }

    public class ClericalUpdateDetailsDto
    {
        public string? Id { get; set; }
        public string? UpdateType { get; set; }
        public string? PaymentId { get; set; }
        public string? OldValue { get; set; }
        public string? OldValue2 { get; set; }
        public string? OldValue3 { get; set; }
        public string? OldValue4 { get; set; }
        public string? OldRepresentation { get; set; }
        public string? OldPowerOfAttorneyUrl { get; set; }
        public string? NewValue { get; set; }
        public string? NewValue2 { get; set; }
        public string? NewValue3 { get; set; }
        public string? NewValue4 { get; set; }
        public string? NewRepresentation { get; set; }
        public string? NewPowerOfAttorneyUrl { get; set; }
    }   
}
