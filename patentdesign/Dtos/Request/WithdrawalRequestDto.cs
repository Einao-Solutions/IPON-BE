using patentdesign.Enums;

namespace patentdesign.Dtos.Request
{
    public class WithdrawalRequestDto
    {
        public string? FileId { get; set; }
        public string PaymentRRR { get; set; }
        public DateTime? WithdrawalDate { get; set; }
        public DateTime? WithdrawalRequestDate { get; set; }
        public List<TT>? WithdrawalLetter { get; set; }
        public List<TT>? WithdrawalSupportingDocuments { get; set; }
        public string? UserId { get; set; }
        /// <summary>
        /// File type (optional)
        /// Accepts: Patent/patent/0, Design/design/1, TradeMark/Trademark/trademark/trade mark/trade-mark/tm/2
        /// </summary>
        public string? FileType { get; set; }
    }
}
