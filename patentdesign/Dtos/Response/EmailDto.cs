namespace patentdesign.Dtos.Response;

public class OppositionEmailDto
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
    public string SignatoryName {get; set;}
}