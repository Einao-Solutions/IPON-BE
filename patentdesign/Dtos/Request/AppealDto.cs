using patentdesign.Models;

namespace patentdesign.Dtos.Request;

public class AppealDto
{
    public string FileNumber { get; set; }
    public List<IFormFile> Docs { get; set; }
}

public class TreatAppealDto
{
    public string? FileNumber { get; set; }
    public string? ApplicationId { get; set; }
    public string? Reason { get; set; }
    public bool IsApproved { get; set; } = false;
}