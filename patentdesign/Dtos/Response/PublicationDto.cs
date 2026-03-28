using patentdesign.Models;

namespace patentdesign.Dtos.Response
{
    public class PublicationDto
    {
        public string? FileNumber { get; set; }
        public string? Comment { get; set; }
        public string? StaffId { get; set; }
        public string? StaffName { get; set; }
        public List<Opposition>? Opposition { get; set; }
    }
}
