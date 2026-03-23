using patentdesign.Enums;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class DesignLicenseDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? LicenseDate { get; set; }
        public DateTime? LicenseRequestDate { get; set; }
        public List<TT>? Deedoflicense { get; set; }

        [JsonPropertyName("DesignLicenseSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }

        public string? OldLicensorName { get; set; } = string.Empty;
        public string? OldLicensorEmail { get; set; } = string.Empty;
        public string? OldLicensorAddress { get; set; } = string.Empty;
        public string? OldLicensorPhone { get; set; } = string.Empty;
        public string? OldLicensorNationality { get; set; } = string.Empty;
        public string? OldLicensorState { get; set; } = string.Empty;
        public string? OldLicensorCity { get; set; } = string.Empty;

        public string? NewLicenseeEmail { get; set; } = string.Empty;
        public string? NewLicenseeAddress { get; set; } = string.Empty;
        public string? NewLicenseePhone { get; set; } = string.Empty;
        public string? NewLicenseeName { get; set; } = string.Empty;
        public string? NewLicenseeNationality { get; set; } = string.Empty;
        public string? NewLicenseeState { get; set; } = string.Empty;
        public string? NewLicenseeCity { get; set; } = string.Empty;
    }
}
