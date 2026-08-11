using Amazon.Runtime.Internal;
using Azure.Core;
using Bogus.DataSets;
using CloudinaryDotNet.Actions;
using CloudinaryDotNet.Core;
using F23.StringSimilarity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
//using MongoDB.Driver.Core.Operations;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.pdfs;
using patentdesign.Utils;
using QuestPDF.Fluent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tfunctions.pdfs;
using ZstdSharp.Unsafe;
using static QRCoder.PayloadGenerator;
using static QRCoder.PayloadGenerator.ShadowSocksConfig;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;
using File = System.IO.File;

namespace patentdesign.Services;

public class FilesServices
{
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<Counters> _countersCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private static IMongoCollection<TicketInfo> _ticketsCollection;
    private static IMongoCollection<StatusRequests> _statusCollection;
    private static IMongoCollection<AppUser> _userCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;
    private static IMongoCollection<StaffPerformance> _performanceCollection;
    private static IMongoCollection<OppositionType> _oppositionCollection;
    private static IMongoCollection<FileUpdateHistory> _fileUpdateHistoryCollection;
    private static IMongoCollection<PublicationInfo> _publicationCollection;
    private static IMongoCollection<SignatureInfo> _signatures;
    private readonly ILogger<FilesServices> _log;

    private PaymentUtils _remitaPaymentUtils;
    private MongoClient _mongoClient;
    private FinanceService _financeService;
    private PaymentService _paymentService;
    private PublicationServices _publicationServices;
    private NotificationServices _notificationServices;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
     //private string attachmentBaseUrl = "http://localhost:5044";

    public FilesServices(IMongoDatabase db, IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, ILogger<FilesServices> log, PaymentService paymentService, PublicationServices publicationServices, NotificationServices notificationServices)
    {
        var s = patentDesignDbSettings.Value;
        _fillingCollection = db.GetCollection<Filling>(s.FilesCollectionName);
        _countersCollection = db.GetCollection<Counters>(s.CountersCollectionName);
        _financeCollection = db.GetCollection<FinanceHistory>(s.FinanceCollectionName);
        _performanceCollection = db.GetCollection<StaffPerformance>("staffPerformance");
        _statusCollection = db.GetCollection<StatusRequests>("statusrequests");
        _oppositionCollection = db.GetCollection<OppositionType>(s.OppositionCollectionName);
        _ticketsCollection = db.GetCollection<TicketInfo>(s.TicketCollectionName);
        _userCollection = db.GetCollection<AppUser>("appUsers");
        _attachmentCollection = db.GetCollection<AttachmentInfo>(s.AttachmentCollectionName);
        _remitaPaymentUtils = remitaPaymentUtils;
        _paymentService = paymentService;
        _log = log;
        _fileUpdateHistoryCollection = db.GetCollection<FileUpdateHistory>("FileUpdateHistory");
        _publicationCollection = db.GetCollection<PublicationInfo>("trademarkJournal");
        _publicationServices = publicationServices;
        _notificationServices = notificationServices;
        _signatures = db.GetCollection<SignatureInfo>("signatures");
    }

    public async Task<Filling?> GetFileAsync(string id)
    {
        try
        {
            _log.LogDebug("Fetching file by Id {FileId}", id);
            var file = await _fillingCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
            NormalizeOwnershipHistory(file);
            return file;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching file {FileId}", id);
            throw;
        }
    }

    // atomically create file
    public async Task CreateFileAsync(Filling newFile)
    {
        // Check for existing file with the same FileId (idempotency)
        var existing = await _fillingCollection.Find(x => x.FileId == newFile.FileId).FirstOrDefaultAsync();
        if (existing != null)
        {
            _log.LogInformation("File with FileId {FileId} already exists. Skipping creation.", newFile.FileId);
            return;
        }
        newFile.FileId = string.Join("/", [newFile.FileId, Guid.NewGuid().ToString().Split("-")[0]]);
        _log.LogInformation("Creating file with FileId {FileId}, Type {FileType}", newFile.FileId, newFile.Type);
        await _fillingCollection.InsertOneAsync(newFile);
    }

    public async Task<Filling?> ManualUpdate(string fileId, string applicationId, string? userName, string? userId, bool? isCertificate = false)
    {
        _log.LogInformation("ManualUpdate started for FileId {FileId}, AppId {AppId}, IsCertificate {IsCert}",
            fileId, applicationId, isCertificate);

        var file = await _fillingCollection.Find(d => d.FileId == fileId).FirstOrDefaultAsync()
                   ?? throw new KeyNotFoundException("File not found.");

        var application = file.ApplicationHistory?.FirstOrDefault(d => d.id == applicationId)
                          ?? throw new KeyNotFoundException("Application not found.");

        var beforeStatus = application.CurrentStatus;

        if (isCertificate == true)
        {
            _log.LogDebug("Updating certificate application for FileId {FileId}", fileId);
            var update = await UpdateCertificatePaymentStatus(fileId, application.PaymentId);
            if (update) return file;
        }

        var paymentInfo = await ValidateAndGetPaymentInfo(application);
        var paymentDate = DateTime.TryParse(paymentInfo.paymentDate, out var paidAt) ? paidAt : DateTime.Now;

        application.StatusHistory ??= new List<ApplicationHistory>();

        await ProcessApplicationType(file, application, paymentDate, userName, userId);

        var idx = file.ApplicationHistory.FindIndex(f => f.id == application.id);
        if (idx >= 0) file.ApplicationHistory[idx] = application;

        if (beforeStatus != application.CurrentStatus)
        {
            await SendStatusUpdateNotificationAsync(
                file,
                application.id,
                application.ApplicationType,
                beforeStatus,
                application.CurrentStatus);
        }

        _log.LogInformation("ManualUpdate completed for FileId {FileId}", fileId);
        return file;
    }
    private async void SavePayment(RemitaResponseClass pay, PaymentTypes type, string fileId, string appId)
    {
        _log.LogDebug("Saving payment record for FileId {FileId}, AppId {AppId}, Type {PaymentType}", fileId, appId, type);
        var paymentDate = DateTime.TryParse(pay.paymentDate, out var paidAt) ? paidAt : DateTime.Now;
        var fileType = await _fillingCollection
            .Find(f => f.FileId == fileId)
            .Project(f => (FileTypes?)f.Type)
            .FirstOrDefaultAsync();
        var fileTypeValue = ResolvePaymentFileType(fileId, fileType);
        var payment = new PaymentRecord
        {
            ApplicationId = appId,
            PaymentType = type.ToString(),
            Date = paymentDate,
            FileId = fileId,
            FileType = fileTypeValue,
            RemitaResponse = pay
        };
        await _paymentService.AddPaymentRecord(payment);
    }

    private static string ResolvePaymentFileType(string? fileId, FileTypes? fileType)
    {
        if (fileType.HasValue)
        {
            return fileType.Value.ToString();
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            return string.Empty;
        }

        if (fileId.Contains("/TM/", StringComparison.OrdinalIgnoreCase))
        {
            return FileTypes.TradeMark.ToString();
        }

        if (fileId.Contains("/PT/", StringComparison.OrdinalIgnoreCase))
        {
            return FileTypes.Patent.ToString();
        }

        if (fileId.Contains("/DS/", StringComparison.OrdinalIgnoreCase))
        {
            return FileTypes.Design.ToString();
        }

        return string.Empty;
    }
    private async Task<Filling?> HandleCertificateValidation(Filling file, ApplicationInfo application, string? userName, string? userId)
    {
        var certRrr = application.CertificatePaymentId ?? file.ApplicationHistory.FirstOrDefault()?.CertificatePaymentId;

        if (string.IsNullOrWhiteSpace(certRrr))
            throw new ArgumentException("Certificate payment reference not found.");

        _log.LogDebug("Validating certificate payment RRR {Rrr} for FileId {FileId}", certRrr, file.Id);
        var certRes = await ValidateCertificatePayment(file.Id, certRrr, userName, userId);
        return certRes.data;
    }

    private async Task<RemitaResponseClass> ValidateAndGetPaymentInfo(ApplicationInfo application)
    {
        if (string.IsNullOrWhiteSpace(application.PaymentId))
            throw new Exception("Payment reference not found for the application.");

        _log.LogDebug("Validating payment for RRR {Rrr}", application.PaymentId);
        var paymentInfo = await _paymentService.CheckPayment(application.PaymentId);
        _log.LogDebug("Payment status for RRR {Rrr}: {Status}", application.PaymentId, paymentInfo?.status);

        if (paymentInfo == null || paymentInfo.status != "00")
            throw new InvalidOperationException($"Payment Not Found or Invalid RRR, {application.PaymentId}");

        return paymentInfo;
    }

    private async Task ProcessApplicationType(Filling file, ApplicationInfo application, DateTime paymentDate, string? userName, string? userId)
    {
        var firstApp = file.ApplicationHistory.FirstOrDefault();
        switch (application.ApplicationType)
        {
            case FormApplicationTypes.NewApplication:
                await ProcessNewApplication(file, application, paymentDate, userName, userId);
                break;
            case FormApplicationTypes.Amendment:
                AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, ApplicationStatuses.AwaitingApproval,
                    paymentDate, userName, userId, "Payment Successful, awaiting search");
                break;
            case FormApplicationTypes.LicenseRenewal:
                await ProcessLicenseRenewal(file, application, paymentDate, userName, userId);
                break;

            case FormApplicationTypes.ChangeOfName:
            case FormApplicationTypes.ChangeOfAddress:
                await ProcessChangeData(file, application, paymentDate, userName, userId);
                return;

            case FormApplicationTypes.ClericalUpdate:
                await ProcessClericalUpdate(file, application, paymentDate, userName, userId);
                return;
            case FormApplicationTypes.Restoration:
                application.CurrentStatus = ApplicationStatuses.PendingRenewal;
                firstApp.CurrentStatus = ApplicationStatuses.PendingRenewal;
                file.FileStatus = ApplicationStatuses.PendingRenewal;
                
                AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, ApplicationStatuses.PendingRenewal,
                   paymentDate, userName, userId, "Payment Successful, Awaiting Renewal Application");
                break;
            case FormApplicationTypes.Reclassification:
                application.CurrentStatus = ApplicationStatuses.AwaitingRecordalProcess;
                AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, ApplicationStatuses.AwaitingRecordalProcess,
                    paymentDate, userName, userId, "Payment Successful");
                break;
            case FormApplicationTypes.DataUpdate:
                application.ApplicationLetters = [ApplicationLetters.RecordalReceipt, ApplicationLetters.RecordalAck];
                AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, ApplicationStatuses.AwaitingSearch,
                    paymentDate, userName, userId, "Payment Successful");
                break;

            case FormApplicationTypes.Assignment:
                application.ApplicationLetters = [ApplicationLetters.AssignmentReceipt, ApplicationLetters.AssignmentAck];
                application.CurrentStatus = ApplicationStatuses.AwaitingRecordalProcess;
                AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, ApplicationStatuses.AwaitingRecordalProcess,
                    paymentDate, userName, userId, "Payment Successful");
                break;
        }
        await _fillingCollection.FindOneAndReplaceAsync(f => f.Id == file.Id, file);

    }

    private async Task ProcessNewApplication(Filling file, ApplicationInfo application, DateTime paymentDate, string? userName, string? userId)
    {
        _log.LogInformation("Processing new application for FileId {FileId}", file.FileId);
        file.FileStatus = ApplicationStatuses.AwaitingSearch;
        file.FileId = await GenerateNewFileId(file);
        file.FilingDate = paymentDate;
        _log.LogInformation("Generated new file ID {FileId} for {FileType}", file.FileId, file.Type);
        AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, ApplicationStatuses.AwaitingSearch,
            paymentDate, userName, userId, "Payment Successful, awaiting search");
        var paymentInfo = await ValidateAndGetPaymentInfo(application);
        if (paymentInfo.status == "00")
        {
            switch (file.Type)
            {
                case FileTypes.Design:
                    application.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(5));
                    file.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(15));
                    break;
                case FileTypes.Patent:
                    var fPriority = file.FirstPriorityInfo.FirstOrDefault();
                    var priority = file.PriorityInfo.FirstOrDefault();
                    if (file.PatentType == PatentTypes.PCT || file.PatentType == PatentTypes.Conventional)
                    {
                        if (fPriority != null && DateOnly.TryParse(fPriority.Date, out var priorityDate))
                        {
                            application.ExpiryDate = priorityDate.AddYears(1);
                        }
                        else if (priority != null && DateOnly.TryParse(priority.Date, out var priorityDate2))
                        {
                            application.ExpiryDate = priorityDate2.AddYears(1);
                        }
                        else
                        {
                            application.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(20));
                        }
                    }
                    else if (file.PatentType == PatentTypes.Non_Conventional)
                    {
                        application.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(10));
                    }
                    file.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(20));
                    break;
                case FileTypes.TradeMark:
                    application.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(7));
                    break;
            }
        }

        SavePayment(paymentInfo, PaymentTypes.NewCreation, file.FileId, application.id);

    }
    public async Task<bool> ExaminePatentDesign(string fileId, string userId, ApplicationStatuses status)
    {
        try
        {
            var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("File not found.");

            var user = await _userCollection.Find(x => x.Id == userId).FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("User not found.");

            var canTreat = user.UserRoles.Contains(Roles.PatentCertification) ||
                   user.UserRoles.Contains(Roles.DesignCertification) ||
                   user.UserRoles.Contains(Roles.SuperAdmin);
            var isPatent = file.Type == FileTypes.Patent;
            if (!canTreat)
                throw new UnauthorizedAccessException("User is not authorized to paerform this action.");

            // Validate application history exists
            if (file.ApplicationHistory == null || file.ApplicationHistory.Count == 0)
                throw new InvalidOperationException("No application history found for this file.");

            // Prepare updates
            var userName = $"{user.FirstName} {user.LastName}".Trim();
            ApplicationHistory statusHistory = null;
            PerformanceDto performance = null;
            if (status is ApplicationStatuses.AwaitingCertificateConfirmation)
            {
                statusHistory = new ApplicationHistory
                {
                    beforeStatus = ApplicationStatuses.AwaitingExaminer,
                    afterStatus = ApplicationStatuses.AwaitingCertificateConfirmation,
                    Date = DateTime.Now,
                    Message = "Examination completed, awaiting certificate confirmation",
                    User = userName,
                    UserId = user.Id
                };
                performance = new PerformanceDto
                {
                    AfterStatus = statusHistory.afterStatus,
                    BeforeStatus = statusHistory.beforeStatus,
                    AppUserId = userId,
                    ApplicationType = FormApplicationTypes.NewApplication,
                    Date = DateTime.Now,
                    FileNumber = fileId,
                    FileType = file.Type,
                    Reason = statusHistory.Message,
                    OfficeUnit = isPatent ? Roles.PatentExaminer : Roles.DesignExaminer
                };
            }
            else if (status is ApplicationStatuses.Active)
            {
                statusHistory = new ApplicationHistory
                {
                    beforeStatus = ApplicationStatuses.AwaitingExaminer,
                    afterStatus = ApplicationStatuses.Active,
                    Date = DateTime.Now,
                    Message = "Certified, file is now Active",
                    User = userName,
                    UserId = user.Id
                };

                performance = new PerformanceDto
                {
                    AfterStatus = statusHistory.afterStatus,
                    BeforeStatus = statusHistory.beforeStatus,
                    AppUserId = userId,
                    ApplicationType = FormApplicationTypes.NewApplication,
                    Date = DateTime.Now,
                    FileNumber = fileId,
                    FileType = file.Type,
                    Reason = statusHistory.Message,
                    OfficeUnit = isPatent ? Roles.PatentCertification : Roles.DesignCertification
                };
            }

            // Apply updates to database
            var updates = Builders<Filling>.Update.Combine([
                Builders<Filling>.Update.Set(f => f.FileStatus, status),
                Builders<Filling>.Update.Push("ApplicationHistory.0.StatusHistory", statusHistory),
                Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", status)
            ]);

            var result = await _fillingCollection.FindOneAndUpdateAsync(
                Builders<Filling>.Filter.Eq(x => x.FileId, fileId),
                updates,
                new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After }
            );

            SavePerformance(performance);
            return true;
        }
        catch (Exception)
        {
            throw;
        }
    }
    private async Task ProcessLicenseRenewal(Filling file, ApplicationInfo application, DateTime paymentDate, string? userName, string? userId)
    {
        _log.LogInformation("Processing license renewal for FileId {FileId}", file.FileId);
        var isTrademark = file.Type == FileTypes.TradeMark;
        var firstRenewal = !file.ApplicationHistory
            .Any(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal
                    && a.CurrentStatus == ApplicationStatuses.Approved);
        var paymentInfo = await ValidateAndGetPaymentInfo(application);

        file.FileStatus = ApplicationStatuses.Active;
        file.ApplicationHistory[0].CurrentStatus = ApplicationStatuses.Active;
        

        AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, isTrademark ? ApplicationStatuses.AutoApproved : ApplicationStatuses.AwaitingApproval,
            paymentDate, userName, userId, "Payment Successful");

        application.ApplicationDate = paymentDate;
        application.CurrentStatus = isTrademark ? ApplicationStatuses.AutoApproved : ApplicationStatuses.AwaitingRenewalConfirmation;

        switch (file.Type)
        {
            case FileTypes.TradeMark:
                application.ExpiryDate = firstRenewal ? DateOnly.FromDateTime(paymentDate.AddYears(7)) : DateOnly.FromDateTime(paymentDate.AddYears(14));
                //Signature for Certificate
                var signature = await _signatures.Find(a => a.Designation == "recordalSignatory" && a.IsActive == true).FirstOrDefaultAsync();
                application.SignatoryName = signature.Name;
                application.SignatureId = signature.Id;
                break;
            case FileTypes.Patent:
                application.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(1));
                break;
            case FileTypes.Design:
                application.ExpiryDate = DateOnly.FromDateTime(paymentDate.AddYears(5));
                break;
        }

        SavePayment(paymentInfo, PaymentTypes.LicenseRenew, file.FileId, application.id);
    }
    private async Task ProcessChangeData(Filling file, ApplicationInfo application, DateTime paymentDate, string? userName, string? userId)
    {
        var approved = await ApproveChangeDataRecordal(new TreatRecordalDto
        {
            appId = application.id,
            reason = "Auto approved",
            fileId = file.FileId,
            userId = userId
        });

        if (!approved)
            throw new NullReferenceException("Failed to apply recordal");

        AddStatusHistory(application, ApplicationStatuses.AwaitingPayment, ApplicationStatuses.AutoApproved,
            paymentDate, userName, userId, "Payment Successful, auto approved.");

        var paymentInfo = await ValidateAndGetPaymentInfo(application);
        SavePayment(paymentInfo, PaymentTypes.NewCreation, file.FileId, application.id);
    }

    private async Task ProcessClericalUpdate(Filling file, ApplicationInfo application, DateTime paymentDate, string? userName, string? userId)
    {
        _log.LogInformation("Processing clerical update for FileId {FileId}, AppId {AppId}", file.FileId, application.id);
        var applied = await ApplyClericalUpdateToFile(file.FileId, application.id);

        if (!applied)
            throw new Exception("Failed to save clerical update");
        var statusEntry = new ApplicationHistory
        {
            beforeStatus = ApplicationStatuses.AwaitingPayment,
            afterStatus = ApplicationStatuses.AutoApproved,
            Date = paymentDate,
            Message = "Payment Successful, auto approved.",
            User = userName,
            UserId = userId
        };

        var filter = Builders<Filling>.Filter.And(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, a => a.id == application.id));

        await _fillingCollection.UpdateOneAsync(filter,
            Builders<Filling>.Update.Combine(
                Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory", statusEntry),
                Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", ApplicationStatuses.AutoApproved)));

        var paymentInfo = await ValidateAndGetPaymentInfo(application);

        SavePayment(paymentInfo, PaymentTypes.ClericalUpdate, file.FileId, application.id);
        _log.LogInformation("Completed clerical update for FileId {FileId}, AppId {AppId}", file.FileId, application.id);
    }

    private void AddStatusHistory(ApplicationInfo application, ApplicationStatuses beforeStatus, ApplicationStatuses afterStatus,
        DateTime date, string? userName, string? userId, string message)
    {
        application.StatusHistory.Add(new ApplicationHistory
        {
            beforeStatus = beforeStatus,
            afterStatus = afterStatus,
            Date = date,
            Message = message,
            User = userName,
            UserId = userId
        });
        application.CurrentStatus = afterStatus;
    }

    private async Task<string> GenerateNewFileId(Filling file)
    {
        _log.LogDebug("Generating new file number for {FileType}", file.Type);
        var segments = (file.FileId ?? string.Empty).Split('/');
        var max = Math.Max(segments.Length - 1, 0);

        var counter = await _countersCollection
                          .Find(Builders<Counters>.Filter.Eq("_id", file.Type))
                          .FirstOrDefaultAsync()
                      ?? throw new Exception("Counter not found for file type.");

        var newId = string.Join("/", segments.Take(max).Concat(new[] { counter.currentNumber.ToString() }));
        _log.LogInformation("Generated file number {NewFileId} for {FileType}", newId, file.Type);
        var counterFilter = Builders<Counters>.Filter.Eq("_id", file.Type);
        await _countersCollection.FindOneAndUpdateAsync(counterFilter, Builders<Counters>.Update.Inc(f => f.currentNumber, 1));

        return newId;
    }

    private string GetPaymentTypeDescription(FormApplicationTypes applicationType) => applicationType switch
    {
        FormApplicationTypes.NewApplication => "New Application",
        FormApplicationTypes.LicenseRenewal => "License Renewal",
        FormApplicationTypes.DataUpdate => "Data Update",
        FormApplicationTypes.Assignment => "Assignment",
        _ => "Application"
    };

    private string GetRecordReason(FileTypes fileType, FormApplicationTypes applicationType) => applicationType switch
    {
        FormApplicationTypes.NewApplication => $"New {fileType} Application",
        FormApplicationTypes.LicenseRenewal => $"{fileType} Renewal Application",
        FormApplicationTypes.DataUpdate => "Data Update Application",
        FormApplicationTypes.Assignment => $"{fileType} Assignment Application",
        _ => "Application"
    };

    public async Task GenerateRandom()
    {
        var random = _fillingCollection.Find(x => x.Type == FileTypes.Design && x.FileStatus == ApplicationStatuses.Active)
            .FirstOrDefault();
        await SaveCertificate(random, "", "");
    }

    public async Task BulkAddition(List<Filling> files)
    {
        await _fillingCollection.InsertManyAsync(files);
    }

    public async Task DownloadAllPayments()
    {
        var nullIDS = await _fillingCollection.Find(x => x.ApplicationHistory[0].PaymentId == null).ToListAsync();
    }

    public async Task<bool> DeleteFileAsync(string id)
    {
        var deletedDoc = await _fillingCollection.FindOneAndDeleteAsync(x => x.Id == id);
        if (deletedDoc == null)
        {
            return false;
        }

        return true;
    }

    public async Task<dynamic> GetRevisioncost(GetRevisionCost data)
    {
        var cost = _remitaPaymentUtils.GetCost(PaymentTypes.Update, data.type, "", null, null, data.fieldToChange);
        return new
        {
            cost = cost.Item1
        };
    }

    public async Task ReIssueReceiptAndAck()
    {
        var none = new List<string>()
        {
            "897f1d9a-3697-41b7-9a07-aebe13b3f72a",
            "8297c20d-3059-4472-a288-7d17405eec52",
            "22953060-3c34-42da-848b-60607353691f",
            "35be7c99-e139-4cbd-a94e-e67a977743bd",
            "a9da6631-981f-44ad-b605-0410e75cfeba"
        };

        foreach (var item in none)
        {
            var res = await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.Id, item)).Limit(1)
                .ToListAsync();
            var dd = res[0];
            var url = await SaveAcknowledgement(dd);
            Console.WriteLine(url);
            break;
        }
    }

    public async Task<dynamic> GetNewAppCostFromRemita(string rrr)
    {
        const string merchantId = "6230040240";
        const string apiKey = "192753";
        var test = rrr + apiKey + merchantId;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var hash = Convert.ToHexString(apiHash).ToLower();
        var transactionStatusUrl =
            $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{rrr}/{hash}/status.reg";
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");
        var response = await client.SendAsync(request);
        var dataMod = await response.Content.ReadAsStringAsync();
        Console.WriteLine(dataMod);
        var obj = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod);
        Console.WriteLine(dataMod);
        return new
        {
            cost = obj.amount
        };
    }

    // if is new application, and status is awaiting payment, awaiting search, formalityfail. allow change,
    // if status is formalityfail, change to awaiting search after making the changes
    // if it is recordal update, add it.
    private async Task<Filling> updateFileToSearch(string userName, string userId, string fileId, string applicationId)
    {
        var newStatusHistory = new ApplicationHistory()
        {
            beforeStatus = ApplicationStatuses.FormalityFail,
            afterStatus = ApplicationStatuses.AwaitingSearch,
            Date = DateTime.Now,
            Message = "user updated data, awaiting search",
            User = userName,
            UserId = userId
        };
        var filter = Builders<Filling>.Filter.And(Builders<Filling>.Filter.Eq("_id", fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == applicationId));
        List<UpdateDefinition<Filling>> operations = [];
        operations.Add(Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory",
            newStatusHistory));
        operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", ApplicationStatuses.AwaitingSearch));
        operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, ApplicationStatuses.AwaitingSearch));
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        var result = await _fillingCollection.FindOneAndUpdateAsync<Filling>(filter, Builders<Filling>.Update.Combine(operations), options);
        return result;
    }

    public async Task<Filling> FreeDataUpdateAsync(DataUpdateReq revision)
    {
        // if coming from formality fail, move to awaiting search
        Console.WriteLine(revision);
        var filters = new List<FilterDefinition<Filling>>()
        {
            Builders<Filling>.Filter.Eq("_id", revision.fileId),
        };
        List<UpdateDefinition<Filling>> operations = [];
        if (ConstantValues.IsPropertyAttachment(revision.fieldToChange))
        {
            var newAtt = JsonSerializer.Deserialize<List<AttachmentType>>(revision.newValue);
            operations.Add(Builders<Filling>.Update.Set($"Attachments", newAtt));
        }
        else
        {
            var uppercaseField = revision.fieldToChange;
            var fieldToChange = uppercaseField.Substring(0, 1).ToUpper() + uppercaseField.Substring(1);
            var mapresult = FileUtils.MapObjToType(revision.fieldToChange, revision.newValue);
            if (fieldToChange == "PatentType")
            {
                // we need the current file ID so we can set it. if it
                var strings = revision.FileNumber.Split("/");
                if (strings.Length is 5 or 6)
                {
                    if (strings[2] is "NC" or "PCT" or "C")
                    {
                        if (mapresult.ToString() == PatentTypes.PCT.ToString())
                        {
                            strings[2] = "PCT";
                        }
                        else if (mapresult.ToString() == PatentTypes.Conventional.ToString())
                        {

                            strings[2] = "C";
                        }
                        else if (mapresult.ToString() == PatentTypes.Non_Conventional.ToString())
                        {
                            strings[2] = "NC";
                        }
                    }
                    var newFileNumber = string.Join("/", strings);
                    operations.Add(Builders<Filling>.Update.Set(x => x.FileId, newFileNumber));
                }
            }
            if (fieldToChange == "Applicants")
            {
                fieldToChange = "applicants";
            }
            operations.Add(Builders<Filling>.Update.Set(fieldToChange, mapresult));
        }

        var autoDataUpdate = Builders<Filling>.Update.Push(f => f.ApplicationHistory, new ApplicationInfo()
        {
            id = revision.revisionId,
            CurrentStatus = ApplicationStatuses.AutoApproved,
            ApplicationType = FormApplicationTypes.DataUpdate,
            ApplicationDate = DateTime.Now,
            ExpiryDate = null,
            PaymentId = null,
            NewValue = revision.newValue,
            OldValue = revision.oldValue,
            FieldToChange = revision.fieldToChange,
            StatusHistory = [new ApplicationHistory()
            {
                Date = DateTime.Now,
                afterStatus = ApplicationStatuses.AutoApproved,
                beforeStatus = ApplicationStatuses.None,
                Message = $"Automatically approved data update for field -{revision.fieldToChange}-",
                User = revision.user,
                UserId = revision.userId
            } ]
        });
        operations.Add(autoDataUpdate);
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        var result = await _fillingCollection.FindOneAndUpdateAsync<Filling>(Builders<Filling>.Filter.And(filters), Builders<Filling>.Update.Combine(operations), options);
        if (result.ApplicationHistory
                .Where(x => (x.ApplicationType is FormApplicationTypes.NewApplication or FormApplicationTypes.LicenseRenewal)
                            && x.CurrentStatus is ApplicationStatuses.FormalityFail).ToList().Count > 0)
        {
            var id = result.ApplicationHistory.FirstOrDefault(x =>
                x.CurrentStatus is ApplicationStatuses.FormalityFail &&
                x.ApplicationType is FormApplicationTypes.NewApplication or FormApplicationTypes.LicenseRenewal)
                ?.id;
            return await updateFileToSearch(revision.user, revision.userId, result.Id, id!);
        }
        else { return result; }
    }

    public async Task<dynamic> UpdateCost(UpdateReq req)
    {
        var data = _remitaPaymentUtils.GetCost(PaymentTypes.Update, req.fileType, "", null, null, req.patentChangeType);
        var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
            data.Item1, data.Item3, data.Item2, "Data update Application",
            req.name, req.email, req.number);
        return new { cost = data.Item1, rrr = paymentId };
    }

    public async Task<SearchRes?> SearchForFile(string userID, string fileNumber)
    {
        var res = await _fillingCollection.Find(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x => x.FileId, fileNumber),
            Builders<Filling>.Filter.Eq(x => x.CreatorAccount, userID),
        ]), new FindOptions()
        {
            Collation = new Collation("en_US", strength: new Optional<CollationStrength?>(CollationStrength.Primary))
        }).Project(x => new SearchRes()
        {
            FileStatus = x.FileStatus,
            Id = x.Id
        }).FirstOrDefaultAsync();
        _log.LogDebug("Search result for FileNumber {FileNumber}: {Result}", fileNumber, JsonSerializer.Serialize(res));
        return res;
    }
    public async Task<List<string>> LoadListOfIds(int startingIndex,
        SummaryRequestObj filter)
    {
        var filters = getFilter(filter);
        var ids = await _fillingCollection.Find(filters).Project(x => x.Id).Skip(startingIndex).Limit(10).ToListAsync();
        return ids;
    }
    public async Task<dynamic?> SearchForRenewal(string? userId, string fileNumber)
    {
        var res = await _fillingCollection.Find(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x => x.FileId, fileNumber),
           userId==""?  Builders<Filling>.Filter.Empty: Builders<Filling>.Filter.Eq(x => x.CreatorAccount, userId),
        ]), new FindOptions()
        {
            Collation = new Collation("en_US", strength: new Optional<CollationStrength?>(CollationStrength.Primary))
        }).Project(x => new RenewSearchRes
        {
            FileStatus = x.FileStatus,
            Id = x.Id,
            title = x.Type == FileTypes.Design ? x.TitleOfDesign : x.Type == FileTypes.Patent ? x.TitleOfInvention : x.TitleOfTradeMark,
            fileType = x.Type,
            designType = x.DesignType,
            amount = "",
            applicants = x.applicants.Count > 1 ? x.applicants[0].Name + ".et al" : x.applicants[0].Name
        }).FirstOrDefaultAsync();
        if (res == null)
        {
            return new SearchRes()
            {
                Id = null,
                FileStatus = null
            };
        }
        else
        {
            var amount = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, res.fileType, "", res.designType, null, null);
            res.amount = amount.Item1;
        }
        return res;
    }

    public async Task<Filling> UpdateToAwaitingSearch(ManualPaymentConfirmation data)
    {
        //TODO: print acknowledgement, print receipt
        var newStatusHistory = new ApplicationHistory()
        {
            beforeStatus = ApplicationStatuses.AwaitingPayment,
            afterStatus = ApplicationStatuses.AwaitingSearch,
            Date = DateTime.Now,
            Message = "payment confirmed, awaiting search",
            User = data.userName,
            UserId = data.userID
        };
        var filter = Builders<Filling>.Filter.And(Builders<Filling>.Filter.Eq("_id", data.fileId),
        Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == data.applicationId));
        List<UpdateDefinition<Filling>> operations = [];
        operations.Add(Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory",
            newStatusHistory));
        operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", ApplicationStatuses.AwaitingSearch));
        if (data.applicationType is FormApplicationTypes.NewApplication or FormApplicationTypes.LicenseRenewal)
        {
            operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, ApplicationStatuses.AwaitingSearch));
        }
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        var result = await _fillingCollection.FindOneAndUpdateAsync<Filling>(filter, Builders<Filling>.Update.Combine(operations), options);

        return result;
    }

    public async Task<Filling> SaveDateUpdateApplication(DataUpdateReq data)
    {
        var costData = _remitaPaymentUtils.GetCost(PaymentTypes.Update, data.fileType, "", null, null, data.fieldToChange);
        _log.LogDebug("Cost data for update: {CostData}", costData);
        _log.LogDebug("Data update request for FileId: {FileId}", data.fileId);
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(costData.Item1, costData.Item3, costData.Item2,
            $"Payment for data update application",
            data.applicantName, data.email, data.phone);
        _log.LogDebug("Generated RRR: {Rrr}", rrr);
        List<UpdateDefinition<Filling>> operations = [];
        var newApp = new ApplicationInfo()
        {
            id = data.revisionId,
            ApplicationDate = DateTime.Now,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            ApplicationType = FormApplicationTypes.DataUpdate,
            OldValue = data.oldValue,
            NewValue = data.newValue,
            PaymentId = rrr,
            FieldToChange = data.fieldToChange,
            StatusHistory =
            [
                new ApplicationHistory()
                {
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.AwaitingPayment,
                    Message = "Data update saved, awaiting payment",
                    Date = DateTime.Now,
                    User = data.user,
                    UserId = data.userId
                }
            ]
        };
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        operations.Add(Builders<Filling>.Update.Push(x => x.ApplicationHistory, newApp));
        var res = await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, data.fileId),
            Builders<Filling>.Update.Combine(operations), options);
        return res;
    }

    public async Task ValidatePayment()
    {
        var allpending = await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.FileStatus, ApplicationStatuses.AwaitingPayment)).ToListAsync();
        foreach (var pending in allpending)
        {
            var rrr = pending.ApplicationHistory[0].PaymentId;
            // validate rrr
            var status = await ValidationRRR(rrr);
            if (status.Item1)
            {
                // if (pending.Id != "b6116cbc-da10-4951-96aa-5b2981c30a72")
                // {
                _log.LogInformation("Validating payment for FileId: {FileId}", pending.Id);
                // update field
                await UpdateApplicationStatus(new UpdateDataType()
                {
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    AfterStatus = ApplicationStatuses.AwaitingSearch,
                    userId = pending.CreatorAccount,
                    applicationId = pending.ApplicationHistory[0].id,
                    orderID = rrr,
                    amount = status.Item2.ToString(),
                    paymentId = rrr,
                    message = "Payment Successful, awaiting search",
                    applicantName = pending.applicants.Count > 1
                        ? pending.applicants[0].Name + ". et al"
                        : pending.applicants[0].Name,
                    fileId = pending.Id,
                    title = pending.Type == FileTypes.Design ? "New Design Application" : "New Patent Application",
                    FileType = pending.Type,
                    user = "admin",
                    applicationType = FormApplicationTypes.NewApplication,
                });
            }
        }
    }

    public async Task<Filling> CreateFileRenewal(UpdateDataType data)
    {
        var remitaResponse = await CheckStatusViaOrderId(data.paymentId);
        var applicationId = Guid.NewGuid().ToString();
        var app = new ApplicationInfo()
        {
            ApplicationDate = DateTime.Now,
            CurrentStatus = ApplicationStatuses.AwaitingSearch,
            ExpiryDate = null,
            ApplicationType = FormApplicationTypes.LicenseRenewal,
            ApplicationLetters = new List<ApplicationLetters>() { ApplicationLetters.RenewalReceipt, ApplicationLetters.RenewalAck },
            PaymentId = data.paymentId,
            id = applicationId,
            LicenseType = "renewal",
            StatusHistory = new List<ApplicationHistory>()
            {
                new ApplicationHistory()
                {
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = ApplicationStatuses.AwaitingSearch,
                    Date = DateTime.Now,
                    Message = "Renewal Payment confirmed, awaiting search",
                    User = data.user,
                    UserId = data.userId
                }
            }
        };
        List<UpdateDefinition<Filling>> operations =
        [
            Builders<Filling>.Update.Push(x=>x.ApplicationHistory, app),
        ];
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        var result = await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.
            Eq(x => x.Id, data.fileId), Builders<Filling>.Update.Combine(operations), options);
        saveFinance(remitaResponse.Item2, $"{result.Type.ToString()} Renewal Application", applicationId, data.fileId,
            result.applicants[0].country, result.Type, result.DesignType, result.PatentType, result.TrademarkType, result.TrademarkClass,
            rrr: data.paymentId);
        return result;
    }

    private DateOnly getNewExpiryDate(List<DateOnly?> allPreviousDates, FileTypes fileType, string fileId, FormApplicationTypes appType)
    {
        DateOnly furthestDate;
        furthestDate = allPreviousDates?.Where(x => x != null).Max() ?? DateOnly.FromDateTime(DateTime.Now);
        if (fileType is FileTypes.Patent)
        {
            if (appType == FormApplicationTypes.NewApplication)
            {
                var file = _fillingCollection.Find(x => x.Id == fileId).FirstOrDefault();
                if (file.PatentType is PatentTypes.Conventional or PatentTypes.PCT)
                {
                    if (file.PriorityInfo.Count > 0)
                    {
                        var validDates = file.PriorityInfo
                            .Select(x => x.Date)
                            .Where(date => !string.IsNullOrWhiteSpace(date))
                            .Select(date => DateOnly.Parse(date))
                            .ToList();

                        if (validDates.Count > 0)
                        {
                            furthestDate = validDates.Min();
                        }
                    }
                }
            }
            return furthestDate.AddYears(1);
        }

        if (fileType is FileTypes.Design)
        {
            return furthestDate.AddYears(5);
        }

        if (fileType is FileTypes.TradeMark)
        {
            var newDate = allPreviousDates?.Where(x => x != null).Max() == null ?
                DateOnly.FromDateTime(DateTime.Now).AddYears(7) : allPreviousDates
                ?.Where(x => x != null).Max().Value.AddYears(14);
            return newDate ?? DateOnly.FromDateTime(DateTime.Now).AddYears(7);
        }
        return furthestDate;
    }

    //public async Task<Filling?> NewApplicationPayment(UpdateDataType data)
    //{
    //    RemitaResponseClass? response = null;
    //    if (data.simulate == false)
    //    {
    //        var status = true;
    //        var checker_data = await CheckStatusViaOrderId(data.paymentId);
    //        status = checker_data.Item1;
    //        response = checker_data.Item2;
    //        if (!status) return null;
    //    }

    //    var fil = (await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.Id, data.fileId)).Limit(1)
    //        .ToListAsync()).First();
    //    if (fil.ApplicationHistory[0].ApplicationLetters.Contains(ApplicationLetters.NewApplicationReceipt))
    //    {
    //        return fil;
    //    }

    //    var newStatusHistory = new ApplicationHistory()
    //    {
    //        beforeStatus = data.beforeStatus,
    //        afterStatus = data.AfterStatus,
    //        Date = DateTime.Now,
    //        Message = data.message,
    //        User = data.user,
    //        UserId = data.userId
    //    };
    //    List<UpdateDefinition<Filling>> operations =
    //    [
    //        Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory",
    //            newStatusHistory),

    //        Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", data.AfterStatus)
    //    ];
    //    operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, data.AfterStatus));
    //    var document = await _countersCollection.Find(Builders<Counters>.Filter.Eq("_id", data.FileType))
    //        .FirstOrDefaultAsync();
    //    var strings = fil.FileId.Split("/");
    //    var max = strings.Length - 1;
    //    var newId = string.Join("/", strings.Take(max).Concat(new[] { document.currentNumber.ToString() }));
    //    operations.Add(Builders<Filling>.Update.Set(x => x.FileId, newId));
    //    var counterfilter = Builders<Counters>.Filter.Eq("_id", fil.Type);
    //    _countersCollection.FindOneAndUpdate(counterfilter, Builders<Counters>.Update.Inc(f => f.currentNumber, 1));
    //    fil.FileId = newId;
    //    operations.Add(Builders<Filling>.Update.AddToSetEach(x => x.ApplicationHistory[0].ApplicationLetters,
    //        [ApplicationLetters.NewApplicationReceipt, ApplicationLetters.NewApplicationAcknowledgement]));
    //    var filter = Builders<Filling>.Filter.And(Builders<Filling>.Filter.Eq("_id", data.fileId),
    //        Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == data.applicationId));
    //    var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
    //    var result =
    //        await _fillingCollection.FindOneAndUpdateAsync(filter, Builders<Filling>.Update.Combine(operations),
    //            options);
    //    saveFinance(response, $"New {fil.Type.ToString()} Application", fil.ApplicationHistory[0].id,
    //        fil.Id, fil.applicants[0].country, fil.Type, fil.DesignType, fil.PatentType, fil.TrademarkType, fil.TrademarkClass, null
    //        );
    //    SavePerformance(PerformanceType.Application, FormApplicationTypes.NewApplication, null, null,
    //        DateTime.Now, data.user, result.Id, result.Type, result.PatentType, result.DesignType, result.TrademarkType);

    //    return result;
    //}

    private void saveFinance(
        RemitaResponseClass? response, string? reason, string? applicationId, string? fileId, string? country = null,
        FileTypes? type = null,
        DesignTypes? designTypes = null, PatentTypes? patentType = null, TradeMarkType? markType = null,
        int? markclass = null, string? rrr = null
    )
    {
        _financeCollection.InsertOne(new FinanceHistory()
        {
            remitaResonse = response,
            date = DateTime.Parse(response.paymentDate),
            total = response.amount ?? 0,
            ministryFee = response.lineItems[0].beneficiaryAmount,
            techFee = response.lineItems[1].beneficiaryAmount,
            reason = reason,
            applicationID = applicationId,
            fileId = fileId,
            country = country,
            Type = type,
            DesignType = designTypes,
            PatentType = patentType,
            TradeMarkType = markType,
            TradeMarkClass = markclass
        });
    }

    public async Task<Filling?> UpdateApplicationStatus(UpdateDataType data)
    {
        try
        {
            _log.LogInformation("UpdateApplicationStatus started for FileId {FileId}, AppId {AppId}, {Before} → {After}",
                data.fileId, data.applicationId, data.beforeStatus, data.AfterStatus);

            if (string.IsNullOrWhiteSpace(data.fileId) || string.IsNullOrWhiteSpace(data.applicationId))
            {
                _log.LogWarning("UpdateApplicationStatus aborted due to invalid identifiers. FileId: {FileId}, AppId: {AppId}",
                    data.fileId, data.applicationId);
                return null;
            }

            var userName = await ResolveUserNameAsync(data);
            var paymentOk = await ValidatePaymentIfRequiredAsync(data);
            if (!paymentOk) return null;

            var now = DateTime.Now;
            var newStatusHistory = new ApplicationHistory
            {
                beforeStatus = data.beforeStatus,
                afterStatus = data.AfterStatus,
                Date = now,
                Message = data.message,
                User = userName,
                UserId = data.userId
            };

            var operations = BuildBaseStatusOperations(data.AfterStatus, newStatusHistory);

            var perf = new PerformanceDto
            {
                ApplicationId = data.applicationId,
                Reason = data.message,
                AfterStatus = data.AfterStatus,
                BeforeStatus = data.beforeStatus,
                Date = now,
                ApplicationType = data.applicationType,
                AppUserId = data.userId,
                FileNumber = data.fileNumber,
                FileType = data.FileType,
                OfficeUnit = null
            };

            await ApplyNewApplicationStatusUpdatesAsync(data, userName, operations, perf);
            var result = await ApplyApplicationStatusUpdateAsync(data, operations);

            if (result == null)
            {
                _log.LogWarning("No file/application matched update filter for FileId {FileId}, AppId {AppId}",
                    data.fileId, data.applicationId);
                return null;
            }

            await SyncCertificationStatusAsync(data, result, newStatusHistory);

            SavePerformance(perf);
            _log.LogInformation("UpdateApplicationStatus completed for FileId {FileId}, AppId {AppId}, NewStatus {Status}",
                data.fileId, data.applicationId, data.AfterStatus);
            var fileOwner = await GetFileOwner(data.fileNumber ?? data.fileId);

            var notif = new CreateNotificationDto
            {
                Audience = NotificationAudience.User,
                Category = NotificationCategory.StatusUpdate,
                Priority = NotificationPriority.Medium,
                PreviousStatus = data.beforeStatus,
                NewStatus = data.AfterStatus,
                ApplicationType = data.applicationType,
                Title = "Application Status Update",
                Message = $"Your {data.applicationType} status has been updated from {data.beforeStatus} to {data.AfterStatus}",
                RecipientId = fileOwner,
                CreatedBy = "System",
                FileNumber = data.fileNumber,
                FileType = data.FileType,
                ApplicationId = data.applicationId,
                ActionUrl = $"/dataview/?id={data.fileId}"
            };
            await _notificationServices.CreateNotificationAsync(notif);
            _log.LogInformation($"notification sent to {notif.RecipientId} ");
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "UpdateApplicationStatus failed for FileId {FileId}, AppId {AppId}, {Before} → {After}",
                data.fileId, data.applicationId, data.beforeStatus, data.AfterStatus);
            throw;
        }
    }
    private async Task<string> GetFileOwner(string fileNumber)
    {
        var creatorId = await _fillingCollection.Find(x => x.FileId == fileNumber).Project(x => x.CreatorAccount).FirstOrDefaultAsync();
        return await _userCollection.Find(x => x.CreatorId == creatorId).Project(x => x.Email).FirstOrDefaultAsync() ?? "Unknown User";
    }
    private async Task<string> ResolveUserNameAsync(UpdateDataType data)
    {
        return data.user
               ?? await _userCollection.Find(x => x.Id == data.userId).Project(x => x.Name).FirstOrDefaultAsync()
               ?? "Unknown User";
    }

    private static List<UpdateDefinition<Filling>> BuildBaseStatusOperations(ApplicationStatuses afterStatus,
        ApplicationHistory history)
    {
        return
        [
            Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory", history),
            Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", afterStatus)
        ];
    }

    private bool RequiresPaymentValidation(UpdateDataType data)
    {
        return !data.simulate &&
               data is
               {
                   AfterStatus: ApplicationStatuses.AwaitingSearch,
                   beforeStatus: ApplicationStatuses.AwaitingPayment,
                   applicationType: FormApplicationTypes.NewApplication
               };
    }

    private async Task<bool> ValidatePaymentIfRequiredAsync(UpdateDataType data)
    {
        if (!RequiresPaymentValidation(data)) return true;

        _log.LogDebug("Validating payment for FileId {FileId}, AppId {AppId}, RRR {Rrr}",
            data.fileId, data.applicationId, data.paymentId);
        var (status, _) = await CheckStatusViaOrderId(data.paymentId);
        if (status) return true;

        _log.LogWarning("Payment validation failed for FileId {FileId}, AppId {AppId}, RRR {Rrr}",
            data.fileId, data.applicationId, data.paymentId);
        return false;
    }

    private async Task ApplyNewApplicationStatusUpdatesAsync(UpdateDataType data, string userName,
        List<UpdateDefinition<Filling>> operations, PerformanceDto perf)
    {
        if (data.applicationType is FormApplicationTypes.NewApplication or FormApplicationTypes.LicenseRenewal)
        {
            if (data.AfterStatus is not ApplicationStatuses.Published)
            {
                operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, data.AfterStatus));
            }
        }

        if (data.applicationType is not FormApplicationTypes.NewApplication) return;

        if (data.AfterStatus is ApplicationStatuses.Active)
        {
            var nextDate = getNewExpiryDate(data.dates, data.FileType ?? FileTypes.Design, data.fileId,
                FormApplicationTypes.NewApplication);
            _log.LogDebug("Calculated expiry date {ExpiryDate} for FileId {FileId}", nextDate, data.fileId);

            if (data.FileType != FileTypes.TradeMark)
            {
                operations.Add(Builders<Filling>.Update.AddToSetEach(
                    "ApplicationHistory.$.ApplicationLetters",
                    [ApplicationLetters.NewApplicationAcceptance, ApplicationLetters.NewApplicationCertificate]));
            }

            if (data.FileType is FileTypes.TradeMark)
            {
                var rtmCounter = await _countersCollection.Find(e => e.id == "RTM").FirstOrDefaultAsync();
                if (rtmCounter == null)
                {
                    _log.LogError("RTM counter not found while activating trademark file {FileId}", data.fileId);
                    throw new InvalidOperationException("RTM counter not found");
                }

                _log.LogDebug("Assigning RTM number {RtmNumber} to FileId {FileId}", rtmCounter.currentNumber,
                    data.fileId);
                operations.Add(Builders<Filling>.Update.AddToSetEach(
                    "ApplicationHistory.$.ApplicationLetters",
                    [ApplicationLetters.NewApplicationCertificate]));
                operations.Add(Builders<Filling>.Update.Set(x => x.RtmNumber, rtmCounter.currentNumber.ToString()));

                await _countersCollection.FindOneAndUpdateAsync(e => e.id == "RTM",
                    Builders<Counters>.Update.Inc(f => f.currentNumber, 1));

                var signatory = await SignDocument("trademarkCertificateSignatory");
                if (signatory is null)
                {
                    _log.LogWarning("No active trademark certificate signatory found for FileId {FileId}", data.fileId);
                }
                else
                {
                    operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.SignatureId", signatory.Value.Item2));
                    operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.SignatoryName", signatory.Value.Item1));
                }

                perf.OfficeUnit = Roles.TrademarkCertification;
            }
            else if (data.FileType is FileTypes.Patent)
            {
                perf.OfficeUnit = Roles.PatentCertification;
            }
            else
            {
                perf.OfficeUnit = Roles.DesignCertification;
            }

            operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.ExpiryDate", nextDate));
        }

        if (data.AfterStatus is ApplicationStatuses.RejectedByExaminer or ApplicationStatuses.Rejected)
        {
            _log.LogInformation("Application rejected for FileId {FileId}, Status {Status}", data.fileId, data.AfterStatus);
            operations.Add(Builders<Filling>.Update.Push("ApplicationHistory.$.ApplicationLetters",
                ApplicationLetters.NewApplicationRejection));

            if (data.FileType is FileTypes.TradeMark) perf.OfficeUnit = Roles.TrademarkExaminer;
            else if (data.FileType is FileTypes.Patent) perf.OfficeUnit = Roles.PatentExaminer;
            else perf.OfficeUnit = Roles.DesignExaminer;
        }
        
        if (data.AfterStatus is not ApplicationStatuses.Publication) return;

        var publish = new PublicationDto
        {
            FileNumber = data.fileNumber,
            Comment = data.message,
            StaffId = data.userId,
            StaffName = userName,
        };
        var pubResult = await _publicationServices.SavePublication(publish);
        if (pubResult is null)
        {
            _log.LogError("Failed to save publication data for FileId {FileId}", data.fileId);
            throw new NullReferenceException("Failed to save publication data");
        }

        _log.LogDebug("Publication saved for FileId {FileId} with Id {PublicationId}", data.fileId, pubResult);
        perf.OfficeUnit = Roles.TrademarkExaminer;
    }

    private async Task<Filling?> ApplyApplicationStatusUpdateAsync(UpdateDataType data,
        List<UpdateDefinition<Filling>> operations)
    {
        var filter = Builders<Filling>.Filter.And(
            Builders<Filling>.Filter.Eq("_id", data.fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == data.applicationId));
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        return await _fillingCollection.FindOneAndUpdateAsync(filter, Builders<Filling>.Update.Combine(operations), options);
    }

    private async Task SyncCertificationStatusAsync(UpdateDataType data, Filling result, ApplicationHistory history)
    {
        if (result.ApplicationHistory?.Any(a =>
                a.ApplicationType == FormApplicationTypes.Certification && a.id != data.applicationId) != true) return;

        var certFilter = Builders<Filling>.Filter.And(
            Builders<Filling>.Filter.Eq("_id", data.fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory,
                a => a.ApplicationType == FormApplicationTypes.Certification && a.id != data.applicationId));

        await _fillingCollection.UpdateOneAsync(
            certFilter,Builders<Filling>.Update.Combine(
            Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", data.AfterStatus),
            Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory", history)));
    }

    public async Task<PaginatedResponse> GetPaginatedSummaryAsync(int startingIndex, int quantity, SummaryRequestObj filter)
    {
        _log.LogDebug("GetPaginatedSummary: Index {Index}, Quantity {Qty}, FileTypes {Types}, Status {Status}",
            startingIndex, quantity, filter.types != null ? string.Join(",", filter.types) : "all",
            filter.status != null ? string.Join(",", filter.status) : "all");

        var filters = getFilter(filter);
        var fillBuilder = Builders<Filling>.Projection;
        var projection = fillBuilder.Expression(x => new FileSummary()
        {
            FileId = x.FileId,
            title = x.Type == FileTypes.Patent ? x.TitleOfInvention : x.Type == FileTypes.Design ? x.TitleOfDesign : x.TitleOfTradeMark,
            fileStatus = x.FileStatus,
            Summaries = x.ApplicationHistory.Select(y => new FileApplicationSummary()
            {
                applicationDate = y.ApplicationDate,
                ApplicationType = y.ApplicationType,
                ApplicationStatus = y.CurrentStatus
            }).ToList(),
            id = x.Id.ToString(),
            Type = x.Type,
            TrademarkClass = x.TrademarkClass,
            PatentType = x.PatentType,
            DesignType = x.DesignType,
            FilingDate = x.FilingDate
        });
        var count = _fillingCollection.CountDocuments(filters);
        var result = await _fillingCollection.Find(filters).Project(projection).Skip(startingIndex).Limit(quantity).ToListAsync();
        _log.LogDebug("GetPaginatedSummary returned {ResultCount} of {TotalCount} records", result.Count, count);

        return new PaginatedResponse()
        {
            result = result,
            count = count
        };
    }

    public async Task<dynamic> GetCertificatePaymentCost(string fileId, string userId)
    {
        _log.LogInformation("GetCertificatePaymentCost for FileId {FileId}, UserId {UserId}", fileId, userId);


        var file = await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.FileId, fileId)).FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogError("File is null");
            throw new KeyNotFoundException("File not found");
        }
        var user = await _userCollection.Find(Builders<AppUser>.Filter.Eq("_id", userId)).FirstOrDefaultAsync();
        if (user == null)
        {
            _log.LogError("User is null");
            throw new KeyNotFoundException("User not found");
        }

        var applicant = file.applicants.FirstOrDefault();
        if (applicant == null)
        {
            _log.LogError("No applicant found");
            throw new KeyNotFoundException("Applicant not found");
        }

        var username = $"{user.FirstName} {user.LastName}";
        var data = _remitaPaymentUtils.GetCost(PaymentTypes.TrademarkCertificate, FileTypes.TradeMark, "");
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
            data.Item1, data.Item3, data.Item2,
            "Application for Certificate",
            applicant.Name, applicant.Email, applicant.Phone);

        if (string.IsNullOrWhiteSpace(rrr))
        {
            _log.LogError("Failed to generate RRR for certificate payment on FileId {FileId}", fileId);
            throw new KeyNotFoundException("Unable to generate payment reference");

        }
        _log.LogDebug("Generated certificate RRR {Rrr} for FileId {FileId}, Amount {Amount}", rrr, fileId, data.Item1);

        var certApp = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationDate = DateTime.Now,
            CurrentStatus = ApplicationStatuses.AwaitingCertification,
            ApplicationType = FormApplicationTypes.Certification,
            PaymentId = rrr,
            CertificatePaymentId = rrr,
            StatusHistory =
            [
                new ApplicationHistory
                {
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.AwaitingCertification,
                    Date = DateTime.Now,
                    Message = "Certificate application initiated, awaiting payment",
                    User = username,
                    UserId = user.Id
                }
            ]
        };

        var filter = Builders<Filling>.Filter.Eq(f => f.FileId, fileId);

        // update certificate id + file status
        await _fillingCollection.UpdateOneAsync(
            filter,
            Builders<Filling>.Update.Combine(
                Builders<Filling>.Update.Set("ApplicationHistory.0.CertificatePaymentId", rrr),
                Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.AwaitingCertification),
                Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.AwaitingCertification)
            )
        );

        // push the new certificate application entry
        await _fillingCollection.UpdateOneAsync(
            filter,
            Builders<Filling>.Update.Push(f => f.ApplicationHistory, certApp) // <-- correct array
        );

        _log.LogInformation("Certificate application created for FileId {FileId}, AppId {AppId}", fileId, certApp.id);
        return new
        {
            rrr,
            total = data.Item1,
            applicant = applicant.Name,
            fileId,
            appId = certApp.id
        };
    }

    public async Task updateApproved()
    {
        var resul = _fillingCollection.AsQueryable().Where(x =>
            x.FileStatus == ApplicationStatuses.Active &&
            x.FileId.Split(separator).Length == 6 &&
            x.ApplicationHistory[0].Letters.Count == 3).ToList();
        Console.WriteLine(resul[0].Id);
        Console.WriteLine(resul[1].Id);
        Console.WriteLine(resul[2].Id);
        return;
        foreach (var fil in resul)
        {
            Console.WriteLine(fil.Id);
            var acceptanceUrl = await SaveAcceptance(fil, "", "");
            await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, fil.Id),
                Builders<Filling>.Update.AddToSetEach(x => x.ApplicationHistory[0].Letters, new List<KeyValuePair<string, List<string>>>()
                {
                    new ("acceptance", [acceptanceUrl])
                }));
        }
    }

    public async Task replaceLetters()
    {
        new List<string>()
        {
            "78f7ce9a-8a06-49e2-9ca1-851edf38d8b1",
        };
        var d = await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.Id, "30bb0888-af39-4af7-9f00-30427fd9f613")).ToListAsync();
        // var d = await _fillingCollection.Find(Builders<Filling>.Filter.Gte(x=>x.DateCreated, DateTime.Today )).ToListAsync();
        List<string> newFiles = [];
        var oldDesign = new List<int>() { };
        var oldPatent = new List<int>() { };
        var newlist = new List<Filling>() { };
        var oldlist = new List<Filling>() { };
        foreach (var id in d)
        {
            // if ( id.FileId.Split("/").Length==7)
            // {
            newFiles.Add(id.FileId);
            newlist.Add(id);
            // }
            // else
            // {
            //     if (id.FileId.Split("/").Count() == 5)
            //     {
            //         oldlist.Add(id);
            //     }
            //     
            // }
        }
        // int patentCounter = 14000;
        // int designCounter = 4000;
        foreach (var file in newlist)
        {
            // if (file.FileStatus is ApplicationStatuses.AwaitingPayment || file.Type is FileTypes.Patent)
            // {
            //     continue;
            //     await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, file.Id),
            //         Builders<Filling>.Update.Set(x => x.FileId, Guid.NewGuid().ToString().Split("-")[0]));
            // }
            // else
            // {

            // reprint and save receipt, ack, acp
            // var strings = file.FileId.Split("/");
            // var newStrings=strings.ToList();newStrings.RemoveAt(4);
            // var newFileId = string.Join("/", newStrings);
            // var newFileId=string.Join("/",strings.Take(strings.Length-1).Concat(new []{designCounter.ToString() }));
            // Console.WriteLine($"old file id: {file.FileId}");
            // file.FileId = newFileId;
            // Console.WriteLine(newFileId);
            // designCounter += 1;
            var applicantName = file.applicants.Count > 1
                ? file.applicants[0].Name + " et al."
                : file.applicants[0].Name;
            var applicantNationality = file.applicants.Count > 1
                ? (file.applicants.Select(x => x.country).ToList().Any(x => x != "Nigeria") ? "foreign" : "Nigeria")
                : file.applicants[0].country;
            var title = file.Type is FileTypes.Design ? file.TitleOfDesign : file.TitleOfInvention;
            foreach (var applicationInfo in file.ApplicationHistory)
            {
                var att = new Dictionary<string, List<string>>() { };
                if (applicationInfo.ApplicationType is FormApplicationTypes.NewApplication)
                {
                    foreach (var (key, urList) in applicationInfo.Letters)
                    {
                        if (key is "receipt" or "Receipt")
                        {
                            var receiptModel = new Receipt()
                            {
                                rrr = applicationInfo.PaymentId,
                                Amount = _remitaPaymentUtils.GetCost(PaymentTypes.NewCreation, file.Type,
                                    applicantNationality, file.DesignType, file.PatentType, null).Item1,
                                Date = applicationInfo.ApplicationDate.ToString(),
                                ApplicantName = applicantName,
                                payType = PaymentTypes.NewCreation,
                                FileId = file.FileId,
                                Title = title,
                                Category = file.Type.ToString(),
                                PaymentFor = $"New {file.Type} Application"
                            };
                            var newReceipt = await SaveReceipt(receiptModel, file);
                            att.Add("receipt", [newReceipt]);
                        }

                        if (key is "Acknowledgement" or "acknowledgement")
                        {
                            var newUrl = await SaveAcknowledgement(file);
                            att.Add("acknowledgement", [newUrl]);

                        }
                        if (key is "acceptance" or "Acceptance")
                        {
                            var newUrl = await SaveAcceptance(file, "", "");
                            att.Add("acceptance", [newUrl]);

                        }

                        if (key is "rejection" or "Rejection")
                        {
                            var newUrl = await SaveRejection(file, "", "");
                            att.Add("rejection", [newUrl]);
                        }
                    }
                }

                if (applicationInfo.ApplicationType is FormApplicationTypes.DataUpdate &&
                    applicationInfo.CurrentStatus != ApplicationStatuses.AwaitingPayment &&
                    applicationInfo.CurrentStatus != ApplicationStatuses.AutoApproved)
                {
                    foreach (var (key, urList) in applicationInfo.Letters)
                    {
                        if (key is "receipt" or "Receipt")
                        {
                            var receiptModel = new Receipt()
                            {
                                rrr = applicationInfo.PaymentId,
                                Amount = _remitaPaymentUtils.GetCost(PaymentTypes.Update, file.Type,
                                    applicantNationality, file.DesignType, file.PatentType,
                                    applicationInfo.FieldToChange).Item1,
                                Date = applicationInfo.ApplicationDate.ToString(),
                                ApplicantName = applicantName,
                                payType = PaymentTypes.Update,
                                FileId = file.FileId,
                                Title = title,
                                Category = file.Type.ToString(),
                                PaymentFor = $"Data update Application"
                            };
                            var newReceipt = await SaveReceipt(receiptModel, file);
                            att.Add("receipt", [newReceipt]);

                        }
                    }
                }

                if (applicationInfo.ApplicationType is FormApplicationTypes.LicenseRenewal)
                {
                    // if renewal, do the same thing, although we have just receipt for now
                    foreach (var (key, urList) in applicationInfo.Letters)
                    {
                        if (key is "receipt" or "Receipt")
                        {
                            var receiptModel = new Receipt()
                            {
                                rrr = applicationInfo.PaymentId,
                                Amount = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, file.Type,
                                    applicantNationality, file.DesignType, file.PatentType, null).Item1,
                                Date = applicationInfo.ApplicationDate.ToString(),
                                ApplicantName = applicantName,
                                payType = PaymentTypes.LicenseRenew,
                                FileId = file.FileId,
                                Title = title,
                                Category = file.Type.ToString(),
                                PaymentFor = $"{file.Type} Renewal Application"
                            };
                            var renewalReceipt = await SaveReceipt(receiptModel, file);
                            att.Add("receipt", [renewalReceipt]);
                        }
                    }
                }

                // replace and save
                var result = await _fillingCollection.FindOneAndUpdateAsync(
                    Builders<Filling>.Filter.And([
                            Builders<Filling>.Filter.Eq(x => x.Id, file.Id),
                                Builders<Filling>.Filter.ElemMatch(x => x.ApplicationHistory,
                                    g => g.id == applicationInfo.id),
                        ]
                    ), Builders<Filling>.Update.Combine([
                        // Builders<Filling>.Update.Set(x=>x.FileId, newFileId),
                        Builders<Filling>.Update.Set("ApplicationHistory.$.Letters", att)
                    ]),
                    new FindOneAndUpdateOptions<Filling>()
                    {
                        ReturnDocument = ReturnDocument.After
                    });
                Console.WriteLine(JsonSerializer.Serialize(result.ApplicationHistory.Select(t => t.Letters)));
            }
        }
    }

    private FilterDefinition<Filling> getFilter(SummaryRequestObj filter)
    {
        var filterBuilder = Builders<Filling>.Filter;
        var nationFilter = filter.applicantCountries == null
            ? filterBuilder.Empty
            : filterBuilder.Or(filter.applicantCountries?.Select(x =>
                Builders<Filling>.Filter.AnyEq(y => y.applicants.Select(z => z.country), x)));
        var statusFilter = filter.status == null
            ? Builders<Filling>.Filter.Empty
            : (filter.status.Count == 1 && filter.status[0] == ApplicationStatuses.Inactive)
                ? filterBuilder.Eq(x => x.FileStatus, ApplicationStatuses.Inactive)
                : filterBuilder.And(
                    Builders<Filling>.Filter.And([
                        Builders<Filling>.Filter.Where(f => f.ApplicationHistory.Any(app =>
                            app.CurrentStatus == filter.status[0] && app.ApplicationType == filter.applicationTypes[0]))
                    ]));
        var creatorFilter = filter.userType == UserTypes.User
            ? filterBuilder.Eq(f => f.CreatorAccount, filter.userId)
            : filterBuilder.Empty;
        var applicationTypes = filter.applicationTypes == null
            ? filterBuilder.Empty
            : filterBuilder.Or(
                filter.applicationTypes.Select(x =>
                    filterBuilder.AnyEq(z => z.ApplicationHistory.Select(y => y.ApplicationType), x))
            );
        var typeFilter = filter.types == null
            ? filterBuilder.Empty
            : filterBuilder.In(f => f.Type, filter.types);
        var titleFilter = filter.Title == null
            ? filterBuilder.Empty
            : filterBuilder.Or([
                filterBuilder.Regex(f => f.TitleOfDesign, new BsonRegularExpression(filter.Title, "i")),
                filterBuilder.Regex(f => f.TitleOfInvention, new BsonRegularExpression(filter.Title, "i")),
                filterBuilder.Regex(f => f.TitleOfTradeMark, new BsonRegularExpression(filter.Title, "i")),
                filterBuilder.Regex(f => f.FileId, new BsonRegularExpression(filter.Title, "i")),
                filterBuilder.Regex(f => f.applicants.Select(x => x.Name), new BsonRegularExpression(filter.Title, "i"))
            ]);
        var startDateFilter = filter.startDate == null
            ? filterBuilder.Empty
            : filterBuilder.Gte(f => f.DateCreated, filter.startDate);
        var endDateFilter = filter.endDate == null
            ? filterBuilder.Empty
            : filterBuilder.Lt(f => f.DateCreated, filter.endDate.Value.AddDays(1));
        var patentTypeFilter = filter.patentTypes == null
            ? filterBuilder.Empty
            : filterBuilder.Where(f => f.PatentType != null && filter.patentTypes.Contains(f.PatentType.Value));
        var designTypeFilter = filter.designTypes == null
            ? filterBuilder.Empty
            : filterBuilder.Where(f => f.DesignType != null && filter.designTypes.Contains(f.DesignType.Value));
        var priorityFilter = filter.PriorityNumber == null
            ? filterBuilder.Empty
            : filterBuilder.Regex(f => f.PriorityInfo.Select(y => y.number), filter.PriorityNumber);
        var filters = typeFilter & startDateFilter & endDateFilter & priorityFilter &
                      statusFilter & designTypeFilter & patentTypeFilter & nationFilter & applicationTypes &
                      creatorFilter &
                      titleFilter;
        return filters;

    }


    // get  all by user
    // get all new applications, awaiting search
    // atomically process new creation
    private string GetContentType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLower();
        switch (extension)
        {
            case ".png":
                return "image/png";
            case ".jpg":
                return "image/jpeg";
            case ".pdf":
                return "application/pdf";
            default:
                return "application/octet-stream";
        }
    }

    public async Task<List<string>> UploadAttachment(List<TT> files)
    {
        var uris = new List<string>() { };
        foreach (var item in files)
        {
            if (item.data != null)
            {
                var extention = item.fileName.Split(".").Last();
                var trustedFileName = Path.GetRandomFileName();
                trustedFileName = trustedFileName.Split(".")[0] + $".{extention}";

                await _attachmentCollection.InsertOneAsync(new AttachmentInfo
                {
                    Id = trustedFileName,
                    ContentType = item.contentType,
                    Data = item.data
                });
                uris.Add(
                    $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}");
            }
        }
        return uris;
    }

    public async Task ProcessNewCreation(Filling newFile, List<TT> attachments)
    {
        _log.LogInformation("Processing new creation for FileId {FileId}, Type {Type}", newFile.FileId, newFile.Type);
        _log.LogDebug("Attachments count: {Count} for FileId {FileId}", attachments.Count, newFile.FileId);
        _log.LogDebug("Additional Description: ", newFile.AdditionalDescription);
        if (newFile.Type is FileTypes.Design)
        {
            var designReps = attachments.Where(x => x.Name is "design1" or "design2" or "design3" or "design4" or "designDrawings").ToList();
            var designUrls = await UploadAttachment(designReps);
            newFile.Attachments.Add(new AttachmentType()
            {
                name = "designs",
                url = designUrls
            });

            var nov = attachments.FirstOrDefault(x => x.Name is "nov" or "novelty" or "noveltyStatement" or "statementOfNovelty");
            if (nov != null)
            {
                var novurl = await UploadAttachment([nov]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "nov",
                    url = novurl
                });
            }

            var form2 = attachments.FirstOrDefault(x => x.Name is "form2" or "poa");
            if (form2 != null)
            {
                var form2url = await UploadAttachment([form2]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "form2",
                    url = form2url
                });
            }

            var priorityDoc = attachments.FirstOrDefault(x => x.Name is "pdoc" or "priorityDocument" or "designPriorityDocument");
            if (priorityDoc != null)
            {
                var priorityDocurl = await UploadAttachment([priorityDoc]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "pdoc",
                    url = priorityDocurl
                });
            }

            var otherDocs = attachments.Where(x => x.Name is "any" or "others").ToList();
            if (otherDocs.Any())
            {
                var otherUrls = await UploadAttachment(otherDocs);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "others",
                    url = otherUrls
                });
            }
        }

        if (newFile.Type is FileTypes.Patent)
        {
            var csReps = attachments.Where(x => x.Name == "cs").ToList();
            if (csReps.Any())
            {
                var csUrls = await UploadAttachment(csReps);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "cs",
                    url = csUrls
                });
            }

            var poaReps = attachments.Where(x => x.Name == "poa").ToList();
            if (poaReps.Any())
            {
                var poaUrls = await UploadAttachment(poaReps);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "poa",
                    url = poaUrls
                });
            }

            var drawingReps = attachments.Where(x => x.Name == "drawings").ToList();
            if (drawingReps.Any())
            {
                var drawingUrls = await UploadAttachment(drawingReps);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "drawings",
                    url = drawingUrls
                });
            }

            var priorityDocs = attachments.Where(x => x.Name == "priorityDocument").ToList();
            if (priorityDocs.Any())
            {
                var priorityDocUrls = await UploadAttachment(priorityDocs);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "priorityDocument",
                    url = priorityDocUrls
                });
            }

            var pctReps = attachments.Where(x => x.Name == "pct").ToList();
            if (pctReps.Any())
            {
                var pctUrls = await UploadAttachment(pctReps);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "pct",
                    url = pctUrls
                });
            }

            var otherDocs = attachments.Where(x => x.Name == "others").ToList();
            if (otherDocs.Any())
            {
                var otherUrls = await UploadAttachment(otherDocs);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "others",
                    url = otherUrls
                });
            }
        }


        if (newFile.Type is FileTypes.TradeMark)
        {
            var representation = attachments.FirstOrDefault(x => x.Name == "representation");
            if (representation != null)
            {
                var repurl = await UploadAttachment([representation]);

                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "representation",
                    url = repurl
                });
            }

            var form2 = attachments.FirstOrDefault(x => x.Name == "form2");
            if (form2 != null)
            {
                var form2url = await UploadAttachment([form2]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "form2",
                    url = form2url
                });
            }

            var other1 = attachments.FirstOrDefault(x => x.Name == "other1");
            if (other1 != null)
            {
                var priorityDocurl = await UploadAttachment([other1]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "other1",
                    url = priorityDocurl
                });
            }

            var other2 = attachments.FirstOrDefault(x => x.Name == "other2");
            if (other2 != null)
            {
                var other2url = await UploadAttachment([other2]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "other2",
                    url = other2url
                });
            }
        }

        var applicationDate = DateTime.Now;
        var applicantNationality = newFile.applicants.Select(x => x.country).Any(y => y.ToLower() != "nigeria") ? "Other" : "nigeria";
        // create fileId,
        var fileId = CreateTempFileNumber(newFile.Type, applicantNationality, newFile.PatentType, newFile.DesignType, newFile.TrademarkType);

        // add license history
        newFile.FileId = fileId;
        var fileStatusId = Guid.NewGuid().ToString();
        var fileHistory = new ApplicationInfo()
        {
            id = fileStatusId,
            ApplicationDate = applicationDate,
            ExpiryDate = null,
            LicenseType = "Fresh",
            ApplicationType = FormApplicationTypes.NewApplication,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            Letters = [],
            StatusHistory =
            [
                new ApplicationHistory()
                {
                    Date = applicationDate,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.AwaitingPayment,
                    Message = "Saved Successfully, awaiting Payment"
                }
            ],
            PaymentId = null
        };
        _log.LogInformation("Created application history for new file with FileId {FileId}, ApplicationId {AppId}", fileId, fileStatusId);
        // add date created
        newFile.DateCreated = applicationDate;
        // add last request date
        newFile.LastRequestDate = applicationDate;
        // save
        //generate RRR
        var applicantName = newFile.applicants.Count > 1 ? newFile.applicants[0].Name + " et al." : newFile.applicants[0].Name;
        var costData = _remitaPaymentUtils.GetCost(PaymentTypes.NewCreation, newFile.Type, applicantNationality, newFile.DesignType, newFile.PatentType);
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(costData.Item1, costData.Item3, costData.Item2, $"Payment for new {newFile.Type.ToString()} Application",
            applicantName, newFile.Correspondence.email, newFile.Correspondence.phone);
        if (rrr != null)
        {
            _log.LogInformation("Generated RRR {Rrr} for new file with FileId {FileId}", rrr, fileId);
            fileHistory.PaymentId = rrr;
        }
        fileHistory.Applicants = newFile.applicants;
        newFile.ApplicationHistory = [fileHistory];
        newFile.FileStatus = ApplicationStatuses.AwaitingPayment;
        await CreateFileAsync(newFile);
        // update data
        // send back
    }

    private static readonly char[] separator = new char[] { '/' };

    public async Task GenerateDesignCerts()
    {
        var desingActive = _fillingCollection.AsQueryable().Where(x =>
            x.Type == FileTypes.Design &&
            x.FileId.Split(separator).Length == 6 && x.FileStatus == ApplicationStatuses.Active).ToList();
        foreach (var filling in desingActive)
        {
            Console.WriteLine($"{desingActive.IndexOf(filling) + 1}, {filling.Id}");
            // var acceptanceUrl=await SaveAcceptance(filling, "", "ILoduba C.O");
            var certificateUrl = await SaveCertificate(filling, "", "ILoduba C.O");
            if (filling.ApplicationHistory[0].Letters.ContainsKey("acceptance"))
            {
                filling.ApplicationHistory[0].Letters.Add("certificate", [certificateUrl]);
            }
            await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x => x.Id, filling.Id),
            ]),
            Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].Letters, filling.ApplicationHistory[0].Letters));
        }
    }

    private string CreateTempFileNumber(FileTypes type, string applicantsCountry, PatentTypes? patentType = null,
        DesignTypes? designType = null, TradeMarkType? tradeMarkType = null)
    {
        _log.LogInformation("Creating temporary file number for Type {Type}, ApplicantCountry {Country}, PatentType {PatentType}, DesignType {DesignType}, TradeMarkType {TradeMarkType}",
            type, applicantsCountry, patentType, designType, tradeMarkType);
        var firstSection = applicantsCountry.ToLower() == "nigeria".ToLower() ? "NG" : "F";
        var secondSection = type is FileTypes.Design ? "DS" : type is FileTypes.Patent ? "PT" : "TM";
        var thirdSection = "";
        var year = DateTime.Now.Year.ToString();
        if (type == FileTypes.Patent)
        {
            thirdSection = patentType == PatentTypes.Conventional ? "C" :
                patentType == PatentTypes.Non_Conventional ? "NC" : "PCT";
        }
        if (type == FileTypes.Design)
        {
            thirdSection = designType == DesignTypes.NonTextile ? "NT" : "T";
        }

        if (type == FileTypes.TradeMark)
        {
            firstSection = tradeMarkType == TradeMarkType.Local ? "NG" : "F";
            var tradeNumber = string.Join("/", [firstSection, secondSection, "O", year]);
            return tradeNumber;
        }

        var fileNumber = string.Join("/", [firstSection, secondSection, thirdSection, "O", year]);
        _log.LogDebug("Generated temporary file number {FileNumber}", fileNumber);
        return fileNumber;
    }

    private async Task<(bool, RemitaResponseClass)> CheckStatusViaOrderId(string Rrr)
    {
        const string merchantId = "6230040240";
        const string apiKey = "192753";
        var test = Rrr + apiKey + merchantId;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var hash = Convert.ToHexString(apiHash).ToLower();
        var transactionStatusUrl =
            $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{Rrr}/{hash}/status.reg";
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");
        var response = await client.SendAsync(request);
        var dataMod = await response.Content.ReadAsStringAsync();
        // Console.WriteLine(dataMod);
        var obj = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod);
        // Console.WriteLine($"{Rrr}, {obj.amount}, {obj}");
        if (obj.status == "00")
        {
            return (true, obj);
        }
        else
        {
            return (false, obj);
        }
    }

    private async Task<(bool, double?)> ValidationRRR(string Rrr)
    {
        const string merchantId = "6230040240";
        const string apiKey = "192753";
        var test = Rrr + apiKey + merchantId;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var hash = Convert.ToHexString(apiHash).ToLower();
        var transactionStatusUrl =
            $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{Rrr}/{hash}/status.reg";
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");
        var response = await client.SendAsync(request);
        var dataMod = await response.Content.ReadAsStringAsync();
        var obj = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod);
        if (obj.status == "00")
        {
            return (true, obj.amount);
        }
        else
        {
            return (false, obj.amount);
        }
    }

    
    public async Task<List<FileStatsRes>?> FileStats(string? userId)
    {
        try
        {
            var pipeline = new BsonDocument[] { };
            if (userId == "null")
            {
                pipeline = new BsonDocument[]
                {
                new BsonDocument("$facet", new BsonDocument
                {
                    {
                        "detailedStats", new BsonArray
                        {
                            new BsonDocument{ { "$unwind", new BsonDocument { { "path", "$ApplicationHistory" } } }},
                            new BsonDocument{{
                                "$group", new BsonDocument
                                {
                                    {
                                        "_id", new BsonDocument
                                        {
                                            { "fileType", "$Type" },
                                            { "applicationType", "$ApplicationHistory.ApplicationType" },
                                            { "status", "$ApplicationHistory.CurrentStatus" }
                                        }
                                    },
                                    { "count", new BsonDocument("$sum", 1) }
                                }
                            }},
                            new BsonDocument{{
                                "$project", new BsonDocument
                                {
                                    { "_id", 0 },
                                    { "fileType", "$_id.fileType" },
                                    { "type", "$_id.applicationType" },
                                    { "count", "$count" },
                                    { "status", "$_id.status" }
                                }
                            }}
                        }
                    },
                    {
                        "fileStats", new BsonArray
                        {
                            new BsonDocument{{
                                "$group", new BsonDocument {
                                    { "_id", new BsonDocument { { "fileType", "$Type" } } },
                                    { "count", new BsonDocument("$sum", 1) } }
                                }},
                            new BsonDocument{ {"$project", new BsonDocument{
                                    { "_id", 0 },
                                    { "fileType", "$_id.fileType" },
                                    { "count", "$count" },
                                }}}
                            }
                        },
                    {"inactive", new BsonArray
                    {
                        new BsonDocument
                        {
                            {"$match", new BsonDocument{{"FileStatus", new BsonDocument{{"$eq", "Inactive" }}}} },
                        },
                        new BsonDocument{{"$count", "total"}}
                    }}
                })
                };
            }
            else
            {
                pipeline = new BsonDocument[]
                {
                new BsonDocument("$match", new BsonDocument
                {
                    {
                        "CreatorAccount", new BsonDocument()
                        {
                            { "$eq", userId }
                        }
                    }
                }),
                new BsonDocument("$facet", new BsonDocument
                {
                    {
                        "detailedStats", new BsonArray
                        {
                            new BsonDocument { { "$unwind", new BsonDocument { { "path", "$ApplicationHistory" } } } },
                            new BsonDocument
                            {
                                {
                                    "$group", new BsonDocument
                                    {
                                        {
                                            "_id", new BsonDocument
                                            {
                                                { "fileType", "$Type" },
                                                { "applicationType", "$ApplicationHistory.ApplicationType" },
                                                { "status", "$ApplicationHistory.CurrentStatus" }
                                            }
                                        },
                                        { "count", new BsonDocument("$sum", 1) }
                                    }
                                }
                            },
                            new BsonDocument
                            {
                                {
                                    "$project", new BsonDocument
                                    {
                                        { "_id", 0 },
                                        { "fileType", "$_id.fileType" },
                                        { "type", "$_id.applicationType" },
                                        { "count", "$count" },
                                        { "status", "$_id.status" }
                                    }
                                }
                            }
                        }
                    },
                    {
                        "fileStats", new BsonArray
                        {
                            new BsonDocument{{
                                "$group", new BsonDocument {
                                    { "_id", new BsonDocument { { "fileType", "$Type" } } },
                                    { "count", new BsonDocument("$sum", 1) } }
                            }},
                            new BsonDocument{ {"$project", new BsonDocument{
                                { "_id", 0 },
                                { "fileType", "$_id.fileType" },
                                { "count", "$count" },
                            }}}
                        }
                    },
                    {"inactive", new BsonArray
                    {
                        new BsonDocument
                        {
                            {"$match", new BsonDocument{{"FileStatus", new BsonDocument{{"$eq", "Inactive" }}}} },
                        },
                        new BsonDocument{{"$count", "total"}}
                    }}
                })
                };
            }

            var result = await _fillingCollection.Aggregate<BsonDocument>(pipeline).ToListAsync();
            List<FileStatsRes> stats_mapped = [];

            foreach (var doc in result)
            {
                var detailedStatsBson = doc.Contains("detailedStats") ? doc["detailedStats"].AsBsonArray : new BsonArray();
                var fileStatsBson     = doc.Contains("fileStats")     ? doc["fileStats"].AsBsonArray     : new BsonArray();
                var inactiveBson      = doc.Contains("inactive")      ? doc["inactive"].AsBsonArray      : new BsonArray();

                var detailedStats = new List<DetailedStats>();
                foreach (BsonDocument item in detailedStatsBson)
                {
                    try { detailedStats.Add(BsonSerializer.Deserialize<DetailedStats>(item)); }
                    catch { /* skip records with corrupted/unrecognised enum values */ }
                }

                stats_mapped.Add(new FileStatsRes
                {
                    detailedStats = detailedStats,
                    fileStats = fileStatsBson.Select(x => BsonSerializer.Deserialize<FilesCount>(x.AsBsonDocument)).ToList(),
                    inactive  = inactiveBson
                        .Select(x => (dynamic)new { total = x.AsBsonDocument.Contains("total") ? x.AsBsonDocument["total"].AsInt32 : 0 })
                        .ToList()
                });
            }

            return stats_mapped;
            // var builder=Builders<Filling>.Filter;
            // List <dynamic > stats = [];
            // var isCreator = userId == null?builder.Empty: builder.Eq(f => f.CreatorAccount, userId);
            // var isPatent = builder.Eq(f => f.Type, FileTypes.Patent);
            // var isDesign = builder.Eq(f => f.Type, FileTypes.Design);
            // var isTrademark = builder.Eq(f => f.Type, FileTypes.TradeMark);
            // var totalConventional = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, isPatent, builder.Eq(f => f.PatentType, PatentTypes.Conventional)
            // ]));
            // var totalTextile = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, isDesign, builder.Eq(f => f.DesignType, DesignTypes.Textile)
            // ]));
            // var totalNonTextile = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, isDesign, builder.Eq(f => f.DesignType, DesignTypes.NonTextile)
            // ]));
            // var totalNonConventional = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, isPatent, builder.Eq(f => f.PatentType, PatentTypes.Non_Conventional)
            // ]));
            // var totalPct = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, isPatent, builder.Eq(f => f.PatentType, PatentTypes.PCT)
            // ]));
            // var totalTForeign = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, isTrademark, builder.Eq(f => f.TrademarkType, TradeMarkType.Foreign)
            // ]));
            // var totalTLocal = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, isTrademark, builder.Eq(f => f.TrademarkType, TradeMarkType.Local)
            // ]));
            // var totalDue = _fillingCollection.CountDocuments(builder.And([
            //     isCreator, builder.Eq(f => f.FileStatus, ApplicationStatuses.Inactive)
            // ]));
            //     var dataResult = await _fillingCollection.AsQueryable()
            //         .Where(x=>userId==null || x.CreatorAccount==userId)
            //         .GroupBy(t => new { fileType = t.Type, history = t.ApplicationHistory  })
            //         .SelectMany(t => t.Key.history, (t, history) => new { t.Key.fileType, history })
            //         .GroupBy(q => new
            //         {
            //             q.fileType, 
            //             applicationType = q.history.ApplicationType, 
            //             status = q.history.CurrentStatus
            //         })
            //         .Select(t => new
            //         {
            //             t.Key.fileType, 
            //             type = t.Key.applicationType,
            //             count = t.Count(), 
            //             t.Key.status
            //         }).ToListAsync();
            //     stats.Add(new {type="dataResult", value=dataResult});
            //     stats.Add(new {type="totalPatent",count=_fillingCollection.CountDocuments(Builders<Filling>.Filter.And([isCreator,isPatent ]))});
            //     stats.Add(new { type = "totalNC", count = totalNonConventional });
            //     stats.Add(new { type = "totalC", count = totalConventional });
            //     stats.Add(new { type = "totalPCT", count = totalPct });
            //     stats.Add(new { type = "totalTX", count = totalTextile });
            //     stats.Add(new { type = "totalDue", count = totalDue });
            //     stats.Add(new { type = "totalTForeign", count = totalTForeign });
            //     stats.Add(new { type = "totalTLocal", count = totalTLocal });
            //     stats.Add(new { type = "totalNTX", count = totalNonTextile });
            //     stats.Add(new {type="totalDesign", count=_fillingCollection.CountDocuments(Builders<Filling>.Filter.And([isCreator,isDesign ]))});
            //     stats.Add(new {type="totalTrademarks", count=_fillingCollection.CountDocuments(Builders<Filling>.Filter.And([isCreator,isTrademark ]))});
            //     watch.Stop();
            //     Console.WriteLine(watch.ElapsedMilliseconds);
            //     watch.Restart();


            // var pipeline = new BsonDocument[]
            // {
            //     new BsonDocument("$unwind", new BsonDocument { {"path", "$ApplicationHistory"} }),
            //     new BsonDocument("$group", new BsonDocument
            //     {
            //         {"_id", new BsonDocument
            //             {
            //                 {"fileType", "$Type"},
            //                 {"applicationType", "$ApplicationHistory.ApplicationType"},
            //                 {"status", "$ApplicationHistory.CurrentStatus"}
            //             }
            //         },
            //         {"count", new BsonDocument("$sum", 1)}
            //     }),
            //     new BsonDocument("$project", new BsonDocument
            //     {
            //         {"_id", 0},
            //         {"fileType", "$_id.fileType"},
            //         {"type", "$_id.applicationType"},
            //         {"count", "$count"},
            //         {"status", "$_id.status"}
            //     })
            // };
            // var result = await _fillingCollection.Aggregate<BsonDocument>(pipeline).ToListAsync();
            // Console.WriteLine(JsonSerializer.Serialize(result.ToJson()));
            // Console.WriteLine(JsonSerializer.Serialize(stats));
        }
        catch (Exception up)
        {
            throw up;
        }


    }


    public async Task<RenewalDto> GetRenewalCost(string fileNumber, string userId, FileTypes fileType)
    {
        _log.LogInformation("Fetching renewal cost...");
        var user = await _userCollection
            .Find(Builders<AppUser>.Filter.Eq(u => u.Id, userId))
            .FirstOrDefaultAsync();
        if (user == null)
        {
            _log.LogError("User not found");
            throw new KeyNotFoundException("User not found.");
        }

        var userName = user.Name ?? $"{user.FirstName} {user.LastName}";
        var renew = fileType switch
        {
            FileTypes.Patent => await PatentRenewalCost(fileNumber, FileTypes.Patent),
            FileTypes.Design => await DesignRenewalCost(fileNumber, FileTypes.Design),
            FileTypes.TradeMark => await TrademarkRenewalCost(fileNumber, FileTypes.TradeMark),
            _ => throw new ArgumentOutOfRangeException(nameof(fileType), $"Unsupported file type: {fileType}")
        };
        if (renew is null) return null;
        var app = new ApplicationInfo
        {
            ApplicationDate = DateTime.Now,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            ExpiryDate = renew.NextRenewalDue,
            LicenseType = "Renewal",
            ApplicationType = FormApplicationTypes.LicenseRenewal,
            PaymentId = renew.PaymentId,
            StatusHistory =
            [
                new ApplicationHistory
                {
                    Date = DateTime.Now,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.AwaitingPayment,
                    Message = "Renewal initiated, awaiting payment",
                    UserId = userId,
                    User = userName
                }
            ],
        };
        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.FileId, fileNumber),
            Builders<Filling>.Update.Push(f => f.ApplicationHistory, app)
        );
        _log.LogInformation("Renewal application created and awaiting payment.");
        return renew;
    }
    public async Task<RenewalDto> PatentRenewalCost(string fileId, FileTypes fileType)
    {
        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == fileId).FirstOrDefaultAsync();
            if (file is null)
            {
                _log.LogError("File not found");
                throw new KeyNotFoundException();
            }
            var lateRenewal = file.FileStatus == ApplicationStatuses.Inactive;

            var lastRenewal = file.ApplicationHistory.LastOrDefault(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal && a.CurrentStatus == ApplicationStatuses.Approved);
            var renewalDue = lastRenewal?.ExpiryDate?.ToDateTime(TimeOnly.MinValue).AddDays(-90);
            if (renewalDue.HasValue && DateTime.Now < renewalDue.Value)
            {
                _log.LogWarning($"Renewal attempted before due date: {renewalDue.Value.ToString("yyyy-MM-dd")}");
                throw new Exception($"Renewal can only begin on or after: {renewalDue.Value.ToString("yyyy-MM-dd")}");
            }
            
            var applicant = file.applicants.FirstOrDefault();
            var cost = _remitaPaymentUtils.GetCost(lateRenewal ? PaymentTypes.PatentLateRenewal : PaymentTypes.LicenseRenew, fileType, file.FilingCountry ?? "", file.DesignType, file.PatentType);
            var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(cost.Item1, cost.Item3, cost.Item2,
                "Payment for Trademark Renewal", applicant.Name, applicant.Email, applicant.Phone);
            if (rrr is null)
            {
                _log.LogError("Failed to Generate RRR");
                throw new NullReferenceException();
            }

            var renew = new RenewalDto
            {
                ApplicantName = applicant.Name,
                Cost = cost.Item1,
                FileNumber = fileId,
                FileTypes = FileTypes.Patent,
                PaymentId = rrr ?? "",
                ServiceFee = cost.Item3,
                IsLateRenewal = lateRenewal,
                LateRenewalCost = "5000"
            };
            return renew;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in RenewalApplication: {ex.Message}");
            throw;
        }
    }
    public async Task<RenewalDto> DesignRenewalCost(string fileId, FileTypes fileType)
    {
        _log.LogInformation("Fetching design renewal cost...");

        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == fileId).FirstOrDefaultAsync();
            if (file is null)
            {
                _log.LogError("File not found");
                throw new KeyNotFoundException();
            }

            var lateRenewal = file.FileStatus == ApplicationStatuses.Inactive;
            var lastRenewal = file.ApplicationHistory.LastOrDefault(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal && a.CurrentStatus == ApplicationStatuses.Approved);
            var renewalDue = lastRenewal?.ExpiryDate?.ToDateTime(TimeOnly.MinValue).AddDays(-90);
            if (renewalDue.HasValue && DateTime.Now < renewalDue.Value)
            {
                _log.LogWarning($"Renewal attempted before due date: {renewalDue.Value.ToString("yyyy-MM-dd")}");
                throw new Exception($"Renewal can only begin on or after: {renewalDue.Value.ToString("yyyy-MM-dd")}");
            }
            var applicant = file.applicants.FirstOrDefault();
            var cost = _remitaPaymentUtils.GetCost(lateRenewal ? PaymentTypes.PatentLateRenewal : PaymentTypes.LicenseRenew, fileType, file.FilingCountry ?? "", file.DesignType, null);
            var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(cost.Item1, cost.Item3, cost.Item2,
                "Payment for Design Renewal", applicant.Name, applicant.Email, applicant.Phone);
            if (rrr is null)
            {
                _log.LogError("Failed to Generate RRR");
                throw new Exception();
            }

            var renew = new RenewalDto
            {
                ApplicantName = applicant.Name,
                Cost = cost.Item1,
                FileNumber = fileId,
                FileTypes = FileTypes.Design,
                PaymentId = rrr ?? "",
                ServiceFee = cost.Item3,
                LateRenewalCost = "5000",
                IsLateRenewal = lateRenewal
            };
            return renew;

        }
        catch (Exception)
        {
            _log.LogError("Failed to fetch design renewal cost");
            throw;
        }
    }
    //public async Task<RenewalDto> PatentRenewalCost(string fileId, FileTypes fileType)
    //{
    //    var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
    //    if (file == null)
    //        throw new Exception("File not found");

    //    if (file.Type != FileTypes.Patent)
    //        throw new Exception("This method is strictly for patent files.");

    //    // --- Patent logic below ---
    //    // Only for PCT/Conventional: use FirstPriorityInfo
    //    DateOnly? baseDate = null;
    //    if (file.PatentType == PatentTypes.PCT || file.PatentType == PatentTypes.Conventional)
    //    {
    //        if (file.FirstPriorityInfo != null && file.FirstPriorityInfo.Count > 0)
    //        {
    //            baseDate = file.FirstPriorityInfo
    //                .Where(x => !string.IsNullOrWhiteSpace(x.Date))
    //                .Select(x => DateOnly.Parse(x.Date))
    //                .Min();
    //        }
    //        else
    //        {
    //            throw new Exception("No valid First Priority Date found for this patent.");
    //        }
    //    }
    //    else
    //    {
    //        // For Non-Conventional, use FilingDate or DateCreated
    //        if (file.FilingDate != null)
    //            baseDate = DateOnly.FromDateTime(file.FilingDate.Value);
    //        else
    //            baseDate = DateOnly.FromDateTime(file.DateCreated);
    //    }

    //    // Find the most recent renewal (if any)
    //    DateOnly? lastRenewalDate = null;
    //    if (file.ApplicationHistory != null)
    //    {
    //        var lastRenewal = file.ApplicationHistory
    //            .Where(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal)
    //            .OrderByDescending(a => a.ApplicationDate)
    //            .FirstOrDefault();
    //        if (lastRenewal != null)
    //            lastRenewalDate = DateOnly.FromDateTime(lastRenewal.ApplicationDate);
    //    }

    //    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    //    // --- Anniversary logic for first-time renewal ---
    //    if (lastRenewalDate == null)
    //    {
    //        var firstAnniversary = baseDate.Value.AddYears(1);
    //        if (today < firstAnniversary)
    //        {
    //            throw new Exception($"Renewal can only begin on or after the first anniversary: {firstAnniversary:yyyy-MM-dd}");
    //        }
    //    }

    //    // Use last renewal date if available, else base date
    //    var renewalStartDate = lastRenewalDate ?? baseDate.Value;

    //    // Calculate missed years
    //    //int missedYears = today.Year - renewalStartDate.Year;
    //    //if (today > renewalStartDate.AddYears(missedYears)) missedYears++;
    //    //if (missedYears < 1) missedYears = 1;

    //    int missedYears = (today.DayOfYear >= renewalStartDate.DayOfYear)
    //    ? today.Year - renewalStartDate.Year
    //    : today.Year - renewalStartDate.Year - 1;
    //    if (missedYears < 1) missedYears = 1;

    //    // Get normal and late renewal costs
    //    var (normalFeeStr, serviceId, serviceFeeStr) = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, file.Type, file.FilingCountry ?? "", file.DesignType, file.PatentType);
    //    var (lateFeeStr, _, lateServiceFeeStr) = _remitaPaymentUtils.GetCost(PaymentTypes.PatentLateRenewal, file.Type, file.FilingCountry ?? "", file.DesignType, file.PatentType);

    //    int normalFee = int.TryParse(normalFeeStr, out var nf) ? nf : 0;
    //    int lateFee = int.TryParse(lateFeeStr, out var lf) ? lf : 0;
    //    int serviceFee = int.TryParse(serviceFeeStr, out var sf) ? sf : 0;
    //    int lateServiceFee = int.TryParse(lateServiceFeeStr, out var lsf) ? lsf : 0;

    //    bool isFirstRenewal = lastRenewalDate == null;
    //    bool isWithinFirst6Months = false;
    //    if (isFirstRenewal)
    //    {
    //        var baseDateTime = baseDate.Value.ToDateTime(TimeOnly.MinValue);
    //        var monthsSinceBase = ((today.Year - baseDate.Value.Year) * 12) + today.Month - baseDate.Value.Month;
    //        var windowStart = new DateOnly(today.Year, baseDate.Value.Month, baseDate.Value.Day);
    //        var windowEnd = windowStart.AddMonths(6).AddDays(-1);
    //        isWithinFirst6Months = today >= windowStart && today <= windowEnd;
    //    }

    //    int totalNormal = 0;
    //    int totalLate = 0;
    //    int totalService = 0;
    //    int lateYearsCount = 0;

    //    if (isFirstRenewal && isWithinFirst6Months)
    //    {
    //        // Multiply normal fee by missed years, no late fee
    //        totalNormal = missedYears * normalFee;
    //        totalLate = 0;
    //        totalService = missedYears * serviceFee;
    //        lateYearsCount = 0;
    //    }
    //    else
    //    {
    //        // For all missed years, charge both normal and late fee
    //        totalNormal = missedYears * normalFee;
    //        totalLate = missedYears * lateFee;
    //        totalService = missedYears * (serviceFee + lateServiceFee);
    //        lateYearsCount = missedYears;
    //    }

    //    int total = totalNormal + totalLate;

    //    // Generate RRR
    //    var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
    //        total.ToString(), totalService.ToString(), serviceId, $"{file.Type} renewal",
    //        file.applicants.FirstOrDefault()?.Name ?? "",
    //        file.applicants.FirstOrDefault()?.Email ?? "",
    //        file.applicants.FirstOrDefault()?.Phone ?? "");

    //    return new RenewalDto
    //    {
    //        Cost = total.ToString(),
    //        PaymentId = rrr,
    //        FileNumber = fileId,
    //        IsLateRenewal = lateYearsCount > 0,
    //        LateRenewalCost = totalLate > 0 ? totalLate.ToString() : null,
    //        ServiceFee = totalService.ToString(),
    //        MissedYearsCount = missedYears,
    //        LateYearsCount = lateYearsCount,
    //        FileTypes = file.Type,
    //    };
    //}
    public async Task<RenewalDto> TrademarkRenewalCost(string fileId, FileTypes fileType)
    {
        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == fileId).FirstOrDefaultAsync();
            if (file is null)
            {
                _log.LogError("File not found");
                throw new KeyNotFoundException();
            }
            var firstApp = file.ApplicationHistory.FirstOrDefault();
            var lateRenewal = file.FileStatus == ApplicationStatuses.PendingRenewal;

            var lastRenewal = file.ApplicationHistory.LastOrDefault(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal && a.CurrentStatus == ApplicationStatuses.Approved);
            var renewalDue = lastRenewal?.ExpiryDate?.ToDateTime(TimeOnly.MinValue).AddDays(-90) ?? firstApp.ExpiryDate?.ToDateTime(TimeOnly.MinValue).AddDays(-90);
            Console.WriteLine($"Renewal due date: {renewalDue?.ToString("yyyy-MM-dd")}");
            if (!lateRenewal && (renewalDue.HasValue && DateTime.Now < renewalDue.Value))
            {
                _log.LogWarning($"Renewal attempted before due date: {renewalDue.Value.ToString("yyyy-MM-dd")}");
                throw new Exception($"Renewal can only begin on or after: {renewalDue.Value.ToString("yyyy-MM-dd")}");
            }
            
            var applicant = file.applicants.FirstOrDefault();
            //var cost = _remitaPaymentUtils.GetCost(lateRenewal ? PaymentTypes.LateTrademarkRenewal : PaymentTypes.LicenseRenew, fileType, file.FilingCountry ?? "", file.DesignType, null);
            var cost = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, fileType, file.FilingCountry ?? "", file.DesignType, null);

            var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(cost.Item1, cost.Item3, cost.Item2,
                "Payment for Trademark Renewal", applicant.Name, applicant.Email, applicant.Phone);

            if (rrr is null)
            {
                _log.LogError("Failed to Generate RRR");
                throw new NullReferenceException();
            }
            
            var renew = new RenewalDto
            {
                ApplicantName = applicant.Name,
                Cost = cost.Item1,
                FileNumber = fileId,
                FileTypes = FileTypes.TradeMark,
                PaymentId = rrr ?? "",
                ServiceFee = cost.Item3,
                IsLateRenewal = lateRenewal,
                LateRenewalCost = "0",
                IsRenewalEligible = file?.IsRenewalEligible
            };
            return renew;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in RenewalApplication: {ex.Message}");
            throw;
        }
    }
    private async Task<string> SaveAcknowledgement(Filling tradeData)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        // var client=blobContainerClient.GetBlobClient(trustedFileName);
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        byte[]? data = [];

        data = await GenerateAcknowledgement(tradeData);
        using (var ms = new MemoryStream(data))
        {

            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }
        return uri;
    }

    private async Task<byte[]> GenerateAcknowledgement(Filling tradeData)
    {
        byte[] data = [];
        var receipt = new Receipt
        {
            rrr = "",
            Amount = "",
            Date = "",
            ApplicantName = tradeData.applicants.Count > 1 ? tradeData.applicants[0].Name + " et al." : tradeData.applicants[0].Name,
            FileId = tradeData.FileId,
            Title = tradeData.TitleOfTradeMark,
            PaymentFor = ""
        };
        if (tradeData.Type is FileTypes.Design)
        {
            List<byte[]> images = [];

            // foreach (var url in tradeData.Attachments.FirstOrDefault(x => x.name == "designs").url)
            // {
            //     images.Add(await (new HttpClient()).GetByteArrayAsync(url));
            // }
            data = new AcknowledgementModelDesign(tradeData, "uri", images, receipt.Date).GeneratePdf();
        }

        if (tradeData.Type is FileTypes.TradeMark)
        {
            byte[] images = [];

            // foreach (var url in tradeData.Attachments.FirstOrDefault(x => x.name == "designs").url)
            // {
            //     images.Add(await (new HttpClient()).GetByteArrayAsync(url));
            // }
            data = new AcknowledgementModelTrademark(tradeData, "uri", images, receipt).GeneratePdf();
        }


        if (tradeData.Type is FileTypes.Patent)
        {
            data = new AcknowledgementModelPatent(tradeData, "uri").GeneratePdf();
        }

        return data;

    }

    private async Task<string> SaveAcceptance(Filling tradeData, string signatureUrl, string examinerName,
         DateTime? approvalDate = null)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        byte[]? data = [];
        // var sigdata = await (new HttpClient()).GetByteArrayAsync(signatureUrl);
        using (var ms = new MemoryStream(data))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }
        // await client.UploadAsync(new MemoryStream(data),  new BlobUploadOptions()
        // {
        //     HttpHeaders = new BlobHttpHeaders()
        //     {
        //         ContentType = "application/pdf"
        //     }
        // });
        return uri;
    }

    private async Task<byte[]> GenerateCertificate(Filling tradeData)
    {
        byte[]? imageData = null;
        if (tradeData.Type == FileTypes.TradeMark && tradeData.Attachments.FirstOrDefault(x => x.name == "representation") != null)
        { imageData = await (new HttpClient()).GetByteArrayAsync(tradeData.Attachments.First(x => x.name == "representation").url[0]); }
        var data = tradeData.Type == FileTypes.Design
            ? new DesignCertificate(tradeData, tradeData.ApplicationHistory[0].ExpiryDate.ToString()).GeneratePdf()
            : tradeData.Type == FileTypes.TradeMark ? new NewTrademarkCertificate(tradeData, "uri", imageData).GeneratePdf() :
                new ApprovedCertificate(tradeData, $"https://portal.iponigeria.com/qr?fileId={tradeData.FileId}").GeneratePdf();
        return data;
    }

    private async Task<string> SaveCertificate(Filling tradeData, string signatureUrl, string examinerName)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        // var sigdata = await (new HttpClient()).GetByteArrayAsync(signatureUrl);
        byte[]? data = [];
        byte[] sigdata = [];
        data = await GenerateCertificate(tradeData);
        using (var ms = new MemoryStream(data))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }
        return uri;
    }

    private async Task<byte[]> GenerateRejection(Filling tradeData, string signatureUrl)
    {
        List<byte[]> images = [];
        byte[]? data = [];
        byte[] sigdata = [];


        if (tradeData.Type is FileTypes.Design)
        {
            foreach (var url in tradeData.Attachments.FirstOrDefault(x => x.name == "designs").url)
            {
                images.Add(await (new HttpClient()).GetByteArrayAsync(url));
            }
        }

        var examinerName = tradeData.ApplicationHistory[0].StatusHistory.FirstOrDefault(x =>
                x.afterStatus == ApplicationStatuses.Rejected ||
                x.afterStatus == ApplicationStatuses.RejectedByExaminer)
            .User;
        if (tradeData.Type is FileTypes.Design)
        {
            data = new RejectionModelDesign(tradeData, "uri", sigdata, images, examinerName).GeneratePdf();
        }

        if (tradeData.Type is FileTypes.Patent)
        {
            data = new RejectionModelPatent(tradeData, "uri", sigdata, examinerName).GeneratePdf();
        }

        if (tradeData.Type is FileTypes.TradeMark)
        {
            byte[] image = [];
            try
            {
                if ((tradeData.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) ||
                    tradeData.Attachments.FirstOrDefault(e => e.name == "representation") != null)
                {
                    image = await (new HttpClient()).GetByteArrayAsync(tradeData.Attachments
                        .First(r => r.name == "representation").url[0]);
                }
            }
            catch (Exception)
            {
                image = [];
            }
            data = new RejectionModelTrademark(tradeData, "uri", sigdata, examinerName, image).GeneratePdf();
        }

        return data;
    }

    private async Task<string> SaveRejection(Filling tradeData, string signatureUrl, string examinerName)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        // var client=blobContainerClient.GetBlobClient(trustedFileName);
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        byte[]? data = [];
        // var sigdata = await (new HttpClient()).GetByteArrayAsync(signatureUrl);
        byte[] sigdata = [];
        List<byte[]> images = [];

        // await client.UploadAsync(new MemoryStream(data),  new BlobUploadOptions()
        // {
        //     HttpHeaders = new BlobHttpHeaders()
        //     {
        //         ContentType = "application/pdf"
        //     }
        // });
        using (var ms = new MemoryStream(data))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }

        return uri;
    }

    private async Task<string> SaveReceipt(Receipt dataReceipt, Filling fileData)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        var bytes = new ReceiptModel(dataReceipt, uri, fileData).GeneratePdf();
        using (var ms = new MemoryStream(bytes))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }
        return uri;
    }

    //private async Task<string> DataUpdateAck(string field, string? previous = null, string? newTitle = null)
    //{
    //    var trustedFileName = Path.GetRandomFileName();
    //    trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
    //    var uri=$"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
    //    var recordalInfo = $"Application for update to {field}. Previous title: {previous}. Proposed new title: {newTitle}";
    //    if (new List<string>()
    //            { "patentabstract", "titleofdesign", "titleofinvention", "statementofnovelty" }.Contains(field)==false)
    //    {
    //        recordalInfo= $"Date update to field {field} Acknowledged";
    //    }
    //    var bytes= new RecordalAck(recordalInfo).GeneratePdf();
    //    using (var ms = new MemoryStream(bytes))
    //    {
    //        await _attachmentCollection.InsertOneAsync(new AttachmentInfo
    //        {
    //            Id = trustedFileName,
    //            ContentType = "application/pdf",
    //            Data = ms.ToArray()
    //        });
    //    }
    //    return uri;
    //}

    private async Task<string> CertificateOfRecordal(Filling fileData, byte[] image, string date)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";

        // Create RecordalCertificate with the Filling object
        var bytes = new RecordalCertificate(fileData, image, date).GeneratePdf();

        using (var ms = new MemoryStream(bytes))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }

        return uri;
    }

    // private async Task<string> SavePdf(byte[] bytes)
    // {
    //     var trustedFileName = Path.GetRandomFileName();
    //     trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
    //     var client=blobContainerClient.GetBlobClient(trustedFileName);
    //     await client.UploadAsync(new MemoryStream(bytes),  new BlobUploadOptions()
    //     {
    //         HttpHeaders = new BlobHttpHeaders()
    //         {
    //             ContentType = "application/pdf"
    //         }
    //     });
    //     var link = client.Uri.ToString();
    //     return link;
    // }

    public async Task<(byte[], string, string)?> GetAttachment(string fileId)
    {
        var filter = Builders<AttachmentInfo>.Filter.Eq(x => x.Id, fileId);
        var attachmentInfo = await _attachmentCollection.Find(filter).Limit(1).ToListAsync();
        if (attachmentInfo != null)
        {
            return (attachmentInfo[0].Data, attachmentInfo[0].ContentType, attachmentInfo[0].Id);
        }
        return null;
    }

    //public async Task<BatchRenewRes> GetBatchRenewalInfo(BatchRenewReq data)
    //{
    //    List<BatchRenewData> resData = [];
    //    var filters = new List<FilterDefinition<Filling>>
    //     {
    //         Builders<Filling>.Filter.Eq(x => x.CreatorAccount, data.userId),
    //         Builders<Filling>.Filter.Eq(x => x.FileStatus, ApplicationStatuses.Inactive)
    //     };
    //    var projection = Builders<Filling>.Projection.Expression(x => new BatchReqSummary()
    //    {
    //        FileNumber = x.FileId,
    //        Id = x.Id,
    //        Title = x.Type == FileTypes.Patent ? x.TitleOfInvention : x.TitleOfDesign,
    //        Type = x.Type,
    //        DesignType = x.DesignType,
    //        PatentType = x.PatentType,
    //        Number = x.Correspondence.phone,
    //        Email = x.Correspondence.email,
    //        ApplicantNames = x.applicants.Select(y => y.Name).ToList(),
    //    });
    //    long count = 0;
    //    count = _fillingCollection.CountDocuments(Builders<Filling>.Filter.And(filters));
    //    var fileResults = await _fillingCollection.Find(Builders<Filling>.Filter.And(
    //        filters
    //    )).Project(projection).Limit(10).Skip(data.skip ?? 0).ToListAsync();

    //    foreach (var fileInfo in fileResults)
    //    {
    //        // get cost and RRR
    //        var rrr_cost = await GetRenewalCost(new GetRenewalCost()
    //        {
    //            number = fileInfo.Number,
    //            designType = fileInfo.DesignType,
    //            type = fileInfo.Type,
    //            applicantName = fileInfo.ApplicantNames.Count > 1
    //                ? fileInfo.ApplicantNames[0] + " et al"
    //                : fileInfo.ApplicantNames[0],
    //            applicantEmail = fileInfo.Email,
    //            patentType = fileInfo.PatentType
    //        });
    //        resData.Add(new BatchRenewData()
    //        {
    //            cost = rrr_cost.Item2,
    //            paymentId = rrr_cost.Item1,
    //            fileNumber = fileInfo.FileNumber,
    //            fileTitle = fileInfo.Title,
    //            id = fileInfo.Id,
    //            fileType = fileInfo.Type,
    //            title = fileInfo.Type == FileTypes.Design ? "Design Renewal" : "Patent Renewal",
    //            applicant = fileInfo.ApplicantNames.Count > 1
    //                ? fileInfo.ApplicantNames[0] + " et al"
    //                : fileInfo.ApplicantNames[0],
    //        });
    //    }

    //    return new BatchRenewRes()
    //    {
    //        total = count,
    //        data = resData
    //    };
    //}

    public async Task<object?> GetUserTicketFiles(string userId, string userTypes)
    {
        FilterDefinition<Filling> filter = Builders<Filling>.Filter.Empty;
        if (userTypes == "user")
        { filter = Builders<Filling>.Filter.Eq(x => x.Id, userId); }
        if (userTypes == "design")
        { filter = Builders<Filling>.Filter.Eq(x => x.Type, FileTypes.Design); }
        if (userTypes == "patent")
        { filter = Builders<Filling>.Filter.Eq(x => x.Type, FileTypes.Patent); }
        return await _fillingCollection.Find(filter).Project(x => new
        {
            fileID = x.FileId,
            title = x.Type == FileTypes.Design ? x.TitleOfDesign : x.TitleOfInvention,
            id = x.Id,
            applicant = x.applicants.Select(y => y.Name)
        }).ToListAsync();
    }

    public async Task<Filling?> AdminUpdateAsync(AdminUpdateReq req)
    {
        var latestAddition = new ApplicationHistory()
        {
            afterStatus = req.afterStatus,
            beforeStatus = req.beforeStatus,
            Date = DateTime.Now,
            Message = req.reason,
            User = req.userName,
            UserId = req.userId
        };
        var filter = Builders<Filling>.Filter.And(Builders<Filling>.Filter.Eq("_id", req.fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == req.applicationId));
        List<UpdateDefinition<Filling>> operations = [];
        operations.Add(Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory",
            latestAddition));
        operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", req.afterStatus));
        if (req.applicationType is FormApplicationTypes.NewApplication)
        {
            operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, req.afterStatus));
        }
        if (req is { beforeStatus: ApplicationStatuses.Active, applicationType: FormApplicationTypes.NewApplication or FormApplicationTypes.LicenseRenewal })
        {
            var file = await GetFileAsync(req.fileId);
            // receipt and ack
            var letters = file.ApplicationHistory.FirstOrDefault(x => x.id == req.applicationId).ApplicationLetters;
            operations.Add(Builders<Filling>.Update.Unset("ApplicationHistory.$.ExpiryDate"));
            if (letters.Contains(ApplicationLetters.NewApplicationAcceptance))
            {
                letters.Remove(ApplicationLetters.NewApplicationAcceptance);
            }

            if (letters.Contains(ApplicationLetters.NewApplicationCertificate))
            {
                letters.Remove(ApplicationLetters.NewApplicationCertificate);
            }
            operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.ApplicationLetters", letters));
        }
        if (req is
            {
                beforeStatus: ApplicationStatuses.RejectedByExaminer or ApplicationStatuses.Rejected,
                applicationType: FormApplicationTypes.NewApplication or FormApplicationTypes.LicenseRenewal
            })
        {
            var file = await GetFileAsync(req.fileId);
            var letters = file.ApplicationHistory.FirstOrDefault(x => x.id == req.applicationId).ApplicationLetters;
            if (letters.Contains(ApplicationLetters.NewApplicationRejection))
            {
                letters.Remove(ApplicationLetters.NewApplicationRejection);
            }
            operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.ApplicationLetters", letters));
        }
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        var result = await _fillingCollection.FindOneAndUpdateAsync<Filling>(filter, Builders<Filling>.Update.Combine(operations), options);

        if (result != null)
        {
            await SendStatusUpdateNotificationAsync(
                result,
                req.applicationId,
                req.applicationType,
                req.beforeStatus,
                req.afterStatus);
        }

        //savePerformance(PerformanceType.Staff, FormApplicationTypes.None, req.beforeStatus, req.afterStatus,
        //    DateTime.Now, req.userName, result.Id, result.Type, result.PatentType, result.DesignType, result.TrademarkType);
        return result;
    }

    private async Task SendStatusUpdateNotificationAsync(
        Filling file,
        string applicationId,
        FormApplicationTypes applicationType,
        ApplicationStatuses previousStatus,
        ApplicationStatuses newStatus)
    {
        var fileOwner = await GetFileOwner(file.FileId);

        var notif = new CreateNotificationDto
        {
            Audience = NotificationAudience.User,
            Category = NotificationCategory.StatusUpdate,
            Priority = NotificationPriority.Medium,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ApplicationType = applicationType,
            Title = "Application Status Update",
            Message = $"Your {applicationType} status has been updated from {previousStatus} to {newStatus}",
            RecipientId = fileOwner,
            CreatedBy = "System",
            FileNumber = file.FileId,
            FileType = file.Type,
            ApplicationId = applicationId,
            ActionUrl = $"/dataview/?id={file.Id}"
        };

        await _notificationServices.CreateNotificationAsync(notif);
        _log.LogInformation("notification sent to {RecipientId}", notif.RecipientId);
    }

    public void SavePerformance(PerformanceDto perf)
    {
        var performance = new StaffPerformance
        {
            FileNumber = perf.FileNumber,
            FileType = perf.FileType,
            AfterStatus = perf.AfterStatus,
            BeforeStatus = perf.BeforeStatus,
            ApplicationType = perf.ApplicationType,
            AppUserId = perf.AppUserId,
            Date = perf.Date,
            Reason = perf.Reason,
            OfficeUnit = perf.OfficeUnit,
        };
        _performanceCollection.InsertOne(performance);
    }

    public async Task<(string?, string)> GenerateOppositionRRR(PaymentTypes type, string description, string name, string email, string number)
    {
        var details = await _remitaPaymentUtils.GenerateOppositionID(type, description, name, email, number);
        return details;
    }


    //public async Task PaidButNotReflecting()
    //{
    //    var allAwaiting = await _fillingCollection.Find(x => x.FileStatus == ApplicationStatuses.AwaitingPayment).Skip(10).ToListAsync();
    //    var recent = allAwaiting.Where(x => x.DateCreated >= DateTime.Parse("2024-11-1")).ToList();
    //    Console.WriteLine($"the total of recent awaiting payment is: {recent.Count}");
    //    foreach (var filling in recent)
    //    {
    //        Console.WriteLine($"{recent.IndexOf(filling) + 1} checking if payment is valid is: {filling.Id}");
    //        var status = await CheckStatusViaOrderId(filling.ApplicationHistory[0].PaymentId);
    //        if (status.Item1)
    //        {
    //            Console.WriteLine("updating to awaiting search");
    //            await NewApplicationPayment(
    //                new UpdateDataType()
    //                {
    //                    simulate = false,
    //                    beforeStatus = ApplicationStatuses.AwaitingPayment,
    //                    AfterStatus = ApplicationStatuses.AwaitingSearch,
    //                    title = filling.Type switch
    //                    {
    //                        FileTypes.Design => filling.TitleOfDesign,
    //                        FileTypes.Patent => filling.TitleOfInvention,
    //                        _ => filling.TitleOfTradeMark
    //                    },
    //                    applicantName = filling.applicants.Count > 1 ? filling.applicants[0].Name + " et al." : filling.applicants[0].Name,
    //                    amount = status.Item2.amount.ToString(),
    //                    paymentId = filling.ApplicationHistory[0].PaymentId,
    //                    message = "Payment successful, awaiting search",
    //                    user = "Auto",
    //                    userId = "Auto",
    //                    fileId = filling.Id,
    //                    applicationId = filling.ApplicationHistory[0].id,
    //                    FileType = filling.Type
    //                }
    //                );
    //        }
    //    }
    //}

    public async Task NewDesignPDF()
    {
        var allActive = await _fillingCollection.Find(x =>
            x.FileStatus == ApplicationStatuses.Active).ToListAsync();
        var noAcceptance = allActive.Where(x => x.ApplicationHistory[0].Letters.ContainsKey("acceptance") == false &&
                                              x.ApplicationHistory[0].Letters.ContainsKey("certificate") == true).ToList();
        Console.WriteLine(noAcceptance.Count);
        foreach (var filling in noAcceptance)
        {
            var approvalDate = filling.ApplicationHistory[0].StatusHistory.FirstOrDefault(x =>
                    x.beforeStatus == ApplicationStatuses.AwaitingExaminer &&
                    x.afterStatus == ApplicationStatuses.Active)
                .Date;
            Console.WriteLine($"{noAcceptance.IndexOf(filling)}, {filling.Id}, {approvalDate}");
            var link = await SaveAcceptance(filling, "", "", approvalDate);
            var currentLetters = filling.ApplicationHistory[0].Letters;
            var newLetters = new Dictionary<string, List<string>>() { };
            if (currentLetters.ContainsKey("receipt"))
            {
                var receipt = currentLetters["receipt"];
                newLetters.Add("receipt", receipt);
            }

            if (currentLetters.ContainsKey("acknowledgement"))
            {
                var receipt = currentLetters["acknowledgement"];
                newLetters.Add("acknowledgement", receipt);
            }

            newLetters.Add("acceptance", [link]);
            if (currentLetters.ContainsKey("certificate"))
            {
                var receipt = currentLetters["certificate"];
                newLetters.Add("certificate", receipt);
            }

            await _fillingCollection.FindOneAndUpdateAsync(x => x.Id == filling.Id,
                Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].Letters, newLetters));
        }
    }

    public async Task<object?> GetApplicationData(string fileId, string applicationId, string requestType)
    {
        if (requestType == "file")
        {
            var file = _fillingCollection.Find(d => d.Id == fileId).FirstOrDefault();
            NormalizeOwnershipHistory(file);
            return file;
        }
        var fileDoc = _fillingCollection.Find(d => d.Id == fileId).FirstOrDefault();
        var result = fileDoc?.ApplicationHistory?.FirstOrDefault(f => f.id == applicationId);
        if (result == null) return null;

        if (result.ApplicationType == FormApplicationTypes.Ownership)
        {
            result.OldValue = CoerceOwnershipValue(result.OldValue);
            result.NewValue = CoerceOwnershipValue(result.NewValue);
        }

        // For recordal application types the SuperAdmin UI expects a shaped `hist` payload
        // with a top-level `assignment` block (type 5) and camelCase old/new value keys so
        // its forms can pre-fill directly. Falling back to the raw ApplicationInfo for all
        // other types preserves existing consumer behaviour.
        switch (result.ApplicationType)
        {
            case FormApplicationTypes.Assignment:       // 5
            case FormApplicationTypes.RegisteredUser:   // 7
            case FormApplicationTypes.Merger:           // 8
            case FormApplicationTypes.ChangeOfName:     // 9
            case FormApplicationTypes.ChangeOfAddress:  // 10
                return Utils.ApplicationHistoryShaper.Shape(result, fileDoc?.FileId);
        }

        return result;
    }

    public async Task<Filling?> UpdateJsonData(string fileId, string applicationId, string requestType, object data)
    {
        if (requestType == "file")
        {
            var updatedFile = JsonSerializer.Deserialize<Filling>(data.ToString(),
                new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            var newValue = await _fillingCollection.FindOneAndReplaceAsync(
                Builders<Filling>.Filter.Eq(d => d.Id, fileId), updatedFile, new FindOneAndReplaceOptions<Filling>()
                {
                    ReturnDocument = ReturnDocument.After
                });
            return newValue;
        }

        var parsed = JsonSerializer.Deserialize<ApplicationInfo>(data.ToString(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
        var filter = Builders<Filling>.Filter.And(Builders<Filling>.Filter.Eq("_id", fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == applicationId));
        var result = await _fillingCollection.FindOneAndUpdateAsync(filter,
            Builders<Filling>.Update.Set("ApplicationHistory.$", parsed), new FindOneAndUpdateOptions<Filling>()
            {
                ReturnDocument = ReturnDocument.After
            });
        return result;
    }

    public async Task DeletePending()
    {
        var pending = await _fillingCollection.Find(x => x.FileStatus == ApplicationStatuses.AwaitingPayment)
            .Project(x => new { x.Id, x.DateCreated, x.ApplicationHistory[0].PaymentId }).ToListAsync();
        Console.WriteLine($"total to be deleted: {pending.Count}");
        List<string> toBeDeleted = [];
        List<Dictionary<string, string>> toConfirm = [];
        foreach (var curr in pending)
        {
            Console.WriteLine($"Checking remita....: {curr.Id}");
            var response = await _remitaPaymentUtils.GetDetailsByRRR(curr.PaymentId);
            if (response.status == "00")
            {
                toConfirm.Add(new Dictionary<string, string>()
                {
                    ["Id"] = curr.Id,
                    ["Date"] = response.paymentDate
                });
                Console.WriteLine($"has been paid for, but still showing awaiting payment {response.paymentDate}, {curr.Id}");
            }
            else
            {
                Console.WriteLine("Not paid for, can be deleted");
                toBeDeleted.Add(curr.Id);
                continue;
                // await _fillingCollection.FindOneAndDeleteAsync(x => x.Id == curr.Id);
            }
        }

        foreach (var co in toConfirm)
        {
            var file = await _fillingCollection.Find(x => x.Id == co["Id"]).FirstOrDefaultAsync();
            var document = await _countersCollection.Find(Builders<Counters>.Filter.Eq("_id", file.Type))
         .FirstOrDefaultAsync();
            var strings = file.FileId.Split("/");
            var max = strings.Length - 1;
            var newId = string.Join("/", strings.Take(max).Concat(new[] { document.currentNumber.ToString() }));
            var counterfilter = Builders<Counters>.Filter.Eq("_id", file.Type);
            Console.WriteLine("Updating....");
            await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, co["Id"]),
                Builders<Filling>.Update.Combine([
                    Builders<Filling>.Update.Set(t=>t.FileStatus, ApplicationStatuses.AwaitingSearch),
                     Builders<Filling>.Update.Set(t=>t.ApplicationHistory[0].CurrentStatus, ApplicationStatuses.AwaitingSearch),
                     Builders<Filling>.Update.Set(x => x.FileId, newId),
                     Builders<Filling>.Update.Push(x=>x.ApplicationHistory[0].StatusHistory, new ApplicationHistory()
                     {
                         beforeStatus = ApplicationStatuses.AwaitingPayment,
                         afterStatus = ApplicationStatuses.AwaitingSearch,
                         Message = "Payment successful, awaiting search",
                         Date = DateTime.Parse(co["Date"]),
                         User = "Auto",
                         UserId = "Auto"
                     }),
                     Builders<Filling>.Update.AddToSetEach(t=>t.ApplicationHistory[0].ApplicationLetters, [ApplicationLetters.NewApplicationAcknowledgement, ApplicationLetters.NewApplicationReceipt]),
                ]));
            await _countersCollection.FindOneAndUpdateAsync(counterfilter, Builders<Counters>.Update.Inc(f => f.currentNumber, 1));
        }
        // await _fillingCollection.DeleteManyAsync(x => toBeDeleted.Contains(x.Id));
    }

    public record ValCert
    {
        public Filling? data { get; set; }
    }

    public async Task<ValCert> ValidateCertificatePayment(string fileId, string rrr, string userName, string userId)
    {
        RemitaResponseClass? remita = null;
        if (rrr.Contains("IPO"))
        {
            remita = await _remitaPaymentUtils.GetDetailsByOrderId(rrr);
        }
        else
        {
            remita = await _remitaPaymentUtils.GetDetailsByRRR(rrr);
        }

        var file = await _fillingCollection.Find(x => x.Id == fileId).FirstOrDefaultAsync();
        file.ApplicationHistory[0].ApplicationLetters.Add(ApplicationLetters.NewApplicationCertificateReceipt);
        file.ApplicationHistory[0].ApplicationLetters.Add(ApplicationLetters.NewApplicationCertificateAck);
        var cIndex = file.ApplicationHistory.FindIndex(x => x.ApplicationType == FormApplicationTypes.Certification);
        var newLetters = file.ApplicationHistory[0].ApplicationLetters;
        var result = await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, fileId),
            Builders<Filling>.Update.Combine([
                Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].ApplicationLetters, newLetters),
                     Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].CertificatePaymentId, rrr),
                     Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].CurrentStatus,
                         ApplicationStatuses.AwaitingCertificateConfirmation),
                     Builders<Filling>.Update.Set(x => x.ApplicationHistory[cIndex].CurrentStatus,
                         ApplicationStatuses.AwaitingCertificateConfirmation),
                     Builders<Filling>.Update.Set(x => x.FileStatus,
                         ApplicationStatuses.AwaitingCertificateConfirmation),
                     Builders<Filling>.Update.Push(c=>c.ApplicationHistory[0].StatusHistory, new ApplicationHistory()
                     {
                         beforeStatus = file.ApplicationHistory[0].CurrentStatus,
                         afterStatus = ApplicationStatuses.AwaitingCertificateConfirmation,
                         Date = DateTime.Now,
                         Message = "Certificate Payment successful, awaiting confirmation",
                         User = userName,
                         UserId = userId
                     })
            ]), new FindOneAndUpdateOptions<Filling>()
            {
                ReturnDocument = ReturnDocument.After
            });
        saveFinance(remita, "Trademark Certificate", file.ApplicationHistory[0].id, file.Id, file.applicants[0].country, file.Type, file.DesignType, file.PatentType, file.TrademarkType, file.TrademarkClass);
        SavePayment(remita, PaymentTypes.TrademarkCertificate, fileId, file.ApplicationHistory[0].id);

        return new ValCert()
        {
            data = result
        };
    }

    // Content types accepted for the Change-of-Agent Power of Attorney document.
    private static readonly HashSet<string> _reAssignPoaAllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "image/jpeg",
        "image/png",
    };

    // 10 MB cap on decoded POA bytes.
    private const long ReAssignPoaMaxBytes = 10L * 1024L * 1024L;

    // Association type used to tag the stored POA on the file's attachments list
    // and on the AttachmentInfo metadata.
    private const string ReAssignPoaAssociationType = "ChangeOfAgentPOA";

    /// <summary>
    /// Result envelope for <see cref="ReAssign(ReAssignType)"/>. When <paramref name="ok"/> is false
    /// and <paramref name="isValidationError"/> is true, the caller should surface a 400. When
    /// <paramref name="ok"/> is false and <paramref name="isValidationError"/> is false, the caller
    /// should surface a 404 (file missing) or 500 (unexpected).
    /// </summary>
    public record ReAssignResult(bool ok, string? error, bool isValidationError, Filling? file);

    public async Task<ReAssignResult> ReAssign(ReAssignType data)
    {
        try
        {
            Console.WriteLine($"[ReAssign] Attempting to reassign fileId: {data?.fileId}");

            // ---- Validation --------------------------------------------------------------------
            if (data == null)
            {
                return new ReAssignResult(false, "Request body is required.", true, null);
            }

            if (string.IsNullOrWhiteSpace(data.fileId))
            {
                return new ReAssignResult(false, "fileId is required.", true, null);
            }

            if (string.IsNullOrWhiteSpace(data.newOwner))
            {
                return new ReAssignResult(false, "newOwner is required.", true, null);
            }

            if (data.newCorrespondence == null || data.oldCorrespondence == null)
            {
                return new ReAssignResult(false, "Old and new correspondence details are required.", true, null);
            }

            var poa = data.poa;
            if (poa == null)
            {
                return new ReAssignResult(false, "Power of Attorney document is required.", true, null);
            }

            if (string.IsNullOrWhiteSpace(poa.fileName))
            {
                return new ReAssignResult(false, "Power of Attorney fileName is required.", true, null);
            }

            if (string.IsNullOrWhiteSpace(poa.contentType))
            {
                return new ReAssignResult(false, "Power of Attorney contentType is required.", true, null);
            }

            if (poa.data == null || poa.data.Length == 0)
            {
                return new ReAssignResult(false, "Power of Attorney data is required.", true, null);
            }

            if (!_reAssignPoaAllowedContentTypes.Contains(poa.contentType))
            {
                return new ReAssignResult(
                    false,
                    "Power of Attorney contentType must be one of: application/pdf, application/msword, application/vnd.openxmlformats-officedocument.wordprocessingml.document, image/jpeg, image/png.",
                    true,
                    null);
            }

            if (poa.data.LongLength > ReAssignPoaMaxBytes)
            {
                return new ReAssignResult(
                    false,
                    $"Power of Attorney file exceeds the maximum allowed size of {ReAssignPoaMaxBytes / (1024 * 1024)} MB.",
                    true,
                    null);
            }

            // Confirm the target file exists before we allocate an attachment record.
            var existing = await _fillingCollection
                .Find(x => x.FileId == data.fileId)
                .FirstOrDefaultAsync();
            if (existing == null)
            {
                Console.WriteLine($"[ReAssign] No document found with fileId: {data.fileId}");
                return new ReAssignResult(false, $"File with fileId '{data.fileId}' was not found.", false, null);
            }

            // ---- Persist the POA using the shared attachment pipeline --------------------------
            var sanitizedOriginalName = SanitizePoaFileName(poa.fileName);
            var extension = Path.GetExtension(sanitizedOriginalName);
            if (string.IsNullOrEmpty(extension))
            {
                extension = GetExtensionFromContentType(poa.contentType);
            }

            var trustedFileName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + extension;
            var uploadedAt = DateTime.UtcNow;

            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = poa.contentType,
                Data = poa.data,
                Name = sanitizedOriginalName,
                Size = poa.data.LongLength,
                UploadedByUserId = data.userId,
                UploadedAtUtc = uploadedAt,
                AssociatedFileId = data.fileId,
                AssociationType = ReAssignPoaAssociationType,
            });

            var poaUrl = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";

            // ---- Resolve the new agent's full name from the users store -----------------------
            AppUser? newUser = null;
            if (!string.IsNullOrWhiteSpace(data.newOwner))
            {
                newUser = await _userCollection
                    .Find(u => u.CreatorId == data.newOwner)
                    .FirstOrDefaultAsync();
            }
            var resolvedNewName = !string.IsNullOrWhiteSpace(newUser?.Name)
                ? newUser!.Name
                : (newUser != null
                    ? $"{newUser.FirstName} {newUser.LastName}".Trim()
                    : null);
            if (string.IsNullOrWhiteSpace(resolvedNewName))
            {
                resolvedNewName = data.newCorrespondence?.name ?? string.Empty;
            }

            // ---- Resolve the previous agent's full name (fallback to user store / correspondence)
            var oldOwnerId = !string.IsNullOrWhiteSpace(data.oldId) ? data.oldId : existing.CreatorAccount;
            AppUser? oldUser = null;
            if (!string.IsNullOrWhiteSpace(oldOwnerId))
            {
                oldUser = await _userCollection
                    .Find(u => u.CreatorId == oldOwnerId)
                    .FirstOrDefaultAsync();
            }
            var resolvedOldName = !string.IsNullOrWhiteSpace(data.oldName)
                ? data.oldName
                : (!string.IsNullOrWhiteSpace(oldUser?.Name)
                    ? oldUser!.Name
                    : (oldUser != null
                        ? $"{oldUser.FirstName} {oldUser.LastName}".Trim()
                        : null));
            if (string.IsNullOrWhiteSpace(resolvedOldName))
            {
                resolvedOldName = data.oldCorrespondence?.name ?? string.Empty;
            }

            // ---- Structured oldValue / newValue objects for the FE ---------------------------
            var oldValue = new Dictionary<string, object?>
            {
                ["id"] = !string.IsNullOrWhiteSpace(data.oldId) ? data.oldId : oldOwnerId,
                ["name"] = resolvedOldName,
                ["email"] = data.oldCorrespondence?.email,
                ["phone"] = data.oldCorrespondence?.phone,
                ["address"] = data.oldCorrespondence?.address,
                ["state"] = data.oldCorrespondence?.state,
                ["nationality"] = existing.Correspondence?.Nationality
            };

            var newValue = new Dictionary<string, object?>
            {
                ["id"] = data.newOwner,
                ["name"] = resolvedNewName,
                ["email"] = data.newCorrespondence?.email,
                ["phone"] = data.newCorrespondence?.phone,
                ["address"] = data.newCorrespondence?.address,
                ["state"] = data.newCorrespondence?.state,
                ["nationality"] = data.newCorrespondence?.Nationality,
                ["attachments"] = new List<Dictionary<string, object?>>
                {
                    new()
                    {
                        ["fileName"] = sanitizedOriginalName,
                        ["contentType"] = poa.contentType,
                        ["url"] = poaUrl,
                    }
                }
            };

            var applicationType = data.applicationType ?? FormApplicationTypes.Ownership;
            var applicationDate = data.applicationDate ?? DateTime.UtcNow;
            var currentStatus = data.currentStatus ?? existing.FileStatus;

            // ---- Update the Filling: owner, correspondence, attachments list, audit trail -----
            var filter = Builders<Filling>.Filter.Eq(x => x.FileId, data.fileId);
            var update = Builders<Filling>.Update.Combine(
                Builders<Filling>.Update.Set(x => x.CreatorAccount, data.newOwner),
                Builders<Filling>.Update.Set(x => x.Correspondence, data.newCorrespondence),
                Builders<Filling>.Update.Push(x => x.Attachments, new AttachmentType
                {
                    name = ReAssignPoaAssociationType,
                    url = new List<string> { poaUrl }
                }),
                Builders<Filling>.Update.Push(x => x.ApplicationHistory, new ApplicationInfo()
                {
                    ApplicationType = applicationType,
                    ApplicationDate = applicationDate,
                    CurrentStatus = currentStatus,
                    PaymentId = data.paymentId,
                    CertificatePaymentId = data.certificatePaymentId,
                    FieldToChange = "ownership",
                    OldValue = oldValue,
                    NewValue = newValue,
                    StatusHistory =
                    [
                        new ApplicationHistory()
                        {
                            Date = applicationDate,
                            beforeStatus = ApplicationStatuses.AwaitingConfirmation,
                            afterStatus = currentStatus,
                            User = data.userName,
                            UserId = data?.userId,
                            Message =
                                $"Ownership transferred from {data?.oldName} (id: {data?.oldId}) to {resolvedNewName} (id: {data?.newOwner}). " +
                                $"Correspondence updated. Power of Attorney document attached: id={trustedFileName}, url={poaUrl}."
                        }
                    ]
                })
            );

            var options = new FindOneAndUpdateOptions<Filling>
            {
                ReturnDocument = ReturnDocument.After
            };

            var result = await _fillingCollection.FindOneAndUpdateAsync(filter, update, options);

            if (result == null)
            {
                Console.WriteLine($"[ReAssign] Update failed for fileId: {data.fileId} after POA upload.");
                return new ReAssignResult(false, "Failed to update the file.", false, null);
            }

            NormalizeOwnershipHistory(result);

            _log.LogInformation(
                "Ownership transferred from {OldName} ({OldId}) to {NewName} ({NewOwner}) on file {FileId} by {UserName} ({UserId})",
                data.oldName,
                data.oldId,
                resolvedNewName,
                data.newOwner,
                data.fileId,
                data.userName,
                data.userId);

            Console.WriteLine($"[ReAssign] Update successful for fileId: {data.fileId}, POA id: {trustedFileName}");
            return new ReAssignResult(true, null, false, result);
        }
        catch (FormatException fex)
        {
            Console.WriteLine($"[ReAssign] Invalid base64 POA data for fileId: {data?.fileId}. Exception: {fex.Message}");
            return new ReAssignResult(false, "Power of Attorney data is not valid base64.", true, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReAssign] Error occurred while updating fileId: {data?.fileId}. Exception: {ex.Message}");
            return new ReAssignResult(false, "An unexpected error occurred while processing the request.", false, null);
        }
    }

    private static string SanitizePoaFileName(string fileName)
    {
        // Strip any path components and normalize invalid characters, then cap length.
        var name = Path.GetFileName(fileName ?? string.Empty);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        // Also collapse path separators that may have slipped through on cross-platform input.
        name = name.Replace('/', '_').Replace('\\', '_');
        const int maxLen = 128;
        if (name.Length > maxLen)
        {
            var ext = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            if (stem.Length > maxLen - ext.Length)
            {
                stem = stem.Substring(0, Math.Max(1, maxLen - ext.Length));
            }
            name = stem + ext;
        }
        return string.IsNullOrWhiteSpace(name) ? "poa" : name;
    }

    // Ensures every Ownership ApplicationHistory entry exposes oldValue/newValue as objects
    // with at minimum a `name` field. Legacy documents that stored these as plain strings
    // are lazily projected to { name: <string> } so the FE can render them uniformly.
    // Even older rows (with only StatusHistory[0].Message populated) are reconstructed
    // by parsing the audit message that the old ReAssign flow wrote.
    private static void NormalizeOwnershipHistory(Filling? file)
    {
        if (file?.ApplicationHistory == null || file.ApplicationHistory.Count == 0) return;

        // Collect POA attachment URLs (added by the ChangeOfAgent flow) so we can
        // attach one to each legacy ownership row when the message doesn't carry a URL.
        var poaAttachmentUrls = file.Attachments?
            .Where(a => a != null
                        && !string.IsNullOrWhiteSpace(a.name)
                        && a.name!.IndexOf("ChangeOfAgentPOA", StringComparison.OrdinalIgnoreCase) >= 0
                        && a.url != null)
            .SelectMany(a => a.url!)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList() ?? new List<string>();
        var poaAttachmentIndex = 0;

        foreach (var entry in file.ApplicationHistory)
        {
            if (entry == null || entry.ApplicationType != FormApplicationTypes.Ownership) continue;

            entry.OldValue = CoerceOwnershipValue(entry.OldValue);
            entry.NewValue = CoerceOwnershipValue(entry.NewValue);

            var hasOld = HasName(entry.OldValue);
            var hasNew = HasName(entry.NewValue);
            if (hasOld && hasNew) continue;

            // Try to reconstruct from the legacy audit message.
            var message = entry.StatusHistory?
                .Select(s => s?.Message)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            if (string.IsNullOrWhiteSpace(message)) continue;

            var (oldParsed, newParsed) = ParseLegacyOwnershipMessage(message!);

            if (!hasOld && oldParsed != null) entry.OldValue = oldParsed;
            if (!hasNew && newParsed != null)
            {
                // Attach the next available POA URL as a fallback if the parser didn't find one.
                if (newParsed.TryGetValue("poa", out var poaObj) && poaObj is Dictionary<string, object?> poaDict)
                {
                    if (poaDict.TryGetValue("url", out var urlVal) && (urlVal == null || (urlVal is string us && string.IsNullOrWhiteSpace(us))))
                    {
                        if (poaAttachmentIndex < poaAttachmentUrls.Count)
                            poaDict["url"] = poaAttachmentUrls[poaAttachmentIndex++];
                    }
                }
                else if (poaAttachmentIndex < poaAttachmentUrls.Count)
                {
                    newParsed["poa"] = new Dictionary<string, object?>
                    {
                        ["url"] = poaAttachmentUrls[poaAttachmentIndex++]
                    };
                }
                entry.NewValue = newParsed;
            }
        }
    }

    private static bool HasName(object? value)
    {
        if (value is Dictionary<string, object?> dict &&
            dict.TryGetValue("name", out var n) &&
            n is string s &&
            !string.IsNullOrWhiteSpace(s))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Ensures every Assignment entry in a file's application history carries a fully populated
    /// ASSIGNOR block (previous owner) and ASSIGNEE block (new owner) so the SuperAdmin Assignment
    /// form can render both sides. Data is sourced (in priority order) from the entry's existing
    /// oldValue/newValue, then the stored <see cref="AssignmentType"/> object, and finally the
    /// file's current applicant (the owner on record). Both the preferred <c>assignment</c> object
    /// and the legacy <c>oldValue</c>/<c>newValue</c> fallback keys are written.
    /// </summary>
    private static void NormalizeAssignmentHistory(Filling? file)
    {
        if (file?.ApplicationHistory == null || file.ApplicationHistory.Count == 0) return;

        var currentOwner = file.applicants?.FirstOrDefault();

        static string? Get(object? payload, params string[] names)
            => patentdesign.Utils.ApplicationHistoryShaper.TryGetPayloadString(payload, names);

        // Reads a value from a nested object (e.g. oldValue.correspondence.email) after coercion.
        static string? GetNested(object? payload, string container, params string[] names)
        {
            if (payload is IDictionary<string, object?> dict)
            {
                foreach (var kv in dict)
                {
                    if (string.Equals(kv.Key, container, StringComparison.OrdinalIgnoreCase))
                        return Get(kv.Value, names);
                }
            }
            return null;
        }

        foreach (var entry in file.ApplicationHistory)
        {
            // Handle both Assignment (type 5) and Ownership (type 6) recordals — the SuperAdmin
            // Assignment form renders either as an assignor/assignee transfer.
            if (entry == null ||
                (entry.ApplicationType != FormApplicationTypes.Assignment &&
                 entry.ApplicationType != FormApplicationTypes.Ownership))
                continue;

            // Coerce Bson/JsonElement payloads into plain dictionaries first (same helper the
            // Ownership normalizer uses) so reads are reliable and the reassigned values below
            // serialize cleanly under System.Text.Json.
            entry.OldValue = CoerceOwnershipValue(entry.OldValue);
            entry.NewValue = CoerceOwnershipValue(entry.NewValue);

            var a = entry.Assignment;

            // ASSIGNOR — the current owner before the transfer.
            var assignorName        = Get(entry.OldValue, "assignorName", "name") ?? a?.assignorName ?? currentOwner?.Name;
            var assignorEmail       = Get(entry.OldValue, "assignorEmail", "email") ?? GetNested(entry.OldValue, "correspondence", "email") ?? currentOwner?.Email;
            var assignorPhone       = Get(entry.OldValue, "assignorPhone", "phone") ?? GetNested(entry.OldValue, "correspondence", "phone") ?? currentOwner?.Phone;
            var assignorNationality = Get(entry.OldValue, "assignorNationality", "nationality") ?? GetNested(entry.OldValue, "correspondence", "state") ?? currentOwner?.country;
            var assignorAddress     = Get(entry.OldValue, "assignorAddress", "address") ?? GetNested(entry.OldValue, "correspondence", "address") ?? a?.assignorAddress ?? currentOwner?.Address;
            var assignorCountry     = Get(entry.OldValue, "assignorCountry", "country") ?? a?.assignorCountry ?? currentOwner?.country;

            // ASSIGNEE — the new owner.
            var assigneeName        = Get(entry.NewValue, "assigneeName", "name") ?? a?.assigneeName;
            var assigneeEmail       = Get(entry.NewValue, "assigneeEmail", "email") ?? GetNested(entry.NewValue, "correspondence", "email");
            var assigneePhone       = Get(entry.NewValue, "assigneePhone", "phone") ?? GetNested(entry.NewValue, "correspondence", "phone");
            var assigneeNationality = Get(entry.NewValue, "assigneeNationality", "nationality") ?? GetNested(entry.NewValue, "correspondence", "state");
            var assigneeAddress     = Get(entry.NewValue, "assigneeAddress", "address") ?? GetNested(entry.NewValue, "correspondence", "address") ?? a?.assigneeAddress;
            var assigneeCountry     = Get(entry.NewValue, "assigneeCountry", "country") ?? a?.assigneeCountry;

            var dateOfAssignment = Get(entry.NewValue, "dateOfAssignment")
                ?? (a != null && a.dateOfAssignment != default ? a.dateOfAssignment.ToString("yyyy-MM-dd") : null);
            var deedUrl = Get(entry.NewValue, "assignmentDeedUrl", "deedOfAgreementUrl") ?? a?.deedOfAgreementUrl;
            var authUrl = Get(entry.NewValue, "authorizationLetterUrl") ?? a?.authorizationLetterUrl;

            // Merge (do NOT clobber): keep existing keys such as `correspondence` and `poa`
            // that the Ownership view relies on, while ADDING the assignor/assignee alias keys
            // the SuperAdmin Assignment form reads.
            var oldDict = entry.OldValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();
            var newDict = entry.NewValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();

            void Set(IDictionary<string, object?> d, string key, object? val)
            {
                if (val != null || !d.ContainsKey(key)) d[key] = val;
            }

            Set(oldDict, "name", assignorName);
            Set(oldDict, "email", assignorEmail);
            Set(oldDict, "phone", assignorPhone);
            Set(oldDict, "nationality", assignorNationality);
            Set(oldDict, "address", assignorAddress);
            Set(oldDict, "country", assignorCountry);
            Set(oldDict, "assignorName", assignorName);
            Set(oldDict, "assignorEmail", assignorEmail);
            Set(oldDict, "assignorPhone", assignorPhone);
            Set(oldDict, "assignorNationality", assignorNationality);
            Set(oldDict, "assignorAddress", assignorAddress);
            Set(oldDict, "assignorCountry", assignorCountry);
            entry.OldValue = oldDict;

            var attachments = new List<Dictionary<string, object?>>();
            if (!string.IsNullOrWhiteSpace(deedUrl))
                attachments.Add(new() { ["fileName"] = "Deed of Assignment", ["contentType"] = "application/pdf", ["url"] = deedUrl });
            if (!string.IsNullOrWhiteSpace(authUrl))
                attachments.Add(new() { ["fileName"] = "Authorization Letter", ["contentType"] = "application/pdf", ["url"] = authUrl });

            Set(newDict, "assigneeName", assigneeName);
            Set(newDict, "assigneeEmail", assigneeEmail);
            Set(newDict, "assigneePhone", assigneePhone);
            Set(newDict, "assigneeNationality", assigneeNationality);
            Set(newDict, "assigneeAddress", assigneeAddress);
            Set(newDict, "assigneeCountry", assigneeCountry);
            Set(newDict, "name", assigneeName);
            Set(newDict, "address", assigneeAddress);
            Set(newDict, "country", assigneeCountry);
            Set(newDict, "dateOfAssignment", dateOfAssignment);
            if (attachments.Count > 0) Set(newDict, "attachments", attachments);
            entry.NewValue = newDict;

            // Preferred path: a fully-populated `assignment` object (serialized camelCase).
            entry.Assignment = new AssignmentType
            {
                Id                     = a?.Id ?? Guid.NewGuid().ToString(),
                assignorName           = assignorName ?? string.Empty,
                assignorAddress        = assignorAddress ?? string.Empty,
                assignorCountry        = assignorCountry ?? string.Empty,
                assignorEmail          = assignorEmail ?? string.Empty,
                assignorPhone          = assignorPhone ?? string.Empty,
                assignorNationality    = assignorNationality ?? string.Empty,
                assigneeName           = assigneeName ?? string.Empty,
                assigneeAddress        = assigneeAddress ?? string.Empty,
                assigneeCountry        = assigneeCountry ?? string.Empty,
                assigneeEmail          = assigneeEmail ?? string.Empty,
                assigneePhone          = assigneePhone ?? string.Empty,
                assigneeNationality    = assigneeNationality ?? string.Empty,
                authorizationLetterUrl = authUrl ?? string.Empty,
                deedOfAgreementUrl     = deedUrl ?? string.Empty,
                assignmentDeedUrl      = deedUrl ?? string.Empty,
                dateOfAssignment       = a?.dateOfAssignment ?? default,
                receiptUrl             = a?.receiptUrl,
                acceptanceUrl          = a?.acceptanceUrl,
                rejectionUrl           = a?.rejectionUrl,
                acknowledgementUrl     = a?.acknowledgementUrl,
                message                = a?.message,
            };
        }
    }

    // Populates newValue/oldValue for the remaining recordal types so the SuperAdmin forms fill in:
    //   7  RegisteredUser  -> newValue { name, email, phone, nationality, address }
    //   8  Merger          -> newValue { name, email, phone, dateOfMerger, nationality, address }
    //   9  ChangeOfName    -> newValue { newName } ; oldValue { name }
    //   10 ChangeOfAddress -> newValue { newAddress } ; oldValue { address }
    private static void NormalizeRecordalHistory(Filling? file)
    {
        if (file?.ApplicationHistory == null || file.ApplicationHistory.Count == 0) return;

        var currentOwner = file.applicants?.FirstOrDefault();

        static string? Get(object? payload, params string[] names)
            => patentdesign.Utils.ApplicationHistoryShaper.TryGetPayloadString(payload, names);

        static void Set(IDictionary<string, object?> d, string key, object? val)
        {
            if (val != null || !d.ContainsKey(key)) d[key] = val;
        }

        foreach (var entry in file.ApplicationHistory)
        {
            if (entry == null) continue;

            switch (entry.ApplicationType)
            {
                case FormApplicationTypes.RegisteredUser:
                {
                    entry.NewValue = CoerceOwnershipValue(entry.NewValue);
                    var newDict = entry.NewValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                    Set(newDict, "name", Get(entry.NewValue, "name") ?? currentOwner?.Name ?? string.Empty);
                    Set(newDict, "email", Get(entry.NewValue, "email") ?? currentOwner?.Email ?? string.Empty);
                    Set(newDict, "phone", Get(entry.NewValue, "phone") ?? currentOwner?.Phone ?? string.Empty);
                    Set(newDict, "nationality", Get(entry.NewValue, "nationality") ?? currentOwner?.country ?? string.Empty);
                    Set(newDict, "address", Get(entry.NewValue, "address") ?? currentOwner?.Address ?? string.Empty);
                    entry.NewValue = newDict;
                    break;
                }
                case FormApplicationTypes.Merger:
                {
                    entry.NewValue = CoerceOwnershipValue(entry.NewValue);
                    var newDict = entry.NewValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                    Set(newDict, "name", Get(entry.NewValue, "name") ?? currentOwner?.Name ?? string.Empty);
                    Set(newDict, "email", Get(entry.NewValue, "email") ?? currentOwner?.Email ?? string.Empty);
                    Set(newDict, "phone", Get(entry.NewValue, "phone") ?? currentOwner?.Phone ?? string.Empty);
                    Set(newDict, "dateOfMerger", Get(entry.NewValue, "dateOfMerger") ?? string.Empty);
                    Set(newDict, "nationality", Get(entry.NewValue, "nationality") ?? currentOwner?.country ?? string.Empty);
                    Set(newDict, "address", Get(entry.NewValue, "address") ?? currentOwner?.Address ?? string.Empty);
                    entry.NewValue = newDict;
                    break;
                }
                case FormApplicationTypes.ChangeOfName:
                {
                    entry.OldValue = CoerceOwnershipValue(entry.OldValue);
                    entry.NewValue = CoerceOwnershipValue(entry.NewValue);
                    var oldDict = entry.OldValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                    var newDict = entry.NewValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                    Set(oldDict, "name", Get(entry.OldValue, "name") ?? currentOwner?.Name ?? string.Empty);
                    Set(newDict, "newName", Get(entry.NewValue, "newName", "name") ?? string.Empty);
                    entry.OldValue = oldDict;
                    entry.NewValue = newDict;
                    break;
                }
                case FormApplicationTypes.ChangeOfAddress:
                {
                    entry.OldValue = CoerceOwnershipValue(entry.OldValue);
                    entry.NewValue = CoerceOwnershipValue(entry.NewValue);
                    var oldDict = entry.OldValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                    var newDict = entry.NewValue as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                    Set(oldDict, "address", Get(entry.OldValue, "address") ?? currentOwner?.Address ?? string.Empty);
                    Set(newDict, "newAddress", Get(entry.NewValue, "newAddress", "address") ?? string.Empty);
                    entry.OldValue = oldDict;
                    entry.NewValue = newDict;
                    break;
                }
                default:
                    break;
            }
        }
    }

    private static (Dictionary<string, object?>? old, Dictionary<string, object?>? @new) ParseLegacyOwnershipMessage(string message)
    {
        // Legacy message shape (single-line or multi-line):
        //   Correspondence information changed from:
        //    Name:{...}, Address:{...}, State:{...}, number: {...}, email: {...}
        //    to
        //    Name:{...}, Address:{...}, State:{...}, number: {...}, email: {...}.
        //    previous owner: {oldName} with id: {oldId}.
        //    Power of Attorney document attached: id={fileId}, url={url}
        try
        {
            // Split into "from" and "to" halves. Support both "\n to" and " to " separators.
            var normalized = message.Replace("\r", " ").Replace("\n", " ");

            var fromIdx = normalized.IndexOf("changed from", StringComparison.OrdinalIgnoreCase);
            var toIdx = normalized.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
            string oldPart, newPart;
            if (fromIdx >= 0 && toIdx > fromIdx)
            {
                oldPart = normalized.Substring(fromIdx, toIdx - fromIdx);
                newPart = normalized.Substring(toIdx + 4);
            }
            else
            {
                // If we can't split, treat entire text as the "new" side and skip old.
                oldPart = string.Empty;
                newPart = normalized;
            }

            var oldCorr = ParseCorrespondence(oldPart);
            var newCorr = ParseCorrespondence(newPart);

            var previousOwnerName = ExtractBetween(newPart, "previous owner:", " with id:")
                ?? ExtractBetween(normalized, "previous owner:", " with id:");
            var previousOwnerId = ExtractBetween(newPart, "with id:", ".")
                ?? ExtractBetween(normalized, "with id:", ".");
            var poaUrl = ExtractBetween(newPart, "url=", null)
                ?? ExtractBetween(normalized, "url=", null);
            var poaFileId = ExtractBetween(newPart, "id=", ",")
                ?? ExtractBetween(normalized, "Power of Attorney document attached: id=", ",");

            // If the extracted previous owner value is missing or looks like an internal id
            // (guid/objectid/hex), prefer the human-readable correspondence name instead.
            var oldCorrespondenceName = oldCorr != null && oldCorr.TryGetValue("name", out var ocn)
                ? ocn as string
                : null;
            var resolvedOldName = string.IsNullOrWhiteSpace(previousOwnerName) || LooksLikeIdentifier(previousOwnerName)
                ? (!string.IsNullOrWhiteSpace(oldCorrespondenceName) ? oldCorrespondenceName : previousOwnerName?.Trim())
                : previousOwnerName!.Trim();

            Dictionary<string, object?>? oldValue = null;
            if (!string.IsNullOrWhiteSpace(resolvedOldName) || !string.IsNullOrWhiteSpace(previousOwnerId) || oldCorr != null)
            {
                oldValue = new Dictionary<string, object?>
                {
                    ["id"] = string.IsNullOrWhiteSpace(previousOwnerId) ? null : previousOwnerId!.Trim(),
                    ["name"] = resolvedOldName,
                    ["correspondence"] = oldCorr,
                };
            }

            Dictionary<string, object?>? newValue = null;
            if (newCorr != null || !string.IsNullOrWhiteSpace(poaUrl))
            {
                newValue = new Dictionary<string, object?>
                {
                    ["id"] = null,
                    ["name"] = newCorr != null && newCorr.TryGetValue("name", out var nname) ? nname : null,
                    ["correspondence"] = newCorr,
                };
                if (!string.IsNullOrWhiteSpace(poaUrl) || !string.IsNullOrWhiteSpace(poaFileId))
                {
                    newValue["poa"] = new Dictionary<string, object?>
                    {
                        ["fileName"] = string.IsNullOrWhiteSpace(poaFileId) ? null : poaFileId!.Trim(),
                        ["url"] = string.IsNullOrWhiteSpace(poaUrl) ? null : poaUrl!.Trim(),
                    };
                }
            }

            return (oldValue, newValue);
        }
        catch
        {
            return (null, null);
        }
    }

    private static Dictionary<string, object?>? ParseCorrespondence(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return null;

        string? name = ExtractBetween(segment, "Name:", ",");
        string? address = ExtractBetween(segment, "Address:", ",");
        string? state = ExtractBetween(segment, "State:", ",");
        string? phone = ExtractBetween(segment, "number:", ",");
        // email may be followed by "." or end-of-segment; try both terminators.
        string? email = ExtractBetween(segment, "email:", ".")
                        ?? ExtractBetween(segment, "email:", null);

        if (string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(address)
            && string.IsNullOrWhiteSpace(email)
            && string.IsNullOrWhiteSpace(phone)
            && string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["name"] = string.IsNullOrWhiteSpace(name) ? null : name!.Trim(),
            ["email"] = string.IsNullOrWhiteSpace(email) ? null : email!.Trim(),
            ["phone"] = string.IsNullOrWhiteSpace(phone) ? null : phone!.Trim(),
            ["address"] = string.IsNullOrWhiteSpace(address) ? null : address!.Trim(),
            ["state"] = string.IsNullOrWhiteSpace(state) ? null : state!.Trim(),
        };
    }

    private static string? ExtractBetween(string source, string startToken, string? endToken)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(startToken)) return null;
        var start = source.IndexOf(startToken, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += startToken.Length;
        if (start >= source.Length) return null;

        int end;
        if (string.IsNullOrEmpty(endToken))
        {
            end = source.Length;
        }
        else
        {
            end = source.IndexOf(endToken, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = source.Length;
        }

        var value = source.Substring(start, end - start).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Detects strings that look like an internal identifier rather than a human name:
    //   - Mongo ObjectId (24 hex chars)
    //   - GUID (with or without dashes / braces)
    //   - Pure numeric strings
    // Used so we prefer a readable correspondence name over a raw id in the FE payload.
    private static bool LooksLikeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim().Trim('{', '}');
        if (trimmed.Length == 0) return false;

        if (Guid.TryParse(trimmed, out _)) return true;

        if (trimmed.Length == 24 && trimmed.All(Uri.IsHexDigit)) return true;

        if (trimmed.All(char.IsDigit)) return true;

        // Contains no whitespace and no vowels? Likely opaque token, not a name.
        if (!trimmed.Any(char.IsWhiteSpace)
            && trimmed.Length >= 16
            && !trimmed.Any(c => "aeiouAEIOU".IndexOf(c) >= 0))
        {
            return true;
        }

        return false;
    }

    private static void NormalizeOwnershipHistory(IEnumerable<Filling>? files)
    {
        if (files == null) return;
        foreach (var f in files) NormalizeOwnershipHistory(f);
    }

    private static object? CoerceOwnershipValue(object? raw)
    {
        if (raw == null) return null;

        // Legacy string entry: wrap as { name: <string> } so the FE can render it.
        if (raw is string s)
        {
            return new Dictionary<string, object?> { ["name"] = s };
        }

        // MongoDB may deserialize object fields as BsonDocument; expose it as a plain
        // dictionary so System.Text.Json produces standard JSON for the FE.
        if (raw is MongoDB.Bson.BsonDocument bson)
        {
            return BsonToDictionary(bson);
        }

        return raw;
    }

    private static Dictionary<string, object?> BsonToDictionary(MongoDB.Bson.BsonDocument doc)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var element in doc.Elements)
        {
            dict[element.Name] = BsonValueToClr(element.Value);
        }
        return dict;
    }

    private static object? BsonValueToClr(MongoDB.Bson.BsonValue value)
    {
        if (value == null || value.IsBsonNull) return null;
        return value.BsonType switch
        {
            MongoDB.Bson.BsonType.Document => BsonToDictionary(value.AsBsonDocument),
            MongoDB.Bson.BsonType.Array => value.AsBsonArray.Select(BsonValueToClr).ToList(),
            MongoDB.Bson.BsonType.String => value.AsString,
            MongoDB.Bson.BsonType.Boolean => value.AsBoolean,
            MongoDB.Bson.BsonType.Int32 => value.AsInt32,
            MongoDB.Bson.BsonType.Int64 => value.AsInt64,
            MongoDB.Bson.BsonType.Double => value.AsDouble,
            MongoDB.Bson.BsonType.DateTime => value.ToUniversalTime(),
            MongoDB.Bson.BsonType.ObjectId => value.AsObjectId.ToString(),
            _ => value.ToString()
        };
    }




    private async Task<string?> saveAck(OtherPaymentModel data)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        var bytes = new OtherAck(data).GeneratePdf();
        using (var ms = new MemoryStream(bytes))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }
        return uri;
    }

    private async Task<Dictionary<string, object>> GenerateReceipt(Receipt dataReceipt, Filling tradeData)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        var bytes = new ReceiptModel(dataReceipt, uri, tradeData).GeneratePdf();
        return new Dictionary<string, object>()
        {
            ["data"] = bytes,
            ["type"] = "application/pdf",
            ["name"] = trustedFileName
        };
    }

    public async Task<object?> DashboardRenew(string fileId, string userName, string userId)
    {
        var file = _fillingCollection.Find(x => x.Id == fileId).FirstOrDefault();
        var applicants = file.applicants.Count > 1 ? file.applicants[0].Name + " et al." : file.applicants[0].Name;
        if (applicants?.Length > 75)
        {
            applicants = applicants.Trim().Substring(0, 75);
        }
        var email = file.applicants[0].Email;
        var phone = file.applicants[0].Phone;
        var paymentInfo = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, file.Type, "", file.DesignType, null);
        var rrr = await _remitaPaymentUtils.
            GenerateRemitaPaymentId(paymentInfo.Item1, paymentInfo.Item3, paymentInfo.Item2,
                $"Rights Renewal for {file.Type.ToString()}", applicants, email, phone);
        if (rrr != null)
        {
            _fillingCollection.FindOneAndUpdate(x => x.Id == file.Id,
                Builders<Filling>.Update.Push(f => f.ApplicationHistory, new ApplicationInfo()
                {
                    PaymentId = rrr,
                    ApplicationType = FormApplicationTypes.LicenseRenewal,
                    CurrentStatus = ApplicationStatuses.AwaitingPayment,
                    ApplicationDate = DateTime.Now,
                    LicenseType = "Renewal",
                    StatusHistory =
                    [
                        new ApplicationHistory()
                         {
                             beforeStatus = ApplicationStatuses.None,
                             afterStatus = ApplicationStatuses.AwaitingPayment,
                             Message = "Remita ID generated, awaiting Payment",
                             Date = DateTime.Now,
                             User = userName,
                             UserId = userId
                         }
                    ]
                }));
        }

        return new
        {
            rrr,
            file.Id,
            title = file.Type == FileTypes.Patent ? file.TitleOfInvention : file.Type == FileTypes.Design ? file.TitleOfDesign : file.TitleOfTradeMark,
            applicant = applicants
        };
    }

    public object? UserNotifications(string? userId = null, bool? staffTickets = false, bool? showAllOpposition = false)
    {
        long ticketsCount = 0;
        long oppositionCount = 0;
        if (staffTickets == true)
        {
            ticketsCount = _ticketsCollection.Find(Builders<TicketInfo>.Filter.Eq(r => r.Status, TicketState.AwaitingStaff)).CountDocuments();
        }
        else
        {
            ticketsCount = _ticketsCollection.Find(Builders<TicketInfo>.Filter.And([
                Builders<TicketInfo>.Filter.Eq(r => r.creatorId, userId),
                 Builders<TicketInfo>.Filter.Eq(r => r.Status, TicketState.AwaitingUser),
             ])).CountDocuments();

        }
        if (showAllOpposition == false)
        {
            oppositionCount = _oppositionCollection.Find(Builders<OppositionType>.Filter.Or([
            Builders<OppositionType>.Filter.Eq(e=>e.fileCreatorId,userId),
             Builders<OppositionType>.Filter.Eq(e=>e.creatorId,userId),
             ])).CountDocuments();
        }

        if (showAllOpposition == true)
        {
            oppositionCount = _oppositionCollection.CountDocuments(Builders<OppositionType>.Filter.Empty);
        }
        return new
        {
            support = ticketsCount,
            opposition = oppositionCount
        };
    }

    //public async Task<Filling?> UpdateCorThis(string id, string userId)
    //{
    //    var corr = await _userCollection.Find(d => d.Id == userId).Project(x => x.DefaultCorrespondence).FirstOrDefaultAsync();
    //    var updated = await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, id),
    //        Builders<Filling>.Update.Set(d => d.Correspondence, corr), new FindOneAndUpdateOptions<Filling>()
    //        {
    //            ReturnDocument = ReturnDocument.After
    //        });
    //    return updated;
    //}

    //public async Task<Filling?> UpdateCorAll(string id, string userId, string creatorAccount)
    //{
    //    var filter = Builders<Filling>.Filter;
    //    var defaultdata = await _userCollection.Find(d => d.id == userId).Project(x => x.DefaultCorrespondence).FirstOrDefaultAsync();
    //    await _fillingCollection.UpdateManyAsync(Builders<Filling>.Filter.And([
    //        Builders<Filling>.Filter.Eq(x => x.CreatorAccount, creatorAccount),
    //         Builders<Filling>.Filter.Or([
    //              filter.Eq(r=> r.Correspondence, null),
    //              filter.Eq(r=>r.Correspondence.name, "null"),
    //              filter.Eq(r=>r.Correspondence.address, "null"),
    //              filter.Eq(r=>r.Correspondence.email, "null"),
    //              filter.Eq(r=>r.Correspondence.phone, "null"),
    //              filter.Eq(r=>r.Correspondence.state, "null"),
    //              filter.Eq(r=>r.Correspondence.name, "NULL"),
    //              filter.Eq(r=>r.Correspondence.address, "NULL"),
    //              filter.Eq(r=>r.Correspondence.email, "NULL"),
    //              filter.Eq(r=>r.Correspondence.phone, "NULL"),
    //              filter.Eq(r=>r.Correspondence.state, "NULL"),
    //              filter.Eq(r=>r.Correspondence.name, "-"),
    //              filter.Eq(r=>r.Correspondence.address, "-" ),
    //              filter.Eq(r=>r.Correspondence.email,"-"),
    //              filter.Eq(r=>r.Correspondence.phone, "-"),
    //              filter.Eq(r=> r.Correspondence.state,"-"),
    //              filter.Eq(r=>r.Correspondence.name, null ),
    //              filter.Eq(r=>r.Correspondence.address,null),
    //              filter.Eq(r=>r.Correspondence.email, null),
    //              filter.Eq(r=>r.Correspondence.phone, null),
    //              filter.Eq(r=>r.Correspondence.state , null),
    //         ])
    //    ]), Builders<Filling>.Update.Set(d => d.Correspondence, defaultdata));
    //    var current = await _fillingCollection.Find(d => d.Id == id).FirstOrDefaultAsync();
    //    return current;
    //}

    public async Task<List<StatusRequests>?> GetUserStatusRequests(string? userId, int count = 10, int skip = 0)
    {
        if (userId == null)
        {
            return await _statusCollection.Find(x => x.Id != "").Limit(count).Skip(skip).ToListAsync() ?? [];
        }

        else
        {
            return await _statusCollection.Find(x => x.userId == userId).Limit(count).Skip(skip).ToListAsync() ?? [];
        }
    }

    public async Task<StatusRequests?> updateStatusRequest(string requestId, bool? simulate = false)
    {
        var result = await _statusCollection.FindOneAndUpdateAsync(
            Builders<StatusRequests>.Filter.Eq(x => x.Id, requestId),
            Builders<StatusRequests>.Update.Combine([
            Builders<StatusRequests>.Update.Set(x => x.status, ApplicationStatuses.Active),
             Builders<StatusRequests>.Update.Set(x => x.receiptLetter, ApplicationLetters.StatusRequestReceipt),
             Builders<StatusRequests>.Update.Set(x => x.ackLetter, ApplicationLetters.StatusRequestAck),
            ]),
            new FindOneAndUpdateOptions<StatusRequests>()
            {
                ReturnDocument = ReturnDocument.After
            });
        var rrr = await CheckStatusViaOrderId(result.paymentId);

        saveFinance(rrr.Item2, "Status Check Application", result.Id, "-", "Nigeria");
        return result;
    }

    public async Task<Dictionary<string, object?>> GetStatusFromId(string fileNumber)
    {
        var foundFiles = _fillingCollection.Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileNumber),
            new FindOptions()
            {
                Collation = new Collation("en_US",
                    strength: new Optional<CollationStrength?>(CollationStrength.Primary))
            }).FirstOrDefault();
        List<byte[]>? images = null;
        if (foundFiles.Attachments.Any(d => d.name == "representation" || d.name == "representations"))
        {
            try
            {
                foreach (var imageLink in foundFiles.Attachments
                             .FirstOrDefault(x => x.name == "representation" || x.name == "representations").url)
                {
                    images.Add(await (new HttpClient()).GetByteArrayAsync(imageLink));
                }
            }
            catch (Exception e)
            {
                images = null;
            }
        }

        var generatedData = new StatusSearchPdf(foundFiles, images).GeneratePdf();
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        return new Dictionary<string, object>()
        {
            ["data"] = generatedData,
            ["type"] = "application/pdf",
            ["name"] = trustedFileName
        };

    }

    public async Task<object?> StatusCheck(string fileNumber, string userId, Dictionary<string, object>? data = null)
    {
        // check if user has paid for it before
        var userCreated = _statusCollection.Find(Builders<StatusRequests>.Filter.And([
            Builders<StatusRequests>.Filter.Eq(d => d.fileId, fileNumber),
             Builders<StatusRequests>.Filter.Eq(d => d.userId, userId),
             Builders<StatusRequests>.Filter.Ne(d => d.status, ApplicationStatuses.AwaitingPayment),
         ]), new FindOptions()
         {
             Collation = new Collation("en_US", strength: new Optional<CollationStrength?>(CollationStrength.Primary))
         }).Project(d => d.Id).FirstOrDefault();
        if (userCreated != null)
        {
            return new { status = "already_paid_for", data = userCreated };
        }

        var foundFiles = await _fillingCollection.Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileNumber),
            new FindOptions()
            {
                Collation = new Collation("en_US", strength: new Optional<CollationStrength?>(CollationStrength.Primary))
            }).Project(d => new
            {
                d.FileId,
                d.CreatorAccount,
                title = d.TitleOfDesign ?? d.TitleOfInvention ?? d.TitleOfTradeMark,
                status = d.ApplicationHistory.Select(e =>
                     new
                     {
                         e.ApplicationType,
                         e.CurrentStatus,
                         e.ApplicationDate
                     })
            }).ToListAsync();

        if (foundFiles.Count == 0)
        {
            // file number does not exist
            return new { status = "not_found" };
        }
        if (foundFiles.Any(e => e.CreatorAccount != userId))
        {
            // requires payment, return amount due.
            var applicantName = data["applicantName"];
            var applicantEmail = data["applicantEmail"];
            var applicantPhone = data["applicantPhone"];
            var (amount, serviceId, serviceFee) =
                _remitaPaymentUtils.GetCost(PaymentTypes.statusCheck, FileTypes.Patent, "");
            var remitaResponse = await _remitaPaymentUtils.GenerateRemitaPaymentId(amount, serviceFee, serviceId,
                $"Status Check for {fileNumber}",
                applicantName.ToString(), applicantEmail.ToString(), applicantPhone.ToString()
            );
            var newRequestId = Guid.NewGuid().ToString();
            if (remitaResponse != null)
            {
                await _statusCollection.InsertOneAsync(new StatusRequests()
                {
                    Id = newRequestId,
                    userId = userId.ToString(),
                    paymentId = remitaResponse,
                    status = ApplicationStatuses.AwaitingPayment,
                    fileId = fileNumber,
                    date = DateTime.Now,
                    applicantName = applicantName.ToString()
                });
            }
            return new { status = "requires_payment", data = new { remitaResponse, amount, newRequestId } };
        }
        return new { status = "file_belongs_to_user" };
    }

    public async Task<Dictionary<string, object?>?> GetStatusFromRequestId(string requestId, string userId, bool isAdmin)
    {
        string? fileNumber = null;
        fileNumber = isAdmin
            ? _statusCollection.Find(d => d.Id == requestId).Project(x => x.fileId).FirstOrDefault()
            : _statusCollection.Find(d => d.Id == requestId && d.userId == userId).Project(x => x.fileId)
                .FirstOrDefault();
        return fileNumber != null ? await GetStatusFromId(fileNumber) : null;
    }

    public async Task<bool> Updatemanystatus(UpdateMany req)
    {
        try
        {
            var newStatus = Enum.GetValues<ApplicationStatuses>()[req.newStatus];
            await _fillingCollection.UpdateManyAsync(Builders<Filling>.Filter.In(x => x.Id, req.files),
                Builders<Filling>.Update.Combine([
                    Builders<Filling>.Update.Set(x=>x.FileStatus, newStatus),
                     Builders<Filling>.Update.Set(x=>x.ApplicationHistory[0].CurrentStatus, newStatus),
                     Builders<Filling>.Update.Push(x=>x.ApplicationHistory[0].StatusHistory, new ApplicationHistory()
                     {
                         afterStatus = newStatus,
                         Message = req.reasons,
                         Date = DateTime.Now,
                         User = req.userName,
                         UserId = req.userId,
                     }),
                ]));
            throw new NotImplementedException();
            return true;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public async Task<AvailabilitySearchDto> AvailabilitySearchCost(string name, string email)
    {
        var data = _remitaPaymentUtils.GetCost(PaymentTypes.AvailabilitySearch, null, "", null, null, null);


        var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
            data.Item1, data.Item3, data.Item2, "Availability Search",
            name, email, "");
        var searchCost = new AvailabilitySearchDto
        {
            cost = data.Item1,
            rrr = paymentId
        };
        return searchCost;
    }

    public async Task<RecordalDto> StatusSearchCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.StatusSearch, fileType, "", null, null, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Status Search",
                applicant.Name, applicant.Email, applicant.Phone);

            var statusSearchCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                TrademarkClass = fileInfo.TrademarkClass
            };

            return statusSearchCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-StatusSearchCost");
            throw;
        }
    }

    public async Task<RecordalDto> PatentAssignmentCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            // Check if there is already a pending/created assignment application
            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.Assignment &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfInvention ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantPhone = applicant.Phone,
                ApplicantCity = applicant.city,
                PatentType = fileInfo.PatentType,
                PatentApplicationType = fileInfo.PatentApplicationType,
                TitleOfInvention = fileInfo.TitleOfInvention,
                FileOrigin = fileInfo.FileOrigin
            };

            if (existingApp != null)
            {
                // Do NOT generate a new RRR; just tell the frontend an app already exists
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            // Normal cost + RRR generation path
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PatentAssignment, fileType, "", null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Patent Assignment",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = data.Item1;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-PatentAssignmentCost");
            throw;
        }
    }

    public async Task<RecordalDto> PatentLicenseCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.License &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfInvention ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantPhone = applicant.Phone,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city,
                PatentType = fileInfo.PatentType,
                PatentApplicationType = fileInfo.PatentApplicationType,
                TitleOfInvention = fileInfo.TitleOfInvention,
                FileOrigin = fileInfo.FileOrigin
            };

            if (existingApp != null)
            {
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PatentLicense, fileType, "", null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Patent License",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = data.Item1;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-PatentLicenseCost");
            throw;
        }
    }
    
    public async Task<RecordalDto> PatentMortgageCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.Mortgage &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfInvention ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantPhone = applicant.Phone,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city,
                PatentType = fileInfo.PatentType,
                PatentApplicationType = fileInfo.PatentApplicationType,
                TitleOfInvention = fileInfo.TitleOfInvention,
                FileOrigin = fileInfo.FileOrigin
            };

            if (existingApp != null)
            {
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PatentMortgage, fileType, "", null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Patent Mortgage",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = data.Item1;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-PatentMortgageCost");
            throw;
        }
    }

    public async Task<RecordalDto> PatentMergerCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.Merger &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfInvention ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantPhone = applicant.Phone,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city,
                PatentType = fileInfo.PatentType,
                PatentApplicationType = fileInfo.PatentApplicationType,
                TitleOfInvention = fileInfo.TitleOfInvention,
                FileOrigin = fileInfo.FileOrigin
            };

            if (existingApp != null)
            {
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PatentMerger, fileType, "", null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Patent Merger",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = data.Item1;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-PatentMergerCost");
            throw;
        }
    }

    public async Task<RecordalDto> PatentCtcCost(string fileId, FileTypes fileType, int numberOfAttachments = 1)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            // ✅ Check if there is already a pending/created CTC application
            //var existingApp = fileInfo.ApplicationHistory?
            //    .FirstOrDefault(a =>
            //        a.ApplicationType == FormApplicationTypes.CertifiedTrueCopy &&
            //        !string.IsNullOrWhiteSpace(a.PaymentId));

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.CertifiedTrueCopy &&
                    !string.IsNullOrWhiteSpace(a.PaymentId) &&
                    (a.CurrentStatus == ApplicationStatuses.Approved ||
                     a.CurrentStatus == ApplicationStatuses.AwaitingRecordalProcess));


            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfInvention ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantPhone = applicant.Phone,
                ApplicantCity = applicant.city,
                PatentType = fileInfo.PatentType,
                PatentApplicationType = fileInfo.PatentApplicationType,
                TitleOfInvention = fileInfo.TitleOfInvention,
                FileOrigin = fileInfo.FileOrigin,
                Attachments = fileInfo.Attachments ?? new List<AttachmentType>()
            };

            if (existingApp != null)
            {
                // Do NOT generate a new RRR; just tell the frontend an app already exists
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            // Normal cost + RRR generation path
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PatentCtc, fileType, "", null, null, null);

            // MULTIPLY the cost by number of attachments
            decimal baseAmount = decimal.Parse(data.Item1);
            decimal finalAmount = baseAmount * numberOfAttachments;
            string finalAmountStr = finalAmount.ToString();

            // Generate RRR with the FINAL (multiplied) amount
            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                finalAmountStr, data.Item3, data.Item2, "Patent CTC",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = finalAmountStr; // Return the multiplied amount
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-PatentCtcCost");
            throw;
        }
    }

    public async Task<RecordalDto> PatentAmendmentCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            // ✅ Check if there is already a pending/created amendment application
            //var existingApp = fileInfo.ApplicationHistory?
            //    .FirstOrDefault(a =>
            //        a.ApplicationType == FormApplicationTypes.Amendment &&
            //        !string.IsNullOrWhiteSpace(a.PaymentId));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfInvention ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantPhone = applicant.Phone,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city,
                PatentType = fileInfo.PatentType,
                PatentApplicationType = fileInfo.PatentApplicationType,
                TitleOfInvention = fileInfo.TitleOfInvention,
                FileOrigin = fileInfo.FileOrigin,

                Applicants = fileInfo.applicants,
                Inventors = fileInfo.Inventors,
                PriorityInfo = fileInfo.PriorityInfo,
                FirstPriorityInfo = fileInfo.FirstPriorityInfo,
                PatentAbstract = fileInfo.PatentAbstract,
                Correspondence = fileInfo.Correspondence,
            };

            //if (existingApp != null)
            //{
            //    // Do NOT generate a new RRR; just tell the frontend an app already exists
            //    dto.HasExistingApplication = true;
            //    dto.ExistingApplicationId = existingApp.id;
            //    dto.ExistingRRR = existingApp.PaymentId;
            //    return dto;
            //}

            // Normal cost + RRR generation path
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PatentAmendment, fileType, "", null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Patent Amendment",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = data.Item1;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-PatentAmendmentCost");
            throw;
        }
    }

    public async Task<RecordalDto> DesignAmendmentCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfDesign ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantPhone = applicant.Phone,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city,
                DesignType = fileInfo.DesignType,
                TitleOfDesign = fileInfo.TitleOfDesign,
                StatementOfNovelty = fileInfo.StatementOfNovelty,
                FileOrigin = fileInfo.FileOrigin,
                DesignCreators = fileInfo.DesignCreators,
                Applicants = fileInfo.applicants,
                PriorityInfo = fileInfo.PriorityInfo,
                FirstPriorityInfo = fileInfo.FirstPriorityInfo,
                Correspondence = fileInfo.Correspondence,
                Attachments = fileInfo.Attachments ?? new List<AttachmentType>(),
            };

            var data = _remitaPaymentUtils.GetCost(PaymentTypes.DesignAmendment, fileType, "", null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Design Amendment",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = data.Item1;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-DesignAmendmentCost");
            throw;
        }
    }

    //Design licence costs
    public async Task<RecordalDto> DesignAssignmentCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.DesignAssignment, fileType, "", null, null, null);
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();
            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }
            var applicant = fileInfo.applicants[0];

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.Assignment &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var designAssignmentCost = new RecordalDto
            {
                FileId = fileInfo.FileId,
                FileTitle = fileInfo.TitleOfDesign ?? string.Empty,
                TitleOfInvention = fileInfo.TitleOfDesign ?? string.Empty,
                FileOrigin = fileInfo.FileOrigin ?? fileInfo.FilingCountry,
                DesignType = fileInfo.DesignType,
                DesignTypeDescription = fileInfo.DesignType?.ToString(),
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantPhone = applicant.Phone,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city,
                TrademarkClass = fileInfo.TrademarkClass
            };

            if (existingApp != null)
            {
                designAssignmentCost.HasExistingApplication = true;
                designAssignmentCost.ExistingApplicationId = existingApp.id;
                designAssignmentCost.ExistingRRR = existingApp.PaymentId;
                return designAssignmentCost;
            }

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Design Assignment",
                applicant.Name, applicant.Email, applicant.Phone);

            designAssignmentCost.Amount = data.Item1;
            designAssignmentCost.rrr = paymentId;

            return designAssignmentCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-Design Assignment Cost retrieval");
            throw;
        }
    }
    public async Task<RecordalDto?> DesignLicenseCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.License &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var dto = new RecordalDto
            {
                FileId = fileInfo.FileId,
                FileTitle = fileInfo.TitleOfDesign ?? string.Empty,
                DesignType = fileInfo.DesignType,
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantPhone = applicant.Phone,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city
            };

            if (existingApp != null)
            {
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            var (amount, narration, serviceFee) = _remitaPaymentUtils
                .GetCost(PaymentTypes.DesignLicense, fileType, string.Empty, null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                amount,
                serviceFee,
                narration,
                "Design License",
                applicant.Name,
                applicant.Email,
                applicant.Phone);

            dto.Amount = amount;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-Design License Cost retrieval");
            throw;
        }
    }
    public async Task<RecordalDto> DesignMergerCost(string fileId, FileTypes fileType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.Merger &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfDesign ?? string.Empty,
                TitleOfInvention = fileInfo.TitleOfDesign ?? string.Empty,
                FileOrigin = fileInfo.FileOrigin ?? fileInfo.FilingCountry,
                DesignType = fileInfo.DesignType,
                DesignTypeDescription = fileInfo.DesignType?.ToString(),
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantPhone = applicant.Phone,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city,
                TrademarkClass = fileInfo.TrademarkClass
            };

            if (existingApp != null)
            {
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            var data = _remitaPaymentUtils.GetCost(PaymentTypes.DesignMerger, fileType, "", null, null, null);

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Design Merger",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = data.Item1;
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-Design Merger Cost retrieval");
            throw;
        }
    }
    public async Task<RecordalDto> DesignCtcCost(string fileId, FileTypes fileType, int numberOfAttachments = 1)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            // ✅ Check if there is already a pending/created CTC application
            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.CertifiedTrueCopy &&
                    !string.IsNullOrWhiteSpace(a.PaymentId) &&
                    (a.CurrentStatus == ApplicationStatuses.Approved ||
                     a.CurrentStatus == ApplicationStatuses.AwaitingRecordalProcess));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfDesign ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantPhone = applicant.Phone,
                ApplicantCity = applicant.city,
                DesignType = fileInfo.DesignType,
                TitleOfInvention = fileInfo.TitleOfDesign,
                FileOrigin = fileInfo.FileOrigin,
                TrademarkClass = fileInfo.TrademarkClass,
                Attachments = fileInfo.Attachments ?? new List<AttachmentType>()
            };

            if (existingApp != null)
            {
                // Do NOT generate a new RRR; just tell the frontend an app already exists
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            // Normal cost + RRR generation path
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.DesignCtc, fileType, "", null, null, null);

            // ADD base cost + service fee, then MULTIPLY by number of attachments
            decimal baseAmount = decimal.Parse(data.Item1);
            decimal serviceFee = decimal.Parse(data.Item3);
            decimal finalAmount = (baseAmount + serviceFee) * numberOfAttachments;
            string finalAmountStr = finalAmount.ToString();

            // Generate RRR with the FINAL total
            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                finalAmountStr, data.Item3, data.Item2, "Design CTC",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = finalAmountStr; // Return the multiplied amount
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-DesignCtcCost");
            throw;
        }
    }

    public async Task<RecordalDto> TrademarkCtcCost(string fileId, FileTypes fileType, int numberOfAttachments = 1)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            // Check if there is already a pending/created CTC application
            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.CertifiedTrueCopy &&
                    !string.IsNullOrWhiteSpace(a.PaymentId) &&
                    (a.CurrentStatus == ApplicationStatuses.Approved ||
                     a.CurrentStatus == ApplicationStatuses.AwaitingPayment ||
                     a.CurrentStatus == ApplicationStatuses.AwaitingRecordalProcess));

            var dto = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantPhone = applicant.Phone,
                ApplicantCity = applicant.city,
                TitleOfInvention = fileInfo.TitleOfTradeMark,
                FileOrigin = fileInfo.FileOrigin,
                TrademarkClass = fileInfo.TrademarkClass,
                Attachments = fileInfo.Attachments ?? new List<AttachmentType>()
            };

            if (existingApp != null)
            {
                // Do NOT generate a new RRR; just tell the frontend an app already exists
                dto.HasExistingApplication = true;
                dto.ExistingApplicationId = existingApp.id;
                dto.ExistingRRR = existingApp.PaymentId;
                return dto;
            }

            // Normal cost + RRR generation path
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.TrademarkCtc, fileType, "", null, null, null);

            // ADD base cost + service fee, then MULTIPLY by number of attachments
            decimal baseAmount = decimal.Parse(data.Item1);
            decimal serviceFee = decimal.Parse(data.Item3);
            decimal finalAmount = (baseAmount + serviceFee) * numberOfAttachments;
            string finalAmountStr = finalAmount.ToString();

            // Generate RRR with the FINAL total
            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                finalAmountStr, data.Item3, data.Item2, "Trademark CTC",
                applicant.Name, applicant.Email, applicant.Phone);

            dto.Amount = finalAmountStr; // Return the multiplied amount
            dto.rrr = paymentId;

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-TrademarkCtcCost");
            throw;
        }
    }

    public async Task<bool> NewDesignCtcApplication(DesignCtcDto dto, string userId)
    {
        _log.LogInformation($"[NewDesignCtcApplication] Starting - FileId: {dto.FileId}, RRR: {dto.Rrr}, UserId: {userId}, AttachmentCount: {dto.AttachmentIds?.Count ?? 0}");

        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning($"[NewDesignCtcApplication] File not found - FileId: {dto.FileId}");
            return false;
        }

        var applicant = file.applicants.FirstOrDefault();

        // Fetch user for performance tracking
        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
            throw new UnauthorizedAccessException("User not found");

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "CTC application submitted, awaiting payment";

        // Application history
        var ctcHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.CertifiedTrueCopy,
            CurrentStatus = status,
            ApplicationDate = dto.CtcRequestDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Design CTC Application",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.CtcRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = status,
                    Message = statusMessage,
                    User = user.FirstName + " " + user.LastName,
                    UserId = user.Id
                }
            }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = ctcHistory.id,
            RecordalType = "Design CTC Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            FilingDate = (dto.CtcRequestDate ?? DateTime.Now).ToString(),
            RequestedAttachments = dto.AttachmentIds,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, ctcHistory);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.DesignCtc, file.FileId, ctcHistory.id);
        }

        _log.LogInformation($"[NewDesignCtcApplication] Completed successfully - FileId: {dto.FileId}, AppId: {ctcHistory.id}, PaymentSuccessful: {paymentSuccessful}");
        return true;
    }

    public async Task<RecordalDto> DesignMortgageCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.DesignMortgage, fileType, "", null, null, null);
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();
            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }
            var applicant = fileInfo.applicants[0];

            var existingApp = fileInfo.ApplicationHistory?
                .FirstOrDefault(a =>
                    a.ApplicationType == FormApplicationTypes.Mortgage &&
                    !string.IsNullOrWhiteSpace(a.PaymentId));

            var designMortgageCost = new RecordalDto
            {
                FileId = fileId,
                FileTitle = fileInfo.TitleOfDesign ?? "",
                TitleOfInvention = fileInfo.TitleOfDesign ?? string.Empty,
                FileOrigin = fileInfo.FileOrigin ?? fileInfo.FilingCountry,
                DesignType = fileInfo.DesignType,
                ApplicantName = applicant.Name,
                ApplicantEmail = applicant.Email,
                ApplicantPhone = applicant.Phone,
                ApplicantAddress = applicant.Address,
                ApplicantNationality = applicant.country,
                ApplicantState = applicant.State,
                ApplicantCity = applicant.city
              //  TrademarkClass = fileInfo.TrademarkClass
            };

            if (existingApp != null)
            {
                designMortgageCost.HasExistingApplication = true;
                designMortgageCost.ExistingApplicationId = existingApp.id;
                designMortgageCost.ExistingRRR = existingApp.PaymentId;
                return designMortgageCost;
            }

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Design Mortgage",
                applicant.Name, applicant.Email, applicant.Phone);

            designMortgageCost.Amount = data.Item1;
            designMortgageCost.rrr = paymentId;

            return designMortgageCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-Design Mortgage Cost retrieval");
            throw;
        }
    }

    public async Task<RecordalDto> GetPublicationStatusUpdateCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PublicationStatusUpdate, fileType, "", null, null, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GeneratePublicationStatusUpdateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "File Publication Status Update",
                applicant.Name, applicant.Email, applicant.Phone);

            var publicationStatusUpdateCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                TrademarkClass = fileInfo.TrademarkClass
            };

            return publicationStatusUpdateCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-Publication Status Update");
            throw;
        }
    }

    public async Task<RecordalDto> GetFileWithdrawalCost(string fileId, FileTypes fileType)
    {
        try
        {
            _log.LogInformation($"[GetFileWithdrawalCost] Starting - FileId: {fileId}, FileType: {fileType}");
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.FileWithdrawal, fileType, "", null, null, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                _log.LogError("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "File Withdrawal",
                applicant.Name, applicant.Email, applicant.Phone);

            var fileWithdrawalCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? fileInfo.TitleOfInvention ?? fileInfo.TitleOfDesign ?? "",
                ApplicantName = applicant.Name,
                TrademarkClass = fileInfo.TrademarkClass
            };

            return fileWithdrawalCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at- File withdrawal Cost retrieval");
            throw;
        }
    }

    public async Task<PatentClericalUpdateDto> GetPatentClericalUpdateCost(string fileId, FileTypes fileType, string? updateType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            var data = _remitaPaymentUtils.GetCost(PaymentTypes.PatentClericalUpdate, fileType, "", null, null, null);

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                throw new Exception("File not found or no applicants available.");
            }

            var firstApplicant = fileInfo.applicants[0];

            string paymentId = null;
            if (fileInfo.FileStatus != ApplicationStatuses.Withdrawn)
            {
                paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                    data.Item1, data.Item3, data.Item2, "Patent Clerical Update",
                    firstApplicant.Name, firstApplicant.Email, firstApplicant.Phone);
            }
            else
            {
                paymentId = "Free";
            }

            var updateCost = new PatentClericalUpdateDto
            {
                Cost = data.Item1,
                PaymentRRR = paymentId,
                FileStatus = fileInfo.FileStatus,
                FileId = fileId,
                FileType = fileInfo.Type,
                UpdateType = "Patent Clerical Update",
                PatentType = fileInfo.PatentType,
                PatentApplicationType = fileInfo.PatentApplicationType,
                FileOrigin = fileInfo.FileOrigin,
                TitleOfInvention = fileInfo.TitleOfInvention,
                ServiceFee = data.Item3,
                Applicants = fileInfo.applicants,
                Inventors = fileInfo.Inventors,
                CorrespondenceName = fileInfo.Correspondence?.name,
                CorrespondenceAddress = fileInfo.Correspondence?.address,
                CorrespondenceEmail = fileInfo.Correspondence?.email,
                CorrespondencePhone = fileInfo.Correspondence?.phone,
                PatentAbstract = fileInfo.PatentAbstract,
            };
            return updateCost;
        }
        catch (Exception up)
        {
            _log.LogError(up, "Error-at-PatentClericalUpdateCost");
            throw;
        }
    }

    public async Task<RecordalDto> NonConventionalCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.NonConventional, fileType, "", null, PatentTypes.Non_Conventional, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Non-Conventional Patent Payment",
                applicant.Name, applicant.Email, applicant.Phone);

            var mergeCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfInvention ?? "",
                ApplicantName = applicant.Name,
                // TrademarkClass = fileInfo.TrademarkClass
            };

            return mergeCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-NewNonConventional");
            throw;
        }
    }

    public async Task<List<AvailabilitySearchDto>> GetRelatedTitles(string? fileName = null, int? classNo = null, string? type = null)
    {
        var filters = new List<FilterDefinition<Filling>>();

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            // Use first 4 characters (or less if shorter)
            var searchTerm = fileName.Length >= 4 ? fileName.Substring(0, 4) : fileName;
            var escapedName = Regex.Escape(searchTerm);
            filters.Add(Builders<Filling>.Filter.Regex(
                s => s.TitleOfTradeMark,
                new BsonRegularExpression($"^{escapedName}", "i")
            ));
        }

        if (classNo.HasValue)
        {
            filters.Add(Builders<Filling>.Filter.Eq(s => s.TrademarkClass, classNo.Value));
        }

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<FileTypes>(type, ignoreCase: true, out var parsedType))
        {
            filters.Add(Builders<Filling>.Filter.Eq(s => s.Type, parsedType));
        }

        var finalFilter = filters.Count > 0
            ? Builders<Filling>.Filter.And(filters)
            : FilterDefinition<Filling>.Empty;

        var projection = Builders<Filling>.Projection.Expression(f => new AvailabilitySearchDto
        {
            FileId = f.FileId,
            Correspondence = f.Correspondence ?? new CorrespondenceType(),
            TitleOfDesign = f.TitleOfDesign,
            TitleOfInvention = f.TitleOfInvention,
            TitleOfTradeMark = f.TitleOfTradeMark,
            TradeMarkClass = f.TrademarkClass,
            TrademarkType = f.TrademarkType,
            FileApplicant = f.applicants[0].Name,
            FilingDate = f.ApplicationHistory[0].ApplicationDate.ToString(),
            TradeMarkLogo = f.TrademarkLogo,
            FileStatus = f.FileStatus,
            LogoUrl = null,
            Similarity = 0 // initialize
        });

        var result = await _fillingCollection
            .Find(finalFilter)
            .Project(projection)
            .ToListAsync();

        // Calculate similarity and sort
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var jaro = new F23.StringSimilarity.JaroWinkler();

            foreach (var dto in result)
            {
                var title = dto.TitleOfTradeMark ?? string.Empty;
                var similarityScore = jaro.Similarity(fileName, title); // returns 0.0 to 1.0
                dto.Similarity = Math.Round(similarityScore * 100, 2); // percentage
            }

            result = result.OrderByDescending(r => r.Similarity).ToList();
        }

        // Fetch logo URLs
        foreach (var dto in result)
        {
            var file = await _fillingCollection.Find(x => x.FileId == dto.FileId).FirstOrDefaultAsync();
            var repAttachment = file?.Attachments
                .FirstOrDefault(a => a.name == "representation" && a.url != null && a.url.Count > 0);
            var imageUrl = repAttachment?.url[0];
            dto.LogoUrl = imageUrl;
        }

        return result;
    }

    public async Task<bool> AddRegisteredUser(RegisteredUserDto regUser)
    {
        try
        {
            var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, regUser.FileId))
            .FirstOrDefaultAsync();
            if (file == null) return false;
            var applicant = file.applicants.FirstOrDefault();
            string docUrl = "";
            //Console.WriteLine("Here we are:");
            //Console.WriteLine(JsonSerializer.Serialize(regUser.document));
            if (regUser.document != null)
            {
                using var ms = new MemoryStream();
                await regUser.document?.CopyToAsync(ms);
                var userDoc = ms.ToArray();
                var ctype = regUser.document?.ContentType;
                var links = await UploadAttachment(new List<TT>() { new TT()
                {
                    contentType = "application/pdf",
                    data = userDoc,
                    fileName = "sample" + ".pdf",
                    Name = "",
                } });

                docUrl = links[0];
            }
            //Console.WriteLine("document url: " + docUrl);

            //Add to app history
            var history = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.RegisteredUser,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.Now,
                PaymentId = regUser.rrr,
                FieldToChange = "registeredUser",
                OldValue = new Dictionary<string, object?>
                {
                    ["title"]        = file.Type == FileTypes.Design ? file.TitleOfDesign : file.Type == FileTypes.Patent ? file.TitleOfInvention : file.TitleOfTradeMark,
                    ["fileNumber"]   = file.FileId,
                    ["fileType"]     = file.Type.ToString(),
                    ["productClass"] = file.TrademarkClass,
                    ["rtmNumber"]    = file.RtmNumber,
                    ["name"]        = applicant?.Name,
                    ["email"]       = applicant?.Email,
                    ["phone"]       = applicant?.Phone,
                    ["address"]     = applicant?.Address,
                    ["nationality"] = applicant?.country,
                },
                NewValue = new Dictionary<string, object?>
                {
                    ["name"]        = regUser.Name,
                    ["email"]       = regUser.Email,
                    ["phone"]       = regUser.Phone,
                    ["address"]     = regUser.Address,
                    ["nationality"] = regUser.Nationality,
                    ["attachments"] = new List<Dictionary<string, object?>>
                    {
                        new()
                        {
                            ["fileName"]    = "Registered User Document",
                            ["contentType"] = "application/pdf",
                            ["url"]         = docUrl,
                        }
                    }
                },
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Message = "Registered user application submitted, awaiting approval",
                        User = applicant.Name,
                        UserId = file.CreatorAccount
                    }
                }
            };
            //Create new registered user
            var newRegUser = new RegisteredUser
            {
                Name = regUser.Name,
                Email = regUser.Email,
                Phone = regUser.Phone,
                Address = regUser.Address,
                Nationality = regUser.Nationality,
                FileId = file.FileId,
                isApproved = false,
                Id = history.id
            };
            //create new recordal info
            var recordal = new PostRegistrationApp
            {
                Id = history.id,
                RecordalType = "Registered User",
                FileNumber = regUser.FileId,
                rrr = regUser.rrr,
                dateOfRecordal = DateTime.Now.ToString(),
                documentUrl = docUrl,
                FilingDate = DateTime.Now.ToString(),
                Name = regUser.Name,
                Email = regUser.Email,
                Phone = regUser.Phone,
                Address = regUser.Address,
                DateTreated = "",

            };
            var update = Builders<Filling>.Update
                .Push(f => f.RegisteredUsers, newRegUser)
                .Push(f => f.PostRegApplications, recordal)
                .Push(f => f.ApplicationHistory, history);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            return false;
        }
        return true;
    }
    public async Task<List<RegisteredUser>> GetAllRegisteredUsers(string fileId)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();
        var regUsers = file.RegisteredUsers?.ToList();
        if (regUsers == null) return null;
        return regUsers;

    }
    public async Task<RegisteredUser> GetRegUserApplication(string fileId, string appId)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();
        var regUser = file.RegisteredUsers?.FirstOrDefault(a => a.Id == appId);
        return regUser;

    }
    public async Task<bool> ApproveRegUser(TreatRecordalDto recordalApp)
    {
        try
        {
            Console.WriteLine($"Approving registered user for fileId: {recordalApp.fileId}, appId: {recordalApp.appId}");
            var file = await _fillingCollection
                 .Find(Builders<Filling>.Filter.Eq(f => f.FileId, recordalApp.fileId))
                 .FirstOrDefaultAsync();

            if (file == null) return false;
            var user = await _userCollection.Find(u => u.Id == recordalApp.userId).FirstOrDefaultAsync();
            if (user == null) throw new UnauthorizedAccessException("Unauthorized user");
            // Update post reg
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;
            app.CurrentStatus = ApplicationStatuses.Approved;

            //Signature for Certificate
            var signature = await _signatures.Find(a => a.Designation == "recordalSignatory" && a.IsActive == true).FirstOrDefaultAsync();
            app.SignatoryName = signature.Name;
            app.SignatureId = signature.Id;
            //app.Signature = signature.SignatureData;

            //Update reg user
            var regUser = file.RegisteredUsers?.FirstOrDefault(r => r.Id == recordalApp.appId);
            if (regUser == null) return false;
            regUser.isApproved = true;

            var update = Builders<Filling>.Update
                .Set(f => f.PostRegApplications, file.PostRegApplications)
                .Set(f => f.ApplicationHistory, file.ApplicationHistory)
                .Set(f => f.RegisteredUsers, file.RegisteredUsers);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            var perform = new PerformanceDto
            {
                AfterStatus = ApplicationStatuses.Approved,
                BeforeStatus = ApplicationStatuses.AwaitingRecordalProcess,
                ApplicationType = FormApplicationTypes.Assignment,
                AppUserId = recordalApp.userId,
                Date = DateTime.Now,
                FileNumber = recordalApp.fileId,
                FileType = file.Type,
                OfficeUnit = Roles.TrademarkCertification,
                Reason = recordalApp.reason,
            };

            SavePerformance(perform);
            return true;

        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in ApproveRegisteredUser: {ex.Message}");
            Console.WriteLine(ex);
            return false;
        }
    }
    public async Task<RecordalDto> MergerCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.Merger, fileType, "", null, null, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Trademark Merger",
                applicant.Name, applicant.Email, applicant.Phone);

            var mergeCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                TrademarkClass = fileInfo.TrademarkClass
            };

            return mergeCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-MergerCost");
            throw;
        }
    }

    public async Task<RecordalDto> ReclassificationCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.Reclassification, fileType, "", null, null, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                throw new KeyNotFoundException("File not found");
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Reclassification of Trademark",
                applicant.Name, applicant.Email, applicant.Phone);

            var reclassificationCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                TrademarkClass = fileInfo.TrademarkClass
            };

            return reclassificationCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-Reclassification-Cost");
            throw;
        }
    }

    public async Task<string> TrademarkReclassification(ChangeDataRecordalDto dto)
    {
        try
        {
            _log.LogInformation($"{dto.FileId} applies for reclassification...");
            var file = await _fillingCollection.Find(f => f.FileId == dto.FileId).FirstOrDefaultAsync();
            if (file == null)
            {
                _log.LogError("File not found");
                throw new KeyNotFoundException("File not found");
            }
            var applicant = file.applicants.FirstOrDefault();
            var user = await _userCollection
                           .Find(Builders<AppUser>.Filter.Eq(u => u.Id, dto.userId))
                           .FirstOrDefaultAsync()
                       ?? await _userCollection
                           .Find(Builders<AppUser>.Filter.Eq(u => u.CreatorId, dto.userId))
                           .FirstOrDefaultAsync();

            var appHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.Reclassification,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.Now,
                PaymentId = dto.rrr,
                FieldToChange = "Reclassification of Trademark",
                NewValue = "",
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Message = "Reclassification application submitted",
                        User = user.Name,
                        UserId = user.Id
                    }
                }
            };
            var app = new PostRegistrationApp
            {
                Id = appHistory.id,
                FilingDate = DateTime.Now.ToString(),
                rrr = dto.rrr,
                FileNumber = dto.FileId,
                OldClass = file.TrademarkClass,
                Class = dto.NewClass,
                RecordalType = "Reclassification",
                DateTreated = ""
            };


            var update = Builders<Filling>.Update
                .Push(f => f.PostRegApplications, app)
                .Push(f => f.ApplicationHistory, appHistory);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            _log.LogInformation("Reclassification application saved");
            return appHistory.id;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Failed to submit reclassification application");
            throw;
        }
    }

    public async Task<bool> NewMergerApplication(MergerApplicationDto mergerApp)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, mergerApp.FileId))
            .FirstOrDefaultAsync();
        if (file == null) return false;
        var applicant = file.applicants.FirstOrDefault();
        var user = await _userCollection
            .Find(Builders<AppUser>.Filter.Eq(u => u.Id, mergerApp.userId))
            .FirstOrDefaultAsync()
            ?? await _userCollection
                .Find(Builders<AppUser>.Filter.Eq(u => u.Id, file.CreatorAccount))
                .FirstOrDefaultAsync();
        string docUrl = "";
        if (mergerApp.document != null)
        {
            using var ms = new MemoryStream();
            await mergerApp.document?.CopyToAsync(ms);
            var userDoc = ms.ToArray();
            var ctype = mergerApp.document?.ContentType;
            var links = await UploadAttachment(new List<TT>() { new TT()
            {
                    contentType = "application/pdf",
                    data = userDoc,
                    fileName = "sample" + ".pdf",
                    Name = "",
            } });

            docUrl = links[0];
        }
        Console.WriteLine("document url: " + docUrl);
        var userName = user != null
                ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
                : applicant?.Name ?? "Unknown";
        try
        {

            // Create ApplicationInfo for ApplicationHistory
            var mergerHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.Merger,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.Now,
                PaymentId = mergerApp.rrr,
                FieldToChange = "merger",
                OldValue = new Dictionary<string, object?>
                {
                    ["title"]        = file.Type == FileTypes.Design ? file.TitleOfDesign : file.Type == FileTypes.Patent ? file.TitleOfInvention : file.TitleOfTradeMark,
                    ["fileNumber"]   = file.FileId,
                    ["fileType"]     = file.Type.ToString(),
                    ["productClass"] = file.TrademarkClass,
                    ["rtmNumber"]    = file.RtmNumber,
                    ["name"]        = applicant?.Name,
                    ["email"]       = applicant?.Email,
                    ["phone"]       = applicant?.Phone,
                    ["address"]     = applicant?.Address,
                    ["nationality"] = applicant?.country,
                },
                NewValue = new Dictionary<string, object?>
                {
                    ["name"]        = mergerApp.Name,
                    ["email"]       = mergerApp.Email,
                    ["phone"]       = mergerApp.Phone,
                    ["address"]     = mergerApp.Address,
                    ["nationality"] = mergerApp.Nationality,
                    ["mergerDate"]  = mergerApp.MergerDate,
                    ["attachments"] = new List<Dictionary<string, object?>>
                    {
                        new()
                        {
                            ["fileName"]    = "Merger Document",
                            ["contentType"] = "application/pdf",
                            ["url"]         = docUrl,
                        }
                    }
                },
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Message = "Merger application submitted",
                        User = userName,
                        UserId = user.Id
                    }
                }
            };
            var merger = new PostRegistrationApp
            {
                Id = mergerHistory.id,
                dateOfRecordal = mergerApp.MergerDate,
                FilingDate = DateTime.Now.ToString(),
                rrr = mergerApp.rrr,
                FileNumber = mergerApp.FileId,
                OldAddress = applicant.Address,
                Address = mergerApp.Address,
                OldNationality = applicant.country,
                Nationality = mergerApp.Nationality,
                OldName = applicant.Name,
                Name = mergerApp.Name,
                OldEmail = applicant.Email,
                Email = mergerApp.Email,
                OldPhone = applicant.Phone,
                Phone = mergerApp.Phone,
                documentUrl = docUrl,
                RecordalType = "Merger",
                DateTreated = ""
            };


            var update = Builders<Filling>.Update
                .Push(f => f.PostRegApplications, merger)
                .Push(f => f.ApplicationHistory, mergerHistory);


            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
        }
        catch (Exception ex)
        {

            _log.LogError(ex, $"Error in NewMergerApplication: {ex.Message}");
        }
        return true;
    }
    public async Task<bool> ApproveMerger(TreatRecordalDto recordalApp)
    {
        try
        {
            var file = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, recordalApp.fileId))
                .FirstOrDefaultAsync();

            if (file == null) return false;
            var user = await _userCollection.Find(u => u.Id == recordalApp.userId).FirstOrDefaultAsync();
            if (user == null) throw new UnauthorizedAccessException("Unauthorized User");
            // Update Post reg app
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;

            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status in App History
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;

            app.CurrentStatus = ApplicationStatuses.Approved;

            //Signature for Certificate
            var signature = await _signatures.Find(a => a.Designation == "recordalSignatory" && a.IsActive == true).FirstOrDefaultAsync();
            app.SignatoryName = signature.Name;
            app.Signature = signature.SignatureData;

            // Update Applicant
            var applicant = file.applicants?.FirstOrDefault();
            if (applicant == null) return false;

            applicant.Name = recordal.Name;
            applicant.Address = recordal.Address;
            applicant.Email = recordal.Email;
            applicant.Phone = recordal.Phone;

            var update = Builders<Filling>.Update
                .Set(f => f.PostRegApplications, file.PostRegApplications)
                .Set(f => f.ApplicationHistory, file.ApplicationHistory)
                .Set(f => f.applicants, file.applicants);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            var perform = new PerformanceDto
            {
                AfterStatus = ApplicationStatuses.Approved,
                BeforeStatus = ApplicationStatuses.AwaitingRecordalProcess,
                ApplicationType = FormApplicationTypes.Assignment,
                AppUserId = recordalApp.userId,
                Date = DateTime.Now,
                FileNumber = recordalApp.fileId,
                FileType = file.Type,
                OfficeUnit = Roles.TrademarkCertification,
                Reason = recordalApp.reason,
            };

            SavePerformance(perform);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in ApproveMerger: {ex.Message}");
            Console.WriteLine(ex);
            return false;
        }
    }

    public async Task<MergerApplicationDto> GetMergerApplication(string fileId, string appId)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();

        if (file == null) return null;

        var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == appId);
        if (recordal == null) return null;

        var mergerDetails = new MergerApplicationDto
        {
            FileId = fileId,
            rrr = recordal.rrr,
            OldName = recordal.OldName,
            Name = recordal.Name,
            OldEmail = recordal.OldEmail,
            Email = recordal.Email,
            OldAddress = recordal.OldAddress,
            Address = recordal.Address,
            OldNationality = recordal.OldNationality,
            Nationality = recordal.Nationality,
            OldPhone = recordal.OldPhone,
            Phone = recordal.Phone,
            MergerDate = recordal.dateOfRecordal,
            documentUrl = recordal.documentUrl
        };

        return mergerDetails;
    }

    public async Task<RecordalDto> GetChangeDataCost(string fileId, FileTypes fileType, string changeType)
    {
        try
        {
            var classChange = changeType == "Class";

            _log.LogInformation($"Calculating cost for {changeType} application for fileId: {fileId}");

            var data = _remitaPaymentUtils.GetCost(classChange ? PaymentTypes.Reclassification : PaymentTypes.ChangeDataRecordal, fileType, "", null, null, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                _log.LogError("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Recordal Application",
                applicant.Name, applicant.Email, applicant.Phone);

            if (paymentId == null)
            {
                _log.LogError("Failed to generate payment ID");
                return null;
            }
            var changeCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                ApplicantAddress = applicant.Address,
                ApplicantEmail = applicant.Email,
                ApplicantPhone = applicant.Phone,
                ApplicantNationality = applicant.country,
                TrademarkClass = fileInfo.TrademarkClass,
                DataChangeType = changeType
            };
            _log.LogInformation($"New Change data recordal application for {fileId}, with {paymentId}");
            return changeCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-ChangeRecordalDataCost");
            throw;
        }
    }
    public async Task<string> ChangeDataRecordal(ChangeDataRecordalDto newData)
    {
        _log.LogInformation($"New Change {newData.ChangeType} application");
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, newData.FileId))
            .FirstOrDefaultAsync();

        if (file == null)
        {
            _log.LogError($"{newData.FileId} not found");
            return null;
        }
        var applicant = file.applicants.FirstOrDefault();
        var user = await _userCollection
            .Find(Builders<AppUser>.Filter.Eq(u => u.Id, newData.userId))
            .FirstOrDefaultAsync()
            ?? await _userCollection
                .Find(Builders<AppUser>.Filter.Eq(u => u.CreatorId, newData.userId))
                .FirstOrDefaultAsync();
        var userName = user != null
            ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : applicant?.Name ?? "Unknown";

        var userId = user?.Id ?? user.CreatorId;
        string docUrl = "";
        if (newData.document != null)
        {
            using var ms = new MemoryStream();
            await newData.document.CopyToAsync(ms);
            var userDoc = ms.ToArray();
            var links = await UploadAttachment(new List<TT>
            {
                new TT
                {
                    contentType = newData.document.ContentType,
                    data = userDoc,
                    fileName = Path.GetFileName(newData.document.FileName),
                    Name = ""
                }
            });
            docUrl = links[0];
        }

        try
        {
            var appHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = newData.ChangeType == "Name"
                    ? FormApplicationTypes.ChangeOfName
                    : FormApplicationTypes.ChangeOfAddress,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.Now,
                PaymentId = newData.rrr,
                FieldToChange = newData.ChangeType == "Name"
                    ? "changeOfName"
                    : "changeOfAddress",
                OldValue = new Dictionary<string, object?>
                {
                    ["title"]        = file.Type == FileTypes.Design ? file.TitleOfDesign : file.Type == FileTypes.Patent ? file.TitleOfInvention : file.TitleOfTradeMark,
                    ["fileNumber"]   = file.FileId,
                    ["fileType"]     = file.Type.ToString(),
                    ["productClass"] = file.TrademarkClass,
                    ["rtmNumber"]    = file.RtmNumber,
                    ["name"]        = applicant?.Name,
                    ["email"]       = applicant?.Email,
                    ["phone"]       = applicant?.Phone,
                    ["address"]     = applicant?.Address,
                    ["nationality"] = applicant?.country,
                },
                NewValue = new Dictionary<string, object?>
                {
                    ["newName"]    = newData.ChangeType == "Name" ? newData.NewName : null,
                    ["newAddress"] = newData.ChangeType == "Address" ? newData.NewAddress : null,
                    ["attachments"] = new List<Dictionary<string, object?>>
                    {
                        new()
                        {
                            ["fileName"]    = newData.document != null ? Path.GetFileName(newData.document.FileName) : "Supporting Document",
                            ["contentType"] = newData.document?.ContentType ?? "application/pdf",
                            ["url"]         = docUrl,
                        }
                    }
                },
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Message = "Change Data",
                        User = user.Name ?? userName,
                        UserId = userId
                    }
                }
            };

            var recordal = new PostRegistrationApp
            {
                Id = appHistory.id,
                FilingDate = DateTime.Now.ToString(),
                rrr = newData.rrr,
                FileNumber = newData.FileId,
                documentUrl = docUrl,
                RecordalType = newData.ChangeType == "Name"
                    ? "Change of Applicant Name"
                    : "Change of Applicant Address",
                DateTreated = "",
                OldName = newData.ChangeType == "Name" ? applicant.Name : null,
                Name = newData.ChangeType == "Name" ? newData.NewName : null,
                OldAddress = newData.ChangeType == "Address" ? applicant.Address : null,
                Address = newData.ChangeType == "Address" ? newData.NewAddress : null

            };

            var update = Builders<Filling>.Update
                .Push(f => f.PostRegApplications, recordal)
                .Push(f => f.ApplicationHistory, appHistory);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            return appHistory.id;
        }
        catch (Exception ex)
        {
            _log.LogError("Error during ChangeDataRecordal", ex);
            throw;
        }

   
    }
    public async Task<ChangeDataRecordalDto> GetChangeDataRecordal(string fileId, string appId)
    {
        var file = await _fillingCollection
           .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
           .FirstOrDefaultAsync();

        if (file == null) return null;

        var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == appId);
        if (recordal == null) return null;

        var changeDetails = new ChangeDataRecordalDto
        {
            FileId = fileId,
            rrr = recordal.rrr,
            OldName = recordal.OldName,
            NewName = recordal?.Name,
            OldAddress = recordal.OldAddress,
            NewAddress = recordal?.Address,
            OldClassDescription = recordal?.OldClass.HasValue == true ? FileUtils.TrademarkClassMapper.GetDescription(recordal.OldClass.Value) : null,
            NewClassDescription = recordal?.Class.HasValue == true ? FileUtils.TrademarkClassMapper.GetDescription(recordal.Class.Value) : null,
            documentUrl = recordal?.documentUrl,
            OldClass = recordal.OldClass,
            NewClass = recordal?.Class,
        };

        return changeDetails;
    }

    public async Task<List<AvailabilitySearchDto>> GetFileByNumber(string fileNumber)
    {
        var result = new List<AvailabilitySearchDto>();

        if (string.IsNullOrWhiteSpace(fileNumber))
            return result;

        try
        {
            var filter = Builders<Filling>.Filter.Or(
                Builders<Filling>.Filter.Eq(f => f.FileId, fileNumber),
                Builders<Filling>.Filter.Eq(f => f.RtmNumber, fileNumber)
            );

            var projection = Builders<Filling>.Projection.Expression(f => new AvailabilitySearchDto
            {
                FileId = f.FileId,
                Correspondence = f.Correspondence ?? new CorrespondenceType(),
                CreatorAccount = f.CreatorAccount,
                TitleOfDesign = f.TitleOfDesign,
                TitleOfInvention = f.TitleOfInvention,
                TitleOfTradeMark = f.TitleOfTradeMark,
                TradeMarkClass = f.TrademarkClass,
                TrademarkType = f.TrademarkType,
                FileApplicant = f.applicants[0].Name ?? string.Empty,
                Applicants = f.applicants,
                FilingDate = f.FilingDate.ToString() ?? f.ApplicationHistory[0].ApplicationDate.ToString(),
                TradeMarkLogo = f.TrademarkLogo,
                FileStatus = f.FileStatus,
                FileTypes = f.Type,
                PatentApplicationType = f.PatentApplicationType.ToString() ?? string.Empty,
                PatentType = f.PatentType.ToString() ?? string.Empty,
                LogoUrl = null,
                Disclaimer = f.TrademarkDisclaimer,
                FileOrigin = f.FileOrigin,
                PublicationDate = f.PublicationDate,
                FirstPriorityInfo = f.FirstPriorityInfo,
                WithdrawalDate = f.WithdrawalDate,
                WithdrawalRequestDate = f.WithdrawalRequestDate,
                IsRenewalEligible = f.IsRenewalEligible
            });

            result = await _fillingCollection
                .Find(filter)
                .Project(projection)
                .ToListAsync();

            foreach (var dto in result)
            {
                var file = await _fillingCollection.Find(x => x.FileId == dto.FileId).FirstOrDefaultAsync();
                var repAttachment = file?.Attachments
                    .FirstOrDefault(a => a.name == "representation" && a.url != null && a.url.Count > 0);

                dto.LogoUrl = repAttachment?.url.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            // Optionally log the error
            _log.LogError(ex, $"Error in GetFileByNumber: {ex.Message}");
        }

        return result;
    }

    public async Task<(bool success, string message)> ChangeOfAgent(string fileId, string userId, IFormFile? powerOfAttorney)
    {
        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == fileId).FirstOrDefaultAsync();
            if (file == null)
                return (false, "File not found.");

            var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null)
                return (false, "User not found.");

            // Build new correspondence from the user's profile
            var newCorrespondence = new CorrespondenceType
            {
                id   = user.Id,
                name = user.Name ?? $"{user.FirstName} {user.LastName}".Trim(),
                email   = user.Email,
                phone   = user.PhoneNumber,
                address = user.Address,
                Nationality = user.Nationality,
                state   = user.State?.ToString()
            };

            // Upload power of attorney if provided
            string? poaUrl = null;
            if (powerOfAttorney != null && powerOfAttorney.Length > 0)
            {
                using var ms = new MemoryStream();
                await powerOfAttorney.CopyToAsync(ms);
                var ext      = Path.GetExtension(powerOfAttorney.FileName);
                var fileName = Path.GetRandomFileName().Split('.')[0] + ext;

                await _attachmentCollection.InsertOneAsync(new AttachmentInfo
                {
                    Id          = fileName,
                    ContentType = powerOfAttorney.ContentType,
                    Data        = ms.ToArray()
                });

                poaUrl = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={fileName}";
            }

            // Update the file's correspondence and creator account
            var update = Builders<Filling>.Update
                .Set(f => f.Correspondence, newCorrespondence)
                .Set(f => f.CreatorAccount, user.Id);

            await _fillingCollection.UpdateOneAsync(f => f.FileId == fileId, update);

            _log.LogInformation("ChangeOfAgent completed for FileId {FileId}. POA attached: {HasPoa}",
                fileId, poaUrl != null);

            return (true, "Agent changed successfully.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in ChangeOfAgent");
            return (false, "An error occurred while processing the request.");
        }
    }

    public async Task<bool> ApproveChangeDataRecordal(TreatRecordalDto recordalApp)
    {
        try
        {
            _log.LogInformation($"Approving data change for fileId: {recordalApp.fileId}, appId: {recordalApp.appId}");
            _log.LogDebug(JsonSerializer.Serialize(recordalApp, new JsonSerializerOptions { WriteIndented = true }));
            var file = await _fillingCollection
                 .Find(Builders<Filling>.Filter.Eq(f => f.FileId, recordalApp.fileId))
                 .FirstOrDefaultAsync();

            if (file == null) throw new KeyNotFoundException("File not found");
            var user = await _userCollection.Find(u => u.Id == recordalApp.userId).FirstOrDefaultAsync();
            if (user == null)
            {
                user = await _userCollection
                    .Find(u => u.CreatorId == recordalApp.userId)
                    .FirstOrDefaultAsync();
            }

            if (user == null)
            {
                _log.LogError("Unauthorized user");
                throw new KeyNotFoundException("Unauthorized user");
            }

            // Update post reg
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null)
            {
                _log.LogError("Recordal not found");
                return false;
            }
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;
            app.CurrentStatus = ApplicationStatuses.Approved;

            //Signature for Certificate
            var signature = await _signatures.Find(a => a.Designation == "recordalSignatory" && a.IsActive == true).FirstOrDefaultAsync();
            app.SignatoryName = signature.Name;
            app.Signature = signature.SignatureData;

            var history = new ApplicationHistory
            {
                beforeStatus = ApplicationStatuses.AwaitingPayment,
                afterStatus = ApplicationStatuses.Approved,
                Date = DateTime.Now,
                User = user.Name ?? $"{user.FirstName} {user.LastName}",
                UserId = recordalApp.userId,
                Message = "Payment successful, Awaiting Approval"
            };
            app.StatusHistory ??= new List<ApplicationHistory>();
            app.StatusHistory.Add(history);
            file.applicants ??= new List<ApplicantInfo>();
            var applicant = file.applicants.FirstOrDefault();

            if (recordal.RecordalType == "Change of Applicant Address")
            {
                applicant.Address = recordal.Address;
            }
            else if (recordal.RecordalType == "Change of Applicant Name")
            {
                applicant.Name = recordal.Name;
            } else if (recordal.RecordalType == "Reclassification")
            {
                file.TrademarkClass = recordal.Class;
                var descr = FileUtils.TrademarkClassMapper.GetDescription(recordal.Class.Value);
                file.TrademarkClassDescription = descr;
            }

            var update = Builders<Filling>.Update
                .Set(f => f.PostRegApplications, file.PostRegApplications)
                .Set(f => f.ApplicationHistory, file.ApplicationHistory)
                .Set(f => f.applicants, file.applicants);
            if (recordal.RecordalType == "Reclassification")
            {
                update = Builders<Filling>.Update.Combine(
                    update,
                    Builders<Filling>.Update.Set(f => f.TrademarkClass, file.TrademarkClass),
                    Builders<Filling>.Update.Set(f => f.TrademarkClassDescription, file.TrademarkClassDescription)
                );
            }
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            var perform = new PerformanceDto
            {
                AfterStatus = ApplicationStatuses.Approved,
                BeforeStatus = ApplicationStatuses.AwaitingRecordalProcess,
                ApplicationType = app.ApplicationType,
                AppUserId = recordalApp.userId,
                Date = DateTime.Now,
                FileNumber = recordalApp.fileId,
                FileType = file.Type,
                OfficeUnit = Roles.TrademarkCertification,
                Reason = recordalApp.reason,
            };

            SavePerformance(perform);
            _log.LogInformation($"{recordal.RecordalType} has been approved");
            return true;

        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in ApproveChangeDateRecordal: {ex.Message}");
            Console.WriteLine(ex);
            return false;
        }
    }
    public async Task<bool> DenyRecordal(TreatRecordalDto recordalApp)
    {
        try
        {
            var file = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, recordalApp.fileId))
                .FirstOrDefaultAsync();
            if (file == null) return false;
            var user = await _userCollection.Find(u => u.Id == recordalApp.userId).FirstOrDefaultAsync();
            if (user == null) throw new UnauthorizedAccessException("Unauthorized User");
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;

            // Try find both types of recordal (post-registration or clerical)
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            var clerical = file.ClericalUpdates?.FirstOrDefault(p => p.Id == recordalApp.appId);

            if (recordal == null && clerical == null)
            {
                return false;
            }

            // Mark whichever was found as denied and add reason / date
            if (recordal != null)
            {
                // PostRegistrationApp.DateTreated is stored as string elsewhere — keep consistency
                recordal.DateTreated = DateTime.Now.ToString();
                recordal.Reason = recordalApp.reason;
            }

            if (clerical != null)
            {
                clerical.DateTreated = DateTime.Now;
                clerical.Reason = recordalApp.reason;
                clerical.IsApproved = false;
            }

            app.CurrentStatus = ApplicationStatuses.Rejected;

            var updates = new List<UpdateDefinition<Filling>>
            {
                Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory)
            };

            if (recordal != null)
                updates.Add(Builders<Filling>.Update.Set(f => f.PostRegApplications, file.PostRegApplications));

            if (clerical != null)
                updates.Add(Builders<Filling>.Update.Set(f => f.ClericalUpdates, file.ClericalUpdates));

            var update = Builders<Filling>.Update.Combine(updates);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            var perform = new PerformanceDto
            {
                AfterStatus = ApplicationStatuses.Approved,
                BeforeStatus = ApplicationStatuses.AwaitingRecordalProcess,
                ApplicationType = FormApplicationTypes.Assignment,
                AppUserId = recordalApp.userId,
                Date = DateTime.Now,
                FileNumber = recordalApp.fileId,
                FileType = file.Type,
                OfficeUnit = Roles.TrademarkCertification,
                Reason = recordalApp.reason,
            };

            SavePerformance(perform);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in DenyRecordal: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RenewalApplication(string fileId, string rrr)
    {
        try
        {
            var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();
            if (file == null) return false;
            var applicant = file.applicants.FirstOrDefault();

            //Application History
            var renewalHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.LicenseRenewal,
                CurrentStatus = ApplicationStatuses.Approved,
                ApplicationDate = DateTime.Now,
                PaymentId = rrr,
                FieldToChange = "Renewal Application",
                NewValue = "",
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.AwaitingPayment,
                        afterStatus = ApplicationStatuses.Approved,
                        Message = "Renewal Application",
                        User = applicant.Name,
                        UserId = file.CreatorAccount
                    }
                }
            };
            //Post Registration Application
            var renewal = new PostRegistrationApp
            {
                Id = renewalHistory.id,
                dateOfRecordal = DateTime.Now.ToString(),
                FilingDate = DateTime.Now.ToString(),
                rrr = rrr,
                FileNumber = fileId,
                RecordalType = "Renewal"
            };

            var update = Builders<Filling>.Update
                .Push(f => f.PostRegApplications, renewal)
                .Push(f => f.ApplicationHistory, renewalHistory);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in RenewalApplication: {ex.Message}");
            return false;
        }
    }

    //Service method that updates application history of any file that a statusSearch was done on
    public async Task<bool> AddNewStatusSearchHistoryAsync(string fileId, string rrr)
    {
        try
        {
            var file = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();
            if (file == null) return false;

            var applicant = file.applicants.FirstOrDefault();
            if (applicant == null) return false;

            var newHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.StatusSearch,
                CurrentStatus = file.FileStatus,
                ApplicationDate = DateTime.Now,
                PaymentId = rrr,
                FieldToChange = "Status Search",
                NewValue = "",
                StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = DateTime.Now,
                    beforeStatus = file.FileStatus,
                    afterStatus = file.FileStatus,
                    Message = "File Status Search",
                    User = applicant.Name,
                    UserId = file.CreatorAccount
                }
            }
            };

            var update = Builders<Filling>.Update.Push(f => f.ApplicationHistory, newHistory);

            var result = await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );

            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in AddNewStatusSearchHistoryAsync: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Success, string Message)> PublicationStatusUpdateAsync(PublicationUpdateDto dto)
    {
        _log.LogInformation("Starting publication status update for FileId {FileId}, UserId {UserId}", dto.FileId, dto.UserId);
        var file = await _fillingCollection.Find(x => x.FileId == dto.FileId).FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Publication status update failed. File not found for FileId {FileId}", dto.FileId);
            return (false, "File not found.");
        }
        var user = await _userCollection.Find(u => u.Id == dto.UserId).FirstOrDefaultAsync();
        if (user == null)
        {
            _log.LogWarning("Publication status update failed. User not found for UserId {UserId}", dto.UserId);
            return (false, "User Not found");
        }
        // Check if publication has already been done
        if (file.PublicationDate != null)
        {
            _log.LogWarning("Publication status update skipped. Publication already completed for FileId {FileId}", dto.FileId);
            return (false, "Publication has already been done on the file.");
        }

        // Proceed with publication update logic
        // (existing logic for payment check, attachments, and history addition...)

        if (dto.PaymentRRR != null)
        {
            RemitaResponseClass payDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.PaymentRRR);
            if (payDetails == null || payDetails.status != "00")
            {
                throw new Exception($"Payment Not Found or Invalid RRR, ${dto.PaymentRRR}");
            }
            Console.WriteLine(payDetails);
            var payment = new PaymentRecord
            {
                PaymentType = "Publication Status Update",
                Date = DateTime.Now,
                FileId = file.FileId,
                FileType = file.Type.ToString(),
                RemitaResponse = payDetails
            };
            Console.WriteLine(payment);
            await _paymentService.AddPaymentRecord(payment);
        }
        else if (dto.PaymentRRR == null)
        {
            throw new Exception("No Payment Id found");
        }

        // Update publication date
        file.PublicationDate = dto.PublicationDate;
        file.PublicationRequestDate = DateTime.Now;
        var applicant = file.applicants.FirstOrDefault();

        // Handle attachments as files (TT), not just URLs
        if (dto.AttachmentFiles != null && dto.AttachmentFiles.Any())
        {
            // Upload files and get URLs
            var publicationUrls = await UploadAttachment(dto.AttachmentFiles);

            file.Attachments ??= new List<AttachmentType>();
            var publicationAttachment = file.Attachments.FirstOrDefault(a => a.name == "publication");
            if (publicationAttachment != null)
            {
                // Add only new URLs if not already present
                foreach (var url in publicationUrls)
                {
                    if (!publicationAttachment.url.Contains(url))
                        publicationAttachment.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "publication",
                    url = publicationUrls
                });
            }
        }

        var publicationStatusUpdateHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.PublicationStatusUpdate,
            CurrentStatus = ApplicationStatuses.AwaitingStatusUpdate,
            ApplicationDate = DateTime.Now,
            PaymentId = dto.PaymentRRR,
            FieldToChange = "Publication Status Update",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = DateTime.Now,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.AwaitingStatusUpdate,
                    Message = "Publication Status Update",
                    User = user.Name,
                    UserId = user.Id
                }
            }
        };

        file.ApplicationHistory ??= new List<ApplicationInfo>();
        file.ApplicationHistory.Add(publicationStatusUpdateHistory);

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);
        _log.LogInformation("Publication status update completed for FileId {FileId}", dto.FileId);
        return (true, "Publication status updated successfully.");
    }

    public async Task<(bool Success, string Message)> WithdrawalRequestAsync(WithdrawalRequestDto dto)
    {
        _log.LogInformation("Starting withdrawal request for FileId {FileId}, UserId {UserId}", dto.FileId, dto.UserId);
        var file = await _fillingCollection.Find(x => x.FileId == dto.FileId).FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Withdrawal request failed. File not found for FileId {FileId}", dto.FileId);
            return (false, "File not found.");
        }

        var user = await _userCollection.Find(u => u.Id == dto.UserId).FirstOrDefaultAsync();
        if (user == null)
        {
            _log.LogWarning("Withdrawal request failed. User not found for UserId {UserId}", dto.UserId);
            return (false, "User Not found");
        }

        if (file.WithdrawalDate != null)
        {
            _log.LogWarning("Withdrawal request skipped. Withdrawal already completed for FileId {FileId}", dto.FileId);
            return (false, "Withdrawal has already been done on the file.");
        }

        //Payment validation
        if (!string.IsNullOrEmpty(dto.PaymentRRR))
        {
            var payDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.PaymentRRR);
            if (payDetails == null || payDetails.status != "00")
                throw new Exception($"Payment Not Found or Invalid RRR, {dto.PaymentRRR}");

            var payment = new PaymentRecord
            {
                PaymentType = "File Withdrawal",
                Date = DateTime.Now,
                FileId = file.FileId,
                FileType = file.Type.ToString(),
                RemitaResponse = payDetails
            };
            await _paymentService.AddPaymentRecord(payment);
        }
        else
        {
            throw new Exception("No Payment Id found");
        }

        // Save dates
        file.WithdrawalDate = DateTime.Now;
        file.WithdrawalRequestDate = DateTime.Now;

        // Handle attachments
        file.Attachments ??= new List<AttachmentType>();

        // Handle attachments as files (TT), not just URLs
        if (dto.WithdrawalLetter != null && dto.WithdrawalLetter.Any())
        {
            // Upload files and get URLs
            var withdrawalLetterUrls = await UploadAttachment(dto.WithdrawalLetter);

            file.Attachments ??= new List<AttachmentType>();
            var withdrawalLetterAttachment = file.Attachments.FirstOrDefault(a => a.name == "withdrawal_letter");
            if (withdrawalLetterAttachment != null)
            {
                // Add only new URLs if not already present
                foreach (var url in withdrawalLetterUrls)
                {
                    if (!withdrawalLetterAttachment.url.Contains(url))
                        withdrawalLetterAttachment.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "withdrawal_letter",
                    url = withdrawalLetterUrls
                });
            }
        }

        // Handle withdrawal supporting documents as files (TT), not just URLs
        if (dto.WithdrawalSupportingDocuments != null && dto.WithdrawalSupportingDocuments.Any())
        {
            var supportingDocUrls = await UploadAttachment(dto.WithdrawalSupportingDocuments);

            file.Attachments ??= new List<AttachmentType>();
            var supportingDocAttachment = file.Attachments.FirstOrDefault(a => a.name == "withdrawal_supporting_documents");
            if (supportingDocAttachment != null)
            {
                foreach (var url in supportingDocUrls)
                {
                    if (!supportingDocAttachment.url.Contains(url))
                        supportingDocAttachment.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "withdrawal_supporting_documents",
                    url = supportingDocUrls
                });
            }
        }

        // Application history
        var applicant = file.applicants.FirstOrDefault();
        var withdrawalHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.WithdrawalRequest,
            CurrentStatus = ApplicationStatuses.RequestWithdrawal,
            ApplicationDate = DateTime.Now,
            PaymentId = dto.PaymentRRR,
            FieldToChange = "Withdrawal Request",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = DateTime.Now,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.RequestWithdrawal,
                    Message = "Withdrawal Request Submitted",
                    User = user.Name,
                    UserId = user.Id
                }
            }
        };

        file.ApplicationHistory ??= new List<ApplicationInfo>();
        file.ApplicationHistory.Add(withdrawalHistory);

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);
        _log.LogInformation("Withdrawal request completed for FileId {FileId}", dto.FileId);
        return (true, "Withdrawal request submitted successfully.");
    }

    public async Task<object?> GetWithdrawalDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        var withdrawalDate = file.WithdrawalDate;
        var withdrawalRequestDate = file.WithdrawalRequestDate;

        var withdrawalLetterAttachments = file.Attachments?
            .Where(a => a.name == "withdrawal_letter")
            .Select(a => new { a.name, a.url })
            .ToList();

        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "withdrawal_supporting_documents")
            .Select(a => new { a.name, a.url })
            .ToList();

        return new
        {
            FileId = file.FileId,
            WithdrawalDate = withdrawalDate,
            WithdrawalRequestDate = withdrawalRequestDate,
            WithdrawalLetterAttachments = withdrawalLetterAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments
        };
    }

    public async Task<(bool Success, string Message)> WithdrawalRequestDecisionAsync(string fileId, bool approve, string? comment, string? userId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var staff = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (staff == null)
            return (false, "Unauthorized user");

        var applicant = file.applicants.FirstOrDefault();

        // Find the ApplicationInfo for WithdrawalRequest
        var withdrawalApp = file.ApplicationHistory
            .FirstOrDefault(a => a.ApplicationType == FormApplicationTypes.WithdrawalRequest);

        if (withdrawalApp == null)
            return (false, "No withdrawal request found");

        // Prepare new status history entry
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = approve ? "Withdrawal request approved" : "Withdrawal request refused",
            beforeStatus = ApplicationStatuses.RequestWithdrawal,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = staff.Name,
            UserId = userId
        };

        file.WithdrawalReason = comment;
        withdrawalApp.StatusHistory.Add(newStatus);

        // Update current status
        withdrawalApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        // If approved, update file status to Withdrawn
        if (approve)
            file.FileStatus = ApplicationStatuses.Withdrawn;

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = userId,
            AfterStatus = newStatus.afterStatus,
            BeforeStatus = newStatus.beforeStatus,
            ApplicationType = FormApplicationTypes.WithdrawalRequest,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = newStatus.Message,
            Date = newStatus.Date,
            OfficeUnit = file.Type switch
            {
                FileTypes.TradeMark => Roles.TrademarkAcceptance,
                FileTypes.Patent => Roles.PatentExaminer,
                FileTypes.Design => Roles.DesignExaminer,
                _ => null
            }
        };
        SavePerformance(performance);

        return (true, approve ? "Withdrawal request approved" : "Withdrawal request refused");
    }

    public async Task<object?> GetFilePublicationDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        var publicationDate = file.PublicationDate;
        var publicationAttachments = file.Attachments?
            .Where(a => a.name == "publication")
            .Select(a => new { a.name, a.url })
            .ToList();

        return new
        {
            FileId = file.FileId,
            PublicationDate = publicationDate,
            Attachments = publicationAttachments
        };
    }

    public async Task<(bool Success, string Message)> PublicationStatusDecisionAsync(string fileId, bool approve, string? comment, string? userId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var staff = await _userCollection.Find(u=>u.Id == userId).FirstOrDefaultAsync();
        if (staff == null)
            return (false, "Unauthorized user");
        // Find the ApplicationInfo for PublicationStatusUpdate
        var publicationApp = file.ApplicationHistory
            .FirstOrDefault(a => a.ApplicationType == FormApplicationTypes.PublicationStatusUpdate);

        if (publicationApp == null)
            return (false, "No publication status update found");

        // Prepare new status history entry
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = approve ? "Publication status approved" : "Publication status refused",
            beforeStatus = ApplicationStatuses.AwaitingStatusUpdate,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = staff.Name,
            UserId = userId
        };

        file.PublicationReason = comment;
        // Add new status history
        publicationApp.StatusHistory.Add(newStatus);

        // Update current status
        publicationApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        // If approved, update file status
        if (approve)
        {
            file.FileStatus = ApplicationStatuses.AwaitingCertification;
            var pub = new PublicationDto
            {
                FileNumber = fileId,
                Comment = comment,
                StaffName = staff.Name,
                StaffId = staff.Id,
                PublicationDate = file.PublicationDate
            };
           var pubId = await _publicationServices.SavePublication(pub);
           if (pubId is not null)
           {
               var pubUpdate = Builders<PublicationInfo>.Update.Combine(
                   Builders<PublicationInfo>.Update.Set(p => p.IsBatchPublished, true),
                   Builders<PublicationInfo>.Update.Set(p => p.BatchPublishDate, DateTime.Now));

               await _publicationCollection.UpdateOneAsync(
                   Builders<PublicationInfo>.Filter.Eq(p => p.Id, pubId),
                   pubUpdate);
            }
        }

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = userId,
            AfterStatus = newStatus.afterStatus,
            BeforeStatus = newStatus.beforeStatus,
            ApplicationType = FormApplicationTypes.PublicationStatusUpdate,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = newStatus.Message,
            Date = newStatus.Date,
            OfficeUnit = file.Type switch
            {
                FileTypes.TradeMark => Roles.TrademarkPublication,
              // FileTypes.Patent => Roles.PatentExaminer,
              // FileTypes.Design => Roles.DesignExaminer,
                _ => null
            }
        };
        SavePerformance(performance);

        return (true, approve ? "Publication status approved" : "Publication status refused");
    }

    public async Task<RecordalDto> GetAssignmentCost(string fileId, FileTypes fileType)
    {
        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.Assignment, fileType, "", null, null, null);
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();
            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }
            var applicant = fileInfo.applicants[0];
            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Recordal Application",
                applicant.Name, applicant.Email, applicant.Phone);
            Console.WriteLine("amount: " + data.Item1);

            var assignmentCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                TrademarkClass = fileInfo.TrademarkClass,
                ServiceFee = data.Item3,
                RtmNumber = fileInfo.RtmNumber,
                ApplicantEmail = applicant.Email,
                ApplicantNationality = applicant.country,
                ApplicantPhone = applicant.Phone,
                ApplicantAddress = applicant.Address
            };
            return assignmentCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-AssignmentCost");
            throw;
        }
    }
    public async Task<bool> NewAssignmentApplication(AssignmentAppDto assignmentApp)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, assignmentApp.FileId))
            .FirstOrDefaultAsync();
        if (file == null) return false;
        var applicant = file.applicants.FirstOrDefault();
        var user = await _userCollection
            .Find(Builders<AppUser>.Filter.Eq(u => u.Id, assignmentApp.userId))
            .FirstOrDefaultAsync()
            ?? await _userCollection
                .Find(Builders<AppUser>.Filter.Eq(u => u.Id, file.CreatorAccount))
                .FirstOrDefaultAsync();
        var userName = user != null
            ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : applicant?.Name ?? "Unknown";
        var userId = user?.Id ?? file.CreatorAccount;

        string assignDeedUrl = "";
        if (assignmentApp.AssignmentDeed != null)
        {
            using var ms = new MemoryStream();
            await assignmentApp.AssignmentDeed?.CopyToAsync(ms);
            var assignDeed = ms.ToArray();
            var ctype = assignmentApp.AssignmentDeed?.ContentType;
            var deedLinks = await UploadAttachment(new List<TT>() { new TT()
            {
                    contentType = "application/pdf",
                    data = assignDeed,
                    fileName = "sample" + ".pdf",
                    Name = "",
            } });
            assignDeedUrl = deedLinks[0];
        }
        string authLetterUrl = "";
        if (assignmentApp.AuthorizationLetter != null)
        {
            using var ms = new MemoryStream();
            await assignmentApp.AuthorizationLetter?.CopyToAsync(ms);
            var authLetter = ms.ToArray();
            var ctype = assignmentApp.AuthorizationLetter?.ContentType;
            var links = await UploadAttachment(new List<TT>() { new TT()
            {
                    contentType = "application/pdf",
                    data = authLetter,
                    fileName = "sample" + ".pdf",
                    Name = "",
            } });
            authLetterUrl = links[0];
        }
        try
        {
            // Create ApplicationInfo for ApplicationHistory
            var assignmentHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.Assignment,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.Now,
                PaymentId = assignmentApp.rrr,
                FieldToChange = "Assignment Application",
                NewValue = "",
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Message = "Assignment application submitted, awaiting approval",
                        User = userName,
                        UserId = userId
                    }
                }
            };
            //Create new assignee
            var newAssignee = new Assignee
            {
                Name = assignmentApp.AssigneeName,
                AssignorName = applicant.Name,
                Email = assignmentApp.AssigneeEmail,
                AssignorEmail = applicant.Email,
                Phone = assignmentApp.AssigneePhone,
                AssignorPhone = applicant.Phone,
                Address = assignmentApp.AssigneeAddress,
                AssignorAddress = applicant.Address,
                Nationality = assignmentApp.AssigneeNationality,
                AssignorNationality = applicant.country,
                FileId = file.FileId,
                isApproved = false,
                Id = assignmentHistory.id,
                rrr = assignmentApp.rrr,
                AssignmentDeedUrl = assignDeedUrl,
                AuthorizationLetterUrl = authLetterUrl,
            };
            //create new recordal info
            var recordal = new PostRegistrationApp
            {
                Id = assignmentHistory.id,
                RecordalType = "Assignment",
                FileNumber = assignmentApp.FileId,
                rrr = assignmentApp.rrr,
                dateOfRecordal = DateTime.Now.ToString(),
                documentUrl = assignDeedUrl,
                document2Url = authLetterUrl,
                FilingDate = DateTime.Now.ToString(),
                OldName = assignmentApp.AssignorName,
                Name = assignmentApp.AssigneeName,
                OldEmail = assignmentApp.AssignorEmail,
                Email = assignmentApp.AssigneeEmail,
                OldPhone = assignmentApp.AssignorPhone,
                Phone = assignmentApp.AssigneePhone,
                OldAddress = assignmentApp.AssignorAddress,
                Address = assignmentApp.AssigneeAddress,
                DateTreated = "",

            };
            var update = Builders<Filling>.Update
                .Push(f => f.Assignees, newAssignee)
                .Push(f => f.PostRegApplications, recordal)
                .Push(f => f.ApplicationHistory, assignmentHistory);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in NewAssignmentApplication: {ex.Message}");
            return false;
        }
    }
    public async Task<AssignmentAppDto> GetAssignmentApplication(string fileId, string appId)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();


        if (file == null) throw new KeyNotFoundException("File not found");

        var assignee = file.Assignees?.FirstOrDefault(a => a.Id == appId);
        var assignor = file.ApplicationHistory[0].Applicants[0];
        Console.WriteLine(JsonSerializer.Serialize(assignor));

        if (assignee == null) throw new KeyNotFoundException("Assignee not found");

        var assigneeDetails = new AssignmentAppDto
        {
            FileId = fileId,
            rrr = assignee.rrr,
            AssigneeName = assignee.Name,
            AssigneeEmail = assignee.Email,
            AssigneeAddress = assignee.Address,
            AssigneePhone = assignee.Phone,
            AssigneeNationality = assignee.Nationality,
            AssignorName = assignee.AssignorName ?? assignor.Name,
            AssignorEmail = assignee.AssignorEmail ?? assignor.Email,
            AssignorAddress = assignee.AssignorAddress ?? assignor.Address,
            AssignorPhone = assignee.AssignorPhone ?? assignor.Phone,
            AssignorNationality = assignee.AssignorNationality ?? assignor.country,
            AuthorizationLetterUrl = assignee.AuthorizationLetterUrl,
            AssignmentDeedUrl = assignee.AssignmentDeedUrl,
        };

        return assigneeDetails;

    }
    public async Task<bool> ApproveAssignment(TreatRecordalDto recordalApp)
    {
        try
        {
            _log.LogInformation($"Approving assignment for fileId: {recordalApp.fileId}, appId: {recordalApp.appId}");
            var file = await _fillingCollection
                 .Find(Builders<Filling>.Filter.Eq(f => f.FileId, recordalApp.fileId))
                 .FirstOrDefaultAsync();
            if (file == null) return false;
            var staff = await _userCollection.Find(u => u.Id == recordalApp.userId).FirstOrDefaultAsync();
            if (staff == null) throw new UnauthorizedAccessException("User is not authorized");
            // Update post reg
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status

            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;
            app.CurrentStatus = ApplicationStatuses.Approved;

            //Signature for Certificate
            var signature = await _signatures.Find(a => a.Designation == "recordalSignatory" && a.IsActive == true).FirstOrDefaultAsync();
            app.SignatoryName = signature.Name;
            app.Signature = signature.SignatureData;

            // Update Applicant
            var applicant = file.applicants?.FirstOrDefault();
            if (applicant == null) return false;

            applicant.Name = recordal.Name;
            applicant.Address = recordal.Address;
            applicant.Email = recordal.Email;
            applicant.Phone = recordal.Phone;
            //Update assignee
            var assignee = file.Assignees?.FirstOrDefault(r => r.Id == recordalApp.appId);
            if (assignee == null) return false;
            assignee.isApproved = true;

            var update = Builders<Filling>.Update
                .Set(f => f.PostRegApplications, file.PostRegApplications)
                .Set(f => f.ApplicationHistory, file.ApplicationHistory)
                .Set(f => f.Assignees, file.Assignees)
                .Set(f => f.applicants, file.applicants);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );

            var perform = new PerformanceDto
            {
                AfterStatus = ApplicationStatuses.Approved,
                BeforeStatus = ApplicationStatuses.AwaitingRecordalProcess,
                ApplicationType = FormApplicationTypes.Assignment,
                AppUserId = recordalApp.userId,
                Date = DateTime.Now,
                FileNumber = recordalApp.fileId,
                FileType = file.Type,
                OfficeUnit = Roles.TrademarkCertification,
                Reason = recordalApp.reason,
            };

            SavePerformance(perform);

            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in ApproveAssignment: {ex.Message}");
            Console.WriteLine(ex);
            return false;
        }
    }
    public async Task<ClericalUpdateDto> GetClericalUpdateCost(GetClericalCostDto dto)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileNumber))
                .FirstOrDefaultAsync();
            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                throw new Exception("File not found or no applicants available.");
            }
            var search = fileInfo.FileStatus == ApplicationStatuses.AwaitingSearch;
            var user = await _userCollection.Find(u => u.Id == dto.UserId).FirstOrDefaultAsync();
            if (user == null)
            {
                throw new Exception("User not found.");
            }
            string userName = $"{user.FirstName} {user.LastName}";

            (string, string, string) data = default;
            switch (dto.FileType)
            {
                case FileTypes.TradeMark:
                    data = _remitaPaymentUtils.GetCost(
                        PaymentTypes.ClericalUpdate, dto.FileType, "", null, null, null
                    );
                    break;

                case FileTypes.Patent:
                    data = _remitaPaymentUtils.GetCost(
                        PaymentTypes.PatentClericalUpdate, dto.FileType, "", null, null, null
                    );
                    break;

                case FileTypes.Design:
                    data = _remitaPaymentUtils.GetCost(
                        PaymentTypes.DesignClericalUpdate, dto.FileType, "", null, null, null
                    );
                    break;

                default:
                    throw new Exception("Failed to get cost");
            }

            var applicant = fileInfo.applicants[0];
            if (applicant == null) throw new Exception("No applicant found for the file.");
            string paymentId = null;
            if (!search)
            {
                paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                    data.Item1, data.Item3, data.Item2, "Clerical Update",
                    userName, user.Email, user.PhoneNumber);
            }
            else
            {
                paymentId = "Free";

            }


            Console.WriteLine("amount: " + data.Item1);

            var repAttachment = fileInfo?.Attachments
                    .FirstOrDefault(a => a.name == "representation" && a.url != null && a.url.Count > 0);
            var updateCost = new ClericalUpdateDto();
            switch (fileInfo?.Type)
            {
                case FileTypes.TradeMark:
                    updateCost = new ClericalUpdateDto
                    {
                        Cost = search ? "0" : data.Item1,
                        PaymentRRR = paymentId,
                        FileStatus = fileInfo.FileStatus,
                        FileId = dto.FileNumber,
                        FileTitle = fileInfo.TitleOfTradeMark ?? "",
                        FileType = fileInfo.Type,
                        ApplicantName = applicant.Name,
                        UpdateType = dto.UpdateType,
                        FileClass = fileInfo.TrademarkClass,
                        ServiceFee = data.Item3,
                        ApplicantEmail = applicant.Email,
                        ApplicantNationality = applicant.country,
                        ApplicantPhone = applicant.Phone,
                        ApplicantAddress = applicant.Address,
                        CorrespondenceName = fileInfo.Correspondence?.name,
                        CorrespondenceAddress = fileInfo.Correspondence?.address,
                        CorrespondenceEmail = fileInfo.Correspondence?.email,
                        CorrespondencePhone = fileInfo.Correspondence?.phone,
                        RepresentationUrl = repAttachment?.url.FirstOrDefault(),
                        Disclaimer = fileInfo.TrademarkDisclaimer,
                        TrademarkType = fileInfo.TrademarkType

                    };
                    break;
                case FileTypes.Design:
                    var designs = fileInfo.Attachments.FirstOrDefault(d => d.name == "designs");

                    updateCost = new ClericalUpdateDto
                    {
                        Cost = search ? "0" : data.Item1,
                        PaymentRRR = paymentId,
                        FileStatus = fileInfo.FileStatus,
                        FileId = dto.FileNumber,
                        FileTitle = fileInfo.TitleOfDesign ?? "",
                        FileType = fileInfo.Type,
                        ApplicantName = applicant.Name,
                        UpdateType = dto.UpdateType,
                        FileClass = fileInfo.TrademarkClass,
                        ServiceFee = data.Item3,
                        ApplicantEmail = applicant.Email,
                        ApplicantNationality = applicant.country,
                        ApplicantPhone = applicant.Phone,
                        ApplicantAddress = applicant.Address,
                        CorrespondenceName = fileInfo.Correspondence?.name,
                        CorrespondenceAddress = fileInfo.Correspondence?.address,
                        CorrespondenceEmail = fileInfo.Correspondence?.email,
                        CorrespondencePhone = fileInfo.Correspondence?.phone,
                        RepresentationUrl = repAttachment?.url.FirstOrDefault(),
                        Disclaimer = fileInfo.TrademarkDisclaimer,
                        TitleOfDesign = fileInfo.TitleOfDesign,
                        NoveltyStatement = fileInfo.StatementOfNovelty,
                        DesignType = fileInfo.DesignType,
                        DesignCreators = fileInfo.DesignCreators,
                        ExistingDesignAttachments = designs?.url
                    };
                    break;
                case FileTypes.Patent:
                    updateCost = new ClericalUpdateDto
                    {
                        Cost = search ? "0" : data.Item1,
                        PaymentRRR = paymentId,
                        FileStatus = fileInfo.FileStatus,
                        FileId = dto.FileNumber,
                        FileTitle = fileInfo.TitleOfInvention,
                        FileType = fileInfo.Type,
                        UpdateType = dto.UpdateType,
                        PatentType = fileInfo.PatentType,
                        PatentApplicationType = fileInfo.PatentApplicationType,
                        FileOrigin = fileInfo.FileOrigin,
                        TitleOfInvention = fileInfo.TitleOfInvention,
                        ServiceFee = data.Item3,
                        Applicants = fileInfo.applicants,
                        Inventors = fileInfo.Inventors,
                        ApplicantName = applicant.Name,
                        ApplicantEmail = applicant.Email,
                        ApplicantNationality = applicant.country,
                        ApplicantPhone = applicant.Phone,
                        ApplicantAddress = applicant.Address,
                        CorrespondenceName = fileInfo.Correspondence?.name,
                        CorrespondenceAddress = fileInfo.Correspondence?.address,
                        CorrespondenceEmail = fileInfo.Correspondence?.email,
                        CorrespondencePhone = fileInfo.Correspondence?.phone,
                        PatentAbstract = fileInfo.PatentAbstract,
                    };
                    break;
                default:
                    throw new Exception("Invalid file type for clerical update.");
            }

            return updateCost;
        }
        catch (Exception up)
        {
            _log.LogError(up, "Error-at-ClericalUpdate Cost");
            throw;
        }
    }

    public async Task<string> ClericalUpdate(ClericalUpdateDto updateData)
    {
        if (updateData == null)
            throw new ArgumentNullException(nameof(updateData));

        if (string.IsNullOrWhiteSpace(updateData.FileId))
            throw new ArgumentException("FileId is required");

        try
        {
            _log.LogInformation("Starting clerical update for FileId {FileId}, UpdateType {UpdateType}", updateData.FileId, updateData.UpdateType);
            Console.WriteLine($"Finding file: {updateData.FileId}");
            Console.WriteLine(JsonSerializer.Serialize(updateData, new JsonSerializerOptions { WriteIndented = true }));

            var file = await _fillingCollection
                .Find(f => f.FileId == updateData.FileId)
                .FirstOrDefaultAsync();

            if (file == null)
                throw new KeyNotFoundException("File Not Found");

            var user = await _userCollection.Find(u => u.Id == updateData.UserId).FirstOrDefaultAsync();
            if (user is null) throw new KeyNotFoundException("User not found");

            // Check if this exact clerical update already exists (idempotency)
            var existingUpdate = file.ClericalUpdates?.FirstOrDefault(c =>
                c.PaymentRRR == updateData.PaymentRRR &&
                c.UpdateType == updateData.UpdateType.ToString() &&
                c.FilingDate.Date == DateTime.Now.Date
            );

            if (existingUpdate != null)
            {
                _log.LogInformation("Clerical update already exists for FileId {FileId}, UpdateType {UpdateType}, AppId {AppId}", updateData.FileId, updateData.UpdateType, existingUpdate.Id);
                Console.WriteLine("Application already exists!");
                return existingUpdate.Id;
            }

            var applicant = file.applicants?.FirstOrDefault();

            var isAmendment =
                file.FileStatus == ApplicationStatuses.Publication ||
                file.FileStatus == ApplicationStatuses.AwaitingCertification;
            var appHistoryId = Guid.NewGuid().ToString();

            var appHistory = new ApplicationInfo
            {
                id = appHistoryId,
                ApplicationDate = DateTime.Now,
                PaymentId = updateData.PaymentRRR,
                ApplicationType = isAmendment
                    ? FormApplicationTypes.Amendment
                    : FormApplicationTypes.ClericalUpdate,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                StatusHistory =
                [
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.AwaitingPayment,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Message = "Clerical Update",
                        User = user.Name ?? $"{user.FirstName} {user.LastName}",
                        UserId = updateData.UserId
                    }
                ]
            };
            Console.WriteLine("creating clerical record... ");
            var clerical = await CreateClericalUpdateRecord(file, updateData, appHistoryId);
            clerical.IsAmendment = isAmendment;

            var update = Builders<Filling>.Update
                .Push(f => f.ApplicationHistory, appHistory)
                .Push(f => f.ClericalUpdates, clerical);

            var result = await _fillingCollection.UpdateOneAsync(
                f => f.Id == file.Id,
                update
            );

            Console.WriteLine($"Mongo Result → Matched: {result.MatchedCount}, Modified: {result.ModifiedCount}");

            if (result.MatchedCount == 0)
                throw new Exception("Update failed: document not matched");

            _log.LogInformation("Clerical update created for FileId {FileId}, AppId {AppId}", updateData.FileId, appHistoryId);
            return appHistoryId;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error during clerical update");
            return "Failed";
        }
    }
    private async Task<ClericalUpdate> CreateClericalUpdateRecord(
    Filling file,
    ClericalUpdateDto updateData,
    string appHistoryId)
    {
        var isPatent = file.Type == FileTypes.Patent;
        var isDesign = file.Type == FileTypes.Design;

        var clerical = new ClericalUpdate
        {
            Id = appHistoryId,
            UpdateType = updateData.UpdateType.ToString(),
            FilingDate = DateTime.Now,
            PaymentRRR = updateData.PaymentRRR
        };

        file.applicants ??= new List<ApplicantInfo>();

        async Task<string?> UploadSingle(IFormFile? file, string name)
        {
            if (file == null) return null;

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var urls = await UploadAttachment(
                new List<TT>
                {
                    new TT
                    {
                        data = ms.ToArray(),
                        fileName = Path.GetFileName(file.FileName),
                        contentType = file.ContentType,
                        Name = name
                    }
                });

            return urls.FirstOrDefault();
        }

        var representationUrl = await UploadSingle(updateData.Representation, "representation");
        var poaUrl = await UploadSingle(updateData.PowerOfAttorney, "poa");
        var otherAttachmentUrl = await UploadSingle(updateData.OtherAttachment, "others");

        var designUrls = new List<string>();
        if (updateData.DesignAttachments?.Any() == true)
        {
            for (int i = 0; i < updateData.DesignAttachments.Count; i++)
            {
                var url = await UploadSingle(updateData.DesignAttachments[i], $"design{i + 1}");
                if (!string.IsNullOrEmpty(url))
                    designUrls.Add(url);
            }
        }

        switch (updateData.UpdateType)
        {
            case ClericalUpdateTypes.ApplicantName:
                clerical.OldApplicantNames = file.applicants.Select(a => a.Name).ToList();
                clerical.NewApplicantNames = new List<string> { updateData.ApplicantName };
                break;

            case ClericalUpdateTypes.ApplicantAddress:
                clerical.OldApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                clerical.NewApplicantAddresses = new List<string> { updateData.ApplicantAddress };

                if (!string.IsNullOrWhiteSpace(updateData.ApplicantEmail))
                {
                    clerical.OldApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                    clerical.NewApplicantEmails = new List<string> { updateData.ApplicantEmail };
                }
                if (!string.IsNullOrWhiteSpace(updateData.ApplicantPhone))
                {
                    clerical.OldApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                    clerical.NewApplicantPhones = new List<string> { updateData.ApplicantPhone };
                }

                break;

            case ClericalUpdateTypes.TrademarkType:
                if (!string.IsNullOrWhiteSpace(updateData.ApplicantNationality))
                {
                    clerical.OldApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                    clerical.NewApplicantNationalities = new List<string> { updateData.ApplicantNationality };
                }
                if (updateData.TrademarkType is not null)
                {
                    clerical.OldTrademarkType = file.TrademarkType;
                    clerical.NewTrademarkType = updateData.TrademarkType;
                }
                break;

            case ClericalUpdateTypes.FileClass:
                clerical.OldFileClass = file.TrademarkClass?.ToString();
                clerical.NewFileClass = updateData.FileClass?.ToString();
                if (updateData.FileClass != null)
                {
                    var newDescription = FileUtils.TrademarkClassMapper.GetDescription(updateData.FileClass.Value);
                    var oldDescription = FileUtils.TrademarkClassMapper.GetDescription(file.TrademarkClass.Value);
                    clerical.OldClassDescription = oldDescription;
                    clerical.NewClassDescription = newDescription;
                }
                if (updateData.AdditionalDescription != null)
                {
                    clerical.OldAdditionalDescription = file?.AdditionalDescription;
                    clerical.NewAdditionalDescription = updateData.AdditionalDescription;
                }
                break;

            case ClericalUpdateTypes.CorrespondenceInformation:
                clerical.OldCorrespondenceName = file.Correspondence?.name;
                clerical.NewCorrespondenceName = updateData.CorrespondenceName;
                clerical.OldCorrespondenceAddress = file.Correspondence?.address;
                clerical.NewCorrespondenceAddress = updateData.CorrespondenceAddress;
                clerical.OldCorrespondenceEmail = file.Correspondence?.email;
                clerical.NewCorrespondenceEmail = updateData.CorrespondenceEmail;
                clerical.OldCorrespondencePhone = file.Correspondence?.phone;
                clerical.NewCorrespondencePhone = updateData.CorrespondencePhone;
                clerical.OldPowerOfAttorneyUrl =
                    file.Attachments?.FirstOrDefault(a => a.name == "poa")?.url?.FirstOrDefault();
                clerical.NewPowerOfAttorneyUrl = poaUrl;
                clerical.NewAttachmentUrl = otherAttachmentUrl ?? null;
                break;

            case ClericalUpdateTypes.FileTitle:
                clerical.OldFileTitle =
                    updateData.FileType == FileTypes.Design ? file.TitleOfDesign :
                    updateData.FileType == FileTypes.Patent ? file.TitleOfInvention :
                    file.TitleOfTradeMark;

                clerical.NewFileTitle = updateData.FileTitle;

                clerical.OldRepresentationUrl =
                    file.Attachments?.FirstOrDefault(a => a.name == "representation")?.url?.FirstOrDefault();

                clerical.NewRepresentationUrl = representationUrl;

                if (updateData.TrademarkLogo.HasValue)
                {
                    clerical.OldTrademarkLogo = file.TrademarkLogo?.ToString();
                    clerical.NewTrademarkLogo = updateData.TrademarkLogo.Value.ToString();
                }
                if (isPatent)
                {
                    if (!string.IsNullOrWhiteSpace(updateData.PatentAbstract))
                    {
                        clerical.OldPatentAbstract = file.PatentAbstract;
                        clerical.NewPatentAbstract = updateData.PatentAbstract;
                    }
                    if (updateData.PatentApplicationType.HasValue)
                    {
                        clerical.OldPatentApplicationType = file.PatentApplicationType;
                        clerical.NewPatentApplicationType = updateData.PatentApplicationType;
                    }
                }
                break;

            case ClericalUpdateTypes.DesignInformation:
                if (!string.IsNullOrWhiteSpace(updateData.TitleOfDesign))
                {
                    clerical.OldFileTitle = file.TitleOfDesign;
                    clerical.NewFileTitle = updateData.TitleOfDesign;
                }

                if (!string.IsNullOrWhiteSpace(updateData.NoveltyStatement))
                {
                    clerical.OldNoveltyStatement = file.StatementOfNovelty;
                    clerical.NewNoveltyStatement = updateData.NoveltyStatement;
                }

                if (updateData.DesignType is not null)
                {
                    clerical.OldDesignType = file.DesignType;
                    clerical.NewDesignType = updateData.DesignType;
                }

                break;

            case ClericalUpdateTypes.CreatorInformation:
                clerical.OldDesignCreators = file.DesignCreators?.Select(c => new ApplicantInfo
                {
                    id = c.id,
                    Name = c.Name,
                    Address = c.Address,
                    Email = c.Email,
                    Phone = c.Phone,
                    country = c.country,
                    State = c.State,
                    city = c.city
                }).ToList();

                // Build new creators list by merging changes
                var updatedCreators = file.DesignCreators?.ToList() ?? new List<ApplicantInfo>();

                if (updateData.DesignCreators?.Any() == true)
                {
                    foreach (var newCreator in updateData.DesignCreators)
                    {
                        var existingCreator = updatedCreators.FirstOrDefault(c => c.id == newCreator.id);

                        if (existingCreator != null)
                        {
                            // Update only the fields that are provided (not null/empty)
                            if (!string.IsNullOrWhiteSpace(newCreator.Name))
                                existingCreator.Name = newCreator.Name;
                            if (!string.IsNullOrWhiteSpace(newCreator.Address))
                                existingCreator.Address = newCreator.Address;
                            if (!string.IsNullOrWhiteSpace(newCreator.Email))
                                existingCreator.Email = newCreator.Email;
                            if (!string.IsNullOrWhiteSpace(newCreator.Phone))
                                existingCreator.Phone = newCreator.Phone;
                            if (!string.IsNullOrWhiteSpace(newCreator.country))
                                existingCreator.country = newCreator.country;
                        }
                        else
                        {
                            // New creator - add to list with generated ID if missing
                            newCreator.id ??= Guid.NewGuid().ToString();
                            updatedCreators.Add(newCreator);
                        }
                    }
                }

                // Handle removals if specified
                if (updateData.RemoveInventorIds?.Any() == true)
                {
                    updatedCreators.RemoveAll(c => updateData.RemoveInventorIds.Contains(c.id));
                }

                clerical.NewDesignCreators = updatedCreators;
                clerical.NewDesignCreatorNames = updatedCreators.Select(c => c.Name).ToList();
                clerical.NewDesignCreatorAddresses = updatedCreators.Select(c => c.Address).ToList();
                clerical.NewDesignCreatorEmails = updatedCreators.Select(c => c.Email).ToList();
                clerical.NewDesignCreatorPhones = updatedCreators.Select(c => c.Phone).ToList();
                clerical.NewDesignCreatorNationalities = updatedCreators.Select(c => c.country).ToList();

                if (file.DesignCreators?.Any() == true)
                {
                    clerical.OldDesignCreatorNames = file.DesignCreators.Select(c => c.Name).ToList();
                    clerical.OldDesignCreatorAddresses = file.DesignCreators.Select(c => c.Address).ToList();
                    clerical.OldDesignCreatorEmails = file.DesignCreators.Select(c => c.Email).ToList();
                    clerical.OldDesignCreatorPhones = file.DesignCreators.Select(c => c.Phone).ToList();
                    clerical.OldDesignCreatorNationalities = file.DesignCreators.Select(c => c.country).ToList();
                }
                break;

            case ClericalUpdateTypes.AddApplicant:

            case ClericalUpdateTypes.RemoveApplicant:

            case ClericalUpdateTypes.AddAndRemoveApplicant:
                clerical.OldApplicantNames = file.applicants.Select(a => a.Name).ToList();

                var modified = file.applicants.ToList();

                if (updateData.RemoveApplicantIds?.Any() == true)
                    modified.RemoveAll(a => updateData.RemoveApplicantIds.Contains(a.id));

                if (updateData.NewApplicants?.Any() == true)
                    modified.AddRange(updateData.NewApplicants);

                clerical.NewApplicantNames = modified.Select(a => a.Name).ToList();
                break;

            case ClericalUpdateTypes.EditInventors:
                clerical.OldInventors = file.Inventors?.Select(i => new ApplicantInfo
                {
                    id = i.id,
                    Name = i.Name,
                    Address = i.Address,
                    Email = i.Email,
                    Phone = i.Phone,
                    country = i.country,
                    State = i.State,
                    city = i.city
                }).ToList();

                // Build new inventors list by merging changes
                var updatedInventors = file.Inventors?.ToList() ?? new List<ApplicantInfo>();

                if (updateData.NewInventors?.Any() == true)
                {
                    foreach (var newInventor in updateData.NewInventors)
                    {
                        var existingInventor = updatedInventors.FirstOrDefault(i => i.id == newInventor.id);

                        if (existingInventor != null)
                        {
                            // Update only the fields that are provided (not null/empty)
                            if (!string.IsNullOrWhiteSpace(newInventor.Name))
                                existingInventor.Name = newInventor.Name;
                            if (!string.IsNullOrWhiteSpace(newInventor.Address))
                                existingInventor.Address = newInventor.Address;
                            if (!string.IsNullOrWhiteSpace(newInventor.Email))
                                existingInventor.Email = newInventor.Email;
                            if (!string.IsNullOrWhiteSpace(newInventor.Phone))
                                existingInventor.Phone = newInventor.Phone;
                            if (!string.IsNullOrWhiteSpace(newInventor.country))
                                existingInventor.country = newInventor.country;
                            if (!string.IsNullOrWhiteSpace(newInventor.State))
                                existingInventor.State = newInventor.State;
                            if (!string.IsNullOrWhiteSpace(newInventor.city))
                                existingInventor.city = newInventor.city;
                        }
                        else
                        {
                            // New inventor - add to list with generated ID if missing
                            newInventor.id ??= Guid.NewGuid().ToString();
                            updatedInventors.Add(newInventor);
                        }
                    }
                }

                // Handle removals if specified
                if (updateData.RemoveInventorIds?.Any() == true)
                {
                    updatedInventors.RemoveAll(i => updateData.RemoveInventorIds.Contains(i.id));
                }

                clerical.NewInventors = updatedInventors;
                clerical.NewInventorNames = updatedInventors.Select(i => i.Name).ToList();
                clerical.NewInventorAddresses = updatedInventors.Select(i => i.Address).ToList();
                clerical.NewInventorEmails = updatedInventors.Select(i => i.Email).ToList();
                clerical.NewInventorPhones = updatedInventors.Select(i => i.Phone).ToList();
                clerical.NewInventorNationalities = updatedInventors.Select(i => i.country).ToList();
                clerical.NewInventorStates = updatedInventors.Select(i => i.State).ToList();
                clerical.NewInventorCities = updatedInventors.Select(i => i.city).ToList();

                if (file.Inventors?.Any() == true)
                {
                    clerical.OldInventorNames = file.Inventors.Select(i => i.Name).ToList();
                    clerical.OldInventorAddresses = file.Inventors.Select(i => i.Address).ToList();
                    clerical.OldInventorEmails = file.Inventors.Select(i => i.Email).ToList();
                    clerical.OldInventorPhones = file.Inventors.Select(i => i.Phone).ToList();
                    clerical.OldInventorNationalities = file.Inventors.Select(i => i.country).ToList();
                    clerical.OldInventorStates = file.Inventors.Select(i => i.State).ToList();
                    clerical.OldInventorCities = file.Inventors.Select(i => i.city).ToList();
                }
                break;

            case ClericalUpdateTypes.PriorityInfo:
                if (updateData.PriorityInfo is not null)
                {
                    clerical.OldPriorityInfo = file.PriorityInfo;
                    clerical.NewPriorityInfo = updateData.PriorityInfo;
                }
                if (updateData.FirstPriorityInfo is not null)
                {
                    clerical.OldFirstPriorityInfo = file.FirstPriorityInfo;
                    clerical.NewFirstPriorityInfo = updateData.FirstPriorityInfo;
                }
                
                break;

            case ClericalUpdateTypes.DesignAttachments:
                clerical.OldDesignAttachmentUrls = file.Attachments?
                                                    .Where(a => a.name.StartsWith("design"))
                                                    .SelectMany(a => a.url ?? new List<string>())
                                                    .ToList();
                var remainingUrls = clerical.OldDesignAttachmentUrls?.ToList() ?? new List<string>();
                if (updateData.RemoveDesignAttachmentUrls?.Any() == true)
                {
                    remainingUrls.RemoveAll(url => updateData.RemoveDesignAttachmentUrls.Contains(url));
                }

                // Add newly uploaded design attachments
                if (designUrls.Any())
                {
                    remainingUrls.AddRange(designUrls);
                }

                // Store the final list
                clerical.NewDesignAttachmentUrls = remainingUrls;

                // For backwards compatibility with single attachment field
                clerical.NewAttachmentUrl = remainingUrls.LastOrDefault();

                break;
        }
        return clerical;
    }

    public async Task<bool> ApplyClericalUpdateToFile(string fileId, string clericalUpdateId)
    {
        try
        {
            _log.LogInformation($"Applying clerical update {clericalUpdateId} to file {fileId}");

            // Fetch file
            var file = await _fillingCollection
                .Find(f => f.FileId == fileId)
                .FirstOrDefaultAsync();

            if (file == null)
                throw new KeyNotFoundException("File not found");
            var isDesign = file.Type == FileTypes.Design;
            var isPatent = file.Type == FileTypes.Patent;
            var isTrademark = file.Type == FileTypes.TradeMark;
            file.applicants ??= new List<ApplicantInfo>();
            file.ClericalUpdates ??= new List<ClericalUpdate>();
            file.ApplicationHistory ??= new List<ApplicationInfo>();
            file.Attachments ??= new List<AttachmentType>();

            // Fetch clerical update
            var clerical = file.ClericalUpdates.FirstOrDefault(c => c.Id == clericalUpdateId);
            if (clerical == null)
            {
                _log.LogDebug("Clerical application not found");
                throw new KeyNotFoundException("Clerical update record not found");
            };

            var app = file.ApplicationHistory.FirstOrDefault(a => a.id == clericalUpdateId);

            // Free update check
            var freeUpdate = file.FileStatus == ApplicationStatuses.AwaitingSearch;
            if (!freeUpdate)
            {
                var paid = await _paymentService.CheckPayment(app?.PaymentId ?? clerical.PaymentRRR);
                Console.WriteLine(paid);
                if (paid?.status != "00")
                    throw new KeyNotFoundException("Payment not completed for this clerical update");
            }

            // Match application history by ID
            if (app == null)
                throw new KeyNotFoundException("Application history not found");
        
            var updates = new List<UpdateDefinition<Filling>>();

            // Handle each clerical update type
            switch (clerical.UpdateType)
            {
                case "ApplicantName":
                    if (clerical.NewApplicantNames?.Any() == true)
                    {
                        for (int i = 0; i < clerical.NewApplicantNames.Count && i < file.applicants.Count; i++)
                            if (!string.IsNullOrWhiteSpace(clerical.NewApplicantNames[i]))
                                file.applicants[i].Name = clerical.NewApplicantNames[i];

                        updates.Add(Builders<Filling>.Update.Set(f => f.applicants, file.applicants));
                    }
                    else if (!string.IsNullOrWhiteSpace(clerical.NewApplicantName) && file.applicants.Count > 0)
                    {
                        file.applicants[0].Name = clerical.NewApplicantName;
                        updates.Add(Builders<Filling>.Update.Set(f => f.applicants, file.applicants));
                    }
                    break;
                case "ApplicantAddress":
                    if (clerical.NewApplicantAddresses?.Any() == true)
                    {
                        for (int i = 0; i < clerical.NewApplicantAddresses.Count && i < file.applicants.Count; i++)
                        {
                            if (i < clerical.NewApplicantAddresses?.Count && !string.IsNullOrWhiteSpace(clerical.NewApplicantAddresses[i]))
                                file.applicants[i].Address = clerical.NewApplicantAddresses[i];

                            if (i < clerical.NewApplicantEmails?.Count && !string.IsNullOrWhiteSpace(clerical.NewApplicantEmails[i]))
                                file.applicants[i].Email = clerical.NewApplicantEmails[i];

                            if (i < clerical.NewApplicantPhones?.Count && !string.IsNullOrWhiteSpace(clerical.NewApplicantPhones[i]))
                                file.applicants[i].Phone = clerical.NewApplicantPhones[i];

                            if (i < clerical.NewApplicantNationalities?.Count && !string.IsNullOrWhiteSpace(clerical.NewApplicantNationalities[i]))
                                file.applicants[i].country = clerical.NewApplicantNationalities[i];

                            if (i < clerical.NewApplicantStates?.Count && !string.IsNullOrWhiteSpace(clerical.NewApplicantStates[i]))
                                file.applicants[i].State = clerical.NewApplicantStates[i];

                            if (i < clerical.NewApplicantCities?.Count && !string.IsNullOrWhiteSpace(clerical.NewApplicantCities[i]))
                                file.applicants[i].city = clerical.NewApplicantCities[i];
                        }
                        updates.Add(Builders<Filling>.Update.Set(f => f.applicants, file.applicants));
                    }
                    else if (!string.IsNullOrWhiteSpace(clerical.NewApplicantAddress) && file.applicants.Count > 0)
                    {
                        file.applicants[0].Address = clerical.NewApplicantAddress;
                        updates.Add(Builders<Filling>.Update.Set(f => f.applicants, file.applicants));
                    }
                    break;
                case "CreatorInformation":
                    if (clerical.NewDesignCreators?.Any() == true)
                    {
                        updates.Add(Builders<Filling>.Update.Set(f => f.DesignCreators, clerical.NewDesignCreators));
                    }
                    break;
                case "EditInventors":
                    if (clerical.NewInventors?.Any() == true)
                    {
                        updates.Add(Builders<Filling>.Update.Set(f => f.Inventors, clerical.NewInventors));
                    }
                    break;
                case "DesignInformation":
                    if (!string.IsNullOrWhiteSpace(clerical.NewFileTitle))
                    {
                        updates.Add(Builders<Filling>.Update.Set(f=> f.TitleOfDesign, clerical.NewFileTitle));
                    }

                    if (!string.IsNullOrWhiteSpace(clerical.NewNoveltyStatement))
                    {
                        updates.Add(Builders<Filling>.Update.Set(f=> f.StatementOfNovelty, clerical.NewNoveltyStatement));
                    }

                    if (clerical.NewDesignType is not null)
                    {
                        updates.Add(Builders<Filling>.Update.Set(f=> f.DesignType, clerical.NewDesignType));
                    }
                    break;
                case "FileClass":
                    if (!string.IsNullOrWhiteSpace(clerical.NewFileClass))
                        updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkClass, int.Parse(clerical.NewFileClass)));

                    if (!string.IsNullOrWhiteSpace(clerical.NewClassDescription))
                        updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkClassDescription, clerical.NewClassDescription));
                    if (!string.IsNullOrWhiteSpace(clerical.NewAdditionalDescription))
                        updates.Add(Builders<Filling>.Update.Set(f => f.AdditionalDescription, clerical.NewAdditionalDescription));
                    if (!string.IsNullOrWhiteSpace(clerical.NewDisclaimer))
                        updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkDisclaimer, clerical.NewDisclaimer));
                    break;
                case "CorrespondenceInformation":
                    var correspondence = file.Correspondence ?? new CorrespondenceType();
                    var hasCorrespondence = false;

                    if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceName)) { correspondence.name = clerical.NewCorrespondenceName; hasCorrespondence = true; }
                    if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceAddress)) { correspondence.address = clerical.NewCorrespondenceAddress; hasCorrespondence = true; }
                    if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondencePhone)) { correspondence.phone = clerical.NewCorrespondencePhone; hasCorrespondence = true; }
                    if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceEmail)) { correspondence.email = clerical.NewCorrespondenceEmail; hasCorrespondence = true; }

                    // Handle attachments
                    if (!string.IsNullOrWhiteSpace(clerical.NewPowerOfAttorneyUrl))
                    {
                        var idx = file.Attachments.FindIndex(a => a.name == "poa");
                        if (idx >= 0)
                            file.Attachments[idx].url = new List<string> { clerical.NewPowerOfAttorneyUrl };
                        else
                            file.Attachments.Add(new AttachmentType { name = "poa", url = new List<string> { clerical.NewPowerOfAttorneyUrl } });
                    }

                    if (!string.IsNullOrWhiteSpace(clerical.NewAttachmentUrl))
                        file.Attachments.Add(new AttachmentType { name = "other", url = new List<string> { clerical.NewAttachmentUrl } });

                    if (file.Attachments.Any())
                        updates.Add(Builders<Filling>.Update.Set(f => f.Attachments, file.Attachments));

                    if (hasCorrespondence)
                        updates.Add(Builders<Filling>.Update.Set(f => f.Correspondence, correspondence));

                    break;
                case "TrademarkType":
                    if (!string.IsNullOrWhiteSpace(clerical.NewApplicantNationality))
                    {
                        for (int i = 0; i < clerical.NewApplicantNationalities.Count && i < file.applicants.Count; i++)
                            if (!string.IsNullOrWhiteSpace(clerical.NewApplicantNationalities[i]))
                                file.applicants[i].country = clerical.NewApplicantNationalities[i];
                        updates.Add(Builders<Filling>.Update.Set(f => f.applicants, file.applicants));
                    }
                    else if (!string.IsNullOrWhiteSpace(clerical.NewApplicantNationality) && file.applicants.Count > 0)
                    {
                        file.applicants[0].country = clerical.NewApplicantNationality;
                        updates.Add(Builders<Filling>.Update.Set(f => f.applicants, file.applicants));
                    }
                    if (clerical.NewTrademarkType is not null)
                    {
                        updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkType, clerical.NewTrademarkType));

                        // Update FileId prefix based on TrademarkType
                        var fileIdParts = file.FileId?.Split('/');
                        if (fileIdParts is { Length: >= 1 })
                        {
                            // TradeMarkType.Foreign = 1, TradeMarkType.Local = 0
                            var newPrefix = clerical.NewTrademarkType == TradeMarkType.Foreign ? "F" : "NG";

                            if (fileIdParts[0] != newPrefix)
                            {
                                fileIdParts[0] = newPrefix;
                                var updatedFileId = string.Join("/", fileIdParts);
                                updates.Add(Builders<Filling>.Update.Set(f => f.FileId, updatedFileId));
                            }
                        }
                    }
                    break;
                case "FileTitle":
                    if (!string.IsNullOrWhiteSpace(clerical.NewFileTitle))
                    {
                        switch (file.Type)
                        {
                            case FileTypes.Design:
                                updates.Add(Builders<Filling>.Update.Set(f => f.TitleOfDesign, clerical.NewFileTitle));
                                break;
                            case FileTypes.Patent:
                                updates.Add(Builders<Filling>.Update.Set(f => f.TitleOfInvention, clerical.NewFileTitle));

                                break;
                            case FileTypes.TradeMark:
                                updates.Add(Builders<Filling>.Update.Set(f => f.TitleOfTradeMark, clerical.NewFileTitle));
                                break;
                        }
                    }
                    if (isPatent)
                    {
                        if (!string.IsNullOrWhiteSpace(clerical.NewPatentAbstract))
                        {
                            updates.Add(Builders<Filling>.Update.Set(f => f.PatentAbstract, clerical.NewPatentAbstract));
                        }
                        if (clerical.NewPatentApplicationType != null)
                        {
                            updates.Add(Builders<Filling>.Update.Set(f => f.PatentApplicationType, clerical.NewPatentApplicationType));
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(clerical.NewTrademarkLogo) &&
                        Enum.TryParse<TradeMarkLogo>(clerical.NewTrademarkLogo, out var logo))
                    {
                        updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkLogo, logo));
                    }

                    if (!string.IsNullOrWhiteSpace(clerical.NewRepresentationUrl))
                    {
                        var repIdx = file.Attachments.FindIndex(a => a.name == "representation");
                        if (repIdx >= 0)
                            file.Attachments[repIdx].url = new List<string> { clerical.NewRepresentationUrl };
                        else
                            file.Attachments.Add(new AttachmentType { name = "representation", url = new List<string> { clerical.NewRepresentationUrl } });

                        updates.Add(Builders<Filling>.Update.Set(f => f.Attachments, file.Attachments));
                    }
                    break;
                case "DesignAttachments":
                    if (clerical.NewDesignAttachmentUrls?.Any() == true)
                    {
                        var designsIdx = file.Attachments.FindIndex(a => a.name == "designs");
                        if (designsIdx >= 0)
                        {
                            file.Attachments[designsIdx].url = clerical.NewDesignAttachmentUrls;
                        }
                        else
                        {
                            file.Attachments.Add(new AttachmentType
                            {
                                name = "designs",
                                url = clerical.NewDesignAttachmentUrls
                            });
                        }
                        updates.Add(Builders<Filling>.Update.Set(f => f.Attachments, file.Attachments));
                    }
                    break;
                case "PriorityInfo":
                    if (clerical.NewFirstPriorityInfo?.Any() == true)
                    {
                        updates.Add(Builders<Filling>.Update.Set(f => f.FirstPriorityInfo, clerical.NewFirstPriorityInfo));
                    }
                    if (clerical.NewPriorityInfo?.Any() == true)
                    {
                        updates.Add(Builders<Filling>.Update.Set(f => f.PriorityInfo, clerical.NewPriorityInfo));
                    }
                    break;
            }

            if (!updates.Any())
                throw new Exception("No update definitions were generated");

            // Update statuses
            if (app.ApplicationType != FormApplicationTypes.Amendment)
            {
                clerical.IsApproved = true;
                clerical.DateTreated = DateTime.Now;
                app.CurrentStatus = ApplicationStatuses.AutoApproved;

                if (isTrademark && (file.FileStatus == ApplicationStatuses.AwaitingExaminer || file.FileStatus == ApplicationStatuses.Re_conduct))
                {
                    file.ApplicationHistory[0].CurrentStatus = ApplicationStatuses.AwaitingSearch;
                    updates.Add(Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.AwaitingSearch));
                }
                else if (!isTrademark && (file.FileStatus == ApplicationStatuses.AwaitingExaminer))
                {
                    file.ApplicationHistory[0].CurrentStatus = ApplicationStatuses.AwaitingExaminer;
                    updates.Add(Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.AwaitingExaminer));
                }

                updates.Add(Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory));
                updates.Add(Builders<Filling>.Update.Set(f => f.ClericalUpdates, file.ClericalUpdates));
            }
            else
            {
                app.CurrentStatus = ApplicationStatuses.AwaitingApproval;
                clerical.DateTreated = DateTime.Now;
                updates.Add(Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory));
                updates.Add(Builders<Filling>.Update.Set(f => f.ClericalUpdates, file.ClericalUpdates));
            }

            // Apply updates to Mongo
            var result = await _fillingCollection.UpdateOneAsync(
                f => f.FileId == fileId,
                Builders<Filling>.Update.Combine(updates)
            );

            Console.WriteLine($"Clerical update applied successfully. ModifiedCount: {result.ModifiedCount}");
            if (result.ModifiedCount > 0)
            {
                _log.LogInformation("Clerical update applied for FileId {FileId}, AppId {AppId}", fileId, clericalUpdateId);
            }
            else
            {
                _log.LogWarning("Clerical update produced no changes for FileId {FileId}, AppId {AppId}", fileId, clericalUpdateId);
            }

            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            _log.LogError(ex, $"Error applying clerical update {clericalUpdateId} to file {fileId}");
            return false;
        }
    }

    //Get existing clerical update application
    public async Task<ClericalUpdateDetailsDto> GetClericalUpdateApp(string fileId, string appId)
    {
        static string? GetArrayValue(List<string>? values)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            var nonEmpty = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (nonEmpty.Count == 0)
            {
                return null;
            }

            return string.Join(", ", nonEmpty);
        }

        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();
        Console.WriteLine("file: " + fileId);
        Console.WriteLine("Application" + appId);
        if (file == null) return null;

        var clerical = file.ClericalUpdates?.FirstOrDefault(p => p.Id == appId);
        if (clerical == null) return null;
        var update = new ClericalUpdateDetailsDto
        {
            UpdateType = clerical.UpdateType,
            PaymentId = clerical.PaymentRRR
        };
        switch (clerical.UpdateType)
        {
            case "ApplicantName":
                update.OldValue = GetArrayValue(clerical?.OldApplicantNames) ?? clerical?.OldApplicantName;
                update.NewValue = GetArrayValue(clerical?.NewApplicantNames) ?? clerical?.NewApplicantName;
                break;
            case "ApplicantAddress":
                update.OldValue = clerical?.OldApplicantAddress;
                update.NewValue = clerical?.NewApplicantAddress;
                update.OldValue2 = clerical?.OldApplicantEmail;
                update.NewValue2 = clerical?.NewApplicantEmail;
                update.OldValue3 = clerical?.OldApplicantPhone;
                update.NewValue3 = clerical?.NewApplicantPhone;
                break;
            case "FileClass":
                update.OldValue = clerical?.OldFileClass;
                update.NewValue = clerical?.NewFileClass;
                update.OldValue2 = clerical?.OldClassDescription;
                update.NewValue2 = clerical?.NewClassDescription;
                break;
            case "Correspondence":
                update.OldValue = clerical?.OldCorrespondenceName;
                update.NewValue = clerical?.NewCorrespondenceName;
                update.OldValue2 = clerical?.OldCorrespondenceAddress;
                update.NewValue2 = clerical?.NewCorrespondenceAddress;
                update.OldValue3 = clerical?.OldCorrespondenceEmail;
                update.NewValue3 = clerical?.NewCorrespondenceEmail;
                update.OldValue4 = clerical?.OldCorrespondencePhone;
                update.NewValue4 = clerical?.NewCorrespondencePhone;
                update.OldPowerOfAttorneyUrl = clerical?.OldPowerOfAttorneyUrl;
                update.NewPowerOfAttorneyUrl = clerical?.NewPowerOfAttorneyUrl;
                break;
            case "FileTitle":
                update.OldValue = clerical?.OldFileTitle;
                update.NewValue = clerical?.NewFileTitle;

                update.OldValue2 = clerical?.OldTrademarkLogo;
                update.NewValue2 = clerical?.NewTrademarkLogo;
                update.OldRepresentation = clerical?.OldRepresentationUrl;
                if (update.OldRepresentation != null)
                {
                    update.NewRepresentation = clerical?.NewRepresentationUrl;
                }
                break;
            case "Opposition Amendment":
                update.OldValue = clerical?.OldRepresentationUrl;
                update.NewValue = clerical?.NewRepresentationUrl;

                update.OldValue2 = clerical?.OldDisclaimer;
                update.NewValue2 = clerical?.NewDisclaimer;

                update.OldValue3 = clerical?.OldAdditionalDescription;
                update.NewValue3 = clerical?.NewAdditionalDescription;
                break;
        }

        return update;
    }
    public async Task<bool> UpdateRecordalStatus(string fileId, string rrr)
    {
        try
        {
            var file = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();
            Console.WriteLine(file);
            if (file == null)
                return false;
            var remita = await _remitaPaymentUtils.GetDetailsByRRR(rrr);
            if (remita != null && remita.status != "00") return false;
            var recordal = file.ApplicationHistory?.FirstOrDefault(a => a.PaymentId == rrr);
            Console.WriteLine(recordal);
            if (recordal == null)
                return false;
            var payment = new PaymentRecord
            {
                PaymentType = recordal.ApplicationType.ToString(),
                Date = DateTime.Now,
                FileId = fileId,
                ApplicationId = recordal.id,
                FileType = file.Type.ToString(),
                RemitaResponse = remita
            };
            await _paymentService.AddPaymentRecord(payment);
            if (file.FileStatus == ApplicationStatuses.Publication || file.FileStatus == ApplicationStatuses.AwaitingCertification)
            {
                var rec = new TreatRecordalDto
                {
                    fileId = file.FileId,
                    appId = recordal.id,
                    reason = "Auto-Approved by System"
                };
                await ApproveChangeDataRecordal(rec);
                recordal.CurrentStatus = ApplicationStatuses.AutoApproved;
                recordal.StatusHistory[0].afterStatus = ApplicationStatuses.AutoApproved;
            }
            else
            {
                recordal.CurrentStatus = ApplicationStatuses.AwaitingRecordalProcess;
                recordal.StatusHistory[0].afterStatus = ApplicationStatuses.AwaitingRecordalProcess;
            }

            recordal.StatusHistory[0].beforeStatus = ApplicationStatuses.AwaitingPayment;



            var update = Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );

            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error at UpdateRecordalStatus");
            Console.WriteLine(ex);
            return false;
        }
    }
    public async Task<bool> UpdateCertificatePaymentStatus(string fileId, string rrr)
    {
        try
        {
            var filter = Builders<Filling>.Filter.And(
                Builders<Filling>.Filter.Eq(f => f.FileId, fileId),
                Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory,
                    a => a.CertificatePaymentId == rrr)
            );

            var newStatusHistory = new ApplicationHistory
            {
                Date = DateTime.Now,
                beforeStatus = ApplicationStatuses.AwaitingCertification,
                afterStatus = ApplicationStatuses.AwaitingCertificateConfirmation,
                Message = "Payment Successful moving to Awaiting Certificate Confirmation",
            };

            var update = Builders<Filling>.Update
                .Set("ApplicationHistory.$[app].CurrentStatus", ApplicationStatuses.AwaitingCertificateConfirmation)
                .Set("FileStatus", ApplicationStatuses.AwaitingCertificateConfirmation)
                .Push("ApplicationHistory.$[app].StatusHistory", newStatusHistory);

            var arrayFilters = new List<ArrayFilterDefinition>
            {
                new JsonArrayFilterDefinition<BsonDocument>("{'app.CertificatePaymentId': '" + rrr + "'}")
            };

            var updateOptions = new UpdateOptions { ArrayFilters = arrayFilters };

            var result = await _fillingCollection.UpdateOneAsync(filter, update, updateOptions);

            if (result.ModifiedCount > 0)
            {
                Console.WriteLine("Successfully updated certificate payment status for FileId");
                return true;
            }
            else
            {
                Console.WriteLine("No document updated. Either already updated or document not found. FileId");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error while updating certificate payment status for FileId");
            throw ex;
        }
    }

    public async Task<FileApplicationsDto> GetApplicationsByFile(string fileId)
    {
        try
        {
            var file = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();
            string? fileTitle = "";
            if (file.Type == FileTypes.TradeMark)
            {
                fileTitle = file.TitleOfTradeMark;
            }
            else if (file.Type == FileTypes.Patent)
            {
                fileTitle = file.TitleOfInvention;
            }
            else
            {
                fileTitle = file.TitleOfDesign;
            }
            var apps = file.ApplicationHistory.ToList();

            CertificateAppDto cert = new CertificateAppDto
            {
                CurrentStatus = apps[0].CurrentStatus,
                PaymentId = apps[0].CertificatePaymentId,
                id = apps[0].id,
                ApplicationType = FormApplicationTypes.Certification,
                ApplicationDate = apps[0].ApplicationDate
            };

            var result = new FileApplicationsDto
            {
                FileTitle = fileTitle ?? "",
                Applications = apps,
                CertificateApp = cert
            };
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error at GetPaymentIds for FileId: {fileId}");
            throw;
        }
    }
    public async Task<bool> UpdatePaymentId(UpdatePaymentDto dto)
    {
        try
        {
            var filter = Builders<Filling>.Filter.And(
                Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId),
                Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, a => a.id == dto.ApplicationId)
            );

            UpdateDefinition<Filling> update;

            if (dto.Type == FormApplicationTypes.Certification)
            {
                update = Builders<Filling>.Update
                    .Set("ApplicationHistory.$.CertificatePaymentId", dto.NewPaymentId);
            }
            else
            {
                update = Builders<Filling>.Update
                    .Set("ApplicationHistory.$.PaymentId", dto.NewPaymentId);
            }

            var result = await _fillingCollection.UpdateOneAsync(filter, update);

            if (result.ModifiedCount > 0)
            {
                var file = await _fillingCollection.Find(f => f.FileId == dto.FileId).FirstOrDefaultAsync();
                if (file != null)
                {
                    await LogFileUpdateAsync(
                        dto.FileId!,
                        file.TitleOfInvention ?? file.TitleOfDesign ?? file.TitleOfTradeMark ?? "(No Title)",
                        file.Type,
                        "Payment ID",
                        dto.User!
                    );
                }

                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            _log.LogError(e, $"Error updating PaymentId for FileId: {dto.FileId}, ApplicationId: {dto.ApplicationId}");
            return false;
        }
    }


    public async Task<FileUpdateDto?> GetAllFileDetails(string fileNumber)
    {
        if (string.IsNullOrWhiteSpace(fileNumber))
            return null;

        try
        {
            var filter = Builders<Filling>.Filter.Or(
                Builders<Filling>.Filter.Eq(f => f.FileId, fileNumber),
                Builders<Filling>.Filter.Eq(f => f.RtmNumber, fileNumber)
            );

            var filling = await _fillingCollection.Find(filter).FirstOrDefaultAsync();
            if (filling == null) return null;

            NormalizeOwnershipHistory(filling);
            NormalizeAssignmentHistory(filling);
            NormalizeRecordalHistory(filling);

            var designs = filling.Attachments?
                     .Where(a => a.name == "designs")
                     .SelectMany(a => a.url)
                     .ToList();

            var dto = new FileUpdateDto
            {
                //Id = filling.Id,
                FileId = filling.FileId,
                FileOrigin = filling.FileOrigin,
                FilingCountry = filling.FilingCountry,
                ApplicationHistory = filling.ApplicationHistory,
                FileStatus = filling.FileStatus,
                Type = filling.Type,
                TitleOfInvention = filling.TitleOfInvention,
                PatentAbstract = filling.PatentAbstract,
                Correspondence = filling.Correspondence,
                applicants = filling.applicants,
                PatentApplicationType = filling.PatentApplicationType,
                PatentType = filling.PatentType,
                Inventors = filling.Inventors,
                PriorityInfo = filling.PriorityInfo,
                FirstPriorityInfo = filling.FirstPriorityInfo,
                DesignType = filling.DesignType,
                TitleOfDesign = filling.TitleOfDesign,
                StatementOfNovelty = filling.StatementOfNovelty,
                DesignCreators = filling.DesignCreators,
                Attachments = filling.Attachments,
                TitleOfTradeMark = filling.TitleOfTradeMark,
                TrademarkClass = filling.TrademarkClass,
                TrademarkClassDescription = filling.TrademarkClassDescription,
                TrademarkLogo = filling.TrademarkLogo,
                TrademarkType = filling.TrademarkType,
                TrademarkDisclaimer = filling.TrademarkDisclaimer,
                TrademarkSpecification = filling.TrademarkSpecification,
                RtmNumber = filling.RtmNumber,
                Comment = filling.Comment,
                DesignAttachments = designs
            };

            return dto;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in Getting File details: {ex.Message}");
            return null;
        }
    }

    public async Task<(int status, string message)> UpdatePatentFiles(UpdatePatentFileDto dto)
    {
        var existing = await _fillingCollection.Find(x => x.FileId == dto.FileId).FirstOrDefaultAsync();
        if (existing == null)
            return (404, "Filing record not found");

        if (!string.IsNullOrEmpty(dto.FileOrigin))
            existing.FileOrigin = dto.FileOrigin;

        if (!string.IsNullOrEmpty(dto.FilingCountry))
            existing.FilingCountry = dto.FilingCountry;

        if (dto.PatentApplicationType != null) existing.PatentApplicationType = dto.PatentApplicationType.Value;

        // Correspondence nationality only
        if (!string.IsNullOrEmpty(dto.CorrespondenceNationality))
            existing.Correspondence.Nationality = dto.CorrespondenceNationality;

        void MergeList<T>(List<T> existingList, List<T> incomingList, Func<T, string> getId, Action<T, T> mergeItem)
        {
            foreach (var item in incomingList)
            {
                var existingItem = existingList.FirstOrDefault(x => getId(x) == getId(item));
                if (existingItem != null)
                {
                    mergeItem(existingItem, item);
                }
                else
                {
                    existingList.Add(item);
                }
            }
        }

        if (dto.Applicants?.Any() == true)
            MergeList(existing.applicants, dto.Applicants, x => x.id, (e, u) =>
            {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.country)) e.country = u.country;
                if (!string.IsNullOrWhiteSpace(u.city)) e.city = u.city;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.State)) e.State = u.State;
            });

        if (dto.Inventors?.Any() == true)
            MergeList(existing.Inventors, dto.Inventors, x => x.id, (e, u) =>
            {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.country)) e.country = u.country;
                if (!string.IsNullOrWhiteSpace(u.city)) e.city = u.city;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.State)) e.State = u.State;
            });

        if (dto.FirstPriorityInfo?.Any() == true)
            MergeList(existing.FirstPriorityInfo, dto.FirstPriorityInfo, x => x.id, (e, u) =>
            {
                if (!string.IsNullOrWhiteSpace(u.Country)) e.Country = u.Country;
                if (!string.IsNullOrWhiteSpace(u.Date)) e.Date = u.Date;
                if (!string.IsNullOrWhiteSpace(u.number)) e.number = u.number;
            });

        await _fillingCollection.ReplaceOneAsync(
              x => x.FileId == dto.FileId, existing
              );

        return (200, "Filing record updated successfully.");
    }

    public async Task<(int StatusCode, string Message, Filling? UpdatedFile)> UpdateFilingAsync(FileUpdateDto request)
    {
        var existing = await _fillingCollection.Find(x => x.FileId == request.FileId).FirstOrDefaultAsync();
        if (existing == null)
            return (404, "Filing record not found", null);

        // Scalar fields
        if (!string.IsNullOrWhiteSpace(request.TitleOfInvention)) existing.TitleOfInvention = request.TitleOfInvention;
        if (!string.IsNullOrWhiteSpace(request.PatentAbstract)) existing.PatentAbstract = request.PatentAbstract;
        if (!string.IsNullOrWhiteSpace(request.TitleOfDesign)) existing.TitleOfDesign = request.TitleOfDesign;
        if (!string.IsNullOrWhiteSpace(request.StatementOfNovelty)) existing.StatementOfNovelty = request.StatementOfNovelty;
        if (!string.IsNullOrWhiteSpace(request.TitleOfTradeMark)) existing.TitleOfTradeMark = request.TitleOfTradeMark;
        if (!string.IsNullOrWhiteSpace(request.TrademarkDisclaimer)) existing.TrademarkDisclaimer = request.TrademarkDisclaimer;
        if (!string.IsNullOrWhiteSpace(request.TrademarkSpecification)) existing.TrademarkSpecification = request.TrademarkSpecification;
        if (!string.IsNullOrWhiteSpace(request.RtmNumber)) existing.RtmNumber = request.RtmNumber;
        if (!string.IsNullOrWhiteSpace(request.Comment)) existing.Comment = request.Comment;
        if (!string.IsNullOrEmpty(request.FilingCountry)) existing.FilingCountry = request.FilingCountry;

        // Nullable types
        if (request.PatentApplicationType != null) existing.PatentApplicationType = request.PatentApplicationType.Value;
        if (request.PatentType != null) existing.PatentType = request.PatentType.Value;
        if (request.DesignType != null) existing.DesignType = request.DesignType.Value;
        if (request.TrademarkClass != null)
        {
            existing.TrademarkClass = request.TrademarkClass.Value;
            // Derive the description server-side from the canonical class map so the two
            // fields can never drift, regardless of what the client sends.
            existing.TrademarkClassDescription = FileUtils.TrademarkClassMapper.GetDescription(request.TrademarkClass.Value);
        }
        if (request.TrademarkLogo != null) existing.TrademarkLogo = request.TrademarkLogo.Value;
        if (request.TrademarkType != null) existing.TrademarkType = request.TrademarkType.Value;
        if (request.FileStatus != null) existing.FileStatus = request.FileStatus.Value;

        // Correspondence merging
        if (request.Correspondence != null)
        {
            existing.Correspondence ??= new CorrespondenceType();

            if (!string.IsNullOrWhiteSpace(request.Correspondence.name))
                existing.Correspondence.name = request.Correspondence.name;

            if (!string.IsNullOrWhiteSpace(request.Correspondence.address))
                existing.Correspondence.address = request.Correspondence.address;

            if (!string.IsNullOrWhiteSpace(request.Correspondence.email))
                existing.Correspondence.email = request.Correspondence.email;

            if (!string.IsNullOrWhiteSpace(request.Correspondence.phone))
                existing.Correspondence.phone = request.Correspondence.phone;

            if (!string.IsNullOrWhiteSpace(request.Correspondence.state))
                existing.Correspondence.state = request.Correspondence.state;
        }

        void MergeList<T>(List<T> existingList, List<T> incomingList, Func<T, string> getId, Action<T, T> mergeItem)
        {
            foreach (var item in incomingList)
            {
                var existingItem = existingList.FirstOrDefault(x => getId(x) == getId(item));
                if (existingItem != null)
                {
                    mergeItem(existingItem, item);
                }
                else
                {
                    existingList.Add(item);
                }
            }
        }

        // === FULL REPLACEMENT for these 4 fields ===
        if (request.FileStatus != null)
            existing.FileStatus = (ApplicationStatuses)request.FileStatus;

        if (request.applicants != null)
            existing.applicants = request.applicants;

        if (request.Inventors != null)
            existing.Inventors = request.Inventors;

        if (request.PriorityInfo != null)
            existing.PriorityInfo = request.PriorityInfo;

        if (request.FirstPriorityInfo != null)
            existing.FirstPriorityInfo = request.FirstPriorityInfo;

        if (request.DesignCreators != null)
            existing.DesignCreators = request.DesignCreators;

        if (request.UpdatedAttachments != null)
        {
            var newAttachments = new List<AttachmentType>();

            // 1. Add existing attachments
            foreach (var att in request.UpdatedAttachments.ExistingAttachments)
            {
                newAttachments.Add(new AttachmentType
                {
                    name = att.name,
                    url = att.url
                });
            }

            // 2. Add new attachments, merging if name exists
            var groupedNewFiles = request.UpdatedAttachments.NewAttachments.GroupBy(f => f.Name);
            foreach (var group in groupedNewFiles)
            {
                var uploadedUrls = await UploadAttachment(group.ToList());
                var existingAttachment = newAttachments.FirstOrDefault(a => a.name == group.Key);
                if (existingAttachment != null)
                {
                    // Append new URLs to existing attachment
                    existingAttachment.url.AddRange(uploadedUrls);
                }
                else
                {
                    // Create new attachment entry
                    newAttachments.Add(new AttachmentType
                    {
                        name = group.Key,
                        url = uploadedUrls
                    });
                }
            }

            existing.Attachments = newAttachments;
        }

        //if (request.UpdatedAttachments != null)
        //{
        //    var newAttachments = new List<AttachmentType>();

        //    // 1. Process existing attachments (URLs)
        //    foreach (var att in request.UpdatedAttachments.ExistingAttachments)
        //    {
        //        newAttachments.Add(new AttachmentType
        //        {
        //            name = att.name,
        //            url = att.url
        //        });
        //    }

        //    // 2. Process new attachments (files to upload)
        //    var groupedNewFiles = request.UpdatedAttachments.NewAttachments.GroupBy(f => f.Name);
        //    foreach (var group in groupedNewFiles)
        //    {
        //        var uploadedUrls = await UploadAttachment(group.ToList());
        //        newAttachments.Add(new AttachmentType
        //        {
        //            name = group.Key,
        //            url = uploadedUrls
        //        });
        //    }

        //    // 3. Replace the attachments list with the new one
        //    existing.Attachments = newAttachments;
        //}

        //if (request.Attachments?.Any() == true)
        //    MergeList(existing.Attachments, request.Attachments, x => x.name, (e, u) => {
        //        if (!string.IsNullOrWhiteSpace(u.name)) e.name = u.name;
        //        if (u.url != null && u.url.Any()) e.url = u.url;
        //    });

        await _fillingCollection.ReplaceOneAsync(
            x => x.FileId == request.FileId, existing
        );

        // Log the update history
        await LogFileUpdateAsync(
            existing.FileId ?? "Unknown FileId",
            existing.TitleOfTradeMark
                ?? existing.TitleOfInvention
                ?? existing.TitleOfDesign
                ?? "Untitled",
            existing.Type,
            "File Info",
            request.UpdatedBy ?? "Unknown User"
        );

        // Reload from DB so the response reflects exactly what was persisted.
        var updated = await _fillingCollection.Find(x => x.FileId == request.FileId).FirstOrDefaultAsync();
        return (200, "Filing record updated successfully.", updated ?? existing);
    }

    public async Task LogFileUpdateAsync(string fileNumber,
        string title,
        FileTypes fileType,
        string updateType,
        string adminName)
    {
        if (string.IsNullOrWhiteSpace(fileNumber) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(adminName))
        {
            Console.WriteLine("Skipping File Update Log: missing fileNumber/title/adminName");
            return;
        }

        var record = new FileUpdateHistory
        {
            Id = Guid.NewGuid().ToString(),
            FileNumber = fileNumber,
            Title = title,
            FileType = fileType,
            UpdateType = updateType,
            AdminName = adminName,
            DateUpdated = DateTime.UtcNow
        };

        await _fileUpdateHistoryCollection.InsertOneAsync(record);
    }

    public async Task<List<FileUpdateHistory>> GetAllFileUpdateHistoryAsync()
    {
        return await _fileUpdateHistoryCollection.Find(_ => true)
                                                 .SortByDescending(x => x.DateUpdated)
                                                 .ToListAsync();
    }

    public async Task<FileTypes?> GetFileTypeByFileIdAsync(string fileId)
    {
        var filing = await _fillingCollection
            .Find(f => f.FileId == fileId)
            .FirstOrDefaultAsync();

        return filing?.Type; // null if not found
    }
    public async Task<dynamic> GetAppealCost(string fileId, string userId)
    {
        var file = await _fillingCollection.Find(f => f.FileId == fileId).FirstOrDefaultAsync()
                   ?? throw new KeyNotFoundException($"File {fileId} not found");

        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync()
                   ?? throw new KeyNotFoundException($"User {userId} not found");

        var userName = $"{user.FirstName} {user.LastName}";

        try
        {
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.Appeal, file.Type, "", null, null, null);
            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Appeal Request",
                userName, user.Email, user.PhoneNumber);

            return new { cost = data.Item1, rrr = paymentId };
        }
        catch (Exception)
        {
            throw;
        }
    }
    public async Task<bool> UploadAppealFiles(AppealDto app)
    {
        var file = await _fillingCollection.Find(f => f.FileId == app.FileNumber).FirstOrDefaultAsync();
        if (file == null) return false;

        var applicant = file.applicants?.FirstOrDefault();
        try
        {
            if (app.Docs == null || app.Docs.Count == 0) return false;

            var appealDocUrls = new List<string>();

            foreach (var (doc, i) in app.Docs.Select((doc, idx) => (doc, idx)))
            {
                using var ms = new MemoryStream();
                await doc.CopyToAsync(ms);

                var appealDoc = ms.ToArray();
                var url = await UploadAttachment(new List<TT>
                {
                    new TT
                    {
                        contentType = doc.ContentType,
                        data = appealDoc,
                        fileName = Path.GetFileName(doc.FileName),
                        Name = $"Appeal Document {i + 1}"
                    }
                });

                appealDocUrls.Add(url[0]);
            }

            var appHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.AppealRequest,
                CurrentStatus = ApplicationStatuses.AppealRequest,
                ApplicationDate = DateTime.Now,
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AppealRequest,
                        Message = "Appeal Request",
                        User = applicant?.Name,
                        UserId = file.CreatorAccount
                    }
                }
            };

            var appeal = new Appeal
            {
                Id = appHistory.id,
                Date = DateTime.Now,
                AppealDocs = appealDocUrls
            };

            var attachments = file.Attachments ?? new List<AttachmentType>();
            for (int i = 0; i < appealDocUrls.Count; i++)
            {
                attachments.Add(new AttachmentType
                {
                    name = $"Appeal Doc {i + 1}",
                    url = new List<string> { appealDocUrls[i] }
                });
            }

            file.FileStatus = ApplicationStatuses.AppealRequest;
            var finalUpdate = Builders<Filling>.Update.Combine(
                Builders<Filling>.Update.Set(f => f.Attachments, attachments),
                Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.AppealRequest),
                Builders<Filling>.Update.Push(f => f.ApplicationHistory, appHistory),
                Builders<Filling>.Update.Push(f => f.Appeals, appeal)
            );

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, file.FileId),
                finalUpdate
            );

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<Appeal> GetAppealRequest(string fileNumber, string appId)
    {
        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == fileNumber).FirstOrDefaultAsync();
            if (file == null) throw new Exception("File not found");
            var appeal = file.Appeals.FirstOrDefault(a => a.Id == appId);
            if (appeal == null) throw new Exception("No Appeal found");
            return appeal;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<bool> TreatAppeal(TreatAppealDto req)
    {
        try
        {
            Console.WriteLine("appeal: " + req);
            var file = await _fillingCollection.Find(f => f.FileId == req.FileNumber).FirstOrDefaultAsync();
            if (file == null || file.FileStatus == ApplicationStatuses.Rejected) throw new Exception("File not found");
            var user = await _userCollection.Find(u => u.Id == req.UserId).FirstOrDefaultAsync();
            if (user == null) throw new UnauthorizedAccessException("Unauthorized User");
            var appeal = file.Appeals?.FirstOrDefault(a => a.Id == req.ApplicationId);
            if (appeal == null) throw new Exception("No Appeal found");
            var history = file.ApplicationHistory?.FirstOrDefault(h => h.id == req.ApplicationId);
            if (history == null) throw new Exception("Application not found in History");
            appeal.Reason = req.Reason;

            if (req.IsApproved)
            {
                history.CurrentStatus = ApplicationStatuses.Approved;
                appeal.DateTreated = DateTime.Now;
                file.FileStatus = ApplicationStatuses.Publication;
            }
            else
            {
                history.CurrentStatus = ApplicationStatuses.Rejected;
                appeal.DateTreated = DateTime.Now;
                file.FileStatus = ApplicationStatuses.Rejected;
            }

            var finalUpdate = Builders<Filling>.Update.Combine(
                Builders<Filling>.Update.Set(f => f.FileStatus, file.FileStatus),
                Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory),
                Builders<Filling>.Update.Set(f => f.Appeals, file.Appeals)
            );
            var result = await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, req.FileNumber),
                finalUpdate
            );
            var perform = new PerformanceDto
            {
                AfterStatus = history.CurrentStatus,
                BeforeStatus = ApplicationStatuses.Rejected,
                ApplicationType = FormApplicationTypes.Assignment,
                AppUserId = req.UserId,
                Date = DateTime.Now,
                FileNumber = file.FileId,
                FileType = file.Type,
                OfficeUnit = Roles.TrademarkAcceptance,
                Reason = req.Reason,
            };

            SavePerformance(perform);
            return result.ModifiedCount > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<FileAttachmentDto?> GetAllPatentAndDesignAttachmentsAsync(string fileId)
    {
        var file = await _fillingCollection
            .Find(f => f.FileId == fileId)
            .FirstOrDefaultAsync();

        if (file == null)
            return null;

        if (file.Type != FileTypes.Patent && file.Type != FileTypes.Design)
            return null;

        return new FileAttachmentDto
        {
            FileId = file.FileId,
            FileType = file.Type,
            PatentType = file.PatentType,
            TitleOfInvention = file.TitleOfInvention,
            FileStatus = file.FileStatus,
            FileOrigin = file.FileOrigin,
            Applicant = file.applicants?.FirstOrDefault(), // ? only first applicant
            TitleOfDesign = file.TitleOfDesign,
            DesignType = file.DesignType,
            StatementOfNovelty = file.StatementOfNovelty,
            Attachments = file.Attachments ?? new List<AttachmentType>()
        };
    }

    public async Task<bool> UpdateAttachmentsAsync(string filingFileId, List<TT> newFiles)
    {
        Console.WriteLine($"?? Looking for FileId in DB: {filingFileId}");
        var filter = Builders<Filling>.Filter.Eq(f => f.FileId, filingFileId);
        var filing = await _fillingCollection.Find(filter).FirstOrDefaultAsync();

        if (filing == null)
        {
            Console.WriteLine("?? No matching filing found!");
            return false;
        }

        Console.WriteLine("? Filing found, proceeding with update...");

        filing.Attachments ??= new List<AttachmentType>();

        // Group incoming files by their "Name" (authorization, cs, etc.)
        var groupedFiles = newFiles.GroupBy(f => f.Name);

        foreach (var group in groupedFiles)
        {
            // Upload all files in this group
            var uploadedUrls = await UploadAttachment(group.ToList());

            // Find existing attachment with same name
            var existing = filing.Attachments.FirstOrDefault(a => a.name == group.Key);

            if (existing != null)
            {
                // Add only new URLs if not already in DB
                foreach (var url in uploadedUrls)
                {
                    if (!existing.url.Contains(url))
                    {
                        existing.url.Add(url);
                    }
                }
            }
            else
            {
                // Create new attachment entry
                filing.Attachments.Add(new AttachmentType
                {
                    name = group.Key,
                    url = uploadedUrls
                });
            }
        }

        // Persist update
        var update = Builders<Filling>.Update.Set(f => f.Attachments, filing.Attachments);
        await _fillingCollection.UpdateOneAsync(filter, update);

        return true;
    }

    public async Task<bool> ApproveAmendmentAsync(AmendmentDto dto)
    {
        // Fetch the file
        var file = await _fillingCollection.Find(f => f.FileId == dto.fileId).FirstOrDefaultAsync();
        if (file == null)
        {
            Console.WriteLine($" File {dto.fileId} not found.");
            return false;
        }
        var user = await _userCollection.Find(u => u.Id == dto.userId).FirstOrDefaultAsync();
        // Find the clerical update flagged as an amendment
        var clerical = file.ClericalUpdates?
            .FirstOrDefault(c => c.Id == dto.appId && c.IsAmendment == true);
        if (clerical == null)
        {
            Console.WriteLine($"Clerical amendment {dto.appId} not found or not marked as amendment.");
            return false;
        }

        var app = file.ApplicationHistory.FirstOrDefault(c => c.id == dto.appId);
        if (app == null)
        {
            Console.WriteLine($" Application history {dto.appId} not found.");
            return false;
        }

        // Update in-memory state for audit
        app.CurrentStatus = ApplicationStatuses.Approved;
        clerical.IsApproved = true;
        clerical.DateTreated = DateTime.Now;
        clerical.Reason = dto.reason;

        Console.WriteLine($"Approving amendment ({clerical.UpdateType}) for file {dto.fileId}");

        // Determine which field-specific updates to apply
        var updates = new List<UpdateDefinition<Filling>>();

        switch (clerical.UpdateType)
        {
            case "ApplicantName":
                if (!string.IsNullOrEmpty(clerical.NewApplicantName))
                    updates.Add(Builders<Filling>.Update.Set("applicants.0.Name", clerical.NewApplicantName));
                break;

            case "ApplicantAddress":
                if (!string.IsNullOrEmpty(clerical.NewApplicantAddress))
                    updates.Add(Builders<Filling>.Update.Set("applicants.0.Address", clerical.NewApplicantAddress));
                if (!string.IsNullOrEmpty(clerical.NewApplicantEmail))
                    updates.Add(Builders<Filling>.Update.Set("applicants.0.Email", clerical.NewApplicantEmail));
                if (!string.IsNullOrEmpty(clerical.NewApplicantPhone))
                    updates.Add(Builders<Filling>.Update.Set("applicants.0.Phone", clerical.NewApplicantPhone));
                if (!string.IsNullOrEmpty(clerical.NewApplicantNationality))
                    updates.Add(Builders<Filling>.Update.Set("applicants.0.country", clerical.NewApplicantNationality));
                break;

            case "FileClass":
                if (!string.IsNullOrEmpty(clerical.NewFileClass))
                    updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkClass, int.Parse(clerical.NewFileClass)));
                if (!string.IsNullOrEmpty(clerical.NewClassDescription))
                    updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkClassDescription, clerical.NewClassDescription));
                if (!string.IsNullOrEmpty(clerical.NewDisclaimer))
                    updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkDisclaimer, clerical.NewDisclaimer));
                break;

            case "Correspondence":
                if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceName))
                    updates.Add(Builders<Filling>.Update.Set(f => f.Correspondence.name, clerical.NewCorrespondenceName));
                if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondencePhone))
                    updates.Add(Builders<Filling>.Update.Set(f => f.Correspondence.phone, clerical.NewCorrespondencePhone));
                if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceAddress))
                    updates.Add(Builders<Filling>.Update.Set(f => f.Correspondence.address, clerical.NewCorrespondenceAddress));
                if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceEmail))
                    updates.Add(Builders<Filling>.Update.Set(f => f.Correspondence.email, clerical.NewCorrespondenceEmail));
                if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceNationality))
                    updates.Add(Builders<Filling>.Update.Set(f => f.Correspondence.Nationality, clerical.NewCorrespondenceNationality));
                if (!string.IsNullOrWhiteSpace(clerical.NewCorrespondenceState))
                    updates.Add(Builders<Filling>.Update.Set(f => f.Correspondence.state, clerical.NewCorrespondenceState));

                if (!string.IsNullOrEmpty(clerical.NewPowerOfAttorneyUrl))
                {
                    var poaIndex = file.Attachments?.FindIndex(a => a.name == "poa") ?? -1;
                    if (poaIndex >= 0)
                    {
                        updates.Add(Builders<Filling>.Update.Set($"Attachments.{poaIndex}.url", new List<string> { clerical.NewPowerOfAttorneyUrl }));
                    }
                    else
                    {
                        updates.Add(Builders<Filling>.Update.Push(f => f.Attachments, new AttachmentType
                        {
                            name = "poa",
                            url = new List<string> { clerical.NewPowerOfAttorneyUrl }
                        }));
                    }
                }

                if (!string.IsNullOrEmpty(clerical.NewAttachmentUrl))
                {
                    updates.Add(Builders<Filling>.Update.Push(f => f.Attachments, new AttachmentType
                    {
                        name = "other",
                        url = new List<string> { clerical.NewAttachmentUrl }
                    }));
                }
                break;

            case "FileTitle":
                if (!string.IsNullOrEmpty(clerical.NewFileTitle))
                    updates.Add(Builders<Filling>.Update.Set(f => f.TitleOfTradeMark, clerical.NewFileTitle));
                if (!string.IsNullOrEmpty(clerical.NewTrademarkLogo))
                    updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkLogo, Enum.Parse<TradeMarkLogo>(clerical.NewTrademarkLogo)));
                if (!string.IsNullOrEmpty(clerical.NewRepresentationUrl))
                {
                    var index = file.Attachments?.FindIndex(a => a.name == "representation") ?? -1;
                    if (index >= 0)
                    {
                        updates.Add(Builders<Filling>.Update.Set($"Attachments.{index}.url", new List<string> { clerical.NewRepresentationUrl }));
                    }
                    else
                    {
                        updates.Add(Builders<Filling>.Update.Push(f => f.Attachments, new AttachmentType
                        {
                            name = "representation",
                            url = new List<string> { clerical.NewRepresentationUrl }
                        }));
                    }
                }
                break;
        }

        // Persist changes: include field updates (if any) AND overwrite the arrays to ensure IsApproved/DateTreated/etc persist.
        var finalUpdates = new List<UpdateDefinition<Filling>>();
        if (updates.Any()) finalUpdates.AddRange(updates);

        // Ensure the approved status and clerical metadata are saved into arrays (we updated them in-memory already)
        finalUpdates.Add(Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory));
        finalUpdates.Add(Builders<Filling>.Update.Set(f => f.ClericalUpdates, file.ClericalUpdates));

        var combinedUpdate = Builders<Filling>.Update.Combine(finalUpdates);

        var filter = Builders<Filling>.Filter.Eq(f => f.FileId, dto.fileId);

        var result = await _fillingCollection.UpdateOneAsync(filter, combinedUpdate);

        Console.WriteLine($"Amendment ({clerical.UpdateType}) approved and applied for {dto.fileId}. ModifiedCount: {result.ModifiedCount}");

        var performance = new PerformanceDto
        {
            AppUserId = dto.userId,
            AfterStatus = ApplicationStatuses.Approved,
            BeforeStatus = ApplicationStatuses.AwaitingApproval,
            ApplicationType = FormApplicationTypes.Amendment,
            FileNumber = dto.fileId,
            FileType = file.Type,
            Reason = dto.reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.TrademarkAcceptance
        };
        SavePerformance(performance);

        return result.ModifiedCount > 0;
    }

    #region Patent Assignment Registration Section
    public async Task<bool> NewPatentAssignmentApplication(PatentAssignmentDto dto)
    {
        _log.LogInformation("Starting patent assignment application for FileId {FileId}, Rrr {Rrr}", dto.FileId, dto.Rrr);
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Patent assignment application failed. File not found for FileId {FileId}", dto.FileId);
            return false;
        }

        var applicant = file.applicants.FirstOrDefault();

        // Upload Assignment Deed
        if (dto.AssignmentDeed != null && dto.AssignmentDeed.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.AssignmentDeed);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "Patent post registration deed of assignments");
            if (existingDeed != null)
            {
                // Add only new URLs if not already present
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "Patent post registration deed of assignments",
                    url = deedLinks
                });
            }
        }

        // Upload Supporting Documents
        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "Patent post registration assignment supporting documents");
            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "Patent post registration assignment supporting documents",
                    url = supportingDocsUrl
                });
            }
        }

        // Verify payment (single check here)
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "Assignment application submitted, awaiting payment";

        // Application history
        var assignmentHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.Assignment,
            CurrentStatus = status,
            ApplicationDate = dto.AssignmentDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Patent Assignment Application",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.AssignmentRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = applicant?.Name,
                    UserId = file.CreatorAccount
                }
            }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = assignmentHistory.id,
            RecordalType = "Patent Assignment Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.AssignmentDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.AssignmentRequestDate ?? DateTime.Now).ToString(),
            // Old assignor (previous patent holder)
            OldAssignorName = dto.OldAssignorName,
            OldAssignorEmail = dto.OldAssignorEmail,
            OldAssignorPhone = dto.OldAssignorPhone,
            OldAssignorAddress = dto.OldAssignorAddress,
            OldAssignorNationality = dto.OldAssignorNationality,
            OldAssignorState = dto.OldAssignorState,
            OldAssignorCity = dto.OldAssignorCity,

            // New assignee (now the applicant)
            Name = dto.NewAssigneeName,
            Email = dto.NewAssigneeEmail,
            Phone = dto.NewAssigneePhone,
            Address = dto.NewAssigneeAddress,
            Nationality = dto.NewAssigneeNationality,
            State = dto.NewAssigneeState,
            City = dto.NewAssigneeCity,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
                .Push(f => f.PostRegApplications, recordal)
                .Push(f => f.ApplicationHistory, assignmentHistory)
                .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.PatentAssignment, file.FileId, assignmentHistory.id);
        }

        _log.LogInformation("Completed patent assignment application for FileId {FileId}, PaymentSuccessful {PaymentSuccessful}", dto.FileId, paymentSuccessful);
        return true;
    }

    public async Task<object?> GetPatentAssignmentDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch assignment deed attachments
        var assignmentDeedAttachments = file.Attachments?
            .Where(a => a.name == "Patent post registration deed of assignments")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch supporting document attachments
        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "Patent post registration assignment supporting documents")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch the PostRegApp for assignment (should be only one per your requirements)
        var assignmentApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Patent Assignment Recordal");

        // New assignee details
        var newAssignee = assignmentApp == null ? null : new
        {
            Name = assignmentApp.Name,
            Address = assignmentApp.Address,
            Email = assignmentApp.Email,
            Phone = assignmentApp.Phone,
            State = assignmentApp.State,
            Nationality = assignmentApp.Nationality,
            City = assignmentApp.City
        };

        // Old assignor details
        var oldAssignor = assignmentApp == null ? null : new
        {
            Name = assignmentApp.OldAssignorName,
            Address = assignmentApp.OldAssignorAddress,
            Email = assignmentApp.OldAssignorEmail,
            Phone = assignmentApp.OldAssignorPhone,
            State = assignmentApp.OldAssignorState,
            Nationality = assignmentApp.OldAssignorNationality,
            City = assignmentApp.OldAssignorCity,
        };

        var filingDate = assignmentApp.FilingDate;

        return new
        {
            FileId = file.FileId,
            AssignmentDeedAttachments = assignmentDeedAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments,
            NewAssignee = newAssignee,
            OldAssignor = oldAssignor,
            Filingdate = filingDate
        };
    }

    public async Task<(bool Success, string Message)> PatentAssignmentDecisionAsync(string fileId, string appId, bool approve, string reason, ApplicantInfo newAssignee = null, string? appUserId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        // Find the ApplicationInfo for Assignment
        var assignmentApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Assignment);

        if (assignmentApp == null)
            return (false, "No assignment application found");

        // Prepare new status history entry
        var beforeStatus = assignmentApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = assignmentApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = file.applicants.FirstOrDefault()?.Name,
            UserId = file.CreatorAccount
        };

        assignmentApp.StatusHistory.Add(newStatus);
        assignmentApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        // Update applicant info if approved
        if (approve && newAssignee != null)
        {
            file.applicants = new List<ApplicantInfo> { newAssignee };
        }

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = string.IsNullOrWhiteSpace(appUserId) ? file.CreatorAccount : appUserId,
            AfterStatus = assignmentApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Assignment,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.PatentExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Assignment approved" : "Assignment refused");
    }

    #endregion

    #region Patent License Post Registration Section
    public async Task<bool> NewPatentLicenseApplication(PatentLicenseDto dto)
    {
        _log.LogInformation("Starting patent license application for FileId {FileId}, Rrr {Rrr}", dto.FileId, dto.Rrr);
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Patent license application failed. File not found for FileId {FileId}", dto.FileId);
            return false;
        }

        var applicant = file.applicants.FirstOrDefault();

        // Upload Deed of License
        if (dto.Deedoflicense != null && dto.Deedoflicense.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.Deedoflicense);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "Deedoflicense");
            if (existingDeed != null)
            {
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "Deedoflicense",
                    url = deedLinks
                });
            }
        }

        // Upload Supporting Documents
        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "PatentLicenseSupportingDocuments");
            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "PatentLicenseSupportingDocuments",
                    url = supportingDocsUrl
                });
            }
        }


        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "License application submitted, awaiting payment";

        // Application history
        var licenseHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.License,
            CurrentStatus = status,
            ApplicationDate = dto.LicenseDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Patent License Application",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
        {
            new ApplicationHistory
            {
                Date = dto.LicenseRequestDate ?? DateTime.Now,
                beforeStatus = ApplicationStatuses.AwaitingPayment,
                afterStatus = status,
                Message = statusMessage,
                User = applicant?.Name,
                UserId = file.CreatorAccount
            }
        }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = licenseHistory.id,
            RecordalType = "Patent License Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.LicenseDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.LicenseRequestDate ?? DateTime.Now).ToString(),
            // Old licensor (previous patent holder)
            OldLicensorName = dto.OldLicensorName,
            OldLicensorEmail = dto.OldLicensorEmail,
            OldLicensorPhone = dto.OldLicensorPhone,
            OldLicensorAddress = dto.OldLicensorAddress,
            OldLicensorNationality = dto.OldLicensorNationality,
            OldLicensorState = dto.OldLicensorState,
            OldLicensorCity = dto.OldLicensorCity,

            // New licensee (now the applicant)
            Name = dto.NewLicenseeName,
            Email = dto.NewLicenseeEmail,
            Phone = dto.NewLicenseePhone,
            Address = dto.NewLicenseeAddress,
            Nationality = dto.NewLicenseeNationality,
            State = dto.NewLicenseeState,
            City = dto.NewLicenseeCity,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
        .Push(f => f.PostRegApplications, recordal)
        .Push(f => f.ApplicationHistory, licenseHistory)
        .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.PatentLicense, file.FileId, licenseHistory.id);
        }

        _log.LogInformation("Completed patent license application for FileId {FileId}, PaymentSuccessful {PaymentSuccessful}", dto.FileId, paymentSuccessful);
        return true;
    }

    public async Task<object?> GetPatentLicenseDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch deed of license attachments
        var deedOfLicenseAttachments = file.Attachments?
            .Where(a => a.name == "Deedoflicense")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch supporting document attachments
        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "PatentLicenseSupportingDocuments")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch the PostRegApp for license
        var licenseApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Patent License Recordal");

        // New licensee details
        var newLicensee = licenseApp == null ? null : new
        {
            Name = licenseApp.Name,
            Address = licenseApp.Address,
            Email = licenseApp.Email,
            Phone = licenseApp.Phone,
            State = licenseApp.State,
            Nationality = licenseApp.Nationality,
            City = licenseApp.City,
        };

        // Old licensor details
        var oldLicensor = licenseApp == null ? null : new
        {
            Name = licenseApp.OldLicensorName,
            Address = licenseApp.OldLicensorAddress,
            Email = licenseApp.OldLicensorEmail,
            Phone = licenseApp.OldLicensorPhone,
            State = licenseApp.OldLicensorState,
            Nationality = licenseApp.OldLicensorNationality,
            City = licenseApp.OldLicensorCity
        };

        var filingDate = licenseApp.FilingDate;


        return new
        {
            FileId = file.FileId,
            DeedOfLicenseAttachments = deedOfLicenseAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments,
            NewLicensee = newLicensee,
            OldLicensor = oldLicensor,
            Filingdate = filingDate
        };
    }

    public async Task<(bool Success, string Message)> PatentLicenseDecisionAsync(string fileId, string appId, bool approve, string reason, ApplicantInfo newLicensee = null, string? appUserId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        // Find the ApplicationInfo for License
        var licenseApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.License);

        if (licenseApp == null)
            return (false, "No license application found");

        // Prepare new status history entry
        var beforeStatus = licenseApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = licenseApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = file.applicants.FirstOrDefault()?.Name,
            UserId = file.CreatorAccount
        };

        licenseApp.StatusHistory.Add(newStatus);
        licenseApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        // If approved, update applicant info with new licensee details
        if (approve && newLicensee != null)
        {
            file.applicants = new List<ApplicantInfo> { newLicensee };
        }

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = string.IsNullOrWhiteSpace(appUserId) ? file.CreatorAccount : appUserId,
            AfterStatus = licenseApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.License,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.PatentExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "License approved" : "License refused");
    }

    #endregion

    #region Patent Mortgage Post Registration Section
    public async Task<bool> NewPatentMortgageApplication(PatentMortgageDto dto)
    {
        _log.LogInformation("Starting patent mortgage application for FileId {FileId}, Rrr {Rrr}", dto.FileId, dto.Rrr);
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Patent mortgage application failed. File not found for FileId {FileId}", dto.FileId);
            return false;
        }

        var applicant = file.applicants.FirstOrDefault();

        // Upload Deed of Mortgage
        if (dto.Deedofmortgage != null && dto.Deedofmortgage.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.Deedofmortgage);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "Deedofmortgage");
            if (existingDeed != null)
            {
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "Deedofmortgage",
                    url = deedLinks
                });
            }
        }

        // Upload Supporting Documents
        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "PatentMortgageSupportingDocuments");
            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "PatentMortgageSupportingDocuments",
                    url = supportingDocsUrl
                });
            }
        }

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "Mortgage application submitted, awaiting payment";

        // Application history
        var mortgageHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.Mortgage,
            CurrentStatus = status,
            ApplicationDate = dto.MortgageDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Patent Mortgage Application",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
        {
            new ApplicationHistory
            {
                Date = dto.MortgageRequestDate ?? DateTime.Now,
                beforeStatus = ApplicationStatuses.AwaitingPayment,
                afterStatus = status,
                Message = statusMessage,
                User = applicant?.Name,
                UserId = file.CreatorAccount
            }
        }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = mortgageHistory.id,
            RecordalType = "Patent Mortgage Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.MortgageDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.MortgageRequestDate ?? DateTime.Now).ToString(),
            // Old mortgagor (previous patent holder)
            OldMortgagorName = dto.OldMortgageeName,
            OldMortgagorEmail = dto.OldMortgageeEmail,
            OldMortgagorPhone = dto.OldMortgageePhone,
            OldMortgagorAddress = dto.OldMortgageeAddress,
            OldMortgagorNationality = dto.OldMortgageeNationality,
            OldMortgagorState = dto.OldMortgageeState,
            OldMortgagorCity = dto.OldMortgageeCity,
            // New mortgagee (now the applicant)
            Name = dto.NewMortgagorName,
            Email = dto.NewMortgagorEmail,
            Phone = dto.NewMortgagorPhone,
            Address = dto.NewMortgagorAddress,
            Nationality = dto.NewMortgagorNationality,
            State = dto.NewMortgagorState,
            City = dto.NewMortgagorCity,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
         .Push(f => f.PostRegApplications, recordal)
         .Push(f => f.ApplicationHistory, mortgageHistory)
         .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.PatentMortgage, file.FileId, mortgageHistory.id);
        }

        _log.LogInformation("Completed patent mortgage application for FileId {FileId}, PaymentSuccessful {PaymentSuccessful}", dto.FileId, paymentSuccessful);
        return true;
    }

    public async Task<object?> GetPatentMortgageDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch deed of mortgage attachments
        var deedOfMortgageAttachments = file.Attachments?
            .Where(a => a.name == "Deedofmortgage")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch supporting document attachments
        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "PatentMortgageSupportingDocuments")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch the PostRegApp for mortgage
        var mortgageApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Patent Mortgage Recordal");

        // New mortgagee details
        var newMortgagee = mortgageApp == null ? null : new
        {
            Name = mortgageApp.Name,
            Address = mortgageApp.Address,
            Email = mortgageApp.Email,
            Phone = mortgageApp.Phone,
            State = mortgageApp.State,
            Nationality = mortgageApp.Nationality,
            City = mortgageApp.City,
        };

        // Old mortgagor details
        var oldMortgagor = mortgageApp == null ? null : new
        {
            Name = mortgageApp.OldMortgagorName,
            Address = mortgageApp.OldMortgagorAddress,
            Email = mortgageApp.OldMortgagorEmail,
            Phone = mortgageApp.OldMortgagorPhone,
            State = mortgageApp.OldMortgagorState,
            Nationality = mortgageApp.OldMortgagorNationality,
            City = mortgageApp.OldMortgagorCity
        };

        var filingDate = mortgageApp.FilingDate;

        return new
        {
            FileId = file.FileId,
            DeedOfMortgageAttachments = deedOfMortgageAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments,
            NewMortgagee = newMortgagee,
            OldMortgagor = oldMortgagor,
            Filingdate = filingDate
        };
    }

    public async Task<(bool Success, string Message)> PatentMortgageDecisionAsync(string fileId, string appId, bool approve, string reason, ApplicantInfo newMortgagee = null, string? appUserId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        // Find the ApplicationInfo for Mortgage
        var mortgageApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Mortgage);

        if (mortgageApp == null)
            return (false, "No mortgage application found");

        // Prepare new status history entry
        var beforeStatus = mortgageApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = mortgageApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = file.applicants.FirstOrDefault()?.Name,
            UserId = file.CreatorAccount
        };

        mortgageApp.StatusHistory.Add(newStatus);
        mortgageApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        // If approved, update applicant info with new mortgagee details
        if (approve && newMortgagee != null)
        {
            file.applicants = new List<ApplicantInfo> { newMortgagee };
        }

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = string.IsNullOrWhiteSpace(appUserId) ? file.CreatorAccount : appUserId,
            AfterStatus = mortgageApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Mortgage,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.PatentExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Mortgage approved" : "Mortgage refused");
    }

    #endregion

    #region Patent Merger Post Registration Section
    public async Task<bool> NewPatentMergerApplication(PatentMergerDto dto)
    {
        _log.LogInformation("Starting patent merger application for FileId {FileId}, Rrr {Rrr}", dto.FileId, dto.Rrr);
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Patent merger application failed. File not found for FileId {FileId}", dto.FileId);
            return false;
        }

        var applicant = file.applicants.FirstOrDefault();

        // Upload Deed of Merger
        if (dto.Deedofmerger != null && dto.Deedofmerger.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.Deedofmerger);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "Deedofmerger");
            if (existingDeed != null)
            {
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "Deedofmerger",
                    url = deedLinks
                });
            }
        }

        // Upload Supporting Documents
        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "PatentMergerSupportingDocuments");
            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "PatentMergerSupportingDocuments",
                    url = supportingDocsUrl
                });
            }
        }

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "Merger application submitted, awaiting payment";

        // Application history
        var mergerHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.Merger,
            CurrentStatus = status,
            ApplicationDate = dto.MergerDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Patent Merger Application",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.MergerRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = applicant?.Name,
                    UserId = file.CreatorAccount
                }
            }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = mergerHistory.id,
            RecordalType = "Patent Merger Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.MergerDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.MergerRequestDate ?? DateTime.Now).ToString(),
            // Old merger party (previous patent holder)
            OldMergerName = dto.OldMergerName,
            OldMergerEmail = dto.OldMergerEmail,
            OldMergerPhone = dto.OldMergerPhone,
            OldMergerAddress = dto.OldMergerAddress,
            OldMergerNationality = dto.OldMergerNationality,
            OldMergerState = dto.OldMergerState,
            OldMergerCity = dto.OldMergerCity,
            // New merged party (now the applicant)
            Name = dto.NewMergerName,
            Email = dto.NewMergerEmail,
            Phone = dto.NewMergerPhone,
            Address = dto.NewMergerAddress,
            Nationality = dto.NewMergerNationality,
            State = dto.NewMergerState,
            City = dto.NewMergerCity,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, mergerHistory)
            .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.PatentMerger, file.FileId, mergerHistory.id);
        }

        _log.LogInformation("Completed patent merger application for FileId {FileId}, PaymentSuccessful {PaymentSuccessful}", dto.FileId, paymentSuccessful);
        return true;
    }

    public async Task<object?> GetPatentMergerDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch deed of merger attachments
        var deedOfMergerAttachments = file.Attachments?
            .Where(a => a.name == "Deedofmerger")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch supporting document attachments
        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "PatentMergerSupportingDocuments")
            .Select(a => new { a.name, a.url })
            .ToList();

        // Fetch the PostRegApp for merger
        var mergerApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Patent Merger Recordal");

        // New merged party details
        var newMergedParty = mergerApp == null ? null : new
        {
            Name = mergerApp.Name,
            Address = mergerApp.Address,
            Email = mergerApp.Email,
            Phone = mergerApp.Phone,
            State = mergerApp.State,
            Nationality = mergerApp.Nationality,
            City = mergerApp.City,
        };

        // Old merger party details
        var oldMergerParty = mergerApp == null ? null : new
        {
            Name = mergerApp.OldMergerName,
            Address = mergerApp.OldMergerAddress,
            Email = mergerApp.OldMergerEmail,
            Phone = mergerApp.OldMergerPhone,
            State = mergerApp.OldMergerState,
            Nationality = mergerApp.OldMergerNationality,
            City = mergerApp.OldMergerCity
        };

        var filingDate = mergerApp.FilingDate;

        return new
        {
            FileId = file.FileId,
            DeedOfMergerAttachments = deedOfMergerAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments,
            NewMergedParty = newMergedParty,
            OldMergerParty = oldMergerParty,
            filingDate = filingDate

        };
    }

    public async Task<(bool Success, string Message)> PatentMergerDecisionAsync(string fileId, string appId, bool approve, string reason, ApplicantInfo newMergedParty = null, string? appUserId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");
        // Find the ApplicationInfo for Merger
        var mergerApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Merger);
        if (mergerApp == null)
            return (false, "No merger application found");
        // Prepare new status history entry
        var beforeStatus = mergerApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = mergerApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = file.applicants.FirstOrDefault()?.Name,
            UserId = file.CreatorAccount
        };
        mergerApp.StatusHistory.Add(newStatus);
        mergerApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;
        // If approved, update applicant info with new merged party details
        if (approve && newMergedParty != null)
        {
            file.applicants = new List<ApplicantInfo> { newMergedParty };
        }
        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = string.IsNullOrWhiteSpace(appUserId) ? file.CreatorAccount : appUserId,
            AfterStatus = mergerApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Merger,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.PatentExaminer
        };
        SavePerformance(performance);
        return (true, approve ? "Merger approved" : "Merger refused");
    }

    #endregion

    #region Patent Ctc Post Registration Section

    public async Task<bool> NewPatentCtcApplication(PatentCtcDto dto)
    {
        _log.LogInformation("Starting patent CTC application for FileId {FileId}, Rrr {Rrr}", dto.FileId, dto.Rrr);
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Patent CTC application failed. File not found for FileId {FileId}", dto.FileId);
            return false;
        }

        var applicant = file.applicants.FirstOrDefault();

        // Validate that requested attachments exist
        var requestedAttachments = new List<string>();
        foreach (var attachmentId in dto.AttachmentIds)
        {
            var attachment = file.Attachments?.FirstOrDefault(a => a.name == attachmentId);
            if (attachment != null)
            {
                requestedAttachments.Add(attachment.name);
            }
        }

        if (requestedAttachments.Count == 0)
        {
            throw new Exception("None of the requested attachments were found in the file.");
        }

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting CTC processing"
            : "CTC application submitted, awaiting payment";

        // Application history
        var ctcHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.CertifiedTrueCopy,
            CurrentStatus = status,
            ApplicationDate = dto.CtcRequestDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Patent Certified True Copy Application",
            NewValue = string.Join(", ", requestedAttachments), // Store requested attachment IDs
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.CtcRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = applicant?.Name,
                    UserId = file.CreatorAccount
                }
            }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = ctcHistory.id,
            RecordalType = "Patent Certified True Copy",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.CtcRequestDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.CtcRequestDate ?? DateTime.Now).ToString(),
            message = $"Certified copies requested for: {string.Join(", ", dto.AttachmentIds)}",
            RequestedAttachments = requestedAttachments,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, ctcHistory);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.PatentCtc, file.FileId, ctcHistory.id);
        }

        _log.LogInformation("Completed patent CTC application for FileId {FileId}, PaymentSuccessful {PaymentSuccessful}", dto.FileId, paymentSuccessful);
        return true;
    }

    public async Task<object?> GetPatentCtcDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch the PostRegApp for CTC
        var ctcApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Patent Certified True Copy");

        if (ctcApp == null)
            return null;

        // ✅ Get the saved attachment names from PostRegistrationApp
        var requestedAttachmentNames = ctcApp.RequestedAttachments ?? new List<string>();

        var requestedAttachments = (file.Attachments ?? new List<AttachmentType>())
        .Where(a => requestedAttachmentNames.Any(reqName =>
            string.Equals(reqName?.Trim(), a.name?.Trim(), StringComparison.OrdinalIgnoreCase)))
        .Select(a => new
        {
            Name = a.name,
            Urls = a.url,
            Count = a.url?.Count ?? 0
        })
        .ToList();

        return new
        {
            FileId = file.FileId,
            RequestedAttachments = requestedAttachments,
            FilingDate = ctcApp.FilingDate,

        };
    }

    public async Task<(bool Success, string Message)> PatentCtcDecisionAsync(string fileId, string appId, bool approve, string reason, string? appUserId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        // Find the ApplicationInfo for CTC
        var ctcApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.FieldToChange == "Patent Certified True Copy Application");

        if (ctcApp == null)
            return (false, "No CTC application found");

        // Prepare new status history entry
        var beforeStatus = ctcApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = ctcApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = file.applicants.FirstOrDefault()?.Name,
            UserId = file.CreatorAccount
        };

        ctcApp.StatusHistory.Add(newStatus);
        ctcApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        // Update the PostRegApp as well
        var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == appId);
        if (recordal != null)
        {
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = reason;
        }

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = string.IsNullOrWhiteSpace(appUserId) ? file.CreatorAccount : appUserId,
            AfterStatus = ctcApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.CertifiedTrueCopy,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.PatentExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "CTC request approved - certified copies ready" : "CTC request refused");
    }

    #endregion

    #region Patent Amendment Post Registration Section
    private PostRegistrationApp CreateAmendmentRecord(Filling file, PatentAmendmentDto dto, string appHistoryId)
    {
        var recordal = new PostRegistrationApp
        {
            Id = appHistoryId,
            RecordalType = "Patent Amendment",
            FileNumber = dto.FileId,
            rrr = dto.PaymentRRR,
            dateOfRecordal = (dto.AmendmentRequestDate ?? DateTime.Now).ToString(),
            FilingDate = DateTime.Now.ToString(),
            AmendmentType = dto.UpdateType.ToString(),
            IsAmendment = true,
            IsApproved = false,
            DateTreated = ""
        };

        switch (dto.UpdateType)
        {
            case PatentAmendmentTypes.ApplicantName:
                var oldNames = file.applicants?.Select(a => a.Name).ToList() ?? new List<string>();
                var newNames = dto.ApplicantNames ?? new List<string>();

                recordal.OldDataJson = JsonSerializer.Serialize(oldNames);
                recordal.NewDataJson = JsonSerializer.Serialize(newNames);
                recordal.message = $"Updating {newNames.Count} applicant names";
                break;

            case PatentAmendmentTypes.ApplicantAddress:
                var oldAddresses = file.applicants?.Select(a => a.Address).ToList() ?? new List<string>();
                var oldEmails = file.applicants?.Select(a => a.Email).ToList() ?? new List<string>();
                var oldPhones = file.applicants?.Select(a => a.Phone).ToList() ?? new List<string>();
                var oldNationalities = file.applicants?.Select(a => a.country).ToList() ?? new List<string>();

                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    Addresses = oldAddresses,
                    Emails = oldEmails,
                    Phones = oldPhones,
                    Nationalities = oldNationalities,
                    States = file.applicants?.Select(a => a.State).ToList() ?? new List<string>(),
                    Cities = file.applicants?.Select(a => a.city).ToList() ?? new List<string>()
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    Addresses = dto.ApplicantAddresses ?? new List<string>(),
                    Emails = dto.ApplicantEmails ?? new List<string>(),
                    Phones = dto.ApplicantPhones ?? new List<string>(),
                    Nationalities = dto.ApplicantNationalities ?? new List<string>(),
                    States = dto.ApplicantStates ?? new List<string>(),
                    Cities = dto.ApplicantCities ?? new List<string>()
                });
                recordal.message = "Updating applicant address information";
                break;

            case PatentAmendmentTypes.FileTitle:
                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    Title = file.TitleOfInvention,
                    Abstract = file.PatentAbstract,
                    ApplicationType = file.PatentApplicationType?.ToString()
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    Title = dto.FileTitle,
                    Abstract = dto.PatentAbstract,
                    ApplicationType = dto.PatentApplicationType
                });
                recordal.message = "Updating patent title and abstract";
                break;

            case PatentAmendmentTypes.CorrespondenceInformation:
                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    Name = file.Correspondence?.name,
                    Address = file.Correspondence?.address,
                    Email = file.Correspondence?.email,
                    Phone = file.Correspondence?.phone,
                    State = file.Correspondence?.state,
                    Nationality = file.Correspondence?.Nationality
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    Name = dto.CorrespondenceName,
                    Address = dto.CorrespondenceAddress,
                    Email = dto.CorrespondenceEmail,
                    Phone = dto.CorrespondencePhone,
                    State = dto.CorrespondenceState,
                    Nationality = dto.CorrespondenceNationality
                });
                recordal.message = "Updating correspondence information";
                break;

            case PatentAmendmentTypes.EditInventors:
                recordal.OldDataJson = JsonSerializer.Serialize(file.Inventors ?? new List<ApplicantInfo>());
                recordal.NewDataJson = JsonSerializer.Serialize(dto.NewInventors ?? new List<ApplicantInfo>());
                recordal.message = "Updating inventor information";
                break;

            case PatentAmendmentTypes.PriorityInfo:
                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    FirstPriorityInfo = file.FirstPriorityInfo ?? new List<PriorityInfo>(),
                    PriorityInfo = file.PriorityInfo ?? new List<PriorityInfo>()
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    FirstPriorityInfo = dto.FirstPriorityInfo ?? new List<PriorityInfo>(),
                    PriorityInfo = dto.PriorityInfo ?? new List<PriorityInfo>()
                });
                recordal.message = "Updating priority information";
                break;

            case PatentAmendmentTypes.AddAndRemoveApplicant:
                recordal.OldDataJson = JsonSerializer.Serialize(file.applicants ?? new List<ApplicantInfo>());
                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    EditedApplicants = dto.EditedApplicants ?? new List<ApplicantInfo>(),
                    NewApplicants = dto.NewApplicants ?? new List<ApplicantInfo>(),
                    RemoveIds = dto.RemoveApplicantIds ?? new List<string>()
                });
                recordal.message = "Adding/Removing applicants";
                break;
        }

        return recordal;
    }

    private void ApplyAmendmentChanges(Filling file, PostRegistrationApp amendment)
    {
        switch (amendment.AmendmentType)
        {
            case "ApplicantName":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newNames = JsonSerializer.Deserialize<List<string>>(amendment.NewDataJson);
                    int updateCount = Math.Min(file.applicants.Count, newNames.Count);
                    for (int i = 0; i < updateCount; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(newNames[i]))
                        {
                            Console.WriteLine($"Updating applicant {i}: '{file.applicants[i].Name}' → '{newNames[i]}'");
                            file.applicants[i].Name = newNames[i];
                        }
                    }
                }
                break;

            case "ApplicantAddress":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);
                    var addresses = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Addresses").GetRawText());
                    var emails = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Emails").GetRawText());
                    var phones = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Phones").GetRawText());
                    var nationalities = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Nationalities").GetRawText());
                    var states = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("States").GetRawText());
                    var cities = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Cities").GetRawText());

                    int updateCount = Math.Min(file.applicants.Count, addresses.Count);
                    for (int i = 0; i < updateCount; i++)
                    {
                        if (i < addresses.Count && !string.IsNullOrWhiteSpace(addresses[i]))
                            file.applicants[i].Address = addresses[i];
                        if (i < emails.Count && !string.IsNullOrWhiteSpace(emails[i]))
                            file.applicants[i].Email = emails[i];
                        if (i < phones.Count && !string.IsNullOrWhiteSpace(phones[i]))
                            file.applicants[i].Phone = phones[i];
                        if (i < nationalities.Count && !string.IsNullOrWhiteSpace(nationalities[i]))
                            file.applicants[i].country = nationalities[i];
                        if (i < states.Count && !string.IsNullOrWhiteSpace(states[i]))
                            file.applicants[i].State = states[i];
                        if (i < cities.Count && !string.IsNullOrWhiteSpace(cities[i]))
                            file.applicants[i].city = cities[i];
                    }
                }
                break;

            case "FileTitle":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                    var titleProp = newData.GetProperty("Title");
                    if (titleProp.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(titleProp.GetString()))
                        file.TitleOfInvention = titleProp.GetString();

                    var abstractProp = newData.GetProperty("Abstract");
                    if (abstractProp.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(abstractProp.GetString()))
                        file.PatentAbstract = abstractProp.GetString();

                    var appTypeProp = newData.GetProperty("ApplicationType");
                    if (appTypeProp.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(appTypeProp.GetString()))
                    {
                        if (Enum.TryParse<PatentApplicationTypes>(appTypeProp.GetString(), out PatentApplicationTypes appType))
                            file.PatentApplicationType = appType;
                    }
                }
                break;

            case "CorrespondenceInformation":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    file.Correspondence ??= new CorrespondenceType();
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                    var nameProp = newData.GetProperty("Name");
                    if (nameProp.ValueKind != JsonValueKind.Null)
                        file.Correspondence.name = nameProp.GetString();

                    var addressProp = newData.GetProperty("Address");
                    if (addressProp.ValueKind != JsonValueKind.Null)
                        file.Correspondence.address = addressProp.GetString();

                    var emailProp = newData.GetProperty("Email");
                    if (emailProp.ValueKind != JsonValueKind.Null)
                        file.Correspondence.email = emailProp.GetString();

                    var phoneProp = newData.GetProperty("Phone");
                    if (phoneProp.ValueKind != JsonValueKind.Null)
                        file.Correspondence.phone = phoneProp.GetString();

                    var stateProp = newData.GetProperty("State");
                    if (stateProp.ValueKind != JsonValueKind.Null)
                        file.Correspondence.state = stateProp.GetString();

                    var nationalityProp = newData.GetProperty("Nationality");
                    if (nationalityProp.ValueKind != JsonValueKind.Null)
                        file.Correspondence.Nationality = nationalityProp.GetString();
                }
                break;

            case "EditInventors":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newInventors = JsonSerializer.Deserialize<List<ApplicantInfo>>(amendment.NewDataJson);
                    file.Inventors = newInventors ?? new List<ApplicantInfo>();
                }
                break;

            case "PriorityInfo":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                    var firstPriorityProp = newData.GetProperty("FirstPriorityInfo");
                    if (firstPriorityProp.ValueKind != JsonValueKind.Null)
                    {
                        var firstPriorityInfo = JsonSerializer.Deserialize<List<PriorityInfo>>(firstPriorityProp.GetRawText());
                        file.FirstPriorityInfo = firstPriorityInfo ?? new List<PriorityInfo>();
                    }

                    var priorityProp = newData.GetProperty("PriorityInfo");
                    if (priorityProp.ValueKind != JsonValueKind.Null)
                    {
                        var priorityInfo = JsonSerializer.Deserialize<List<PriorityInfo>>(priorityProp.GetRawText());
                        file.PriorityInfo = priorityInfo ?? new List<PriorityInfo>();
                    }
                }
                break;

            case "AddAndRemoveApplicant":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                    var editedApplicants = JsonSerializer.Deserialize<List<ApplicantInfo>>(
                        newData.GetProperty("EditedApplicants").GetRawText());
                    var newApplicants = JsonSerializer.Deserialize<List<ApplicantInfo>>(
                        newData.GetProperty("NewApplicants").GetRawText());

                    // Start with edited applicants and add new ones
                    var finalApplicants = new List<ApplicantInfo>();

                    if (editedApplicants != null)
                        finalApplicants.AddRange(editedApplicants);

                    if (newApplicants != null)
                        finalApplicants.AddRange(newApplicants);

                    file.applicants = finalApplicants;
                }
                break;
        }
    }

    public async Task<bool> NewPatentAmendmentApplication(PatentAmendmentDto dto)
    {
        _log.LogInformation("Starting patent amendment application for FileId {FileId}, Rrr {Rrr}", dto.FileId, dto.Rrr);
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning("Patent amendment application failed. File not found for FileId {FileId}", dto.FileId);
            return false;
        }

        var applicant = file.applicants.FirstOrDefault();

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting amendment approval"
            : "Amendment application submitted, awaiting payment";

        var appHistoryId = Guid.NewGuid().ToString();

        // Application history
        var amendmentHistory = new ApplicationInfo
        {
            id = appHistoryId,
            ApplicationType = FormApplicationTypes.Amendment,
            CurrentStatus = status,
            ApplicationDate = dto.AmendmentRequestDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Patent Amendment Application",
            NewValue = dto.UpdateType.ToString(),
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.AmendmentRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = applicant?.Name,
                    UserId = file.CreatorAccount
                }
            }
        };

        // Create amendment record
        var amendmentRecord = CreateAmendmentRecord(file, dto, appHistoryId);

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, amendmentRecord)
            .Push(f => f.ApplicationHistory, amendmentHistory);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.PatentAmendment, file.FileId, amendmentHistory.id);
        }

        _log.LogInformation("Completed patent amendment application for FileId {FileId}, PaymentSuccessful {PaymentSuccessful}", dto.FileId, paymentSuccessful);
        return true;
    }

    private async Task<List<string>> ProcessBase64Attachments(List<DesignAttachmentDto> attachments)
    {
        var uploadedUrls = new List<string>();

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.Data))
                continue;

            try
            {
                // Strip the data URL prefix (e.g., "data:application/pdf;base64,")
                string base64Data = attachment.Data;
                if (base64Data.Contains(","))
                {
                    base64Data = base64Data.Split(',')[1];
                }

                // Convert base64 to byte array
                byte[] fileBytes = Convert.FromBase64String(base64Data);

                // Determine file extension from content type or filename
                string extension = GetExtensionFromContentType(attachment.Type);
                if (string.IsNullOrEmpty(extension) && !string.IsNullOrEmpty(attachment.Name))
                {
                    extension = Path.GetExtension(attachment.Name);
                }

                // Generate unique filename
                var trustedFileName = Path.GetRandomFileName();
                trustedFileName = Path.GetFileNameWithoutExtension(trustedFileName) + extension;

                // Upload to storage
                await _attachmentCollection.InsertOneAsync(new AttachmentInfo
                {
                    Id = trustedFileName,
                    ContentType = attachment.Type,
                    Data = fileBytes
                });

                // Generate URL
                var url = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
                uploadedUrls.Add(url);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error processing base64 attachment: {Name}", attachment.Name);
                // Continue processing other files even if one fails
            }
        }

        return uploadedUrls;
    }

    private string GetExtensionFromContentType(string contentType)
    {
        return contentType?.ToLower() switch
        {
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "application/pdf" => ".pdf",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            _ => ".bin"
        };
    }

    private async Task<PostRegistrationApp> CreateDesignAmendmentRecord(Filling file, DesignAmendmentDto dto, string appHistoryId)
    {
        var recordal = new PostRegistrationApp
        {
            Id = appHistoryId,
            RecordalType = "Design Amendment",
            FileNumber = dto.FileId,
            rrr = dto.PaymentRRR,
            dateOfRecordal = (dto.AmendmentRequestDate ?? DateTime.Now).ToString(),
            FilingDate = DateTime.Now.ToString(),
            AmendmentType = dto.UpdateType.ToString(),
            IsAmendment = true,
            IsApproved = false,
            DateTreated = ""
        };

        switch (dto.UpdateType)
        {
            case DesignAmendmentTypes.ApplicantName:
                var oldNames = file.applicants?.Select(a => a.Name).ToList() ?? new List<string>();
                var newNames = dto.ApplicantNames ?? new List<string>();

                recordal.OldDataJson = JsonSerializer.Serialize(oldNames);
                recordal.NewDataJson = JsonSerializer.Serialize(newNames);
                recordal.message = $"Updating {newNames.Count} applicant names";
                break;

            case DesignAmendmentTypes.ApplicantAddress:
                var oldAddresses = file.applicants?.Select(a => a.Address).ToList() ?? new List<string>();
                var oldEmails = file.applicants?.Select(a => a.Email).ToList() ?? new List<string>();
                var oldPhones = file.applicants?.Select(a => a.Phone).ToList() ?? new List<string>();
                var oldNationalities = file.applicants?.Select(a => a.country).ToList() ?? new List<string>();

                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    Addresses = oldAddresses,
                    Emails = oldEmails,
                    Phones = oldPhones,
                    Nationalities = oldNationalities,
                    States = file.applicants?.Select(a => a.State).ToList() ?? new List<string>(),
                    Cities = file.applicants?.Select(a => a.city).ToList() ?? new List<string>()
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    Addresses = dto.ApplicantAddresses ?? new List<string>(),
                    Emails = dto.ApplicantEmails ?? new List<string>(),
                    Phones = dto.ApplicantPhones ?? new List<string>(),
                    Nationalities = dto.ApplicantNationalities ?? new List<string>(),
                    States = dto.ApplicantStates ?? new List<string>(),
                    Cities = dto.ApplicantCities ?? new List<string>()
                });
                recordal.message = "Updating applicant address information";
                break;

            case DesignAmendmentTypes.DesignTitle:
                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    Title = file.TitleOfDesign,
                    DesignType = file.DesignType,
                    StatementOfNovelty = file.StatementOfNovelty
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    Title = dto.DesignTitle,
                    DesignType = dto.DesignType,
                    StatementOfNovelty = dto.StatementOfNovelty
                });
                recordal.message = "Updating design title and details";
                break;

            case DesignAmendmentTypes.CorrespondenceInformation:
                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    Name = file.Correspondence?.name,
                    Address = file.Correspondence?.address,
                    Email = file.Correspondence?.email,
                    Phone = file.Correspondence?.phone,
                    State = file.Correspondence?.state,
                    Nationality = file.Correspondence?.Nationality
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    Name = dto.CorrespondenceName,
                    Address = dto.CorrespondenceAddress,
                    Email = dto.CorrespondenceEmail,
                    Phone = dto.CorrespondencePhone,
                    State = dto.CorrespondenceState,
                    Nationality = dto.CorrespondenceNationality
                });
                recordal.message = "Updating correspondence information";
                break;

            case DesignAmendmentTypes.PriorityInfo:
                recordal.OldDataJson = JsonSerializer.Serialize(new
                {
                    FirstPriorityInfo = file.FirstPriorityInfo ?? new List<PriorityInfo>(),
                    PriorityInfo = file.PriorityInfo ?? new List<PriorityInfo>()
                });

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    FirstPriorityInfo = dto.FirstPriorityInfo ?? new List<PriorityInfo>(),
                    PriorityInfo = dto.PriorityInfo ?? new List<PriorityInfo>()
                });
                recordal.message = "Updating priority information";
                break;

            case DesignAmendmentTypes.AddAndRemoveApplicant:
                recordal.OldDataJson = JsonSerializer.Serialize(file.applicants ?? new List<ApplicantInfo>());
                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    EditedApplicants = dto.EditedApplicants ?? new List<ApplicantInfo>(),
                    NewApplicants = dto.NewApplicants ?? new List<ApplicantInfo>(),
                    RemoveIds = dto.RemoveApplicantIds ?? new List<string>()
                });
                recordal.message = "Adding/Removing applicants";
                break;

            case DesignAmendmentTypes.DesignType:
                recordal.OldDataJson = JsonSerializer.Serialize(file.DesignType);
                recordal.NewDataJson = JsonSerializer.Serialize(dto.DesignType);
                recordal.message = "Updating design type";
                break;

            case DesignAmendmentTypes.StatementOfNovelty:
                recordal.OldDataJson = JsonSerializer.Serialize(file.StatementOfNovelty);
                recordal.NewDataJson = JsonSerializer.Serialize(dto.StatementOfNovelty);
                recordal.message = "Updating statement of novelty";
                break;

            case DesignAmendmentTypes.CreatorInformation:
                recordal.OldDataJson = JsonSerializer.Serialize(file.DesignCreators ?? new List<ApplicantInfo>());
                recordal.NewDataJson = JsonSerializer.Serialize(dto.DesignCreators ?? new List<ApplicantInfo>());
                recordal.message = "Updating design creators information";
                break;

            case DesignAmendmentTypes.DesignAttachments:
                var oldAttachments = file.Attachments?.SelectMany(a => a.url).ToList() ?? new List<string>();
                recordal.OldDataJson = JsonSerializer.Serialize(oldAttachments);

                // Process base64 files and upload them
                List<AttachmentType> uploadedAttachments = new List<AttachmentType>();
                if (dto.NewDesignAttachments != null && dto.NewDesignAttachments.Any())
                {
                    var uploadedUrls = await ProcessBase64Attachments(dto.NewDesignAttachments);
                    uploadedAttachments.Add(new AttachmentType
                    {
                        name = "design-amendments",
                        url = uploadedUrls
                    });
                }

                recordal.NewDataJson = JsonSerializer.Serialize(new
                {
                    RemoveUrls = dto.RemoveDesignAttachmentUrls ?? new List<string>(),
                    NewAttachments = uploadedAttachments
                });
                recordal.message = "Updating design attachments";
                break;
        }

        return recordal;
    }

    public async Task<bool> NewDesignAmendmentApplication(DesignAmendmentDto dto, string userId)
    {
        _log.LogInformation($"[NewDesignAmendmentApplication] Starting - FileId: {dto.FileId}, RRR: {dto.Rrr}, UserId: {userId}, UpdateType: {dto.UpdateType}");

        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning($"[NewDesignAmendmentApplication] File not found - FileId: {dto.FileId}");
            return false;
        }

        var user = await _userCollection.Find(Builders<AppUser>.Filter.Eq(u => u.Id, userId)).FirstOrDefaultAsync();
        if (user == null)
            return false;

        var applicant = file.applicants.FirstOrDefault();

        var userName = user != null
            ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : applicant?.Name ?? "Unknown";

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting amendment approval"
            : "Amendment application submitted, awaiting payment";

        var appHistoryId = Guid.NewGuid().ToString();

        // Application history
        var amendmentHistory = new ApplicationInfo
        {
            id = appHistoryId,
            ApplicationType = FormApplicationTypes.Amendment,
            CurrentStatus = status,
            ApplicationDate = dto.AmendmentRequestDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Design Amendment Application",
            NewValue = dto.UpdateType.ToString(),
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.AmendmentRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = userName,
                    UserId = user?.Id
                }
            }
        };

        // Create amendment record
        var amendmentRecord = await CreateDesignAmendmentRecord(file, dto, appHistoryId);

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, amendmentRecord)
            .Push(f => f.ApplicationHistory, amendmentHistory);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.DesignAmendment, file.FileId, amendmentHistory.id);
        }

        _log.LogInformation($"[NewDesignAmendmentApplication] Completed successfully - FileId: {dto.FileId}, AppId: {amendmentHistory.id}, PaymentSuccessful: {paymentSuccessful}");
        return true;
    }

    public async Task<object?> GetPatentAmendmentDetailsAsync(string fileId, string appId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch the SPECIFIC PostRegApp for amendment using both RecordalType and Id
        var amendmentApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Patent Amendment"
                              && a.IsAmendment == true
                              && a.Id == appId);

        if (amendmentApp == null)
            return null;

        // Also get the corresponding ApplicationHistory entry
        var applicationHistory = file.ApplicationHistory?
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Amendment);

        var amendmentDetails = new
        {
            FileId = file.FileId,
            ApplicationId = appId,
            AmendmentType = amendmentApp.AmendmentType,
            FilingDate = amendmentApp.FilingDate,
            Status = applicationHistory?.CurrentStatus,
            PaymentRRR = applicationHistory?.PaymentId,

            // Current file info
            AllApplicants = file.applicants?.Select((a, index) => new
            {
                Index = index,
                Id = a.id,
                Name = a.Name,
                Address = a.Address,
                Email = a.Email,
                Phone = a.Phone,
                Country = a.country
            }).ToList(),

            // Amendment changes (before/after comparison)
            Changes = GetAmendmentChanges(amendmentApp),

            IsApproved = amendmentApp.IsApproved,
            Reason = amendmentApp.Reason,
            DateTreated = amendmentApp.DateTreated
        };

        return amendmentDetails;
    }

    private object GetAmendmentChanges(PostRegistrationApp amendment)
    {
        switch (amendment.AmendmentType)
        {
            case "ApplicantName":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var oldNames = JsonSerializer.Deserialize<List<string>>(amendment.OldDataJson ?? "[]");
                    var newNames = JsonSerializer.Deserialize<List<string>>(amendment.NewDataJson);
                    return new { OldNames = oldNames, NewNames = newNames };
                }
                return new { Message = "No name changes found" };

            case "ApplicantAddress":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var oldData = JsonSerializer.Deserialize<dynamic>(amendment.OldDataJson ?? "{}");
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson ?? "{}");
                    return new
                    {
                        OldAddressData = oldData,
                        NewAddressData = newData
                    };
                }
                return new { Message = "No address changes found" };

            case "FileTitle":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var oldData = JsonSerializer.Deserialize<dynamic>(amendment.OldDataJson ?? "{}");
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson ?? "{}");
                    return new
                    {
                        OldTitleData = oldData,
                        NewTitleData = newData
                    };
                }
                return new { Message = "No title changes found" };

            case "CorrespondenceInformation":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var oldData = JsonSerializer.Deserialize<dynamic>(amendment.OldDataJson ?? "{}");
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson ?? "{}");
                    return new
                    {
                        OldCorrespondence = oldData,
                        NewCorrespondence = newData
                    };
                }
                return new { Message = "No correspondence changes found" };

            case "EditInventors":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var oldInventors = JsonSerializer.Deserialize<List<ApplicantInfo>>(amendment.OldDataJson ?? "[]");
                    var newInventors = JsonSerializer.Deserialize<List<ApplicantInfo>>(amendment.NewDataJson ?? "[]");
                    return new
                    {
                        OldInventors = oldInventors,
                        NewInventors = newInventors
                    };
                }
                return new { Message = "No inventor changes found" };

            case "PriorityInfo":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var oldData = JsonSerializer.Deserialize<dynamic>(amendment.OldDataJson ?? "{}");
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson ?? "{}");
                    return new
                    {
                        OldPriorityData = oldData,
                        NewPriorityData = newData
                    };
                }
                return new { Message = "No priority changes found" };

            case "AddAndRemoveApplicant":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var oldApplicants = JsonSerializer.Deserialize<List<ApplicantInfo>>(amendment.OldDataJson ?? "[]");
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson ?? "{}");
                    return new
                    {
                        OldApplicants = oldApplicants,
                        NewApplicantData = newData
                    };
                }
                return new { Message = "No applicant changes found" };

            default:
                return new { Message = $"Unknown amendment type: {amendment.AmendmentType}" };
        }
    }

    public async Task<(bool Success, string Message)> PatentAmendmentDecisionAsync(string fileId, string appId, bool approve, string reason, string? appUserId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        // Find the ApplicationInfo for Amendment
        var amendmentApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Amendment);

        if (amendmentApp == null)
            return (false, "No amendment application found");

        // Find the PostRegApp for amendment
        var amendmentRecord = file.PostRegApplications?
            .FirstOrDefault(p => p.Id == appId && p.IsAmendment == true);

        if (amendmentRecord == null)
            return (false, "No amendment record found");

        // Update status
        var beforeStatus = amendmentApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = amendmentApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = file.applicants.FirstOrDefault()?.Name,
            UserId = file.CreatorAccount
        };

        amendmentApp.StatusHistory.Add(newStatus);
        amendmentApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;
        amendmentRecord.IsApproved = approve;
        amendmentRecord.DateTreated = DateTime.Now.ToString();
        amendmentRecord.Reason = reason;

        // ✅ Apply changes if approved
        if (approve)
        {
            ApplyAmendmentChanges(file, amendmentRecord);
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = string.IsNullOrWhiteSpace(appUserId) ? file.CreatorAccount : appUserId,
            AfterStatus = amendmentApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Amendment,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.PatentExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Amendment approved and applied" : "Amendment rejected");
    }

    #endregion

    //Design License Post Registration Section
    public async Task<bool> NewDesignLicenseApplication(DesignLicenseDto dto)
    {
        _log.LogInformation($"[NewDesignLicenseApplication] Starting - FileId: {dto.FileId}, RRR: {dto.Rrr}, UserId: {dto.UserId}");

        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning($"[NewDesignLicenseApplication] File not found - FileId: {dto.FileId}");
            return false;
        }

        var user = await _userCollection.Find(Builders<AppUser>.Filter.Eq(u => u.Id, dto.UserId)).FirstOrDefaultAsync();
        if (user == null)
            return false;

        var applicant = file.applicants.FirstOrDefault();

        // Deed of License upload
        if (dto.Deedoflicense != null && dto.Deedoflicense.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.Deedoflicense);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "DesignDeedoflicense");
            if (existingDeed != null)
            {
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignDeedoflicense",
                    url = deedLinks
                });
            }
        }

        // Supporting documents upload
        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "DesignLicenseSupportingDocuments");
            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignLicenseSupportingDocuments",
                    url = supportingDocsUrl
                });
            }
        }

        var userName = user != null
                ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
                : applicant?.Name ?? "Unknown";

        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "License application submitted, awaiting payment";

        var licenseHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.License,
            CurrentStatus = status,
            ApplicationDate = dto.LicenseDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Design License Application",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.LicenseRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = userName,
                    UserId = user?.Id
                }
            }
        };

        var recordal = new PostRegistrationApp
        {
            Id = licenseHistory.id,
            RecordalType = "Design License Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.LicenseDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.LicenseRequestDate ?? DateTime.Now).ToString(),
            OldLicensorName = dto.OldLicensorName,
            OldLicensorEmail = dto.OldLicensorEmail,
            OldLicensorPhone = dto.OldLicensorPhone,
            OldLicensorAddress = dto.OldLicensorAddress,
            OldLicensorNationality = dto.OldLicensorNationality,
            OldLicensorState = dto.OldLicensorState,
            OldLicensorCity = dto.OldLicensorCity,
            Name = dto.NewLicenseeName,
            Email = dto.NewLicenseeEmail,
            Phone = dto.NewLicenseePhone,
            Address = dto.NewLicenseeAddress,
            Nationality = dto.NewLicenseeNationality,
            State = dto.NewLicenseeState,
            City = dto.NewLicenseeCity,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, licenseHistory)
            .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.DesignLicense, file.FileId, licenseHistory.id);
        }

        _log.LogInformation($"[NewDesignLicenseApplication] Completed successfully - FileId: {dto.FileId}, AppId: {licenseHistory.id}, PaymentSuccessful: {paymentSuccessful}");
        return true;
    }

    public async Task<object?> GetDesignLicenseDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        var deedOfLicenseAttachments = file.Attachments?
            .Where(a => a.name == "DesignDeedoflicense")
            .Select(a => new { a.name, a.url })
            .ToList();

        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "DesignLicenseSupportingDocuments")
            .Select(a => new { a.name, a.url })
            .ToList();

        var licenseApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Design License Recordal");

        var newLicensee = licenseApp == null ? null : new
        {
            Name = licenseApp.Name,
            Address = licenseApp.Address,
            Email = licenseApp.Email,
            Phone = licenseApp.Phone,
            State = licenseApp.State,
            Nationality = licenseApp.Nationality,
            City = licenseApp.City,
        };

        var oldLicensor = licenseApp == null ? null : new
        {
            Name = licenseApp.OldLicensorName,
            Address = licenseApp.OldLicensorAddress,
            Email = licenseApp.OldLicensorEmail,
            Phone = licenseApp.OldLicensorPhone,
            State = licenseApp.OldLicensorState,
            Nationality = licenseApp.OldLicensorNationality,
            City = licenseApp.OldLicensorCity
        };

        return new
        {
            FileId = file.FileId,
            DeedOfLicenseAttachments = deedOfLicenseAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments,
            NewLicensee = newLicensee,
            OldLicensor = oldLicensor,
            Filingdate = licenseApp?.FilingDate
        };
    }

    public async Task<(bool Success, string Message)> DesignLicenseDecisionAsync(
    string fileId,
    string appId,
    bool approve,
    string reason,
    ApplicantInfo newLicensee = null, string? userId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null) throw new UnauthorizedAccessException("Unauthorized User");

        var licenseApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.License);

        if (licenseApp == null)
            return (false, "No license application found");

        var beforeStatus = licenseApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = licenseApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = user.FirstName + " " + user.LastName,
            UserId = user.Id
        };

        licenseApp.StatusHistory.Add(newStatus);
        licenseApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        if (approve && newLicensee != null)
        {
            file.applicants = new List<ApplicantInfo> { newLicensee };
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = user.Id ?? user.CreatorId,
            AfterStatus = licenseApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.License,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.DesignExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Design license approved" : "Design license refused");
    }

    //Design Mortgage Post Registration Section
    public async Task<bool> NewDesignMortgageApplication(DesignMortgageDto dto)
    {
        _log.LogInformation($"[NewDesignMortgageApplication] Starting - FileId: {dto.FileId}, RRR: {dto.Rrr}, UserId: {dto.UserId}");

        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning($"[NewDesignMortgageApplication] File not found - FileId: {dto.FileId}");
            return false;
        }

        var user = await _userCollection.Find(Builders<AppUser>.Filter.Eq(u => u.Id, dto.UserId)).FirstOrDefaultAsync();
        if (user == null)
            return false;

        var applicant = file.applicants.FirstOrDefault();

        if (dto.Deedofmortgage != null && dto.Deedofmortgage.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.Deedofmortgage);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "DesignDeedofmortgage");
            if (existingDeed != null)
            {
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignDeedofmortgage",
                    url = deedLinks
                });
            }
        }

        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "DesignMortgageSupportingDocuments");
            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignMortgageSupportingDocuments",
                    url = supportingDocsUrl
                });
            }
        }

        var userName = user != null
        ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
        : applicant?.Name ?? "Unknown";

        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "Mortgage application submitted, awaiting payment";

        var mortgageHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.Mortgage,
            CurrentStatus = status,
            ApplicationDate = dto.MortgageDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Design Mortgage Application",
            NewValue = string.Empty,
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.MortgageRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = userName,
                    UserId = user?.Id
                }
            }
        };

        var recordal = new PostRegistrationApp
        {
            Id = mortgageHistory.id,
            RecordalType = "Design Mortgage Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.MortgageDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.MortgageRequestDate ?? DateTime.Now).ToString(),
            OldMortgagorName = dto.OldMortgageeName,
            OldMortgagorEmail = dto.OldMortgageeEmail,
            OldMortgagorPhone = dto.OldMortgageePhone,
            OldMortgagorAddress = dto.OldMortgageeAddress,
            OldMortgagorNationality = dto.OldMortgageeNationality,
            OldMortgagorState = dto.OldMortgageeState,
            OldMortgagorCity = dto.OldMortgageeCity,
            Name = dto.NewMortgagorName,
            Email = dto.NewMortgagorEmail,
            Phone = dto.NewMortgagorPhone,
            Address = dto.NewMortgagorAddress,
            Nationality = dto.NewMortgagorNationality,
            State = dto.NewMortgagorState,
            City = dto.NewMortgagorCity,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : string.Empty
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, mortgageHistory)
            .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.DesignMortgage, file.FileId, mortgageHistory.id);
        }

        _log.LogInformation($"[NewDesignMortgageApplication] Completed successfully - FileId: {dto.FileId}, AppId: {mortgageHistory.id}, PaymentSuccessful: {paymentSuccessful}");
        return true;
    }

    public async Task<object?> GetDesignMortgageDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        var deedOfMortgageAttachments = file.Attachments?
            .Where(a => a.name == "DesignDeedofmortgage")
            .Select(a => new { a.name, a.url })
            .ToList();

        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "DesignMortgageSupportingDocuments")
            .Select(a => new { a.name, a.url })
            .ToList();

        var mortgageApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Design Mortgage Recordal");

        var newMortgagee = mortgageApp == null ? null : new
        {
            Name = mortgageApp.Name,
            Address = mortgageApp.Address,
            Email = mortgageApp.Email,
            Phone = mortgageApp.Phone,
            State = mortgageApp.State,
            Nationality = mortgageApp.Nationality,
            City = mortgageApp.City
        };

        var oldMortgagor = mortgageApp == null ? null : new
        {
            Name = mortgageApp.OldMortgagorName,
            Address = mortgageApp.OldMortgagorAddress,
            Email = mortgageApp.OldMortgagorEmail,
            Phone = mortgageApp.OldMortgagorPhone,
            State = mortgageApp.OldMortgagorState,
            Nationality = mortgageApp.OldMortgagorNationality,
            City = mortgageApp.OldMortgagorCity
        };

        return new
        {
            FileId = file.FileId,
            DeedOfMortgageAttachments = deedOfMortgageAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments,
            NewMortgagee = newMortgagee,
            OldMortgagor = oldMortgagor,
            Filingdate = mortgageApp?.FilingDate
        };
    }

    public async Task<(bool Success, string Message)> DesignMortgageDecisionAsync(
        string fileId,
        string appId,
        bool approve,
        string reason,
        ApplicantInfo newMortgagee = null, string? userId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null) throw new UnauthorizedAccessException("Unauthorized User");

        var mortgageApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Mortgage);

        if (mortgageApp == null)
            return (false, "No mortgage application found");

        var beforeStatus = mortgageApp.CurrentStatus;
        var statusEntry = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = mortgageApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = user.FirstName + " " + user.LastName,
            UserId = user.Id
        };

        mortgageApp.StatusHistory ??= new List<ApplicationHistory>();
        mortgageApp.StatusHistory.Add(statusEntry);
        mortgageApp.CurrentStatus = statusEntry.afterStatus.Value;

        var recordal = file.PostRegApplications?
            .FirstOrDefault(a => a.Id == appId && a.RecordalType == "Design Mortgage Recordal");

        if (recordal != null)
        {
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = reason;
        }

        if (approve && newMortgagee != null)
        {
            file.applicants = new List<ApplicantInfo> { newMortgagee };

            if (recordal != null)
            {
                recordal.Name = newMortgagee.Name;
                recordal.Email = newMortgagee.Email;
                recordal.Phone = newMortgagee.Phone;
                recordal.Address = newMortgagee.Address;
                recordal.Nationality = newMortgagee.country;
                recordal.State = newMortgagee.State;
                recordal.City = newMortgagee.city;
            }
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = string.IsNullOrWhiteSpace(userId) ? user.CreatorId : userId,
            AfterStatus = mortgageApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Mortgage,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.DesignExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Design mortgage approved" : "Design mortgage refused");
    }

    //Design Assignment Post Registration Section
    public async Task<bool> NewDesignAssignmentApplication(DesignAssignmentDto dto)
    {
        _log.LogInformation($"[NewDesignAssignmentApplication] Starting - FileId: {dto.FileId}, RRR: {dto.Rrr}, UserId: {dto.UserId}");

        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null)
        {
            _log.LogWarning($"[NewDesignAssignmentApplication] File not found - FileId: {dto.FileId}");
            return false;
        }

        var user = await _userCollection.Find(Builders<AppUser>.Filter.Eq(u => u.Id, dto.UserId)).FirstOrDefaultAsync();
        if (user == null)
            return false;

        var applicant = file.applicants?.FirstOrDefault();

        if (dto.DeedOfAssignment != null && dto.DeedOfAssignment.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.DeedOfAssignment);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "DesignDeedofassignment");
            if (existingDeed != null)
            {
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignDeedofassignment",
                    url = deedLinks
                });
            }
        }

        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "DesignAssignmentSupportingDocuments");
            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignAssignmentSupportingDocuments",
                    url = supportingDocsUrl
                });
            }
        }

        var userName = user != null
        ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
        : applicant?.Name ?? "Unknown";

        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "Assignment application submitted, awaiting payment";

        var assignmentHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.Assignment,
            CurrentStatus = status,
            ApplicationDate = dto.AssignmentDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Design Assignment Application",
            NewValue = string.Empty,
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.AssignmentRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = userName,
                    UserId = user?.Id
                }
            }
        };

        var recordal = new PostRegistrationApp
        {
            Id = assignmentHistory.id,
            RecordalType = "Design Assignment Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            dateOfRecordal = (dto.AssignmentDate ?? DateTime.Now).ToString(),
            FilingDate = (dto.AssignmentRequestDate ?? DateTime.Now).ToString(),
            OldAssignorName = dto.OldAssignorName,
            OldAssignorEmail = dto.OldAssignorEmail,
            OldAssignorPhone = dto.OldAssignorPhone,
            OldAssignorAddress = dto.OldAssignorAddress,
            OldAssignorNationality = dto.OldAssignorNationality,
            OldAssignorState = dto.OldAssignorState,
            OldAssignorCity = dto.OldAssignorCity,
            Name = dto.NewAssigneeName,
            Email = dto.NewAssigneeEmail,
            Phone = dto.NewAssigneePhone,
            Address = dto.NewAssigneeAddress,
            Nationality = dto.NewAssigneeNationality,
            State = dto.NewAssigneeState,
            City = dto.NewAssigneeCity,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : string.Empty
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, assignmentHistory)
            .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update);

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.DesignAssignment, file.FileId, assignmentHistory.id);
        }

        _log.LogInformation($"[NewDesignAssignmentApplication] Completed successfully - FileId: {dto.FileId}, AppId: {assignmentHistory.id}, PaymentSuccessful: {paymentSuccessful}");
        return true;
    }

    public async Task<object?> GetDesignAssignmentDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        var deedAttachments = file.Attachments?
            .Where(a => a.name == "DesignDeedofassignment")
            .Select(a => new { a.name, a.url })
            .ToList();

        var supportingAttachments = file.Attachments?
            .Where(a => a.name == "DesignAssignmentSupportingDocuments")
            .Select(a => new { a.name, a.url })
            .ToList();

        var assignmentApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Design Assignment Recordal");

        var newAssignee = assignmentApp == null ? null : new
        {
            Name = assignmentApp.Name,
            Address = assignmentApp.Address,
            Email = assignmentApp.Email,
            Phone = assignmentApp.Phone,
            State = assignmentApp.State,
            Nationality = assignmentApp.Nationality,
            City = assignmentApp.City
        };

        var oldAssignor = assignmentApp == null ? null : new
        {
            Name = assignmentApp.OldAssignorName,
            Address = assignmentApp.OldAssignorAddress,
            Email = assignmentApp.OldAssignorEmail,
            Phone = assignmentApp.OldAssignorPhone,
            State = assignmentApp.OldAssignorState,
            Nationality = assignmentApp.OldAssignorNationality,
            City = assignmentApp.OldAssignorCity
        };

        return new
        {
            FileId = file.FileId,
            DeedOfAssignmentAttachments = deedAttachments,
            SupportingDocumentAttachments = supportingAttachments,
            DesignType = file.DesignType,
            DesignTypeDescription = file.DesignType?.ToString(),
            TitleOfDesign = file.TitleOfDesign,
            NewAssignee = newAssignee,
            OldAssignor = oldAssignor,
            Filingdate = assignmentApp?.FilingDate
        };
    }

    public async Task<(bool Success, string Message)> DesignAssignmentDecisionAsync(
        string fileId,
        string appId,
        bool approve,
        string reason,
        ApplicantInfo newAssignee = null,
        string? userId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
            throw new UnauthorizedAccessException("Unauthorized User");

        var assignmentApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Assignment);

        if (assignmentApp == null)
            return (false, "No assignment application found");

        var beforeStatus = assignmentApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = assignmentApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = user.FirstName + " " + user.LastName,
            UserId = user.Id
        };

        assignmentApp.StatusHistory.Add(newStatus);
        assignmentApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        var recordal = file.PostRegApplications?
            .FirstOrDefault(p => p.Id == appId) ??
                       file.PostRegApplications?
                           .FirstOrDefault(p => p.RecordalType == "Design Assignment Recordal");

        if (recordal != null)
        {
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = reason;
        }

        if (approve)
        {
            if (newAssignee == null && recordal != null)
            {
                newAssignee = new ApplicantInfo
                {
                    Name = recordal.Name,
                    Address = recordal.Address,
                    Email = recordal.Email,
                    Phone = recordal.Phone,
                    State = recordal.State,
                    country = recordal.Nationality,
                    city = recordal.City
                };
            }

            if (newAssignee == null || string.IsNullOrWhiteSpace(newAssignee.Name))
            {
                return (false, "Assignee information is required to approve assignment");
            }

            file.applicants = new List<ApplicantInfo>
            {
                new ApplicantInfo
                {
                    Name = newAssignee.Name,
                    Address = newAssignee.Address,
                    Email = newAssignee.Email,
                    Phone = newAssignee.Phone,
                    State = newAssignee.State,
                    country = newAssignee.country,
                    city = newAssignee.city
                }
            };
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = user.Id ?? user.CreatorId,
            AfterStatus = assignmentApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Assignment,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.DesignExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Design assignment approved" : "Design assignment refused");
    }

    //Design Merger Post Registration Section
    public async Task<bool> NewDesignMergerApplication(DesignMergerDto dto)
    {
        _log.LogInformation($"[NewDesignMergerApplication] Starting - FileId: {dto.FileId}, RRR: {dto.Rrr}, UserId: {dto.UserId}");

        var fileId = dto.FileId?.Trim();
        if (string.IsNullOrWhiteSpace(fileId))
        {
            _log.LogWarning("[NewDesignMergerApplication] Design merger submission missing file id");
            return false;
        }

        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();
        if (file == null) return false;

        var user = await _userCollection.Find(Builders<AppUser>.Filter.Eq(u => u.Id, dto.UserId)).FirstOrDefaultAsync();
        if (user == null)
            return false;

        var applicant = file.applicants?.FirstOrDefault();

        // Upload deed of merger
        if (dto.Deedofmerger != null && dto.Deedofmerger.Count > 0)
        {
            var deedLinks = await UploadAttachment(dto.Deedofmerger);
            file.Attachments ??= new List<AttachmentType>();
            var existingDeed = file.Attachments.FirstOrDefault(a => a.name == "DesignDeedofmerger");

            if (existingDeed != null)
            {
                foreach (var url in deedLinks)
                {
                    if (!existingDeed.url.Contains(url))
                        existingDeed.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignDeedofmerger",
                    url = deedLinks
                });
            }
        }

        // Upload supporting documents
        if (dto.SupportingDocuments != null && dto.SupportingDocuments.Count > 0)
        {
            var supportingDocsUrl = await UploadAttachment(dto.SupportingDocuments);
            file.Attachments ??= new List<AttachmentType>();
            var existingSupport = file.Attachments.FirstOrDefault(a => a.name == "DesignMergerSupportingDocuments");

            if (existingSupport != null)
            {
                foreach (var url in supportingDocsUrl)
                {
                    if (!existingSupport.url.Contains(url))
                        existingSupport.url.Add(url);
                }
            }
            else
            {
                file.Attachments.Add(new AttachmentType
                {
                    name = "DesignMergerSupportingDocuments",
                    url = supportingDocsUrl
                });
            }
        }

        var userName = user != null
        ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))
        : applicant?.Name ?? "Unknown";

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        var paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "Merger application submitted, awaiting payment";

        var mergerDate = dto.MergerDate ?? DateTime.Now;
        var requestDate = dto.MergerRequestDate ?? DateTime.Now;

        // Application history entry
        var mergerHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.Merger,
            CurrentStatus = status,
            ApplicationDate = mergerDate,
            PaymentId = dto.Rrr,
            FieldToChange = "Design Merger Application",
            NewValue = string.Empty,
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = requestDate,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = status,
                    Message = statusMessage,
                    User = userName,
                    UserId = user?.Id
                }
            }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = mergerHistory.id,
            RecordalType = "Design Merger Recordal",
            FileNumber = fileId,
            rrr = dto.Rrr,
            dateOfRecordal = mergerDate.ToString(),
            FilingDate = requestDate.ToString(),
            OldMergerName = dto.OldMergerName ?? applicant?.Name,
            OldMergerEmail = dto.OldMergerEmail ?? applicant?.Email,
            OldMergerPhone = dto.OldMergerPhone ?? applicant?.Phone,
            OldMergerAddress = dto.OldMergerAddress ?? applicant?.Address,
            OldMergerNationality = dto.OldMergerNationality ?? applicant?.country,
            OldMergerState = dto.OldMergerState ?? applicant?.State,
            OldMergerCity = dto.OldMergerCity ?? applicant?.city,
            Name = dto.NewMergerName ?? applicant?.Name,
            Email = dto.NewMergerEmail ?? applicant?.Email,
            Phone = dto.NewMergerPhone ?? applicant?.Phone,
            Address = dto.NewMergerAddress ?? applicant?.Address,
            Nationality = dto.NewMergerNationality ?? applicant?.country,
            State = dto.NewMergerState ?? applicant?.State,
            City = dto.NewMergerCity ?? applicant?.city,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : string.Empty
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, mergerHistory)
            .Set(f => f.Attachments, file.Attachments);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update);

        if (paymentSuccessful)
        {
            SavePayment(paymentDetails, PaymentTypes.DesignMerger, file.FileId, mergerHistory.id);
        }

        _log.LogInformation($"[NewDesignMergerApplication] Completed successfully - FileId: {fileId}, AppId: {mergerHistory.id}, PaymentSuccessful: {paymentSuccessful}");
        return true;
    }

    public async Task<object?> GetDesignMergerDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        var deedOfMergerAttachments = file.Attachments?
            .Where(a => a.name == "DesignDeedofmerger")
            .Select(a => new { a.name, a.url })
            .ToList();

        var supportingDocumentAttachments = file.Attachments?
            .Where(a => a.name == "DesignMergerSupportingDocuments")
            .Select(a => new { a.name, a.url })
            .ToList();

        var mergerApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Design Merger Recordal");

        var newMergedParty = mergerApp == null ? null : new
        {
            Name = mergerApp.Name,
            Address = mergerApp.Address,
            Email = mergerApp.Email,
            Phone = mergerApp.Phone,
            State = mergerApp.State,
            Nationality = mergerApp.Nationality,
            City = mergerApp.City,
        };

        var oldMergerParty = mergerApp == null ? null : new
        {
            Name = mergerApp.OldMergerName,
            Address = mergerApp.OldMergerAddress,
            Email = mergerApp.OldMergerEmail,
            Phone = mergerApp.OldMergerPhone,
            State = mergerApp.OldMergerState,
            Nationality = mergerApp.OldMergerNationality,
            City = mergerApp.OldMergerCity
        };

        return new
        {
            FileId = file.FileId,
            DeedOfMergerAttachments = deedOfMergerAttachments,
            SupportingDocumentAttachments = supportingDocumentAttachments,
            NewMergedParty = newMergedParty,
            OldMergerParty = oldMergerParty,
            filingDate = mergerApp?.FilingDate
        };
    }

    public async Task<object?> GetDesignCtcDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch the PostRegApp for CTC
        var ctcApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Design CTC Recordal");

        if (ctcApp == null)
            return null;

        // ✅ Get the saved attachment names from PostRegistrationApp
        var requestedAttachmentNames = ctcApp.RequestedAttachments ?? new List<string>();

        var requestedAttachments = (file.Attachments ?? new List<AttachmentType>())
            .Where(a => requestedAttachmentNames.Any(reqName =>
                string.Equals(reqName?.Trim(), a.name?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Select(a => new
            {
                Name = a.name,
                Urls = a.url,
                Count = a.url?.Count ?? 0
            })
            .ToList();

        return new
        {
            FileId = file.FileId,
            RequestedAttachments = requestedAttachments,
            FilingDate = ctcApp.FilingDate,
            Rrr = ctcApp.rrr
        };
    }

    public async Task<object?> GetDesignAmendmentDetailsAsync(string fileId, string appId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch the SPECIFIC PostRegApp for amendment using both RecordalType and Id
        var amendmentApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Design Amendment"
                              && a.IsAmendment == true
                              && a.Id == appId);

        if (amendmentApp == null)
            return null;

        // Also get the corresponding ApplicationHistory entry
        var applicationHistory = file.ApplicationHistory?
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Amendment);

        var amendmentDetails = new
        {
            FileId = file.FileId,
            ApplicationId = appId,
            AmendmentType = amendmentApp.AmendmentType,
            FilingDate = amendmentApp.FilingDate,
            Status = applicationHistory?.CurrentStatus,
            PaymentRRR = applicationHistory?.PaymentId,

            // Current file info
            AllApplicants = file.applicants?.Select((a, index) => new
            {
                Index = index,
                Id = a.id,
                Name = a.Name,
                Address = a.Address,
                Email = a.Email,
                Phone = a.Phone,
                Nationality = a.country,
                State = a.State,
                City = a.city
            }).ToList(),

            CurrentDesignTitle = file.TitleOfDesign,
            CurrentDesignType = file.DesignType,
            CurrentStatementOfNovelty = file.StatementOfNovelty,
            CurrentCorrespondence = file.Correspondence,
            CurrentPriorityInfo = file.PriorityInfo,
            CurrentFirstPriorityInfo = file.FirstPriorityInfo,

            // Amendment data
            OldDataJson = amendmentApp.OldDataJson,
            NewDataJson = amendmentApp.NewDataJson,
            Message = amendmentApp.message
        };

        return amendmentDetails;
    }

    private void ApplyDesignAmendmentChanges(Filling file, PostRegistrationApp amendment)
    {
        switch (amendment.AmendmentType)
        {
            case "ApplicantName":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newNames = JsonSerializer.Deserialize<List<string>>(amendment.NewDataJson);
                    int updateCount = Math.Min(file.applicants.Count, newNames.Count);
                    for (int i = 0; i < updateCount; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(newNames[i]))
                        {
                            file.applicants[i].Name = newNames[i];
                        }
                    }
                }
                break;

            case "ApplicantAddress":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);
                    var addresses = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Addresses").GetRawText());
                    var emails = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Emails").GetRawText());
                    var phones = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Phones").GetRawText());
                    var nationalities = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Nationalities").GetRawText());
                    var states = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("States").GetRawText());
                    var cities = JsonSerializer.Deserialize<List<string>>(newData.GetProperty("Cities").GetRawText());

                    int updateCount = Math.Min(file.applicants.Count, addresses.Count);
                    for (int i = 0; i < updateCount; i++)
                    {
                        if (i < addresses.Count && !string.IsNullOrWhiteSpace(addresses[i]))
                            file.applicants[i].Address = addresses[i];
                        if (i < emails.Count && !string.IsNullOrWhiteSpace(emails[i]))
                            file.applicants[i].Email = emails[i];
                        if (i < phones.Count && !string.IsNullOrWhiteSpace(phones[i]))
                            file.applicants[i].Phone = phones[i];
                        if (i < nationalities.Count && !string.IsNullOrWhiteSpace(nationalities[i]))
                            file.applicants[i].country = nationalities[i];
                        if (i < states.Count && !string.IsNullOrWhiteSpace(states[i]))
                            file.applicants[i].State = states[i];
                        if (i < cities.Count && !string.IsNullOrWhiteSpace(cities[i]))
                            file.applicants[i].city = cities[i];
                    }
                }
                break;

            case "DesignTitle":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                    var titleProp = newData.GetProperty("Title");
                    if (titleProp.ValueKind != JsonValueKind.Null)
                    {
                        var titleValue = titleProp.GetString();
                        if (!string.IsNullOrWhiteSpace(titleValue))
                            file.TitleOfDesign = titleValue;
                    }

                    var designTypeProp = newData.GetProperty("DesignType");
                    if (designTypeProp.ValueKind != JsonValueKind.Null)
                    {
                        var designTypeString = designTypeProp.GetString();
                        if (!string.IsNullOrWhiteSpace(designTypeString))
                        {
                            if (Enum.TryParse<DesignTypes>(designTypeString, true, out DesignTypes designType))
                                file.DesignType = designType;
                        }
                    }

                    var noveltyProp = newData.GetProperty("StatementOfNovelty");
                    if (noveltyProp.ValueKind != JsonValueKind.Null)
                    {
                        var noveltyValue = noveltyProp.GetString();
                        if (!string.IsNullOrWhiteSpace(noveltyValue))
                            file.StatementOfNovelty = noveltyValue;
                    }
                }
                break;

            case "DesignType":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newDesignType = JsonSerializer.Deserialize<string>(amendment.NewDataJson);
                    if (!string.IsNullOrWhiteSpace(newDesignType))
                    {
                        if (Enum.TryParse<DesignTypes>(newDesignType, true, out DesignTypes designType))
                            file.DesignType = designType;
                    }
                }
                break;

            case "StatementOfNovelty":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newNovelty = JsonSerializer.Deserialize<string>(amendment.NewDataJson);
                    if (!string.IsNullOrWhiteSpace(newNovelty))
                        file.StatementOfNovelty = newNovelty;
                }
                break;

            case "CorrespondenceInformation":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    try
                    {
                        file.Correspondence ??= new CorrespondenceType();
                        var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                        var nameProp = newData.GetProperty("Name");
                        if (nameProp.ValueKind != JsonValueKind.Null)
                        {
                            var nameValue = nameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(nameValue))
                                file.Correspondence.name = nameValue;
                        }

                        var addressProp = newData.GetProperty("Address");
                        if (addressProp.ValueKind != JsonValueKind.Null)
                        {
                            var addressValue = addressProp.GetString();
                            if (!string.IsNullOrWhiteSpace(addressValue))
                                file.Correspondence.address = addressValue;
                        }

                        var emailProp = newData.GetProperty("Email");
                        if (emailProp.ValueKind != JsonValueKind.Null)
                        {
                            var emailValue = emailProp.GetString();
                            if (!string.IsNullOrWhiteSpace(emailValue))
                                file.Correspondence.email = emailValue;
                        }

                        var phoneProp = newData.GetProperty("Phone");
                        if (phoneProp.ValueKind != JsonValueKind.Null)
                        {
                            var phoneValue = phoneProp.GetString();
                            if (!string.IsNullOrWhiteSpace(phoneValue))
                                file.Correspondence.phone = phoneValue;
                        }

                        var stateProp = newData.GetProperty("State");
                        if (stateProp.ValueKind != JsonValueKind.Null)
                        {
                            var stateValue = stateProp.GetString();
                            if (!string.IsNullOrWhiteSpace(stateValue))
                                file.Correspondence.state = stateValue;
                        }

                        var nationalityProp = newData.GetProperty("Nationality");
                        if (nationalityProp.ValueKind != JsonValueKind.Null)
                        {
                            var nationalityValue = nationalityProp.GetString();
                            if (!string.IsNullOrWhiteSpace(nationalityValue))
                                file.Correspondence.Nationality = nationalityValue;
                        }
                    }
                    catch (JsonException)
                    {
                        // If deserialization fails, skip update
                    }
                }
                break;

            case "PriorityInfo":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                    var firstPriorityProp = newData.GetProperty("FirstPriorityInfo");
                    if (firstPriorityProp.ValueKind != JsonValueKind.Null)
                    {
                        var firstPriorityInfo = JsonSerializer.Deserialize<List<PriorityInfo>>(firstPriorityProp.GetRawText());
                        file.FirstPriorityInfo = firstPriorityInfo ?? new List<PriorityInfo>();
                    }

                    var priorityProp = newData.GetProperty("PriorityInfo");
                    if (priorityProp.ValueKind != JsonValueKind.Null)
                    {
                        var priorityInfo = JsonSerializer.Deserialize<List<PriorityInfo>>(priorityProp.GetRawText());
                        file.PriorityInfo = priorityInfo ?? new List<PriorityInfo>();
                    }
                }
                break;

            case "AddAndRemoveApplicant":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                    var editedApplicants = JsonSerializer.Deserialize<List<ApplicantInfo>>(
                        newData.GetProperty("EditedApplicants").GetRawText());
                    var newApplicants = JsonSerializer.Deserialize<List<ApplicantInfo>>(
                        newData.GetProperty("NewApplicants").GetRawText());

                    var finalApplicants = new List<ApplicantInfo>();

                    if (editedApplicants != null)
                        finalApplicants.AddRange(editedApplicants);

                    if (newApplicants != null)
                        finalApplicants.AddRange(newApplicants);

                    file.applicants = finalApplicants;
                }
                break;

            case "CreatorInformation":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    try
                    {
                        var newCreators = JsonSerializer.Deserialize<List<ApplicantInfo>>(amendment.NewDataJson);
                        if (newCreators != null && newCreators.Count > 0)
                        {
                            file.DesignCreators = newCreators;
                        }
                    }
                    catch (JsonException)
                    {
                        // If deserialization fails, skip update
                    }
                }
                break;

            case "DesignAttachments":
                if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
                {
                    try
                    {
                        var newData = JsonSerializer.Deserialize<dynamic>(amendment.NewDataJson);

                        var removeUrls = JsonSerializer.Deserialize<List<string>>(
                            newData.GetProperty("RemoveUrls").GetRawText());
                        var newAttachments = JsonSerializer.Deserialize<List<AttachmentType>>(
                            newData.GetProperty("NewAttachments").GetRawText());

                        // Remove attachments where any URL in the url list matches a removeUrl
                        if (removeUrls != null && removeUrls.Count > 0)
                        {
                            var filteredAttachments = new List<AttachmentType>();
                            foreach (var attachment in file.Attachments)
                            {
                                bool shouldRemove = false;
                                foreach (var url in attachment.url)
                                {
                                    if (removeUrls.Contains(url))
                                    {
                                        shouldRemove = true;
                                        break;
                                    }
                                }
                                if (!shouldRemove)
                                {
                                    filteredAttachments.Add(attachment);
                                }
                            }
                            file.Attachments = filteredAttachments;
                        }

                        // Add new attachments
                        if (newAttachments != null && newAttachments.Count > 0)
                        {
                            file.Attachments.AddRange(newAttachments);
                        }
                    }
                    catch (JsonException)
                    {
                        // If deserialization fails, skip update
                    }
                }
                break;
        }
    }

    public async Task<(bool Success, string Message)> DesignAmendmentDecisionAsync(
        string fileId,
        string appId,
        bool approve,
        string reason,
        string? userId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        if (string.IsNullOrWhiteSpace(userId))
            return (false, "User ID is required");

        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
            return (false, "User not found or unauthorized");

        // Find the ApplicationInfo for Amendment
        var amendmentApp = file.ApplicationHistory
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Amendment);

        if (amendmentApp == null)
            return (false, "No amendment application found");

        // Find the PostRegApp for amendment
        var amendmentRecord = file.PostRegApplications?
            .FirstOrDefault(p => p.Id == appId && p.IsAmendment == true);

        if (amendmentRecord == null)
            return (false, "No amendment record found");

        // Update status
        var beforeStatus = amendmentApp.CurrentStatus;
        var newStatus = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = amendmentApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = user.FirstName + " " + user.LastName,
            UserId = user.Id
        };

        amendmentApp.StatusHistory.Add(newStatus);
        amendmentApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;
        amendmentRecord.IsApproved = approve;
        amendmentRecord.DateTreated = DateTime.Now.ToString();
        amendmentRecord.Reason = reason;

        // Apply changes if approved
        if (approve)
        {
            ApplyDesignAmendmentChanges(file, amendmentRecord);
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = user.Id ?? user.CreatorId,
            AfterStatus = amendmentApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Amendment,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.DesignExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Amendment approved and applied" : "Amendment rejected");
    }

    public async Task<(bool Success, string Message)> DesignMergerDecisionAsync(
    string fileId,
    string appId,
    bool approve,
    string reason,
    ApplicantInfo mergedEntity = null,
    string? userId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
            throw new UnauthorizedAccessException("Unauthorized User");

        var mergerApp = file.ApplicationHistory?
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.Merger);

        if (mergerApp == null)
            return (false, "No merger application found");

        var beforeStatus = mergerApp.CurrentStatus;
        var statusEntry = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = mergerApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = user.FirstName + " " + user.LastName,
            UserId = user.Id
        };

        mergerApp.StatusHistory ??= new List<ApplicationHistory>();
        mergerApp.StatusHistory.Add(statusEntry);
        mergerApp.CurrentStatus = statusEntry.afterStatus.Value;

        var recordal = file.PostRegApplications?
            .FirstOrDefault(r => r.Id == appId && r.RecordalType == "Design Merger Recordal");

        if (recordal != null)
        {
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = reason;
        }

        if (approve)
        {
            // If mergedEntity is provided, use it; otherwise extract from recordal
            if (mergedEntity == null && recordal != null)
            {
                // Extract the new merged party from the recordal data
                mergedEntity = new ApplicantInfo
                {
                    Name = recordal.Name,
                    Email = recordal.Email,
                    Phone = recordal.Phone,
                    Address = recordal.Address,
                    country = recordal.Nationality,
                    State = recordal.State,
                    city = recordal.City
                };
            }

            if (mergedEntity != null)
            {
                file.applicants = new List<ApplicantInfo> { mergedEntity };

                if (recordal != null)
                {
                    recordal.Name = mergedEntity.Name;
                    recordal.Email = mergedEntity.Email;
                    recordal.Phone = mergedEntity.Phone;
                    recordal.Address = mergedEntity.Address;
                    recordal.Nationality = mergedEntity.country;
                    recordal.State = mergedEntity.State;
                    recordal.City = mergedEntity.city;
                }
            }
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = user.Id ?? user.CreatorId,
            AfterStatus = mergerApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.Merger,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.DesignExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Design merger approved" : "Design merger refused");
    }

    public async Task<(bool Success, string Message)> DesignCtcDecisionAsync(
        string fileId,
        string appId,
        bool approve,
        string reason,
        string? userId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
            throw new UnauthorizedAccessException("Unauthorized User");

        var ctcApp = file.ApplicationHistory?
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.CertifiedTrueCopy);

        if (ctcApp == null)
            return (false, "No CTC application found");

        var beforeStatus = ctcApp.CurrentStatus;
        var statusEntry = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = ctcApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = user.FirstName + " " + user.LastName,
            UserId = user.Id
        };

        ctcApp.StatusHistory ??= new List<ApplicationHistory>();
        ctcApp.StatusHistory.Add(statusEntry);
        ctcApp.CurrentStatus = statusEntry.afterStatus.Value;

        var recordal = file.PostRegApplications?
            .FirstOrDefault(r => r.Id == appId && r.RecordalType == "Design CTC Recordal");

        if (recordal != null)
        {
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = reason;
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        var performance = new PerformanceDto
        {
            AppUserId = user.Id ?? user.CreatorId,
            AfterStatus = ctcApp.CurrentStatus,
            BeforeStatus = beforeStatus,
            ApplicationType = FormApplicationTypes.CertifiedTrueCopy,
            FileNumber = file.FileId,
            FileType = file.Type,
            Reason = reason,
            Date = DateTime.Now,
            OfficeUnit = Roles.DesignExaminer
        };
        SavePerformance(performance);

        return (true, approve ? "Design CTC approved" : "Design CTC refused");
    }

    // TRADEMARK CTC METHODS
    public async Task<bool> NewTrademarkCtcApplication(TrademarkCtcDto dto)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileId))
            .FirstOrDefaultAsync();
        if (file == null) return false;

        var applicant = file.applicants.FirstOrDefault();

        // Verify payment
        var paymentDetails = await _remitaPaymentUtils.GetDetailsByRRR(dto.Rrr);
        bool paymentSuccessful = paymentDetails != null && paymentDetails.status == "00";

        var status = paymentSuccessful
            ? ApplicationStatuses.AwaitingRecordalProcess
            : ApplicationStatuses.AwaitingPayment;

        var statusMessage = paymentSuccessful
            ? "Payment successful, awaiting recordal process"
            : "CTC application submitted, awaiting payment";

        // Application history
        var ctcHistory = new ApplicationInfo
        {
            id = Guid.NewGuid().ToString(),
            ApplicationType = FormApplicationTypes.CertifiedTrueCopy,
            CurrentStatus = status,
            ApplicationDate = dto.CtcRequestDate ?? DateTime.Now,
            PaymentId = dto.Rrr,
            FieldToChange = "Trademark CTC Application",
            NewValue = "",
            StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = dto.CtcRequestDate ?? DateTime.Now,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = status,
                    Message = statusMessage,
                    User = applicant?.Name,
                    UserId = file.CreatorAccount
                }
            }
        };

        // Recordal info
        var recordal = new PostRegistrationApp
        {
            Id = ctcHistory.id,
            RecordalType = "Trademark CTC Recordal",
            FileNumber = dto.FileId,
            rrr = dto.Rrr,
            FilingDate = (dto.CtcRequestDate ?? DateTime.Now).ToString(),
            RequestedAttachments = dto.AttachmentIds,
            DateTreated = paymentSuccessful ? DateTime.Now.ToString() : ""
        };

        var update = Builders<Filling>.Update
            .Push(f => f.PostRegApplications, recordal)
            .Push(f => f.ApplicationHistory, ctcHistory);

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
            update
        );
        return true;
    }

    public async Task<object?> GetTrademarkCtcDetailsAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return null;

        // Fetch the PostRegApp for CTC
        var ctcApp = file.PostRegApplications?
            .FirstOrDefault(a => a.RecordalType == "Trademark CTC Recordal");

        if (ctcApp == null)
            return null;

        // Fetch the ApplicationHistory for CTC
        var appHistory = file.ApplicationHistory?
            .FirstOrDefault(a => a.id == ctcApp.Id && a.ApplicationType == FormApplicationTypes.CertifiedTrueCopy);

        // Get requested attachments
        var requestedAttachments = new List<object>();
        if (ctcApp.RequestedAttachments != null && file.Attachments != null)
        {
            foreach (var attachmentName in ctcApp.RequestedAttachments)
            {
                var attachment = file.Attachments.FirstOrDefault(a => a.name == attachmentName);
                if (attachment != null)
                {
                    requestedAttachments.Add(new
                    {
                        name = attachment.name,
                        urls = attachment.url
                    });
                }
            }
        }

        var applicantName = file.applicants != null && file.applicants.Count > 0
            ? (file.applicants.Count > 1
                ? file.applicants[0]?.Name + " et al."
                : file.applicants[0]?.Name)
            : "";

        return new
        {
            FileId = file.FileId,
            AppId = ctcApp.Id,
            FilingDate = ctcApp.FilingDate,
            PaymentRRR = ctcApp.rrr,
            Status = appHistory?.CurrentStatus,
            RequestedAttachments = requestedAttachments,
            DateTreated = ctcApp.DateTreated,
            Reason = ctcApp.Reason,
            ApplicantName = applicantName
        };
    }

    public async Task<(bool Success, string Message)> TrademarkCtcDecisionAsync(
        string fileId,
        string appId,
        bool approve,
        string reason,
        string? userId = null)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        AppUser? user = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            user = await _userCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null)
                return (false, "User not found or unauthorized");
        }

        var ctcApp = file.ApplicationHistory?
            .FirstOrDefault(a => a.id == appId && a.ApplicationType == FormApplicationTypes.CertifiedTrueCopy);

        if (ctcApp == null)
            return (false, "No CTC application found");

        var beforeStatus = ctcApp.CurrentStatus;
        var statusEntry = new ApplicationHistory
        {
            Date = DateTime.Now,
            Message = reason,
            beforeStatus = ctcApp.CurrentStatus,
            afterStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected,
            User = user != null ? user.FirstName + " " + user.LastName : "",
            UserId = user?.Id
        };

        ctcApp.StatusHistory ??= new List<ApplicationHistory>();
        ctcApp.StatusHistory.Add(statusEntry);
        ctcApp.CurrentStatus = statusEntry.afterStatus.Value;

        var recordal = file.PostRegApplications?
            .FirstOrDefault(r => r.Id == appId && r.RecordalType == "Trademark CTC Recordal");

        if (recordal != null)
        {
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = reason;
        }

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        if (user != null)
        {
            var performance = new PerformanceDto
            {
                AppUserId = user.Id ?? user.CreatorId,
                AfterStatus = ctcApp.CurrentStatus,
                BeforeStatus = beforeStatus,
                ApplicationType = FormApplicationTypes.CertifiedTrueCopy,
                FileNumber = file.FileId,
                FileType = file.Type,
                Reason = reason,
                Date = DateTime.Now,
                OfficeUnit = Roles.TrademarkCertification
            };
            SavePerformance(performance);
        }

        return (true, approve ? "Trademark CTC approved" : "Trademark CTC refused");
    }

    /// <summary>
    /// Get design attachments data for a specific file to verify/fix image URLs
    /// </summary>
    public async Task<object> GetDesignAttachmentsDataAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();

        if (file == null)
        {
            return new { 
                success = false, 
                message = $"File {fileId} not found in database",
                fileId = fileId
            };
        }

        var designAttachment = file.Attachments?.FirstOrDefault(x => x.name == "designs");

        // Check for alternative attachment names that might contain design images
        var alternativeNames = new[] { "drawings", "representation", "images", "design", "attachment" };
        var alternativeAttachments = file.Attachments?
            .Where(a => alternativeNames.Contains(a.name?.ToLower()) && a.url?.Any() == true)
            .Select(a => new { a.name, urlCount = a.url?.Count ?? 0, urls = a.url })
            .ToList();

        return new
        {
            success = true,
            fileId = file.FileId,
            internalId = file.Id,
            title = file.TitleOfDesign,
            hasDesignAttachment = designAttachment != null,
            designUrls = designAttachment?.url ?? new List<string>(),
            designUrlCount = designAttachment?.url?.Count ?? 0,
            allAttachments = file.Attachments?.Select(a => new { 
                a.name, 
                urlCount = a.url?.Count ?? 0,
                hasUrls = a.url?.Any() == true 
            }).ToList(),
            alternativeImageAttachments = alternativeAttachments
        };
    }

    /// <summary>
    /// Copy image URLs from one attachment to the "designs" attachment
    /// </summary>
    public async Task<object> CopyImagesToDesignsAttachmentAsync(string fileId, string sourceAttachmentName)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();

        if (file == null)
        {
            return new { 
                success = false, 
                message = $"File {fileId} not found in database",
                availableAttachments = new string[0]
            };
        }

        Console.WriteLine($"[CopyImages] Looking for attachment '{sourceAttachmentName}' in file {fileId}");
        Console.WriteLine($"[CopyImages] Available attachments: {string.Join(", ", file.Attachments?.Select(a => a.name) ?? new List<string>())}");

        // Find source attachment
        var sourceAttachment = file.Attachments?.FirstOrDefault(x => 
            x.name?.Equals(sourceAttachmentName, StringComparison.OrdinalIgnoreCase) == true);

        if (sourceAttachment == null)
        {
            return new { 
                success = false, 
                message = $"Source attachment '{sourceAttachmentName}' not found",
                availableAttachments = file.Attachments?.Select(a => a.name).ToList() ?? new List<string>()
            };
        }

        if (sourceAttachment.url == null || !sourceAttachment.url.Any())
        {
            return new { 
                success = false, 
                message = $"Source attachment '{sourceAttachmentName}' has no URLs",
                availableAttachments = file.Attachments?.Select(a => a.name).ToList() ?? new List<string>()
            };
        }

        Console.WriteLine($"[CopyImages] Found {sourceAttachment.url.Count} URLs in '{sourceAttachmentName}'");

        // Find or create designs attachment
        var designsAttachment = file.Attachments?.FirstOrDefault(x => x.name == "designs");

        if (designsAttachment == null)
        {
            Console.WriteLine($"[CopyImages] Creating new 'designs' attachment");
            // Create new designs attachment
            designsAttachment = new AttachmentType
            {
                name = "designs",
                url = new List<string>()
            };

            if (file.Attachments == null)
                file.Attachments = new List<AttachmentType>();

            file.Attachments.Add(designsAttachment);
        }

        // Copy ALL URLs (don't filter them)
        var urlsToCopy = sourceAttachment.url.ToList();

        Console.WriteLine($"[CopyImages] Copying {urlsToCopy.Count} URLs to 'designs'");
        foreach (var url in urlsToCopy)
        {
            Console.WriteLine($"[CopyImages]   - {url}");
        }

        designsAttachment.url = urlsToCopy;

        // Update database
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

        Console.WriteLine($"[CopyImages] Successfully updated database for file {fileId}");

        return new
        {
            success = true,
            message = $"Copied {urlsToCopy.Count} image URL(s) from '{sourceAttachmentName}' to 'designs'",
            fileId = file.FileId,
            sourceAttachment = sourceAttachmentName,
            copiedUrls = urlsToCopy
        };
    }

    /// <summary>
    /// Diagnostic method to check design attachments and image URLs
    /// </summary>
    public async Task<object> DiagnoseDesignImagesAsync(string fileId)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();

        if (file == null)
        {
            return new { 
                success = false, 
                message = "File not found",
                fileId = fileId
            };
        }

        var diagnosis = new
        {
            success = true,
            fileId = file.FileId,
            fileType = file.Type.ToString(),
            title = file.TitleOfDesign ?? file.TitleOfInvention ?? file.TitleOfTradeMark,
            hasAttachments = file.Attachments != null,
            totalAttachments = file.Attachments?.Count ?? 0,
            designAttachment = file.Attachments?.FirstOrDefault(x => x.name == "designs"),
            allAttachmentNames = file.Attachments?.Select(a => a.name).ToList() ?? new List<string>()
        };

        // If there's a design attachment, check each URL
        var designAttachment = file.Attachments?.FirstOrDefault(x => x.name == "designs");
        if (designAttachment != null)
        {
            var urlChecks = new List<object>();

            if (designAttachment.url != null)
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                foreach (var url in designAttachment.url)
                {
                    var urlCheck = new
                    {
                        url = url,
                        isNull = string.IsNullOrWhiteSpace(url),
                        isNullString = url?.Equals("NULL", StringComparison.OrdinalIgnoreCase) ?? false,
                        accessible = false,
                        error = (string)null
                    };

                    if (!string.IsNullOrWhiteSpace(url) && !url.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                            urlCheck = urlCheck with 
                            { 
                                accessible = response.IsSuccessStatusCode,
                                error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                            };
                        }
                        catch (Exception ex)
                        {
                            urlCheck = urlCheck with { error = ex.Message };
                        }
                    }

                    urlChecks.Add(urlCheck);
                }
            }

            return new
            {
                diagnosis.success,
                diagnosis.fileId,
                diagnosis.fileType,
                diagnosis.title,
                diagnosis.hasAttachments,
                diagnosis.totalAttachments,
                hasDesignAttachment = true,
                designUrlCount = designAttachment.url?.Count ?? 0,
                urlChecks = urlChecks,
                diagnosis.allAttachmentNames
            };
        }

        return diagnosis;
    }

public async Task<string?> GetFileIdByFileNumber(string fileNumber)
{
    var file = await _fillingCollection.Find(f => f.FileId == fileNumber).FirstOrDefaultAsync();
    return file?.Id;
}

public async Task<RestorationDto> FileRestorationCost(string fileId, string userId)
{
    _log.LogInformation($"Filing restoration for {fileId}");
    try
    {
        var file = await _fillingCollection.Find(f => f.FileId == fileId).FirstOrDefaultAsync();
        if (file == null || file.FileStatus != ApplicationStatuses.Inactive){
            _log.LogError("File not found or inactive");
            throw new Exception("File is either Active or Not found");
        }
        var user = await _userCollection
       .Find(Builders<AppUser>.Filter.Eq(u => u.Id, userId))
       .FirstOrDefaultAsync();
        if (user is null)
        {
            _log.LogError("User not found for restoration request");
            throw new KeyNotFoundException("User not found");
        }

        var userName = !string.IsNullOrWhiteSpace(user.Name)
            ? user.Name
            : $"{user.FirstName} {user.LastName}".Trim();

        var applicant = file.applicants?.FirstOrDefault();
        if (applicant is null)
        {
            _log.LogError("No applicant data found for restoration request");
            throw new InvalidOperationException("File has no applicant data");
        }

        var applicantName = applicant.Name ?? string.Empty;
        var applicantEmail = applicant.Email ?? string.Empty;
        var applicantPhone = applicant.Phone ?? string.Empty;
        //var cost = _remitaPaymentUtils.GetCost(PaymentTypes.FileRestoration, file.Type, file.FilingCountry ?? "", file.DesignType, null);
        //var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(cost.Item1, cost.Item3, cost.Item2,
            //"Payment for Trademark File Restoration", applicantName, applicantEmail, applicantPhone);
        //if (rrr is null)
        //{
        //    _log.LogError("Failed to Generate RRR");
        //    throw new NullReferenceException();
        //}
        var app = new ApplicationInfo
        {
            ApplicationDate = DateTime.Now,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            ExpiryDate = null,
            LicenseType = "",
            ApplicationType = FormApplicationTypes.Restoration,
            PaymentId = "-",
            StatusHistory =
            [
                new ApplicationHistory
                {
                    Date = DateTime.Now,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.PendingRenewal,
                    Message = "File Restoration initiated, awaiting payment",
                    UserId = userId,
                    User = userName
                }
            ],
        };

        file.FileStatus = ApplicationStatuses.PendingRenewal;

        await _fillingCollection.UpdateOneAsync(
            Builders<Filling>.Filter.Eq(f => f.FileId, fileId),
            Builders<Filling>.Update.Push(f => f.ApplicationHistory, app)
        );
        _log.LogInformation("Restoration application created and awaiting payment.");
        var restore = new RestorationDto
        {
            Applicant = applicantName,
            FileNumber = fileId,
            PaymentId = "-",
            FileStatus = file.FileStatus,
            Cost = "0"
        };
        return restore;
    }
    catch (Exception e)
    {
        _log.LogError(e, "Failed to create restoration application");
        throw;
    }
    }

private async Task<(string, string)?> SignDocument(string designation)
    {
        var signatory = _signatures.Find(s => s.Designation == designation && s.IsActive).FirstOrDefault();
        if (signatory is null)
        {
            _log.LogDebug("Failed to find signatory");
            return null;
        }
        return (signatory.Name, signatory.Id);
    }
}