using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Models;
using System.Security.Authentication;
using MongoDB.Bson;
using patentdesign.Dtos.Response;
using QuestPDF.Fluent;
using Tfunctions.pdfs;
using patentdesign.Dtos.Request;

namespace patentdesign.Services
{
    public class PublicationServices
    {
        private readonly IConfiguration _config;
        private static IMongoCollection<AppUser> _users;
        private static IMongoCollection<PublicationInfo> _pubCollection;
            
        private static IMongoCollection<Filling> _files; 
        private MongoClient _mongoClient;
        private EmailServices _emailServices;
        private OppositionService _oppositionServices;
        private readonly ILogger<AuthServices> _log;
        public PublicationServices(IMongoDatabase db, IConfiguration config, EmailServices emailServices, ILogger<AuthServices> log, OppositionService oppositionServices)
        {
            _config = config;
            _log = log;

            _users = db.GetCollection<AppUser>("appUsers");
            _pubCollection = db.GetCollection<PublicationInfo>("trademarkJournal");
            _files = db.GetCollection<Filling>("files");
            _emailServices = emailServices;
            _oppositionServices = oppositionServices;
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
        public async Task<byte[]> GetTrademarkJournal(DateTime startDate, DateTime endDate, FileTypes type)
        {
            _log.LogInformation("Generating batch publication PDF for type {FileType} from {StartDate} to {EndDate}",
                type, startDate, endDate);

            var filter = Builders<PublicationInfo>.Filter.And(
                Builders<PublicationInfo>.Filter.Gte(x => x.PublicationDate, startDate),
                Builders<PublicationInfo>.Filter.Lte(x => x.PublicationDate, endDate));

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
                    Images = type == FileTypes.Design ? x.Attachments : null,
                    PriorityInfo = type == FileTypes.Patent ? x.PriorityInfo : null,
                    Representation = x.Representation
                }).ToListAsync();

            _log.LogInformation("Fetched {Count} publications, starting image downloads", publicationsData.Count);

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
                        dt.Representation = bytes;
                        dt.ImagesUrl ??= [];
                        dt.ImagesUrl.Insert(0, bytes);
                    }
                }

                // design images
                if (type == FileTypes.Design)
                {
                    var attachment = dt.Images?.FirstOrDefault(x => x.name == "designs");
                    if (attachment?.url is { Count: > 0 } urls)
                    {
                        var downloaded = await Task.WhenAll(urls.Select(DownloadAsync));
                        dt.ImagesUrl ??= [];
                        dt.ImagesUrl.AddRange(downloaded.Where(b => b != null)!);
                    }
                }
            });

            await Task.WhenAll(tasks);

            _log.LogInformation("Image downloads complete; generating PDF");

            // Move CPU-bound PDF rendering off the request thread
            var pdfData = await Task.Run(() =>
                new JournalDocumentNewspaper(publicationsData, type, startDate, endDate).GeneratePdf());

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
                if (nextStatus == ApplicationStatuses.Opposition)
                {
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
                    await _oppositionServices.StaffOpposition(opp);
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
    }
}
    