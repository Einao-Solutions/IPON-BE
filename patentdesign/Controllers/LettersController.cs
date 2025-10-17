using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Models;
using patentdesign.Services;

[ApiController]
[Route("api/letters")]
public class LettersController(LettersServices lettersServices) : ControllerBase
{
    [HttpGet("generate")]
    public async Task<IActionResult> Generate([FromQuery] string? applicationId = null, [FromQuery] string? fileId = null,
        [FromQuery] int? letterType = null, [FromQuery] string? oppositionId = null)
    {
        // Validate parameters
        if (letterType == null || letterType < 0 || letterType >= Enum.GetValues<ApplicationLetters>().Length)
        {
            return BadRequest("Invalid letter type");
        }

        var r = Enum.GetValues<ApplicationLetters>().ToList()[letterType.Value];
        Console.WriteLine("App Letter Type: " + r);
        Console.WriteLine("FileId: " + fileId);
        Console.WriteLine("ApplicationId: " + applicationId);
        Console.WriteLine("OppositionId: " + oppositionId);
        var result = await lettersServices.GenerateLetter(fileId, r, applicationId, oppositionId);
        if (result == null || !result.ContainsKey("data") || result["data"] == null)
        {
            Console.WriteLine("Result: " + JsonSerializer.Serialize(result));
            
            return NotFound("No letter could be generated for the provided parameters.");
        }
        Response.Headers.Add("Content-Disposition", $"inline; filename={result["name"]}");
        Response.Headers.Add("Content-Type", result["type"] as string);
        return File(result["data"] as byte[], result["type"] as string);
    }
    [HttpGet("GetDocuments")]
    public async Task<IActionResult> GetDocuments([FromQuery] string fileId, [FromQuery] string paymentId)
    {
        if (string.IsNullOrEmpty(fileId) && string.IsNullOrEmpty(paymentId))
        {
            return BadRequest("Either applicationId or paymentId must be provided");
        }
        try
        {
            var documents = await lettersServices.DocumentModule(fileId, paymentId);

            if (documents == null)
            {
                return NotFound("No documents found for the provided credentials.");
            }

            return Ok(documents);
        }
        catch (Exception ex)
        {
            // Specific argument-related errors
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [HttpGet("verify-trademark")]
    public async Task<IActionResult> VerifyTmDoc([FromQuery]string fileId)
    {
        var doc = await lettersServices.VerifyTmDoc(fileId);
        if (doc == null) return NotFound("File Not Found");
        return Ok(doc);
    }
}