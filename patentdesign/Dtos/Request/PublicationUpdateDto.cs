using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class PublicationUpdateDto
    {
        public string? FileId { get; set; }
        public DateTime? PublicationDate { get; set; }
        public List<TT>? AttachmentFiles { get; set; } // List of URLs
        public string PaymentRRR { get; set; }

    }
}
