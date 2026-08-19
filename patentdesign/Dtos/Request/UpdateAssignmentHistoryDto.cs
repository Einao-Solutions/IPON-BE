namespace patentdesign.Dtos.Request;

// Payload sent by the SuperAdmin "Update File Information" page when an admin edits
// the Assignor/Assignee details on an existing Assignment (applicationType 5) history entry.
public class UpdateAssignmentHistoryDto
{
    // Identifies the file and the specific application history entry to update.
    public string FileNumber { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;

    // Assignor (editable) — the party transferring the file.
    public string? AssignorName { get; set; }
    public string? AssignorEmail { get; set; }
    public string? AssignorPhone { get; set; }
    public string? AssignorNationality { get; set; }
    public string? AssignorAddress { get; set; }
    public string? AssignorCountry { get; set; }

    // Assignee (editable) — the party receiving the file.
    public string? AssigneeName { get; set; }
    public string? AssigneeEmail { get; set; }
    public string? AssigneePhone { get; set; }
    public string? AssigneeNationality { get; set; }
    public string? AssigneeAddress { get; set; }
    public string? AssigneeCountry { get; set; }

    public string? DateOfAssignment { get; set; }
}
