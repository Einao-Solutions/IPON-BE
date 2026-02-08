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

        // Old mortgagor (current patent holder)
        public string? OldMortgageeName { get; set; }
        public string? OldMortgageeEmail { get; set; }
        public string? OldMortgageePhone { get; set; }
        public string? OldMortgageeAddress { get; set; }
        public string? OldMortgageeNationality { get; set; }
        public string? OldMortgageeState { get; set; }
        public string? OldMortgageeCity { get; set; }

        // New mortgagee (to become new applicant)
        public string? NewMortgagorName { get; set; }
        public string? NewMortgagorEmail { get; set; }
        public string? NewMortgagorPhone { get; set; }
        public string? NewMortgagorAddress { get; set; }
        public string? NewMortgagorNationality { get; set; }
        public string? NewMortgagorState { get; set; }
        public string? NewMortgagorCity { get; set; }

    }
}
