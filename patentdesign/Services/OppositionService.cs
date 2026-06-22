using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using MongoDB.Driver;
using patentdesign;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using QuestPDF.Fluent;
using System.Net.Mail;
using System.Reflection.Emit;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tfunctions.pdfs;
using static System.Net.WebRequestMethods;

public class OppositionService
{
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private static IMongoCollection<Opposition> _oppositionCollection;
    private static IMongoCollection<CounterStatement> _counterStatementCollection;
    private static IMongoCollection<StatutoryDeclaration> _statutoryDeclarationCollection;
    private static IMongoCollection<OppositionWithdrawal> _oppositionWithdrawalCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;
    private static IMongoCollection<PublicationInfo> _publicationCollection;
    private static IMongoCollection<AppUser> _userCollection;
    private readonly ILogger<OppositionService> _log;

    private PaymentUtils _remitaPaymentUtils;
    private FilesServices _fileServices;
    private MongoClient _mongoClient;
    private EmailServices _emailServices;
    private PaymentService _paymentServices;
    private NotificationServices _notificationServices;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";
    private IMongoDatabase db;
    private IMongoDatabase pdDb;
    public OppositionService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, FilesServices fileServices, EmailServices emailServices, ILogger<OppositionService> log, PaymentService paymentServices, NotificationServices notificationServices)
    {
        _remitaPaymentUtils = remitaPaymentUtils;
        _fileServices = fileServices;
        _emailServices = emailServices;
        _notificationServices = notificationServices;
        var s = patentDesignDbSettings.Value;

        // Initialize MongoClient and databases
        _mongoClient = new MongoClient(s.ConnectionString);
        db = _mongoClient.GetDatabase(s.DatabaseName);
        pdDb = _mongoClient.GetDatabase(s.DatabaseName);

        _fillingCollection = db.GetCollection<Filling>(s.FilesCollectionName);
        _attachmentCollection = db.GetCollection<AttachmentInfo>(s.AttachmentCollectionName);
        _oppositionCollection = db.GetCollection<Opposition>(s.OppositionCollectionName);
        _counterStatementCollection = db.GetCollection<CounterStatement>(s.CounterStatementsCollectionName);
        _statutoryDeclarationCollection = db.GetCollection<StatutoryDeclaration>(s.StatutoryDeclarationsCollectionName);
        _oppositionWithdrawalCollection = db.GetCollection<OppositionWithdrawal>(s.OppositionWithdrawalsCollectionName ?? "oppositionWithdrawals");
        _financeCollection = db.GetCollection<FinanceHistory>(s.FinanceCollectionName);
        _log = log;
        _publicationCollection = db.GetCollection<PublicationInfo>("trademarkJournal");
        _userCollection = db.GetCollection<AppUser>("appUsers");
        _paymentServices = paymentServices;
    }
    public async Task<OppositionSearchDto> OppositionSearch(string fileNumber)
    {
        try
        {
            _log.LogInformation($"Searching to Oppose {fileNumber}...");
            var file = await _fillingCollection.Find(f => f.FileId == fileNumber).FirstOrDefaultAsync();
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
                CreatorId = data.UserId,
                FileOwnerId = (await _fillingCollection.Find(f => f.Id == data.FileId).FirstOrDefaultAsync())?.CreatorAccount,
            };
            await _oppositionCollection.InsertOneAsync(oppose);
            _log.LogInformation($"New Opposition {oppose.FileNumber} saved");

            // Send in-app notifications to file owner and opposer
            try
            {
                var fileTitle = oppose.FileTitle ?? oppose.FileNumber;
                await _notificationServices.SendOppositionNotificationsAsync(
                    fileOwnerId: oppose.FileOwnerId,
                    opposerUserId: oppose.UserId,
                    fileNumber: oppose.FileNumber,
                    fileTitle: fileTitle,
                    opposerName: oppose.Name,
                    oppositionId: oppose.id
                );
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to send opposition notifications — non-critical");
            }

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
            Builders<PublicationInfo>.Update.Push(p => p.Opposition, opp)
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
            _log.LogError(e, "Failed to Oppose by staff");
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
                    To = opp.Email,
                    Subject = "Opposition Filed Successfully",
                    EmailType = EmailType.OppositionConfirmation,
                    OppositionConfirmationMail = new OppositionConfirmationMail
                    {
                        To = opp.Email,
                        OpposerName = opp.Name,
                        OppositionId = opp.id,
                        FileNumber = opp.FileNumber,
                        FileTitle = opp.FileTitle,
                        DateFiled = opp.OppositionDate?.ToString("dd MMMM yyyy") ?? DateTime.Now.ToString("dd MMMM yyyy"),
                        PaymentReference = opp.PaymentId
                    }
                });
                _log.LogInformation($"Opposition confirmation email sent to opposer {opp.Email}");
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, "Failed to send opposition confirmation email — proceeding anyway");
            }

            // Send in-app notifications to both the opposer and the file owner
            try
            {
                var fileForNotify = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                string notifyTitle = fileForNotify?.Type switch
                {
                    FileTypes.Design => fileForNotify.TitleOfDesign,
                    FileTypes.Patent => fileForNotify.TitleOfInvention,
                    _ => fileForNotify?.TitleOfTradeMark
                };
                await _notificationServices.SendOppositionNotificationsAsync(
                    fileOwnerId: opp.FileOwnerId,
                    opposerUserId: opp.UserId,
                    fileNumber: opp.FileNumber,
                    fileTitle: notifyTitle,
                    opposerName: opp.Name,
                    oppositionId: opp.id
                );
                _log.LogInformation($"In-app notifications sent for opposition {opp.id}");
            }
            catch (Exception notifEx)
            {
                _log.LogWarning(notifEx, "Failed to send in-app notifications — proceeding anyway");
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
            var date = opp.OppositionDate.ToString();
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
        // Back-office statuses (withdrawal, awaiting payment, withdrawn) don't require Paid=true
        var backOfficeStatuses = new[]
        {
            ApplicationStatuses.RequestWithdrawal,
            ApplicationStatuses.WithdrawalRequested,
            ApplicationStatuses.Withdrawn,
            ApplicationStatuses.AwaitingPayment
        };
        bool isBackOfficeQuery = status != null && backOfficeStatuses.Contains(status.Value);

        FilterDefinition<Opposition> baseFilter;
        if (isBackOfficeQuery)
        {
            // RequestWithdrawal(29) and WithdrawalRequested(38) both mean "pending withdrawal" — match either
            if (status == ApplicationStatuses.RequestWithdrawal || status == ApplicationStatuses.WithdrawalRequested)
            {
                baseFilter = Builders<Opposition>.Filter.In(x => x.Status,
                    new ApplicationStatuses?[] { ApplicationStatuses.RequestWithdrawal, ApplicationStatuses.WithdrawalRequested });
            }
            else
            {
                baseFilter = Builders<Opposition>.Filter.Eq(x => x.Status, status);
            }
        }
        else
        {
            var paidFilter = Builders<Opposition>.Filter.Eq(x => x.Paid, true);
            baseFilter = status != null
                ? Builders<Opposition>.Filter.And(paidFilter, Builders<Opposition>.Filter.Eq(x => x.Status, status))
                : paidFilter;
        }

        FilterDefinition<Opposition> filter;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            // Match on either UserId OR CreatorId so legacy docs (which only have one) are included
            var userFilter = Builders<Opposition>.Filter.Or(
                Builders<Opposition>.Filter.Eq(x => x.UserId, userId),
                Builders<Opposition>.Filter.Eq(x => x.CreatorId, userId)
            );
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
            sn        = ++sn,
            date      = (x.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
            title     = x.FileTitle,
            fileId    = x.FileNumber,
            name      = x.Name,
            email     = x.Email,
            status    = x.Status,
            paymentId = x.PaymentId,
            id        = x.id
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
                _ => file.TitleOfTradeMark
            };

            var applicant = file.applicants?.FirstOrDefault();
            var repAttachment = file.Attachments?.FirstOrDefault(a =>
                a.name != null && a.name.Contains("representation", StringComparison.OrdinalIgnoreCase));

            return new CsSearchDto
            {
                Success = true,
                FileNumber = file.FileId,
                FileName = title,
                FileOwner = applicant?.Name,
                TrademarkClass = file.TrademarkClass,
                RepresentationUrl = repAttachment?.url?.FirstOrDefault(),
                OppositionId = opp.id,
                Message = null
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
            serviceFee = svcFee,
            total = govFee + svcFee,
            currency = "NGN"
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
                _ => file.TitleOfTradeMark
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
                Id = Guid.NewGuid().ToString(),
                OppositionId = opp.id,
                Text = dto.CounterStatement,
                Attachments = attachmentUrls,
                PaymentId = rrr,
                UserId = dto.UserId,
                SubmittedDate = DateTime.Now
            };
            await _counterStatementCollection.InsertOneAsync(cs);
            _log.LogInformation($"Counter Statement {cs.Id} saved with RRR {rrr}");

            var invoice = new OppositionSearchDto
            {
                FileNumber = file.FileId,
                FileTitle = title,
                Class = file.TrademarkClass,
                ApplicantName = applicant?.Name,
                RepresentationUrl = repAttachment?.url?.FirstOrDefault(),
                Cost = cost.Item1,
                PaymentId = rrr,
                ServiceFee = cost.Item3,
                FileId = file.Id
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
                    _ => file?.TitleOfTradeMark
                };
                var fileOwnerName = file?.applicants?.FirstOrDefault()?.Name ?? "File Owner";

                var mail = new CounterStatementMail
                {
                    To = opp.Email,
                    Subject = "Counter Statement Filed Against Your Opposition",
                    OpposerName = opp.Name,
                    FileOwnerName = fileOwnerName,
                    FileNumber = opp.FileNumber,
                    Title = fileTitle,
                    CounterStatementDate = DateTime.Now.ToString("dd MMMM yyyy"),
                    SignatoryName = ""
                };
                await _emailServices.SendMail(new EmailDto
                {
                    To = opp.Email,
                    Subject = "Counter Statement Filed Against Your Opposition",
                    EmailType = EmailType.CounterStatement,
                    CounterStatementMail = mail
                });
                _log.LogInformation($"Counter statement notification sent to opposer {opp.Email}");
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, "Failed to send counter statement notification email — proceeding anyway");
            }

            // Send in-app notifications
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                string fileTitle = file?.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _ => file?.TitleOfTradeMark
                };
                await _notificationServices.SendCounterStatementNotificationsAsync(
                    opposerUserId: opp.UserId,
                    fileOwnerId: opp.FileOwnerId,
                    fileNumber: opp.FileNumber,
                    fileTitle: fileTitle,
                    oppositionId: opp.id
                );
            }
            catch (Exception notifyEx)
            {
                _log.LogWarning(notifyEx, "Failed to send counter statement in-app notifications — non-critical");
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
                Id = Guid.NewGuid().ToString(),
                OppositionId = opp.id,
                Text = dto.Comment,
                Attachments = attachmentUrls,
                PaymentId = rrr,
                UserId = dto.UserId,
                Role = dto.Role?.ToLower(),
                Paid = false,
                SubmittedDate = DateTime.Now
            };
            await _statutoryDeclarationCollection.InsertOneAsync(sd);
            _log.LogInformation($"Statutory Declaration {sd.Id} saved with RRR {rrr}");

            var invoice = new
            {
                paymentId = rrr,
                fileNumber = file.FileId,
                fileTitle = title,
                applicantName = applicant?.Name,
                opposerName = opp.Name,
                cost = cost.Item1,
                serviceFee = cost.Item3
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
                    ApplicationDate = sd.SubmittedDate
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
                        To = applicantEmail,
                        Subject = "Statutory Declaration Filed",
                        EmailType = EmailType.StatutoryDeclaration,
                        StatutoryDeclarationMail = new StatutoryDeclarationMail
                        {
                            To = applicantEmail,
                            Subject = "Statutory Declaration Filed",
                            RecipientName = fileOwner?.Name ?? "Applicant",
                            FilerRole = filerRole,
                            FileNumber = opp.FileNumber,
                            FileTitle = fileTitle,
                            OppositionId = opp.id,
                            DateFiled = DateTime.Now.ToString("dd MMMM yyyy")
                        }
                    });
                    _log.LogInformation("Statutory declaration notification sent to applicant");
                }

                // Notify opposer
                var opposerEmail = opp.Email ?? "";
                if (!string.IsNullOrEmpty(opposerEmail))
                {
                    await _emailServices.SendMail(new EmailDto
                    {
                        To = opposerEmail,
                        Subject = "Statutory Declaration Filed",
                        EmailType = EmailType.StatutoryDeclaration,
                        StatutoryDeclarationMail = new StatutoryDeclarationMail
                        {
                            To = opposerEmail,
                            Subject = "Statutory Declaration Filed",
                            RecipientName = opp.Name ?? "Opposer",
                            FilerRole = filerRole,
                            FileNumber = opp.FileNumber,
                            FileTitle = fileTitle,
                            OppositionId = opp.id,
                            DateFiled = DateTime.Now.ToString("dd MMMM yyyy")
                        }
                    });
                    _log.LogInformation($"Statutory declaration notification sent to opposer {opposerEmail}");
                }
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, "Failed to send statutory declaration notification email — proceeding anyway");
            }

            // Send in-app notifications
            try
            {
                var fileForNotify = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                string notifyTitle = fileForNotify?.Type switch
                {
                    FileTypes.Design => fileForNotify.TitleOfDesign,
                    FileTypes.Patent => fileForNotify.TitleOfInvention,
                    _ => fileForNotify?.TitleOfTradeMark
                };
                await _notificationServices.SendStatutoryDeclarationNotificationsAsync(
                    fileOwnerId: opp.FileOwnerId,
                    opposerUserId: opp.UserId,
                    fileNumber: opp.FileNumber,
                    fileTitle: notifyTitle,
                    oppositionId: opp.id,
                    filerRole: sd.Role ?? "opposer"
                );
            }
            catch (Exception notifyEx)
            {
                _log.LogWarning(notifyEx, "Failed to send statutory declaration in-app notifications — non-critical");
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

            // Fetch withdrawal records keyed by oppositionId
            var allWithdrawals = await _oppositionWithdrawalCollection
                .Find(Builders<OppositionWithdrawal>.Filter.In(w => w.OppositionId, oppIds))
                .ToListAsync();
            var withdrawalByOpp = allWithdrawals
                .GroupBy(w => w.OppositionId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(w => w.CreatedAt).First());

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
                withdrawalByOpp.TryGetValue(opp.id, out var withdrawal);
                string fileName = file?.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _ => file?.TitleOfTradeMark
                };

                var hasCounterStatement = oppCounterStatements.Count > 0;
                var counterStatementDate = hasCounterStatement
                    ? oppCounterStatements.First().SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss")
                    : null;

                // Build audit history
                var history = new List<object>();
                history.Add(new { action = "Opposition filed", date = (opp.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ") });
                if (hasCounterStatement)
                    history.Add(new { action = "Counter statement filed", date = oppCounterStatements.First().SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ssZ") });
                if (oppStatutoryDeclarations.Count > 0)
                    history.Add(new { action = "Statutory declaration filed", date = oppStatutoryDeclarations.First().SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ssZ") });
                if (withdrawal != null && withdrawal.Paid)
                    history.Add(new { action = "Withdrawal request submitted", date = withdrawal.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ") });
                if (opp.Status == ApplicationStatuses.Withdrawn)
                    history.Add(new { action = "Withdrawal approved", date = (opp.ResolvedDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ") });

                return new
                {
                    id = opp.id,
                    fileNumber = opp.FileNumber,
                    fileName = fileName,
                    title = fileName,
                    creatorId = opp.CreatorId ?? opp.UserId,
                    userId = opp.UserId,
                    applicantName = file?.applicants?.FirstOrDefault()?.Name,
                    fileOwner = file?.applicants?.FirstOrDefault()?.Name,
                    trademarkClass = file?.TrademarkClass,
                    name = opp.Name,
                    email = opp.Email,
                    phone = opp.Phone,
                    address = opp.Address,
                    nationality = opp.Nationality,
                    reason = opp.Reason,
                    oppositionText = opp.Reason,
                    status = opp.Status,
                    fileStatus = file?.FileStatus,
                    oppositionStatus = opp.Status,
                    oppositionDate = (opp.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                    paymentId = opp.PaymentId,
                    date = (opp.OppositionDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                    decision = opp.Decision,
                    resolutionStatement = opp.ResolutionStatement,
                    resolvedBy = opp.ResolvedBy,
                    hasCounterStatement = hasCounterStatement,
                    counterStatementDate = counterStatementDate,
                    supportingDocs = opp.SupportingDocs ?? new List<string>(),
                    withdrawalReason = withdrawal?.Reason,
                    withdrawalDocument = withdrawal?.SupportingDocs?.FirstOrDefault(),
                    history = history,
                    counterStatements = oppCounterStatements.Select(cs => new
                    {
                        id = cs.Id,
                        oppositionId = cs.OppositionId,
                        filedBy = cs.UserId,
                        dateFiled = cs.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        statement = cs.Text,
                        submittedDate = cs.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        text = cs.Text,
                        attachments = cs.Attachments ?? new List<string>()
                    }).ToList(),
                    statutoryDeclarations = oppStatutoryDeclarations.Select(sd => new
                    {
                        id = sd.Id,
                        oppositionId = sd.OppositionId,
                        filedBy = sd.UserId,
                        role = sd.Role,
                        dateFiled = sd.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        statement = sd.Text,
                        submittedDate = sd.SubmittedDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                        text = sd.Text,
                        attachments = sd.Attachments ?? new List<string>()
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

    // ─── Backfill Opposition Creator IDs ─────────────────────────────────────
    public async Task BackfillOppositionCreatorIds()
    {
        try
        {
            // Step 1: CreatorId missing → copy from UserId
            var missingCreator = Builders<Opposition>.Filter.And(
                Builders<Opposition>.Filter.Or(
                    Builders<Opposition>.Filter.Exists(o => o.CreatorId, false),
                    Builders<Opposition>.Filter.Eq(o => o.CreatorId, (string?)null),
                    Builders<Opposition>.Filter.Eq(o => o.CreatorId, string.Empty)),
                Builders<Opposition>.Filter.Ne(o => o.UserId, (string?)null),
                Builders<Opposition>.Filter.Ne(o => o.UserId, string.Empty));
            var r1 = await _oppositionCollection.UpdateManyAsync(missingCreator,
                Builders<Opposition>.Update.Pipeline(new[] { new MongoDB.Bson.BsonDocument("$set", new MongoDB.Bson.BsonDocument("CreatorId", "$UserId")) }));
            _log.LogInformation($"Opposition backfill step 1: CreatorId set from UserId on {r1.ModifiedCount} doc(s)");

            // Step 2: UserId missing → copy from CreatorId
            var missingUser = Builders<Opposition>.Filter.And(
                Builders<Opposition>.Filter.Or(
                    Builders<Opposition>.Filter.Exists(o => o.UserId, false),
                    Builders<Opposition>.Filter.Eq(o => o.UserId, (string?)null),
                    Builders<Opposition>.Filter.Eq(o => o.UserId, string.Empty)),
                Builders<Opposition>.Filter.Ne(o => o.CreatorId, (string?)null),
                Builders<Opposition>.Filter.Ne(o => o.CreatorId, string.Empty));
            var r2 = await _oppositionCollection.UpdateManyAsync(missingUser,
                Builders<Opposition>.Update.Pipeline(new[] { new MongoDB.Bson.BsonDocument("$set", new MongoDB.Bson.BsonDocument("UserId", "$CreatorId")) }));
            _log.LogInformation($"Opposition backfill step 2: UserId set from CreatorId on {r2.ModifiedCount} doc(s)");

            // Step 3: Both still missing → look up owner by Email in appUsers
            var orphanFilter = Builders<Opposition>.Filter.And(
                Builders<Opposition>.Filter.Or(
                    Builders<Opposition>.Filter.Exists(o => o.UserId, false),
                    Builders<Opposition>.Filter.Eq(o => o.UserId, (string?)null),
                    Builders<Opposition>.Filter.Eq(o => o.UserId, string.Empty)),
                Builders<Opposition>.Filter.Or(
                    Builders<Opposition>.Filter.Exists(o => o.CreatorId, false),
                    Builders<Opposition>.Filter.Eq(o => o.CreatorId, (string?)null),
                    Builders<Opposition>.Filter.Eq(o => o.CreatorId, string.Empty)),
                Builders<Opposition>.Filter.Ne(o => o.Email, (string?)null),
                Builders<Opposition>.Filter.Ne(o => o.Email, string.Empty));
            var orphans = await _oppositionCollection.Find(orphanFilter).ToListAsync();
            _log.LogInformation($"Opposition backfill step 3: {orphans.Count} orphan doc(s) — resolving by Email");
            int resolved = 0;
            foreach (var o in orphans)
            {
                if (string.IsNullOrWhiteSpace(o.Email)) continue;
                var emailRegex = new MongoDB.Bson.BsonRegularExpression(
                    $"^{System.Text.RegularExpressions.Regex.Escape(o.Email.Trim())}$", "i");
                var user = await _userCollection
                    .Find(Builders<AppUser>.Filter.Regex(u => u.Email, emailRegex))
                    .FirstOrDefaultAsync();
                if (user == null || string.IsNullOrWhiteSpace(user.Id)) continue;
                await _oppositionCollection.UpdateOneAsync(
                    Builders<Opposition>.Filter.Eq(x => x.id, o.id),
                    Builders<Opposition>.Update.Set(x => x.UserId, user.Id).Set(x => x.CreatorId, user.Id));
                resolved++;
            }
            _log.LogInformation($"Opposition backfill step 3: resolved {resolved}/{orphans.Count} doc(s) by Email");
            if (orphans.Count > resolved)
                _log.LogWarning($"Opposition backfill: {orphans.Count - resolved} doc(s) still have no owner — Email did not match any AppUser");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Opposition backfill failed");
        }
    }

    // ─── Submit Opposition Withdrawal ────────────────────────────────────────
    public async Task<(bool success, object? invoice, string message)> SubmitOppositionWithdrawal(OppositionWithdrawalRequestDto dto)
    {
        try
        {
            _log.LogInformation($"Submitting Opposition Withdrawal for opposition {dto.OppositionId}...");

            if (string.IsNullOrWhiteSpace(dto.UserId))
                return (false, null, "UserId is required");
            if (string.IsNullOrWhiteSpace(dto.OppositionId))
                return (false, null, "OppositionId is required");

            var opp = await _oppositionCollection.Find(o => o.id == dto.OppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, null, "Opposition not found");

            // Guard: block if withdrawal already submitted
            if (opp.Status == ApplicationStatuses.WithdrawalRequested)
                return (false, null, "A withdrawal request has already been submitted for this opposition and is pending review. You cannot submit another withdrawal request.");

            // Idempotency: reject if an unpaid or paid withdrawal already exists
            var existing = await _oppositionWithdrawalCollection
                .Find(w => w.OppositionId == dto.OppositionId && w.UserId == dto.UserId)
                .FirstOrDefaultAsync();
            if (existing != null)
            {
                if (existing.Paid)
                    return (false, null, "Withdrawal already confirmed for this opposition");
                // Remove stale unpaid record so user can retry
                await _oppositionWithdrawalCollection.DeleteOneAsync(w => w.Id == existing.Id);
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

            // Upload supporting docs
            var attachmentUrls = new List<string>();
            if (dto.SupportingDocs != null)
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
                            Name        = $"Opposition Withdrawal Document {i + 1}"
                        }
                    });
                    attachmentUrls.Add(urls[0]);
                }
            }

            // Generate payment reference
            var cost = _remitaPaymentUtils.GetCost(PaymentTypes.OppositionWithdrawal, file.Type, applicant?.country);
            var rrr = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                cost.Item1, cost.Item3, cost.Item2,
                "Opposition Withdrawal",
                applicant?.Name ?? opp.Name ?? "Applicant",
                applicant?.Email ?? opp.Email ?? "",
                applicant?.Phone ?? "");

            _log.LogInformation($"[WithdrawalRRR] Remita returned RRR={rrr ?? "NULL"}");
            if (rrr == null)
                return (false, null, "Unable to generate payment reference");

            // Save withdrawal record
            var withdrawal = new OppositionWithdrawal
            {
                Id          = Guid.NewGuid().ToString(),
                OppositionId = opp.id,
                FileNumber  = opp.FileNumber ?? dto.FileNumber,
                FileId      = dto.FileId,
                FileTitle   = title,
                Reason      = dto.Reason,
                UserId      = dto.UserId,
                SupportingDocs = attachmentUrls,
                PaymentId   = rrr,
                Paid        = false,
                CreatedAt   = DateTime.UtcNow
            };
            await _oppositionWithdrawalCollection.InsertOneAsync(withdrawal);
            _log.LogInformation($"Opposition Withdrawal {withdrawal.Id} saved with RRR {rrr}");

            var invoice = new
            {
                paymentId     = rrr,
                fileNumber    = opp.FileNumber ?? dto.FileNumber,
                fileTitle     = title,
                applicantName = applicant?.Name,
                opposerName   = opp.Name,
                oppositionId  = opp.id,
                serviceFee    = cost.Item3,
                cost          = cost.Item1
            };

            return (true, invoice, "Opposition Withdrawal submitted successfully");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error submitting opposition withdrawal");
            throw;
        }
    }

    // ─── Update Opposition Withdrawal Payment ────────────────────────────────
    public async Task<(bool success, string message)> UpdateOppositionWithdrawalPayment(string paymentId)
    {
        try
        {
            _log.LogInformation($"Updating opposition withdrawal payment {paymentId}...");

            var withdrawal = await _oppositionWithdrawalCollection.Find(w => w.PaymentId == paymentId).FirstOrDefaultAsync();
            if (withdrawal == null) return (false, "Withdrawal not found for this payment ID");

            if (withdrawal.Paid) return (true, "Withdrawal payment already confirmed");

            // Guard: block if opposition already in WithdrawalRequested state
            var oppCheck = await _oppositionCollection.Find(o => o.id == withdrawal.OppositionId).FirstOrDefaultAsync();
            if (oppCheck?.Status == ApplicationStatuses.WithdrawalRequested)
                return (false, "A withdrawal request has already been submitted for this opposition and is pending review. You cannot submit another withdrawal request.");

            await _oppositionWithdrawalCollection.UpdateOneAsync(
                Builders<OppositionWithdrawal>.Filter.Eq(w => w.PaymentId, paymentId),
                Builders<OppositionWithdrawal>.Update.Combine(
                    Builders<OppositionWithdrawal>.Update.Set(w => w.Paid, true),
                    Builders<OppositionWithdrawal>.Update.Set(w => w.PaymentStatus, "success"),
                    Builders<OppositionWithdrawal>.Update.Set(w => w.UpdatedAt, DateTime.UtcNow)));

            // Set opposition status to WithdrawalRequested (38)
            var opp = await _oppositionCollection.Find(o => o.id == withdrawal.OppositionId).FirstOrDefaultAsync();
            if (opp != null)
            {
                await _oppositionCollection.UpdateOneAsync(
                    Builders<Opposition>.Filter.Eq(o => o.id, opp.id),
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.WithdrawalRequested));
                _log.LogInformation($"Opposition {opp.id} status set to WithdrawalRequested");

                // Notify the applicant (file owner) by email and update their ApplicationHistory
                try
                {
                    var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                    if (file != null)
                    {
                        await _fillingCollection.UpdateOneAsync(
                            Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                            Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.WithdrawalRequested));

                        string fileTitle = file.Type switch
                        {
                            FileTypes.Design => file.TitleOfDesign,
                            FileTypes.Patent => file.TitleOfInvention,
                            _ => file.TitleOfTradeMark
                        };
                        var fileApplicant = file.applicants?.FirstOrDefault();
                        var applicantEmail = file.Correspondence?.email ?? fileApplicant?.Email ?? "";

                        if (!string.IsNullOrEmpty(applicantEmail))
                        {
                            await _emailServices.SendMail(new EmailDto
                            {
                                To = applicantEmail,
                                Subject = "Opposition Withdrawal Request Submitted",
                                EmailType = EmailType.WithdrawalNotification,
                                WithdrawalNotificationMail = new WithdrawalNotificationMail
                                {
                                    To = applicantEmail,
                                    ApplicantName = fileApplicant?.Name ?? "Applicant",
                                    OpposerName = opp.Name ?? "Opposer",
                                    FileNumber = opp.FileNumber,
                                    FileTitle = fileTitle,
                                    OppositionId = opp.id,
                                    WithdrawalDate = DateTime.Now.ToString("dd MMMM yyyy")
                                }
                            });
                                    _log.LogInformation("Withdrawal notification sent to applicant");
                                    }
                                }
                            }
                            catch (Exception notifyEx)
                            {
                                _log.LogWarning(notifyEx, "Failed to notify applicant of withdrawal — proceeding anyway");
                            }

                            // Send in-app notifications
                            try
                            {
                                var fileForNotify = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
                                string notifyTitle = fileForNotify?.Type switch
                                {
                                    FileTypes.Design => fileForNotify.TitleOfDesign,
                                    FileTypes.Patent => fileForNotify.TitleOfInvention,
                                    _ => fileForNotify?.TitleOfTradeMark
                                };
                                await _notificationServices.SendWithdrawalNotificationsAsync(
                                    fileOwnerId: opp.FileOwnerId,
                                    opposerUserId: opp.UserId,
                                    fileNumber: opp.FileNumber,
                                    fileTitle: notifyTitle,
                                    oppositionId: opp.id
                                );
                            }
                            catch (Exception notifyEx)
                            {
                                _log.LogWarning(notifyEx, "Failed to send withdrawal in-app notifications — non-critical");
                            }
            }

            _log.LogInformation($"Opposition Withdrawal payment confirmed for paymentId {paymentId}");
            return (true, "Withdrawal payment confirmed");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating opposition withdrawal payment");
            throw;
        }
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

    public async Task<AmendmentCost> TrademarkAmendmentCost(OppositionAmendmentReq req)
    {
        _log.LogInformation("Fetching Amendment Cost...");
        try
        {
            var user = await _userCollection.Find(u => u.Id == req.UserId).FirstOrDefaultAsync();
            var file = await _fillingCollection.Find(f => f.FileId == req.FileNumber).FirstOrDefaultAsync();
            if (user is null || file is null)
            {
                _log.LogError($"User {req.UserId} not found");
                throw new KeyNotFoundException();
            }
            var logo = file.Attachments.FirstOrDefault(a => a.name == "representation");

            if (file.FileStatus != ApplicationStatuses.Opposition)
            {
                _log.LogError($"File {req.FileNumber} is not in Opposition status");
                throw new InvalidOperationException("File must be in Opposition status to amend");
            }
            var data = _remitaPaymentUtils.GetCost(PaymentTypes.TrademarkAmendment, file.Type, "", null, null, null);
            var applicant = file.applicants.FirstOrDefault();
            var paymentId = await _remitaPaymentUtils.GenerateRemitaPaymentId(
                data.Item1, data.Item3, data.Item2, "Amendment of Opposed File",
                applicant.Name, applicant.Email, applicant.Phone);
            
            var amendmentCost = new AmendmentCost
            {
                Amount = data.Item1,
                PaymentId = paymentId,
                FileId = req.FileNumber,
                FileTitle = file.TitleOfTradeMark,
                Applicant = applicant,
                Class = file.TrademarkClass,
                AdditionalSpecs = file.AdditionalDescription,
                Disclaimer = file.TrademarkDisclaimer,
                RepresentationUrl = logo.url.FirstOrDefault(),
                FileStatus = file.FileStatus
            };
            return amendmentCost;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching amendment cost");
            throw;
        }
    }
    public async Task<string> TrademarkAmendment(OppositionAmendmentDto dto)
    {
        _log.LogInformation("Processing trademark amendment...");
        try
        {
            var file = await _fillingCollection.Find(f => f.FileId == dto.FileNumber).FirstOrDefaultAsync();
            var user = await _userCollection.Find(u => u.Id == dto.UserId).FirstOrDefaultAsync();

            if (file == null || user == null)
            {
                _log.LogError($"File {dto.FileNumber} not found");
                throw new KeyNotFoundException("File not found");
            }
            if (file.FileStatus != ApplicationStatuses.Opposition)
            {
                _log.LogError($"File {dto.FileNumber} is not in Opposition status");
                throw new InvalidOperationException("File must be in Opposition status to amend");
            }
            var application = new ApplicationInfo
            {
                ApplicationDate = DateTime.Now,
                PaymentId = dto.PaymentId,
                ApplicationType = FormApplicationTypes.Amendment,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                id = Guid.NewGuid().ToString(),
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        beforeStatus = ApplicationStatuses.None,
                        afterStatus = ApplicationStatuses.AwaitingPayment,
                        Date = DateTime.Now,
                        Message = "Amendment of opposedfile",
                        User = user.Name ?? $"{user.FirstName} {user.LastName}",
                        UserId = user.Id
                    }
                }
            };
            var update = new ClericalUpdate
            {
                Id = application.id,
                FilingDate = DateTime.Now,
                UpdateType = "Opposition Amendment",
                PaymentRRR = dto.PaymentId,
            };

            if (!string.IsNullOrWhiteSpace(dto.NewAdditionalDescription))
            {
                update.OldAdditionalDescription = file.AdditionalDescription;
                update.NewAdditionalDescription = dto.NewAdditionalDescription;
            }
            if (!string.IsNullOrWhiteSpace(dto.NewDisclaimer))
            {
                update.OldDisclaimer = file.TrademarkDisclaimer;
                update.NewDisclaimer = dto.NewDisclaimer;
            }
            if (!string.IsNullOrWhiteSpace(dto.NewDisclaimer))
            {
                update.OldDisclaimer = file.TrademarkDisclaimer;
                update.NewDisclaimer = dto.NewDisclaimer;
            }

            if (dto.NewRepresentation is not null)
            {
                update.OldRepresentationUrl = file.Attachments?.FirstOrDefault(a => a.name == "representation")?.url.FirstOrDefault();
                using var ms = new MemoryStream();
                await dto.NewRepresentation.CopyToAsync(ms);

                var urls = await _fileServices.UploadAttachment(new List<TT>
                {
                    new TT
                    {
                        contentType = dto.NewRepresentation.ContentType,
                        data        = ms.ToArray(),
                        fileName    = Path.GetFileName(dto.NewRepresentation.FileName),
                        Name        = "representation"
                    }
                });

                if (urls is { Count: > 0 })
                {
                    update.NewRepresentation = urls[0];
                }
            }

            var finalUpdate = Builders<Filling>.Update.Combine(
                  Builders<Filling>.Update.Push(f => f.ApplicationHistory, application),
                  Builders<Filling>.Update.Push(f => f.ClericalUpdates, update)
            );

            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, file.FileId),
                finalUpdate
            );
            return application.id;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error processing trademark amendment");
            throw;
        }
    }

    // ─── Approve Trademark Amendment (Opposition) ────────────────────────────
    public async Task<(bool success, string message)> ApproveTrademarkAmendment(TreatRecordalDto dto)
    {
        _log.LogInformation($"Approving trademark amendment for File {dto.fileId}, App {dto.appId}...");
        try
        {
            if (dto == null)
                return (false, "Request payload is required");

            if (string.IsNullOrWhiteSpace(dto.fileId) || string.IsNullOrWhiteSpace(dto.appId))
                return (false, "fileId and appId are required");

            var file = await _fillingCollection
                .Find(f => f.FileId == dto.fileId)
                .FirstOrDefaultAsync();
            if (file == null)
                return (false, "File not found");

            file.ClericalUpdates ??= new List<ClericalUpdate>();
            file.ApplicationHistory ??= new List<ApplicationInfo>();
            file.Attachments ??= new List<AttachmentType>();

            var clerical = file.ClericalUpdates.FirstOrDefault(c => c.Id == dto.appId);
            if (clerical == null)
                return (false, "Amendment record not found");

            var app = file.ApplicationHistory.FirstOrDefault(a => a.id == dto.appId);
            if (app == null)
                return (false, "Application history entry not found");

            if (clerical.IsApproved == true)
                return (true, "Amendment already approved");

            // Resolve approving user (optional)
            AppUser user = null;
            if (!string.IsNullOrWhiteSpace(dto.userId))
                user = await _userCollection.Find(u => u.Id == dto.userId).FirstOrDefaultAsync();

            var updates = new List<UpdateDefinition<Filling>>();

            // Apply Additional Description change
            if (!string.IsNullOrWhiteSpace(clerical.NewAdditionalDescription))
            {
                updates.Add(Builders<Filling>.Update.Set(f => f.AdditionalDescription, clerical.NewAdditionalDescription));
            }

            // Apply Disclaimer change
            if (!string.IsNullOrWhiteSpace(clerical.NewDisclaimer))
            {
                updates.Add(Builders<Filling>.Update.Set(f => f.TrademarkDisclaimer, clerical.NewDisclaimer));
            }

            // Apply Representation change (replace or add 'representation' attachment)
            if (!string.IsNullOrWhiteSpace(clerical.NewRepresentation))
            {
                var repIdx = file.Attachments.FindIndex(a => a.name == "representation");
                if (repIdx >= 0)
                    file.Attachments[repIdx].url = new List<string> { clerical.NewRepresentation };
                else
                    file.Attachments.Add(new AttachmentType
                    {
                        name = "representation",
                        url = new List<string> { clerical.NewRepresentation }
                    });

                updates.Add(Builders<Filling>.Update.Set(f => f.Attachments, file.Attachments));
            }

            if (updates.Count == 0)
                return (false, "No amendment changes found to apply");

            // Mark clerical as approved
            clerical.IsApproved = true;
            clerical.DateTreated = DateTime.Now;
            clerical.Reason = dto.reason;

            // Update application history entry
            var userName = user?.Name ?? (user != null ? $"{user.FirstName} {user.LastName}" : "System");
            var previousStatus = app.CurrentStatus;
            app.CurrentStatus = ApplicationStatuses.Approved;
            app.StatusHistory ??= new List<ApplicationHistory>();
            app.StatusHistory.Add(new ApplicationHistory
            {
                beforeStatus = previousStatus,
                afterStatus = ApplicationStatuses.Approved,
                Date = DateTime.Now,
                Message = string.IsNullOrWhiteSpace(dto.reason) ? "Opposition amendment approved" : dto.reason,
                User = userName,
                UserId = dto.userId
            });

            updates.Add(Builders<Filling>.Update.Set(f => f.ClericalUpdates, file.ClericalUpdates));
            updates.Add(Builders<Filling>.Update.Set(f => f.ApplicationHistory, file.ApplicationHistory));

            var result = await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, dto.fileId),
                Builders<Filling>.Update.Combine(updates));

            if (result.ModifiedCount == 0)
                return (false, "No changes were applied to the file");

            _log.LogInformation($"Trademark amendment {dto.appId} approved for file {dto.fileId}");

            // record performance
            if (user != null)
            {
                try
                {
                    _fileServices.SavePerformance(new PerformanceDto
                    {
                        AfterStatus = ApplicationStatuses.Approved,
                        BeforeStatus = previousStatus,
                        ApplicationId = dto.appId,
                        AppUserId = user.Id,
                        ApplicationType = FormApplicationTypes.Amendment,
                        Date = DateTime.Now,
                        FileNumber = dto.fileId,
                        FileType = file.Type,
                        OfficeUnit = Roles.TrademarkOpposition,
                        Reason = dto.reason
                    });
                }
                catch (Exception perfEx)
                {
                    _log.LogWarning(perfEx, "Failed to record performance — proceeding anyway");
                }
            }

            return (true, "Trademark amendment approved successfully");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error approving trademark amendment");
            throw;
        }
    }

    // ─── Treat Withdrawal (approve / refuse) ──────────────────────────────────
    public async Task<(bool success, string message)> TreatWithdrawal(TreatWithdrawalDto dto)
    {
        try
        {
            var opp = await _oppositionCollection.Find(o => o.id == dto.OppositionId).FirstOrDefaultAsync();
            if (opp == null)
                return (false, "Opposition not found");
            if (opp.Status != ApplicationStatuses.WithdrawalRequested && opp.Status != ApplicationStatuses.RequestWithdrawal)
                return (false, "Opposition is not in a withdrawal-request state");

            var action = dto.Action?.Trim().ToLower();
            if (action != "approve" && action != "refuse")
                return (false, "Action must be 'approve' or 'refuse'");
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return (false, "Reason is required");

            var officerName = dto.UserName ?? "Staff";
            var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync();
            var fileId = opp.FileNumber ?? opp.FileTitle ?? dto.OppositionId;

            if (action == "approve")
            {
                // 1. Opposition record → Withdrawn (24)
                await _oppositionCollection.UpdateOneAsync(
                    o => o.id == opp.id,
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Withdrawn));

                // 2 & 3. Filling document — FileStatus (back-office view) + ApplicationHistory[].CurrentStatus (applicant view)
                if (file != null)
                {
                    var prevFileStatus = file.FileStatus;

                    // Find the NewApplication entry in ApplicationHistory to update its CurrentStatus
                    var appEntry = file.ApplicationHistory?.FirstOrDefault(
                        a => a.ApplicationType == FormApplicationTypes.NewApplication);

                    // Update StatusHistory audit entry on the in-memory file object first
                    if (file.ApplicationHistory != null && file.ApplicationHistory.Count > 0)
                    {
                        var targetEntry = appEntry ?? file.ApplicationHistory[0];
                        targetEntry.StatusHistory ??= new List<ApplicationHistory>();
                        targetEntry.StatusHistory.Add(new ApplicationHistory
                        {
                            beforeStatus = prevFileStatus,
                            afterStatus  = ApplicationStatuses.AwaitingCertification,
                            Date         = DateTime.UtcNow,
                            Message      = $"Opposition withdrawn. Approved by {officerName}. Reason: {dto.Reason}",
                            User         = officerName,
                            UserId       = dto.StaffId
                        });
                        // Also update the in-memory object before ReplaceOne so it doesn't overwrite the status
                        file.FileStatus = ApplicationStatuses.AwaitingCertification;
                        if (appEntry != null)
                            appEntry.CurrentStatus = ApplicationStatuses.AwaitingCertification;
                    }

                    // Single ReplaceOne with all changes — avoids UpdateOne being overwritten by ReplaceOne
                    await _fillingCollection.ReplaceOneAsync(f => f.FileId == opp.FileNumber, file);
                }

                // 3. Email opposer
                _ = _emailServices.SendMail(new EmailDto
                {
                    To        = opp.Email,
                    Subject   = "Opposition Withdrawal Approved",
                    EmailType = EmailType.WithdrawalApproved,
                    WithdrawalApprovedMail = new WithdrawalApprovedMail
                    {
                        To            = opp.Email,
                        RecipientName = opp.Name ?? opp.Email,
                        FileNumber    = fileId,
                        FileTitle     = opp.FileTitle ?? "",
                        OfficerName   = officerName,
                        Reason        = dto.Reason,
                        RecipientRole = "opposer"
                    }
                });

                // 4. Email file applicant
                var applicant = file?.applicants?.FirstOrDefault();
                if (applicant != null && !string.IsNullOrWhiteSpace(applicant.Email))
                {
                    _ = _emailServices.SendMail(new EmailDto
                    {
                        To        = applicant.Email,
                        Subject   = "Opposition Against Your Trademark Withdrawn",
                        EmailType = EmailType.WithdrawalApprovedApplicant,
                        WithdrawalApprovedApplicantMail = new WithdrawalApprovedApplicantMail
                        {
                            To            = applicant.Email,
                            RecipientName = applicant.Name ?? applicant.Email,
                            FileNumber    = fileId,
                            FileTitle     = opp.FileTitle ?? "",
                            OfficerName   = officerName
                        }
                    });
                }

                return (true, "Withdrawal approved. File updated to Awaiting Certification.");
            }
            else // refuse
            {
                // Email opposer
                _ = _emailServices.SendMail(new EmailDto
                {
                    To        = opp.Email,
                    Subject   = "Opposition Withdrawal Refused",
                    EmailType = EmailType.WithdrawalRefused,
                    WithdrawalRefusedMail = new WithdrawalRefusedMail
                    {
                        To            = opp.Email,
                        RecipientName = opp.Name ?? opp.Email,
                        FileNumber    = fileId,
                        OfficerName   = officerName,
                        Reason        = dto.Reason
                    }
                });

                // Email file applicant
                var refusedApplicant = file?.applicants?.FirstOrDefault();
                if (refusedApplicant != null && !string.IsNullOrWhiteSpace(refusedApplicant.Email))
                {
                    _ = _emailServices.SendMail(new EmailDto
                    {
                        To        = refusedApplicant.Email,
                        Subject   = "Update on Opposition Against Your Trademark",
                        EmailType = EmailType.WithdrawalRefusedApplicant,
                        WithdrawalRefusedApplicantMail = new WithdrawalRefusedApplicantMail
                        {
                            To            = refusedApplicant.Email,
                            RecipientName = refusedApplicant.Name ?? refusedApplicant.Email,
                            FileNumber    = fileId,
                            FileTitle     = opp.FileTitle ?? "",
                            OfficerName   = officerName
                        }
                    });
                }

                return (true, "Withdrawal refused. No status changes made.");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error treating withdrawal for opposition {OppositionId}", dto.OppositionId);
            throw;
        }
    }
    // ─── Backfill: fix files stuck on Opposition(15) whose opposition is Withdrawn(24) ─
    public async Task<int> BackfillWithdrawnFileStatuses()
    {
        // Use raw BsonDocument to avoid any model deserialization issues
        var oppColl  = db.GetCollection<MongoDB.Bson.BsonDocument>("opposition");
        var fileColl = db.GetCollection<MongoDB.Bson.BsonDocument>(
            _fillingCollection.CollectionNamespace.CollectionName);

        var oppFilter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Or(
            MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("Status", 24),
            MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("Status", "Withdrawn")
        );
        var withdrawnOpps = await oppColl.Find(oppFilter).ToListAsync();

        int fixed_count = 0;
        foreach (var opp in withdrawnOpps)
        {
            var fileNumber = opp.Contains("FileNumber") ? opp["FileNumber"].ToString() : null;
            var fileId     = opp.Contains("FileId")     ? opp["FileId"].ToString()     : null;
            var linkKey    = !string.IsNullOrEmpty(fileNumber) ? fileNumber
                           : !string.IsNullOrEmpty(fileId)     ? fileId : null;
            if (string.IsNullOrEmpty(linkKey)) continue;

            var fileFilter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("FileId", linkKey);
            var file = await fileColl.Find(fileFilter).FirstOrDefaultAsync();
            if (file == null) continue;

            // Check if file is stuck on an opposition-related status (15 or 30)
            if (!file.Contains("FileStatus")) continue;
            var fileStatus = file["FileStatus"];
            bool isOpposition = (fileStatus.IsInt32 && (fileStatus.AsInt32 == 15 || fileStatus.AsInt32 == 30))
                             || (fileStatus.IsString && (fileStatus.AsString == "Opposition" || fileStatus.AsString == "NewOpposition"));
            if (!isOpposition) continue;

            // Update FileStatus = 20 (AwaitingCertification)
            await fileColl.UpdateOneAsync(fileFilter,
                MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update
                    .Set("FileStatus", 20));

            // Also update ApplicationHistory[].CurrentStatus for the applicant view
            // Targets the NewApplication entry (ApplicationType == 0 or "NewApplication")
            var arrayFilterInt = new MongoDB.Driver.BsonDocumentArrayFilterDefinition<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("elem.ApplicationType",
                    new MongoDB.Bson.BsonDocument("$in",
                        new MongoDB.Bson.BsonArray { 0, "NewApplication" })));

            // Try updating matching array element
            var updateOptions = new MongoDB.Driver.UpdateOptions
            {
                ArrayFilters = new MongoDB.Driver.ArrayFilterDefinition[] { arrayFilterInt }
            };
            var arrResult = await fileColl.UpdateOneAsync(fileFilter,
                MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update
                    .Set("ApplicationHistory.$[elem].CurrentStatus", 20),
                updateOptions);

            // Fallback: if no array element matched, update index 0
            if (arrResult.ModifiedCount == 0)
            {
                await fileColl.UpdateOneAsync(fileFilter,
                    MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update
                        .Set("ApplicationHistory.0.CurrentStatus", 20));
            }

            fixed_count++;
        }

        return fixed_count;
    }

    // ─── One-shot: patch ApplicationHistory.CurrentStatus for already-backfilled files ─
    public async Task<int> BackfillWithdrawnApplicationHistory()
    {
        var oppColl  = db.GetCollection<MongoDB.Bson.BsonDocument>("opposition");
        var fileColl = db.GetCollection<MongoDB.Bson.BsonDocument>(
            _fillingCollection.CollectionNamespace.CollectionName);

        // Find all Withdrawn oppositions
        var oppFilter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Or(
            MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("Status", 24),
            MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("Status", "Withdrawn")
        );
        var withdrawnOpps = await oppColl.Find(oppFilter).ToListAsync();

        int fixed_count = 0;
        foreach (var opp in withdrawnOpps)
        {
            var fileNumber = opp.Contains("FileNumber") ? opp["FileNumber"].ToString() : null;
            var fileId     = opp.Contains("FileId")     ? opp["FileId"].ToString()     : null;
            var linkKey    = !string.IsNullOrEmpty(fileNumber) ? fileNumber
                           : !string.IsNullOrEmpty(fileId)     ? fileId : null;
            if (string.IsNullOrEmpty(linkKey)) continue;

            var fileFilter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("FileId", linkKey);
            var file = await fileColl.Find(fileFilter).FirstOrDefaultAsync();
            if (file == null) continue;

            // Ensure FileStatus is AwaitingCertification (20) — it should be after the first backfill
            await fileColl.UpdateOneAsync(fileFilter,
                MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update.Set("FileStatus", 20));

            // Update ApplicationHistory[].CurrentStatus using arrayFilters
            var arrayFilter = new MongoDB.Driver.BsonDocumentArrayFilterDefinition<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("elem.ApplicationType",
                    new MongoDB.Bson.BsonDocument("$in", new MongoDB.Bson.BsonArray { 0, "NewApplication" })));

            var arrResult = await fileColl.UpdateOneAsync(fileFilter,
                MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update
                    .Set("ApplicationHistory.$[elem].CurrentStatus", 20),
                new MongoDB.Driver.UpdateOptions
                {
                    ArrayFilters = new MongoDB.Driver.ArrayFilterDefinition[] { arrayFilter }
                });

            // Fallback: update first element if no match
            if (arrResult.ModifiedCount == 0)
            {
                await fileColl.UpdateOneAsync(fileFilter,
                    MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update
                        .Set("ApplicationHistory.0.CurrentStatus", 20));
            }

            fixed_count++;
        }

        return fixed_count;
    }

    public async Task<object> DebugWithdrawnOppositions()
    {
        // Use raw BsonDocument to avoid deserialization issues
        var oppColl = db.GetCollection<MongoDB.Bson.BsonDocument>("opposition");
        var fileColl = db.GetCollection<MongoDB.Bson.BsonDocument>(
            _fillingCollection.CollectionNamespace.CollectionName);

        // Find all opposition docs where Status == 24 (Withdrawn)
        var filter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Or(
            MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("Status", 24),
            MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("Status", "Withdrawn")
        );
        var oppDocs = await oppColl.Find(filter).ToListAsync();

        var results = new List<object>();
        foreach (var doc in oppDocs)
        {
            var fileNumber = doc.Contains("FileNumber") ? doc["FileNumber"].ToString() : null;
            var fileId     = doc.Contains("FileId")     ? doc["FileId"].ToString()     : null;
            var linkKey    = !string.IsNullOrEmpty(fileNumber) ? fileNumber
                           : !string.IsNullOrEmpty(fileId)     ? fileId : null;

            string? fileStatus = null;
            string? linkedFileId = null;
            if (linkKey != null)
            {
                var fileFilter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("FileId", linkKey);
                var fileDoc = await fileColl.Find(fileFilter).FirstOrDefaultAsync();
                if (fileDoc != null)
                {
                    linkedFileId = fileDoc.Contains("FileId") ? fileDoc["FileId"].ToString() : null;
                    fileStatus   = fileDoc.Contains("FileStatus") ? fileDoc["FileStatus"].ToString() : null;
                }
            }

            results.Add(new
            {
                OppId         = doc.Contains("_id")        ? doc["_id"].ToString()        : null,
                OppFileNumber = fileNumber,
                OppFileId     = fileId,
                OppStatus     = doc.Contains("Status")     ? doc["Status"].ToString()     : null,
                LinkedFileId  = linkedFileId,
                FileStatus    = fileStatus,
                FileFound     = linkedFileId != null
            });
        }
        return results;
    }

}