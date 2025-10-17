using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class WithdrawalRequestDto
    {
        public string FileId { get; set; }
        public string PaymentRRR { get; set; }
        public DateTime? WithdrawalDate { get; set; }
        public DateTime? WithdrawalRequestDate { get; set; }
        public List<TT>? WithdrawalLetter { get; set; }
        public List<TT>? WithdrawalSupportingDocuments { get; set; }
    }
}
