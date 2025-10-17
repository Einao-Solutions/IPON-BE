namespace patentdesign.Dtos.Request;

public class OppositionRequestDto
{
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? FileTitle { get; set; }
    public string? Name { get; set; }
    public string? Phone {get; set;}
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Nationality { get; set; }
    public string? Reason { get; set; }
    public List<IFormFile>? SupportingDocs { get; set; }
    public DateTime? OppositionDate { get; set; }
    public string? PaymentId { get; set; }
}

public class OppositionSearchDto
{
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? FileTitle { get; set; }
    public string? Cost { get; set; }
    public string? PaymentId { get; set; }
    public string? ServiceFee { get; set; }
    public string? RepresentationUrl { get; set; }
    public string? ApplicantName { get; set; }
    public int? Class { get; set; }
}

public class OppositionStatsDto
{
    public long? AwaitingCounter { get; set; }
    public long? NewOpposition { get; set; }
}