using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Models;
using System.Security.Authentication;
using patentdesign.Dtos.Response;

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
            try
            {
                var publicationInfo = new PublicationInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    FileNumber = pub.FileNumber,
                    PublicationDate = DateTime.Now,
                    Comment = pub.Comment,
                    StaffId = pub.StaffId,
                    StaffName = pub.StaffName,
                    IsPublished = false,
                    IsOpposed = false,
                    Opposition = pub.Opposition,
                    Title = file.TitleOfTradeMark ?? file.TitleOfInvention,
                    Applicants = file.applicants,
                    Inventors = file.Inventors,
                    Correspondence = file.Correspondence,
                    FilingDate = file.FilingDate,
                    PriorityInfo = file.PriorityInfo,
                    FirstPriorityInfo = file.FirstPriorityInfo,
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
                         & Builders<PublicationInfo>.Filter.Eq(p => p.IsPublished, false);

            // Fetch matching publications to get their FileNumbers
            var publications = await _pubCollection.Find(filter).ToListAsync();

            if (publications.Count == 0)
            {
                _log.LogInformation("No trademarks found eligible for publishing");
                return 0;
            }

            // Update all matching publications to IsPublished = true
            var pubUpdate = Builders<PublicationInfo>.Update.Set(p => p.IsPublished, true);
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

    }
}
