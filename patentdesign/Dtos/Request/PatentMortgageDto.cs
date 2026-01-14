using patentdesign.Enums;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class PatentMortgageDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? MortgageDate { get; set; }
        public DateTime? MortgageRequestDate { get; set; }
        public List<TT>? Deedofmortgage { get; set; }

        [JsonPropertyName("PatentMortgageSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }
     
    }
}
