using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using patentdesign.Models;

namespace patentdesign.Dtos.Response;

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