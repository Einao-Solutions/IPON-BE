namespace patentdesign.Dtos.Response;

/// <summary>
/// DTO for withdrawal request details response
/// </summary>
public class WithdrawalDetailsDto
{
    /// <summary>
    /// File identifier (e.g., F/TM/O/2016/88119)
    /// </summary>
    public string? FileId { get; set; }

    /// <summary>
    /// Type of the file (Trademark, Design, Patent, etc.)
    /// </summary>
    public string? FileType { get; set; }

    /// <summary>
    /// Date when the withdrawal request was submitted
    /// </summary>
    public DateTime? WithdrawalRequestDate { get; set; }

    /// <summary>
    /// Date when the withdrawal was processed
    /// </summary>
    public DateTime? WithdrawalDate { get; set; }

    /// <summary>
    /// Current application status (RequestWithdrawal, Approved, Rejected, etc.)
    /// </summary>
    public string? ApplicationStatus { get; set; }

    /// <summary>
    /// Payment reference, RRR, or payment ID for the withdrawal request
    /// </summary>
    public string? PaymentId { get; set; }

    /// <summary>
    /// List of withdrawal letter attachments
    /// </summary>
    public List<DocumentAttachmentDto>? WithdrawalLetterAttachments { get; set; } = new();

    /// <summary>
    /// List of supporting document attachments
    /// </summary>
    public List<DocumentAttachmentDto>? SupportingDocumentAttachments { get; set; } = new();
}

/// <summary>
/// DTO for document attachment with name and URL
/// </summary>
public class DocumentAttachmentDto
{
    /// <summary>
    /// Name/identifier of the attachment
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// URL to access the attachment document
    /// </summary>
    public string? Url { get; set; }
}
