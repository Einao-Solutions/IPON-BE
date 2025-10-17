using patentdesign.Models;

namespace patentdesign.Dtos.Response;

public class DocumentsDto
{
    public string? ApplicationId { get; set; }
    public string? PaymentId { get; set; }
    public List<ApplicationLetters>? Documents { get; set; }

}

public class TrademarkDocsDto
{
    public string? FileId { get; set; }
    public string? Title { get; set; }
    public DateTime? FilingDate { get; set; }
    public ApplicationStatuses FileStatus { get; set; }
}