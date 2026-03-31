using patentdesign.Enums;

namespace patentdesign.Dtos.Response;

public class EmailDto
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string? Body { get; set; }
    public string? CarbonCopy { get; set; }
    public EmailType EmailType { get; set; }
    public OppositionMail? OppositionMail { get; set; }
    public ResetPasswordMail? ResetPasswordMail { get; set; }
    public StatusUpdateMail? StatusUpdateMail { get; set; }
}

public class BulkEmailDto
{
    public Dictionary<string, string>? Recipients { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; } 
}

public class OppositionMail
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public string ApplicantName { get; set; }
    public string FileNumber { get; set; }
    public string Title { get; set; }
    public string OpposerName { get; set; }
    public string Reason { get; set; }
    public string OppositionDate { get; set; }
    public string? SignatoryName { get; set; }
}
public class ResetPasswordMail
{
    public string UserName { get; set; }
    public string ResetLink { get; set; }
}

public class StatusUpdateMail
{
    public string ApplicationType { get; set; }
    public string FormerStatus { get; set; }
    public string NewStatus { get; set; }
    public DateTime DateTreated { get; set; }
    public string? Remarks { get; set; }
}