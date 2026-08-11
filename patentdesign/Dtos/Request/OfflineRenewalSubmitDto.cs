using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class OfflineRenewalSubmitDto
    {
        public string? FileId { get; set; } = string.Empty;
        public string? UserId { get; set; } = string.Empty;
        public int? RenewalYear { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string? PaymentId { get; set; } = string.Empty;
        public List<TT>? RenewalReceiptAttachments { get; set; }
        public List<TT>? RenewalCertificateAttachments { get; set; }
    }
}
