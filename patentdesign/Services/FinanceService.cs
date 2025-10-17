using Bogus;
using Bogus.DataSets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Models;
using patentdesign.Utils;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace patentdesign.Services;

public class FinanceService
{
    private static IMongoCollection<FinanceHistory> _financeCollection;
    private static IMongoCollection<Counters> _countersCollection;
    private PaymentUtils _remitaPaymentUtils;
    private MongoClient _mongoClient;

    public FinanceService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils)
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
        _financeCollection = pdDb.GetCollection<FinanceHistory>(patentDesignDbSettings.Value.FinanceCollectionName);
        _remitaPaymentUtils = remitaPaymentUtils;
    }
    
    public async Task<List<FinanceSummaryType>?> GetFinanceSummary(FinanceQueryType data)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument
            {
                {
                    "date", new BsonDocument
                    {
                        { "$gte", data.startDate },
                        { "$lte", data.endDate }
                    }
                }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument()
                {
                    {"reason", "$reason"},
                    {"country", "$country"},
                } },
                
                { "total", new BsonDocument("$sum", "$total") },
                { "techFee", new BsonDocument("$sum", "$techFee") },
                { "ministryFee", new BsonDocument("$sum", "$ministryFee") },
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "total", 1 },
                { "techFee", 1 },
                { "ministryFee", 1 },
                 { "_id", 0 },
                 { "type", "$_id.reason" },
                { "country", "$_id.country" }
            }),
        };
        var result = await _financeCollection.Aggregate<FinanceSummaryType>(pipeline).ToListAsync();
        return result;
    }

}