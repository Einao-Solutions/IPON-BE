using Microsoft.AspNetCore.Mvc;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers;


[ApiController]
[Route("api/assignment")]
public class AssignmentController(AssignmentService assignmentService) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult> Generate([FromBody] AssignmentTypeReq assignmentData)
    {
        var data = await assignmentService.Generate(
            new AssignmentType()
            {
                assignorName = assignmentData.assignorName,
                assigneeName = assignmentData.assigneeName,
                assigneeAddress = assignmentData.assigneeAddress,
                assignorAddress = assignmentData.assignorAddress,
                assignorCountry = assignmentData.assignorCountry,
                assigneeCountry = assignmentData.assigneeCountry,
                authorizationLetterUrl = assignmentData.authorizationLetterUrl,
                deedOfAgreementUrl = assignmentData.deedOfAgreementUrl,
                dateOfAssignment = assignmentData.dateOfAssignment
            }, assignmentData.fileId, assignmentData.type, assignmentData.creatorAccount, assignmentData.userName,
            assignmentData.applicantName, assignmentData.applicantEmail, assignmentData.applicantNumber);
        return Ok(data);
    }

    [HttpPost("SearchForFile")]
    public async Task<ActionResult<List<TicketSummary>?>> SearchForFile([FromBody] AssReq data)
    {
        var res = await assignmentService.SearchForFile(data.fileNumber, data.userId);
        if (res != null)
            return Ok(res);
        else return NotFound("Invalid file Number");
    }

    [HttpPost("UpdateAssignment")]
    public async Task<ActionResult<Filling?>> UpdateAssignment([FromBody] AssUpdateReq data)
    {
        var res = await assignmentService.UpdateAssignmentStatus(data);
        if (res != null)
            return Ok(res);
        else return NotFound("omo....");
    }

    [HttpPost("PayAssignment")]
    public async Task<ActionResult<dynamic?>> PayAssignment([FromBody] AssUpdateReq data)
    {
        var res = await assignmentService.PayAssignment(data);
        if (res != null)
            return Ok(res);
        else return NotFound("omo....");
    }

    [HttpGet("ValidationRRR")]
    public async Task<ActionResult<dynamic?>> PayAssignment([FromQuery] string data)
    {
        var res = await assignmentService.ValidationRRR(data);
        if (res.Item2 != null)
            return Ok(new { cost = res.Item2?.ToString() });
        else return NotFound("omo....");
    }
}