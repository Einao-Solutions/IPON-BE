using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using QuestPDF.Fluent;
using Tfunctions.pdfs;

public class OppositionService
{
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private static IMongoCollection<Opposition> _oppositionCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;

    private PaymentUtils _remitaPaymentUtils;
    private FileServices _fileServices;
    private MongoClient _mongoClient;
    private EmailServices _emailServices;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";
    public OppositionService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, FileServices fileServices, EmailServices emailServices)
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
        _remitaPaymentUtils = remitaPaymentUtils;
        _fileServices = fileServices;
        _emailServices = emailServices;
        var pdDb = _mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _fillingCollection = pdDb.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
        _attachmentCollection =
            pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
        _oppositionCollection =
            pdDb.GetCollection<Opposition>(patentDesignDbSettings.Value.OppositionCollectionName);
        _financeCollection = pdDb.GetCollection<FinanceHistory>(patentDesignDbSettings.Value.FinanceCollectionName);


    }
    public async Task<OppositionSearchDto> OppositionSearch(string fileNumber)
    {
        try
        {
            var file = await _fillingCollection.Find(f=>f.FileId == fileNumber).FirstOrDefaultAsync(); 
            if (file == null) throw new Exception("File not found");
            string title;
            switch (file.Type)
            {
                case FileTypes.TradeMark:
                    title = file.TitleOfTradeMark;
                    break;
                case FileTypes.Design:
                    title = file.TitleOfDesign;
                    break;
                case FileTypes.Patent:
                    title = file.TitleOfInvention;
                    break;
                default:
                    title = file.TitleOfTradeMark;
                    break;
            }
            
            var applicant = file.applicants.FirstOrDefault();
            if(file.FileStatus != ApplicationStatuses.Publication) throw new Exception("Only Files in Publication can be opposed.");
            var repAttachment = file?.Attachments.FirstOrDefault(a => a.name == "representation" && a.url != null && a.url.Count > 0);
            var cost = _remitaPaymentUtils.GetCost(PaymentTypes.Opposition, file?.Type, applicant?.country, null, null,
                null);
            Console.WriteLine(cost);
            if (string.IsNullOrEmpty(cost.Item1) &&
                string.IsNullOrEmpty(cost.Item2) &&
                string.IsNullOrEmpty(cost.Item3))
            {
                throw new Exception("Failed to get cost");
            }

            var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(cost.Item1, cost.Item3, cost.Item2,
                "New Opposition", applicant.Name, applicant.Email, applicant.Phone);
            if (rrr == null) throw new Exception("Unable to Generate RRR");
            var projection = new OppositionSearchDto
            {
                FileNumber = file.FileId,
                FileTitle = title,
                Class = file.TrademarkClass,
                ApplicantName = applicant.Name, 
                RepresentationUrl = repAttachment?.url.FirstOrDefault(),
                Cost = cost.Item1,
                PaymentId = rrr,
                ServiceFee = cost.Item3,
                FileId = file.Id
            };
            return projection;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
    public async Task<bool> SubmitOpposition(OppositionRequestDto data)
    {
        try
        {
            var oppDocUrls = new List<string>();
            
            if (data?.SupportingDocs?.Count > 0)
            {
                Console.WriteLine("Uploading supporting docs");
                foreach (var (doc, i) in data.SupportingDocs.Select((doc, idx) => (doc, idx)))
                {
                    using var ms = new MemoryStream();
                    await doc.CopyToAsync(ms);

                    var oppDoc = ms.ToArray();
                    var url = await _fileServices.UploadAttachment(new List<TT>
                    {
                        new TT
                        {
                            contentType = doc.ContentType,
                            data = oppDoc,
                            fileName = Path.GetFileName(doc.FileName),
                            Name = $"Opposition Document {i + 1}"
                        }
                    });
                    oppDocUrls.Add(url[0]);
                }
            }

            Console.WriteLine("Creating new opposition");
            var oppose = new Opposition
            {
                FileNumber = data.FileNumber,
                Name = data.Name,
                OppositionDate = null,
                PaymentId = data.PaymentId,
                Phone = data.Phone,
                Email = data.Email,
                Address = data.Address,
                Nationality = data.Nationality,
                Reason = data.Reason,
                SupportingDocs = oppDocUrls,
                Status = ApplicationStatuses.NewOpposition,
                FileTitle = data.FileTitle,
                FileId = data.FileId,
            };
            await _oppositionCollection.InsertOneAsync(oppose);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<bool> UpdateOppositionPaymentStatus(string paymentId)
    {
        try
        {
            var opp = await _oppositionCollection.Find(x => x.PaymentId == paymentId).FirstOrDefaultAsync();
            opp.Paid = true;
            opp.OppositionDate = DateTime.Now;
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(x => x.PaymentId, paymentId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(x => x.Paid, true),
                    Builders<Opposition>.Update.Set(x => x.OppositionDate, DateTime.Now)
                ));

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<List<Opposition>> GetOppositionRequests()
    {
        try
        {
            var opps = await _oppositionCollection.Find(o => o.Paid == true).ToListAsync();
            return opps;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    public async Task<bool> NotifyApplicant(string oppositionId)
    {
        try
        {
            var opp = await _oppositionCollection.Find(x => x.id == oppositionId).FirstOrDefaultAsync();
            if (opp == null) throw new Exception("Opposition not found");
            var date =  opp.OppositionDate.ToString();
            var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
            if (file == null) throw new Exception("File not found");
            var app = file.applicants.FirstOrDefault();
            if (app == null) throw new Exception("Applicant not found");
            var mail = new OppositionEmailDto
            {
                To = app.Email,
                Subject = "Important Notice! Opposition Filed Against Your Trademark Application",
                OppositionDate = date,
                ApplicantName = app.Name,
                FileNumber = file.FileId,
                Reason = opp.Reason,
                SignatoryName = "John Doe",
                OpposerName = opp.Name,
                Title = opp.FileTitle
            };
            var res = await _emailServices.NotifyApplicantMail(mail);
            if (res)
            {
                opp.Status = ApplicationStatuses.AwaitingCounter;
                opp.ApplicantNotified = true;
                opp.ApplicantNotifiedDate = DateTime.Now;
                
                await _oppositionCollection.UpdateOneAsync(
                    Builders<Opposition>.Filter.Eq(x => x.id, oppositionId),
                    Builders<Opposition>.Update.Combine(
                        Builders<Opposition>.Update.Set(x => x.Status, ApplicationStatuses.AwaitingCounter),
                        Builders<Opposition>.Update.Set(x => x.ApplicantNotified, true),
                        Builders<Opposition>.Update.Set(x => x.ApplicantNotifiedDate, DateTime.Now)
                    ));
            }
            file.FileStatus = ApplicationStatuses.AwaitingCounter;
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, file.FileId),
                Builders<Filling>.Update.Combine(
                Builders<Filling>.Update.Push(f => f.Oppositions, opp),
                Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.Opposition)
                ));

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    public async Task<long> GetOppositionCount()
    {
        var total = await _oppositionCollection.CountDocumentsAsync(o => o.Paid == true);
        return total;
    }
    // public async Task<object> AddNewOpposition(
    //     string description,
    //     string name, string email,
    //     string number, string address, string fileUrl, string fileID, string title, string userId, string userName)
    // {
    //     var details =
    //         await _remitaPaymentUtils.GenerateOppositionID(PaymentTypes.OppositionCreation, description, name, email,
    //             number);
    //     var fileCreator=await _fillingCollection.Find(x => x.Id == fileID).Project(x => x.CreatorAccount).FirstOrDefaultAsync();
    //     var opposition = new OppositionType()
    //     {
    //         fileId = fileID,
    //         creatorId = userId,
    //         fileCreatorId = fileCreator,
    //         address = address,
    //         email = email,
    //         number = number,
    //         name = name,
    //         title = title,
    //         creationPaymentID = details.Item1,
    //         currentStatus = ApplicationStatuses.AwaitingPayment,
    //         created = DateTime.Now,
    //         oppositionFile = fileUrl,
    //         history =
    //         [
    //             new ApplicationHistory()
    //             {
    //                 beforeStatus = ApplicationStatuses.None,
    //                 afterStatus = ApplicationStatuses.AwaitingPayment,
    //                 Date = DateTime.Now,
    //                 Message = "Opposition saved, awaiting payment",
    //                 User = userName,
    //                 UserId = userId
    //             }
    //         ],
    //
    //     };
    //     await _oppositionCollection.InsertOneAsync(opposition);
    //     return new
    //     {
    //         rrr = details.Item1,
    //         amount = details.Item2,
    //         id = opposition.Id
    //     };
    // }
    //
    // public async Task<OppositionType> AddResponse(OppResReq data)
    // {
    //
    //     var status=await ValidatePayment(data.paymentId);
    //     if (status.status == "00")
    //     {
    //         AddToFinance("Opposition Response Application", "-", "-",
    //             "-", FileTypes.TradeMark, status);
    //     }
    //     var response = await _oppositionCollection.FindOneAndUpdateAsync(
    //         Builders<OppositionType>.Filter.Eq(x => x.Id, data.oppositionID),
    //         Builders<OppositionType>.Update.Combine([
    //             Builders<OppositionType>.Update.Set(x => x.responseFile, data.fileUrl),
    //             Builders<OppositionType>.Update.Set(x => x.responseReceiptUrl, ApplicationLetters.OppositionResponseReceipt),
    //             Builders<OppositionType>.Update.Set(x => x.responseAckUrl, ApplicationLetters.OppositionResponseAck),
    //             Builders<OppositionType>.Update.Set(x => x.responseName, data.name),
    //             Builders<OppositionType>.Update.Set(x => x.responsePaymentId, data.paymentId),
    //             Builders<OppositionType>.Update.Set(x => x.responseAddress, data.address),
    //             Builders<OppositionType>.Update.Set(x => x.responseEmail, data.email),
    //             Builders<OppositionType>.Update.Set(x => x.responseNumber, data.number),
    //             Builders<OppositionType>.Update.Set(x => x.currentStatus, ApplicationStatuses.AwaitingResolution),
    //             Builders<OppositionType>.Update.Push(x => x.history, new ApplicationHistory()
    //             {
    //                 Message = "Counter statement added, awaiting resolution",
    //                 UserId = data.userId,
    //                 User = data.userName,
    //                 Date = DateTime.Now,
    //                 afterStatus = ApplicationStatuses.AwaitingResolution,
    //                 beforeStatus = ApplicationStatuses.AwaitingResponse
    //             }),
    //         ]), new FindOneAndUpdateOptions<OppositionType>()
    //         {
    //             ReturnDocument = ReturnDocument.After
    //         });
    //     return response;
    // }
    //
    // public async Task<object?> AddResolution(OppResReq data)
    // {
    //     // var receipt =
    //     //     await GenerateAndSaveReceipt("", data.amount, data.paymentId, data.name, data.description, DateTime.Now);
    //     // var ack = await GenerateAndSaveAcknowledgement(new OppositionAckType()
    //     // {
    //     //     address = data.address,
    //     //     description = data.description,
    //     //     email = data.email,
    //     //     number = data.number,
    //     //     name = data.name,
    //     //     date = DateTime.Now,
    //     //     paymentId = data.paymentId,
    //     // });
    //     var status = await ValidatePayment(data.paymentId);
    //     if (status.status == "00")
    //     {
    //         AddToFinance("Opposition Resolution Application", "-", "-",
    //             "-", FileTypes.TradeMark, status);
    //     }
    //
    //     var response = await _oppositionCollection.FindOneAndUpdateAsync(
    //         Builders<OppositionType>.Filter.Eq(x => x.Id, data.oppositionID),
    //         Builders<OppositionType>.Update.Combine([
    //             Builders<OppositionType>.Update.Set(x => x.resolutionFile, data.fileUrl),
    //             Builders<OppositionType>.Update.Set(x => x.resolutionReceipt, ApplicationLetters.OppositionResolutionReceipt),
    //             Builders<OppositionType>.Update.Set(x => x.resolutionAcknowledgement, ApplicationLetters.OppositionResolutionAck),
    //             Builders<OppositionType>.Update.Set(x => x.resolutionpaymentId, data.paymentId),
    //             Builders<OppositionType>.Update.Set(x => x.currentStatus, ApplicationStatuses.AwaitingOppositionStaff),
    //             Builders<OppositionType>.Update.Push(x => x.history, new ApplicationHistory()
    //             {
    //                 Message = "Resolution added, awaiting opposition office",
    //                 UserId = data.userId,
    //                 User = data.userName,
    //                 Date = DateTime.Now,
    //                 afterStatus = ApplicationStatuses.AwaitingOppositionStaff,
    //                 beforeStatus = ApplicationStatuses.AwaitingResolution
    //             }),
    //         ]));
    //     return response;
    // }
    //
    // public async Task<OppositionType?> UpdateOppositionStatus(AssUpdateReq data)
    // {
    //     var latestStatus = new ApplicationHistory()
    //     {
    //         Message = data.reason,
    //         UserId = data.userId,
    //         User = data.userName,
    //         Date = DateTime.Now,
    //         afterStatus = data.newStatus,
    //         beforeStatus = data.currentStatus
    //     };
    //     var operations = new List<UpdateDefinition<OppositionType>>()
    //     {
    //         Builders<OppositionType>.Update.Set(x => x.currentStatus, data.newStatus),
    //         Builders<OppositionType>.Update.Push(x => x.history, latestStatus)
    //     };
    //     if (data is
    //         {
    //             currentStatus: ApplicationStatuses.AwaitingPayment, newStatus: ApplicationStatuses.AwaitingResponse
    //         })
    //     {
    //         var oppositionInfo = await _oppositionCollection.Find(x => x.Id == data.applicationId)
    //             .FirstOrDefaultAsync();
    //         operations.Add(
    //             Builders<OppositionType>.Update.Set(x => x.recepitUrl, ApplicationLetters.NewOppositionReceipt));
    //         operations.Add(Builders<OppositionType>.Update.Set(x => x.ackUrl, ApplicationLetters.NewOppositionAck));
    //         var updated=await _fillingCollection.FindOneAndUpdateAsync(Builders<Filling>.Filter.Eq(x => x.Id, oppositionInfo.fileId),
    //             Builders<Filling>.Update.Combine([
    //                 Builders<Filling>.Update.Push(x => x.ApplicationHistory[0].StatusHistory, new ApplicationHistory()
    //                 {
    //                     beforeStatus = ApplicationStatuses.Publication,
    //                     afterStatus = ApplicationStatuses.Opposition,
    //                     Message = "New opposition filled",
    //                     Date = DateTime.Now,
    //                     User = data.userName,
    //                     UserId = data.userId
    //                 }),
    //             ]), new FindOneAndUpdateOptions<Filling>()
    //             {
    //                 ReturnDocument = ReturnDocument.After
    //             });
    //
    //         var status=await ValidatePayment(data.paymentId);
    //         if (status.status == "00")
    //         {
    //             AddToFinance("New Opposition Application", updated.applicants[0].country, updated.Id,
    //                 updated.ApplicationHistory[0].id, updated.Type, status);
    //         }
    //     }
    //
    //     if (data.newStatus == ApplicationStatuses.Resolved &&
    //         data.currentStatus == ApplicationStatuses.AwaitingOppositionStaff)
    //     {
    //     }
    //
    //     var updatedAck = await _oppositionCollection.FindOneAndUpdateAsync(
    //         Builders<OppositionType>.Filter.Eq(x => x.Id, data.applicationId),
    //         Builders<OppositionType>.Update.Combine(operations), new FindOneAndUpdateOptions<OppositionType>()
    //         {
    //             ReturnDocument = ReturnDocument.After
    //         });
    //     return updatedAck;
    // }
    //
    //
    // private async Task<string> GenerateAndSaveReceipt(
    //     string type, string amount, string paymentId, string name, string title, DateTime date)
    // {
    //     var trustedFileName = Path.GetRandomFileName();
    //     trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
    //     var uri =
    //         $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
    //     var bytes = new OppositionReceipt(new OppositionReceiptType()
    //     {
    //        amount=amount,
    //        paymentId = paymentId, 
    //        name=name, 
    //        description=title, 
    //        date = date
    //     }, uri).GeneratePdf();
    //     using (var ms = new MemoryStream(bytes))
    //     {
    //         await _attachmentCollection.InsertOneAsync(new AttachmentInfo
    //         {
    //             Id = trustedFileName,
    //             ContentType = "application/pdf",
    //             Data = ms.ToArray()
    //         });
    //     }
    //     return uri;
    // }
    //
    //
    //
    // private async Task<string> GenerateAndSaveAcknowledgement(OppositionAckType data)
    // {
    //     var trustedFileName = Path.GetRandomFileName();
    //     trustedFileName = trustedFileName.Split(".")[0] + $".pdf";
    //     var uri =
    //         $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
    //     var bytes = new OppositionAcknowledgement(data, uri).GeneratePdf();
    //     using (var ms = new MemoryStream(bytes))
    //     {
    //         await _attachmentCollection.InsertOneAsync(new AttachmentInfo
    //         {
    //             Id = trustedFileName,
    //             ContentType = "application/pdf",
    //             Data = ms.ToArray()
    //         });
    //     }
    //
    //     return uri;
    // }
    //
    //
    public async Task<Object> LoadSummary(int quantity, int skip, ApplicationStatuses? status)
    {
        var filter = Builders<Opposition>.Filter.And([
            status != null
                ? Builders<Opposition>.Filter.Eq(x => x.Status, status)
                : Builders<Opposition>.Filter.Empty,
            Builders<Opposition>.Filter.Eq(x => x.Paid, true)
        ]);
        var count = _oppositionCollection.CountDocuments(filter);
        var result = await _oppositionCollection.Find(filter).Project(x => new
        {
            x.FileNumber,
            x.Name,
            x.PaymentId,
            x.Email,
            x.Address,
            x.OppositionDate,
            x.Status,
            x.FileTitle,
            x.FileId,
            x.id
        }).Skip(skip).Limit(quantity).ToListAsync();
        return new {data= result, count=count};
    }
    // public async Task<object?> Count(string? userId = null)
    // {
    //     var resolved = _oppositionCollection.CountDocuments(Builders<OppositionType>.Filter.And(
    //         [
    //             Builders<OppositionType>.Filter.Eq(x => x.currentStatus, ApplicationStatuses.Resolved),
    //             userId == null
    //                 ? Builders<OppositionType>.Filter.Empty
    //                 : Builders<OppositionType>.Filter.Or([
    //                     Builders<OppositionType>.Filter.Eq(x => x.fileCreatorId, userId),
    //                     Builders<OppositionType>.Filter.Eq(x => x.creatorId, userId),
    //                 ])
    //         ]
    //     ));
    //     var staff = _oppositionCollection.CountDocuments(
    //         Builders<OppositionType>.Filter.And(
    //             [
    //                 Builders<OppositionType>.Filter.Eq(x => x.currentStatus,
    //                     ApplicationStatuses.AwaitingOppositionStaff),
    //                 userId == null
    //                     ? Builders<OppositionType>.Filter.Empty
    //                     : Builders<OppositionType>.Filter.Or([
    //                         Builders<OppositionType>.Filter.Eq(x => x.fileCreatorId, userId),
    //                         Builders<OppositionType>.Filter.Eq(x => x.creatorId, userId),
    //                     ])
    //             ]
    //         ));
    //     var response =
    //         _oppositionCollection.CountDocuments(Builders<OppositionType>.Filter.And(
    //             [
    //                 Builders<OppositionType>.Filter.Eq(x => x.currentStatus, ApplicationStatuses.AwaitingResponse),
    //                 userId == null
    //                     ? Builders<OppositionType>.Filter.Empty
    //                     : Builders<OppositionType>.Filter.Or([
    //                         Builders<OppositionType>.Filter.Eq(x => x.fileCreatorId, userId),
    //                         Builders<OppositionType>.Filter.Eq(x => x.creatorId, userId),
    //                     ])
    //             ]
    //         ));
    //     var resolution =
    //         _oppositionCollection.CountDocuments(Builders<OppositionType>.Filter.And(
    //             [
    //                 Builders<OppositionType>.Filter.Eq(x => x.currentStatus, ApplicationStatuses.AwaitingResolution),
    //                 userId == null
    //                     ? Builders<OppositionType>.Filter.Empty
    //                     : Builders<OppositionType>.Filter.Or([
    //                         Builders<OppositionType>.Filter.Eq(x => x.fileCreatorId, userId),
    //                         Builders<OppositionType>.Filter.Eq(x => x.creatorId, userId),
    //                     ])
    //             ]
    //         ));
    //     var payment = _oppositionCollection.CountDocuments(Builders<OppositionType>.Filter.And(
    //         [
    //             Builders<OppositionType>.Filter.Eq(x => x.currentStatus, ApplicationStatuses.AwaitingPayment),
    //             userId == null
    //                 ? Builders<OppositionType>.Filter.Empty
    //                 : Builders<OppositionType>.Filter.Or([
    //                     Builders<OppositionType>.Filter.Eq(x => x.creatorId, userId),
    //                 ])
    //         ]
    //     ));
    //     var data= new
    //     {
    //         resolved, staff, response, resolution, payment
    //     };
    //     return data;
    // }
    public async Task<Opposition?> GetOpposition(string id)
    {
        return await _oppositionCollection.Find(x => x.id == id).FirstOrDefaultAsync();
    }

    public async Task<OppositionStatsDto> GetStats()
    {
        var stats = new OppositionStatsDto();
        long awaitingCounter = _oppositionCollection.CountDocuments(Builders<Opposition>.Filter.Eq(x => x.Status, ApplicationStatuses.AwaitingCounter));
        long newOpps =
            _oppositionCollection.CountDocuments(
                Builders<Opposition>.Filter.Eq(o => o.Status, ApplicationStatuses.NewOpposition));
        
        stats.AwaitingCounter = awaitingCounter;
        stats.NewOpposition = newOpps;
        return stats;
    }
    //
    // public async Task<List<ApplicationHistory>> GetOppositionHistory(string id)
    // {
    //     return await 
    //         _oppositionCollection.Find
    //             (x => x.Id == id).Project(x => x.history).FirstOrDefaultAsync();
    // }
    // public async Task<object> Generate(GenerateOpReq data)
    // {
    //     if (data.type == "resolution")
    //     {
    //         var dt = await _oppositionCollection.Find(x => x.Id == data.oppositionID).Project(
    //             x => new
    //             {
    //                 x.name,
    //                 x.email,
    //                 x.number,
    //             }).FirstOrDefaultAsync();
    //         data.name = dt.name;
    //         data.email = dt.email;
    //         data.number = dt.number;
    //     }
    //     if (data.type==""){}
    //     var result= await _remitaPaymentUtils.GenerateOppositionID(PaymentTypes.OppositionCreation,
    //         data.description, data.name, data.email, data.number);
    //     return new
    //     {
    //         rrr = result.Item1,
    //         amount = result.Item2
    //     };
    // }
    //
    // private void AddToFinance(string reason, string country, string fileId, string applicationId,
    //     FileTypes type, RemitaResponseClass response)
    // {
    //
    //     var history = _remitaPaymentUtils.GenerateHistory(
    //         reason,
    //         country,
    //         applicationId,
    //         fileId,
    //         response,
    //         type
    //     );
    //     _financeCollection.InsertOne(history);
    // }
    //
    // private async Task<RemitaResponseClass> ValidatePayment(string rrr)
    // {
    //     const string merchantId = "6230040240";
    //     const string apiKey = "192753";
    //     var test = rrr + apiKey + merchantId;
    //     var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
    //     var hash = Convert.ToHexString(apiHash).ToLower();
    //     var transactionStatusUrl =
    //         $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{rrr}/{hash}/status.reg";
    //     var client = new HttpClient();
    //     var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
    //     request.Headers.TryAddWithoutValidation("Authorization",
    //         $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");
    //     var response = await client.SendAsync(request);
    //     var dataMod = await response.Content.ReadAsStringAsync();
    //     var obj = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod);
    //     return obj;
     //}
}