using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
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


        public AdminServices(IMongoDatabase db, IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, ILogger<AdminServices> log, PaymentService paymentService, EmailServices emailServices)
        {
            var s = patentDesignDbSettings.Value;
            _fillingCollection = db.GetCollection<Filling>(s.FilesCollectionName);
            _countersCollection = db.GetCollection<Counters>(s.CountersCollectionName);
            _financeCollection = db.GetCollection<FinanceHistory>(s.FinanceCollectionName);
            _performanceCollection = db.GetCollection<PerformanceMarker>("performance");
            _statusCollection = db.GetCollection<StatusRequests>("statusrequests");
            _oppositionCollection = db.GetCollection<OppositionType>(s.OppositionCollectionName);
            _ticketsCollection = db.GetCollection<TicketInfo>(s.TicketCollectionName);
            _userCollection = db.GetCollection<AppUser>("appUsers");
            _signatures = db.GetCollection<SignatureInfo>("signatures");
            _attachmentCollection = db.GetCollection<AttachmentInfo>(s.AttachmentCollectionName);
            _remitaPaymentUtils = remitaPaymentUtils;
            _statusLogs = db.GetCollection<StatusChangeLog>("StatusChangeLogs");
            _paymentService = paymentService;
            _log = log;
            _fileUpdateHistoryCollection = db.GetCollection<FileUpdateHistory>("FileUpdateHistory");
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

        public async Task<ApplicationInfo?> CreateApplicationHistory(ApplicationHistoryDto dto)
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
                var (ok, validationError) = ValidateApplicationHistoryPayload(dto);
                if (!ok)
                {
                    _log.LogWarning("Application history validation failed: {Error}", validationError);
                    throw new ArgumentException(validationError);
                }

                var userName = $"{user.FirstName} {user.LastName}";
                _log.LogInformation("Creating application history for FileId: {FileId} by User: {UserName}", dto.FileNumber, userName);
                var uploadedAt = DateTime.UtcNow;
                var processedOldValue = NormalizeHistoryPayload(dto.OldValue);
                var processedNewValue = await ProcessNewValueAsync(dto.NewValue, dto.FileNumber, dto.UserId, uploadedAt);

                var app = new ApplicationInfo
                {
                    id = Guid.NewGuid().ToString(),
                    ApplicationDate = dto.ApplicationDate,
                    ApplicationType = dto.ApplicationType,
                    CurrentStatus = dto.CurrentStatus,
                    PaymentId = dto.PaymentId,
                    CertificatePaymentId = dto?.CertificatePaymentId,
                    OldValue = processedOldValue,
                    NewValue = processedNewValue,
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
                var result = await _fillingCollection.UpdateOneAsync(filter, update);

                if (result.ModifiedCount == 0)
                {
                    _log.LogError("Failed to save application history to database for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, app.id);
                    throw new Exception("Failed to save application history to database - no documents were modified");
                }

                _log.LogInformation("Application history created successfully for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, app.id);
                return app;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error creating application history");
                throw;
            }
        }

        public async Task<ApplicationInfo?> UpdateApplicationHistory(UpdateApplicationHistoryDto dto)
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
                    return null;
                }

                // 5. Execute update
                var result = await _fillingCollection.UpdateOneAsync(
                    filter,
                    Builders<Filling>.Update.Combine(updates)
                );

                if (result.ModifiedCount == 0)
                {
                    _log.LogError("Failed to update application history - no documents were modified for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, dto.ApplicationId);
                    throw new Exception("Failed to update application history in database - no documents were modified");
                }

                // 6. Fetch the updated file to return the updated application
                var updatedFile = await _fillingCollection.Find(f => f.FileId == dto.FileNumber)
                    .FirstOrDefaultAsync();

                if (updatedFile?.ApplicationHistory == null)
                {
                    throw new Exception("Failed to retrieve updated application history");
                }

                var updatedApplication = updatedFile.ApplicationHistory
                    .FirstOrDefault(a => a.id == dto.ApplicationId);

                if (updatedApplication == null)
                {
                    throw new Exception("Updated application history entry not found");
                }

                _log.LogInformation("Application history updated for FileId: {FileId}, ApplicationId: {ApplicationId}. ModifiedCount: {ModifiedCount}", dto.FileNumber, dto.ApplicationId, result.ModifiedCount);
                return updatedApplication;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error updating application history");
                throw;
            }
        }

        public async Task<Filling?> DeleteApplicationHistory(DeleteApplicationHistoryDto dto)
        {
            try
            {
                // 1. Fetch file first
                var file = await _fillingCollection.Find(f => f.FileId == dto.FileNumber)
                    .FirstOrDefaultAsync();

                if (file == null || file.ApplicationHistory == null || !file.ApplicationHistory.Any())
                {
                    _log.LogError("File not found or has no application history for FileId: {FileId}", dto.FileNumber);
                    return null;
                }

                var applicationToRemove = file.ApplicationHistory
                    .FirstOrDefault(a => a.id == dto.ApplicationId);

                if (applicationToRemove == null)
                {
                    _log.LogError("Application not found in history for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, dto.ApplicationId);
                    return null;
                }

                // 2. Build filter and remove the application history entry, returning the post-delete document
                var filter = Builders<Filling>.Filter.Eq(f => f.FileId, dto.FileNumber);

                var update = Builders<Filling>.Update.PullFilter(
                    f => f.ApplicationHistory,
                    a => a.id == dto.ApplicationId
                );

                var options = new FindOneAndUpdateOptions<Filling> { ReturnDocument = ReturnDocument.After };

                // 3. Execute update
                var updatedFile = await _fillingCollection.FindOneAndUpdateAsync(filter, update, options);

                if (updatedFile == null)
                {
                    _log.LogError("FindOneAndUpdate returned null for FileId: {FileId}, ApplicationId: {ApplicationId}", dto.FileNumber, dto.ApplicationId);
                    return null;
                }

                _log.LogInformation(
                    "Application history entry deleted for FileId: {FileId}, ApplicationId: {ApplicationId} by UserId: {UserId} at {Timestamp}",
                    dto.FileNumber, dto.ApplicationId, dto.UserId ?? "unknown", DateTime.UtcNow);

                // 4. Write audit record
                var fileTitle = updatedFile.TitleOfInvention
                    ?? updatedFile.TitleOfDesign
                    ?? updatedFile.TitleOfTradeMark
                    ?? string.Empty;

                await _fileUpdateHistoryCollection.InsertOneAsync(new FileUpdateHistory
                {
                    Id          = Guid.NewGuid().ToString(),
                    FileNumber  = dto.FileNumber,
                    Title       = fileTitle,
                    FileType    = updatedFile.Type,
                    UpdateType  = "DeleteApplicationHistory",
                    AdminName   = dto.UserName ?? dto.UserId ?? "unknown",
                    DateUpdated = DateTime.UtcNow
                });

                return updatedFile;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error deleting application history");
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

        // ---- Private helpers -----------------------------------------------------------------

        private static string GetExtensionFromContentType(string? contentType) =>
            contentType?.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/jpg"  => ".jpg",
                "image/png"  => ".png",
                "image/gif"  => ".gif",
                "application/pdf" => ".pdf",
                "application/msword" => ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                _ => ".bin"
            };

        /// <summary>
        /// Inspects the <c>newValue</c> JSON object for an <c>attachments</c> array.  Any item
        /// that carries a base64 <c>data</c> field is stored in the attachment collection and the
        /// <c>data</c> field is replaced with a downloadable <c>url</c>.  The rest of the object
        /// is preserved verbatim so the front-end receives every property it sent.
        /// </summary>
        private async Task<object?> ProcessNewValueAsync(
            object? rawNewValue,
            string fileId,
            string? userId,
            DateTime uploadedAt)
        {
            if (rawNewValue == null) return null;

            // System.Text.Json deserialises object? fields as JsonElement.
            if (rawNewValue is not System.Text.Json.JsonElement je) return rawNewValue;
            if (je.ValueKind != System.Text.Json.JsonValueKind.Object) return NormalizeHistoryPayload(rawNewValue);

            var result = new Dictionary<string, object?>();

            foreach (var prop in je.EnumerateObject())
            {
                if (prop.Name.Equals("attachments", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var processedAttachments = new List<Dictionary<string, object?>>();

                    foreach (var att in prop.Value.EnumerateArray())
                    {
                        string? fileName = null, contentType = null, url = null;
                        byte[]? data = null;

                        if (att.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            foreach (var attProp in att.EnumerateObject())
                            {
                                switch (attProp.Name.ToLowerInvariant())
                                {
                                    case "filename":
                                        fileName = attProp.Value.GetString(); break;
                                    case "contenttype":
                                        contentType = attProp.Value.GetString(); break;
                                    case "url":
                                        url = attProp.Value.GetString(); break;
                                    case "data":
                                        if (attProp.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                                        {
                                            try { data = Convert.FromBase64String(attProp.Value.GetString()!); }
                                            catch { /* not valid base64 — skip */ }
                                        }
                                        break;
                                }
                            }
                        }

                        // Upload binary data and produce a URL when data is present and no URL yet.
                        if (data != null && data.Length > 0
                            && !string.IsNullOrWhiteSpace(fileName)
                            && !string.IsNullOrWhiteSpace(contentType)
                            && string.IsNullOrWhiteSpace(url))
                        {
                            var ext = Path.GetExtension(fileName);
                            if (string.IsNullOrEmpty(ext))
                                ext = GetExtensionFromContentType(contentType);

                            var trustedId = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ext;

                            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
                            {
                                Id            = trustedId,
                                ContentType   = contentType,
                                Data          = data,
                                Name          = fileName,
                                Size          = data.LongLength,
                                UploadedByUserId  = userId,
                                UploadedAtUtc     = uploadedAt,
                                AssociatedFileId  = fileId,
                                AssociationType   = "ApplicationHistoryAttachment"
                            });

                            url = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedId}";
                            _log.LogInformation(
                                "Stored ApplicationHistory attachment {Id} for file {FileId}", trustedId, fileId);
                        }

                        processedAttachments.Add(new Dictionary<string, object?>
                        {
                            ["fileName"]    = fileName,
                            ["contentType"] = contentType,
                            ["url"]         = url
                        });
                    }

                    result["attachments"] = processedAttachments;
                }
                else
                {
                    result[prop.Name] = NormalizeHistoryPayload(prop.Value);
                }
            }

            return result;
        }

        // ---- Application history retrieval + validation ---------------------------------------

        /// <summary>
        /// Returns a single application history entry (<c>hist</c>) shaped for the SuperAdmin UI.
        /// For assignment entries (applicationType = 5) the <c>assignment</c> object is populated
        /// when available; <c>oldValue</c> / <c>newValue</c> are always returned as fallbacks.
        /// </summary>
        public async Task<ApplicationHistoryResponseDto?> GetApplicationHistoryAsync(string applicationId)
        {
            if (string.IsNullOrWhiteSpace(applicationId)) return null;

            var filter = Builders<Filling>.Filter.ElemMatch(
                f => f.ApplicationHistory, a => a.id == applicationId);

            var file = await _fillingCollection.Find(filter).FirstOrDefaultAsync();
            var app = file?.ApplicationHistory?.FirstOrDefault(a => a.id == applicationId);
            if (app == null) return null;

            return Utils.ApplicationHistoryShaper.Shape(app, file!.FileId);
        }

        /// <summary>
        /// TEMP DIAGNOSTIC: returns the raw stored assignment data for every assignment entry
        /// in a file's application history so we can see exactly what is (or isn't) persisted.
        /// </summary>
        public async Task<object?> DiagnoseAssignmentHistory(string fileNumber)
        {
            var file = await _fillingCollection.Find(f => f.FileId == fileNumber).FirstOrDefaultAsync();
            if (file == null) return null;

            var entries = (file.ApplicationHistory ?? new List<ApplicationInfo>())
                .Select(a => new
                {
                    a.id,
                    applicationType = a.ApplicationType,
                    applicationTypeNumber = (int)a.ApplicationType,
                    a.CurrentStatus,
                    a.ApplicationDate,
                    hasAssignmentObject = a.Assignment != null,
                    assignmentObject = a.Assignment,
                    oldValueType = a.OldValue?.GetType().Name,
                    oldValue = a.OldValue,
                    newValueType = a.NewValue?.GetType().Name,
                    newValue = a.NewValue,
                    shaped = Utils.ApplicationHistoryShaper.Shape(a, file.FileId)
                })
                .ToList();

            return new
            {
                fileNumber = file.FileId,
                fileId = file.Id,
                fileType = file.Type.ToString(),
                applicantsOnFile = file.applicants,
                totalHistoryEntries = entries.Count,
                assignmentEntries = entries.Where(e => e.applicationTypeNumber == (int)FormApplicationTypes.Assignment).ToList(),
                allEntries = entries
            };
        }

        /// <summary>
        /// Enforces the per-application-type required fields described in the SuperAdmin
        /// recordal specification. Only recordal types 5, 7, 8, 9 and 10 are validated;
        /// all other types are accepted unchanged.
        /// </summary>
        private static (bool ok, string? error) ValidateApplicationHistoryPayload(ApplicationHistoryDto dto)
        {
            string? Get(object? p, params string[] names) => Utils.ApplicationHistoryShaper.TryGetPayloadString(p, names);

            switch (dto.ApplicationType)
            {
                case FormApplicationTypes.Assignment: // 5
                    if (string.IsNullOrWhiteSpace(Get(dto.OldValue, "assignorName", "name")))
                        return (false, "Assignment requires assignor name (oldValue.assignorName or oldValue.name).");
                    if (string.IsNullOrWhiteSpace(Get(dto.NewValue, "assigneeName", "name")))
                        return (false, "Assignment requires assignee name (newValue.assigneeName or newValue.name).");
                    break;
                case FormApplicationTypes.RegisteredUser: // 7
                    if (string.IsNullOrWhiteSpace(Get(dto.NewValue, "name")))
                        return (false, "RegisteredUser requires newValue.name.");
                    break;
                case FormApplicationTypes.Merger: // 8
                    if (string.IsNullOrWhiteSpace(Get(dto.NewValue, "name")))
                        return (false, "Merger requires newValue.name.");
                    if (string.IsNullOrWhiteSpace(Get(dto.NewValue, "dateOfMerger")))
                        return (false, "Merger requires newValue.dateOfMerger.");
                    break;
                case FormApplicationTypes.ChangeOfName: // 9
                    if (string.IsNullOrWhiteSpace(Get(dto.NewValue, "newName")))
                        return (false, "ChangeOfName requires newValue.newName.");
                    break;
                case FormApplicationTypes.ChangeOfAddress: // 10
                    if (string.IsNullOrWhiteSpace(Get(dto.NewValue, "newAddress")))
                        return (false, "ChangeOfAddress requires newValue.newAddress.");
                    break;
            }
            return (true, null);
        }

        // NOTE: TryGetPayloadString has been moved to Utils.ApplicationHistoryShaper for reuse.

        private static object? NormalizeHistoryPayload(object? payload)
        {
            if (payload == null) return null;

            if (payload is not System.Text.Json.JsonElement element)
            {
                return payload;
            }

            return element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Object => element
                    .EnumerateObject()
                    .ToDictionary(p => p.Name, p => NormalizeHistoryPayload(p.Value)),
                System.Text.Json.JsonValueKind.Array => element
                    .EnumerateArray()
                    .Select(item => NormalizeHistoryPayload(item))
                    .ToList(),
                System.Text.Json.JsonValueKind.String => element.GetString(),
                System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l)
                    ? l
                    : (element.TryGetDecimal(out var d) ? d : element.GetDouble()),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                _ => null
            };
        }
    }
}