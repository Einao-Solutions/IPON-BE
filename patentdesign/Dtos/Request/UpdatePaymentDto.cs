using patentdesign.Models;

namespace patentdesign.Dtos.Request;

public class UpdatePaymentDto
{
    public string? FileId { get; set; }
    public string? NewPaymentId { get; set; }
    public string? ApplicationId { get; set; }
    public string? User { get; set; }
    public string? OldPaymentId { get; set; } 
    
}