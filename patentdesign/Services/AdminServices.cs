using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services.Interface;
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
        private static IMongoCollection<StatusChangeLog> _statusLogs;

        private PaymentUtils _remitaPaymentUtils;
        private MongoClient _mongoClient;
        private FinanceService _financeService;
        private PaymentService _paymentService;

        private string attachmentBaseUrl = "https://integration.iponigeria.com";
        //private string attachmentBaseUrl = "http://localhost:5044";

        //adding log service
        private ILoggerService _log;
        public AdminServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, ILoggerService log, PaymentService paymentService)
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
            _attachmentCollection =
                pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
            _remitaPaymentUtils = remitaPaymentUtils;
            _statusLogs = pdDb.GetCollection<StatusChangeLog>("StatusChangeLogs");
            _paymentService = paymentService;
            _log = log;
            _fileUpdateHistoryCollection = pdDb.GetCollection<FileUpdateHistory>("FileUpdateHistory");

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
                    throw new Exception("File not found");
                }
                var user = await _userCollection.Find(x => x.Id == dto.UserId).FirstOrDefaultAsync();
                if (user == null)
                {
                    throw new Exception("User not found");
                }
                var userName = $"{user.FirstName} {user.LastName}";
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
                    return false;

                var applicationIndex = file.ApplicationHistory
                    .FindIndex(a => a.id == dto.ApplicationId);

                if (applicationIndex < 0)
                    return false;

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
                    return false;

                // 5. Execute update
                var result = await _fillingCollection.UpdateOneAsync(
                    filter,
                    Builders<Filling>.Update.Combine(updates)
                );

                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error updating application history");
                return false;
            }
        }

    }
}