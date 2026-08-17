using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using patentdesign.Models;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Response;

/// <summary>
/// Response shape used by the SuperAdmin UI to pre-fill recordal forms.
/// The UI reads <c>assignment.*</c> first for assignment entries (applicationType = 5),
/// then falls back to <c>oldValue.*</c> / <c>newValue.*</c>. For recordal types
/// 7 (RegisteredUser), 8 (Merger), 9 (ChangeOfName) and 10 (ChangeOfAddress)
/// the UI reads <c>newValue.*</c>.
/// </summary>
public class ApplicationHistoryResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("applicationType")] public FormApplicationTypes ApplicationType { get; set; }
    [JsonPropertyName("applicationDate")] public DateTime ApplicationDate { get; set; }
    [JsonPropertyName("currentStatus")] public ApplicationStatuses CurrentStatus { get; set; }
    [JsonPropertyName("paymentId")] public string? PaymentId { get; set; }
    [JsonPropertyName("fileNumber")] public string? FileNumber { get; set; }

    [JsonPropertyName("assignment")]
    public AssignmentPayloadDto? Assignment { get; set; }

    [JsonPropertyName("oldValue")] public object? OldValue { get; set; }
    [JsonPropertyName("newValue")] public object? NewValue { get; set; }
}

public class AssignmentPayloadDto
{
    [JsonPropertyName("assignorName")] public string? AssignorName { get; set; }
    [JsonPropertyName("assignorEmail")] public string? AssignorEmail { get; set; }
    [JsonPropertyName("assignorPhone")] public string? AssignorPhone { get; set; }
    [JsonPropertyName("assignorNationality")] public string? AssignorNationality { get; set; }
    [JsonPropertyName("assignorAddress")] public string? AssignorAddress { get; set; }
    [JsonPropertyName("assignorCountry")] public string? AssignorCountry { get; set; }
    [JsonPropertyName("assigneeName")] public string? AssigneeName { get; set; }
    [JsonPropertyName("assigneeEmail")] public string? AssigneeEmail { get; set; }
    [JsonPropertyName("assigneePhone")] public string? AssigneePhone { get; set; }
    [JsonPropertyName("assigneeNationality")] public string? AssigneeNationality { get; set; }
    [JsonPropertyName("assigneeAddress")] public string? AssigneeAddress { get; set; }
    [JsonPropertyName("assigneeCountry")] public string? AssigneeCountry { get; set; }
    [JsonPropertyName("dateOfAssignment")] public DateTime? DateOfAssignment { get; set; }
    [JsonPropertyName("assignmentDeedUrl")] public string? AssignmentDeedUrl { get; set; }
    [JsonPropertyName("authorizationLetterUrl")] public string? AuthorizationLetterUrl { get; set; }
}

public class FileApplicationsDto
{
    public string FileTitle { get; set; } = string.Empty;

    public List<ApplicationInfo> Applications { get; set; } = [];
    public CertificateAppDto CertificateApp { get; set; }
}
public class CertificateAppDto
{
    public string id { get; set; } 
    public FormApplicationTypes ApplicationType { get; set; }
    public ApplicationStatuses CurrentStatus { get; set; }
    public string? PaymentId { get; set; }
    public DateTime ApplicationDate { get; set; }
}

public class StatusChangeDto
{
    public ApplicationStatuses NewStatus { get; set; } 
    public string? UserId { get; set; }
    public string? FileId { get; set; }
    public string? Reason { get; set; }
}
public class ApplicationHistoryDto
{
    public DateTime ApplicationDate { get; set; }
    public FormApplicationTypes ApplicationType { get; set; }
    public ApplicationStatuses CurrentStatus { get; set; }
    public string? UserId { get; set; }
    public string? PaymentId { get; set; }
    public string? CertificatePaymentId { get; set; }
    public string FileNumber { get; set; }
    /// <summary>Previous party/application data (e.g. assignor before assignment).</summary>
    public object? OldValue { get; set; }
    /// <summary>
    /// New party/application data. May include an <c>attachments</c> array whose items carry
    /// <c>{ fileName, contentType, data: "&lt;base64&gt;" }</c>. The service stores the binary
    /// via the attachment pipeline and replaces <c>data</c> with a downloadable <c>url</c>.
    /// </summary>
    public object? NewValue { get; set; }
}

public class UpdateApplicationHistoryDto
{
    public string FileNumber { get; set; }
    public string ApplicationId { get; set; }
    public DateTime? ApplicationDate { get; set; }
    public FormApplicationTypes? ApplicationType { get; set; }
    public ApplicationStatuses? CurrentStatus { get; set; }
    public string? PaymentId { get; set; }
    public string? CertificatePaymentId { get; set; }
}

public class AnnouncementMailDto
{
    public string? Subject { get; set; }
    public string? Message { get; set; }
}

public class SignatoryDto
{
    public string? Name { get; set; }
    public string? Designation { get; set; }
    public IFormFile? Signature { get; set; }
    public List<FormApplicationTypes> ApplicationTypes { get; set; }
}

public class Signatory
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public byte[] Signature { get; set; }
}