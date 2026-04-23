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
    private static IMongoCollection<CounterStatement> _counterStatementCollection;
    private static IMongoCollection<StatutoryDeclaration> _statutoryDeclarationCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;
    private static IMongoCollection<PublicationInfo> _publicationCollection;
    private static IMongoCollection<AppUser> _userCollection;
    private readonly ILogger<OppositionService> _log;

    private PaymentUtils _remitaPaymentUtils;
    private FilesServices _fileServices;
    private MongoClient _mongoClient;
    private EmailServices _emailServices;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";
    public OppositionService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, FilesServices fileServices, EmailServices emailServices, ILogger<OppositionService> log)
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
        _counterStatementCollection =
            pdDb.GetCollection<CounterStatement>(patentDesignDbSettings.Value.CounterStatementsCollectionName);
        _statutoryDeclarationCollection =
            pdDb.GetCollection<StatutoryDeclaration>(patentDesignDbSettings.Value.StatutoryDeclarationsCollectionName);
        _financeCollection = pdDb.GetCollection<FinanceHistory>(patentDesignDbSettings.Value.FinanceCollectionName);
        _log = log;
        _publicationCollection = pdDb.GetCollection<PublicationInfo>("trademarkJournal");
        _userCollection = pdDb.GetCollection<AppUser>("appUsers");
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
    public async Task<string> SubmitOpposition(OppositionRequestDto data)
    {
        _log.LogInformation($"Submitting Opposition {data.FileNumber}...");
        _log.LogInformation($"[DEBUG] SubmitOpposition received — UserId: '{data.UserId}', Name: '{data.Name}', Email: '{data.Email}', PaymentId: '{data.PaymentId}'");
        try
        {

            var oppDocUrls = new List<string>();
            
            if (data?.SupportingDocs?.Count > 0)
            {
                _log.LogDebug("Uploading supporting docs");
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

            _log.LogDebug("Creating new opposition");
            var oppose = new Opposition
            {
                id = Guid.NewGuid().ToString(),
                FileNumber = data.FileNumber,
                Name = data.Name,
                OppositionDate = DateTime.Now,
                PaymentId = data.PaymentId,
                Phone = data.Phone,
                Email = data.Email,
                Address = data.Address,
                Nationality = data.Nationality,
                Reason = data.Reason,
                SupportingDocs = oppDocUrls,
                Status = ApplicationStatuses.AwaitingPayment,
                FileTitle = data.FileTitle,
                FileId = data.FileId,
                UserId = data.UserId,
            };
            await _oppositionCollection.InsertOneAsync(oppose);
            _log.LogInformation($"New Opposition {oppose.FileNumber} saved");

            return oppose.id;
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
            _log.LogWarning($"Publication record not found for {opp.FileNumber} — skipping publication update");
            return true;
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

    public async Task<bool> StaffOpposition(OppositionRequestDto dto)
    {
        _log.LogInformation($"Staff Opposing Publication {dto.FileNumber}...");
        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == dto.FileNumber).FirstOrDefaultAsync();
            if (file is null || file.ApplicationHistory is null)
            {
                _log.LogError("File not found");
                throw new KeyNotFoundException("File not found");
            }
            var staff = await _userCollection.Find(u => u.Id == dto.StaffId).FirstOrDefaultAsync();
            if (staff is null)
            {
                _log.LogError("Staff user not found");
                throw new KeyNotFoundException("Staff user not found");
            }
            var userName = staff.Name ?? $"{staff.FirstName} {staff.LastName}";
            file.FileStatus = ApplicationStatuses.Opposition;
            file.ApplicationHistory[0].CurrentStatus = ApplicationStatuses.Opposition;
            file.ApplicationHistory[0].StatusHistory.Add(new ApplicationHistory
            { 
               afterStatus = ApplicationStatuses.Opposition,
               beforeStatus = ApplicationStatuses.Publication,
               Date = DateTime.Now,
               Message = dto.Reason,
               User = userName,
               UserId = dto.StaffId,
            });
            _log.LogDebug("Creating new opposition");
            var oppose = new Opposition
            {
                id = Guid.NewGuid().ToString(),
                FileNumber = dto.FileNumber,
                Name = userName,
                OppositionDate = DateTime.Now,
                PaymentId = null,
                Phone = staff.PhoneNumber,
                Email = staff.Email,
                Address = staff.Address,
                Nationality = staff.Nationality,
                Reason = dto.Reason,
                SupportingDocs = null,
                Status = ApplicationStatuses.NewOpposition,
                FileTitle = file.TitleOfTradeMark,
                FileId = dto.FileNumber,
                IsStaffOpposition = true
            };
            await _oppositionCollection.InsertOneAsync(oppose);
            _log.LogInformation($"New Opposition {oppose.FileNumber} saved");
            await OpposePublication(oppose);
            _log.LogDebug("Updating file status to Opposition");
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileNumber),
                Builders<Filling>.Update.Combine(
                    Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.Opposition),
                    Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory)
                ));
            _log.LogInformation($"Publication {dto.FileNumber} has been opposed by staff {staff.Name}");
            var perf = new PerformanceDto
            {
                AfterStatus = ApplicationStatuses.Opposition,
                ApplicationId = oppose.id,
                AppUserId = staff.Id,
                ApplicationType = FormApplicationTypes.NewApplication,
                BeforeStatus = ApplicationStatuses.Publication,
                Date = DateTime.Now,
                OfficeUnit = Roles.TrademarkOpposition,
                FileNumber = oppose.FileNumber,
                FileType = FileTypes.TradeMark
            };
            _fileServices.SavePerformance(perf);
            return true;
        }
        catch (Exception e)
        {
            _log.LogError(e,"Failed to Oppose by staff");
            throw;
        }
    }
    public async Task<bool> UpdateOppositionPaymentStatus(string paymentId)
    {
        try
        {
            var opp = await _oppositionCollection.Find(x => x.PaymentId == paymentId).FirstOrDefaultAsync();
            if (opp == null) throw new Exception("Opposition not found for this payment ID");

            // Idempotency: if already paid, return success without duplicating
            if (opp.Paid == true) return true;

            opp.Paid = true;
            opp.OppositionDate = DateTime.Now;
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(x => x.PaymentId, paymentId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(x => x.Paid, true),
                    Builders<Opposition>.Update.Set(x => x.Status, ApplicationStatuses.AwaitingCounter),
                    Builders<Opposition>.Update.Set(x => x.OppositionDate, DateTime.Now)
                ));
            // File status and opposition status — only update AFTER payment confirmed
            var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
            if (file != null)
            {
                // Save previous file status for restore on decline
                await _oppositionCollection.UpdateOneAsync(
                    Builders<Opposition>.Filter.Eq(o => o.id, opp.id),
                    Builders<Opposition>.Update.Set(o => o.PreviousFileStatus, file.FileStatus));

                // File status → NewOpposition (30), Application status → AwaitingCounter (31)
                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Combine(
                        Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.NewOpposition),
                        Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.AwaitingCounter)));
                _log.LogInformation($"File {opp.FileNumber} — FileStatus=NewOpposition(30), ApplicationStatus=AwaitingCounter(31)");
            }

            _log.LogInformation($"Opposition payment confirmed for {opp.FileNumber}");

            // Update publication record (non-fatal)
            try { await OpposePublication(opp); }
            catch (Exception ex) { _log.LogWarning(ex, "OpposePublication failed — proceeding anyway"); }

            // Notify applicant via email (non-fatal)
            try { await NotifyApplicant(opp.id); }
            catch (Exception ex) { _log.LogWarning(ex, "NotifyApplicant failed — proceeding anyway"); }

            // Send confirmation email to the opposer
            try
            {
                await _emailServices.SendMail(new EmailDto
                {
                    To        = opp.Email,
                    Subject   = "Opposition Filed Successfully",
                    EmailType = EmailType.OppositionConfirmation,
                    OppositionConfirmationMail = new OppositionConfirmationMail
                    {
                        To               = opp.Email,
                        OpposerName      = opp.Name,
                        OppositionId     = opp.id,
                        FileNumber       = opp.FileNumber,
                        FileTitle        = opp.FileTitle,
                        DateFiled        = opp.OppositionDate?.ToString("dd MMMM yyyy") ?? DateTime.Now.ToString("dd MMMM yyyy"),
                        PaymentReference = opp.PaymentId
                    }
                });
                _log.LogInformation($"Opposition confirmation email sent to opposer {opp.Email}");
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, "Failed to send opposition confirmation email — proceeding anyway");
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
            var opps = await _oppositionCollection.Find(x => x.Paid == true).ToListAsync();
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

            opp.ApplicantNotified = true;
            opp.ApplicantNotifiedDate = DateTime.Now;

            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(x => x.id, oppositionId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(x => x.ApplicantNotified, true),
                    Builders<Opposition>.Update.Set(x => x.ApplicantNotifiedDate, DateTime.Now)
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
        var total = await _oppositionCollection.CountDocumentsAsync(Builders<Opposition>.Filter.Eq(x => x.Paid, true));
        return total;
    }

    public async Task<Object> LoadSummary(int quantity, int skip, ApplicationStatuses? status)
    {
        var paidFilter = Builders<Opposition>.Filter.Eq(x => x.Paid, true);
        var filter = status != null
            ? Builders<Opposition>.Filter.And(paidFilter, Builders<Opposition>.Filter.Eq(x => x.Status, status))
            : paidFilter;
        var count = _oppositionCollection.CountDocuments(filter);
        var raw = await _oppositionCollection.Find(filter).Skip(skip).Limit(quantity).ToListAsync();
        var sn = skip;
        var result = raw.Select(x => new
        {
            sn            = ++sn,
            date          = (x.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
            title         = x.FileTitle,
            fileId        = x.FileNumber,
            name          = x.Name,
            status        = x.Status,
            paymentId     = x.PaymentId,
            id            = x.id
        }).ToList();
        return new { data = result, count };
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
        var paidFilter = Builders<Opposition>.Filter.Eq(x => x.Paid, true);
        long awaitingCounter = _oppositionCollection.CountDocuments(
            Builders<Opposition>.Filter.And(paidFilter, Builders<Opposition>.Filter.Eq(x => x.Status, ApplicationStatuses.AwaitingCounter)));
        long newOpps = _oppositionCollection.CountDocuments(
            Builders<Opposition>.Filter.And(paidFilter, Builders<Opposition>.Filter.Eq(o => o.Status, ApplicationStatuses.NewOpposition)));
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

    // ─── Counter Statement Search ───────────────────────────────────────────
    public async Task<CsSearchDto> CsSearchFile(string fileNumber)
    {
        try
        {
            _log.LogInformation($"Searching for Counter Statement file {fileNumber}...");

            // If the fileNumber is an opposition file number, resolve the original file number
            var file = await _fillingCollection.Find(f => f.FileId == fileNumber).FirstOrDefaultAsync();
            Opposition opp = null;

            if (file == null)
            {
                // Try finding via opposition record (frontend may pass opposition's FileNumber)
                opp = await _oppositionCollection.Find(o => o.FileNumber == fileNumber).FirstOrDefaultAsync();
                if (opp != null)
                    file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
            }

            if (file == null)
                return new CsSearchDto { Success = false, Message = "File not found" };

            // Accept any opposed-related status so old records are not blocked
            var allowedStatuses = new[]
            {
                ApplicationStatuses.Opposition,
                ApplicationStatuses.AwaitingCounter,
                ApplicationStatuses.NewOpposition
            };
            if (!allowedStatuses.Contains(file.FileStatus))
                return new CsSearchDto { Success = false, Message = "Counter Statement is only available for Opposed files" };

            if (opp == null)
                opp = await _oppositionCollection
                    .Find(o => o.FileNumber == fileNumber)
                    .FirstOrDefaultAsync();

            if (opp == null)
                return new CsSearchDto { Success = false, Message = "No active opposition found for this file" };

            string title = file.Type switch
            {
                FileTypes.Design => file.TitleOfDesign,
                FileTypes.Patent => file.TitleOfInvention,
                _                => file.TitleOfTradeMark
            };

            var applicant = file.applicants?.FirstOrDefault();
            var repAttachment = file.Attachments?.FirstOrDefault(a =>
                a.name != null && a.name.Contains("representation", StringComparison.OrdinalIgnoreCase));

            return new CsSearchDto
            {
                Success           = true,
                FileNumber        = file.FileId,
                FileName          = title,
                FileOwner         = applicant?.Name,
                TrademarkClass    = file.TrademarkClass,
                RepresentationUrl = repAttachment?.url?.FirstOrDefault(),
                OppositionId      = opp.id,
                Message           = null
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error searching for counter statement file");
            throw;
        }
    }

    // ─── Counter Statement Fee ───────────────────────────────────────────────
    public object GetCounterStatementFee()
    {
        var cost = _remitaPaymentUtils.GetCost(PaymentTypes.CounterStatement, null, null);
        int.TryParse(cost.Item1, out int govFee);
        int.TryParse(cost.Item3, out int svcFee);
        return new
        {
            governmentFee = govFee,
            serviceFee    = svcFee,
            total         = govFee + svcFee,
            currency      = "NGN"
        };
    }

    // ─── Submit Counter Statement ────────────────────────────────────────────
    public async Task<(bool success, OppositionSearchDto invoice, string message)> SubmitCounterStatement(CounterStatementRequestDto dto)
    {
        try
        {
            _log.LogInformation($"Submitting Counter Statement for file {dto.FileNumber}...");

            if (string.IsNullOrWhiteSpace(dto.UserId))
                return (false, null, "UserId is required");

            var opp = await _oppositionCollection
                .Find(o => o.FileNumber == dto.FileNumber || o.id == dto.FileNumber)
                .FirstOrDefaultAsync();
            if (opp == null)
                return (false, null, "No active opposition found for this file");

            var file = await _fillingCollection.Find(f => f.FileId == (opp.FileNumber ?? dto.FileNumber)).FirstOrDefaultAsync();
            if (file == null)
                return (false, null, "File not found");

            var applicant = file.applicants?.FirstOrDefault();

            string title = file.Type switch
            {
                FileTypes.Design => file.TitleOfDesign,
                FileTypes.Patent => file.TitleOfInvention,
                _                => file.TitleOfTradeMark
            };

            var repAttachment = file.Attachments?.FirstOrDefault(a =>
                a.name != null && a.name.Contains("representation", StringComparison.OrdinalIgnoreCase));

            // Upload attachments
            var attachmentUrls = new List<string>();
            if (dto.SupportingDocs?.Count > 0)
            {
                foreach (var (doc, i) in dto.SupportingDocs.Select((d, idx) => (d, idx)))
                {
                    using var ms = new MemoryStream();
                    await doc.CopyToAsync(ms);
                    var urls = await _fileServices.UploadAttachment(new List<TT>
                    {
                        new TT
                        {
                            contentType = doc.ContentType,
                            data        = ms.ToArray(),
                            fileName    = Path.GetFileName(doc.FileName),
                            Name        = $"Counter Statement Document {i + 1}"
                        }
                    });
                    attachmentUrls.Add(urls[0]);
                }
            }

            // Generate RRR
            var cost = _remitaPaymentUtils.GetCost(PaymentTypes.CounterStatement, file.Type, applicant?.country);
            var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                cost.Item1, cost.Item3, cost.Item2,
                "Counter Statement", applicant?.Name, applicant?.Email, applicant?.Phone);
            if (rrr == null)
                return (false, null, "Unable to generate payment reference");

            // Save record (pending payment)
            var cs = new CounterStatement
            {
                Id            = Guid.NewGuid().ToString(),
                OppositionId  = opp.id,
                Text          = dto.CounterStatement,
                Attachments   = attachmentUrls,
                PaymentId     = rrr,
                UserId        = dto.UserId,
                SubmittedDate = DateTime.Now
            };
            await _counterStatementCollection.InsertOneAsync(cs);
            _log.LogInformation($"Counter Statement {cs.Id} saved with RRR {rrr}");

            var invoice = new OppositionSearchDto
            {
                FileNumber        = file.FileId,
                FileTitle         = title,
                Class             = file.TrademarkClass,
                ApplicantName     = applicant?.Name,
                RepresentationUrl = repAttachment?.url?.FirstOrDefault(),
                Cost              = cost.Item1,
                PaymentId         = rrr,
                ServiceFee        = cost.Item3,
                FileId            = file.Id
            };

            return (true, invoice, "Counter Statement submitted successfully");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error submitting counter statement");
            throw;
        }
    }

    // ─── Update Counter Statement Payment (16 → 33) ──────────────────────────
    public async Task<(bool success, string message)> UpdateCounterStatementPayment(string paymentId)
    {
        try
        {
            _log.LogInformation($"Updating counter statement payment {paymentId}...");

            var cs = await _counterStatementCollection.Find(x => x.PaymentId == paymentId).FirstOrDefaultAsync();
            if (cs == null)
                return (false, "Counter statement not found for this payment ID");

            // Idempotency: if already paid, return success without duplicating
            if (cs.Paid == true)
                return (true, "Counter statement payment already confirmed");

            await _counterStatementCollection.UpdateOneAsync(
                Builders<CounterStatement>.Filter.Eq(x => x.PaymentId, paymentId),
                Builders<CounterStatement>.Update.Set(x => x.Paid, true));

            var opp = await _oppositionCollection.Find(o => o.id == cs.OppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, "Opposition not found");

            // Push counter statement into opposition record; opposition status → AwaitingStatutoryDeclaration
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, cs.OppositionId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(o => o.IsCountered, true),
                    Builders<Opposition>.Update.Set(o => o.CounteredDate, DateTime.Now.ToString()),
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.StatutoryDeclaration),
                    Builders<Opposition>.Update.Push(o => o.CounterStatements, cs)
                ));

            // Update file's application status to StatutoryDeclaration (33)
            var csFile = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
            if (csFile != null)
            {
                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.StatutoryDeclaration));
            }

            // Notify the opposer that a counter statement has been filed
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                string fileTitle = file?.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _                => file?.TitleOfTradeMark
                };
                var fileOwnerName = file?.applicants?.FirstOrDefault()?.Name ?? "File Owner";

                var mail = new CounterStatementMail
                {
                    To                   = opp.Email,
                    Subject              = "Counter Statement Filed Against Your Opposition",
                    OpposerName          = opp.Name,
                    FileOwnerName        = fileOwnerName,
                    FileNumber           = opp.FileNumber,
                    Title                = fileTitle,
                    CounterStatementDate = DateTime.Now.ToString("dd MMMM yyyy"),
                    SignatoryName        = ""
                };
                await _emailServices.SendMail(new EmailDto
                {
                    To                   = opp.Email,
                    Subject              = "Counter Statement Filed Against Your Opposition",
                    EmailType            = EmailType.CounterStatement,
                    CounterStatementMail = mail
                });
                _log.LogInformation($"Counter statement notification sent to opposer {opp.Email}");
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, "Failed to send counter statement notification email — proceeding anyway");
            }

            _log.LogInformation($"Counter statement payment confirmed. File {opp.FileNumber} moved to StatutoryDeclaration");
            return (true, "Counter statement payment confirmed and file status updated");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating counter statement payment");
            throw;
        }
    }

    // ─── Submit Statutory Declaration ────────────────────────────────────────
    public async Task<(bool success, string id, string message)> SubmitStatutoryDeclaration(StatutoryDeclarationRequestDto dto)
    {
        try
        {
            _log.LogInformation($"Submitting Statutory Declaration for opposition {dto.OppositionId}...");

            var opp = await _oppositionCollection.Find(o => o.id == dto.OppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, null, "Opposition not found");

            if (string.IsNullOrWhiteSpace(dto.UserId))
                return (false, null, "UserId is required");

            var attachmentUrls = new List<string>();
            if (dto.Attachments?.Count > 0)
            {
                foreach (var (doc, i) in dto.Attachments.Select((d, idx) => (d, idx)))
                {
                    using var ms = new MemoryStream();
                    await doc.CopyToAsync(ms);
                    var urls = await _fileServices.UploadAttachment(new List<TT>
                    {
                        new TT
                        {
                            contentType = doc.ContentType,
                            data        = ms.ToArray(),
                            fileName    = Path.GetFileName(doc.FileName),
                            Name        = $"Statutory Declaration Document {i + 1}"
                        }
                    });
                    attachmentUrls.Add(urls[0]);
                }
            }

            var sd = new StatutoryDeclaration
            {
                Id            = Guid.NewGuid().ToString(),
                OppositionId  = dto.OppositionId,
                Text          = dto.DeclarationText,
                Attachments   = attachmentUrls,
                PaymentId     = dto.PaymentId,
                UserId        = dto.UserId,
                SubmittedDate = DateTime.Now
            };

            await _statutoryDeclarationCollection.InsertOneAsync(sd);

            // Push into opposition record
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, dto.OppositionId),
                Builders<Opposition>.Update.Push(o => o.StatutoryDeclarations, sd));

            _log.LogInformation($"Statutory Declaration {sd.Id} saved");
            return (true, sd.Id, "Statutory Declaration submitted successfully");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error submitting statutory declaration");
            throw;
        }
    }

    // ─── Decline Opposition (trademark owner wins) ────────────────────────────
    public async Task<(bool success, string message)> DeclineOpposition(string oppositionId)
    {
        try
        {
            _log.LogInformation($"Declining opposition {oppositionId}...");
            var opp = await _oppositionCollection.Find(o => o.id == oppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, "Opposition not found");

            // Restore file status to what it was before the opposition
            var restoreStatus = opp.PreviousFileStatus ?? ApplicationStatuses.Publication;

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                Builders<Filling>.Update.Combine(
                    Builders<Filling>.Update.Set(f => f.FileStatus, restoreStatus),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", restoreStatus)));

            // Opposition → Resolved (19)
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, oppositionId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Resolved),
                    Builders<Opposition>.Update.Set(o => o.IsResolved, true),
                    Builders<Opposition>.Update.Set(o => o.ResolvedDate, DateTime.Now)));

            _log.LogInformation($"Opposition {oppositionId} declined. File {opp.FileNumber} restored to {restoreStatus}");
            return (true, "Opposition declined. Trademark file restored to previous status.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error declining opposition");
            throw;
        }
    }

    // ─── Uphold Opposition (opposer wins) ─────────────────────────────────────
    public async Task<(bool success, string message)> UpholdOpposition(string oppositionId)
    {
        try
        {
            _log.LogInformation($"Upholding opposition {oppositionId}...");
            var opp = await _oppositionCollection.Find(o => o.id == oppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, "Opposition not found");

            // Opposition → Approved (10)
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, oppositionId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Approved),
                    Builders<Opposition>.Update.Set(o => o.IsResolved, true),
                    Builders<Opposition>.Update.Set(o => o.ResolvedDate, DateTime.Now)));

            // Trademark file → Rejected (11)
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                Builders<Filling>.Update.Combine(
                    Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.Rejected),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.Rejected)));

            _log.LogInformation($"Opposition {oppositionId} upheld. File {opp.FileNumber} rejected.");
            return (true, "Opposition upheld. Trademark file has been rejected.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error upholding opposition");
            throw;
        }
    }

    // ─── Get Opposition Detail ────────────────────────────────────────────────
    public async Task<object> GetOppositionDetail(string? oppositionId, string? fileNumber)
    {
        try
        {
            Opposition opp = null;

            if (!string.IsNullOrEmpty(oppositionId))
                opp = await _oppositionCollection.Find(o => o.id == oppositionId).FirstOrDefaultAsync();

            if (opp == null && !string.IsNullOrEmpty(fileNumber))
                opp = await _oppositionCollection
                    .Find(o => o.FileNumber == fileNumber)
                    .SortByDescending(o => o.OppositionDate)
                    .FirstOrDefaultAsync();

            if (opp == null) return null;

            var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
            string fileName = file?.Type switch
            {
                FileTypes.Design => file.TitleOfDesign,
                FileTypes.Patent => file.TitleOfInvention,
                _                => file?.TitleOfTradeMark
            };

            var hasCounterStatement = opp.CounterStatements != null && opp.CounterStatements.Count > 0;
            var counterStatementDate = hasCounterStatement
                ? opp.CounterStatements.First().SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss")
                : null;

            return new
            {
                id                    = opp.id,
                fileNumber            = opp.FileNumber,
                fileName              = fileName,
                title                 = fileName,
                name                  = opp.Name,
                email                 = opp.Email,
                phone                 = opp.Phone,
                address               = opp.Address,
                nationality           = opp.Nationality,
                reason                = opp.Reason,
                oppositionText        = opp.Reason,
                status                = opp.Status,
                fileStatus            = file?.FileStatus,
                oppositionStatus      = file?.ApplicationHistory?.FirstOrDefault()?.CurrentStatus ?? opp.Status,
                oppositionDate        = (opp.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                paymentId             = opp.PaymentId,
                date                  = (opp.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                decision              = opp.Decision,
                resolutionStatement   = opp.ResolutionStatement,
                resolvedBy            = opp.ResolvedBy,
                hasCounterStatement   = hasCounterStatement,
                counterStatementDate  = counterStatementDate,
                supportingDocs        = opp.SupportingDocs ?? new List<string>(),
                counterStatements     = (opp.CounterStatements ?? new List<CounterStatement>()).Select(cs => new
                {
                    id            = cs.Id,
                    filedBy       = cs.UserId,
                    dateFiled     = cs.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                    statement     = cs.Text,
                    submittedDate = cs.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                    text          = cs.Text,
                    attachments   = cs.Attachments ?? new List<string>()
                }).ToList(),
                statutoryDeclarations = (opp.StatutoryDeclarations ?? new List<StatutoryDeclaration>()).Select(sd => new
                {
                    id            = sd.Id,
                    filedBy       = sd.UserId,
                    dateFiled     = sd.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                    statement     = sd.Text,
                    submittedDate = sd.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                    text          = sd.Text,
                    attachments   = sd.Attachments ?? new List<string>()
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching opposition detail");
            throw;
        }
    }

    // ─── Resolve Opposition (unified uphold/decline with decision) ────────────
    public async Task<(bool success, string message)> ResolveOpposition(ResolveOppositionDto dto)
    {
        try
        {
            _log.LogInformation($"Resolving opposition {dto.ApplicationId} with decision: {dto.Decision}...");
            var opp = await _oppositionCollection.Find(o => o.id == dto.ApplicationId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, "Opposition not found");

            var decision = dto.Decision?.ToLower();

            if (decision == "upheld")
            {
                // Opposition upheld — trademark gets rejected
                await _oppositionCollection.UpdateOneAsync(
                    Builders<Opposition>.Filter.Eq(o => o.id, dto.ApplicationId),
                    Builders<Opposition>.Update.Combine(
                        Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Approved),
                        Builders<Opposition>.Update.Set(o => o.IsResolved, true),
                        Builders<Opposition>.Update.Set(o => o.ResolvedDate, DateTime.Now),
                        Builders<Opposition>.Update.Set(o => o.Decision, "upheld"),
                        Builders<Opposition>.Update.Set(o => o.ResolutionStatement, dto.Statement),
                        Builders<Opposition>.Update.Set(o => o.ResolvedBy, dto.UserName),
                        Builders<Opposition>.Update.Set(o => o.ResolvedByUserId, dto.UserId)));

                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Combine(
                        Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.Rejected),
                        Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.Rejected)));

                _log.LogInformation($"Opposition {dto.ApplicationId} upheld. File {opp.FileNumber} rejected.");
                return (true, "Opposition upheld. Trademark file has been rejected.");
            }
            else if (decision == "declined")
            {
                // Opposition declined — file goes back to previous status
                var restoreStatus = opp.PreviousFileStatus ?? ApplicationStatuses.Publication;

                await _oppositionCollection.UpdateOneAsync(
                    Builders<Opposition>.Filter.Eq(o => o.id, dto.ApplicationId),
                    Builders<Opposition>.Update.Combine(
                        Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Resolved),
                        Builders<Opposition>.Update.Set(o => o.IsResolved, true),
                        Builders<Opposition>.Update.Set(o => o.ResolvedDate, DateTime.Now),
                        Builders<Opposition>.Update.Set(o => o.Decision, "declined"),
                        Builders<Opposition>.Update.Set(o => o.ResolutionStatement, dto.Statement),
                        Builders<Opposition>.Update.Set(o => o.ResolvedBy, dto.UserName),
                        Builders<Opposition>.Update.Set(o => o.ResolvedByUserId, dto.UserId)));

                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Combine(
                        Builders<Filling>.Update.Set(f => f.FileStatus, restoreStatus),
                        Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", restoreStatus)));

                _log.LogInformation($"Opposition {dto.ApplicationId} declined. File {opp.FileNumber} restored to {restoreStatus}.");
                return (true, "Opposition declined. Trademark file restored to previous status.");
            }
            else
            {
                // General resolution (backward compatible — no decision field)
                await _oppositionCollection.UpdateOneAsync(
                    Builders<Opposition>.Filter.Eq(o => o.id, dto.ApplicationId),
                    Builders<Opposition>.Update.Combine(
                        Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Resolved),
                        Builders<Opposition>.Update.Set(o => o.IsResolved, true),
                        Builders<Opposition>.Update.Set(o => o.ResolvedDate, DateTime.Now),
                        Builders<Opposition>.Update.Set(o => o.ResolutionStatement, dto.Statement),
                        Builders<Opposition>.Update.Set(o => o.ResolvedBy, dto.UserName),
                        Builders<Opposition>.Update.Set(o => o.ResolvedByUserId, dto.UserId)));

                _log.LogInformation($"Opposition {dto.ApplicationId} marked as resolved.");
                return (true, "Opposition resolved.");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error resolving opposition");
            throw;
        }
    }
}