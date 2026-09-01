namespace patentdesign.Dtos.Request;

/// <summary>
/// DTO for creating/submitting a withdrawal request with file attachments
/// </summary>
public class WithdrawalRequestCreateDto
{
    /// <summary>
    /// File ID (e.g., F/TM/O/2016/88119)
    /// </summary>
    public string? FileId { get; set; }

    /// <summary>
    /// Payment ID/Reference from payment gateway
    /// </summary>
    public string? PaymentId { get; set; }

    /// <summary>
    /// File type (optional, can be passed via query string)
    /// Accepts: Patent/patent/0, Design/design/1, TradeMark/Trademark/trademark/trade mark/trade-mark/tm/2
    /// </summary>
    public string? FileType { get; set; }

    /// <summary>
    /// Withdrawal letter file
    /// </summary>
    public IFormFile? WithdrawalLetter { get; set; }

    /// <summary>
    /// Supporting documents (optional)
    /// </summary>
    public List<IFormFile> SupportingDocuments { get; set; } = new();
}
