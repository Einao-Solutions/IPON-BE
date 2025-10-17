using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using patentdesign.Dtos.Response;
using patentdesign.Services;

namespace patentdesign.Controllers;

[ApiController]
[Route("api/migration")]
public class MigrationController(MigrationService migrationService) : ControllerBase
{
    [HttpGet("GetMarkInfo")]
    public async Task<IActionResult> GetMarkInfo(string regNumber)
    {
        try
        {
            var result = await migrationService.GetFileByRegNumber(regNumber);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    [HttpGet("GetPaymentInfo")]
    public async Task<IActionResult> GetPaymentInfo(string paymentId)
    {
        try
        {
            var pwallet =  await migrationService.GetPayment(paymentId);
            return Ok(pwallet);
        }
        catch (Exception e)
        {
            return BadRequest(new{message = e.Message});
        }
    }
    [HttpPost("ClaimRequest")]
    public async Task<IActionResult> ClaimRequest([FromForm] List<IFormFile> attachments, [FromForm] string markInfo)
    {
        var markData = JsonConvert.DeserializeObject<List<MarkInfoDto>>(markInfo);
        var dto = new ClaimRequestDto
        {
            Attachments = attachments,
            MarkInfo = markData
        };

        await migrationService.NewClaimRequest(dto);
        return Ok(new { message = "Claim submitted successfully" });
    }
    [HttpGet("GetAllClaimRequests")]
    public async Task<IActionResult> GetAllClaimRequests()
    {
        var res = await migrationService.GetAllClaimRequests();
        return Ok(res);
    }
    [HttpGet("GetClaimRequest")]
    public async Task<IActionResult> GetClaimRequest([FromQuery]string fileId)
    {
        var res = await migrationService.GetClaimRequest(fileId);
        return Ok(res);
    }
    [HttpPost("AdminUploadAttach")]
    public async Task<IActionResult> AdminUploadAttach(AdminUploadAttachmentDto req)
    {
        try
        {
            var result = await migrationService.AdminUploadAttach(req);
            return Ok(new { success = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("migrate")]
    public async Task<IActionResult> MigrateFile([FromQuery] string fileId)
    {
        try
        {
            bool result = await migrationService.MigrateFile(fileId);
            return Ok(new { success = result });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(new { message = e.Message });
        }
    }
}