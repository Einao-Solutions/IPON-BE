using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Models;
using patentdesign.Utils;
using QuestPDF.Fluent;
using Tfunctions.pdfs;

public class AssignmentService
{
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private PaymentUtils _remitaPaymentUtils;
    private MongoClient _mongoClient;
    private static IMongoCollection<PerformanceMarker> _performanceCollection;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    // private string attachmentBaseUrl = "http://localhost:5044";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    private static IMongoCollection<FinanceHistory> _financeCollection;

    public AssignmentService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils)
    {
        
        var useSandbox = patentDesignDbSettings.Value.UseSandbox;

        string digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;

        MongoClientSettings settings = MongoClientSettings.FromUrl(
            new MongoUrl(digitalOcean)
        );
        settings.SslSettings =
            new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
        _mongoClient = new MongoClient(settings);
        _remitaPaymentUtils = remitaPaymentUtils;

        // _mongoClient = new MongoClient(patentDesignDbSettings.Value.ConnectionString);
        var pdDb = _mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _performanceCollection = pdDb.GetCollection<PerformanceMarker>("performance");
        _fillingCollection = pdDb.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
        _attachmentCollection =
            pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
        _financeCollection = pdDb.GetCollection<FinanceHistory>(patentDesignDbSettings.Value.FinanceCollectionName);
    }

    public async Task<dynamic?> SearchForFile(string fileId, string? userId = null)
    {
        var res = _fillingCollection.Find(Builders<Filling>.Filter.And([
            Builders<Filling>.Filter.Eq(x => x.FileId, fileId),
            userId != null
                ? Builders<Filling>.Filter.Eq(x => x.CreatorAccount, userId)
                : Builders<Filling>.Filter.Empty,
        ])).Project(x => new
        {
            fileId = x.Id,
            fileType = x.Type,
            applicant = x.applicants.Count > 1 ? (x.applicants[0].Name + " et al.") : x.applicants[0].Name,
            applicantCountry = x.applicants[0].country,
            applicantAddress = x.applicants[0].Address,
            applicantNumber = x.applicants[0].Phone,
            applicantEmail = x.applicants[0].Email,
            fileNumber = x.FileId,
            fileTitle = x.Type == FileTypes.Design ? x.TitleOfDesign : x.Type==FileTypes.Patent? x.TitleOfInvention: x.TitleOfTradeMark,
        }).FirstOrDefault();
        Console.WriteLine(res);
        return res;
    }



    public async Task<dynamic> Generate(AssignmentType assignmentData,
        string fileID, FileTypes fileType, string userId,
        string userName, string applicantName,
        string applicantEmail, string applicantNumber)
    {
        var paymentData = _remitaPaymentUtils.GetCost(PaymentTypes.Assignment, fileType, "");
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
            paymentData.Item1, paymentData.Item3, paymentData.Item2,
            $"{fileType.ToString()} Assignment Application",
            applicantName, applicantEmail, applicantNumber);
        var application = new ApplicationInfo()
        {
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            ApplicationType = FormApplicationTypes.Assignment,
            PaymentId = rrr,
            Assignment = assignmentData,
            StatusHistory =
            [
                new ApplicationHistory()
                {
                    beforeStatus = ApplicationStatuses.None,
                    Date = DateTime.Now,
                    Message = "Saved successfully, awaiting payment",
                    UserId = userId,
                    User = userName,
                    afterStatus = ApplicationStatuses.AwaitingPayment,
                }
            ]
        };
        var data = await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, fileID),
            Builders<Filling>.Update.Push(x => x.ApplicationHistory, application));
        return new
        {
            rrr,
            data,
            application.id,
            cost = paymentData.Item1
        };
    }

    public async Task<dynamic?> PayAssignment(AssUpdateReq data)
    {
        var status=await ValidationRRR(data.paymentId);
        if (status.Item1)
        {
            var latestStatus = new ApplicationHistory()
            {
                Message = data.reason,
                UserId = data.userId,
                User = data.userName,
                Date = DateTime.Now,
                afterStatus = data.newStatus,
                beforeStatus = data.currentStatus
            };
            var operations = new List<UpdateDefinition<Filling>>()
            {
                Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", data.newStatus),
                Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory", latestStatus)
            };

            var apps = _fillingCollection.Find(x => x.Id == data.fileId).Project(x =>
                    new
                    {
                        x.ApplicationHistory, x.Type, x.TitleOfInvention, x.TitleOfTradeMark, x.FileId,
                        x.applicants[0].country
                    })
                .FirstOrDefault();
            operations.Add(Builders<Filling>.Update.AddToSetEach("ApplicationHistory.$.ApplicationLetters",
                [ApplicationLetters.AssignmentReceipt, ApplicationLetters.AssignmentAck]));
            var updatedAck = await _fillingCollection.FindOneAndUpdateAsync(
                Builders<Filling>.Filter.And([
                    Builders<Filling>.Filter.Eq(x => x.Id, data.fileId),
                    Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == data.applicationId)
                ]),
                Builders<Filling>.Update.Combine(operations), new FindOneAndUpdateOptions<Filling>()
                {
                    ReturnDocument = ReturnDocument.After
                });
            AddToFinance(
                $"{apps.Type} Assignment Application",
                apps.country,
                data.fileId,
                data.applicationId,
                apps.Type,
                status.Item3);
            savePerformance(PerformanceType.Application, FormApplicationTypes.Assignment, null, null, 
                DateTime.Now, data.userName, data.fileId, updatedAck.Type, updatedAck.PatentType, updatedAck.DesignType, updatedAck.TrademarkType);
            return true;
        }

        return null;
    }
    public async Task<Filling?> UpdateAssignmentStatus(AssUpdateReq data)
    {
        var latestStatus = new ApplicationHistory()
        {
            Message = data.reason,
            UserId = data.userId,
            User = data.userName,
            Date = DateTime.Now,
            afterStatus = data.newStatus,
            beforeStatus = data.currentStatus
        };
        var operations = new List<UpdateDefinition<Filling>>()
        {
            Builders<Filling>.Update.Set("ApplicationHistory.$.CurrentStatus", data.newStatus),
            Builders<Filling>.Update.Push("ApplicationHistory.$.StatusHistory", latestStatus)
        };

        if (data.newStatus == ApplicationStatuses.Approved &&
            data.currentStatus == ApplicationStatuses.AwaitingExaminer)
        {
            var filters = Builders<Filling>.Filter.And([
                Builders<Filling>.Filter.Eq(x => x.Id, data.fileId),
                Builders<Filling>.Filter.ElemMatch(x => x.ApplicationHistory, d => d.id == data.applicationId)
            ]);
            var fileData = await _fillingCollection.Find(filters).Project(x => new
            {
                x.FileId,
                x.Type,
                ApplicationHistory = x.ApplicationHistory.FirstOrDefault(x => x.id == data.applicationId),
                x.Correspondence,
            }).FirstAsync();
            var signature =
                await (new HttpClient()).GetByteArrayAsync(data.signatureUrl);
            var approval=await GenerateAndSaveCertificate(new AssignmentCertificateType()
            {
                fileNumber = fileData.FileId,
                applicantName = fileData.ApplicationHistory.Assignment.assignorName,
                CorrespondenceType = fileData.Correspondence,
                paymentDate = fileData.ApplicationHistory.StatusHistory.FirstOrDefault(x =>
                    x.beforeStatus == ApplicationStatuses.AwaitingPayment &&
                    x.afterStatus == ApplicationStatuses.AwaitingSearch).Date,
                examinerName = data.userName,
                examinerSignature = signature,
                assignmentType = new AssignmentType()
                {
                    assignorName = fileData.ApplicationHistory.Assignment.assignorName,
                    assignorAddress = fileData.ApplicationHistory.Assignment.assignorAddress,
                    assignorCountry = fileData.ApplicationHistory.Assignment.assignorCountry,
                    dateOfAssignment = fileData.ApplicationHistory.Assignment.dateOfAssignment,
                    assigneeName = fileData.ApplicationHistory.Assignment.assigneeName,
                    assigneeAddress = fileData.ApplicationHistory.Assignment.assigneeAddress,
                    assigneeCountry = fileData.ApplicationHistory.Assignment.assigneeCountry,
                }
            }, fileData.Type);
            operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.Assignment.acceptanceUrl", approval));
            
        }

        if (data.newStatus == ApplicationStatuses.Rejected &&
            data.currentStatus == ApplicationStatuses.AwaitingExaminer)
        {
            var filters = Builders<Filling>.Filter.And([
                Builders<Filling>.Filter.Eq(x => x.Id, data.fileId),
                Builders<Filling>.Filter.ElemMatch(x => x.ApplicationHistory, d => d.id == data.applicationId)
            ]);
            var fileData = await _fillingCollection.Find(filters).Project(x => new
            {
                x.FileId,
                x.Type,
                ApplicationHistory = x.ApplicationHistory.FirstOrDefault(x => x.id == data.applicationId),
                x.Correspondence,
            }).FirstAsync();
            var signature =
                await (new HttpClient()).GetByteArrayAsync(data.signatureUrl);
            var rejection =await GenerateAndSaveRejection(new AssignmentCertificateType()
            {
                fileNumber = fileData.FileId,
                applicantName = fileData.ApplicationHistory.Assignment.assignorName,
                CorrespondenceType = fileData.Correspondence,
                paymentDate = fileData.ApplicationHistory.StatusHistory.FirstOrDefault(x =>
                    x.beforeStatus == ApplicationStatuses.AwaitingPayment &&
                    x.afterStatus == ApplicationStatuses.AwaitingSearch).Date,
                examinerName = data.userName,
                examinerSignature = signature,
                assignmentType = new AssignmentType()
                {
                    assignorName = fileData.ApplicationHistory.Assignment.assignorName,
                    assignorAddress = fileData.ApplicationHistory.Assignment.assignorAddress,
                    assignorCountry = fileData.ApplicationHistory.Assignment.assignorCountry,
                    dateOfAssignment = fileData.ApplicationHistory.Assignment.dateOfAssignment,
                    assigneeName = fileData.ApplicationHistory.Assignment.assigneeName,
                    assigneeAddress = fileData.ApplicationHistory.Assignment.assigneeAddress,
                    assigneeCountry = fileData.ApplicationHistory.Assignment.assigneeCountry,
                }
            }, data.reason);
            operations.Add(Builders<Filling>.Update.Set("ApplicationHistory.$.Assignment.rejectionUrl", rejection));
        }

        if (data.currentStatus == ApplicationStatuses.Approved && data.newStatus != ApplicationStatuses.Approved)
        {
            operations.Add(
                Builders<Filling>.Update.Set<string?>("ApplicationHistory.$.Assignment.acceptanceUrl", null));
        }

        if (data.currentStatus == ApplicationStatuses.Rejected && data.newStatus != ApplicationStatuses.Approved)
        {
            operations.Add(Builders<Filling>.Update.Set<string?>("ApplicationHistory.$.Assignment.rejectionUrl", null));
        }

        var updatedAck = await _fillingCollection.FindOneAndUpdateAsync(
            Builders<Filling>.Filter.And([
                Builders<Filling>.Filter.Eq(x => x.Id, data.fileId),
                Builders<Filling>.Filter.ElemMatch(f => f.ApplicationHistory, f => f.id == data.applicationId)
            ]),
            Builders<Filling>.Update.Combine(operations), new FindOneAndUpdateOptions<Filling>()
            {
                ReturnDocument = ReturnDocument.After
            });
        savePerformance(PerformanceType.Staff, FormApplicationTypes.Assignment, data.currentStatus, data.newStatus, 
            DateTime.Now, data.userName, data.fileId, updatedAck.Type, updatedAck.PatentType, updatedAck.DesignType, updatedAck.TrademarkType);

        return updatedAck;
    }


    private async Task<string> SaveReceipt(Receipt dataReceipt)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
        var uri=$"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        Filling model = new Filling();
        var bytes= new ReceiptModel(dataReceipt, uri, model).GeneratePdf();
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
    
    //private async Task<string?> SaveAck(AssignmentType data, FileTypes fileType, string fileNumber, string title, string rrr)
    //{
    //    var trustedFileName = Path.GetRandomFileName();
    //    trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
    //    var uri=$"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
    //    var bytes= new AssignmentAcknowledgement(data, fileType, title, fileNumber, rrr).GeneratePdf();
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
    
    
    private async Task<string> GenerateAndSaveCertificate(
        AssignmentCertificateType certificateType, FileTypes type)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri =
            $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        
        var bytes = new AssignmentCertificate(certificateType, type).GeneratePdf();
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
    
    private async Task<string> GenerateAndSaveRejection(
        AssignmentCertificateType certificateType, string reason)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
        var uri =
            $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        
        var bytes = new AssignmentRejection(certificateType, reason).GeneratePdf();
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

    public async Task<(bool, double?,RemitaResponseClass)> ValidationRRR(string rrr)
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
        var obj = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod);
        if (obj.status == "00")
        {
            return (true, obj.amount, obj);
        }
        else
        {
            return (false, obj.amount, obj);
        }
    }

    private void AddToFinance(string reason, string country, string fileId, string applicationId, 
        FileTypes type, RemitaResponseClass response)
    {

        var history = _remitaPaymentUtils.GenerateHistory(
            reason,
            country,
            applicationId,
            fileId,
            response,
            type
        );
        _financeCollection.InsertOne(history);
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

}