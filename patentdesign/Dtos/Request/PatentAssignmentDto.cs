using patentdesign.Enums;

namespace patentdesign.Dtos.Request
{
    public class PatentAssignmentDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? AssignmentDate { get; set; }
        public DateTime? AssignmentRequestDate { get; set; }
        public List<TT>? AssignmentDeed { get; set; }
        public List<TT>? SupportingDocuments { get; set; }
    }
}
