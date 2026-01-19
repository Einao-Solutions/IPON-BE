using patentdesign.Enums;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class PatentMergerDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? MergerDate { get; set; }
        public DateTime? MergerRequestDate { get; set; }
        public List<TT>? Deedofmerger { get; set; }

        [JsonPropertyName("PatentMergerSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }
    }
}
