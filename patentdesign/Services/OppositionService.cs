using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using QuestPDF.Fluent;
using System.Reflection.Emit;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tfunctions.pdfs;

public class OppositionService
{
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private static IMongoCollection<Opposition> _oppositionCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;
    private static IMongoCollection<PublicationInfo> _publicationCollection;
    private readonly ILogger<OppositionService> _log;

    private PaymentUtils _remitaPaymentUtils;
    private FileServices _fileServices;
    private MongoClient _mongoClient;
    private EmailServices _emailServices;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";
    public OppositionService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, FileServices fileServices, EmailServices emailServices, ILogger<OppositionService> log)
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
        _log = log;
        _publicationCollection = pdDb.GetCollection<PublicationInfo>("trademarkJournal");
    }
    public async Task<OppositionSearchDto> OppositionSearch(string fileNumber)
    {
        try
        {
            _log.LogInformation($"Searching to Oppose {fileNumber}...");
            var file = await _fillingCollection.Find(f=>f.FileId == fileNumber).FirstOrDefaultAsync();
            if (file == null)
            {
                _log.LogError("File not found");
                throw new KeyNotFoundException("File not found");
            }

            if (file.FileStatus != ApplicationStatuses.Publication)
            {
                _log.LogError("Only Files in Publication can be opposed.");
                throw new NotSupportedException("Only Files in Publication can be opposed.");
            }

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
        _log.LogInformation($"Submitting Opposition {data.FileNumber}...");
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
                id = Guid.NewGuid().ToString(),
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
            _log.LogInformation($"New Opposition {oppose.FileNumber} saved");
            

            
            _log.LogInformation("File Opposed Succesfully");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private async Task<bool> OpposePublication(Opposition opp)
    {
        var pub = await _publicationCollection.Find(p => p.FileNumber == opp.FileNumber).FirstOrDefaultAsync();
        if (pub is null)
        {
            _log.LogError("Publication not found");
            return false;
        }
        if (pub.Opposition is null)
        {
            await _publicationCollection.UpdateOneAsync(
                Builders<PublicationInfo>.Filter.Eq(p => p.FileNumber, opp.FileNumber),
                Builders<PublicationInfo>.Update.Set(p => p.Opposition, new List<Opposition>())
            );
        }
        await _publicationCollection.UpdateOneAsync(
            Builders<PublicationInfo>.Filter.Eq(p => p.FileNumber, opp.FileNumber),
            Builders<PublicationInfo>.Update.Combine(
        Builders<PublicationInfo>.Update.Set(p => p.IsOpposed, true),
            Builders<PublicationInfo>.Update.Push(p=>p.Opposition, opp)
        ));
        _log.LogInformation($"Publication {opp.FileNumber} has been opposed");
        return true;
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
            var oppResult = await OpposePublication(opp);
            if (!oppResult)
            {
                _log.LogError("Failed to oppose publication");
                return false;
            }
            var notice = await NotifyApplicant(opp.id);
            if (!notice)
            {
                _log.LogError("Failed to Notify Applicant");
                return false;
            }
            _log.LogInformation($"Opposition with payment ID {paymentId} has been marked as paid and applicant notified");
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
            var mail = new OppositionMail
            {
                To = file.Correspondence.email ?? app.Email,
                Subject = "Important Notice! Opposition Filed Against Your Trademark Application",
                OppositionDate = date,
                ApplicantName = app.Name,
                FileNumber = file.FileId,
                Reason = opp.Reason,
                SignatoryName = "",
                OpposerName = opp.Name,
                Title = opp.FileTitle
            };
            var email = new EmailDto
            {
                To = file.Correspondence.email ?? app.Email,
                CarbonCopy = app.Email,
                OppositionMail = mail,
                Subject = "Important Notice! Opposition Filed Against Your Trademark Application",
                EmailType = EmailType.Opposition
            };
            await _emailServices.SendMail(email);
            
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