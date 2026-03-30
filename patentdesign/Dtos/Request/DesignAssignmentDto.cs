using patentdesign.Enums;
using patentdesign.Models;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class DesignAssignmentDto
    {
        public string? FileId { get; set; }
        public string? Rrr { get; set; }
        public DateTime? AssignmentDate { get; set; }
        public DateTime? AssignmentRequestDate { get; set; }

        [JsonPropertyName("assignmentDeed")]
        public List<TT>? DeedOfAssignment { get; set; }

        [JsonPropertyName("DesignAssignmentSupportingDocuments")]
        public List<TT>? SupportingDocuments { get; set; }

        public string? OldAssignorName { get; set; } = string.Empty;
        public string? OldAssignorEmail { get; set; } = string.Empty;
        public string? OldAssignorPhone { get; set; } = string.Empty;
        public string? OldAssignorAddress { get; set; } = string.Empty;
        public string? OldAssignorNationality { get; set; } = string.Empty;
        public string? OldAssignorState { get; set; } = string.Empty;
        public string? OldAssignorCity { get; set; } = string.Empty;
         
        public string? NewAssigneeName { get; set; } = string.Empty;
        public string? NewAssigneeEmail { get; set; } = string.Empty;
        public string? NewAssigneePhone { get; set; } = string.Empty;
        public string? NewAssigneeAddress { get; set; } = string.Empty;
        public string? NewAssigneeNationality { get; set; } = string.Empty;
        public string? NewAssigneeState { get; set; } = string.Empty;
        public string? NewAssigneeCity { get; set; } = string.Empty;
    }

    public class DesignAssignmentDecisionDto
    {
        public string FileId { get; set; }
        public string AppId { get; set; }
        public bool Approve { get; set; }
        public string Reason { get; set; }
        public ApplicantInfo? NewAssignee { get; set; }
    }
}
