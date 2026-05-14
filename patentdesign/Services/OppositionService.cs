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
using patentdesign;

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
            
            if (file.FileStatus != ApplicationStatuses.Publication && file.FileStatus != ApplicationStatuses.NewOpposition)
            {
                _log.LogError("Only Files in Publication or Opposed status can be opposed.");
                throw new NotSupportedException("Only Files in Publication or Opposed status can be opposed.");
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
            var user = await _userCollection.Find(u => u.Id == data.UserId).FirstOrDefaultAsync();
            if (user == null)
            {
                _log.LogError("User not found");
                throw new KeyNotFoundException("User not found");
            }
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

            // Check if this user already filed an opposition against this file
            var existingOpp = await _oppositionCollection.Find(o => o.FileNumber == data.FileNumber && o.UserId == data.UserId).FirstOrDefaultAsync();
            if (existingOpp != null)
            {
                throw new InvalidOperationException("You have already filed an opposition against this file.");
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
    FileOwnerId = (await _fillingCollection.Find(f => f.Id == data.FileId).FirstOrDefaultAsync())?.CreatorAccount,
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

                // File status → NewOpposition (30)
                var fileUpdate = Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.NewOpposition);

                // Only update ApplicationHistory.CurrentStatus if this is the earliest opposition on the file
                var earliestOpp = await _oppositionCollection
                    .Find(o => o.FileNumber == opp.FileNumber && o.Paid == true)
                    .SortBy(o => o.OppositionDate)
                    .FirstOrDefaultAsync();
                if (earliestOpp == null || earliestOpp.id == opp.id)
                {
                    fileUpdate = Builders<Filling>.Update.Combine(fileUpdate,
                        Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.AwaitingCounter),
                        Builders<Filling>.Update.Set("ApplicationHistory.0.PaymentId", opp.PaymentId));
                }

                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber), fileUpdate);
                _log.LogInformation($"File {opp.FileNumber} — FileStatus=NewOpposition(30)");
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
                Title = opp.FileTitle,
                OppositionId = opp.id
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

    public async Task<Object> LoadSummary(int quantity, int skip, ApplicationStatuses? status, string? userId = null)
    {
        var paidFilter = Builders<Opposition>.Filter.Eq(x => x.Paid, true);
        var baseFilter = status != null
            ? Builders<Opposition>.Filter.And(paidFilter, Builders<Opposition>.Filter.Eq(x => x.Status, status))
            : paidFilter;

        FilterDefinition<Opposition> filter;
        if (userId != null)
        {
            // Return only oppositions filed by this user
            var userFilter = Builders<Opposition>.Filter.Eq(x => x.UserId, userId);
            filter = Builders<Opposition>.Filter.And(baseFilter, userFilter);
        }
        else
        {
            // No userId — return all (for SuperAdmin/Tech roles)
            filter = baseFilter;
        }

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
        long awaitingOfficeProcess = _oppositionCollection.CountDocuments(
            Builders<Opposition>.Filter.And(paidFilter, Builders<Opposition>.Filter.Eq(o => o.Status, ApplicationStatuses.AwaitingOfficeProcess)));
        long abandoned = _oppositionCollection.CountDocuments(
            Builders<Opposition>.Filter.And(paidFilter,
                Builders<Opposition>.Filter.Eq(o => o.Status, ApplicationStatuses.Resolved),
                Builders<Opposition>.Filter.Eq(o => o.Decision, "Abandoned - No Counter Statement")));
        stats.AwaitingCounter = awaitingCounter;
        stats.NewOpposition = newOpps;
        stats.AwaitingOfficeProcess = awaitingOfficeProcess;
        stats.Abandoned = abandoned;
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

            Opposition opp = null;
            if (!string.IsNullOrEmpty(dto.OppositionId))
            {
                opp = await _oppositionCollection.Find(o => o.id == dto.OppositionId).FirstOrDefaultAsync();
            }
            if (opp == null && !string.IsNullOrEmpty(dto.FileNumber))
            {
                opp = await _oppositionCollection
                    .Find(o => o.FileNumber == dto.FileNumber || o.id == dto.FileNumber)
                    .FirstOrDefaultAsync();
            }
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
            // First ensure CounterStatements array exists (handles null in DB)
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.And(
                    Builders<Opposition>.Filter.Eq(o => o.id, cs.OppositionId),
                    Builders<Opposition>.Filter.Eq(o => o.CounterStatements, null)),
                Builders<Opposition>.Update.Set(o => o.CounterStatements, new List<CounterStatement>()));

            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, cs.OppositionId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(o => o.IsCountered, true),
                    Builders<Opposition>.Update.Set(o => o.CounteredDate, DateTime.Now.ToString()),
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.StatutoryDeclaration),
                    Builders<Opposition>.Update.Push(o => o.CounterStatements, cs)
                ));
            _log.LogInformation($"Opposition {opp.id} updated: Status=StatutoryDeclaration(33), IsCountered=true, CounterStatement pushed");

            // Only update ApplicationHistory.CurrentStatus if this is the earliest opposition on the file
            var earliestOppForCs = await _oppositionCollection
                .Find(o => o.FileNumber == opp.FileNumber && o.Paid == true)
                .SortBy(o => o.OppositionDate)
                .FirstOrDefaultAsync();
            if (earliestOppForCs != null && earliestOppForCs.id == opp.id)
            {
                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.StatutoryDeclaration));
            }

            // Add Counter Statement to applicant's ApplicationHistory
            var csAppEntry = new ApplicationInfo
            {
                id = cs.Id,
                ApplicationType = FormApplicationTypes.CounterStatement,
                CurrentStatus = ApplicationStatuses.StatutoryDeclaration,
                PaymentId = cs.PaymentId,
                ApplicationDate = DateTime.Now
            };
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                Builders<Filling>.Update.Push(f => f.ApplicationHistory, csAppEntry));

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

    // ─── Statutory Declaration Search ───────────────────────────────────────────
    public async Task<object> StatutoryDeclarationSearch(string? oppositionId, string? fileNumber)
    {
        try
        {
            if (string.IsNullOrEmpty(oppositionId) && string.IsNullOrEmpty(fileNumber))
                throw new ArgumentException("Either oppositionId or fileNumber must be provided");

            var results = new List<object>();

            if (!string.IsNullOrEmpty(oppositionId))
            {
                // Clean input: strip OPP- prefix and trim
                if (oppositionId.StartsWith("OPP-", StringComparison.OrdinalIgnoreCase))
                    oppositionId = oppositionId.Substring(4);
                oppositionId = oppositionId.Trim();

                // Try exact match first, then prefix match
                var opp = await _oppositionCollection.Find(o => o.id == oppositionId).FirstOrDefaultAsync();
                if (opp == null)
                {
                    var lowerPrefix = oppositionId.ToLowerInvariant();
                    var filter = Builders<Opposition>.Filter.Regex(
                        o => o.id,
                        new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(lowerPrefix)}", "i"));
                    opp = await _oppositionCollection.Find(filter).FirstOrDefaultAsync();
                }
                if (opp == null)
                    throw new KeyNotFoundException("Opposition not found");

                if (opp.Status != ApplicationStatuses.StatutoryDeclaration)
                    throw new NotSupportedException("This opposition is not in a state that allows statutory declaration filing");

                var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                if (file == null)
                    throw new KeyNotFoundException("File not found");

                string title = file.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _ => file.TitleOfTradeMark
                };
                var applicant = file.applicants?.FirstOrDefault();
                var repAttachment = file.Attachments?.FirstOrDefault(a =>
                    a.name != null && a.name.Contains("representation", StringComparison.OrdinalIgnoreCase));

                var cost = _remitaPaymentUtils.GetCost(PaymentTypes.StatutoryDeclaration, file.Type, applicant?.country);

                return new
                {
                    oppositionId = opp.id,
                    fileNumber = opp.FileNumber,
                    fileName = title,
                    fileOwner = applicant?.Name,
                    trademarkClass = file.TrademarkClass?.ToString(),
                    representationUrl = repAttachment?.url?.FirstOrDefault(),
                    opposerName = opp.Name,
                    fileId = file.Id,
                    paymentId = opp.PaymentId,
                    cost = cost.Item1,
                    serviceFee = cost.Item3,
                    status = (int?)opp.Status
                };
            }
            else
            {
                // fileNumber flow — return all oppositions that allow SD
                var opps = await _oppositionCollection
                    .Find(o => o.FileNumber == fileNumber && o.Status == ApplicationStatuses.StatutoryDeclaration)
                    .SortByDescending(o => o.OppositionDate)
                    .ToListAsync();

                if (opps.Count == 0)
                    throw new KeyNotFoundException("No oppositions awaiting statutory declaration for this file");

                var file = await _fillingCollection.Find(f => f.FileId == fileNumber).FirstOrDefaultAsync();
                if (file == null)
                    throw new KeyNotFoundException("File not found");

                string title = file.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _ => file.TitleOfTradeMark
                };
                var applicant = file.applicants?.FirstOrDefault();
                var repAttachment = file.Attachments?.FirstOrDefault(a =>
                    a.name != null && a.name.Contains("representation", StringComparison.OrdinalIgnoreCase));

                var cost = _remitaPaymentUtils.GetCost(PaymentTypes.StatutoryDeclaration, file.Type, applicant?.country);

                return opps.Select(opp => new
                {
                    oppositionId = opp.id,
                    fileNumber = opp.FileNumber,
                    fileName = title,
                    fileOwner = applicant?.Name,
                    trademarkClass = file.TrademarkClass?.ToString(),
                    representationUrl = repAttachment?.url?.FirstOrDefault(),
                    opposerName = opp.Name,
                    name = opp.Name,
                    fileId = file.Id,
                    paymentId = opp.PaymentId,
                    cost = cost.Item1,
                    serviceFee = cost.Item3,
                    status = (int?)opp.Status
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in StatutoryDeclarationSearch");
            throw;
        }
    }

    // ─── Generate Payment RRR for Opposition Flows ─────────────────────────────
    public async Task<object> GenerateOppositionPayment(GenerateOppositionPaymentDto dto)
    {
        var type = dto.Type?.ToLowerInvariant();
        PaymentTypes paymentType = type switch
        {
            "statutorydeclaration" => PaymentTypes.StatutoryDeclaration,
            "response" => PaymentTypes.CounterStatement,
            "resolution" => PaymentTypes.Opposition,
            _ => throw new NotSupportedException($"Payment type '{dto.Type}' is not supported")
        };

        var cost = _remitaPaymentUtils.GetCost(paymentType, null, null);
        var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
            cost.Item1, cost.Item3, cost.Item2,
            dto.Description ?? "Opposition Payment", dto.Name, dto.Email, dto.Number);
        if (rrr == null)
            throw new Exception("Unable to generate RRR");

        int.TryParse(cost.Item1, out int govFee);
        int.TryParse(cost.Item3, out int svcFee);

        return new
        {
            rrr,
            amount = (govFee + svcFee).ToString()
        };
    }

    // ─── Submit Statutory Declaration (mirrors Counter Statement) ───────────
    public async Task<(bool success, object invoice, string message)> SubmitStatutoryDeclaration(StatutoryDeclarationRequestDto dto)
    {
        try
        {
            _log.LogInformation($"Submitting Statutory Declaration for opposition {dto.OppositionId}...");

            if (string.IsNullOrWhiteSpace(dto.UserId))
                return (false, null, "UserId is required");

            if (string.IsNullOrWhiteSpace(dto.OppositionId))
                return (false, null, "OppositionId is required");

            if (dto.SupportingDocs == null || dto.SupportingDocs.Count == 0)
                return (false, null, "At least one supporting document is required");

            var opp = await _oppositionCollection.Find(o => o.id == dto.OppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, null, "Opposition not found");

            // Duplicate check: only reject if a PAID SD exists
            var existing = await _statutoryDeclarationCollection
                .Find(sd => sd.OppositionId == dto.OppositionId && sd.UserId == dto.UserId)
                .FirstOrDefaultAsync();
            if (existing != null)
            {
                if (existing.Paid == true)
                    return (false, null, "Statutory declaration already filed for this opposition by this user");
                // Remove unpaid duplicate
                await _statutoryDeclarationCollection.DeleteOneAsync(sd => sd.Id == existing.Id);
            }

            var file = await _fillingCollection.Find(f => f.FileId == (opp.FileNumber ?? dto.FileNumber)).FirstOrDefaultAsync();
            if (file == null)
                return (false, null, "File not found");

            var applicant = file.applicants?.FirstOrDefault();

            string title = file.Type switch
            {
                FileTypes.Design => file.TitleOfDesign,
                FileTypes.Patent => file.TitleOfInvention,
                _ => file.TitleOfTradeMark
            };

            // Upload attachments
            var attachmentUrls = new List<string>();
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
                        Name        = $"Statutory Declaration Document {i + 1}"
                    }
                });
                attachmentUrls.Add(urls[0]);
            }

            // Generate RRR
            var cost = _remitaPaymentUtils.GetCost(PaymentTypes.StatutoryDeclaration, file.Type, applicant?.country);
            var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                cost.Item1, cost.Item3, cost.Item2,
                "Statutory Declaration", applicant?.Name ?? opp.Name, applicant?.Email ?? opp.Email, applicant?.Phone ?? opp.Phone);
            if (rrr == null)
                return (false, null, "Unable to generate payment reference");

            // Save record (awaiting payment)
            var sd = new StatutoryDeclaration
            {
                Id            = Guid.NewGuid().ToString(),
                OppositionId  = opp.id,
                Text          = dto.Comment,
                Attachments   = attachmentUrls,
                PaymentId     = rrr,
                UserId        = dto.UserId,
                Role          = dto.Role?.ToLower(),
                Paid          = false,
                SubmittedDate = DateTime.Now
            };
            await _statutoryDeclarationCollection.InsertOneAsync(sd);
            _log.LogInformation($"Statutory Declaration {sd.Id} saved with RRR {rrr}");

            var invoice = new
            {
                paymentId         = rrr,
                fileNumber        = file.FileId,
                fileTitle         = title,
                applicantName     = applicant?.Name,
                opposerName       = opp.Name,
                cost              = cost.Item1,
                serviceFee        = cost.Item3
            };

            return (true, invoice, "Statutory Declaration submitted successfully");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error submitting statutory declaration");
            throw;
        }
    }

    // ─── Update Statutory Declaration Payment ─────────────────────────────────
    public async Task<(bool success, string message)> UpdateStatutoryDeclarationPayment(string paymentId)
    {
        try
        {
            _log.LogInformation($"Updating statutory declaration payment {paymentId}...");

            var sd = await _statutoryDeclarationCollection.Find(x => x.PaymentId == paymentId).FirstOrDefaultAsync();
            if (sd == null)
                return (false, "Statutory declaration not found for this payment ID");

            if (sd.Paid == true)
                return (true, "Statutory declaration payment already confirmed");

            await _statutoryDeclarationCollection.UpdateOneAsync(
                Builders<StatutoryDeclaration>.Filter.Eq(x => x.PaymentId, paymentId),
                Builders<StatutoryDeclaration>.Update.Combine(
                    Builders<StatutoryDeclaration>.Update.Set(x => x.Paid, true),
                    Builders<StatutoryDeclaration>.Update.Set(x => x.ApplicationStatus, ApplicationStatuses.AwaitingOfficeProcess)));

            // Re-fetch the updated SD to push correct state into opposition
            sd = await _statutoryDeclarationCollection.Find(x => x.PaymentId == paymentId).FirstOrDefaultAsync();

            var opp = await _oppositionCollection.Find(o => o.id == sd.OppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, "Opposition not found");

            // Push statutory declaration into opposition record and update opposition status
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.And(
                    Builders<Opposition>.Filter.Eq(o => o.id, sd.OppositionId),
                    Builders<Opposition>.Filter.Eq(o => o.StatutoryDeclarations, null)),
                Builders<Opposition>.Update.Set(o => o.StatutoryDeclarations, new List<StatutoryDeclaration>()));

            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, sd.OppositionId),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Push(o => o.StatutoryDeclarations, sd),
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.AwaitingOfficeProcess)));

            // Update the file's ApplicationHistory[0].CurrentStatus to AwaitingOfficeProcess
            // (only for the earliest opposition on the file)
            var earliestOpp = await _oppositionCollection
                .Find(o => o.FileNumber == opp.FileNumber && o.Paid == true)
                .SortBy(o => o.OppositionDate)
                .FirstOrDefaultAsync();
            if (earliestOpp != null && earliestOpp.id == opp.id)
            {
                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.AwaitingOfficeProcess));
            }

            _log.LogInformation($"Opposition {opp.id} updated: Statutory declaration pushed");

            // Add Statutory Declaration to file's ApplicationHistory only if filed by file owner
            var fileForSdCheck = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
            if (fileForSdCheck != null && sd.UserId != opp.UserId)
            {
                // Filed by applicant (file owner) — add to history
                var sdAppEntry = new ApplicationInfo
                {
                    id = sd.Id,
                    ApplicationType = FormApplicationTypes.StatutoryDeclaration,
                    CurrentStatus = ApplicationStatuses.AwaitingOfficeProcess,
                    PaymentId = sd.PaymentId,
                    ApplicationDate = DateTime.Now
                };
                await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Push(f => f.ApplicationHistory, sdAppEntry));
            }

            // Send email notification to BOTH parties
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                string fileTitle = file?.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _ => file?.TitleOfTradeMark
                };

                var fileOwner = file?.applicants?.FirstOrDefault();
                string filerRole = sd.UserId == opp.UserId ? "Opposer" : "Applicant";

                // Notify applicant (file owner)
                var applicantEmail = fileOwner?.Email ?? "";
                if (!string.IsNullOrEmpty(applicantEmail))
                {
                    await _emailServices.SendMail(new EmailDto
                    {
                        To      = applicantEmail,
                        Subject = "Statutory Declaration Filed",
                        EmailType = EmailType.StatutoryDeclaration,
                        StatutoryDeclarationMail = new StatutoryDeclarationMail
                        {
                            To            = applicantEmail,
                            Subject       = "Statutory Declaration Filed",
                            RecipientName = fileOwner?.Name ?? "Applicant",
                            FilerRole     = filerRole,
                            FileNumber    = opp.FileNumber,
                            FileTitle     = fileTitle,
                            OppositionId  = opp.id,
                            DateFiled     = DateTime.Now.ToString("dd MMMM yyyy")
                        }
                    });
                    _log.LogInformation($"Statutory declaration notification sent to applicant {applicantEmail}");
                }

                // Notify opposer
                var opposerEmail = opp.Email ?? "";
                if (!string.IsNullOrEmpty(opposerEmail))
                {
                    await _emailServices.SendMail(new EmailDto
                    {
                        To      = opposerEmail,
                        Subject = "Statutory Declaration Filed",
                        EmailType = EmailType.StatutoryDeclaration,
                        StatutoryDeclarationMail = new StatutoryDeclarationMail
                        {
                            To            = opposerEmail,
                            Subject       = "Statutory Declaration Filed",
                            RecipientName = opp.Name ?? "Opposer",
                            FilerRole     = filerRole,
                            FileNumber    = opp.FileNumber,
                            FileTitle     = fileTitle,
                            OppositionId  = opp.id,
                            DateFiled     = DateTime.Now.ToString("dd MMMM yyyy")
                        }
                    });
                    _log.LogInformation($"Statutory declaration notification sent to opposer {opposerEmail}");
                }
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, "Failed to send statutory declaration notification email — proceeding anyway");
            }

            _log.LogInformation($"Statutory declaration payment confirmed for opposition {opp.id}");
            return (true, "Payment updated successfully");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating statutory declaration payment");
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
            List<Opposition> oppositions = new();

            if (!string.IsNullOrEmpty(oppositionId))
            {
                // Clean input: strip OPP- prefix and trim
                if (oppositionId.StartsWith("OPP-", StringComparison.OrdinalIgnoreCase))
                    oppositionId = oppositionId.Substring(4);
                oppositionId = oppositionId.Trim();

                // 1. Try exact match
                var opp = await _oppositionCollection.Find(o => o.id == oppositionId).FirstOrDefaultAsync();

                // 2. If no exact match, try prefix match (case-insensitive)
                if (opp == null)
                {
                    var lowerPrefix = oppositionId.ToLowerInvariant();
                    var filter = Builders<Opposition>.Filter.Regex(
                        o => o.id,
                        new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(lowerPrefix)}", "i"));
                    var matches = await _oppositionCollection
                        .Find(filter)
                        .SortByDescending(o => o.OppositionDate)
                        .ToListAsync();
                    oppositions.AddRange(matches);
                }

                if (opp != null)
                    oppositions.Add(opp);
            }
            else if (!string.IsNullOrEmpty(fileNumber))
            {
                // Return ALL oppositions for this file, ordered by date descending
                oppositions = await _oppositionCollection
                    .Find(o => o.FileNumber == fileNumber)
                    .SortByDescending(o => o.OppositionDate)
                    .ToListAsync();
            }

            if (oppositions.Count == 0) return null;

            var firstOpp = oppositions.First();
            var file = await _fillingCollection.Find(f => f.FileId == firstOpp.FileNumber).FirstOrDefaultAsync();

            // Fetch counter statements and statutory declarations from their collections, keyed by oppositionId
            var oppIds = oppositions.Select(o => o.id).ToList();
            var allCs = await _counterStatementCollection
                .Find(Builders<CounterStatement>.Filter.And(
                    Builders<CounterStatement>.Filter.In(cs => cs.OppositionId, oppIds),
                    Builders<CounterStatement>.Filter.Eq(cs => cs.Paid, true)))
                .ToListAsync();
            var allSd = await _statutoryDeclarationCollection
                .Find(Builders<StatutoryDeclaration>.Filter.And(
                    Builders<StatutoryDeclaration>.Filter.In(sd => sd.OppositionId, oppIds),
                    Builders<StatutoryDeclaration>.Filter.Eq(sd => sd.Paid, true)))
                .ToListAsync();

            _log.LogInformation($"GetOppositionDetail: Found {allCs.Count} counter statements for {oppIds.Count} oppositions");
            foreach (var cs in allCs)
                _log.LogInformation($"  CS {cs.Id} -> OppositionId: {cs.OppositionId}");

            var csByOpp = allCs.GroupBy(cs => cs.OppositionId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(cs => cs.SubmittedDate).Take(1).ToList());
            var sdByOpp = allSd.GroupBy(sd => sd.OppositionId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(sd => sd.SubmittedDate).ToList());

            var results = oppositions.Select(opp =>
            {
                var oppCounterStatements = csByOpp.GetValueOrDefault(opp.id, new List<CounterStatement>());
                var oppStatutoryDeclarations = sdByOpp.GetValueOrDefault(opp.id, new List<StatutoryDeclaration>());
                string fileName = file?.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _                => file?.TitleOfTradeMark
                };

                var hasCounterStatement = oppCounterStatements.Count > 0;
                var counterStatementDate = hasCounterStatement
                    ? oppCounterStatements.First().SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss")
                    : null;

                return new
                {
                    id                    = opp.id,
                    fileNumber            = opp.FileNumber,
                    fileName              = fileName,
                    title                 = fileName,
                    applicantName         = file?.applicants?.FirstOrDefault()?.Name,
                    fileOwner             = file?.applicants?.FirstOrDefault()?.Name,
                    trademarkClass        = file?.TrademarkClass,
                    name                  = opp.Name,
                    email                 = opp.Email,
                    phone                 = opp.Phone,
                    address               = opp.Address,
                    nationality           = opp.Nationality,
                    reason                = opp.Reason,
                    oppositionText        = opp.Reason,
                    status                = opp.Status,
                    fileStatus            = file?.FileStatus,
                    oppositionStatus      = opp.Status,
                    oppositionDate        = (opp.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                    paymentId             = opp.PaymentId,
                    date                  = (opp.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                    decision              = opp.Decision,
                    resolutionStatement   = opp.ResolutionStatement,
                    resolvedBy            = opp.ResolvedBy,
                    hasCounterStatement   = hasCounterStatement,
                    counterStatementDate  = counterStatementDate,
                    supportingDocs        = opp.SupportingDocs ?? new List<string>(),
                    counterStatements     = oppCounterStatements.Select(cs => new
                    {
                        id            = cs.Id,
                        oppositionId  = cs.OppositionId,
                        filedBy       = cs.UserId,
                        dateFiled     = cs.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        statement     = cs.Text,
                        submittedDate = cs.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        text          = cs.Text,
                        attachments   = cs.Attachments ?? new List<string>()
                    }).ToList(),
                    statutoryDeclarations = oppStatutoryDeclarations.Select(sd => new
                    {
                        id            = sd.Id,
                        oppositionId  = sd.OppositionId,
                        filedBy       = sd.UserId,
                        role          = sd.Role,
                        dateFiled     = sd.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        statement     = sd.Text,
                        submittedDate = sd.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        text          = sd.Text,
                        attachments   = sd.Attachments ?? new List<string>()
                    }).ToList()
                };
            }).ToList();

            return results;
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

    // ─── Generate Counter Statement Acknowledgement Letter ──────────────────
    public async Task<byte[]> GenerateCounterStatementLetter(string counterStatementId)
    {
        var cs = await _counterStatementCollection.Find(x => x.Id == counterStatementId).FirstOrDefaultAsync();
        if (cs == null) throw new KeyNotFoundException("Counter statement not found");

        var opp = await _oppositionCollection.Find(o => o.id == cs.OppositionId).FirstOrDefaultAsync();
        if (opp == null) throw new KeyNotFoundException("Opposition not found");

        var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
        if (file == null) throw new KeyNotFoundException("File not found");

        var document = new CounterStatementAcknowledgementModel(file, opp, cs);
        return document.GeneratePdf();
    }

    public async Task<byte[]> GenerateCounterStatementLetterByPaymentId(string paymentId)
    {
        var cs = await _counterStatementCollection.Find(x => x.PaymentId == paymentId).FirstOrDefaultAsync();
        if (cs == null) throw new KeyNotFoundException("Counter statement not found");

        var opp = await _oppositionCollection.Find(o => o.id == cs.OppositionId).FirstOrDefaultAsync();
        if (opp == null) throw new KeyNotFoundException("Opposition not found");

        var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
        if (file == null) throw new KeyNotFoundException("File not found");

        var document = new CounterStatementAcknowledgementModel(file, opp, cs);
        return document.GeneratePdf();
    }

    // ─── Generate Statutory Declaration Acknowledgement Letter ───────────────
    public async Task<byte[]> GenerateStatutoryDeclarationLetter(string statutoryDeclarationId)
    {
        var sd = await _statutoryDeclarationCollection.Find(x => x.Id == statutoryDeclarationId).FirstOrDefaultAsync();
        if (sd == null) throw new KeyNotFoundException("Statutory declaration not found");

        var opp = await _oppositionCollection.Find(o => o.id == sd.OppositionId).FirstOrDefaultAsync();
        if (opp == null) throw new KeyNotFoundException("Opposition not found");

        var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
        if (file == null) throw new KeyNotFoundException("File not found");

        var document = new StatutoryDeclarationAcknowledgementModel(file, opp, sd);
        return document.GeneratePdf();
    }

    public async Task<byte[]> GenerateStatutoryDeclarationLetterByPaymentId(string paymentId)
    {
        var sd = await _statutoryDeclarationCollection.Find(x => x.PaymentId == paymentId).FirstOrDefaultAsync();
        if (sd == null) throw new KeyNotFoundException("Statutory declaration not found");

        var opp = await _oppositionCollection.Find(o => o.id == sd.OppositionId).FirstOrDefaultAsync();
        if (opp == null) throw new KeyNotFoundException("Opposition not found");

        var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
        if (file == null) throw new KeyNotFoundException("File not found");

        var document = new StatutoryDeclarationAcknowledgementModel(file, opp, sd);
        return document.GeneratePdf();
    }

    // ─── Generate Opposition Acknowledgement Letter ────────────────────────
    public async Task<byte[]> GenerateOppositionAcknowledgementLetter(string oppositionId)
    {
        var opp = await _oppositionCollection.Find(o => o.id == oppositionId).FirstOrDefaultAsync();
        if (opp == null) throw new KeyNotFoundException("Opposition not found");

        var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();

        var document = new Tfunctions.pdfs.OppositionAcknowledgement(new OppositionAckType
        {
            name = opp.Name,
            email = opp.Email,
            number = opp.Phone,
            address = opp.Address,
            paymentId = opp.PaymentId,
            description = opp.FileTitle ?? opp.FileNumber,
            date = opp.OppositionDate ?? DateTime.UtcNow,
            oppositionId = !string.IsNullOrEmpty(opp.id) ? $"OPP-{opp.id.Substring(0, 8).ToUpper()}" : "-",
            reason = opp.Reason,
            file = file
        }, "uri");
        return document.GeneratePdf();
    }

    public async Task<byte[]> GenerateOppositionAcknowledgementLetterByPaymentId(string paymentId)
    {
        var opp = await _oppositionCollection.Find(o => o.PaymentId == paymentId).FirstOrDefaultAsync();
        if (opp == null) throw new KeyNotFoundException("Opposition not found");

        var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();

        var document = new Tfunctions.pdfs.OppositionAcknowledgement(new OppositionAckType
        {
            name = opp.Name,
            email = opp.Email,
            number = opp.Phone,
            address = opp.Address,
            paymentId = opp.PaymentId,
            description = opp.FileTitle ?? opp.FileNumber,
            date = opp.OppositionDate ?? DateTime.UtcNow,
            oppositionId = !string.IsNullOrEmpty(opp.id) ? $"OPP-{opp.id.Substring(0, 8).ToUpper()}" : "-",
            reason = opp.Reason,
            file = file
        }, "uri");
        return document.GeneratePdf();
    }

    // ─── Backfill PaymentId
    public async Task<int> BackfillOppositionPaymentIds()
    {
        // Get all paid oppositions that have a PaymentId
        var paidOppositions = await _oppositionCollection
            .Find(o => o.Paid == true && o.PaymentId != null)
            .ToListAsync();

        // Group by file number — take the earliest opposition per file
        var grouped = paidOppositions
            .GroupBy(o => o.FileNumber)
            .Select(g => g.OrderBy(o => o.OppositionDate).First())
            .ToList();

        int updated = 0;
        foreach (var opp in grouped)
        {
            if (string.IsNullOrEmpty(opp.FileNumber) || string.IsNullOrEmpty(opp.PaymentId))
                continue;

            var result = await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                Builders<Filling>.Update.Set("ApplicationHistory.0.PaymentId", opp.PaymentId));

            if (result.ModifiedCount > 0)
                updated++;
        }

        _log.LogInformation($"Backfill complete: updated {updated} file(s) with opposition PaymentId");
        return updated;
    }

    // ─── Backfill: Update oppositions with paid SDs to AwaitingOfficeProcess ──
    public async Task<int> BackfillStatutoryDeclarationStatuses()
    {
        var paidSds = await _statutoryDeclarationCollection
            .Find(sd => sd.Paid == true)
            .ToListAsync();

        int updated = 0;
        foreach (var sd in paidSds)
        {
            if (string.IsNullOrEmpty(sd.OppositionId)) continue;

            var result = await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.And(
                    Builders<Opposition>.Filter.Eq(o => o.id, sd.OppositionId),
                    Builders<Opposition>.Filter.Eq(o => o.Status, ApplicationStatuses.StatutoryDeclaration)),
                Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.AwaitingOfficeProcess));

            if (result.ModifiedCount > 0)
            {
                updated++;
                // Also update the file's ApplicationHistory[0].CurrentStatus
                var opp = await _oppositionCollection.Find(o => o.id == sd.OppositionId).FirstOrDefaultAsync();
                if (opp != null)
                {
                    var earliestOpp = await _oppositionCollection
                        .Find(o => o.FileNumber == opp.FileNumber && o.Paid == true)
                        .SortBy(o => o.OppositionDate)
                        .FirstOrDefaultAsync();
                    if (earliestOpp != null && earliestOpp.id == opp.id)
                    {
                        await _fillingCollection.UpdateOneAsync(
                            Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                            Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.AwaitingOfficeProcess));
                    }
                }
            }
        }

        // Also fix files where ApplicationHistory is still at StatutoryDeclaration but opposition is already AwaitingOfficeProcess
        var awaitingOpps = await _oppositionCollection
            .Find(o => o.Status == ApplicationStatuses.AwaitingOfficeProcess)
            .ToListAsync();
        foreach (var opp in awaitingOpps)
        {
            if (string.IsNullOrEmpty(opp.FileNumber)) continue;
            var earliestOpp = await _oppositionCollection
                .Find(o => o.FileNumber == opp.FileNumber && o.Paid == true)
                .SortBy(o => o.OppositionDate)
                .FirstOrDefaultAsync();
            if (earliestOpp != null && earliestOpp.id == opp.id)
            {
                var fileResult = await _fillingCollection.UpdateOneAsync(
                    Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.AwaitingOfficeProcess));
                if (fileResult.ModifiedCount > 0)
                    updated++;
            }
        }

        _log.LogInformation($"Backfill SD statuses complete: updated {updated} record(s) to AwaitingOfficeProcess");

        // Backfill Role on SDs that don't have it
        var sdsWithoutRole = await _statutoryDeclarationCollection
            .Find(sd => sd.Role == null)
            .ToListAsync();
        foreach (var sdItem in sdsWithoutRole)
        {
            if (string.IsNullOrEmpty(sdItem.OppositionId)) continue;
            var oppForSd = await _oppositionCollection.Find(o => o.id == sdItem.OppositionId).FirstOrDefaultAsync();
            if (oppForSd == null) continue;

            var role = sdItem.UserId == oppForSd.UserId ? "opposer" : "applicant";
            await _statutoryDeclarationCollection.UpdateOneAsync(
                Builders<StatutoryDeclaration>.Filter.Eq(x => x.Id, sdItem.Id),
                Builders<StatutoryDeclaration>.Update.Set(x => x.Role, role));
            updated++;
        }

        _log.LogInformation($"Backfill SD roles complete: {sdsWithoutRole.Count} SD(s) updated with role");

        // Backfill ApplicationHistory entries for paid counter statements and statutory declarations
        // Remove ALL CS-type entries from ApplicationHistory for affected files, then re-add one per opposition
        var allPaidCsForCleanup = await _counterStatementCollection.Find(cs => cs.Paid == true).ToListAsync();

        // Group by OppositionId to only keep one CS per opposition
        var csGroupedByOpposition = allPaidCsForCleanup
            .Where(cs => !string.IsNullOrEmpty(cs.OppositionId))
            .GroupBy(cs => cs.OppositionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(cs => cs.SubmittedDate).First());

        _log.LogInformation($"Total paid CS documents: {allPaidCsForCleanup.Count}, Unique oppositions with CS: {csGroupedByOpposition.Count}");

        // First, remove ALL CS-type ApplicationHistory entries from affected files
        var affectedFileNumbers = new HashSet<string>();
        foreach (var kvp in csGroupedByOpposition)
        {
            var opp = await _oppositionCollection.Find(o => o.id == kvp.Key).FirstOrDefaultAsync();
            if (opp == null || string.IsNullOrEmpty(opp.FileNumber)) continue;
            affectedFileNumbers.Add(opp.FileNumber);
            _log.LogInformation($"CS for opposition {kvp.Key} -> file {opp.FileNumber}");
        }

        _log.LogInformation($"Affected files for CS cleanup: {string.Join(", ", affectedFileNumbers)}");

        foreach (var fileNumber in affectedFileNumbers)
        {
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, fileNumber),
                Builders<Filling>.Update.PullFilter(f => f.ApplicationHistory,
                    Builders<ApplicationInfo>.Filter.Eq(a => a.ApplicationType, FormApplicationTypes.CounterStatement)));
        }

        // Re-add exactly one CS entry per opposition
        foreach (var kvp in csGroupedByOpposition)
        {
            var csClean = kvp.Value;
            var oppClean2 = await _oppositionCollection.Find(o => o.id == kvp.Key).FirstOrDefaultAsync();
            if (oppClean2 == null || string.IsNullOrEmpty(oppClean2.FileNumber)) continue;

            var csEntry = new ApplicationInfo
            {
                id = csClean.Id,
                ApplicationType = FormApplicationTypes.CounterStatement,
                CurrentStatus = ApplicationStatuses.StatutoryDeclaration,
                PaymentId = csClean.PaymentId,
                ApplicationDate = csClean.SubmittedDate
            };
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, oppClean2.FileNumber),
                Builders<Filling>.Update.Push(f => f.ApplicationHistory, csEntry));
            updated++;
        }

        // Remove ALL SD entries from ApplicationHistory, then re-add exactly one per applicant-filed paid SD
        var paidSdList = await _statutoryDeclarationCollection.Find(sd => sd.Paid == true).ToListAsync();
        foreach (var sdItem2 in paidSdList)
        {
            if (string.IsNullOrEmpty(sdItem2.OppositionId)) continue;
            var oppForSd2 = await _oppositionCollection.Find(o => o.id == sdItem2.OppositionId).FirstOrDefaultAsync();
            if (oppForSd2 == null || string.IsNullOrEmpty(oppForSd2.FileNumber)) continue;

            // Pull ALL entries with this SD id first (removes duplicates and opposer-filed entries)
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, oppForSd2.FileNumber),
                Builders<Filling>.Update.PullFilter(f => f.ApplicationHistory,
                    Builders<ApplicationInfo>.Filter.Eq(a => a.id, sdItem2.Id)));

            // Only re-add if filed by the applicant (not the opposer)
            if (sdItem2.UserId == oppForSd2.UserId) continue;

            var sdEntry = new ApplicationInfo
            {
                id = sdItem2.Id,
                ApplicationType = FormApplicationTypes.StatutoryDeclaration,
                CurrentStatus = ApplicationStatuses.AwaitingOfficeProcess,
                PaymentId = sdItem2.PaymentId,
                ApplicationDate = sdItem2.SubmittedDate
            };
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, oppForSd2.FileNumber),
                Builders<Filling>.Update.Push(f => f.ApplicationHistory, sdEntry));
            updated++;
        }

        _log.LogInformation($"Backfill ApplicationHistory entries complete");
        return updated;
    }
}