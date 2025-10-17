using Azure.Core;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Models;
using patentdesign.Services;
using System.Text.Json;
namespace patentdesign.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController(FileServices fileService) : ControllerBase
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
    public async Task<ActionResult> LoadCertificatePaymentDetails([FromQuery] string id)
    {
        var result = await fileService.GetCertificatePaymentCost(id);
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

    [HttpPost("NewApplicationPayment")]
    public async Task<ActionResult> NewApplicationPayment([FromBody] UpdateDataType data)
    {
        var response = await fileService.NewApplicationPayment(data);
        return Ok(response);
    }

    [HttpGet("GetRRRCost")]
    public async Task<ActionResult<dynamic>> GetCostFromRRR([FromQuery] string rrr)
    {
        var res = await fileService.GetNewAppCostFromRemita(rrr);
        return Ok(res);
    }


    [HttpGet("PaidButNotReflecting")]
    public async Task<ActionResult<dynamic>> PaidButNotReflecting()
    {
        await fileService.PaidButNotReflecting();
        return Ok("res");
    }

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

    [HttpGet("GetPublication")]
    public async Task<IActionResult> GetJournal([FromQuery] int type, [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        var data = await fileService.GetTypePublication(start, end, Enum.GetValues<FileTypes>()[type]);
        Response.Headers.Add("Content-Disposition", "attachment; filename=journal.pdf");
        return File(data, "application/pdf", "journal.pdf");
    }


    [HttpGet("GetTrademarkPublication")]
    public async Task<IActionResult> GetTrademarkPublication([FromQuery] string? text = null,
        [FromQuery] int? index = null, [FromQuery] int? quantity = null)
    {
        var data = await fileService.GetTrademarkPublication(text, index, quantity);
        return Ok(data);
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

    [HttpPost("RenewalCost")]
    public async Task<IActionResult> GetRenewalCostRRR([FromBody] GetRenewalCost data)
    {
        var res = await fileService.GetRenewalCost(data);
        return Ok(new { rrr = res.Item1, cost = res.Item2 });
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
    public async Task<IActionResult> AdminUpdateApplication([FromBody] AdminUpdateReq req)
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

    [HttpPost("UpdateCorThis")]
    public async Task<IActionResult> UpdateCorThis([FromQuery] string id, [FromQuery] string userId)
    {
        var stats = await fileService.UpdateCorThis(id, userId);
        if (stats != null)
            return Ok(stats);
        else return BadRequest();
    }

    [HttpPost("UpdateCorAll")]
    public async Task<IActionResult> UpdateCorAll([FromQuery] string id, [FromQuery] string userId, [FromQuery] string creatorAccount)
    {
        var stats = await fileService.UpdateCorAll(id, userId, creatorAccount);
        if (stats != null)
            return Ok(stats);
        else return BadRequest();
    }

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

    [HttpPost("batchRenewalInfo")]
    public async Task<ActionResult<BatchRenewRes>> BatchRenewalInfo([FromBody] BatchRenewReq data)
    {
        var stats = await fileService.GetBatchRenewalInfo(data);
        return stats;
    }

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
    public async Task<ActionResult<string>> ReAssign([FromBody] ReAssignType data)
    {
        var result = await fileService.ReAssign(data);
        return Ok(result);
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
        var res = await fileService.ChangeDataRecordal(data);
        if (res == false)
        {
            Console.WriteLine("Failed to submit");
            return NotFound();
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
    [HttpGet("GetRenewalCost")]
    public async Task<IActionResult> GetRenewalCost([FromQuery] string fileId, [FromQuery] FileTypes fileType)
    {
        var res = await fileService.RenewalCost(fileId, fileType);
        if (res == null)
        {
            return NoContent();
        }
        return Ok(res);
    }
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
    [HttpGet("GetClericalUpdateCost")]
    public async Task<IActionResult> GetClericalUpdateCost([FromQuery] string fileId, [FromQuery] FileTypes fileType, [FromQuery] string updateType)
    {
        var res = await fileService.GetClericalUpdateCost(fileId, fileType, updateType);
        if (res == null)
        {
            return NoContent();
        }
        return Ok(res);
    }
    [HttpPost("ClericalUpdate")]
    public async Task<IActionResult> ClericalUpdate([FromForm]ClericalUpdateDto clericalUpdate)
    {
        var res = await fileService.ClericalUpdate(clericalUpdate);
        if (res == false)
        {
            return NoContent();
        }
        return Ok(res);
    }
    [HttpPost("UpdateRecordalStatus")]
    public async Task<IActionResult> UpdateRecordalStatus([FromQuery]string fileId,[FromQuery] string rrr)
    {
        var res = await fileService.UpdateRecordalStatus(fileId, rrr);
        if (res == false)
        {
            return NoContent();
        }
        return Ok(res);
    }
    [HttpPost("UpdateCertificatePaymentStatus")]
    public async Task<IActionResult> UpdateCertificatePaymentStatus([FromQuery]string fileId, [FromQuery]string rrr)
    {
        var res = await fileService.UpdateCertificatePaymentStatus(fileId, rrr);
        if (res == false)
        {
            return NotFound();
        }
        return Ok(res);
    }

    [HttpGet("GetClericalUpdateApp")]
    public async Task<IActionResult> GetClericalUpdateApp([FromQuery] string fileId,[FromQuery] string appId)
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

        var (statusCode, message) = await fileService.UpdateFilingAsync(request);

        return StatusCode(statusCode, new { status = statusCode == 200 ? "SUCCESS" : "ERROR", message });
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
    public async Task<IActionResult> AppealModule([FromForm]AppealDto appeal)
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
        var (success, message) = await fileService.PublicationStatusDecisionAsync(dto.FileId, dto.Approve, dto.Comment);
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
        var (success, message) = await fileService.WithdrawalRequestDecisionAsync(dto.FileId, dto.Approve, dto.Comment);
        if (!success)
            return NotFound(new { message });

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
        
}
