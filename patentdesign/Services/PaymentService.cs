using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Enums;
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
    private static IMongoCollection<XpayApplicant> _payxApplicants;
    private static IMongoCollection<XpayTwallet> _payxWallet;
    private readonly ILogger<PaymentService> _log;
    private PaymentUtils _remitaPaymentUtils;

    private MongoClient _mongoClient;
    //private string attachmentBaseUrl = "https://benin.azure-api.net";
    private string attachmentBaseUrl = "https://integration.iponigeria.com";
    // private string attachmentBaseUrl = "http://localhost:5044";

    public PaymentService(IMongoDatabase db, IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils, ILogger<PaymentService> log)
    {
        _remitaPaymentUtils = remitaPaymentUtils;
        var s = patentDesignDbSettings.Value;
        _paymentCollection = db.GetCollection<PaymentServiceModel>("paymentSetup");
        _payments = db.GetCollection<PaymentRecord>("payments");
        _otherPaymentCollection = db.GetCollection<OtherPaymentModel>("otherPayments");
        _attachmentCollection = db.GetCollection<AttachmentInfo>(s.AttachmentCollectionName);
        _financeCollection = db.GetCollection<FinanceHistory>(s.FinanceCollectionName);
        _payxApplicants = db.GetCollection<XpayApplicant>("xpayApplicants");
        _payxWallet = db.GetCollection<XpayTwallet>("xpayTwallet");
        _log = log;
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
        else if (rrr.Length > 24)
        {
            var payx = await VerifyPayx(rrr);
            if (payx is null)
            {
                _log.LogError("Payx not found");
                return null;
            }
            var response = new RemitaResponseClass
            {
                payerEmail = payx.PayerEmail,
                paymentDate = (payx.PaymentDate)?.ToString("dd MMMM, yyyy"),
                payerName = payx.PayerName,
                payerPhoneNumber = payx.PayerPhone,
                status = "00",
                rrr = rrr
            };
            return response;
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
    private async Task<PayxResponse?> VerifyPayx(string paymentId)
    {
        _log.LogInformation($"Verifying payx Id: {paymentId}...");
        try
        {
            var normalizedPaymentId = paymentId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPaymentId))
            {
                _log.LogError("Payment id is empty");
                return null;
            }

            var lookupIds = new List<string> { normalizedPaymentId };
            var firstToken = normalizedPaymentId.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstToken))
            {
                lookupIds.Add(firstToken);
            }

            foreach (var id in lookupIds.ToList())
            {
                if (id.Length >= 15)
                {
                    lookupIds.Add(id[..15]);
                }
            }

            lookupIds = lookupIds.Distinct().ToList();

            var exactFilter = Builders<XpayTwallet>.Filter.Or(
                Builders<XpayTwallet>.Filter.In(p => p.transID, lookupIds),
                Builders<XpayTwallet>.Filter.In(p => p.ref_no, lookupIds)
            );

            var payment = await _payxWallet.Find(exactFilter).FirstOrDefaultAsync();

            if (payment == null)
            {
                var regexFilters = lookupIds
                    .SelectMany(id => new[]
                    {
                        Builders<XpayTwallet>.Filter.Regex(
                            p => p.transID,
                            new BsonRegularExpression(Regex.Escape(id), "i")),
                        Builders<XpayTwallet>.Filter.Regex(
                            p => p.ref_no,
                            new BsonRegularExpression(Regex.Escape(id), "i"))
                    })
                    .ToList();

                payment = await _payxWallet
                    .Find(Builders<XpayTwallet>.Filter.Or(regexFilters))
                    .FirstOrDefaultAsync();
            }

            if (payment == null)
            {
                _log.LogError("Payment not found");
                return null;
            }
            var applicant = await _payxApplicants.Find(a => a.xid == payment.applicantID.ToString()).FirstOrDefaultAsync();
            if (applicant == null)
            {
                _log.LogError($"Applicant not found for payment {paymentId} (applicantID: {payment.applicantID})");
            }

            var response = new PayxResponse
            {
                PaymentId = paymentId,
                PaymentDate = payment.xreg_date,
                PayerEmail = applicant?.xemail ?? "",
                PayerName = applicant?.xname ?? "",
                PayerPhone = applicant?.xmobile ?? ""
            };

            return response;
        } 
        catch (Exception)
        {
            throw;
        }
    }
}