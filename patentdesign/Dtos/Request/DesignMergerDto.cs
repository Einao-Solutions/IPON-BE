using patentdesign.Enums;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class DesignMergerDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? MergerDate { get; set; }
        public DateTime? MergerRequestDate { get; set; }
        public List<TT>? Deedofmerger { get; set; }

        [JsonPropertyName("DesignMergerSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }

        public string? OldMergerName { get; set; }
        public string? OldMergerEmail { get; set; }
        public string? OldMergerPhone { get; set; }
        public string? OldMergerAddress { get; set; }
        public string? OldMergerNationality { get; set; }
        public string? OldMergerState { get; set; }
        public string? OldMergerCity { get; set; }

        public string? NewMergerName { get; set; }
        public string? NewMergerEmail { get; set; }
        public string? NewMergerPhone { get; set; }
        public string? NewMergerAddress { get; set; }
        public string? NewMergerNationality { get; set; }
        public string? NewMergerState { get; set; }
        public string? NewMergerCity { get; set; }
        public string? UserId { get; set; } = string.Empty;
    }
}
