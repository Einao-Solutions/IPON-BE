using Azure.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using System.Text.Json;
namespace patentdesign.Controllers;

//[Authorize]
[ApiController]
[Route("api/files")]
public class FilesController(FilesServices fileService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SaveFile(Filling newFile)
    {
        await fileService.CreateFileAsync(newFile);
        return CreatedAtAction(nameof(GetFile), new { id = newFile.Id }, newFile);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Filling?>> GetFile(string id)
    {
        return await fileService.GetFileAsync(id);
    }
    [HttpDelete("{id}")]

    public async Task<IActionResult> Delete(string id)
    {
        var status = await fileService.DeleteFileAsync(id);
        if (status)
        {
            return NoContent();
        }

        return NotFound();
    }

    [HttpPost("summary")]
    public async Task<ActionResult> GetSummary([FromQuery] int index,
        [FromQuery] int quantity, [FromBody] SummaryRequestObj request)
    {
        var result = await fileService.GetPaginatedSummaryAsync(index, quantity, request);
        return Ok(result);
    }

    [HttpPost("ValidatePayment")]
    public async Task ValidateAllPayment()
    {
        await fileService.ValidatePayment();
    }


    [HttpGet("CertificatePayment")]
    public async Task<ActionResult> LoadCertificatePaymentDetails([FromQuery] string id, [FromQuery] string userId)
    {
        var result = await fileService.GetCertificatePaymentCost(id, userId);
        return Ok(result);
    }

    [HttpPost("CertificateValidate")]
    public async Task<ActionResult> CertificateValidate([FromQuery] string fileId, [FromQuery] string rrr, [FromQuery] string userId, [FromQuery] string userName)
    {
        var result = await fileService.ValidateCertificatePayment(fileId, rrr, userName, userId);
        return Ok(result);
    }

    [HttpPost("updateee")]
    public async Task TestCCC()
    {
        await fileService.updateApproved();
    }

    [HttpPost("ReIssueReceiptAndAck")]
    public async Task ReIssueReceiptAndAck()
    {
        await fileService.ReIssueReceiptAndAck();
    }

    [HttpPost("uploadAttachment")]
    public async Task<ActionResult> UploadDocumentAttachment([FromBody] List<TT> attachments)
    {
        var result = await fileService.UploadAttachment(attachments);
        return Ok(result);
    }

    [HttpPost("SaveDataUpdate")]
    public async Task<ActionResult> SaveDataUpdate([FromBody] DataUpdateReq data)
    {
        var res = await fileService.SaveDateUpdateApplication(data);
        return Ok(res);
    }

    [HttpGet("Throw")]
    public IActionResult Throw() =>
        throw new Exception("Sample exception.");


    [HttpPost("createNew")]
    public async Task<ActionResult> CreateNewFiling([FromBody] NewCreation1 test)
    {
        var filer = JsonSerializer.Deserialize<Filling>(test.file,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        await fileService.ProcessNewCreation(filer, test.attachments);
        return CreatedAtAction(nameof(GetFile), new { id = filer.Id }, filer);
        // return Ok(test);
    }

    [HttpPost("replaceLetters")]
    public async Task replaceLetters()
    {
        await fileService.replaceLetters();
    }

    [HttpPost("DesignCerts")]
    public async Task GenerateDesignCerts()
    {
        await fileService.GenerateDesignCerts();
    }

    //[HttpPost("NewApplicationPayment")]
    //public async Task<ActionResult> NewApplicationPayment([FromBody] UpdateDataType data)
    //{
    //    var response = await fileService.NewApplicationPayment(data);
    //    return Ok(response);
    //}

    [HttpGet("GetRRRCost")]
    public async Task<ActionResult<dynamic>> GetCostFromRRR([FromQuery] string rrr)
    {
        var res = await fileService.GetNewAppCostFromRemita(rrr);
        return Ok(res);
    }


    //[HttpGet("PaidButNotReflecting")]
    //public async Task<ActionResult<dynamic>> PaidButNotReflecting()
    //{
    //    await fileService.PaidButNotReflecting();
    //    return Ok("res");
    //}

    [HttpGet("DesignPDf")]
    public async Task<ActionResult<dynamic>> NewDesignPDF()
    {
        await fileService.NewDesignPDF();
        return Ok("res");
    }

    [HttpPost("DeletePendings")]
    public async Task<ActionResult<dynamic>> DeletePendings()
    {
        await fileService.DeletePending();
        return Ok("res");
    }



    [HttpPost("updatecost")]
    public async Task<ActionResult<dynamic>> Updatecost([FromBody] UpdateReq req)
    {
        var res = await fileService.UpdateCost(req);
        return Ok(res);
    }

    [HttpGet("GetAttachment")]
    public async Task<IActionResult> GetAttachment([FromQuery] string fileId)
    {
        var attachmentInfo = await fileService.GetAttachment(fileId);
        Response.Headers.Add("Content-Disposition", $"inline; filename={attachmentInfo.Value.Item3}");
        Response.Headers.Add("Content-Type", attachmentInfo.Value.Item2);
        return File(attachmentInfo.Value.Item1, attachmentInfo.Value.Item2);
    }

    
    [HttpGet("GenerateOppositionRRR")]
    public async Task<IActionResult> GenerateOppositionRRR([FromQuery] string description, [FromQuery] string name,
        [FromQuery] string email, [FromQuery] string number)
    {
        var data = await fileService.GenerateOppositionRRR(PaymentTypes.OppositionCreation, description, name, email,
            number);
        return Ok(new { rrr = data.Item1, cost = data.Item2 });
    }

    [HttpPost("ManualUpdate")]
    public async Task<ActionResult> ManualPaymentUpdate([FromQuery] string fileId, [FromQuery] string applicationId,
            [FromQuery] string? userId, [FromQuery] string? userName, [FromQuery] bool isCertificate)
    {
        var result = await fileService.ManualUpdate(fileId, applicationId, userName, userId, isCertificate);
        return Ok(result);
    }


    [HttpPost("BulkAdd")]
    public async Task BulkAddition([FromBody] List<Filling> files)
    {
        Console.WriteLine(JsonSerializer.Serialize(files));
        await fileService.BulkAddition(files);
    }

    [HttpPost("RevisionCost")]
    public async Task<IActionResult> GetRevisionAmount([FromBody] GetRevisionCost data)
    {
        var res = await fileService.GetRevisioncost(data);
        return Ok(res);
    }

    [HttpGet("RenewalCost")]
    public async Task<IActionResult> GetRenewalCostRRR([FromQuery] string fileNumber, string userId, FileTypes fileType)
    {
        var res = await fileService.GetRenewalCost(fileNumber, userId, fileType);
        return Ok(res);
    }

    [HttpPost("freeupdates")]
    public async Task<ActionResult<Filling>> FreeDataUpdateAsync([FromBody] DataUpdateReq revision)
    {
        var result = await fileService.FreeDataUpdateAsync(revision);
        return result;
    }

    [HttpGet("FileStatistics")]

    public async Task<IActionResult> FileStats(string? userId)
    {
        var stats = await fileService.FileStats(userId);
        return Ok(stats);
    }

    [HttpGet("UserNotifications")]

    public async Task<IActionResult> UserNotifications([FromQuery] string? userId, [FromQuery] bool? staffTickets, [FromQuery] bool? showAllOpposition)
    {
        var stats = fileService.UserNotifications(userId, staffTickets, showAllOpposition);
        return Ok(stats);
    }


    [HttpPost("ManualPaymentUpdate")]
    public async Task<IActionResult> ManualPaymentUpdate([FromBody] ManualPaymentConfirmation data)
    {
        var stats = await fileService.UpdateToAwaitingSearch(data);
        return Ok(stats);
    }

    [HttpPost("AdminUpdateApplication")]
    public async Task<IActionResult> AdminUpdateApplication([FromForm] AdminUpdateReq req)
    {
        var stats = await fileService.AdminUpdateAsync(req);
        return Ok(stats);
    }

    [HttpPost("updatemanystatus")]
    public async Task<IActionResult> Updatemanystatus([FromBody] UpdateMany req)
    {
        var stats = await fileService.Updatemanystatus(req);
        if (stats)
            return Ok(stats);
        return BadRequest("BURST ");
    }

    //[HttpPost("UpdateCorThis")]
    //public async Task<IActionResult> UpdateCorThis([FromQuery] string id, [FromQuery] string userId)
    //{
    //    var stats = await fileService.UpdateCorThis(id, userId);
    //    if (stats != null)
    //        return Ok(stats);
    //    else return BadRequest();
    //}

    //[HttpPost("UpdateCorAll")]
    //public async Task<IActionResult> UpdateCorAll([FromQuery] string id, [FromQuery] string userId, [FromQuery] string creatorAccount)
    //{
    //    var stats = await fileService.UpdateCorAll(id, userId, creatorAccount);
    //    if (stats != null)
    //        return Ok(stats);
    //    else return BadRequest();
    //}

    [HttpPost("DownloadAllPayments")]
    public async Task DownloadAllPayments()
    {
        await fileService.DownloadAllPayments();
    }

    [HttpPost("UpdateApplicationStatus")]
    public async Task<ActionResult<Filling>> UpdateApplicationStatus([FromBody] UpdateDataType data)
    {
        var stats = await fileService.UpdateApplicationStatus(data);
        return stats;
    }

    [HttpPost("CreateFileRenewal")]
    public async Task<ActionResult<Filling>> CreateFileRenewal([FromBody] UpdateDataType data)
    {
        var stats = await fileService.CreateFileRenewal(data);
        return stats;
    }

    //[HttpPost("batchRenewalInfo")]
    //public async Task<ActionResult<BatchRenewRes>> BatchRenewalInfo([FromBody] BatchRenewReq data)
    //{
    //    var stats = await fileService.GetBatchRenewalInfo(data);
    //    return stats;
    //}

    [HttpGet("search")]
    public async Task<ActionResult<SearchRes?>> SearchUserFile([FromQuery] string userId, [FromQuery] string fileNumber)
    {
        var stats = await fileService.SearchForFile(userId, fileNumber);
        if (stats != null)
        {
            return stats;
        }

        return new SearchRes()
        {
            Id = null,
            FileStatus = null
        };
    }

    [HttpGet("DashboardRenewal")]
    public async Task<ActionResult> DashboardRenew([FromQuery] string fileId, [FromQuery] string userId, [FromQuery] string userName)
    {
        var response = await fileService.DashboardRenew(fileId, userName, userId);
        return Ok(response);
    }

    [HttpGet("searchForRenewal")]
    public async Task<ActionResult<dynamic>?> SearchForRenewal([FromQuery] string? userId, [FromQuery] string fileNumber)
    {
        var stats = await fileService.SearchForRenewal(userId, fileNumber);
        Console.WriteLine(JsonSerializer.Serialize(stats));
        return Ok(stats);
    }

    [HttpPost("GetListOfIds")]
    public async Task<ActionResult> GetListOfIds([FromQuery] int index, [FromBody] SummaryRequestObj request)
    {
        var res = await fileService.LoadListOfIds(index, request);
        return Ok(res);
    }

    [HttpGet("UserTicketTiles")]
    public async Task<ActionResult> UserTicketTiles([FromQuery] string userId, [FromQuery] string userTypes)
    {
        var result = await fileService.GetUserTicketFiles(userId, userTypes);
        return Ok(result);
    }

    [HttpPost("ReAssign")]
    public async Task<IActionResult> ReAssign([FromBody] ReAssignType data)
    {
        var result = await fileService.ReAssign(data);
        if (result.ok)
        {
            return Ok(result.file);
        }

        if (result.isValidationError)
        {
            return BadRequest(new { message = result.error ?? "Invalid request." });
        }

        if (result.file == null && result.error != null && result.error.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(new { message = result.error });
        }

        return StatusCode(500, new { message = result.error ?? "An unexpected error occurred." });
    }

    [HttpPost("DeletePending")]
    public async Task DeletePending()
    {
        await fileService.DeletePending();
    }

    [HttpGet("getApplicationData")]
    public async Task<IActionResult> GetApplicationData([FromQuery] string fileId, [FromQuery] string applicationId, [FromQuery] string? requestType = "")
    {
        var result = await fileService.GetApplicationData(fileId, applicationId, requestType);
        if (result != null)
        {
            return Ok(result);
        }
        else
        {
            return BadRequest();
        }
    }
    [HttpPost("updateJsonData")]
    public async Task<IActionResult> UpdateJsonData([FromQuery] string fileId, [FromQuery] string applicationId, [FromBody] object data, [FromQuery] string? requestType = "")
    {
        var result = await fileService.UpdateJsonData(fileId, applicationId, requestType, data);
        if (result != null)
        {
            return Ok(result);
        }
        else
        {
            return BadRequest();
        }
    }

    [HttpGet("GetStatusRequests")]
    public async Task<IActionResult> GetUserStatusRequests([FromQuery] string? userId = null)
    {
        if (userId == "null")
        {
            userId = null;
        }
        var result = await fileService.GetUserStatusRequests(userId);
        if (result == null)
        {
            return BadRequest("NOT FOUND");
        }
        return Ok(result);
    }

    [HttpPost("newStatusRequest")]
    public async Task<IActionResult> NewStatusRequest([FromQuery] string fileNumber, [FromQuery] string userId, [FromBody] Dictionary<string, object>? data)
    {

        var result = await fileService.StatusCheck(fileNumber, userId, data);
        if (result == null)
        {
            return BadRequest("NOT FOUND");
        }
        return Ok(result);
    }

    [HttpGet("GetStatusFromRequest")]
    public async Task<IActionResult> GetStatusFromFile([FromQuery] string requestId, [FromQuery] string userId, [FromQuery] bool IsAdmin)
    {
        var result = await fileService.GetStatusFromRequestId(requestId, userId, IsAdmin);
        if (result == null) return BadRequest();
        Response.Headers.Add("Content-Disposition", $"inline; filename={result["name"]}");
        Response.Headers.Add("Content-Type", result["type"] as string);
        return File(result["data"] as byte[], result["type"] as string);
    }
    [HttpPost("UpdateStatusRequest")]
    public async Task<IActionResult> UpdateStatusRequest([FromQuery] string requestId, [FromQuery] bool? simulate = false)
    {
        var result = await fileService.updateStatusRequest(requestId, simulate);
        if (result == null)
        {
            return BadRequest("NOT FOUND");
        }
        return Ok(result);
    }
    
    [HttpGet("GetAvailabilitySearch")]
    public async Task<IActionResult> GetMarkAvailability([FromQuery] string title, [FromQuery] int? classNo, [FromQuery] string type)
    {

        var result = await fileService.GetRelatedTitles(title, classNo, type);
        if (result == null)
        {
            return BadRequest("NOT FOUND");
        }
        return Ok(result);
    }
    [HttpGet("AvailabilitySearchCost")]
    public async Task<IActionResult> AvailabilitySearchCost([FromQuery] string name, [FromQuery] string email)
    {
        var res = await fileService.AvailabilitySearchCost(name, email);
        if (res == null)
        {
            return BadRequest("NOT FOUND");
        }
        return Ok(res);
    }

    [HttpGet("GetStatusSearchCost")]
    public async Task<IActionResult> GetStatusSearchCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        var res = await fileService.StatusSearchCost(fileId, fileType);

        if (res == null)
        {
            return NoContent();
        }

        return Ok(res);
    }

    [HttpGet("GetPublicationStatusUpdateCost")]
    public async Task<IActionResult> GetPublicationStatusUpdateCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        var decodedFileId = Uri.UnescapeDataString(fileId);
        var res = await fileService.GetPublicationStatusUpdateCost(decodedFileId, fileType);

        if (res == null)
        {
            return NoContent();
        }

        return Ok(res);
    }

    [HttpGet("GetFileWithdrawalCost")]
    public async Task<IActionResult> GetFileWithdrawalCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        var decodedFileId = Uri.UnescapeDataString(fileId);
        var res = await fileService.GetFileWithdrawalCost(decodedFileId, fileType);

        if (res == null)
        {
            return NoContent();
        }

        return Ok(res);
    }

    [HttpGet("GetPatentClericalUpdateCost")]
    public async Task<IActionResult> GetPatentClericalUpdateCost(
    [FromQuery] string fileId,
    [FromQuery] FileTypes fileType,
    [FromQuery] string? updateType)
    {
        var res = await fileService.GetPatentClericalUpdateCost(fileId, fileType, updateType);

        if (res == null)
        {
            return NoContent();
        }

        return Ok(res);
    }

    [HttpGet("GetNonConventionalCost")]
    public async Task<IActionResult> GetNonConventionalCost([FromQuery] string? fileId, [FromQuery] FileTypes fileType)
    {
        var res = await fileService.NonConventionalCost(fileId, fileType);

        if (res == null)
        {
            return NoContent();
        }

        return Ok(res);
    }

    [HttpPost("AddRegisteredUsers")]
    public async Task<IActionResult> AddRegisteredUser([FromForm] RegisteredUserDto regUser)
    {
        var result = await fileService.AddRegisteredUser(regUser);
        if (result)
        {
            return Ok("Registered User Added Successfully");
        }
        else
        {
            return BadRequest();
        }
    }

    [HttpGet("GetMergerCost")]
    public async Task<IActionResult> GetMergerCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        var res = await fileService.MergerCost(fileId, fileType);

        if (res == null)
        {
            return NoContent();
        }

        return Ok(res);
    }

    [HttpPost("MergerApplication")]
    public async Task<IActionResult> MergerApplication([FromForm] MergerApplicationDto data)
    {
        var res = await fileService.NewMergerApplication(data);
        if (res == false)
        {
            Console.WriteLine("Failed to submit");
            return NotFound();
        }
        return Ok(res);
    }
    [HttpPost("ApproveMerger")]
    public async Task<IActionResult> ApproveMerger([FromBody] TreatRecordalDto recordalApp)
    {
        var res = await fileService.ApproveMerger(recordalApp);
        if (res == false)
        {
            Console.WriteLine("Failed to approve");
            return NotFound();
        }
        return Ok(res);
    }
    [HttpPost("ApproveRegisteredUser")]
    public async Task<IActionResult> ApproveRegisteredUser([FromBody] TreatRecordalDto recordalApp)
    {
        var res = await fileService.ApproveRegUser(recordalApp);
        if (res == false)
        {
            Console.WriteLine("Failed to approve");
            return NotFound();
        }
        return Ok(res);
    }
    [HttpGet("GetMergerApplication")]
    public async Task<IActionResult> GetMergerApplication([FromQuery] string fileId, [FromQuery] string appId)
    {
        var res = await fileService.GetMergerApplication(fileId, appId);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }
    [HttpGet("GetAllRegisteredUsers")]
    public async Task<IActionResult> GetAllRegisteredUsers([FromQuery] string fileId)
    {
        var res = await fileService.GetAllRegisteredUsers(fileId);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }
    [HttpGet("GetChangeDataRecordalCost")]
    public async Task<IActionResult> GetChangeDataRecordalCost([FromQuery] string fileId, [FromQuery] FileTypes fileType, [FromQuery] string changeType)
    {
        var res = await fileService.GetChangeDataCost(fileId, fileType, changeType);
        if (res == null)
        {
            return NoContent();
        }
        return Ok(res);
    }
    [HttpPost("ChangeDataRecordal")]
    public async Task<IActionResult> ChangeDataRecordal([FromForm] ChangeDataRecordalDto data)
    {
        var changeClass = data.ChangeType == "Class";
        var res = changeClass ? await fileService.TrademarkReclassification(data) : await fileService.ChangeDataRecordal(data);
        if (res == null)
        {
        
            return BadRequest("Failed to submit");
        }
        return Ok(res);
    }
    [HttpGet("GetChangeDataRecordal")]
    public async Task<IActionResult> GetChangeDataRecordal([FromQuery] string fileId, [FromQuery] string appId)
    {
        var res = await fileService.GetChangeDataRecordal(fileId, appId);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }
    [HttpGet("GetFileByFileNumber")]
    public async Task<IActionResult> GetFileByFileNumber([FromQuery] string fileNumber)
    {
        var res = await fileService.GetFileByNumber(fileNumber);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }

    [HttpPost("ChangeOfAgent")]
    public async Task<IActionResult> ChangeOfAgent(
        [FromForm] string fileId,
        [FromForm] string userId,
        IFormFile? powerOfAttorney)
    {
        if (string.IsNullOrWhiteSpace(fileId) || string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { success = false, message = "fileId and userId are required." });

        var (success, message) = await fileService.ChangeOfAgent(fileId, userId, powerOfAttorney);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }
    [HttpGet("GetAllFileDetails")]
    public async Task<IActionResult> GetAllFileDetails([FromQuery] string fileNumber)
    {
        var res = await fileService.GetAllFileDetails(fileNumber);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }
    [HttpPost("DenyRecordal")]
    public async Task<IActionResult> DenyRecordal([FromBody] TreatRecordalDto recordalApp)
    {
        var res = await fileService.DenyRecordal(recordalApp);
        if (res == false)
        {
            Console.WriteLine("Failed to deny recordal");
            return NotFound();
        }
        return Ok(res);
    }
    [HttpGet("GetRegUserApplication")]
    public async Task<IActionResult> GetRegUserApp([FromQuery] string fileId, [FromQuery] string appId)
    {
        var res = await fileService.GetRegUserApplication(fileId, appId);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }
    [HttpPost("ApproveChangeDataRecordal")]
    public async Task<IActionResult> ApproveChangeDataRecordal([FromBody] TreatRecordalDto recordalApp)
    {
        var res = await fileService.ApproveChangeDataRecordal(recordalApp);
        if (res == false)
        {
            Console.WriteLine("Failed to approve change data recordal");
            return NotFound();
        }
        return Ok(res);
    }
    //[HttpGet("GetRenewalCost")]
    //public async Task<IActionResult> GetRenewalCost([FromQuery] string fileId, [FromQuery] FileTypes fileType, [FromQuery] string userId)
    //{
    //    var res = await fileService.TrademarkRenewalCost(fileId, fileType, userId);
    //    if (res == null)
    //    {
    //        return NoContent();
    //    }
    //    return Ok(res);
    //}
    [HttpPost("RenewalApplication")]
    public async Task<IActionResult> RenewalApplication([FromQuery] string fileId, [FromQuery] string rrr)
    {
        var res = await fileService.RenewalApplication(fileId, rrr);
        if (res == false)
        {
            Console.WriteLine("Failed to submit renewal application");
            return NotFound();
        }
        return Ok(res);
    }

    [HttpGet("GetPatentRenewalCost")]
    public async Task<IActionResult> GetPatentRenewalCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        var res = await fileService.PatentRenewalCost(fileId, fileType);
        if (res == null)
        {
            return NoContent();
        }
        return Ok(res);
    }

    /// <summary>
    /// Retrieves the cost and payment reference for a patent assignment application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <param name="fileType">The type of file (e.g., Patent, Design).</param>
    /// <returns>
    /// 200: Success, returns cost and payment details.<br/>
    /// 204: No content if the file or applicant is not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpGet("GetPatentAssignmentCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatentAssignmentCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.PatentAssignmentCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception ex)
        {
            // Optionally log ex
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }


    /// <summary>
    /// Retrieves the cost and payment reference for a patent license application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <param name="fileType">The type of file (e.g., Patent, Design).</param>
    /// <returns>
    /// 200: Success, returns cost and payment details.<br/>
    /// 204: No content if the file or applicant is not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpGet("GetPatentLicenseCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatentLicenseCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.PatentLicenseCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Retrieves the cost and payment reference for a patent mortgage application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <param name="fileType">The type of file (e.g., Patent, Design).</param>
    /// <returns>
    /// 200: Success, returns cost and payment details.<br/>
    /// 204: No content if the file or applicant is not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpGet("GetPatentMortgageCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatentMortgageCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.PatentMortgageCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Retrieves the cost and payment reference for a patent CTC (Certified True Copy) application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <param name="fileType">The type of file (e.g., Patent, Design).</param>
    /// <returns>
    /// 200: Success, returns cost and payment details.<br/>
    /// 204: No content if the file or applicant is not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpGet("GetPatentCtcCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatentCtcCost(
    [FromQuery] string fileId,
    [FromQuery] FileTypes fileType,
    [FromQuery] int numberOfAttachments = 1) // NEW PARAMETER with default value
    {
        try
        {
            var res = await fileService.PatentCtcCost(fileId, fileType, numberOfAttachments);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Retrieves the cost and payment reference for a patent amendment application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <param name="fileType">The type of file (e.g., Patent, Design).</param>
    /// <returns>
    /// 200: Success, returns cost and payment details.<br/>
    /// 204: No content if the file or applicant is not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpGet("GetPatentAmendmentCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatentAmendmentCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.PatentAmendmentCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Retrieves the cost and payment reference for a patent merger application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <param name="fileType">The type of file (e.g., Patent, Design).</param>
    /// <returns>
    /// 200: Success, returns cost and payment details.<br/>
    /// 204: No content if the file or applicant is not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpGet("GetPatentMergerCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatentMergerCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.PatentMergerCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    //Design License Post Registration Section
    [HttpGet("GetDesignLicenseCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignLicenseCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.DesignLicenseCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }

            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Retrieves the cost and payment reference for a design mortgage application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <param name="fileType">The type of file (expected to be Design).</param>
    /// <returns>
    /// 200: Success, returns cost and payment details.<br/>
    /// 204: No content if the file or applicant is not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpGet("GetDesignMortgageCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignMortgageCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.DesignMortgageCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }

            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignLicenseApplication")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignLicenseApplication([FromBody] DesignLicenseDto dto)
    {
        try
        {
            var created = await fileService.NewDesignLicenseApplication(dto);
            if (!created)
            {
                return BadRequest(ApiResponse<string>.Fail("Design license application could not be created."));
            }

            return Ok(ApiResponse<string>.Ok("Design license application submitted successfully."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

  
    [HttpGet("GetDesignLicenseDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignLicenseDetails([FromQuery] string fileId)
    {
        try
        {
            var details = await fileService.GetDesignLicenseDetailsAsync(fileId);
            if (details == null)
            {
                return NotFound(ApiResponse<string>.Fail("Design license recordal not found."));
            }

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignLicenseDecision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignLicenseDecision([FromBody] DesignLicenseDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.DesignLicenseDecisionAsync(
                dto.FileId,
                dto.AppId,
                dto.Approve,
                dto.Reason,
                dto.NewLicensee,
                dto.UserId);

            if (!success)
            {
                return BadRequest(ApiResponse<string>.Fail(message));
            }

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignMortgageApplication")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignMortgageApplication([FromBody] DesignMortgageDto dto)
    {
        try
        {
            var created = await fileService.NewDesignMortgageApplication(dto);
            if (!created)
            {
                return BadRequest(ApiResponse<string>.Fail("Design mortgage application could not be created."));
            }

            return Ok(ApiResponse<string>.Ok("Design mortgage application submitted successfully."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpGet("GetDesignMortgageDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignMortgageDetails([FromQuery] string fileId)
    {
        try
        {
            var details = await fileService.GetDesignMortgageDetailsAsync(fileId);
            if (details == null)
            {
                return NotFound(ApiResponse<string>.Fail("Design mortgage recordal not found."));
            }

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignMortgageDecision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignMortgageDecision([FromBody] DesignMortgageDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.DesignMortgageDecisionAsync(
                dto.FileId,
                dto.AppId,
                dto.Approve,
                dto.Reason,
                dto.NewMortgagee, dto.UserId);

            if (!success)
            {
                return BadRequest(ApiResponse<string>.Fail(message));
            }

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    //Design Assignment Post Registration Section
    [HttpGet("GetDesignAssignmentCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignAssignmentCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.DesignAssignmentCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }

            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignAssignmentApplication")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignAssignmentApplication([FromBody] DesignAssignmentDto dto)
    {
        try
        {
            var created = await fileService.NewDesignAssignmentApplication(dto);
            if (!created)
            {
                return BadRequest(ApiResponse<string>.Fail("Design assignment application could not be created."));
            }

            return Ok(ApiResponse<string>.Ok("Design assignment application submitted successfully."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpGet("GetDesignAssignmentDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignAssignmentDetails([FromQuery] string fileId)
    {
        try
        {
            var details = await fileService.GetDesignAssignmentDetailsAsync(fileId);
            if (details == null)
            {
                return NotFound(ApiResponse<string>.Fail("Design assignment recordal not found."));
            }

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignAssignmentDecision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignAssignmentDecision([FromBody] DesignAssignmentDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.DesignAssignmentDecisionAsync(
                dto.FileId,
                dto.AppId,
                dto.Approve,
                dto.Reason,
                dto.NewAssignee, dto.UserId);

            if (!success)
            {
                return BadRequest(ApiResponse<string>.Fail(message));
            }

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    //Design Merger Post Registration Section
    [HttpGet("GetDesignMergerCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignMergerCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.DesignMergerCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }

            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignMergerApplication")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignMergerApplication([FromBody] DesignMergerDto dto)
    {
        try
        {
            var created = await fileService.NewDesignMergerApplication(dto);
            if (!created)
            {
                return BadRequest(ApiResponse<string>.Fail("Design merger application could not be created."));
            }

            return Ok(ApiResponse<string>.Ok("Design merger application submitted successfully."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpGet("GetDesignMergerDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignMergerDetails([FromQuery] string fileId)
    {
        try
        {
            var details = await fileService.GetDesignMergerDetailsAsync(fileId);
            if (details == null)
            {
                return NotFound(ApiResponse<string>.Fail("Design merger recordal not found."));
            }

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignMergerDecision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignMergerDecision([FromBody] DesignMergerDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.DesignMergerDecisionAsync(
                dto.FileId,
                dto.AppId,
                dto.Approve,
                dto.Reason,
                dto.MergedEntity, dto.UserId);

            if (!success)
            {
                return BadRequest(ApiResponse<string>.Fail(message));
            }

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    //Design CTC Post Registration Section
    [HttpGet("GetDesignCtcCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignCtcCost([FromQuery] string fileId, [FromQuery] FileTypes fileType, [FromQuery] int numberOfAttachments = 1)
    {
        try
        {
            var res = await fileService.DesignCtcCost(fileId, fileType, numberOfAttachments);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }

            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignCtcApplication")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignCtcApplication([FromBody] DesignCtcDto dto)
    {
        try
        {
            var created = await fileService.NewDesignCtcApplication(dto, dto.UserId);
            if (!created)
            {
                return BadRequest(ApiResponse<string>.Fail("Design CTC application could not be created."));
            }

            return Ok(ApiResponse<string>.Ok("Design CTC application submitted successfully."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpGet("GetDesignCtcDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignCtcDetails([FromQuery] string fileId)
    {
        try
        {
            var details = await fileService.GetDesignCtcDetailsAsync(fileId);
            if (details == null)
            {
                return NotFound(ApiResponse<string>.Fail("Design CTC recordal not found."));
            }

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignCtcDecision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignCtcDecision([FromBody] DesignCtcDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.DesignCtcDecisionAsync(
                dto.FileId,
                dto.AppId,
                dto.Approve,
                dto.Reason,
                dto.UserId);

            if (!success)
            {
                return BadRequest(ApiResponse<string>.Fail(message));
            }

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpGet("GetDesignAmendmentCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignAmendmentCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        try
        {
            var res = await fileService.DesignAmendmentCost(fileId, fileType);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignAmendmentApplication")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignAmendmentApplication([FromBody] DesignAmendmentDto dto)
    {
        try
        {
            var created = await fileService.NewDesignAmendmentApplication(dto, dto.UserId);
            if (!created)
            {
                return BadRequest(ApiResponse<string>.Fail("Design amendment application could not be created."));
            }

            return Ok(ApiResponse<string>.Ok("Design amendment application submitted successfully."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpGet("GetDesignAmendmentDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDesignAmendmentDetails([FromQuery] string fileId, [FromQuery] string appId)
    {
        try
        {
            var details = await fileService.GetDesignAmendmentDetailsAsync(fileId, appId);
            if (details == null)
            {
                return NotFound(ApiResponse<string>.Fail("Design amendment application not found."));
            }

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    [HttpPost("DesignAmendmentDecision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesignAmendmentDecision([FromBody] DesignAmendmentDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.DesignAmendmentDecisionAsync(
                dto.FileId,
                dto.AppId,
                dto.Approve,
                dto.Reason,
                dto.UserId);

            if (!success)
            {
                return BadRequest(ApiResponse<string>.Fail(message));
            }

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail($"An error occurred: {ex.Message}"));
        }
    }

    // TRADEMARK CTC ENDPOINTS
    [HttpGet("GetTrademarkCtcCost")]
    [ProducesResponseType(typeof(ApiResponse<RecordalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTrademarkCtcCost(
        [FromQuery] string fileId,
        [FromQuery] FileTypes fileType,
        [FromQuery] int numberOfAttachments = 1)
    {
        try
        {
            var res = await fileService.TrademarkCtcCost(fileId, fileType, numberOfAttachments);
            if (res == null)
            {
                return StatusCode(StatusCodes.Status204NoContent, ApiResponse<string>.Fail("No file or applicant found."));
            }
            return Ok(ApiResponse<RecordalDto>.Ok(res));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("TrademarkCtcApplication")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TrademarkCtcApplication([FromBody] TrademarkCtcDto dto)
    {
        try
        {
            var created = await fileService.NewTrademarkCtcApplication(dto);
            if (!created)
                return BadRequest(ApiResponse<string>.Fail("Trademark CTC application could not be created."));

            return Ok(ApiResponse<string>.Ok("Trademark CTC application submitted successfully."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("GetTrademarkCtcDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTrademarkCtcDetails([FromQuery] string fileId)
    {
        try
        {
            var details = await fileService.GetTrademarkCtcDetailsAsync(fileId);
            if (details == null)
            {
                return NotFound(ApiResponse<string>.Fail("Trademark CTC recordal not found."));
            }

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("TrademarkCtcDecision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TrademarkCtcDecision([FromBody] TrademarkCtcDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.TrademarkCtcDecisionAsync(
                dto.FileId,
                dto.AppId,
                dto.Approve,
                dto.Reason,
                dto.UserId);

            if (!success)
            {
                return BadRequest(ApiResponse<string>.Fail(message));
            }

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<string>.Fail($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("AddStatusSearchHistory")]
    public async Task<IActionResult> AddStatusSearchHistory([FromQuery] string fileId, [FromQuery] string rrr)
    {
        var res = await fileService.AddNewStatusSearchHistoryAsync(fileId, rrr);
        if (!res)
        {
            Console.WriteLine("Failed to add status search history");
            return NotFound();
        }
        return Ok(res);
    }

    [HttpGet("GetAssignmentCost")]
    public async Task<IActionResult> GetAssignmentCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        var res = await fileService.GetAssignmentCost(fileId, fileType);
        if (res == null)
        {
            return NoContent();
        }
        return Ok(res);
    }
    [HttpGet("GetAssignmentApplication")]
    public async Task<IActionResult> GetAssignmentApplication([FromQuery] string fileId, [FromQuery] string appId)
    {
        var res = await fileService.GetAssignmentApplication(fileId, appId);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }
    [HttpPost("AssignmentApplication")]
    public async Task<IActionResult> AssignmentApplication([FromForm] AssignmentAppDto data)
    {
        var res = await fileService.NewAssignmentApplication(data);
        if (res == false)
        {
            Console.WriteLine("Failed to submit assignment application");
            return NotFound();
        }
        return Ok(res);
    }
    [HttpPost("ApproveAssignment")]
    public async Task<IActionResult> ApproveAssignment([FromBody] TreatRecordalDto recordalApp)
    {
        var res = await fileService.ApproveAssignment(recordalApp);
        if (res == false)
        {
            Console.WriteLine("Failed to approve assignment");
            return NotFound();
        }
        return Ok(res);
    }
    [HttpPost("GetClericalUpdateCost")]
    public async Task<IActionResult> GetClericalUpdateCost([FromBody] GetClericalCostDto dto)
    {
        var res = await fileService.GetClericalUpdateCost(dto);
        if (res == null)
        {
            return BadRequest(new {message = "Failed to Get Cost"});
        }
        return Ok(res);
    }
    [HttpPost("ClericalUpdate")]
    public async Task<IActionResult> ClericalUpdate([FromForm] ClericalUpdateDto clericalUpdate)
    {
        var res = await fileService.ClericalUpdate(clericalUpdate);
        if (res == "Failed")
        {
            return BadRequest(new {message = "Failed to Save Application"});
        }
        return Ok(res);
    }
    [HttpPost("ConfirmClericalUpdate")]
    public async Task<IActionResult> ConfirmClericalUpdate([FromQuery] string fileId, [FromQuery] string clericalId)
    {
        var res = await fileService.ApplyClericalUpdateToFile(fileId, clericalId);
        if (res == false)
        {
            return BadRequest(new { message = "Failed to Confirm Clerical Update" });
        }
        return Ok(new { message = "Clerical update confirmed"});
    }
    [HttpPost("UpdateRecordalStatus")]
    public async Task<IActionResult> UpdateRecordalStatus([FromQuery] string fileId, [FromQuery] string rrr)
    {
        var res = await fileService.UpdateRecordalStatus(fileId, rrr);
        if (res == false)
        {
            return NoContent();
        }
        return Ok(res);
    }
    [HttpPost("UpdateCertificatePaymentStatus")]
    public async Task<IActionResult> UpdateCertificatePaymentStatus([FromQuery] string fileId, [FromQuery] string rrr)
    {
        var res = await fileService.UpdateCertificatePaymentStatus(fileId, rrr);
        if (res == false)
        {
            return NotFound();
        }
        return Ok(res);
    }

    [HttpGet("GetClericalUpdateApp")]
    public async Task<IActionResult> GetClericalUpdateApp([FromQuery] string fileId, [FromQuery] string appId)
    {
        var res = await fileService.GetClericalUpdateApp(fileId, appId);
        if (res == null)
        {
            return NoContent();
        }
        return Ok(res);
    }

    [HttpGet("GetApplicationsByFile")]
    public async Task<IActionResult> GetApplicationsByFile([FromQuery] string fileId)
    {
        var res = await fileService.GetApplicationsByFile(fileId);
        return Ok(res);
    }
    [HttpPost("UpdatePaymentId")]
    public async Task<IActionResult> UpdatePaymentId([FromBody] UpdatePaymentDto data)
    {
        var res = await fileService.UpdatePaymentId(data);
        if (res == false)
        {
            return BadRequest("Failed to update payment ID");
        }
        return Ok(res);
    }

    [HttpPut("update-filing")]
    public async Task<IActionResult> UpdateFiling([FromBody] FileUpdateDto request)
    {
        if (string.IsNullOrWhiteSpace(request.FileId))
            return BadRequest(new { status = "ERROR", message = "FileId is required." });

        var (statusCode, message, updatedFile) = await fileService.UpdateFilingAsync(request);

        return StatusCode(statusCode, new
        {
            status = statusCode == 200 ? "SUCCESS" : "ERROR",
            message,
            file = updatedFile
        });
    }

    [HttpPatch("updatepatentfiles")]
    public async Task<IActionResult> UpdatePatentFile([FromBody] UpdatePatentFileDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FileId))
            return BadRequest(new { status = "ERROR", message = "FileId is required." });

        var (statusCode, message) = await fileService.UpdatePatentFiles(dto);

        return StatusCode(statusCode, new { status = statusCode == 200 ? "SUCCESS" : "ERROR", message });
    }

    [HttpGet("File-Update-history")]
    public async Task<IActionResult> GetUpdatedFileHistory()
    {
        var history = await fileService.GetAllFileUpdateHistoryAsync();
        return Ok(history);
    }

    [HttpGet("files/{fileId}/type")]
    public async Task<IActionResult> GetFileType(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return BadRequest(new { message = "FileId is required." });

        // Decode the fileId
        var decodedFileId = Uri.UnescapeDataString(fileId);

        var fileType = await fileService.GetFileTypeByFileIdAsync(decodedFileId);

        if (fileType == null)
            return NotFound(new { message = "File not found." });

        return Ok(new { fileId = decodedFileId, type = fileType.ToString() });
    }

    [HttpGet("{fileId}/getattachments")]
    public async Task<IActionResult> GetAllPatentAndDesignAttachments(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return BadRequest(new { message = "FileId is required." });

        var decodedFileId = Uri.UnescapeDataString(fileId);

        var result = await fileService.GetAllPatentAndDesignAttachmentsAsync(decodedFileId);

        if (result == null)
            return NotFound(new { message = "File not found or not a Patent/Design file." });

        return Ok(result);
    }

    [HttpPatch("{fileId}/updateattachments")]
    public async Task<IActionResult> UpdateAttachments(string fileId, [FromBody] UpdateAttachmentDto dto)
    {
        Console.WriteLine($"?? Incoming fileId: {fileId}");
        if (dto == null || dto.Attachments == null || !dto.Attachments.Any())
            return BadRequest("Attachments payload is required.");

        var decodedFileId = Uri.UnescapeDataString(fileId);
        var success = await fileService.UpdateAttachmentsAsync(decodedFileId, dto.Attachments);

        if (!success)
            return NotFound($"Filing with FileId {decodedFileId} not found.");

        return Ok("Attachments updated successfully.");
    }

    [HttpPost("appeal-module")]
    public async Task<IActionResult> AppealModule([FromForm] AppealDto appeal)
    {
        var result = await fileService.UploadAppealFiles(appeal);

        if (!result)
            return BadRequest("Error uploading appeal");

        return Ok();
    }

    [HttpPost("PublicationStatusUpdate")]
    public async Task<IActionResult> PublicationStatusUpdate([FromBody] PublicationUpdateDto dto)
    {
        var (success, message) = await fileService.PublicationStatusUpdateAsync(dto);
        if (!success)
            return NotFound("File not found");
        return Ok("Publication date and attachments updated successfully.");
    }

    [HttpGet("publication-details/{*fileId}")]
    public async Task<IActionResult> GetPublicationDetails(string fileId)
    {
        var decodedFileId = Uri.UnescapeDataString(fileId);
        var result = await fileService.GetFilePublicationDetailsAsync(decodedFileId);
        if (result == null)
            return NotFound(new { message = "File not found" });

        return Ok(result);
    }

    [HttpPost("PublicationStatusDecision")]
    public async Task<IActionResult> PublicationStatusDecision([FromBody] PublicationStatusDecisionDto dto)
    {
        var (success, message) = await fileService.PublicationStatusDecisionAsync(dto.FileId, dto.Approve, dto.Comment, dto.UserId);
        if (!success)
            return NotFound(new { message });

        return Ok(new { message });
    }

    [HttpPost("withdrawal-request")]
    public async Task<IActionResult> WithdrawalRequest([FromBody] WithdrawalRequestDto dto)
    {
        var (success, message) = await fileService.WithdrawalRequestAsync(dto);
        if (!success)
            return NotFound("File not found");
        return Ok("Withdrawal date and attachments updated successfully.");
    }

    [HttpGet("withdrawal-details/{fileId}")]
    public async Task<IActionResult> GetWithdrawalDetailsAsync(string fileId)
    {
        var decodedFileId = Uri.UnescapeDataString(fileId);
        var result = await fileService.GetWithdrawalDetailsAsync(decodedFileId);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    [HttpPost("withdrawalrequestdecision")]
    public async Task<IActionResult> WithdrawalRequestDecision([FromBody] PublicationStatusDecisionDto dto)
    {
        var (success, message) = await fileService.WithdrawalRequestDecisionAsync(dto.FileId, dto.Approve, dto.Comment, dto.UserId);
        if (!success)
            return NotFound(new { message });

        return Ok(new { message });
    }

    [HttpPost("offline-renewal/submit")]
    public async Task<IActionResult> SubmitOfflineRenewalRequest([FromBody] OfflineRenewalSubmitDto dto)
    {
        var (success, message, requestId) = await fileService.SubmitOfflineRenewalRequestAsync(dto);
        if (!success)
            return BadRequest(new { message });

        return Ok(new { message, requestId });
    }

    [HttpGet("offline-renewal/requests/{requestId}")]
    public async Task<IActionResult> GetOfflineRenewalRequestDetails(string requestId)
    {
        var (success, message, data) = await fileService.GetOfflineRenewalRequestDetailsAsync(requestId);
        if (!success)
            return NotFound(new { message });

        return Ok(new { message, data });
    }

    [HttpGet("offline-renewal/application-history/{applicationHistoryId}")]
    public async Task<IActionResult> GetOfflineRenewalRequestByApplicationHistoryId(string applicationHistoryId)
    {
        var (success, message, data) = await fileService.GetOfflineRenewalRequestDetailsByApplicationHistoryIdAsync(applicationHistoryId);
        if (!success)
            return NotFound(new { message });

        return Ok(new { message, data });
    }

    [HttpPost("offline-renewal/decision")]
    public async Task<IActionResult> DecideOfflineRenewalRequest([FromBody] OfflineRenewalDecisionDto dto)
    {
        var (success, message) = await fileService.DecideOfflineRenewalRequestAsync(dto);
        if (!success)
        {
            if (string.Equals(message, "Unauthorized", StringComparison.OrdinalIgnoreCase))
                return Unauthorized(new { message });

            return BadRequest(new { message });
        }

        return Ok(new { message });
    }

    [HttpGet("getappeal")]
    public async Task<IActionResult> GetAppeal(string fileId, string appId)
    {
        var res = await fileService.GetAppealRequest(fileId, appId);
        if (res == null)
        {
            return NotFound();
        }
        return Ok(res);
    }

    [HttpPost("treat-appeal")]
    public async Task<IActionResult> TreatAppeal(TreatAppealDto data)
    {
        var res = await fileService.TreatAppeal(data);
        if (res == false)
        {
            Console.WriteLine("Failed to treat appeal");
            return NotFound();
        }
        return Ok(res);
    }

    [HttpPost("approve-amendment")]
    public async Task<IActionResult> ApproveAmendment([FromBody] AmendmentDto dto)
    {
        var res = await fileService.ApproveAmendmentAsync(dto);
        if (res == false)
        {
            Console.WriteLine("Failed to approve amendment");
            return NotFound();
        }
        return Ok(res);

    }


    #region Patent Assignment Post Registration Section
    /// <summary>
    /// Submits a new patent assignment application.
    /// </summary>
    /// <remarks>
    /// The frontend must provide the FileId, RRR (Remita payment reference), assignment deed, supporting documents, and assignment dates.
    /// The backend will verify payment, save the application, update status, and attach the provided documents.
    /// </remarks>
    /// <param name="dto">The patent assignment application details, including file ID, RRR, assignment deed, supporting documents, and dates.</param>
    /// <returns>
    /// 200: Success, application submitted and saved.<br/>
    /// 400: Bad request, invalid data or file not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpPost("PatentAssignmentApplication")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PatentAssignmentApplication([FromBody] PatentAssignmentDto dto)
    {
        try
        {
            var result = await fileService.NewPatentAssignmentApplication(dto);
            if (!result)
                return BadRequest(ApiResponse<string>.Fail("Failed to submit patent assignment application."));
            return Ok(ApiResponse<bool>.Ok(true, "Patent assignment application submitted successfully."));
        }
        catch (Exception ex)
        {
           // _log.LogError(ex, "Error-at-PatentAssignmentApplication");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Returns all attachments, new assignee, and old assignor details for a patent assignment application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <returns>
    /// 200: Success, returns assignment attachments and assignee details.<br/>
    /// 404: Not found if no assignment application exists for the file.<br/>
    /// </returns>
    [HttpGet("GetPatentAssignmentDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPatentAssignmentDetails([FromQuery] string fileId)
    {
        var result = await fileService.GetPatentAssignmentDetailsAsync(fileId);
        if (result == null)
            return NotFound(ApiResponse<string>.Fail("No assignment application found for this file."));

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Examiner decision on a patent assignment application.
    /// </summary>
    /// <remarks>
    /// The examiner reviews the assignment application, enters a reason, and chooses to approve or refuse.
    /// If approved, the system updates the assignment status and applicant info. If refused, status is updated and applicant info remains unchanged.
    /// </remarks>
    /// <param name="dto">Assignment decision details including file ID, application ID, approval flag, reason, and new assignee info.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200: Success, returns decision result and message.</item>
    /// <item>404: Not found if file or application does not exist.</item>
    /// <item>500: Internal server error.</item>
    /// </list>
    /// </returns>
    [HttpPost("assignment-decision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AssignmentDecision([FromBody] PatentAssignmentDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.PatentAssignmentDecisionAsync(
                dto.FileId, dto.AppId, dto.Approve, dto.Reason, dto.NewAssignee, dto.AppUserId);

            if (!success)
                return NotFound(ApiResponse<string>.Fail(message));

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception ex)
        {
            // Optionally log ex
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    #endregion


    #region Patent License Post Registration Section
    /// <summary>
    /// Submits a new patent license application.
    /// </summary>
    /// <remarks>
    /// The frontend must provide the FileId, RRR (Remita payment reference), deed of license, supporting documents, and license dates.
    /// The backend will verify payment, save the application, update status, and attach the provided documents.
    /// </remarks>
    /// <param name="dto">The patent license application details, including file ID, RRR, deed of license, supporting documents, and dates.</param>
    /// <returns>
    /// 200: Success, application submitted and saved.<br/>
    /// 400: Bad request, invalid data or file not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpPost("PatentLicenseApplication")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PatentLicenseApplication([FromBody] PatentLicenseDto dto)
    {
        try
        {
            var result = await fileService.NewPatentLicenseApplication(dto);
            if (!result)
                return BadRequest(ApiResponse<string>.Fail("Failed to submit patent license application."));
            return Ok(ApiResponse<bool>.Ok(true, "Patent license application submitted successfully."));
        }
        catch (Exception ex)
        {
            // Optionally log ex
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Returns all attachments, new licensee, and old licensor details for a patent license application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <returns>
    /// 200: Success, returns license attachments and licensee/licensor details.<br/>
    /// 404: Not found if no license application exists for the file.<br/>
    /// </returns>
    [HttpGet("GetPatentLicenseDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPatentLicenseDetails([FromQuery] string fileId)
    {
        var result = await fileService.GetPatentLicenseDetailsAsync(fileId);
        if (result == null)
            return NotFound(ApiResponse<string>.Fail("No license application found for this file."));

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Examiner decision on a patent license application.
    /// </summary>
    /// <remarks>
    /// The examiner reviews the license application, enters a reason, and chooses to approve or refuse.
    /// If approved, the system updates the license status and applicant info. If refused, status is updated and applicant info remains unchanged.
    /// </remarks>
    /// <param name="dto">License decision details including file ID, application ID, approval flag, reason, and new licensee info.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200: Success, returns decision result and message.</item>
    /// <item>404: Not found if file or application does not exist.</item>
    /// <item>500: Internal server error.</item>
    /// </list>
    /// </returns>
    [HttpPost("license-decision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LicenseDecision([FromBody] PatentLicenseDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.PatentLicenseDecisionAsync(
                dto.FileId, dto.AppId, dto.Approve, dto.Reason, dto.NewLicensee, dto.AppUserId);

            if (!success)
                return NotFound(ApiResponse<string>.Fail(message));

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    #endregion


    #region Patent Merger Post Registration Section
    /// <summary>
    /// Submits a new patent merger application.
    /// </summary>
    /// <remarks>
    /// The frontend must provide the FileId, RRR (Remita payment reference), deed of merger, supporting documents, and merger dates.
    /// The backend will verify payment, save the application, update status, and attach the provided documents.
    /// </remarks>
    /// <param name="dto">The patent merger application details, including file ID, RRR, deed of merger, supporting documents, and dates.</param>
    /// <returns>
    /// 200: Success, application submitted and saved.<br/>
    /// 400: Bad request, invalid data or file not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpPost("PatentMergerApplication")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PatentMergerApplication([FromBody] PatentMergerDto dto)
    {
        try
        {
            var result = await fileService.NewPatentMergerApplication(dto);
            if (!result)
                return BadRequest(ApiResponse<string>.Fail("Failed to submit patent merger application."));
            return Ok(ApiResponse<bool>.Ok(true, "Patent merger application submitted successfully."));
        }
        catch (Exception ex)
        {
            // Optionally log ex
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Returns all attachments, new merged party, and old merger party details for a patent merger application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <returns>
    /// 200: Success, returns merger attachments and merger party details.<br/>
    /// 404: Not found if no merger application exists for the file.<br/>
    /// </returns>
    [HttpGet("GetPatentMergerDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPatentMergerDetails([FromQuery] string fileId)
    {
        var result = await fileService.GetPatentMergerDetailsAsync(fileId);
        if (result == null)
            return NotFound(ApiResponse<string>.Fail("No merger application found for this file."));

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Examiner decision on a patent merger application.
    /// </summary>
    /// <remarks>
    /// The examiner reviews the merger application, enters a reason, and chooses to approve or refuse.
    /// If approved, the system updates the merger status and applicant info. If refused, status is updated and applicant info remains unchanged.
    /// </remarks>
    /// <param name="dto">Merger decision details including file ID, application ID, approval flag, reason, and new merged party info.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200: Success, returns decision result and message.</item>
    /// <item>404: Not found if file or application does not exist.</item>
    /// <item>500: Internal server error.</item>
    /// </list>
    /// </returns>
    [HttpPost("merger-decision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MergerDecision([FromBody] PatentMergerDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.PatentMergerDecisionAsync(
                dto.FileId, dto.AppId, dto.Approve, dto.Reason, dto.NewMergedParty, dto.AppUserId);

            if (!success)
                return NotFound(ApiResponse<string>.Fail(message));

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    #endregion


    #region Patent Mortgage Post Registration Section
    /// <summary>
    /// Submits a new patent mortgage application.
    /// </summary>
    /// <remarks>
    /// The frontend must provide the FileId, RRR (Remita payment reference), deed of mortgage, supporting documents, and mortgage dates.
    /// The backend will verify payment, save the application, update status, and attach the provided documents.
    /// </remarks>
    /// <param name="dto">The patent mortgage application details, including file ID, RRR, deed of mortgage, supporting documents, and dates.</param>
    /// <returns>
    /// 200: Success, application submitted and saved.<br/>
    /// 400: Bad request, invalid data or file not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpPost("PatentMortgageApplication")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PatentMortgageApplication([FromBody] PatentMortgageDto dto)
    {
        try
        {
            var result = await fileService.NewPatentMortgageApplication(dto);
            if (!result)
                return BadRequest(ApiResponse<string>.Fail("Failed to submit patent mortgage application."));
            return Ok(ApiResponse<bool>.Ok(true, "Patent mortgage application submitted successfully."));
        }
        catch (Exception ex)
        {
            // Optionally log ex
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Returns all attachments, new mortgagee, and old mortgagor details for a patent mortgage application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <returns>
    /// 200: Success, returns mortgage attachments and mortgagee/mortgagor details.<br/>
    /// 404: Not found if no mortgage application exists for the file.<br/>
    /// </returns>
    [HttpGet("GetPatentMortgageDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPatentMortgageDetails([FromQuery] string fileId)
    {
        var result = await fileService.GetPatentMortgageDetailsAsync(fileId);
        if (result == null)
            return NotFound(ApiResponse<string>.Fail("No mortgage application found for this file."));

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Examiner decision on a patent mortgage application.
    /// </summary>
    /// <remarks>
    /// The examiner reviews the mortgage application, enters a reason, and chooses to approve or refuse.
    /// If approved, the system updates the mortgage status and applicant info. If refused, status is updated and applicant info remains unchanged.
    /// </remarks>
    /// <param name="dto">Mortgage decision details including file ID, application ID, approval flag, reason, and new mortgagee info.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200: Success, returns decision result and message.</item>
    /// <item>404: Not found if file or application does not exist.</item>
    /// <item>500: Internal server error.</item>
    /// </list>
    /// </returns>
    [HttpPost("mortgage-decision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MortgageDecision([FromBody] PatentMortgageDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.PatentMortgageDecisionAsync(
                dto.FileId, dto.AppId, dto.Approve, dto.Reason, dto.NewMortgagee, dto.AppUserId);

            if (!success)
                return NotFound(ApiResponse<string>.Fail(message));

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    #endregion

    [HttpPost("ExaminePatentDesign")]
    public async Task<IActionResult> ExaminePatentDesign([FromQuery] string fileId, [FromQuery] string userId, [FromQuery] ApplicationStatuses status)
    {
        var res = await fileService.ExaminePatentDesign(fileId, userId, status);
        if (res == false)
        {
            return BadRequest("Failed to examine patent/design");
        }
        return Ok(res);
    }

    #region Patent CTC Post Registration Section

    /// <summary>
    /// Submits a new patent CTC (Certified True Copy) application.
    /// </summary>
    /// <remarks>
    /// The frontend must provide the FileId, RRR (Remita payment reference), and a list of attachment IDs to certify.
    /// The backend will verify payment, save the application, update status, and record which attachments were requested.
    /// </remarks>
    /// <param name="dto">The patent CTC application details, including file ID, RRR, attachment IDs, and request date.</param>
    /// <returns>
    /// 200: Success, application submitted and saved.<br/>
    /// 400: Bad request, invalid data or file not found.<br/>
    /// 500: Internal server error.
    /// </returns>
    [HttpPost("PatentCtcApplication")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> NewPatentCtcApplication([FromBody] PatentCtcDto dto)
    {
        try
        {
            var result = await fileService.NewPatentCtcApplication(dto);
            if (!result)
                return BadRequest(ApiResponse<string>.Fail("Failed to create CTC application."));

            return Ok(ApiResponse<bool>.Ok(true, "Patent CTC application created successfully."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Returns all requested attachments and details for a patent CTC application.
    /// </summary>
    /// <param name="fileId">The unique file identifier.</param>
    /// <returns>
    /// 200: Success, returns CTC attachments and application details.<br/>
    /// 404: Not found if no CTC application exists for the file.<br/>
    /// </returns>
    [HttpGet("GetPatentCtcDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPatentCtcDetails([FromQuery] string fileId)
    {
        try
        {
            var details = await fileService.GetPatentCtcDetailsAsync(fileId);
            if (details == null)
                return NotFound(ApiResponse<string>.Fail("CTC application not found."));

            return Ok(ApiResponse<object>.Ok(details));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Examiner decision on a patent CTC application.
    /// </summary>
    /// <remarks>
    /// The examiner reviews the CTC request, enters a reason, and chooses to approve or refuse.
    /// If approved, the certified copies are marked as ready. If refused, the request is rejected.
    /// </remarks>
    /// <param name="dto">CTC decision details including file ID, application ID, approval flag, and reason.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200: Success, returns decision result and message.</item>
    /// <item>404: Not found if file or application does not exist.</item>
    /// <item>500: Internal server error.</item>
    /// </list>
    /// </returns>
    [HttpPost("ctc-decision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PatentCtcDecision([FromBody] PatentCtcDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.PatentCtcDecisionAsync(dto.FileId, dto.AppId, dto.Approve, dto.Reason, dto.AppUserId);

            if (!success)
                return NotFound(ApiResponse<string>.Fail(message));

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    #endregion
    
    #region Patent Amendment Post Registration Section

    /// <summary>
    /// Submit a new patent amendment application.
    /// </summary>
    [HttpPost("PatentAmendmentApplication")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PatentAmendmentApplication([FromBody] PatentAmendmentDto dto)
    {
        try
        {
            var result = await fileService.NewPatentAmendmentApplication(dto);
            if (!result)
                return BadRequest(ApiResponse<string>.Fail("Failed to submit patent amendment application."));
            return Ok(ApiResponse<bool>.Ok(true, "Patent amendment application submitted successfully."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    /// <summary>
    /// Returns amendment details for a specific patent amendment application.
    /// </summary>
    [HttpGet("GetPatentAmendmentDetails")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPatentAmendmentDetails([FromQuery] string fileId, [FromQuery] string appId)
    {
        var result = await fileService.GetPatentAmendmentDetailsAsync(fileId, appId);
        if (result == null)
            return NotFound(ApiResponse<string>.Fail("No amendment application found for this file and application ID."));

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Examiner decision on a patent amendment application.
    /// </summary>
    [HttpPost("amendment-decision")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AmendmentDecision([FromBody] PatentAmendmentDecisionDto dto)
    {
        try
        {
            var (success, message) = await fileService.PatentAmendmentDecisionAsync(
                dto.fileId, dto.appId, dto.approve, dto.reason, dto.appUserId);

            if (!success)
                return NotFound(ApiResponse<string>.Fail(message));

            return Ok(ApiResponse<string>.Ok(message));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("An error occurred while processing your request."));
        }
    }

    #endregion

    /// <summary>
    /// Get design attachments data for a specific file
    /// </summary>
    [HttpGet("design-attachments/{fileId}")]
    public async Task<IActionResult> GetDesignAttachments(string fileId)
    {
        try
        {
            var result = await fileService.GetDesignAttachmentsDataAsync(fileId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Copy images from another attachment to designs attachment
    /// Example: POST /api/files/copy-to-designs?fileId=F/DS/NT/O/2026/6687&sourceAttachmentName=designDrawings
    /// </summary>
    [HttpPost("copy-to-designs")]
    public async Task<IActionResult> CopyToDesignsAttachment([FromQuery] string fileId, [FromQuery] string sourceAttachmentName)
    {
        try
        {
            var result = await fileService.CopyImagesToDesignsAttachmentAsync(fileId, sourceAttachmentName);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Diagnostic endpoint to check design attachments for a specific file
    /// </summary>
    [HttpGet("diagnose-design-images/{fileId}")]
    public async Task<IActionResult> DiagnoseDesignImages(string fileId)
    {
        try
        {
            var result = await fileService.DiagnoseDesignImagesAsync(fileId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("GetFileIdByFileNumber")]
    public async Task<IActionResult> GetFileIdByFileNumber([FromQuery] string fileNumber)
    {
        var file = await fileService.GetFileIdByFileNumber(fileNumber);
        if (file == null)
            return NotFound(new { message = "File not found" });
        return Ok(new { id = file });
    }

    [HttpGet("RestorationRequest")]
    public async Task<IActionResult> RestorationRequest([FromQuery] string fileId, string userId)
    {
        var result = await fileService.FileRestorationCost(fileId,userId);
        if(result == null)
        {
            return BadRequest("Failed to calculate restoration cost. File may not exist or is not eligible for restoration.");
        }
        return Ok(result);
    }
}
