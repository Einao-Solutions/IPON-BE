namespace patentdesign.Dtos.Request
{
    public class PublicationStatusDecisionDto
    {
       public string FileId { get; set; }
       public bool Approve { get; set; }
       public string Comment { get; set; }
    }
}
