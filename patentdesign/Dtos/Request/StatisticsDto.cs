using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class PerformanceDto
    {
        public string? ApplicationId { get; set; }
        public FormApplicationTypes? ApplicationType { get; set; }
        public ApplicationStatuses? BeforeStatus { get; set; }
        public ApplicationStatuses? AfterStatus { get; set; }
        public string? Reason { get; set; }
        public DateTime? Date { get; set; }
        public string? AppUserId { get; set; }
        public Roles? OfficeUnit { get; set; }
        public string? FileNumber { get; set; }
        public FileTypes? FileType { get; set; }
    }
}
