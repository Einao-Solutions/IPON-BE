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