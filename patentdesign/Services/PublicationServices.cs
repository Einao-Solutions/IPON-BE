using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using patentdesign.Models;
using System.Security.Authentication;
using MongoDB.Bson;
using patentdesign.Dtos.Response;
using QuestPDF.Fluent;
using Tfunctions.pdfs;
using patentdesign.pdfs;
using patentdesign.Dtos.Request;
using patentdesign.Enums;
using patentdesign.Utils;

namespace patentdesign.Services
{
    public class PublicationServices
    {
        private readonly IConfiguration _config;
        private static IMongoCollection<AppUser> _users;
        private static IMongoCollection<PublicationInfo> _pubCollection;
        private static IMongoCollection<Counters> _counters;
        private static IMongoCollection<Filling> _files;
        private static IMongoCollection<PublicationJournal> _journals;
        private static IMongoCollection<AttachmentInfo> _attachments;
        private static IMongoCollection<StaffPerformance> _performanceCollection;
        private MongoClient _mongoClient;
        private EmailServices _emailServices;
        private PaymentUtils _paymentUtils;
        private readonly IServiceProvider _serviceProvider;
        private string attachmentBaseUrl = "https://integration.iponigeria.com";
        //private string attachmentBaseUrl = "http://localhost:5044";
        private readonly ILogger<AuthServices> _log;
        public PublicationServices(IMongoDatabase db, IConfiguration config, EmailServices emailServices, ILogger<AuthServices> log, IServiceProvider serviceProvider, PaymentUtils paymentUtils)
        {
            _config = config;
            _log = log;
            _users = db.GetCollection<AppUser>("appUsers");
            _pubCollection = db.GetCollection<PublicationInfo>("trademarkJournal");
            _files = db.GetCollection<Filling>("files");
            _counters = db.GetCollection<Counters>("counters");
            _performanceCollection = db.GetCollection<StaffPerformance>("staffPerformance");
            _journals = db.GetCollection<PublicationJournal>("publicationJournals");
            _attachments = db.GetCollection<AttachmentInfo>("attachments");
            _emailServices = emailServices;
            _serviceProvider = serviceProvider;
            _paymentUtils = paymentUtils;
        }

        public async Task<string> SavePublication(PublicationDto pub)
        {
            _log.LogInformation("Saving publication info for file number: {FileNumber}", pub.FileNumber);
            var file = await _files.Find(f => f.FileId == pub.FileNumber).FirstOrDefaultAsync();
            if (file is null)
            {
                _log.LogError("File with number {FileNumber} not found. Cannot save publication info.", pub.FileNumber);
                throw new KeyNotFoundException("File not found");
            }

            var existing = await _pubCollection.Find(p => p.FileNumber == pub.FileNumber && !p.IsOpposed).FirstOrDefaultAsync();
            if (existing is not null)
            {
                _log.LogWarning("Publication for file {FileNumber} already exists and is not opposed. Skipping duplicate.", pub.FileNumber);
                return existing.Id;
            }
           

            try
            {
                var pubDate = pub.PublicationDate ?? DateTime.Now;
                var quarter = (pubDate.Month + 2) / 3;
                var batchNumber = $"{pubDate.Year}Q{quarter}";

                var publicationInfo = new PublicationInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    FileNumber = pub.FileNumber,
                    PublicationDate = pub.PublicationDate ?? DateTime.Now,
                    Comment = pub.Comment,
                    StaffId = pub.StaffId,
                    StaffName = pub.StaffName,
                    IsBatchPublished = false,
                    Class = file.TrademarkClass,
                    ClassDescription = file.TrademarkClassDescription,
                    IsOpposed = false,
                    Opposition = pub.Opposition,
                    Title = file.TitleOfTradeMark ?? file.TitleOfInvention,
                    Applicants = file.applicants,
                    Inventors = file.Inventors,
                    Correspondence = file.Correspondence,
                    FilingDate = file.FilingDate,
                    PriorityInfo = file.PriorityInfo,
                    FirstPriorityInfo = file.FirstPriorityInfo,
                    Attachments = file.Attachments,
                    IsManualPublication = pub.IsManualPublication ?? false,
                    BatchVolume = batchNumber
                };

                await _pubCollection.InsertOneAsync(publicationInfo);
                _log.LogInformation("Publication info saved successfully for file number: {FileNumber}", pub.FileNumber);
                return publicationInfo.Id;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        public async Task<int> PublishTrademarks()
        {
            _log.LogInformation("Publishing trademarks with publication date at least 60 days ago");

            var cutoffDate = DateTime.Now.AddDays(-60);

            var filter = Builders<PublicationInfo>.Filter.Lte(p => p.PublicationDate, cutoffDate)
                         & Builders<PublicationInfo>.Filter.Eq(p => p.IsBatchPublished, false);

            // Fetch matching publications to get their FileNumbers
            var publications = await _pubCollection.Find(filter).ToListAsync();

            if (publications.Count == 0)
            {
                _log.LogInformation("No trademarks found eligible for publishing");
                return 0;
            }

            // Update all matching publications to IsPublished = true
            var pubUpdate = Builders<PublicationInfo>.Update.Combine(
                Builders<PublicationInfo>.Update.Set(p => p.IsBatchPublished, true),
                Builders<PublicationInfo>.Update.Set(p=>p.BatchPublishDate, DateTime.Now));

            await _pubCollection.UpdateManyAsync(filter, pubUpdate);

            // Update each corresponding file's status
            var fileNumbers = publications.Select(p => p.FileNumber).Where(f => f != null).Distinct().ToList();

            var fileFilter = Builders<Filling>.Filter.In(f => f.FileId, fileNumbers);

            var fileUpdate = Builders<Filling>.Update
                .Set(f => f.FileStatus, ApplicationStatuses.AwaitingCertification)
                .Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.AwaitingCertification);

            var fileResult = await _files.UpdateManyAsync(fileFilter, fileUpdate);

            _log.LogInformation(
                "Published {PubCount} trademarks and updated {FileCount} files to Awaiting Certification",
                publications.Count, fileResult.ModifiedCount);

            return publications.Count;
        }
        public async Task<byte[]> GetTrademarkJournal(string batchVolume, int vol, int num, DateTime? date)
        {
            _log.LogInformation($"Generating batch publication PDF for Batch {batchVolume}");

            var filter = Builders<PublicationInfo>.Filter.Eq(x => x.BatchVolume, batchVolume);

            var publicationsData = await _pubCollection.Find(filter)
                .Project(x => new PublicationInfo()
                {
                    Title = x.Title ?? "",
                    FileNumber = x.FileNumber,
                    Id = x.Id,
                    Inventors = x.Inventors,
                    PublicationDate = x.PublicationDate,
                    Correspondence = x.Correspondence,
                    Applicants = x.Applicants,
                    ClassDescription = x.ClassDescription,
                    Class = x.Class,
                    Attachments = x.Attachments,
                    Representation = x.Representation
                }).ToListAsync();

            foreach (var publication in publicationsData)
            {
                if (publication.Representation is { Length: > 0 } existingImage && !PdfImageHelper.TryDecodeImage(existingImage))
                {
                    _log.LogWarning("Skipping invalid representation image for publication {PublicationId}", publication.Id);
                    publication.Representation = null;
                }
            }

            _log.LogInformation("Fetched {Count} publications, skipping image downloads (temporarily disabled)", publicationsData.Count);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var gate = new SemaphoreSlim(8); // cap concurrent downloads

            async Task<byte[]?> DownloadAsync(string url)
            {
                await gate.WaitAsync();
                try { return await httpClient.GetByteArrayAsync(url); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Image download failed: {Url}", url);
                    return null;
                }
                finally { gate.Release(); }
            }

            var tasks = publicationsData.Select(async dt =>
            {
                // representation thumbnail
                var representation = dt.Attachments?.FirstOrDefault(x => x.name == "representation");
                if (representation?.url?.Count > 0)
                {
                    var bytes = await DownloadAsync(representation.url[0]);
                    if (bytes != null)
                    {
                        if (PdfImageHelper.TryDecodeImage(bytes))
                        {
                            dt.Representation = bytes;
                            dt.ImagesUrl ??= [];
                            dt.ImagesUrl.Insert(0, bytes);
                        }
                        else
                        {
                            _log.LogWarning("Skipping undecodable representation image for publication {PublicationId} from {ImageUrl}", dt.Id, representation.url[0]);
                        }
                    }
                }

                //// design images
                //if (type == FileTypes.Design)
                //{
                //    var attachment = dt.Images?.FirstOrDefault(x => x.name == "designs");
                //    if (attachment?.url is { Count: > 0 } urls)
                //    {
                //        var downloaded = await Task.WhenAll(urls.Select(DownloadAsync));
                //        dt.ImagesUrl ??= [];
                //        dt.ImagesUrl.AddRange(downloaded.Where(b => b != null)!);
                //    }
                //}
            });
             await Task.WhenAll(tasks);

            _log.LogInformation("Generating PDF");
            var publicationDate = date ?? publicationsData.FirstOrDefault()?.BatchPublishDate ?? DateTime.Now;
            
            // Move CPU-bound PDF rendering off the request thread
            var pdfData = await Task.Run(() =>
                new JournalDocumentNewspaper(publicationsData, FileTypes.TradeMark, publicationDate, vol, num).GeneratePdf());

            return pdfData;
        }
        public async Task<PaginatedPublicationResponse> GetTrademarkPublication(string? text, int? index = 0, int? quantity = 10)
        {
            _log.LogInformation("Fetching batch publications by search text: {Text}", text);

            var titleFilter = text == null
                ? Builders<PublicationInfo>.Filter.Empty
                : Builders<PublicationInfo>.Filter.Regex(x => x.Title, new BsonRegularExpression(text, "i"));

            var combinedFilter = Builders<PublicationInfo>.Filter.And(
                Builders<PublicationInfo>.Filter.Eq(x => x.IsBatchPublished, false),
                titleFilter);

            var result = await _pubCollection.Find(combinedFilter)
                .Project(x => new PublicationInfoDto
                {
                    FileId = x.Id,
                    Title = x.Title ?? "",
                    FileNumber = x.FileNumber,
                    Class = x.Class,
                    Representation = x.Attachments != null
                        ? x.Attachments.FirstOrDefault(att => att.name == "representation") != null
                            ? x.Attachments.FirstOrDefault(att => att.name == "representation").url[0]
                            : null
                        : null,
                    Applicant = x.Applicants != null && x.Applicants.Count > 0
                        ? x.Applicants.Count > 1
                            ? x.Applicants[0].Name + "et al."
                            : x.Applicants[0].Name
                        : null,
                    PublicationDate = x.PublicationDate,
                    FilingDate = x.FilingDate ?? x.PublicationDate
                })
                .Skip(index)
                .Limit(quantity)
                .ToListAsync();

            var count = await _pubCollection.CountDocumentsAsync(combinedFilter);
            _log.LogInformation("Fetched {Count} of {Total} batch publications", result.Count, count);

            return new PaginatedPublicationResponse { Result = result, Count = count };
        }
        public async Task<bool> TreatManualBatch(TreatBatchDto dto)
        {
            try
            {
                _log.LogInformation("Treating manual batch for file number: {FileNumber}", dto.FileNumber);
                var file = await _files.Find(f => f.FileId == dto.FileNumber).FirstOrDefaultAsync();
                if (file is null)
                {
                    _log.LogError("File with number {FileNumber} not found. Cannot treat manual batch.", dto.FileNumber);
                    throw new KeyNotFoundException("File not found");
                }
                var app = file.ApplicationHistory?.FirstOrDefault(a => a.id == dto.ApplicationId);
                if (app is null || app.CurrentStatus != ApplicationStatuses.BatchedManualPublication)
                {
                    _log.LogError("Application with ID {ApplicationId} not found or not in BatchedManualPublication status for file {FileNumber}.", dto.ApplicationId, dto.FileNumber);
                    throw new InvalidOperationException("Application not found or not in the correct status");
                }
                var staff = await _users.Find(u => u.Id == dto.StaffId).FirstOrDefaultAsync() ?? await _users.Find(u => u.CreatorId == dto.StaffId).FirstOrDefaultAsync();
                if (staff is null)
                {
                    _log.LogError("Staff with ID {StaffId} not found. Cannot treat manual batch for file {FileNumber}.", dto.StaffId, dto.FileNumber);
                    throw new KeyNotFoundException("Staff not found");
                }
                var nextStatus = dto.IsApproved ? ApplicationStatuses.Published : ApplicationStatuses.Opposition;
                var history = new ApplicationHistory
                {
                    UserId = staff.Id,
                    User = staff.Name ?? $"{staff.FirstName} {staff.LastName}",
                    Message = dto.Comment,
                    beforeStatus = app.CurrentStatus,
                    afterStatus = nextStatus,
                    Date = DateTime.Now,
                };

                app.StatusHistory ??= [];
                app.StatusHistory.Add(history);
                app.CurrentStatus = nextStatus;
                var perf = new PerformanceDto
                {
                    FileNumber = file.FileId,
                    FileType = file.Type,
                    AfterStatus = nextStatus,
                    BeforeStatus = app.CurrentStatus,
                    ApplicationId = app.id,
                    ApplicationType = app.ApplicationType,
                    AppUserId = staff.Id,
                    Date = DateTime.Now,
                    OfficeUnit = Enums.Roles.TrademarkPublication,
                    Reason = dto.Comment
                };

                SavePerformance(perf);

                if (nextStatus == ApplicationStatuses.Opposition)
                {
                    var oppositionServices = _serviceProvider.GetRequiredService<OppositionService>();
                    var opp = new OppositionRequestDto
                    {

                        FileId = file.Id,
                        FileNumber = file.FileId,
                        FileTitle = file.TitleOfTradeMark ?? file.TitleOfInvention,
                        StaffOpposition = true,
                        StaffId = staff.Id,
                        Name = staff.Name ?? $"{staff.FirstName} {staff.LastName}",
                        Email = staff.Email,
                        Phone = staff.PhoneNumber,
                        Address = staff.Address,
                    };
                    await oppositionServices.StaffOpposition(opp);
                }
                file.PublicationReason = dto.Comment;

                await _files.ReplaceOneAsync(f => f.Id == file.Id, file);

                _log.LogInformation("Manual batch treated successfully for file {FileNumber} with status {Status}", dto.FileNumber, app.CurrentStatus);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to treat manual batch for file {FileNumber}", dto.FileNumber);
                return false;
            }
        }
        private async Task<bool> BatchPubs(StaffBatchRequest dto)
        {
            var pubs = await _pubCollection
                .Find(p => p.IsBatchPublished == false)
                .SortBy(p => p.PublicationDate)
                .Limit(5000)
                .ToListAsync();
               
            if (pubs.Count < 5000)
            {
                throw new InvalidOperationException("Not enough publications to batch. Minimum required is 5000.");
            }

            //var batchVolume = $"{DateTime.UtcNow.Year}V{dto.Volume}N{dto.Number}";
            var pubIds = pubs.Select(p => p.Id).ToList();

            var filter = Builders<PublicationInfo>.Filter.In(p => p.Id, pubIds);
            var update = Builders<PublicationInfo>.Update.Combine(
                Builders<PublicationInfo>.Update.Set(p => p.BatchVolume, dto.BatchVolume),
                Builders<PublicationInfo>.Update.Set(p => p.BatchPublishDate, dto.ReleaseDate),
                Builders<PublicationInfo>.Update.Set(p => p.IsBatchPublished, true));

            await _pubCollection.UpdateManyAsync(filter, update);
            
            pubs.ForEach(p => p.BatchVolume = dto.BatchVolume);

            return true;
        }
        public async Task<bool> BatchJournal(StaffBatchRequest dto)
        {
            var staff = await _users.Find(u => u.Id == dto.UserId).FirstOrDefaultAsync() ?? await _users.Find(u => u.CreatorId == dto.UserId).FirstOrDefaultAsync();
            if (staff is null)
            {
                throw new KeyNotFoundException("Staff not found");
            }

            var batch = _counters.Find(c => c.id == "Publication").FirstOrDefault();
            if (batch is null)
            {
                throw new KeyNotFoundException("Publication counter not found");
            }
            var batchVolume = $"{DateTime.UtcNow.Year}V{dto.Volume}N{dto.Number}";
            dto.BatchVolume = batchVolume;
            await BatchPubs(dto);
            var journal = await GetTrademarkJournal(batchVolume, dto.Volume, dto.Number, dto.ReleaseDate);
            var upload = new TT
            {
                data = journal,
                contentType = "application/pdf",
                fileName = $"{batchVolume}.pdf",
                Name = $"TrademarkJournal_{batchVolume}"
            };
            var journalUrl = await UploadJournal(upload);
            var save = new PublicationJournal
            {
                JournalReleaseDate = dto.ReleaseDate,
                FileType = FileTypes.TradeMark,
                BatchedBy = staff.Name ?? $"{staff.FirstName} {staff.LastName}",
                CreatedAt = DateTime.UtcNow,
                DocumentUrl = journalUrl,
                Batch = batchVolume
            };
            await _journals.InsertOneAsync(save);

            var performance = new PerformanceDto
            {
                AppUserId = staff.Id,
                FileType = FileTypes.TradeMark,
                OfficeUnit = Enums.Roles.TrademarkPublication,
                Date = DateTime.Now,
                Reason = $"Batching trademark publications {batchVolume}",
                BeforeStatus = ApplicationStatuses.Publication,
                AfterStatus = ApplicationStatuses.Published
            };
            SavePerformance(performance);

            return true;
        }
        private static void SavePerformance(PerformanceDto perf)
        {
            var performance = new StaffPerformance
            {
                FileNumber = perf.FileNumber,
                FileType = perf.FileType,
                AfterStatus = perf.AfterStatus,
                BeforeStatus = perf.BeforeStatus,
                ApplicationType = perf.ApplicationType,
                AppUserId = perf.AppUserId,
                Date = perf.Date,
                Reason = perf.Reason,
                OfficeUnit = perf.OfficeUnit,
            };
            _performanceCollection.InsertOne(performance);
        }
        private async Task<string> UploadJournal(TT file)
        {
            var extention = file.fileName.Split(".").Last();
            var trustedFileName = Path.GetRandomFileName();
            trustedFileName = trustedFileName.Split(".")[0] + $".{extention}";

            await _attachments.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = file.contentType,
                Data = file.data
            });
            var url = $"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
            return url;
        }
        public async Task<List<PublicationJournal>> GetJournals(int year)
        {
            _log.LogInformation("Getting journal list for {year}", year);
            return await _journals.Find(j => j.JournalReleaseDate.Year == year).ToListAsync();
        }
        public async Task<JournalRequestDto> GetJournalCost(string userId, string batch)
        {
            _log.LogInformation("Getting journal cost for {userId}", userId);
            var user = await _users.Find(u => u.Id == userId).FirstOrDefaultAsync();
            var name = user.Name ?? $"{user.FirstName} {user.LastName}";
            if (user?.PhoneNumber is null)
            {
                throw new InvalidOperationException("User Phone number not found");
            }
            var journal = await _journals.Find(j => j.Batch == batch).FirstOrDefaultAsync();
            if (DateTime.Now < journal.JournalReleaseDate ) throw new KeyNotFoundException("Journal is not yet available");
            var cost = _paymentUtils.GetCost(PaymentTypes.TrademarkJournal, FileTypes.TradeMark, user.Nationality, null, null, null);
            var paymentId = await _paymentUtils.GenerateRemitaPaymentId(
                cost.Item1, cost.Item3, cost.Item2, "Trademark Journal Request",
                name, user.Email, user.PhoneNumber);

            if (paymentId == null)
            {
                _log.LogError("Failed to generate payment ID");
                return null;
            }

            var app = new ApplicationInfo
            {
                PaymentId = paymentId,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.UtcNow,
                ApplicationType = FormApplicationTypes.TrademarkJournalRequest,
                StatusHistory = new List<ApplicationHistory>()
            };

            var updates = Builders<AppUser>.Update.Push("OtherApplications", app);
            
            var result = await _users.FindOneAndUpdateAsync(
                Builders<AppUser>.Filter.Eq(x => x.Id, userId),
                updates,
                new FindOneAndUpdateOptions<AppUser> { ReturnDocument = ReturnDocument.After }
            );

            var req = new JournalRequestDto
            {
                AppId = app.id,
                Cost = cost.Item1,
                ServiceFee = cost.Item3,
                PaymentId = paymentId,
            };
            
            return req;
        }

        public async Task<(bool, string)> UpdateJournalRequestStatus(string appId, string userId)
        {
            _log.LogInformation("Updating journal request status");
            var user = await _users.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null)
            {
                throw new KeyNotFoundException("User not found");
            }

            var app = user.OtherApplications?.Find(a => a.id == appId);
            if (app == null)
            {
                throw new KeyNotFoundException("Application not found");
            }

            if (string.IsNullOrWhiteSpace(app.PaymentId))
            {
                throw new InvalidOperationException("Payment ID not found");
            }

            var payment = await _paymentUtils.GetDetailsByRRR(app.PaymentId);
            if (payment?.status != "00")
            {
                throw new InvalidOperationException("Unsuccessful payment");
            }

            app.CurrentStatus = ApplicationStatuses.JournalRequested;
            var history = new ApplicationHistory
            {
                Date = DateTime.Now,
                beforeStatus = ApplicationStatuses.AwaitingPayment,
                afterStatus = ApplicationStatuses.JournalRequested,
                Message = "Payment successful, awaiting approval",
                User = "System",
                UserId = "System"
            };
            app.StatusHistory ??= [];
            app.StatusHistory.Add(history);
            var filter = Builders<AppUser>.Filter.And(
                Builders<AppUser>.Filter.Eq(x => x.Id, userId),
                Builders<AppUser>.Filter.ElemMatch(x => x.OtherApplications, a => a.id == appId));
            var updates = Builders<AppUser>.Update.Set("OtherApplications.$", app);
            var result = await _users.FindOneAndUpdateAsync(
                filter,
                updates,
                new FindOneAndUpdateOptions<AppUser> { ReturnDocument = ReturnDocument.After }
            );

            return result is not null
                ? (true, "Journal request status updated successfully")
                : (false, "Journal request status update failed");
        }
    }
}
    