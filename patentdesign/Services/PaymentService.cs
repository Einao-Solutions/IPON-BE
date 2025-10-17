using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Models;
using patentdesign.Utils;
using QuestPDF.Fluent;
using Tfunctions.pdfs;

namespace patentdesign.Services;
public class PaymentService
{
    private static IMongoCollection<PaymentServiceModel> _paymentCollection;
    private static IMongoCollection<PaymentRecord> _payments;
    private static IMongoCollection<OtherPaymentModel> _otherPaymentCollection;
    private static IMongoCollection<AttachmentInfo> _attachmentCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;

    private PaymentUtils _remitaPaymentUtils;

    private MongoClient _mongoClient;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";

    public PaymentService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils)
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
        var pdDb = _mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _paymentCollection = pdDb.GetCollection<PaymentServiceModel>("paymentSetup");
        _payments = pdDb.GetCollection<PaymentRecord>("payments");
        _otherPaymentCollection = pdDb.GetCollection<OtherPaymentModel>("otherPayments");
        _attachmentCollection =
            pdDb.GetCollection<AttachmentInfo>(patentDesignDbSettings.Value.AttachmentCollectionName);
        _financeCollection = pdDb.GetCollection<FinanceHistory>(patentDesignDbSettings.Value.FinanceCollectionName);
    }

    public async Task<List<PaymentServiceModel>> GetAllPayment()
    {
        var data = await _paymentCollection.
            Find(Builders<PaymentServiceModel>.Filter.Empty).ToListAsync();
        return data;
    }

    public async Task AddPayment(PaymentServiceModel data)
    {
        await _paymentCollection.InsertOneAsync(data);
    }
    
    
    public async Task<bool> UpdatePayment(PaymentServiceModel latestData)
    {
        await _paymentCollection.ReplaceOneAsync(x => x.Id ==latestData.Id,latestData);
        return true;
    }
    
    public async Task<bool> DeletePayment(string id)
    {
        await _paymentCollection.DeleteOneAsync(x => x.Id ==id);
        return true;
    }
    
    public async Task<string?> GeneratePayment(string id, string agentName, string agentEmail, string agentNumber)
    {
        var data=_paymentCollection.Find(x => x.Id ==id).FirstOrDefault();
        Console.WriteLine(data);
        var result=await _remitaPaymentUtils.GenerateRemitaPaymentId(data.total, data.serviceFee, "4019135160", data.Name, agentName, agentEmail, agentNumber);
        return result;
    }

    public async Task<dynamic?> SaveOtherPayment(OtherPaymentModel data)
    {
         // var status = await  ValidatePayment(data.rrr);
         // if (status.status == "00")
         // {
             data.date = DateTime.Now;
             var receiptModel = new Receipt()
             {
                 Date = DateTime.Now.ToString("f"),
                 rrr = data.rrr,
                 Amount = data.amount,
                 payType = PaymentTypes.Other,
                 PaymentFor = data.ServiceName,
                 ApplicantName = data.name,


             };
             var receiptUrl = await SaveReceipt(receiptModel);
             var ackUrl = await saveAck(data);
             // AddToFinance(data.ServiceName, data.Id, status);
             data.ackUrl = ackUrl;
             data.receiptUrl = receiptUrl;
             await _otherPaymentCollection.InsertOneAsync(data);
             return new
             {
                 receiptUrl = receiptUrl,
                 ackUrl = ackUrl
             };
         // }

         return null;
    }

    public async Task<object?> GetOtherPayment(int count, int skip, string? userId)
    {
        var data=await _otherPaymentCollection.
            Find(userId==null? Builders<OtherPaymentModel>.Filter.Empty:Builders<OtherPaymentModel>.Filter.
                Eq(x=>x.agentId, userId) ).Skip(skip).Limit(count).ToListAsync();
        var total=_otherPaymentCollection.CountDocuments(userId==null? Builders<OtherPaymentModel>.Filter.Empty:Builders<OtherPaymentModel>.Filter.
            Eq(x=>x.agentId, userId) );
        return new
        {
            count= total,
            data=data
        };
    }

    private async Task<string> SaveReceipt(Receipt dataReceipt)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
        var uri=$"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        Filling model = new Filling();
        var bytes= new ReceiptModel(dataReceipt, uri, model).GeneratePdf();
        using (var ms = new MemoryStream(bytes))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }
        return uri;
    }

    private async Task<string?> saveAck(OtherPaymentModel data){
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
        var uri=$"{attachmentBaseUrl}/api/files/getAttachment?fileId={trustedFileName}";
        var bytes= new OtherAck(data).GeneratePdf();
        using (var ms = new MemoryStream(bytes))
        {
            await _attachmentCollection.InsertOneAsync(new AttachmentInfo
            {
                Id = trustedFileName,
                ContentType = "application/pdf",
                Data = ms.ToArray()
            });
        }
        return uri;
    }
    
    private void AddToFinance(string reason, string applicationId,
         RemitaResponseClass response)
    {

        var history = _remitaPaymentUtils.GenerateHistory(
            reason,
            "-",
            applicationId,
            "-",
            response,
            FileTypes.Design
        );
        _financeCollection.InsertOne(history);
    }

    public async Task<RemitaResponseClass?> CheckPayment(string rrr)
    {
        if (rrr.Contains("IPO"))
        {
            // check via order_id
            return await _remitaPaymentUtils.GetDetailsByOrderId(rrr);
        }
        else
        {
            try
            {
                return await _remitaPaymentUtils.GetDetailsByRRR(rrr);
            }
            catch
            {
                return null;
            }
        }
    }

    public async Task AddPaymentRecord(PaymentRecord payment)
    {
        await _payments.InsertOneAsync(payment);
    }
   
}