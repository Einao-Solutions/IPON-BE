using Amazon.Runtime.Internal;
using Azure.Core;
using Bogus.DataSets;
using CloudinaryDotNet.Actions;
using F23.StringSimilarity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
//using MongoDB.Driver.Core.Operations;
using MongoDB.Driver.Linq;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Models;
using patentdesign.pdfs;
using patentdesign.Services.Interface;
using patentdesign.Utils;
using QuestPDF.Fluent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tfunctions.pdfs;
using ZstdSharp.Unsafe;
using static QRCoder.PayloadGenerator;
using static QRCoder.PayloadGenerator.ShadowSocksConfig;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace patentdesign.Services;
public class FileServices
{
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<Counters> _countersCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private static IMongoCollection<TicketInfo> _ticketsCollection;
    private static IMongoCollection<StatusRequests> _statusCollection;
    private static IMongoCollection<UserCreateType> _userCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;
    private static IMongoCollection<PerformanceMarker> _performanceCollection;
    private static IMongoCollection<OppositionType> _oppositionCollection;
    private static IMongoCollection<FileUpdateHistory> _fileUpdateHistoryCollection;
    

    private PaymentUtils _remitaPaymentUtils;
    private MongoClient _mongoClient;
    private FinanceService _financeService;
    private PaymentService _paymentService;
    
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    // private string attachmentBaseUrl = "https://integration.iponigeria.com";
    private string attachmentBaseUrl = "http://localhost:5044";

    //adding log service
    private ILoggerService _log;
    public FileServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, ILoggerService log, PaymentService paymentService)
    {
        var useSandbox = patentDesignDbSettings.Value.UseSandbox;

        string digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;

        MongoClientSettings settings = MongoClientSettings.FromUrl(
            new MongoUrl(digitalOcean)
        );
        settings.SslSettings =
            new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
        _mongoClient = new MongoClient(settings);
        // _mongoClient = new MongoClient(patentDesignDbSettings.Value.ConnectionString);
        var pdDb = _mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _fillingCollection = pdDb.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
        _countersCollection = pdDb.GetCollection<Counters>(patentDesignDbSettings.Value.CountersCollectionName);
        _financeCollection = pdDb.GetCollection<FinanceHistory>(patentDesignDbSettings.Value.FinanceCollectionName);
        _performanceCollection = pdDb.GetCollection<PerformanceMarker>("performance");
        _statusCollection = pdDb.GetCollection<StatusRequests>("statusrequests");
        _oppositionCollection = pdDb.GetCollection<OppositionType>(patentDesignDbSettings.Value.OppositionCollectionName);
        _ticketsCollection = pdDb.GetCollection<TicketInfo>(patentDesignDbSettings.Value.TicketCollectionName);
        _userCollection = pdDb.GetCollection<UserCreateType>(patentDesignDbSettings.Value.UsersCollectionName);
        _attachmentCollection =
            pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
        _remitaPaymentUtils = remitaPaymentUtils;
        _paymentService = paymentService;
        _log = log;
        _fileUpdateHistoryCollection = pdDb.GetCollection<FileUpdateHistory>("FileUpdateHistory");

    }
    
    public async Task<Filling?> GetFileAsync(string id)
    {
        try
        {
            return await _fillingCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error-at-GetSingleFile");
            throw;
        }
    }

    // atomically create file
    public async Task CreateFileAsync(Filling newFile)
    {
        // var session=await _mongoClient.StartSessionAsync();
        // session.StartTransaction();
        // var document=await _countersCollection.Find(Builders<Counters>.Filter.Eq("_id", newFile.Type)).FirstOrDefaultAsync();
        // newFile.FileId = string.Join("/", [newFile.FileId, document.currentNumber.ToString()]);
        newFile.FileId = string.Join("/", [newFile.FileId, Guid.NewGuid().ToString().Split("-")[0]]);
        // var filter = Builders<Counters>.Filter.Eq("_id", newFile.Type);
        await _fillingCollection.InsertOneAsync(newFile);
        // _countersCollection.FindOneAndUpdate(filter, Builders<Counters>.Update.Inc(f=>f.currentNumber, 1));
        // await session.CommitTransactionAsync();
    }

    public async Task<Filling?> ManualUpdate(string fileId, string applicationId, string? userName, string? userId, bool? isCertificate = false)
    {
        var file = _fillingCollection.Find(d => d.Id == fileId).FirstOrDefault();
        var application = file.ApplicationHistory.FirstOrDefault(d => d.id == applicationId);
        var paymentInfo = await _remitaPaymentUtils.GetDetailsByRRR(application.PaymentId);
        if (isCertificate == true)
        {
            var data = await ValidateCertificatePayment(fileId, file.ApplicationHistory[0].CertificatePaymentId, userName, userId);
            return data.data;
        }
        else
        {
            application.StatusHistory.Add(new ApplicationHistory()
            {
                beforeStatus = ApplicationStatuses.AwaitingPayment,
                afterStatus = ApplicationStatuses.AwaitingSearch,
                Date = DateTime.Parse(paymentInfo.paymentDate),
                Message = "Payment Successful, awaiting search",
                User = userName,
                UserId = userId
            });
            application.CurrentStatus = ApplicationStatuses.AwaitingSearch;
        }
        switch (application.ApplicationType)
        {
            case FormApplicationTypes.NewApplication:
                file.FileStatus = ApplicationStatuses.AwaitingSearch;
                var strings = file.FileId.Split("/");
                var max = strings.Length - 1;
                var document = await _countersCollection.Find(Builders<Counters>.Filter.Eq("_id", file.Type))
                    .FirstOrDefaultAsync();
                var newId = string.Join("/", strings.Take(max).Concat(new[] { document.currentNumber.ToString() }));
                var counterfilter = Builders<Counters>.Filter.Eq("_id", file.Type);
                _countersCollection.FindOneAndUpdate(counterfilter, Builders<Counters>.Update.Inc(f => f.currentNumber, 1));
                file.FileId = newId;
                application.ApplicationLetters =
                [
                    ApplicationLetters.NewApplicationCertificateReceipt,
                    ApplicationLetters.NewApplicationAcknowledgement
                ];
                break;
            case FormApplicationTypes.DataUpdate:
                application.ApplicationLetters = [ApplicationLetters.RecordalReceipt, ApplicationLetters.RecordalAck];
                break;
            case FormApplicationTypes.Assignment:
                application.ApplicationLetters =
                    [ApplicationLetters.AssignmentReceipt, ApplicationLetters.AssignmentAck];
                break;
            case FormApplicationTypes.LicenseRenewal:
                application.ApplicationLetters = [ApplicationLetters.RenewalReceipt, ApplicationLetters.RenewalAck];
                break;
            default: break;
        }

        var index = file.ApplicationHistory.FindIndex(f => f.id == application.id);
        file.ApplicationHistory[index] = application;
        await _fillingCollection.FindOneAndReplaceAsync(f => f.Id == file.Id, file);
        var response = await CheckStatusViaOrderId(application.PaymentId);
        var reason = application.ApplicationType == FormApplicationTypes.NewApplication
            ? $"New {file.Type} Application"
            : application.ApplicationType == FormApplicationTypes.LicenseRenewal
                ? $"{file.Type} Renewal Application"
                : application.ApplicationType == FormApplicationTypes.DataUpdate
                    ? "Data Update Application"
                    : application.ApplicationType == FormApplicationTypes.Assignment
                        ? $"{file.Type} Assignment Application"
                        : "";
        saveFinance(response.Item2, reason, applicationId, file.Id, file.applicants[0].country, file.Type,
            file.DesignType, file.PatentType, file.TrademarkType, file.TrademarkClass);
        savePerformance(PerformanceType.Application, application.ApplicationType,
            null, null, DateTime.Now, userName, file.Id, file.Type, file.PatentType, file.DesignType, file.TrademarkType);
        return file;
    }

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
        Console.WriteLine(JsonSerializer.Serialize(res));
        return res;
    }

    public async Task<byte[]> GetTypePublication(DateTime startDate, DateTime endDate, FileTypes type)
    {
        var publicationsData = await _fillingCollection.Find(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x => x.Type, type),
            Builders<Filling>.Filter.Gte(x => x.DateCreated, startDate),
            Builders<Filling>.Filter.Lte(x => x.DateCreated, endDate),
            Builders<Filling>.Filter.In(x => x.FileStatus,
                new List<ApplicationStatuses>() { ApplicationStatuses.Active, ApplicationStatuses.Inactive })
        ])).Project(x => new PublicationType()
        {
            Title = type == FileTypes.Design ? x.TitleOfDesign : type == FileTypes.Patent ? x.TitleOfInvention : "",
            FileId = x.FileId,
            Id = x.Id,
            inventorsCreators = x.Type == FileTypes.Design ? x.DesignCreators : x.Type == FileTypes.Patent ? x.Inventors : new List<ApplicantInfo>() { },
            Date = x.DateCreated,
            Correspondence = x.Correspondence,
            Applicants = x.applicants,
            Images = type == FileTypes.Design ? x.Attachments : null,
            PriorityInfos = type == FileTypes.Patent ? x.PriorityInfo : null
        }).ToListAsync();
        if (type == FileTypes.Design)
        {
            foreach (var dt in publicationsData)
            {
                List<byte[]> image_ = [];
                var urls = dt.Images.FirstOrDefault(x => x.name == "designs").url;
                foreach (var url in urls)
                {
                    image_.Add(await (new HttpClient()).GetByteArrayAsync(url));
                }

                publicationsData[publicationsData.IndexOf(dt)].ImagesUrl = image_;
            }
        }
        var pdfData = new JournalDocument(publicationsData, type, startDate, endDate).GeneratePdf();
        return pdfData;
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
        // print ack and rece
        // add to finance report
        // var financeReport = new FinanceData()
        // {
        //     type = result.Type,
        //     applicationType = data.applicationType,
        //     patentType = result.PatentType,
        //     designType = result.DesignType,
        //     date = DateTime.Now,
        //     applicantCountry = result.applicants[0].country,
        //     paymentID = result.ApplicationHistory.First(x=>x.id==data.applicationId).PaymentId
        // };
        // _=_financeService.AddToDB(financeReport);
        return result;
    }

    public async Task<Filling> SaveDateUpdateApplication(DataUpdateReq data)
    {
        var costData = _remitaPaymentUtils.GetCost(PaymentTypes.Update, data.fileType, "", null, null, data.fieldToChange);
        Console.WriteLine(costData);
        Console.WriteLine(data);
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(costData.Item1, costData.Item3, costData.Item2,
            $"Payment for data update application",
            data.applicantName, data.email, data.phone);
        Console.WriteLine(rrr);
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
                Console.WriteLine($"validating payment for...... {pending.Id}");
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
            ExpiryDate = null, ApplicationType = FormApplicationTypes.LicenseRenewal,
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

    public async Task<Filling?> NewApplicationPayment(UpdateDataType data)
    {
        RemitaResponseClass? response = null;
        if (data.simulate == false)
        {
            var status = true;
            var checker_data = await CheckStatusViaOrderId(data.paymentId);
            status = checker_data.Item1;
            response = checker_data.Item2;
            if (!status) return null;
        }

        var fil = (await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.Id, data.fileId)).Limit(1)
            .ToListAsync()).First();
        if (fil.ApplicationHistory[0].ApplicationLetters.Contains(ApplicationLetters.NewApplicationReceipt))
        {
            return fil;
        }

        var newStatusHistory = new ApplicationHistory()
        {
            beforeStatus = data.beforeStatus,
            afterStatus = data.AfterStatus,
            Date = DateTime.Now,
            Message = data.message,
            User = data.user,
            UserId = data.userId
        };
        List<UpdateDefinition<Filling>> operations =
        [
            Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory",
                newStatusHistory),

            Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", data.AfterStatus)
        ];
        operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, data.AfterStatus));
        var document = await _countersCollection.Find(Builders<Counters>.Filter.Eq("_id", data.FileType))
            .FirstOrDefaultAsync();
        var strings = fil.FileId.Split("/");
        var max = strings.Length - 1;
        var newId = string.Join("/", strings.Take(max).Concat(new[] { document.currentNumber.ToString() }));
        operations.Add(Builders<Filling>.Update.Set(x => x.FileId, newId));
        var counterfilter = Builders<Counters>.Filter.Eq("_id", fil.Type);
        _countersCollection.FindOneAndUpdate(counterfilter, Builders<Counters>.Update.Inc(f => f.currentNumber, 1));
        fil.FileId = newId;
        operations.Add(Builders<Filling>.Update.AddToSetEach(x => x.ApplicationHistory[0].ApplicationLetters,
            [ApplicationLetters.NewApplicationReceipt, ApplicationLetters.NewApplicationAcknowledgement]));
        var filter = Builders<Filling>.Filter.And(Builders<Filling>.Filter.Eq("_id", data.fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == data.applicationId));
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        var result =
            await _fillingCollection.FindOneAndUpdateAsync(filter, Builders<Filling>.Update.Combine(operations),
                options);
        saveFinance(response, $"New {fil.Type.ToString()} Application", fil.ApplicationHistory[0].id,
            fil.Id, fil.applicants[0].country, fil.Type, fil.DesignType, fil.PatentType, fil.TrademarkType, fil.TrademarkClass, null
            );
        savePerformance(PerformanceType.Application, FormApplicationTypes.NewApplication, null, null,
            DateTime.Now, data.user, result.Id, result.Type, result.PatentType, result.DesignType, result.TrademarkType);

        return result;
    }

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
        RemitaResponseClass? remi = null;
        bool paymentSuccessful = false;
        if (data.simulate == false)
        {
            var status = true;
            if (data is
                {
                    AfterStatus: ApplicationStatuses.AwaitingSearch,
                    beforeStatus: ApplicationStatuses.AwaitingPayment,
                    applicationType: FormApplicationTypes.NewApplication or FormApplicationTypes.DataUpdate
                    or FormApplicationTypes.LicenseRenewal
                })
            {
                var response = (await CheckStatusViaOrderId(data.paymentId));
                status = response.Item1;
                remi = response.Item2;
                if (status)
                {
                    paymentSuccessful = true;
                }
            }

            if (!status) return null;
        }
        var newStatusHistory = new ApplicationHistory()
        {
            beforeStatus = data.beforeStatus,
            afterStatus = data.AfterStatus,
            Date = DateTime.Now,
            Message = data.message,
            User = data.user,
            UserId = data.userId
        };
        List<UpdateDefinition<Filling>> operations =
        [
            Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory",
                newStatusHistory),

            Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", data.AfterStatus)

        ];
        if (data.applicationType is FormApplicationTypes.DataUpdate)
        {
            if (data.beforeStatus is ApplicationStatuses.AwaitingPayment
                && data.AfterStatus is ApplicationStatuses.AwaitingSearch)
            {

                var fil = await _fillingCollection.Find(Builders<Filling>.Filter.And(
                     Builders<Filling>.Filter.Eq(a => a.Id, data.fileId),
                     Builders<Filling>.Filter.ElemMatch(a => a.ApplicationHistory, f => f.id == data.applicationId)
                     )).Limit(1).ToListAsync();

                operations.Add(
                    Builders<Filling>.Update.AddToSetEach("ApplicationHistory.$.ApplicationLetters", [ApplicationLetters.RecordalReceipt, ApplicationLetters.RecordalAck]));
                saveFinance(remi, "Data Update Application", data.applicationId, data.fileId, fil[0].applicants[0].country, fil[0].Type, fil[0].DesignType, fil[0].PatentType, fil[0].TrademarkType, fil[0].TrademarkClass);
            }


            if (data.AfterStatus is ApplicationStatuses.Approved)
            {
                var fil = await _fillingCollection.Find(Builders<Filling>.Filter.And(
                    Builders<Filling>.Filter.Eq(a => a.Id, data.fileId),
                    Builders<Filling>.Filter.ElemMatch(a => a.ApplicationHistory, f => f.id == data.applicationId)
                )).Limit(1).ToListAsync();
                var appInfo = fil[0].ApplicationHistory.FirstOrDefault(x => x.id == data.applicationId);
                if (ConstantValues.IsPropertyAttachment(data.fieldToUpdate))
                {
                    var newAtt = JsonSerializer.Deserialize<List<AttachmentType>>(appInfo.NewValue);
                    operations.Add(Builders<Filling>.Update.Set($"Attachments", newAtt));
                }
                else
                {
                    var mapresult = FileUtils.MapObjToType(data.fieldToUpdate.ToLower(), appInfo.NewValue);
                    var fieldToChange = data.fieldToUpdate.Substring(0, 1).ToUpper() + data.fieldToUpdate.Substring(1);
                    if (fieldToChange == "PatentType")
                    {
                        // we need the current file ID so we can set it. if it
                        var strings = data.fileNumber.Split("/");
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

                operations.Add(
                    Builders<Filling>.Update.Push("ApplicationHistory.$.ApplicationLetters",
                        ApplicationLetters.RecordalCertificate));
            }
        }

        if (data.applicationType is FormApplicationTypes.NewApplication)
        {

            operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, data.AfterStatus));
            if (data.AfterStatus is ApplicationStatuses.Active or ApplicationStatuses.Approved)
            {
                var nextDate = getNewExpiryDate(data.dates, data.FileType ?? FileTypes.Design, data.fileId, FormApplicationTypes.NewApplication);

                if (data.applicationType == FormApplicationTypes.NewApplication)
                {
                    if (data.FileType != FileTypes.TradeMark)
                    {
                        operations.Add(Builders<Filling>.Update.AddToSetEach(
                            "ApplicationHistory.$.ApplicationLetters",
                            [
                                ApplicationLetters.NewApplicationAcceptance,
                                ApplicationLetters.NewApplicationCertificate
                            ]));
                    }

                    if (data.FileType is FileTypes.TradeMark)
                    {
                        var rtmNumber = _countersCollection.Find(e => e.id == "RTM").FirstOrDefault().currentNumber;
                        operations.Add(Builders<Filling>.Update.AddToSetEach(
                            "ApplicationHistory.$.ApplicationLetters",
                            [
                                ApplicationLetters.NewApplicationCertificate
                            ]));
                        operations.Add(Builders<Filling>.Update.Set(x => x.RtmNumber, rtmNumber.ToString()));
                        await _countersCollection.FindOneAndUpdateAsync(e => e.id == "RTM",
                            Builders<Counters>.Update.Inc(f => f.currentNumber, 1));
                    }
                }

                operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.ExpiryDate", nextDate));
            }

            if (data.AfterStatus is ApplicationStatuses.RejectedByExaminer || data.AfterStatus is ApplicationStatuses.Rejected)
            {
                var fil = (await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.Id, data.fileId)).Limit(1)
                    .ToListAsync()).First();
                operations.Add(Builders<Filling>.Update.Push(
                    "ApplicationHistory.$.ApplicationLetters", ApplicationLetters.NewApplicationRejection));
            }

        }

        if (data.applicationType is FormApplicationTypes.LicenseRenewal)
        {
            var dt = _fillingCollection.Find(Builders<Filling>.Filter.And(
                Builders<Filling>.Filter.Eq(a => a.Id, data.fileId)
            )).FirstOrDefault();

            if (dt.ApplicationHistory.Select(x => x.ExpiryDate)
                .ToList().Any(y => y < DateOnly.FromDateTime(DateTime.Now)))
            {
                operations.Add(Builders<Filling>.Update.Set(x => x.FileStatus, data.AfterStatus));
            }
            if (data.AfterStatus is ApplicationStatuses.Active or ApplicationStatuses.Approved)
            {
                var nextDate = getNewExpiryDate(data.dates, data.FileType ?? FileTypes.Design, data.fileId, FormApplicationTypes.LicenseRenewal);
                if (data.applicationType == FormApplicationTypes.LicenseRenewal)
                {
                    operations.Add(Builders<Filling>.Update.Push(
                        "ApplicationHistory.$.ApplicationLetters", ApplicationLetters.RenewalCertificate));
                }
                operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.ExpiryDate", nextDate));
            }

            if (data.AfterStatus is ApplicationStatuses.RejectedByExaminer)
            {
                var fil = (await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.Id, data.fileId)).Limit(1).ToListAsync()).First();
                operations.Add(Builders<Filling>.Update.Push(
                    "ApplicationHistory.$.ApplicationLetters", ApplicationLetters.NewApplicationRejection));
            }
        }
        if (data.AfterStatus is ApplicationStatuses.Publication)
        {
            operations.Add(Builders<Filling>.Update.Push("ApplicationHistory.$.ApplicationLetters", ApplicationLetters.NewApplicationAcceptance));
        }

        var filter = Builders<Filling>.Filter.And(Builders<Filling>.Filter.Eq("_id", data.fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == data.applicationId));
        var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };
        var result = await _fillingCollection.FindOneAndUpdateAsync(filter, Builders<Filling>.Update.Combine(operations), options);
        savePerformance(data.beforeStatus == ApplicationStatuses.AwaitingPayment && data.AfterStatus == ApplicationStatuses.AwaitingSearch ?
                PerformanceType.Application : PerformanceType.Staff, data.applicationType, data.beforeStatus, data.AfterStatus,
            DateTime.Now, data.user, result.Id, result.Type, result.PatentType, result.DesignType, result.TrademarkType);
        return result;
    }

    public async Task<PaginatedResponse> GetPaginatedSummaryAsync(int startingIndex, int quantity, SummaryRequestObj filter)
    {
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
        });
        var count = _fillingCollection.CountDocuments(filters);
        var result = await _fillingCollection.Find(filters).Project(projection).Skip(startingIndex).Limit(quantity).ToListAsync();
        return new PaginatedResponse()
        {
            result = result,
            count = count
        };
    }

    public async Task<dynamic> GetCertificatePaymentCost(string fileId)
    {
        var file = await _fillingCollection.Find(Builders<Filling>.Filter.Eq(x => x.FileId, fileId)).FirstOrDefaultAsync();
        if (file == null)
        {
            throw new Exception("File not found");
        }

        // var initPayment = file.ApplicationHistory[0].PaymentId;
        // if (string.IsNullOrEmpty(initPayment))
        // {
        //     throw new Exception("Initial payment not found");
        // }
        var applicant = file.applicants.FirstOrDefault();
        if (applicant == null)
        {
            throw new Exception("Applicant not found");
        }
        
        var data = _remitaPaymentUtils.GetCost(PaymentTypes.TrademarkCertificate, FileTypes.TradeMark, "");
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(data.Item1, data.Item3, data.Item2,
            "Application for issuance of  trademark certificate", applicant.Name, applicant.Email, applicant.Phone);
        Console.WriteLine("cert cost: " + data.Item1);
        Console.WriteLine("service fee:" + data.Item3);
        Console.WriteLine("RRR: " + rrr);
        if (rrr != null)
        {
            _fillingCollection.FindOneAndUpdate(x => x.FileId == fileId,
                Builders<Filling>.Update.Set(t => t.ApplicationHistory[0].CertificatePaymentId, rrr));
        }

        return new
        {
            rrr,
            total = data.Item1,
            applicant.Name,
            fileId
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

        if (newFile.Type is FileTypes.Design)
        {
            var designReps = attachments.Where(x => x.Name is "design1" or "design2" or "design3" or "design4").ToList();
            var designUrls = await UploadAttachment(designReps);
            newFile.Attachments.Add(new AttachmentType()
            {
                name = "designs",
                url = designUrls
            });
            var nov = attachments.FirstOrDefault(x => x.Name == "nov");
            if (nov != null)
            {
                var novurl = await UploadAttachment([nov]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "nov", url = novurl
                });
            }

            var form2 = attachments.FirstOrDefault(x => x.Name == "form2");
            if (form2 != null)
            {
                var form2url = await UploadAttachment([form2]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "form2", url = form2url
                });
            }

            var priorityDoc = attachments.FirstOrDefault(x => x.Name == "pdoc");
            if (priorityDoc != null)
            {
                var priorityDocurl = await UploadAttachment([priorityDoc]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "pdoc", url = priorityDocurl
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
                    name = "representation", url = repurl
                });
            }

            var form2 = attachments.FirstOrDefault(x => x.Name == "form2");
            if (form2 != null)
            {
                var form2url = await UploadAttachment([form2]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "form2", url = form2url
                });
            }

            var other1 = attachments.FirstOrDefault(x => x.Name == "other1");
            if (other1 != null)
            {
                var priorityDocurl = await UploadAttachment([other1]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "other1", url = priorityDocurl
                });
            }

            var other2 = attachments.FirstOrDefault(x => x.Name == "other2");
            if (other2 != null)
            {
                var other2url = await UploadAttachment([other2]);
                newFile.Attachments.Add(new AttachmentType()
                {
                    name = "other2", url = other2url
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
            fileHistory.PaymentId = rrr;
        }
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

    // public async Task<string?> GenerateRemitaPaymentId(string total, string serviceFee, string description, 
    //     string applicantName, string applicantEmail, string applicantNumber, string id) {
    //     // jsonp ({"statuscode" :"025","RRR":"281108917526","status":"Payment Reference generated"})
    //     // return "311092445036";
    //      var _client = new HttpClient();
    //          var orderId =$"IPONMWD{DateTime.Now.Ticks}";
    //          var serviceId = "4019135160";
    //          // var serviceId = id;
    //          var merchantId = "6230040240";
    //          var apiKey = "192753";
    //          using StringContent jsonContent = new(
    //              JsonSerializer.Serialize(new
    //              {
    //                  serviceTypeId= serviceId,
    //                  amount= total,
    //                  // amount= "500",
    //                  orderId,
    //                  payerName= applicantName,
    //                  payerEmail= applicantEmail,
    //                  payerPhone= applicantNumber,
    //                  // payerName= "test teser",
    //                  // payerEmail= "abdulhadih48@gmail.com",
    //                  // payerPhone= "08159730537",
    //                  description,
    //                  lineItems= new []
    //                  {
    //                      new {
    //                          lineItemsId= "itemid1",
    //                          beneficiaryName= "Federal Ministry of Commerce",
    //                          beneficiaryAccount= "0020110961047",
    //                          bankCode= "000",
    //                          beneficiaryAmount= (int.Parse(total) - int.Parse(serviceFee)).ToString(),
    //                          // beneficiaryAmount="250",
    //                          deductFeeFrom= "1",
    //                      },
    //                      new {
    //                          lineItemsId= "itemid2",
    //                          beneficiaryName= "Einao Solutions",
    //                          beneficiaryAccount= "1013590643",
    //                          bankCode= "057",
    //                          // beneficiaryAmount= "250",
    //                          beneficiaryAmount= serviceFee,
    //                          deductFeeFrom= "0",
    //                      }
    //                  }
    //              }
    //              ),
    //              Encoding.UTF8,
    //              "application/json");
    //          _client = new HttpClient();
    //          // var test=merchantId + serviceId +orderId+ "500" + apiKey;
    //          var test=merchantId + serviceId +orderId+ total + apiKey;
    //          var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
    //          var convertedHash=Convert.ToHexString(apiHash).ToLower();
    //          var request = new HttpRequestMessage(HttpMethod.Post,
    //              "https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/merchant/api/paymentinit");
    //          request.Headers.TryAddWithoutValidation("Authorization",$"remitaConsumerKey={merchantId},remitaConsumerToken={convertedHash}");
    //          request.Content = jsonContent;
    //          var response = await _client.SendAsync(request);
    //          var dataMod = await response.Content.ReadAsStringAsync();
    //          Console.WriteLine(dataMod);
    //          try
    //          {
    //              int startIndex = dataMod.IndexOf("{");
    //              int stopIndex = dataMod.IndexOf("}") + 1;
    //              var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(
    //                  dataMod.Substring(startIndex: startIndex, length: stopIndex - startIndex));
    //              Console.WriteLine(dict);
    //              string rrr = dict["RRR"].ToString();
    //              return rrr;
    //          }
    //          catch (Exception e)
    //          {
    //              return null;
    //          }
    //
    //          
    // }
    //
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
            result.ForEach(e => stats_mapped.Add(BsonSerializer.Deserialize<FileStatsRes>(e)));
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

    public async Task<(string, string)> GetRenewalCost(GetRenewalCost data)
    {
        var result = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, data.type, "", data.designType,
            data.patentType, null);
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(result.Item1, result.Item3, result.Item2, $"{data.type} renewal", data.applicantName,
            data.applicantEmail, data.number);
        if (rrr != null)
        {
            _fillingCollection.FindOneAndUpdate(x => x.Id == data.fileId,
                Builders<Filling>.Update.Push(f => f.ApplicationHistory, new ApplicationInfo()
                {
                    PaymentId = rrr,
                    ApplicationType = FormApplicationTypes.LicenseRenewal,
                    ApplicationDate = DateTime.Now,
                    CurrentStatus = ApplicationStatuses.AwaitingPayment,
                    LicenseType = "Renewal",
                    StatusHistory =
                    [
                        new ApplicationHistory()
                        {
                            beforeStatus = ApplicationStatuses.None,
                            afterStatus = ApplicationStatuses.AwaitingPayment,
                            Message = "Remita ID generated, awaiting Payment",
                            Date = DateTime.Now,
                            User = data.userName,
                            UserId = data.userId
                        }
                    ]
                }));
        }
        return (rrr, result.Item1);
    }
    public async Task<RenewalAppDto> PatentRenewalCost(string fileId, FileTypes fileType)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            throw new Exception("File not found");

        if (file.Type != FileTypes.Patent)
            throw new Exception("This method is strictly for patent files.");

        // --- Patent logic below ---
        // Only for PCT/Conventional: use FirstPriorityInfo
        DateOnly? baseDate = null;
        if (file.PatentType == PatentTypes.PCT || file.PatentType == PatentTypes.Conventional)
        {
            if (file.FirstPriorityInfo != null && file.FirstPriorityInfo.Count > 0)
            {
                baseDate = file.FirstPriorityInfo
                    .Where(x => !string.IsNullOrWhiteSpace(x.Date))
                    .Select(x => DateOnly.Parse(x.Date))
                    .Min();
            }
            else
            {
                throw new Exception("No valid First Priority Date found for this patent.");
            }
        }
        else
        {
            // For Non-Conventional, use FilingDate or DateCreated
            if (file.FilingDate != null)
                baseDate = DateOnly.FromDateTime(file.FilingDate.Value);
            else
                baseDate = DateOnly.FromDateTime(file.DateCreated);
        }

        // Find the most recent renewal (if any)
        DateOnly? lastRenewalDate = null;
        if (file.ApplicationHistory != null)
        {
            var lastRenewal = file.ApplicationHistory
                .Where(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal)
                .OrderByDescending(a => a.ApplicationDate)
                .FirstOrDefault();
            if (lastRenewal != null)
                lastRenewalDate = DateOnly.FromDateTime(lastRenewal.ApplicationDate);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // --- Anniversary logic for first-time renewal ---
        if (lastRenewalDate == null)
        {
            var firstAnniversary = baseDate.Value.AddYears(1);
            if (today < firstAnniversary)
            {
                throw new Exception($"Renewal can only begin on or after the first anniversary: {firstAnniversary:yyyy-MM-dd}");
            }
        }

        // Use last renewal date if available, else base date
        var renewalStartDate = lastRenewalDate ?? baseDate.Value;

        // Calculate missed years
        //int missedYears = today.Year - renewalStartDate.Year;
        //if (today > renewalStartDate.AddYears(missedYears)) missedYears++;
        //if (missedYears < 1) missedYears = 1;

        int missedYears = (today.DayOfYear >= renewalStartDate.DayOfYear)
        ? today.Year - renewalStartDate.Year
        : today.Year - renewalStartDate.Year - 1;
        if (missedYears < 1) missedYears = 1;

        // Get normal and late renewal costs
        var (normalFeeStr, serviceId, serviceFeeStr) = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, file.Type, file.FilingCountry ?? "", file.DesignType, file.PatentType);
        var (lateFeeStr, _, lateServiceFeeStr) = _remitaPaymentUtils.GetCost(PaymentTypes.PatentLateRenewal, file.Type, file.FilingCountry ?? "", file.DesignType, file.PatentType);

        int normalFee = int.TryParse(normalFeeStr, out var nf) ? nf : 0;
        int lateFee = int.TryParse(lateFeeStr, out var lf) ? lf : 0;
        int serviceFee = int.TryParse(serviceFeeStr, out var sf) ? sf : 0;
        int lateServiceFee = int.TryParse(lateServiceFeeStr, out var lsf) ? lsf : 0;

        bool isFirstRenewal = lastRenewalDate == null;
        bool isWithinFirst6Months = false;
        if (isFirstRenewal)
        {
            var baseDateTime = baseDate.Value.ToDateTime(TimeOnly.MinValue);
            var monthsSinceBase = ((today.Year - baseDate.Value.Year) * 12) + today.Month - baseDate.Value.Month;
            var windowStart = new DateOnly(today.Year, baseDate.Value.Month, baseDate.Value.Day);
            var windowEnd = windowStart.AddMonths(6).AddDays(-1);
            isWithinFirst6Months = today >= windowStart && today <= windowEnd;
        }

        int totalNormal = 0;
        int totalLate = 0;
        int totalService = 0;
        int lateYearsCount = 0;

        if (isFirstRenewal && isWithinFirst6Months)
        {
            // Multiply normal fee by missed years, no late fee
            totalNormal = missedYears * normalFee;
            totalLate = 0;
            totalService = missedYears * serviceFee;
            lateYearsCount = 0;
        }
        else
        {
            // For all missed years, charge both normal and late fee
            totalNormal = missedYears * normalFee;
            totalLate = missedYears * lateFee;
            totalService = missedYears * (serviceFee + lateServiceFee);
            lateYearsCount = missedYears;
        }

        int total = totalNormal + totalLate;

        // Generate RRR
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
            total.ToString(), totalService.ToString(), serviceId, $"{file.Type} renewal",
            file.applicants.FirstOrDefault()?.Name ?? "",
            file.applicants.FirstOrDefault()?.Email ?? "",
            file.applicants.FirstOrDefault()?.Phone ?? "");

        return new RenewalAppDto
        {
            Cost = total.ToString(),
            rrr = rrr,
            FileId = fileId,
            IsLateRenewal = lateYearsCount > 0,
            LateRenewalCost = totalLate > 0 ? totalLate.ToString() : null,
            ServiceFee = totalService.ToString(),
            MissedYearsCount = missedYears,
            LateYearsCount = lateYearsCount,
            FileTypes = file.Type,
        };
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

    public async Task<BatchRenewRes> GetBatchRenewalInfo(BatchRenewReq data)
    {
        List<BatchRenewData> resData = [];
        var filters = new List<FilterDefinition<Filling>>
         {
             Builders<Filling>.Filter.Eq(x => x.CreatorAccount, data.userId),
             Builders<Filling>.Filter.Eq(x => x.FileStatus, ApplicationStatuses.Inactive)
         };
        var projection = Builders<Filling>.Projection.Expression(x => new BatchReqSummary()
        {
            FileNumber = x.FileId,
            Id = x.Id,
            Title = x.Type == FileTypes.Patent ? x.TitleOfInvention : x.TitleOfDesign,
            Type = x.Type,
            DesignType = x.DesignType,
            PatentType = x.PatentType,
            Number = x.Correspondence.phone,
            Email = x.Correspondence.email,
            ApplicantNames = x.applicants.Select(y => y.Name).ToList(),
        });
        long count = 0;
        count = _fillingCollection.CountDocuments(Builders<Filling>.Filter.And(filters));
        var fileResults = await _fillingCollection.Find(Builders<Filling>.Filter.And(
            filters
        )).Project(projection).Limit(10).Skip(data.skip ?? 0).ToListAsync();

        foreach (var fileInfo in fileResults)
        {
            // get cost and RRR
            var rrr_cost = await GetRenewalCost(new GetRenewalCost()
            {
                number = fileInfo.Number,
                designType = fileInfo.DesignType,
                type = fileInfo.Type,
                applicantName = fileInfo.ApplicantNames.Count > 1
                    ? fileInfo.ApplicantNames[0] + " et al"
                    : fileInfo.ApplicantNames[0],
                applicantEmail = fileInfo.Email,
                patentType = fileInfo.PatentType
            });
            resData.Add(new BatchRenewData()
            {
                cost = rrr_cost.Item2,
                paymentId = rrr_cost.Item1,
                fileNumber = fileInfo.FileNumber,
                fileTitle = fileInfo.Title,
                id = fileInfo.Id,
                fileType = fileInfo.Type,
                title = fileInfo.Type == FileTypes.Design ? "Design Renewal" : "Patent Renewal",
                applicant = fileInfo.ApplicantNames.Count > 1
                    ? fileInfo.ApplicantNames[0] + " et al"
                    : fileInfo.ApplicantNames[0],
            });
        }

        return new BatchRenewRes()
        {
            total = count,
            data = resData
        };
    }

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
        if (req is { beforeStatus: ApplicationStatuses.RejectedByExaminer or ApplicationStatuses.Rejected,
            applicationType: FormApplicationTypes.NewApplication or FormApplicationTypes.LicenseRenewal })
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
        savePerformance(PerformanceType.Staff, FormApplicationTypes.None, req.beforeStatus, req.afterStatus,
            DateTime.Now, req.userName, result.Id, result.Type, result.PatentType, result.DesignType, result.TrademarkType);
        return result;
    }

    private void savePerformance(PerformanceType perType, FormApplicationTypes? type, ApplicationStatuses? reqBeforeStatus, ApplicationStatuses? reqAfterStatus,
        DateTime now, string reqUserName, string resultId, FileTypes resultType, PatentTypes? resultPatentType, DesignTypes? resultDesignType, TradeMarkType? resultTrademarkType)
    {
        _performanceCollection.InsertOne(
            new PerformanceMarker()
            {
                beforeStatus = reqBeforeStatus,
                afterStatus = reqAfterStatus,
                ApplicationType = type,
                fileType = resultType,
                designType = resultDesignType,
                patentType = resultPatentType,
                tradeMarkType = resultTrademarkType,
                Date = now,
                fileId = resultId,
                user = reqUserName,
                Type = perType
            }
        );
    }

    public async Task<(string?, string)> GenerateOppositionRRR(PaymentTypes type, string description, string name, string email, string number)
    {
        var details = await _remitaPaymentUtils.GenerateOppositionID(type, description, name, email, number);
        return details;
    }

    public async Task<object?> GetTrademarkPublication(string? text, int? index = 0, int? quantity = 10)
    {
        var titleFilter = text == null ? Builders<Filling>.Filter.Empty : Builders<Filling>.Filter.Regex(x => x.TitleOfTradeMark, new BsonRegularExpression(text, "i"));
        var result = await _fillingCollection.Find(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x=>x.Type, FileTypes.TradeMark),
             Builders<Filling>.Filter.Or([
             Builders<Filling>.Filter.Eq(x => x.ApplicationHistory[0].CurrentStatus, ApplicationStatuses.Publication),
             ]),
             titleFilter
        ])).Project(x => new
        {
            title = x.TitleOfTradeMark,
            tradeClass = x.TrademarkClass,
            image = x.Attachments.FirstOrDefault(att => att.name == "representation") != null ? x.Attachments.FirstOrDefault(att => att.name == "representation").url[0] : null,
            fileId = x.FileId,
            id = x.Id,
            applicant = x.applicants.Count > 1 ? x.applicants[0].Name + "et al." : x.applicants[0].Name,
            date = x.DateCreated
        }).Limit(quantity).Skip(index).ToListAsync();
        var counter = _fillingCollection.CountDocuments(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x=>x.Type, FileTypes.TradeMark),
             Builders<Filling>.Filter.Eq(x => x.ApplicationHistory[0].CurrentStatus, ApplicationStatuses.Publication),
             titleFilter
        ]));
        return new { result = result, count = counter };
    }

    public async Task PaidButNotReflecting()
    {
        var allAwaiting = await _fillingCollection.Find(x => x.FileStatus == ApplicationStatuses.AwaitingPayment).Skip(10).ToListAsync();
        var recent = allAwaiting.Where(x => x.DateCreated >= DateTime.Parse("2024-11-1")).ToList();
        Console.WriteLine($"the total of recent awaiting payment is: {recent.Count}");
        foreach (var filling in recent)
        {
            Console.WriteLine($"{recent.IndexOf(filling) + 1} checking if payment is valid is: {filling.Id}");
            var status = await CheckStatusViaOrderId(filling.ApplicationHistory[0].PaymentId);
            if (status.Item1)
            {
                Console.WriteLine("updating to awaiting search");
                await NewApplicationPayment(
                    new UpdateDataType()
                    {
                        simulate = false,
                        beforeStatus = ApplicationStatuses.AwaitingPayment,
                        AfterStatus = ApplicationStatuses.AwaitingSearch,
                        title = filling.Type switch
                        {
                            FileTypes.Design => filling.TitleOfDesign,
                            FileTypes.Patent => filling.TitleOfInvention,
                            _ => filling.TitleOfTradeMark
                        },
                        applicantName = filling.applicants.Count > 1 ? filling.applicants[0].Name + " et al." : filling.applicants[0].Name,
                        amount = status.Item2.amount.ToString(),
                        paymentId = filling.ApplicationHistory[0].PaymentId,
                        message = "Payment successful, awaiting search",
                        user = "Auto",
                        userId = "Auto",
                        fileId = filling.Id,
                        applicationId = filling.ApplicationHistory[0].id,
                        FileType = filling.Type
                    }
                    );
            }
        }
    }

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
            return _fillingCollection.Find(d => d.Id == fileId).FirstOrDefault();
        }
        var result = _fillingCollection.Find(d => d.Id == fileId)
            .Project(d => d.ApplicationHistory.FirstOrDefault(f => f.id == applicationId)).FirstOrDefault();
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
        var newLetters = file.ApplicationHistory[0].ApplicationLetters;
        var result = await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, fileId),
            Builders<Filling>.Update.Combine([
                Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].ApplicationLetters, newLetters),
                     Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].CertificatePaymentId, rrr),
                     Builders<Filling>.Update.Set(x => x.ApplicationHistory[0].CurrentStatus,
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
        savePerformance(PerformanceType.Application, FormApplicationTypes.NewApplication,
            ApplicationStatuses.AwaitingCertification, ApplicationStatuses.AwaitingCertificateConfirmation, DateTime.Now, userName, file.Id, file.Type, file.PatentType, file.DesignType, file.TrademarkType);


        return new ValCert()
        {
            data = result
        };
    }

    public async Task<Filling?> ReAssign(ReAssignType data)
    {
        try
        {
            Console.WriteLine($"[ReAssign] Attempting to reassign fileId: {data.fileId}");

            var filter = Builders<Filling>.Filter.Eq(x => x.FileId, data.fileId);
            var update = Builders<Filling>.Update.Combine([
                Builders<Filling>.Update.Set(x => x.CreatorAccount, data.newOwner),
                Builders<Filling>.Update.Set(x => x.Correspondence, data.newCorrespondence),
                Builders<Filling>.Update.Push(x => x.ApplicationHistory, new ApplicationInfo()
                {
                    ApplicationType = FormApplicationTypes.Ownership,
                    ApplicationDate = DateTime.Now,
                    CurrentStatus = ApplicationStatuses.AutoApproved,
                    StatusHistory =
                    [
                        new ApplicationHistory()
                        {
                            Date = DateTime.Now,
                            beforeStatus = ApplicationStatuses.AwaitingConfirmation,
                            afterStatus = ApplicationStatuses.AutoApproved,
                            User = data.userName,
                            UserId = data?.userId,
                            Message =
                                $"Correspondence information changed from: \n Name:{data.oldCorrespondence.name}, Address:{data.oldCorrespondence.address}, " +
                                $"State:{data.oldCorrespondence.state}, number: {data.oldCorrespondence.phone}, email: {data.oldCorrespondence.email} \n to" +
                                $" \n Name:{data.newCorrespondence.name}, Address:{data.newCorrespondence.address}, State:{data.newCorrespondence.state}, number: {data.newCorrespondence.phone}, email: {data.newCorrespondence.email}. \n previous owner: {data.oldName} with id: {data.oldId}"
                        }
                    ]
                })
            ]);

            var options = new FindOneAndUpdateOptions<Filling>
            {
                ReturnDocument = ReturnDocument.After
            };
            
            var result = await _fillingCollection.FindOneAndUpdateAsync(filter, update, options);

            if (result == null)
            {
                Console.WriteLine($"[ReAssign] No document found with fileId: {data.fileId} or update failed.");
            }
            else
            {
                Console.WriteLine($"[ReAssign] Update successful for fileId: {data.fileId}");
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReAssign] Error occurred while updating fileId: {data.fileId}. Exception: {ex.Message}");
            return null;
        }
    }


    private async Task<string?> saveAck(OtherPaymentModel data) {
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

    public async Task<Filling?> UpdateCorThis(string id, string userId)
    {
        var corr = await _userCollection.Find(d => d.id == userId).Project(x => x.DefaultCorrespondence).FirstOrDefaultAsync();
        var updated = await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, id),
            Builders<Filling>.Update.Set(d => d.Correspondence, corr), new FindOneAndUpdateOptions<Filling>()
            {
                ReturnDocument = ReturnDocument.After
            });
        return updated;
    }

    public async Task<Filling?> UpdateCorAll(string id, string userId, string creatorAccount)
    {
        var filter = Builders<Filling>.Filter;
        var defaultdata = await _userCollection.Find(d => d.id == userId).Project(x => x.DefaultCorrespondence).FirstOrDefaultAsync();
        await _fillingCollection.UpdateManyAsync(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x => x.CreatorAccount, creatorAccount),
             Builders<Filling>.Filter.Or([
                  filter.Eq(r=> r.Correspondence, null),
                  filter.Eq(r=>r.Correspondence.name, "null"),
                  filter.Eq(r=>r.Correspondence.address, "null"),
                  filter.Eq(r=>r.Correspondence.email, "null"),
                  filter.Eq(r=>r.Correspondence.phone, "null"),
                  filter.Eq(r=>r.Correspondence.state, "null"),
                  filter.Eq(r=>r.Correspondence.name, "NULL"),
                  filter.Eq(r=>r.Correspondence.address, "NULL"),
                  filter.Eq(r=>r.Correspondence.email, "NULL"),
                  filter.Eq(r=>r.Correspondence.phone, "NULL"),
                  filter.Eq(r=>r.Correspondence.state, "NULL"),
                  filter.Eq(r=>r.Correspondence.name, "-"),
                  filter.Eq(r=>r.Correspondence.address, "-" ),
                  filter.Eq(r=>r.Correspondence.email,"-"),
                  filter.Eq(r=>r.Correspondence.phone, "-"),
                  filter.Eq(r=> r.Correspondence.state,"-"),
                  filter.Eq(r=>r.Correspondence.name, null ),
                  filter.Eq(r=>r.Correspondence.address,null),
                  filter.Eq(r=>r.Correspondence.email, null),
                  filter.Eq(r=>r.Correspondence.phone, null),
                  filter.Eq(r=>r.Correspondence.state , null),
             ])
        ]), Builders<Filling>.Update.Set(d => d.Correspondence, defaultdata));
        var current = await _fillingCollection.Find(d => d.Id == id).FirstOrDefaultAsync();
        return current;
    }

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
            _log.LogError(up, "Error-at-StatusSearchCost");
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
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.FileWithdrawal, fileType, "", null, null, null);

            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();

            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                Console.WriteLine("No file or applicants found.");
                return null;
            }

            var applicant = fileInfo.applicants[0];

            var paymentId = await _remitaPaymentUtils.GenerateFileWithdrawalRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "File Withdrawal",
                applicant.Name, applicant.Email, applicant.Phone);

            var fileWithdrawalCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
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
                PriorityInfo = fileInfo.PriorityInfo,
                FirstPriorityInfo = fileInfo.FirstPriorityInfo,
                CorrespondenceNationality = fileInfo.Correspondence?.Nationality,
                CorrespondenceState = fileInfo.Correspondence?.state,
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
                FieldToChange = "Registered Users Application",
                NewValue = "",
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

            // Update post reg
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;
            app.CurrentStatus = ApplicationStatuses.Approved;

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
                data.Item1, data.Item3, data.Item2, "Recordal Application",
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
    public async Task<bool> NewMergerApplication(MergerApplicationDto mergerApp)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, mergerApp.FileId))
            .FirstOrDefaultAsync();
        if (file == null) return false;

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
        try
        {
            var applicant = file.applicants.FirstOrDefault();


            // Create ApplicationInfo for ApplicationHistory
            var mergerHistory = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.Merger,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.Now,
                PaymentId = mergerApp.rrr,
                FieldToChange = "Merger Application",
                NewValue = "",
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Message = "Merger application submitted",
                        User = applicant.Name,
                        UserId = file.CreatorAccount
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
                Address = mergerApp.Address,
                Nationality = mergerApp.Nationality,
                Name = mergerApp.Name,
                Email = mergerApp.Email,
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

            // Update Post reg app
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;

            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status in App History
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;

            app.CurrentStatus = ApplicationStatuses.Approved;

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
            Name = recordal.Name,
            Email = recordal.Email,
            Address = recordal.Address,
            Nationality = recordal.Nationality,
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
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.ChangeDataRecordal, fileType, "", null, null, null);

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

            var changeCost = new RecordalDto
            {
                Amount = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                ApplicantName = applicant.Name,
                TrademarkClass = fileInfo.TrademarkClass,
                DataChangeType = changeType
            };

            return changeCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-ChangeRecordalDataCost");
            throw;
        }
    }
    public async Task<bool> ChangeDataRecordal(ChangeDataRecordalDto newData)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, newData.FileId))
            .FirstOrDefaultAsync();

        if (file == null) return false;

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
            var applicant = file.applicants.FirstOrDefault();
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
                    ? "Change of Applicant Name"
                    : "Change of Applicant Address",
                NewValue = newData.ChangeType == "Name"
                    ? newData.NewName
                    : newData.NewAddress,
                StatusHistory = new List<ApplicationHistory>
            {
                new ApplicationHistory
                {
                    Date = DateTime.Now,
                    beforeStatus = ApplicationStatuses.None,
                    afterStatus = ApplicationStatuses.AwaitingPayment,
                    Message = "Change Data",
                    User = applicant?.Name,
                    UserId = file.CreatorAccount
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
                Name = newData.NewName,
                Address = newData.NewAddress
            };

            var update = Builders<Filling>.Update
                .Push(f => f.PostRegApplications, recordal)
                .Push(f => f.ApplicationHistory, appHistory);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );
        }
        catch (Exception ex)
        {
            throw new Exception("Error during ChangeDataRecordal", ex);
        }

        return true;
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
            NewName = recordal?.Name,
            NewAddress = recordal?.Address,
            documentUrl = recordal?.documentUrl
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
                FilingDate =  f.FilingDate.ToString() ?? f.ApplicationHistory[0].ApplicationDate.ToString(),                TradeMarkLogo = f.TrademarkLogo,
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
                WithdrawalRequestDate = f.WithdrawalRequestDate
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
    public async Task<bool> ApproveChangeDataRecordal(TreatRecordalDto recordalApp)
    {
        try
        {
            Console.WriteLine($"Approving data change for fileId: {recordalApp.fileId}, appId: {recordalApp.appId}");
            var file = await _fillingCollection
                 .Find(Builders<Filling>.Filter.Eq(f => f.FileId, recordalApp.fileId))
                 .FirstOrDefaultAsync();

            if (file == null) return false;

            // Update post reg
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;
            app.CurrentStatus = ApplicationStatuses.Approved;

            // Update Applicant
            var applicant = file.applicants?.FirstOrDefault();
            if (applicant == null) return false;

            if (recordal.Name == null)
            {
                applicant.Address = recordal.Address;
            }
            else
            {
                applicant.Name = recordal.Name;

            }

            var update = Builders<Filling>.Update
                .Set(f => f.PostRegApplications, file.PostRegApplications)
                .Set(f => f.ApplicationHistory, file.ApplicationHistory)
                .Set(f => f.applicants, file.applicants);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );

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

            // Find recordal
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;

            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;
            // Update Application Status
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;

            app.CurrentStatus = ApplicationStatuses.Rejected;

            // Now write the full modified lists back to the DB
            var update = Builders<Filling>.Update
                .Set(f => f.PostRegApplications, file.PostRegApplications)
                .Set(f => f.ApplicationHistory, file.ApplicationHistory);

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.Id, file.Id),
                update
            );

            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in DenyRecordal: {ex.Message}");
            return false;
        }
    }
    public async Task<RenewalAppDto> RenewalCost(string fileId, FileTypes fileType)
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
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.LicenseRenew, fileType, "", null, null, null);
            Console.WriteLine("Renewal Cost: " + data.Item1);
            Console.WriteLine("Service Fee: " + data.Item3);
            if (fileInfo.FileStatus == ApplicationStatuses.Inactive)
            {
                data = _remitaPaymentUtils.GetCost(PaymentTypes.LateRenewal, fileType, "", null, null, null);
                Console.WriteLine("Renewal Cost: " + data.Item1);
                Console.WriteLine("Service Fee: " + data.Item3);
            }
            var applicant = fileInfo.applicants[0];
            // Remove special characters from applicantName
            var sanitizedApplicantName = Regex.Replace(applicant.Name ?? "", @"[^a-zA-Z0-9\s]", "");

            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Recordal Application",
                sanitizedApplicantName, applicant.Email, applicant.Phone);

            var renewalCost = new RenewalAppDto
            {
                Cost = data.Item1,
                rrr = paymentId,
                FileId = fileId,
                ServiceFee = data.Item3,
                IsLateRenewal = fileInfo.FileStatus == ApplicationStatuses.Inactive,
            };

            return renewalCost;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in RenewalApplication: {ex.Message}");
            throw;
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

            // Ensure status remains Inactive if it was Inactive
            if (file.FileStatus == ApplicationStatuses.Inactive)
            {
                update = update.Set(f => f.FileStatus, ApplicationStatuses.Active);
            }

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
        var file = await _fillingCollection.Find(x => x.FileId == dto.FileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found.");

        // Check if publication has already been done
        if (file.PublicationDate != null)
            return (false, "Publication has already been done on the file.");

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
                    User = applicant.Name,
                    UserId = file.CreatorAccount
                }
            }
        };

        file.ApplicationHistory ??= new List<ApplicationInfo>();
        file.ApplicationHistory.Add(publicationStatusUpdateHistory);

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);
        return (true, "Publication status updated successfully.");
    }

    public async Task<(bool Success, string Message)> WithdrawalRequestAsync(WithdrawalRequestDto dto)
    {
        var file = await _fillingCollection.Find(x => x.FileId == dto.FileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found.");

        if (file.WithdrawalDate != null)
            return (false, "Withdrawal has already been done on the file.");

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
                User = applicant?.Name,
                UserId = file.CreatorAccount
            }
        }
        };

        file.ApplicationHistory ??= new List<ApplicationInfo>();
        file.ApplicationHistory.Add(withdrawalHistory);

        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);
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

    public async Task<(bool Success, string Message)> WithdrawalRequestDecisionAsync(string fileId, bool approve, string? comment)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

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
            User = applicant?.Name,
            UserId = file.CreatorAccount
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

    public async Task<(bool Success, string Message)> PublicationStatusDecisionAsync(string fileId, bool approve, string? comment)
    {
        var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
        if (file == null)
            return (false, "File not found");

        var applicant = file.applicants.FirstOrDefault();

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
            User = applicant.Name,
            UserId = file.CreatorAccount
        };

        file.PublicationReason = comment;
        // Add new status history
        publicationApp.StatusHistory.Add(newStatus);

        // Update current status
        publicationApp.CurrentStatus = approve ? ApplicationStatuses.Approved : ApplicationStatuses.Rejected;

        // If approved, update file status
        if (approve)
            file.FileStatus = ApplicationStatuses.AwaitingCertification;

        // Save changes
        await _fillingCollection.ReplaceOneAsync(x => x.Id == file.Id, file);

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
                        User = applicant.Name,
                        UserId = file.CreatorAccount
                    }
                }
            };
            //Create new registered user
            var newAssignee = new Assignee
            {
                Name = assignmentApp.AssigneeName,
                Email = assignmentApp.AssigneeEmail,
                Phone = assignmentApp.AssigneePhone,
                Address = assignmentApp.AssigneeAddress,
                Nationality = assignmentApp.AssigneeNationality,
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
                Name = assignmentApp.AssigneeName,
                Email = assignmentApp.AssigneeEmail,
                Phone = assignmentApp.AssigneePhone,
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
    public async Task<Assignee> GetAssignmentApplication(string fileId, string appId)
    {
        var file = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
            .FirstOrDefaultAsync();
        var assignee = file.Assignees?.FirstOrDefault(a => a.Id == appId);
        if (assignee == null) return null;

        var assigneeDetails = new AssignmentAppDto
        {
            FileId = fileId,
            rrr = assignee.rrr,
            AssigneeName = assignee.Name,
            AssigneeAddress = assignee.Address,
            AuthorizationLetterUrl = assignee.AuthorizationLetterUrl,
            AssignmentDeedUrl = assignee.AssignmentDeedUrl,
        };

        return assignee;

    }
    public async Task<bool> ApproveAssignment(TreatRecordalDto recordalApp)
    {
        try
        {
            Console.WriteLine($"Approving assignment for fileId: {recordalApp.fileId}, appId: {recordalApp.appId}");
            var file = await _fillingCollection
                 .Find(Builders<Filling>.Filter.Eq(f => f.FileId, recordalApp.fileId))
                 .FirstOrDefaultAsync();
            if (file == null) return false;

            // Update post reg
            var recordal = file.PostRegApplications?.FirstOrDefault(p => p.Id == recordalApp.appId);
            if (recordal == null) return false;
            recordal.DateTreated = DateTime.Now.ToString();
            recordal.Reason = recordalApp.reason;

            // Update Application Status
            var app = file.ApplicationHistory?.FirstOrDefault(p => p.id == recordalApp.appId);
            if (app == null) return false;
            app.CurrentStatus = ApplicationStatuses.Approved;
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
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error in ApproveAssignment: {ex.Message}");
            Console.WriteLine(ex);
            return false;
        }
    }
    public async Task<ClericalUpdateDto> GetClericalUpdateCost(string fileId, FileTypes fileType, string updateType)
    {
        try
        {
            var fileInfo = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, fileId))
                .FirstOrDefaultAsync();
            
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.ClericalUpdate, fileType, "", null, null, null);
            
            if (fileInfo == null || fileInfo.applicants == null || fileInfo.applicants.Count == 0)
            {
                throw new Exception("File not found or no applicants available.");
            }
            
            var applicant = fileInfo.applicants[0];
            if (applicant == null)throw new Exception("No applicant found for the file.");
            string paymentId = null;
            if (fileInfo.FileStatus != ApplicationStatuses.AwaitingSearch)
            {
                paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                    data.Item1, data.Item3, data.Item2, "Clerical Update",
                    applicant.Name, applicant.Email, applicant.Phone);
            }
            else
            {
                paymentId = "Free";
                
            }

            
            Console.WriteLine("amount: " + data.Item1);
            
            var repAttachment = fileInfo?.Attachments
                    .FirstOrDefault(a => a.name == "representation" && a.url != null && a.url.Count > 0);

            var updateCost = new ClericalUpdateDto
            {
                Cost = data.Item1,
                PaymentRRR = paymentId,
                FileStatus = fileInfo.FileStatus,
                FileId = fileId,
                FileTitle = fileInfo.TitleOfTradeMark ?? "",
                FileType = fileInfo.Type,
                ApplicantName = applicant.Name,
                UpdateType = updateType,
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
                Disclaimer = fileInfo.TrademarkDisclaimer
                
            };
            return updateCost;
        }
        catch (Exception up)
        {
            //log error
            _log.LogError(up, "Error-at-ClericalUpdate Cost");
            throw;
        }
    }
    public async Task<bool> ClericalUpdate(ClericalUpdateDto updateData) {
        try
        {
            Console.WriteLine("Finding file: " + updateData.FileId);
            Console.WriteLine(JsonSerializer.Serialize(updateData));
            var file = await _fillingCollection
                .Find(Builders<Filling>.Filter.Eq(f => f.FileId, updateData.FileId))
                .FirstOrDefaultAsync();

            if (file == null) throw new Exception("File Not Found");
            Console.WriteLine("File found: " + file.FileId);
            var applicant = file.applicants?.FirstOrDefault();
            Console.WriteLine("File Status: "+ file.FileStatus);

            if (file.FileStatus != ApplicationStatuses.AwaitingSearch)
            {
                if (updateData.PaymentRRR != null)
                {
                    RemitaResponseClass payDetails = await _remitaPaymentUtils.GetDetailsByRRR(updateData.PaymentRRR);
                    // if (payDetails == null || payDetails.status != "00")
                    // {
                    //     throw new Exception($"Payment Not Found or Invalid RRR, {updateData.PaymentRRR}");
                    // }
                    Console.WriteLine(payDetails);
                    var payment = new PaymentRecord
                    {
                        PaymentType = "Clerical Update",
                        Date = DateTime.Now,
                        FileId = file.FileId,
                        RemitaResponse = payDetails
                    };
                    Console.WriteLine(payment);
                    await _paymentService.AddPaymentRecord(payment);
                }
                else if (updateData.PaymentRRR == null)
                {
                    throw new Exception("No Payment Id found");
                }
            }

            Console.WriteLine("Updating app history...");
            var appHistory = new ApplicationInfo();
            if (file.FileStatus == ApplicationStatuses.AwaitingCertification || file.FileStatus == ApplicationStatuses.Publication)
            {
                appHistory = new ApplicationInfo
                {
                    id = Guid.NewGuid().ToString(),
                    ApplicationType = FormApplicationTypes.ClericalUpdate,
                    CurrentStatus = ApplicationStatuses.Amendment,
                    ApplicationDate = DateTime.Now,
                    PaymentId = updateData.PaymentRRR,
                    FieldToChange = updateData.UpdateType,
                    NewValue = "",
                    StatusHistory = new List<ApplicationHistory>
                    {
                        new ApplicationHistory{
                            Date = DateTime.Now,
                            beforeStatus = ApplicationStatuses.AwaitingPayment,
                            afterStatus = ApplicationStatuses.Amendment,
                            Message = "Clerical Update",
                            User = applicant?.Name,
                            UserId = file.CreatorAccount
                        }
                    }
                };
            }
            else
            {
                appHistory = new ApplicationInfo
                {
                    id = Guid.NewGuid().ToString(),
                    ApplicationType = FormApplicationTypes.ClericalUpdate,
                    CurrentStatus = ApplicationStatuses.Re_conduct,
                    ApplicationDate = DateTime.Now,
                    PaymentId = updateData.PaymentRRR,
                    FieldToChange = updateData.UpdateType,
                    NewValue = "",
                    StatusHistory = new List<ApplicationHistory>
                    {
                        new ApplicationHistory{
                            Date = DateTime.Now,
                            beforeStatus = ApplicationStatuses.AwaitingPayment,
                            afterStatus = ApplicationStatuses.Re_conduct,
                            Message = "Clerical Update",
                            User = applicant?.Name,
                            UserId = file.CreatorAccount
                        }
                    }
                };
            }
            
            // Prepare clerical update archive with previous values
            var clerical = new ClericalUpdate
            {
                Id = appHistory.id,
                UpdateType = updateData.UpdateType,
                FilingDate = DateTime.Now,
                PaymentRRR = updateData.PaymentRRR
            };
            var atty = new AttachmentType();
            var atts = new List<AttachmentType>();
            var updateDef = Builders<Filling>.Update.Combine();
            var attachments = file.Attachments ?? new List<AttachmentType>();
            string docUrl = null;
            if (updateData.Representation != null)
            {
                Console.WriteLine("Found a representation file...");
                using var ms = new MemoryStream();
                await updateData.Representation.CopyToAsync(ms);
                var userDoc = ms.ToArray();
                var links = await UploadAttachment(new List<TT>
                {
                    new TT
                    {
                        contentType = updateData.Representation.ContentType,
                        data = userDoc,
                        fileName = Path.GetFileName(updateData.Representation.FileName),
                        Name = "representation"
                    }
                });
                docUrl = links[0];
            }

            string poaUrl = null;
            if (updateData.PowerOfAttorney != null)
            {
                using var ms = new MemoryStream();
                await updateData.PowerOfAttorney.CopyToAsync(ms);
                var poa = ms.ToArray();
                var url = await UploadAttachment(new List<TT>
                {
                    new TT
                    {
                        contentType = updateData.PowerOfAttorney.ContentType,
                        data = poa,
                        fileName = Path.GetFileName(updateData.PowerOfAttorney.FileName),
                        Name = "poa"
                    }
                });
                poaUrl = url[0];
            }

            string otherAttUrl = null;
            if (updateData.OtherAttachment != null)
            {
                Console.WriteLine(updateData.OtherAttachment);
                using var ms = new MemoryStream();
                await updateData.OtherAttachment.CopyToAsync(ms);
                var otherAtt = ms.ToArray();
                var url = await UploadAttachment(new List<TT>
                {
                    new TT
                    {
                        contentType = updateData.OtherAttachment.ContentType,
                        data = otherAtt,
                        fileName = Path.GetFileName(updateData.OtherAttachment.FileName),
                        Name = "others"
                    }
                });
                otherAttUrl = url[0];
                atty.name = "other";
                atty.url = new List<string> { otherAttUrl };
                Console.WriteLine("url: " + otherAttUrl);
                // ✅ Always add to the attachments list here
                attachments.Add(atty);

                clerical.OldAttachmentUrl = null;
                clerical.NewAttachmentUrl = otherAttUrl;

                // ✅ Always persist into updateDef
                updateDef = updateDef.Set(f => f.Attachments, attachments);

                Console.WriteLine("url: " + otherAttUrl);
            }
            
            Console.WriteLine("FileType: " + file.Type);
            switch (updateData.UpdateType)
            {
                case "ApplicantName":
                    if (updateData.FileType == FileTypes.Patent)
                    {
                        if (updateData.ApplicantNames != null && updateData.ApplicantNames.Count > 0)
                        {
                            clerical.OldApplicantNames = file.applicants.Select(a => a.Name).ToList();
                            clerical.NewApplicantNames = updateData.ApplicantNames;
                            for (int i = 0; i < updateData.ApplicantNames.Count && i < file.applicants.Count; i++)
                            {
                                var newName = updateData.ApplicantNames[i];
                                if (!string.IsNullOrWhiteSpace(newName))
                                {
                                    file.applicants[i].Name = newName;
                                }
                                // else: keep the old name
                            }
                            updateDef = updateDef.Set(f => f.applicants, file.applicants);
                        }
                    }
                    else // Trademark or other single-applicant files
                    {
                        if (!string.IsNullOrWhiteSpace(updateData.ApplicantName))
                        {
                            clerical.OldApplicantName = file.applicants?[0]?.Name;
                            clerical.NewApplicantName = updateData.ApplicantName;
                            updateDef = updateDef.Set("applicants.0.Name", updateData.ApplicantName);
                        }
                    }
                    break;

                case "ApplicantAddress":
                    if (updateData.FileType == FileTypes.Patent)
                    {
                        // Address
                        if (updateData.ApplicantAddresses != null && updateData.ApplicantAddresses.Count > 0)
                        {
                            clerical.OldApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                            clerical.NewApplicantAddresses = updateData.ApplicantAddresses;
                            for (int i = 0; i < updateData.ApplicantAddresses.Count && i < file.applicants.Count; i++)
                            {
                                var newAddress = updateData.ApplicantAddresses[i];
                                if (!string.IsNullOrWhiteSpace(newAddress))
                                    file.applicants[i].Address = newAddress;
                            }
                        }
                        // Email
                        if (updateData.ApplicantEmails != null && updateData.ApplicantEmails.Count > 0)
                        {
                            clerical.OldApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                            clerical.NewApplicantEmails = updateData.ApplicantEmails;
                            for (int i = 0; i < updateData.ApplicantEmails.Count && i < file.applicants.Count; i++)
                            {
                                var newEmail = updateData.ApplicantEmails[i];
                                if (!string.IsNullOrWhiteSpace(newEmail))
                                    file.applicants[i].Email = newEmail;
                            }
                        }
                        // Phone
                        if (updateData.ApplicantPhones != null && updateData.ApplicantPhones.Count > 0)
                        {
                            clerical.OldApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                            clerical.NewApplicantPhones = updateData.ApplicantPhones;
                            for (int i = 0; i < updateData.ApplicantPhones.Count && i < file.applicants.Count; i++)
                            {
                                var newPhone = updateData.ApplicantPhones[i];
                                if (!string.IsNullOrWhiteSpace(newPhone))
                                    file.applicants[i].Phone = newPhone;
                            }
                        }
                        // Nationality
                        if (updateData.ApplicantNationalities != null && updateData.ApplicantNationalities.Count > 0)
                        {
                            clerical.OldApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                            clerical.NewApplicantNationalities = updateData.ApplicantNationalities;
                            for (int i = 0; i < updateData.ApplicantNationalities.Count && i < file.applicants.Count; i++)
                            {
                                var newNationality = updateData.ApplicantNationalities[i];
                                if (!string.IsNullOrWhiteSpace(newNationality))
                                    file.applicants[i].country = newNationality;
                            }
                        }
                        // State
                        if (updateData.ApplicantStates != null && updateData.ApplicantStates.Count > 0)
                        {
                            clerical.OldApplicantStates = file.applicants.Select(a => a.State).ToList();
                            clerical.NewApplicantStates = updateData.ApplicantStates;
                            for (int i = 0; i < updateData.ApplicantStates.Count && i < file.applicants.Count; i++)
                            {
                                var newState = updateData.ApplicantStates[i];
                                if (!string.IsNullOrWhiteSpace(newState))
                                    file.applicants[i].State = newState;
                            }
                        }
                        // City (NEW)
                        if (updateData.ApplicantCities != null && updateData.ApplicantCities.Count > 0)
                        {
                            clerical.OldApplicantCities = file.applicants.Select(a => a.city).ToList();
                            clerical.NewApplicantCities = updateData.ApplicantCities;
                            for (int i = 0; i < updateData.ApplicantCities.Count && i < file.applicants.Count; i++)
                            {
                                var newCity = updateData.ApplicantCities[i];
                                if (!string.IsNullOrWhiteSpace(newCity))
                                    file.applicants[i].city = newCity;
                            }
                        }
                        updateDef = updateDef.Set(f => f.applicants, file.applicants);
                    }
                    else // Trademark or other single-applicant files
                    {
                        if (!string.IsNullOrWhiteSpace(updateData.ApplicantAddress))
                        {
                            clerical.OldApplicantAddress = file.applicants?[0]?.Address;
                            clerical.NewApplicantAddress = updateData.ApplicantAddress;
                            updateDef = updateDef.Set("applicants.0.Address", updateData.ApplicantAddress);
                        }
                        if (!string.IsNullOrWhiteSpace(updateData.ApplicantEmail))
                        {
                            clerical.OldApplicantEmail = file.applicants?[0]?.Email;
                            clerical.NewApplicantEmail = updateData.ApplicantEmail;
                            updateDef = updateDef.Set("applicants.0.Email", updateData.ApplicantEmail);
                        }
                        if (!string.IsNullOrWhiteSpace(updateData.ApplicantPhone))
                        {
                            clerical.OldApplicantPhone = file.applicants?[0]?.Phone;
                            clerical.NewApplicantPhone = updateData.ApplicantPhone;
                            updateDef = updateDef.Set("applicants.0.Phone", updateData.ApplicantPhone);
                        }
                        if (!string.IsNullOrWhiteSpace(updateData.ApplicantNationality))
                        {
                            clerical.OldApplicantNationality = file.applicants?[0]?.country;
                            clerical.NewApplicantNationality = updateData.ApplicantNationality;
                            updateDef = updateDef.Set("applicants.0.country", updateData.ApplicantNationality);
                        }
                        //if (!string.IsNullOrWhiteSpace(updateData.ApplicantState))
                        //{
                        //    clerical.OldApplicantState = file.applicants?[0]?.State;
                        //    clerical.NewApplicantState = updateData.ApplicantState;
                        //    updateDef = updateDef.Set("applicants.0.state", updateData.ApplicantState);
                        //}
                    }
                    break;

                case "FileClass":
                    if (updateData.FileClass.HasValue)
                    {
                        clerical.OldFileClass = file.TrademarkClass?.ToString();
                        clerical.NewFileClass = updateData.FileClass.ToString();
                        updateDef = updateDef.Set(f => f.TrademarkClass, updateData.FileClass);
                    }
                    if (!string.IsNullOrWhiteSpace(updateData.ClassDescription))
                    {
                        clerical.OldClassDescription = file.TrademarkClassDescription;
                        clerical.NewClassDescription = updateData.ClassDescription;
                        updateDef = updateDef.Set(f => f.TrademarkClassDescription, updateData.ClassDescription);
                    }
                    if (!string.IsNullOrWhiteSpace(updateData.Disclaimer))
                    {
                        clerical.OldDisclaimer = file.TrademarkDisclaimer;
                        clerical.NewDisclaimer = updateData.Disclaimer;
                        updateDef = updateDef.Set(f => f.TrademarkDisclaimer, updateData.Disclaimer);
                    }
                    break;

                case "Correspondence":
                    var correspondence = new CorrespondenceType();
                    bool hasCorrespondence = false;

                    if (!string.IsNullOrWhiteSpace(updateData.CorrespondenceName))
                    {
                        clerical.OldCorrespondenceName = file.Correspondence?.name;
                        clerical.NewCorrespondenceName = updateData.CorrespondenceName;
                        correspondence.name = updateData.CorrespondenceName;
                        hasCorrespondence = true;
                    }
                    if (!string.IsNullOrWhiteSpace(updateData.CorrespondenceAddress))
                    {
                        clerical.OldCorrespondenceAddress = file.Correspondence?.address;
                        clerical.NewCorrespondenceAddress = updateData.CorrespondenceAddress;
                        correspondence.address = updateData.CorrespondenceAddress;
                        hasCorrespondence = true;
                    }
                    if (!string.IsNullOrWhiteSpace(updateData.CorrespondencePhone))
                    {
                        clerical.OldCorrespondencePhone = file.Correspondence?.phone;
                        clerical.NewCorrespondencePhone = updateData.CorrespondencePhone;
                        correspondence.phone = updateData.CorrespondencePhone;
                        hasCorrespondence = true;
                    }
                    if (!string.IsNullOrWhiteSpace(updateData.CorrespondenceEmail))
                    {
                        clerical.OldCorrespondenceEmail = file.Correspondence?.email;
                        clerical.NewCorrespondenceEmail = updateData.CorrespondenceEmail;
                        correspondence.email = updateData.CorrespondenceEmail;
                        hasCorrespondence = true;
                    }
                    if (!string.IsNullOrWhiteSpace(updateData.CorrespondenceNationality))
                    {
                        clerical.OldCorrespondenceNationality = file.Correspondence?.Nationality;
                        clerical.NewCorrespondenceNationality = updateData.CorrespondenceNationality;
                        correspondence.Nationality = updateData.CorrespondenceNationality;
                        hasCorrespondence = true;
                    }
                    if (!string.IsNullOrWhiteSpace(updateData.CorrespondenceState))
                    {
                        clerical.OldCorrespondenceState = file.Correspondence?.state;
                        clerical.NewCorrespondenceState = updateData.CorrespondenceState;
                        correspondence.state = updateData.CorrespondenceState;
                        hasCorrespondence = true;
                    }

                    if (!string.IsNullOrEmpty(poaUrl))
                    {
                        var poaIndex = attachments.FindIndex(a => a.name == "poa");
                        if (poaIndex >= 0)
                        {
                            attachments[poaIndex].url = new List<string> { poaUrl };
                            clerical.OldPowerOfAttorneyUrl = attachments[poaIndex].url.FirstOrDefault();
                        }
                        else{
                            attachments.Add(new AttachmentType { name = "poa", url = new List<string> { poaUrl } });
                            clerical.OldPowerOfAttorneyUrl = "None";
                        }
                        clerical.NewPowerOfAttorneyUrl = poaUrl;
                    }
                    if (!string.IsNullOrEmpty(otherAttUrl))
                    {
                        attachments.Add(atty);
                        clerical.OldAttachmentUrl = null;
                        clerical.NewAttachmentUrl = otherAttUrl;
                    }
                    if (!string.IsNullOrEmpty(otherAttUrl))
                        updateDef = updateDef.Set(f => f.Attachments, attachments);

                    if (!string.IsNullOrEmpty(poaUrl))
                        updateDef = updateDef.Set(f => f.Attachments, attachments);
                   

                    if (hasCorrespondence)
                        updateDef = updateDef.Set(f => f.Correspondence, correspondence);
                    break;

                case "FileTitle":
                    if (!string.IsNullOrWhiteSpace(updateData.FileTitle))
                    {
                        Console.WriteLine(updateData.FileType);
                        switch (updateData.FileType)
                        {
                            case FileTypes.Design:
                                clerical.OldFileTitle = file.TitleOfDesign;
                                updateDef = updateDef.Set(f => f.TitleOfDesign, updateData.FileTitle);
                                clerical.NewFileTitle = updateData.FileTitle;
                                break;
                            case FileTypes.Patent:
                                clerical.OldFileTitle = file.TitleOfInvention;
                                updateDef = updateDef.Set(f => f.TitleOfInvention, updateData.FileTitle);
                                clerical.NewFileTitle = updateData.FileTitle;
                                break;
                            default:
                                clerical.OldFileTitle = file.TitleOfTradeMark;
                                updateDef = updateDef.Set(f => f.TitleOfTradeMark, updateData.FileTitle);
                                clerical.NewFileTitle = updateData.FileTitle;
                                break;
                        }
                    }
                        // Add this block for PatentAbstract
                    if (!string.IsNullOrWhiteSpace(updateData.PatentAbstract))
                    {
                        clerical.OldPatentAbstract = file.PatentAbstract;
                        updateDef = updateDef.Set(f => f.PatentAbstract, updateData.PatentAbstract);
                        clerical.NewPatentAbstract = updateData.PatentAbstract;
                    }

                    //// Add this block for PatentApplicationType
                    if (updateData.PatentApplicationType != null)
                    {
                        clerical.OldPatentApplicationType = file.PatentApplicationType ?? default;
                        updateDef = updateDef.Set(f => f.PatentApplicationType, updateData.PatentApplicationType.Value);
                        clerical.NewPatentApplicationType = updateData.PatentApplicationType.Value;
                    }

                    if (updateData.TrademarkLogo != null)
                    {
                        
                        clerical.OldTrademarkLogo = file.TrademarkLogo.ToString();
                        updateDef = updateDef.Set(f => f.TrademarkLogo, updateData.TrademarkLogo);
                        clerical.NewTrademarkLogo = updateData.TrademarkLogo.ToString();
                    }
                    if (!string.IsNullOrEmpty(docUrl))
                    {
                        var repIndex = attachments.FindIndex(a => a.name == "representation");
                        // var indexy = attachments.Count + 1;
                        if (repIndex == -1)
                        {
                            clerical.OldRepresentationUrl = null;
                            attachments.Add(new AttachmentType { name = "representation", url = new List<string> { docUrl } });
                        }
                        else
                        {
                            clerical.OldRepresentationUrl = attachments[repIndex].url[0];
                            attachments[repIndex].url = new List<string> { docUrl };
                        }
                        
                        updateDef = updateDef.Set(f => f.Attachments, attachments);
                        clerical.NewRepresentationUrl = docUrl;
                    }
                    break;
                // ... inside the switch (updateData.UpdateType)
                case "AddApplicant":
                    // Archive old applicants list
                    clerical.OldApplicantNames = file.applicants.Select(a => a.Name).ToList();
                    clerical.OldApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                    clerical.OldApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                    clerical.OldApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                    clerical.OldApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                    clerical.OldApplicantStates = file.applicants.Select(a => a.State).ToList();
                    clerical.OldApplicantCities = file.applicants.Select(a => a.city).ToList();

                    // Add all new applicants to the list
                    if (updateData.NewApplicants != null && updateData.NewApplicants.Count > 0)
                    {
                        foreach (var apps in updateData.NewApplicants)
                        {
                            apps.id ??= Guid.NewGuid().ToString();
                            file.applicants.Add(apps);
                        }
                    }

                    // Archive new applicants list
                    clerical.NewApplicantNames = file.applicants.Select(a => a.Name).ToList();
                    clerical.NewApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                    clerical.NewApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                    clerical.NewApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                    clerical.NewApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                    clerical.NewApplicantStates = file.applicants.Select(a => a.State).ToList();
                    clerical.NewApplicantCities = file.applicants.Select(a => a.city).ToList();

                    // Update DB
                    updateDef = updateDef.Set(f => f.applicants, file.applicants);
                break;
                // ... Fore Patent Removal of Applicant
                case "RemoveApplicant":
                    // Archive old applicants list
                    clerical.OldApplicantNames = file.applicants.Select(a => a.Name).ToList();
                    clerical.OldApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                    clerical.OldApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                    clerical.OldApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                    clerical.OldApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                    clerical.OldApplicantStates = file.applicants.Select(a => a.State).ToList();
                    clerical.OldApplicantCities = file.applicants.Select(a => a.city).ToList();

                    // Remove applicants by ID
                    if (updateData.RemoveApplicantIds != null && updateData.RemoveApplicantIds.Count > 0)
                    {
                        file.applicants.RemoveAll(a => updateData.RemoveApplicantIds.Contains(a.id));
                    }

                    // Archive new applicants list
                    clerical.NewApplicantNames = file.applicants.Select(a => a.Name).ToList();
                    clerical.NewApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                    clerical.NewApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                    clerical.NewApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                    clerical.NewApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                    clerical.NewApplicantStates = file.applicants.Select(a => a.State).ToList();
                    clerical.NewApplicantCities = file.applicants.Select(a => a.city).ToList();

                    // Update DB
                    updateDef = updateDef.Set(f => f.applicants, file.applicants);
                break;
                // ... inside the switch (updateData.UpdateType)
                case "AddAndRemoveApplicant":
                    // Archive old applicants list
                    clerical.OldApplicantNames = file.applicants.Select(a => a.Name).ToList();
                    clerical.OldApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                    clerical.OldApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                    clerical.OldApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                    clerical.OldApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                    clerical.OldApplicantStates = file.applicants.Select(a => a.State).ToList();
                    clerical.OldApplicantCities = file.applicants.Select(a => a.city).ToList();

                    // Remove applicants by ID
                    if (updateData.RemoveApplicantIds != null && updateData.RemoveApplicantIds.Count > 0)
                    {
                        file.applicants.RemoveAll(a => updateData.RemoveApplicantIds.Contains(a.id));
                    }

                    // Add new applicants
                    if (updateData.NewApplicants != null && updateData.NewApplicants.Count > 0)
                    {
                        foreach (var apps in updateData.NewApplicants)
                        {
                            apps.id ??= Guid.NewGuid().ToString();
                            file.applicants.Add(apps);
                        }
                    }

                    // Archive new applicants list
                    clerical.NewApplicantNames = file.applicants.Select(a => a.Name).ToList();
                    clerical.NewApplicantAddresses = file.applicants.Select(a => a.Address).ToList();
                    clerical.NewApplicantEmails = file.applicants.Select(a => a.Email).ToList();
                    clerical.NewApplicantPhones = file.applicants.Select(a => a.Phone).ToList();
                    clerical.NewApplicantNationalities = file.applicants.Select(a => a.country).ToList();
                    clerical.NewApplicantStates = file.applicants.Select(a => a.State).ToList();
                    clerical.NewApplicantCities = file.applicants.Select(a => a.city).ToList();

                    // Update DB
                    updateDef = updateDef.Set(f => f.applicants, file.applicants);
                    break;
                case "EditInventors":
                    // Archive old inventors for audit/history
                    clerical.OldInventorNames = file.Inventors.Select(i => i.Name).ToList();
                    clerical.OldInventorAddresses = file.Inventors.Select(i => i.Address).ToList();
                    clerical.OldInventorEmails = file.Inventors.Select(i => i.Email).ToList();
                    clerical.OldInventorPhones = file.Inventors.Select(i => i.Phone).ToList();
                    clerical.OldInventorNationalities = file.Inventors.Select(i => i.country).ToList();
                    clerical.OldInventorStates = file.Inventors.Select(i => i.State).ToList();
                    clerical.OldInventorCities = file.Inventors.Select(i => i.city).ToList();

                    // Replace inventors with the new list (add, remove, edit all at once)
                    if (updateData.NewInventors != null)
                    {
                        foreach (var inv in updateData.NewInventors)
                        {
                            inv.id ??= Guid.NewGuid().ToString();
                        }
                        file.Inventors = updateData.NewInventors;
                    }
                    else
                    {
                        file.Inventors = new List<ApplicantInfo>();
                    }

                    // Archive new inventors for audit/history
                    clerical.NewInventorNames = file.Inventors.Select(i => i.Name).ToList();
                    clerical.NewInventorAddresses = file.Inventors.Select(i => i.Address).ToList();
                    clerical.NewInventorEmails = file.Inventors.Select(i => i.Email).ToList();
                    clerical.NewInventorPhones = file.Inventors.Select(i => i.Phone).ToList();
                    clerical.NewInventorNationalities = file.Inventors.Select(i => i.country).ToList();
                    clerical.NewInventorStates = file.Inventors.Select(i => i.State).ToList();
                    clerical.NewInventorCities = file.Inventors.Select(i => i.city).ToList();

                    // Update DB
                    updateDef = updateDef.Set(f => f.Inventors, file.Inventors);
                    break;
                case "PriorityInfo":
                    // Archive old values for audit/history
                    
                    clerical.OldFirstPriorityInfo = file.FirstPriorityInfo?.Select(p => new PriorityInfo
                    {
                        id = p.id,
                        number = p.number,
                        Country = p.Country,
                        Date = p.Date
                    }).ToList();
                    clerical.OldPriorityInfo = file.PriorityInfo?.Select(p => new PriorityInfo
                    {
                        id = p.id,
                        number = p.number,
                        Country = p.Country,
                        Date = p.Date
                    }).ToList();

                    // Replace with new lists (add, edit, delete at once)
                    if (updateData.FirstPriorityInfo != null)
                    {
                        foreach (var p in updateData.FirstPriorityInfo)
                            p.id ??= Guid.NewGuid().ToString();
                        file.FirstPriorityInfo = updateData.FirstPriorityInfo;
                    }
                    else
                    {
                        file.FirstPriorityInfo = new List<PriorityInfo>();
                    }

                    if (updateData.PriorityInfo != null)
                    {
                        foreach (var p in updateData.PriorityInfo)
                            p.id ??= Guid.NewGuid().ToString();
                        file.PriorityInfo = updateData.PriorityInfo;
                    }
                    else
                    {
                        file.PriorityInfo = new List<PriorityInfo>();
                    }

                    // Archive new values for audit/history
                    clerical.NewFirstPriorityInfo = file.FirstPriorityInfo;
                    clerical.NewPriorityInfo = file.PriorityInfo;

                    // Update DB
                    updateDef = updateDef
                        .Set(f => f.FirstPriorityInfo, file.FirstPriorityInfo)
                        .Set(f => f.PriorityInfo, file.PriorityInfo);
                break;

            }
            // Final DB update
            Console.WriteLine("Committing update to DB...");
            var finalUpdate = Builders<Filling>.Update.Combine(
                updateDef,
                Builders<Filling>.Update.Push(f => f.ApplicationHistory, appHistory),
                Builders<Filling>.Update.Push(f => f.ClericalUpdates, clerical));


            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, file.FileId), finalUpdate);
            Console.WriteLine("Clerical update completed successfully.");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            _log.LogError(ex, "Error at clerical update");
            return false;
        }
    }
    public async Task<ClericalUpdateDetailsDto> GetClericalUpdateApp(string fileId, string appId)
    {   
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
            PaymentId =  clerical.PaymentRRR
        };
        switch (clerical.UpdateType)
        {
            case "ApplicantName":
                update.OldValue = clerical?.OldApplicantName;
                update.NewValue = file.applicants[0].Name;
                break;
            case "ApplicantAddress":
                update.OldValue = clerical?.OldApplicantAddress;
                update.NewValue = file.applicants[0].Address;
                update.OldValue2 = clerical?.OldApplicantEmail;
                update.NewValue2 = file.applicants[0].Email;
                update.OldValue3 = clerical?.OldApplicantPhone;
                update.NewValue3 = file.applicants[0].Address;
                break;
            case "FileClass":
                update.OldValue = clerical?.OldFileClass;
                update.NewValue = file.TrademarkClass.ToString();
                update.OldValue2 = clerical?.OldClassDescription;
                update.NewValue2 = file.TrademarkClassDescription;
                break;
            case "Correspondence":
                update.OldValue = clerical?.OldCorrespondenceName;
                update.NewValue = file.Correspondence?.name;
                update.OldValue2 = clerical?.OldCorrespondenceAddress;
                update.NewValue2 = file.Correspondence?.address;
                update.OldValue3 = clerical?.OldCorrespondenceEmail;
                update.NewValue3 = file.Correspondence?.email;
                update.OldValue4 = clerical?.OldCorrespondencePhone;
                update.NewValue4 = file.Correspondence?.phone;
                update.OldPowerOfAttorneyUrl = clerical?.OldPowerOfAttorneyUrl;
                update.NewPowerOfAttorneyUrl = file.Attachments?.FirstOrDefault(a => a.name == "poa")?.url.FirstOrDefault();
                break;
            case "FileTitle":
                update.OldValue = clerical?.OldFileTitle;
                switch (file.Type)
                {
                    case FileTypes.TradeMark:
                        update.NewValue = file.TitleOfTradeMark;
                        break;
                    case FileTypes.Design:
                        update.NewValue = file.TitleOfDesign;
                        break;
                    default:
                        update.NewValue = file.TitleOfInvention;
                        break;
                }

                update.OldValue2 = clerical?.OldTrademarkLogo;
                update.OldRepresentation = clerical?.OldRepresentationUrl;
                if (update.OldRepresentation != null)
                { 
                    update.NewRepresentation = file.Attachments?.FirstOrDefault(a=>a.name == "representation")?.url.FirstOrDefault();
                }
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
            if (recordal == null )
                return false;
            var payment = new PaymentRecord
            {
                PaymentType = recordal.ApplicationType.ToString(),
                Date = DateTime.Now,
                FileId = fileId,
                ApplicationId = recordal.id,
                RemitaResponse = remita
            };
            await _paymentService.AddPaymentRecord(payment);
            recordal.CurrentStatus = ApplicationStatuses.AwaitingRecordalProcess;
            recordal.StatusHistory[0].beforeStatus = ApplicationStatuses.AwaitingPayment;
            recordal.StatusHistory[0].afterStatus = ApplicationStatuses.AwaitingRecordalProcess;

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
        var remita = await _remitaPaymentUtils.GetDetailsByRRR(rrr);
        if (remita == null || remita.status != "00")
        {
            Console.WriteLine($"Payment not successful or not found. RRR: {rrr}");
            return false;
        }
        var payment = new PaymentRecord
        {
            PaymentType = "Certificate",
            Date = DateTime.Now,
            FileId = fileId,
            RemitaResponse = remita
        };
        await _paymentService.AddPaymentRecord(payment);
        var filter = Builders<Filling>.Filter.And(
            Builders<Filling>.Filter.Eq(f => f.FileId, fileId),
            Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory,
                a => a.CertificatePaymentId == rrr &&
                     a.CurrentStatus == ApplicationStatuses.AwaitingCertification)
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
        var result = new FileApplicationsDto
        {
            FileTitle = fileTitle?? "",
            Applications = apps
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

            var update = Builders<Filling>.Update
                .Set("ApplicationHistory.$.PaymentId", dto.NewPaymentId);

            var result = await _fillingCollection.UpdateOneAsync(filter, update);

            // return result.ModifiedCount > 0;
            if (result.ModifiedCount > 0)
            {
                // ? Fetch File Info to complete the log
                var file = await _fillingCollection.Find(f => f.FileId == dto.FileId).FirstOrDefaultAsync();
                if (file != null)
                {
                    await LogFileUpdateAsync(
                        dto.FileId!,
                        file.TitleOfInvention ?? file.TitleOfDesign ?? file.TitleOfTradeMark ?? "(No Title)",
                        file.Type, // Assuming this maps to your FileTypes enum
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

            var dto = new FileUpdateDto
            {
                //Id = filling.Id,
                FileId = filling.FileId,
                LastRequestDate = filling.LastRequestDate,
                CreatorAccount = filling.CreatorAccount,
                FileStatus = filling.FileStatus,
                FileOrigin = filling.FileOrigin,
                FilingCountry = filling.FilingCountry,
                //DateCreated = filling.DateCreated,
                Type = filling.Type,
                TitleOfInvention = filling.TitleOfInvention,
                PatentAbstract = filling.PatentAbstract,
                Correspondence = filling.Correspondence,
                LastRequest = filling.LastRequest,
                applicants = filling.applicants,
                PatentApplicationType = filling.PatentApplicationType,
                Revisions = filling.Revisions,
                PatentType = filling.PatentType,
                Inventors = filling.Inventors,
                PriorityInfo = filling.PriorityInfo,
                FirstPriorityInfo = filling.FirstPriorityInfo,
                DesignType = filling.DesignType,
                TitleOfDesign = filling.TitleOfDesign,
                StatementOfNovelty = filling.StatementOfNovelty,
                DesignCreators = filling.DesignCreators,
                Attachments = filling.Attachments,
                FieldStatus = filling.FieldStatus,
                TitleOfTradeMark = filling.TitleOfTradeMark,
                TrademarkClass = filling.TrademarkClass,
                TrademarkClassDescription = filling.TrademarkClassDescription,
                TrademarkLogo = filling.TrademarkLogo,
                TrademarkType = filling.TrademarkType,
                TrademarkDisclaimer = filling.TrademarkDisclaimer,
                RtmNumber = filling.RtmNumber,
                Comment = filling.Comment,
                Registered_Users = filling.Registered_Users,
                RegisteredUsers = filling.RegisteredUsers,
                Assignees = filling.Assignees,
                PostRegApplications = filling.PostRegApplications,
                ClericalUpdates = filling.ClericalUpdates,
                MigratedPCTNo = filling.MigratedPCTNo
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
            MergeList(existing.applicants, dto.Applicants, x => x.id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.country)) e.country = u.country;
                if (!string.IsNullOrWhiteSpace(u.city)) e.city = u.city; 
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.State)) e.State = u.State;
            });

        if (dto.Inventors?.Any() == true)
            MergeList(existing.Inventors, dto.Inventors, x => x.id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.country)) e.country = u.country;
                if (!string.IsNullOrWhiteSpace(u.city)) e.city = u.city;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.State)) e.State = u.State;
            });

        if (dto.FirstPriorityInfo?.Any() == true)
            MergeList(existing.FirstPriorityInfo, dto.FirstPriorityInfo, x => x.id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Country)) e.Country = u.Country;
                if (!string.IsNullOrWhiteSpace(u.Date)) e.Date = u.Date;
                if (!string.IsNullOrWhiteSpace(u.number)) e.number = u.number;
            });

        await _fillingCollection.ReplaceOneAsync(
              x => x.FileId == dto.FileId, existing
              );

        return (200, "Filing record updated successfully.");
    }

    public async Task<(int StatusCode, string Message)> UpdateFilingAsync(FileUpdateDto request)
    {
        var existing = await _fillingCollection.Find(x => x.FileId == request.FileId).FirstOrDefaultAsync();
        if (existing == null)
            return (404, "Filing record not found");

        // Scalar fields
        if (!string.IsNullOrWhiteSpace(request.CreatorAccount)) existing.CreatorAccount = request.CreatorAccount;
        if (!string.IsNullOrWhiteSpace(request.TitleOfInvention)) existing.TitleOfInvention = request.TitleOfInvention;
        if (!string.IsNullOrWhiteSpace(request.PatentAbstract)) existing.PatentAbstract = request.PatentAbstract;
        if (!string.IsNullOrWhiteSpace(request.TitleOfDesign)) existing.TitleOfDesign = request.TitleOfDesign;
        if (!string.IsNullOrWhiteSpace(request.StatementOfNovelty)) existing.StatementOfNovelty = request.StatementOfNovelty;
        if (!string.IsNullOrWhiteSpace(request.TitleOfTradeMark)) existing.TitleOfTradeMark = request.TitleOfTradeMark;
        if (!string.IsNullOrWhiteSpace(request.TrademarkClassDescription)) existing.TrademarkClassDescription = request.TrademarkClassDescription;
        if (!string.IsNullOrWhiteSpace(request.TrademarkDisclaimer)) existing.TrademarkDisclaimer = request.TrademarkDisclaimer;
        if (!string.IsNullOrWhiteSpace(request.RtmNumber)) existing.RtmNumber = request.RtmNumber;
        if (!string.IsNullOrWhiteSpace(request.Comment)) existing.Comment = request.Comment;
        if (!string.IsNullOrWhiteSpace(request.MigratedPCTNo)) existing.MigratedPCTNo = request.MigratedPCTNo;

        // Nullable types
        if (request.LastRequestDate != null) existing.LastRequestDate = request.LastRequestDate.Value;
        if (request.LastRequest != null) existing.LastRequest = request.LastRequest.Value;
        if (request.FileStatus != null) existing.FileStatus = request.FileStatus.Value;
        if (request.Type != null) existing.Type = request.Type.Value;
        if (request.PatentApplicationType != null) existing.PatentApplicationType = request.PatentApplicationType.Value;
        if (request.PatentType != null) existing.PatentType = request.PatentType.Value;
        if (request.DesignType != null) existing.DesignType = request.DesignType.Value;
        if (request.TrademarkClass != null) existing.TrademarkClass = request.TrademarkClass.Value;
        if (request.TrademarkLogo != null) existing.TrademarkLogo = request.TrademarkLogo.Value;
        if (request.TrademarkType != null) existing.TrademarkType = request.TrademarkType.Value;

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

        if (request.FieldStatus != null && request.FieldStatus.Any())
            existing.FieldStatus = request.FieldStatus;

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

        if (request.applicants?.Any() == true)
            MergeList(existing.applicants, request.applicants, x => x.id, (e, u) => { if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name; if (!string.IsNullOrWhiteSpace(u.country)) e.country = u.country; if (!string.IsNullOrWhiteSpace(u.city)) e.city = u.city; if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone; if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email; if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address; });

        if (request.Inventors?.Any() == true)
            MergeList(existing.Inventors, request.Inventors, x => x.id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.country)) e.country = u.country;
                if (!string.IsNullOrWhiteSpace(u.city)) e.city = u.city;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
            });

        if (request.DesignCreators?.Any() == true)
            MergeList(existing.DesignCreators, request.DesignCreators, x => x.id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.country)) e.country = u.country;
                if (!string.IsNullOrWhiteSpace(u.city)) e.city = u.city;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
            });

        if (request.Revisions?.Any() == true)
            MergeList(existing.Revisions, request.Revisions, x => x.TransactionId, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.AssociatedTrade)) e.AssociatedTrade = u.AssociatedTrade;
                if (u.OldValue != null) e.OldValue = u.OldValue;
                if (u.NewValue != null) e.NewValue = u.NewValue;
                if (!string.IsNullOrWhiteSpace(u.Property)) e.Property = u.Property;
                if (!string.IsNullOrWhiteSpace(u.AmountPaid)) e.AmountPaid = u.AmountPaid;
                if (!string.IsNullOrWhiteSpace(u.TransactionId)) e.TransactionId = u.TransactionId;
                if (u.DateTime != default) e.DateTime = u.DateTime;
                if (!string.IsNullOrWhiteSpace(u.userName)) e.userName = u.userName;
                if (!string.IsNullOrWhiteSpace(u.userId)) e.userId = u.userId;
                if (u.currentStatus.HasValue) e.currentStatus = u.currentStatus;
            });

        if (request.PriorityInfo?.Any() == true)
            MergeList(existing.PriorityInfo, request.PriorityInfo, x => x.id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.number)) e.number = u.number;
                if (!string.IsNullOrWhiteSpace(u.Country)) e.Country = u.Country;
                if (!string.IsNullOrWhiteSpace(u.Date)) e.Date = u.Date;
            });

        if (request.Attachments?.Any() == true)
            MergeList(existing.Attachments, request.Attachments, x => x.name, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.name)) e.name = u.name;
                if (u.url != null && u.url.Any()) e.url = u.url;
            });

        //if (request.ApplicationHistory?.Any() == true)
        //    MergeList(existing.ApplicationHistory, request.ApplicationHistory, x => x.id, (e, u) => {
        //        e.ApplicationType = u.ApplicationType;
        //        e.CurrentStatus = u.CurrentStatus;
        //        if (u.ExpiryDate != null) e.ExpiryDate = u.ExpiryDate;
        //        if (!string.IsNullOrWhiteSpace(u.PaymentId)) e.PaymentId = u.PaymentId;
        //        if (!string.IsNullOrWhiteSpace(u.CertificatePaymentId)) e.CertificatePaymentId = u.CertificatePaymentId;
        //        if (u.ApplicationDate != default) e.ApplicationDate = u.ApplicationDate;
        //        if (!string.IsNullOrWhiteSpace(u.LicenseType)) e.LicenseType = u.LicenseType;
        //        if (!string.IsNullOrWhiteSpace(u.OldValue)) e.OldValue = u.OldValue;
        //        if (!string.IsNullOrWhiteSpace(u.NewValue)) e.NewValue = u.NewValue;
        //        if (!string.IsNullOrWhiteSpace(u.FieldToChange)) e.FieldToChange = u.FieldToChange;
        //        if (u.Letters != null && u.Letters.Any()) e.Letters = u.Letters;
        //        if (u.StatusHistory?.Any() == true) e.StatusHistory = u.StatusHistory;
        //        if (u.ApplicationLetters?.Any() == true) e.ApplicationLetters = u.ApplicationLetters;
        //        if (u.Assignment != null) e.Assignment = u.Assignment;
        //        if (!string.IsNullOrWhiteSpace(u.RegisteredUser)) e.RegisteredUser = u.RegisteredUser;
        //    });

        if (request.Registered_Users?.Any() == true)
            MergeList(existing.Registered_Users, request.Registered_Users, x => x.Id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Nationality)) e.Nationality = u.Nationality;
                if (!string.IsNullOrWhiteSpace(u.FileId)) e.FileId = u.FileId;
                if (u.isApproved.HasValue) e.isApproved = u.isApproved;
            });

        if (request.RegisteredUsers?.Any() == true)
            MergeList(existing.RegisteredUsers, request.RegisteredUsers, x => x.Id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Nationality)) e.Nationality = u.Nationality;
                if (!string.IsNullOrWhiteSpace(u.FileId)) e.FileId = u.FileId;
                if (u.isApproved.HasValue) e.isApproved = u.isApproved;
            });

        if (request.Assignees?.Any() == true)
            MergeList(existing.Assignees, request.Assignees, x => x.Id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Nationality)) e.Nationality = u.Nationality;
                if (!string.IsNullOrWhiteSpace(u.FileId)) e.FileId = u.FileId;
                if (!string.IsNullOrWhiteSpace(u.rrr)) e.rrr = u.rrr;
                if (!string.IsNullOrWhiteSpace(u.AuthorizationLetterUrl)) e.AuthorizationLetterUrl = u.AuthorizationLetterUrl;
                if (!string.IsNullOrWhiteSpace(u.AssignmentDeedUrl)) e.AssignmentDeedUrl = u.AssignmentDeedUrl;
                if (u.isApproved.HasValue) e.isApproved = u.isApproved;
            });

        if (request.PostRegApplications?.Any() == true)
            MergeList(existing.PostRegApplications, request.PostRegApplications, x => x.Id, (e, u) => {
                if (!string.IsNullOrWhiteSpace(u.RecordalType)) e.RecordalType = u.RecordalType;
                if (!string.IsNullOrWhiteSpace(u.FileNumber)) e.FileNumber = u.FileNumber;
                if (!string.IsNullOrWhiteSpace(u.FilingDate)) e.FilingDate = u.FilingDate;
                if (!string.IsNullOrWhiteSpace(u.DateTreated)) e.DateTreated = u.DateTreated;
                if (!string.IsNullOrWhiteSpace(u.Reason)) e.Reason = u.Reason;
                if (!string.IsNullOrWhiteSpace(u.Name)) e.Name = u.Name;
                if (!string.IsNullOrWhiteSpace(u.Email)) e.Email = u.Email;
                if (!string.IsNullOrWhiteSpace(u.dateOfRecordal)) e.dateOfRecordal = u.dateOfRecordal;
                if (!string.IsNullOrWhiteSpace(u.Address)) e.Address = u.Address;
                if (!string.IsNullOrWhiteSpace(u.Phone)) e.Phone = u.Phone;
                if (!string.IsNullOrWhiteSpace(u.Nationality)) e.Nationality = u.Nationality;
                if (!string.IsNullOrWhiteSpace(u.documentUrl)) e.documentUrl = u.documentUrl;
                if (!string.IsNullOrWhiteSpace(u.document2Url)) e.document2Url = u.document2Url;
                if (!string.IsNullOrWhiteSpace(u.receiptUrl)) e.receiptUrl = u.receiptUrl;
                if (!string.IsNullOrWhiteSpace(u.certificateUrl)) e.certificateUrl = u.certificateUrl;
                if (!string.IsNullOrWhiteSpace(u.rejectionUrl)) e.rejectionUrl = u.rejectionUrl;
                if (!string.IsNullOrWhiteSpace(u.acknowledgementUrl)) e.acknowledgementUrl = u.acknowledgementUrl;
                if (!string.IsNullOrWhiteSpace(u.message)) e.message = u.message;
                if (!string.IsNullOrWhiteSpace(u.rrr)) e.rrr = u.rrr;
            });

        if (request.ClericalUpdates?.Any() == true)
            MergeList(existing.ClericalUpdates, request.ClericalUpdates, x => x.Id, (e, u) =>
            {
                if (!string.IsNullOrWhiteSpace(u.UpdateType)) e.UpdateType = u.UpdateType;
                if (u.FilingDate != default) e.FilingDate = u.FilingDate;
                if (!string.IsNullOrWhiteSpace(u.PaymentRRR)) e.PaymentRRR = u.PaymentRRR;
                if (!string.IsNullOrWhiteSpace(u.OldTrademarkLogo)) e.OldTrademarkLogo = u.OldTrademarkLogo;
                if (!string.IsNullOrWhiteSpace(u.NewTrademarkLogo)) e.NewTrademarkLogo = u.NewTrademarkLogo;
                if (!string.IsNullOrWhiteSpace(u.OldApplicantName)) e.OldApplicantName = u.OldApplicantName;
                if (!string.IsNullOrWhiteSpace(u.NewApplicantName)) e.NewApplicantName = u.NewApplicantName;
                if (!string.IsNullOrWhiteSpace(u.OldApplicantAddress)) e.OldApplicantAddress = u.OldApplicantAddress;
                if (!string.IsNullOrWhiteSpace(u.NewApplicantAddress)) e.NewApplicantAddress = u.NewApplicantAddress;
                if (!string.IsNullOrWhiteSpace(u.OldApplicantNationality)) e.OldApplicantNationality = u.OldApplicantNationality;
                if (!string.IsNullOrWhiteSpace(u.NewApplicantNationality)) e.NewApplicantNationality = u.NewApplicantNationality;
                if (!string.IsNullOrWhiteSpace(u.OldApplicantEmail)) e.OldApplicantEmail = u.OldApplicantEmail;
                if (!string.IsNullOrWhiteSpace(u.NewApplicantEmail)) e.NewApplicantEmail = u.NewApplicantEmail;
                if (!string.IsNullOrWhiteSpace(u.OldApplicantPhone)) e.OldApplicantPhone = u.OldApplicantPhone;
                if (!string.IsNullOrWhiteSpace(u.NewApplicantPhone)) e.NewApplicantPhone = u.NewApplicantPhone;
                if (!string.IsNullOrWhiteSpace(u.OldFileClass)) e.OldFileClass = u.OldFileClass;
                if (!string.IsNullOrWhiteSpace(u.NewFileClass)) e.NewFileClass = u.NewFileClass;
                if (!string.IsNullOrWhiteSpace(u.OldClassDescription)) e.OldClassDescription = u.OldClassDescription;
                if (!string.IsNullOrWhiteSpace(u.NewClassDescription)) e.NewClassDescription = u.NewClassDescription;
                if (!string.IsNullOrWhiteSpace(u.OldFileTitle)) e.OldFileTitle = u.OldFileTitle;
                if (!string.IsNullOrWhiteSpace(u.NewFileTitle)) e.NewFileTitle = u.NewFileTitle;
                if (!string.IsNullOrWhiteSpace(u.OldCorrespondenceName)) e.OldCorrespondenceName = u.OldCorrespondenceName;
                if (!string.IsNullOrWhiteSpace(u.NewCorrespondenceName)) e.NewCorrespondenceName = u.NewCorrespondenceName;
                if (!string.IsNullOrWhiteSpace(u.OldCorrespondenceAddress)) e.OldCorrespondenceAddress = u.OldCorrespondenceAddress;
                if (!string.IsNullOrWhiteSpace(u.NewCorrespondenceAddress)) e.NewCorrespondenceAddress = u.NewCorrespondenceAddress;
                if (!string.IsNullOrWhiteSpace(u.OldCorrespondenceEmail)) e.OldCorrespondenceEmail = u.OldCorrespondenceEmail;
                if (!string.IsNullOrWhiteSpace(u.NewCorrespondenceEmail)) e.NewCorrespondenceEmail = u.NewCorrespondenceEmail;
                if (!string.IsNullOrWhiteSpace(u.OldCorrespondencePhone)) e.OldCorrespondencePhone = u.OldCorrespondencePhone;
                if (!string.IsNullOrWhiteSpace(u.NewCorrespondencePhone)) e.NewCorrespondencePhone = u.NewCorrespondencePhone;
                if (!string.IsNullOrWhiteSpace(u.OldRepresentationUrl)) e.OldRepresentationUrl = u.OldRepresentationUrl;
                if (!string.IsNullOrWhiteSpace(u.NewRepresentationUrl)) e.NewRepresentationUrl = u.NewRepresentationUrl;
                if (!string.IsNullOrWhiteSpace(u.OldPowerOfAttorneyUrl)) e.OldPowerOfAttorneyUrl = u.OldPowerOfAttorneyUrl;
                if (!string.IsNullOrWhiteSpace(u.NewPowerOfAttorneyUrl)) e.NewPowerOfAttorneyUrl = u.NewPowerOfAttorneyUrl;
            });

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


        return (200, "Filing record updated successfully.");
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
            Console.WriteLine("appeal: "+req);
            var file = await _fillingCollection.Find(f => f.FileId == req.FileNumber).FirstOrDefaultAsync();
            if (file == null) throw new Exception("File not found");
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

    //public async Task<bool> UpdateAttachmentsAsync(string filingFileId, List<AttachmentType> newAttachments)
    //{
    //    var filter = Builders<Filling>.Filter.Eq(f => f.FileId, filingFileId);
    //    var filing = await _fillingCollection.Find(filter).FirstOrDefaultAsync();

    //    if (filing == null)
    //        return false;

    //    filing.Attachments ??= new List<AttachmentType>();

    //    foreach (var newAttachment in newAttachments)
    //    {
    //        var existing = filing.Attachments.FirstOrDefault(a => a.name == newAttachment.name);
    //        if (existing != null)
    //        {
    //            foreach (var url in newAttachment.url)
    //            {
    //                if (!existing.url.Contains(url))
    //                {
    //                    existing.url.Add(url);
    //                }
    //            }
    //        }
    //        else
    //        {
    //            filing.Attachments.Add(newAttachment);
    //        }
    //    }

    //    var update = Builders<Filling>.Update.Set(f => f.Attachments, filing.Attachments);
    //    await _fillingCollection.UpdateOneAsync(filter, update);

    //    return true;
    //}
    public async Task<bool> ApproveAmendment(string fileId, string appId)
    {
        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == fileId).FirstOrDefaultAsync();
            if (file == null) throw new Exception("File not found");
            var app = file.ApplicationHistory.FirstOrDefault(a => a.id == appId);
            if (app == null) throw new Exception("Application not found");

            app.CurrentStatus = ApplicationStatuses.Approved;
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, fileId),
                Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory)
            );
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}