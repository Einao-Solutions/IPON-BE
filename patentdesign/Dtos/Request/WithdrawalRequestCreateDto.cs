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
    /// Withdrawal letter file
    /// </summary>
    public IFormFile? WithdrawalLetter { get; set; }

    /// <summary>
    /// Supporting documents (optional)
    /// </summary>
    public List<IFormFile> SupportingDocuments { get; set; } = new();
}
