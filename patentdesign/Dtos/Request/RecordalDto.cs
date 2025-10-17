using patentdesign.Models;

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
        public string? FileId { get; set; }
        public string? FileType { get; set; }
        public string? DataChangeType { get; set; }
        public string? rrr { get; set; }
        public string? Amount { get; set; }
        public string? ServiceFee { get; set; }
        public string? RtmNumber { get; set; }

    }
    public class MergerApplicationDto
    {
        public string? FileId { get; set; }
        public string? rrr { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? MergerDate { get; set; }
        public string? Nationality { get; set; }
        public string? Address { get; set; }
        public IFormFile? document { get; set; }
        public AttachmentInfo? documentInfo { get; set; }
        public string? documentUrl { get; set; }
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
        public string? NewName { get; set; }
        public string? ChangeType { get; set; }
        public string? NewAddress { get; set; }
        public IFormFile? document { get; set; }
        public AttachmentInfo? documentInfo { get; set; }
        public string? documentUrl { get; set; }
    }
    public class TreatRecordalDto
    {
        public string fileId { get; set; }
        public string appId { get; set; }
        public string reason { get; set; }
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
    }
    public class  AssignmentAppDto
    {
        public string? FileId { get; set; }
        public string? rrr { get; set; }
        public IFormFile? AssignmentDeed { get; set; }
        public IFormFile? AuthorizationLetter { get; set; }
        public string? AssignmentDeedUrl { get; set; }
        public string? AuthorizationLetterUrl { get; set; }
        public string? AssigneeName { get; set; }
        public string? AssigneePhone { get; set; }
        public string? AssigneeEmail { get; set; }
        public string? AssigneeAddress { get; set; }
        public string? AssigneeNationality { get; set; }
    }
}
