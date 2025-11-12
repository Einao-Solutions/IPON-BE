using MongoDB.Bson.Serialization.Attributes;
using patentdesign.Models;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Response;

public class MarkInfoDto
{
    public string? FileNumber { get; set; }
    public string? Class { get; set; }
    public string? Title { get; set; }
    public string? FilingDate { get; set; }
    public TradeMarkType MarkType { get; set; }
    public TradeMarkLogo? Logo { get; set; }
    public string? Description { get; set; }
    public List<ApplicantInfo>? Applicants { get; set; }
    public List<ApplicationInfo>? ApplicationHistory { get; set; }
    public CorrespondenceType? Correspondence { get; set; }
    public string? Disclaimer { get; set; }
    
    public ApplicationStatuses? FileStatus { get; set; }
}

public class PwalletDto
{
    public string? Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public ApplicationStatuses? FileStatus { get; set; }
    public string? paymentId { get; set; }
    public string? ApplicantId { get; set; }
}

public class ClaimRequestDto
{
    public List<IFormFile>? Attachments { get; set; }
    public List<MarkInfoDto>? MarkInfo { get; set; }
    public string? CorrespondenceName { get; set; }
    public string? CorrespondenceEmail { get; set; }
    public string? CorrespondencePhone { get; set; }
    public string? CorrespondenceNationality { get; set; }
    public string? CorrespondenceAddress { get; set; }

}

public class ClaimDetailsDto
{
    public string? FileNumber { get; set; }
    public int? Class { get; set; }
    public string? Title { get; set; }
    public DateTime? FilingDate { get; set; }
    public ApplicationStatuses? FileStatus { get; set; }
    public string? PaymentId { get; set; }
    public DateTime? RequestDate { get; set; }
    public List<string>? Documents { get; set; }
}
public class MigrateUserDto
{
    [JsonPropertyName("_id")]
    public string? _id { get; set; } 
    public string? uuid { get; set; }
    public string? name { get; set; }
    public string? firstName { get; set; }
    public string? lastName { get; set; }
    public string? password { get; set; }
    public string? email { get; set; }
    public bool? verified { get; set; }
    public string? Signature { get; set; }
    public UserTypes? UserType { get; set; }
    public List<UserRoles>? UserRoles { get; set; }
    public CorrespondenceType? DefaultCorrespondence { get; set; }
}