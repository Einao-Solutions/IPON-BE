using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public record UpdateAttachmentDto
    {
        public List<TT> Attachments { get; set; } = new();
    }
}

public class AdminUploadAttachmentDto
{
    public string FileNumber { get; set; }
    public IFormFile Attachment { get; set; }
    public string AttachmentName { get; set; }
}