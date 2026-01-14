using patentdesign.Enums;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class PatentLicenseDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? LicenseDate { get; set; }
        public DateTime? LicenseRequestDate { get; set; }
        public List<TT>? Deedoflicense { get; set; }

        [JsonPropertyName("PatentLicenseSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }

    }
}
