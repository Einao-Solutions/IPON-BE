using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Models;
using System.Security.Authentication;
using MongoDB.Bson;
using patentdesign.Dtos.Response;
using QuestPDF.Fluent;
using Tfunctions.pdfs;

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
        private readonly ILogger<AuthServices> _log;
        public PublicationServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings, IConfiguration config, EmailServices emailServices, ILogger<AuthServices> log)
        {
            _config = config;
            _log = log;

            var useSandbox = patentDesignDbSettings.Value.UseSandbox;

            string digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;

            MongoClientSettings settings = MongoClientSettings.FromUrl(
                new MongoUrl(digitalOcean)
            );
            settings.SslSettings =
                new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            _mongoClient = new MongoClient(settings);
            var pdDb = _mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
            _users = pdDb.GetCollection<AppUser>("appUsers");
            _pubCollection = pdDb.GetCollection<PublicationInfo>("trademarkJournal");
            _files = pdDb.GetCollection<Filling>("files");
            _emailServices = emailServices;
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
            _log.LogInformation("Generating batch publication PDF for type {FileType} from {StartDate} to {EndDate}", type, startDate, endDate);
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

            using var httpClient = new HttpClient();

            foreach (var dt in publicationsData)
            {
                // Pre-download representation image for the headline thumbnail
                var representation = dt.Attachments?.FirstOrDefault(x => x.name == "representation");
                if (representation?.url?.Count > 0)
                {
                    try
                    {
                        var bytes = await httpClient.GetByteArrayAsync(representation.url[0]);
                        dt.Representation = bytes;
                        dt.ImagesUrl ??= [];
                        dt.ImagesUrl.Insert(0, bytes);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to download representation image for {FileNumber}", dt.FileNumber);
                    }
                }

                // Download design images (existing logic)
                if (type == FileTypes.Design)
                {
                    List<byte[]> image_ = dt.ImagesUrl ?? [];
                    var attachment = dt.Images?.FirstOrDefault(x => x.name == "designs");
                    if (attachment?.url != null)
                    {
                        foreach (var url in attachment.url)
                        {
                            image_.Add(await httpClient.GetByteArrayAsync(url));
                        }
                    }
                    dt.ImagesUrl = image_;
                }
            }
            _log.LogInformation("Fetched {Count} publications for PDF generation", publicationsData.Count);

            var pdfData = new JournalDocumentNewspaper(publicationsData, type, startDate, endDate).GeneratePdf();
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
    }
}
    