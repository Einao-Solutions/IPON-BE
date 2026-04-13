using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;
using System.Security.Authentication;

namespace patentdesign.Services
{
    public class AdminServices
    {
        private static IMongoCollection<Filling> _fillingCollection;
        private static IMongoCollection<Counters> _countersCollection;
        private static IMongoCollection<AttachmentInfo> _attachmentCollection;
        private static IMongoCollection<TicketInfo> _ticketsCollection;
        private static IMongoCollection<StatusRequests> _statusCollection;
        private static IMongoCollection<AppUser> _userCollection;
        private static IMongoCollection<FinanceHistory> _financeCollection;
        private static IMongoCollection<PerformanceMarker> _performanceCollection;
        private static IMongoCollection<OppositionType> _oppositionCollection;
        private static IMongoCollection<FileUpdateHistory> _fileUpdateHistoryCollection;
        private static IMongoCollection<SignatureInfo> _signatures;
        private static IMongoCollection<StatusChangeLog> _statusLogs;
        private readonly ILogger<AdminServices> _log;
        
        
        private PaymentUtils _remitaPaymentUtils;
        private MongoClient _mongoClient;
        private FinanceService _financeService;
        private PaymentService _paymentService;
        private EmailServices _emailServices;
        private UsersService _userServices;
        private FilesServices _fileServices;

        private string attachmentBaseUrl = "https://integration.iponigeria.com";
        //private string attachmentBaseUrl = "http://localhost:5044";


        public AdminServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, ILogger<AdminServices> log, PaymentService paymentService, EmailServices emailServices)
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
            _userCollection = pdDb.GetCollection<AppUser>("appUsers");
            _signatures = pdDb.GetCollection<SignatureInfo>("signatures");
            _attachmentCollection =
                pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
            _remitaPaymentUtils = remitaPaymentUtils;
            _statusLogs = pdDb.GetCollection<StatusChangeLog>("StatusChangeLogs");
            _paymentService = paymentService;
            _log = log;
            _fileUpdateHistoryCollection = pdDb.GetCollection<FileUpdateHistory>("FileUpdateHistory");
            _emailServices = emailServices;
        }
        public async Task<StatusChangeLog> ChangeFileStatus(StatusChangeDto dto)
        {
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == dto.FileId).FirstOrDefaultAsync();
                if (file == null)
                {
                    throw new Exception("File not found");
                }
                //log status change
                var log = new StatusChangeLog
                {
                    FileId = dto.FileId,
                    PreviousStatus = file.FileStatus,
                    NewStatus = dto.NewStatus,
                    ChangedById = dto.UserId,
                    Reason = dto.Reason,
                };
                var update = Builders<Filling>.Update.Combine(
                    Builders<Filling>.Update.Set(f => f.FileStatus, dto.NewStatus),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", dto.NewStatus)
                    );
                var result = await _fillingCollection.UpdateOneAsync(
                    f => f.FileId == dto.FileId,
                    update
                );

                log.IsSuccessful = result.ModifiedCount > 0;
                log.DateChanged = DateTime.UtcNow;

                await _statusLogs.InsertOneAsync(log);

                return log;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error changing file status");
                return null;
            }
        }

        public async Task<bool> CreateApplicationHistory(ApplicationHistoryDto dto)
        {
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == dto.FileNumber).FirstOrDefaultAsync();
                if (file == null)
                {
                    _log.LogError("File not found for FileId: {FileId}", dto.FileNumber);
                    throw new KeyNotFoundException("File not found");
                }
                var user = await _userCollection.Find(x => x.Id == dto.UserId).FirstOrDefaultAsync();
                if (user == null)
                {
                    _log.LogError("User not found for UserId: {UserId}", dto.UserId);
                    throw new KeyNotFoundException("User not found");
                }
                var userName = $"{user.FirstName} {user.LastName}";
                _log.LogInformation("Creating application history for FileId: {FileId} by User: {UserName}", dto.FileNumber, userName);
                var app = new ApplicationInfo
                {
                    id = Guid.NewGuid().ToString(),
                    ApplicationDate = dto.ApplicationDate,
                    ApplicationType = dto.ApplicationType,
                    CurrentStatus = dto.CurrentStatus,
                    PaymentId = dto.PaymentId,
                    CertificatePaymentId = dto?.CertificatePaymentId,
                    StatusHistory = new List<ApplicationHistory>
                    {
                        new ApplicationHistory
                        {
                            beforeStatus = ApplicationStatuses.None,
                            afterStatus = dto.CurrentStatus,
                            Date = dto.ApplicationDate,
                            UserId = dto.UserId,
                            User = userName,
                            Message = "Created"
                        }
                    }
                };
                var filter = Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileNumber);
                var update = Builders<Filling>.Update.Push(f => f.ApplicationHistory, app);
                await _fillingCollection.UpdateOneAsync(filter, update);
                _log.LogInformation("Application history created successfully for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, app.id);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error creating application history");
                return false;
            }
        }

        public async Task<bool> UpdateApplicationHistory(UpdateApplicationHistoryDto dto)
        {
            try
            {
                // 1. Fetch file first
                var file = await _fillingCollection.Find(f => f.FileId == dto.FileNumber)
                    .FirstOrDefaultAsync();

                if (file == null || file.ApplicationHistory == null || !file.ApplicationHistory.Any())
                {
                    _log.LogError("File not found or has no application history for FileId: {FileId}", dto.FileNumber);
                    throw new KeyNotFoundException("File not found or has no application history");
                }

                var applicationIndex = file.ApplicationHistory
                    .FindIndex(a => a.id == dto.ApplicationId);

                if (applicationIndex < 0)
                {
                    _log.LogError("Application not found in history for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, dto.ApplicationId);
                    throw new KeyNotFoundException("Application not found in history");
                }
                    

                var isFirstApplication = applicationIndex == 0;

                // 2. Build filter (use positional operator)
                var filter = Builders<Filling>.Filter.And(
                    Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileNumber),
                    Builders<Filling>.Filter.ElemMatch(
                        f => f.ApplicationHistory,
                        a => a.id == dto.ApplicationId
                    )
                );

                var updates = new List<UpdateDefinition<Filling>>();

                // 3. ApplicationHistory updates
                if (dto.ApplicationDate.HasValue)
                    updates.Add(Builders<Filling>.Update
                        .Set("ApplicationHistory.$.ApplicationDate", dto.ApplicationDate.Value));

                if (dto.ApplicationType.HasValue)
                    updates.Add(Builders<Filling>.Update
                        .Set("ApplicationHistory.$.ApplicationType", dto.ApplicationType.Value));

                if (dto.CurrentStatus.HasValue)
                    updates.Add(Builders<Filling>.Update
                        .Set("ApplicationHistory.$.CurrentStatus", dto.CurrentStatus.Value));

                if (!string.IsNullOrEmpty(dto.PaymentId))
                    updates.Add(Builders<Filling>.Update
                        .Set("ApplicationHistory.$.PaymentId", dto.PaymentId));

                if (!string.IsNullOrEmpty(dto.CertificatePaymentId))
                    updates.Add(Builders<Filling>.Update
                        .Set("ApplicationHistory.$.CertificatePaymentId", dto.CertificatePaymentId));

                // 4. Cascade updates if first application
                if (isFirstApplication)
                {
                    if (dto.CurrentStatus.HasValue)
                    {
                        updates.Add(Builders<Filling>.Update
                            .Set(f => f.FileStatus, dto.CurrentStatus.Value));
                    }

                    if (dto.ApplicationDate.HasValue)
                    {
                        updates.Add(Builders<Filling>.Update
                            .Set(f => f.FilingDate, dto.ApplicationDate.Value));
                    }
                }

                if (!updates.Any())
                {
                    _log.LogWarning("No updates to apply for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, dto.ApplicationId);
                    return false;
                }

                // 5. Execute update
                var result = await _fillingCollection.UpdateOneAsync(
                    filter,
                    Builders<Filling>.Update.Combine(updates)
                );
                _log.LogInformation("Application history updated for FileId: {FileId}, ApplicationId: {ApplicationId}. ModifiedCount: {ModifiedCount}", dto.FileNumber, dto.ApplicationId, result.ModifiedCount);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error updating application history");
                throw;
            }
        }

        public async Task<bool> SendAnnouncementMail(AnnouncementMailDto dto)
        {
            _log.LogInformation("Sending announcement mail...");
            try
            {
                var recipients = await _userServices.GetAllUserEmails();
                if (recipients is null)
                {
                    _log.LogError("No recipients found for announcement mail");
                    throw new KeyNotFoundException("No Recipients found");
                }

                var mail = new BulkEmailDto
                {
                    Recipients = recipients,
                    Body = dto.Message,
                    Subject = dto.Subject
                };
                await _emailServices.SendBulkEmailAsync(mail);
                _log.LogInformation("Announcement mail sent successfully to {RecipientCount} recipients", recipients.Count);
                return true;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Failed to send announcement mail");
                throw;
            }
        }

        public async Task<bool> ResetUserPassword(string email)
        {
            try
            {
                _log.LogInformation($"Resetting password for {email}");
                var user = _userCollection.Find(u => u.Email == email).FirstOrDefaultAsync();
                if (user == null)
                {
                    _log.LogError("User not found");
                    return false;
                }

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword("Ipo@1234");
                var update = Builders<AppUser>.Update
                    .Set(u => u.PasswordHash, hashedPassword);

                var result = await _userCollection.UpdateOneAsync(u => u.Email == email, update);
                _log.LogInformation("Password reset completed for {Email}, ModifiedCount: {Count}", email, result.ModifiedCount);
                return true;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Error resetting user password for {Email}", email);
                throw;
            }
        }

        public async Task<bool> UploadSignature(SignatoryDto req)
        {
            try
            {
                _log.LogInformation("Admin uploading signature for {Name}", req.Name);
                if (req.Signature == null) throw new Exception("Signature Image is required");

                // Read the uploaded file into a byte array
                using var ms = new MemoryStream();
                await req.Signature.CopyToAsync(ms);
                var attachmentData = ms.ToArray();

                // Generate a unique filename and save to attachments collection
                var trustedFileName = Path.GetRandomFileName();
                trustedFileName = trustedFileName.Split(".")[0] + Path.GetExtension(req.Signature.FileName);

                await _signatures.InsertOneAsync(new SignatureInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    Designation = req.Designation,
                    SignatureData = attachmentData,
                    Name = req.Name,
                    ApplicationTypes = req.ApplicationTypes,
                });
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        public async Task<SignatureInfo> GetSignatures(FormApplicationTypes appType)
        {
            try
            {
                _log.LogInformation("Retrieving signature for {AppType}...", appType);
                var signature = await _signatures.Find(s => s.ApplicationTypes.Contains(appType)).FirstOrDefaultAsync();
                if (signature == null)
                {
                    _log.LogWarning("No signature found for {AppType}", appType);
                    throw new KeyNotFoundException("Signature not found");
                }
                _log.LogInformation("Signature retrieved for {AppType}", appType);
                return signature;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Error retrieving signature");
                throw;
            }
        }
        public async Task<AppUser> GetUserByEmail(string email)
        {
            _log.LogInformation("Getting user details for {email}",email);
            try
            {
                var user = await _userCollection.Find(u => u.Email == email).FirstOrDefaultAsync();
                if (user == null)
                {
                    _log.LogError("User not found for email: {email}", email);
                    throw new KeyNotFoundException("User not found");
                }

                return user;
            }
            catch (Exception)
            {
                _log.LogError("Failed to get user details");
                throw;
            }
        }
    }
}