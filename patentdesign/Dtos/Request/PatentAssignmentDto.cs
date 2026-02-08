using patentdesign.Enums;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class PatentAssignmentDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? AssignmentDate { get; set; }
        public DateTime? AssignmentRequestDate { get; set; }
        public List<TT>? AssignmentDeed { get; set; }

        [JsonPropertyName("PatentassignmentSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }

        // Old assignor (current patent holder)
        public string? OldAssignorName { get; set; }
        public string? OldAssignorEmail { get; set; }
        public string? OldAssignorPhone { get; set; }
        public string? OldAssignorAddress { get; set; }
        public string? OldAssignorNationality { get; set; }
        public string? OldAssignorState { get; set; }
        public string? OldAssignorCity { get; set; }

        // New assignee (to become new applicant)
        public string? NewAssigneeName { get; set; }
        public string? NewAssigneeEmail { get; set; }
        public string? NewAssigneePhone { get; set; }
        public string? NewAssigneeAddress { get; set; }
        public string? NewAssigneeNationality { get; set; }
        public string? NewAssigneeState { get; set; }
        public string? NewAssigneeCity { get; set; }
    }
}
