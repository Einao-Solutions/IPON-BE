using System;
using System.Security.Authentication;
using System.Text.Json;
using CloudinaryDotNet;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Dtos.Response;
using patentdesign.Models;
using patentdesign.pdfs;
using patentdesign.Utils;
using QuestPDF.Fluent;
using Tfunctions.pdfs;
using ZstdSharp.Unsafe;

namespace patentdesign.Services;
public class LettersServices
{
    public LettersServices(IOptions<PatentDesignDBSettings> patentDesignDbSettings, PaymentUtils remitaPaymentUtils )
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
        _usersCollection = pdDb.GetCollection<UserCreateType>(patentDesignDbSettings.Value.UsersCollectionName);
        _statusRequestsCollection = pdDb.GetCollection<StatusRequests>("statusrequests");
        _migratedFinanceCollection = pdDb.GetCollection<DBRemitaPayment>("migratedFinance");
        _financeCollection = pdDb.GetCollection<FinanceHistory>("finance");
        _remitaPaymentUtils = remitaPaymentUtils;
        _oppositionCollection =
            pdDb.GetCollection<OppositionType>(patentDesignDbSettings.Value.OppositionCollectionName);
    }
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<DBRemitaPayment> _migratedFinanceCollection;
    private static IMongoCollection<FinanceHistory> _financeCollection;
    private static IMongoCollection<OppositionType> _oppositionCollection;
    private static IMongoCollection<StatusRequests> _statusRequestsCollection;
    private static IMongoCollection<UserCreateType> _usersCollection;
    private MongoClient _mongoClient;
    private PaymentUtils _remitaPaymentUtils;
    
    public async Task<Dictionary<string, object>> GenerateLetter(string? fileId = null,
        ApplicationLetters? letterType = null,
        string? applicationId = null, string? oppositionId = null)
    {
        switch (letterType)
        {
            case ApplicationLetters.NewApplicationCertificateReceipt:
                var data1 = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                PaymentInfo? response1 = null;

                if (data1.ApplicationHistory
                        .FirstOrDefault(x => x.ApplicationType == FormApplicationTypes.NewApplication)?.CertificatePaymentId != null)
                {
                    response1 = await GetPaymentData(data1.Comment,
                        data1.ApplicationHistory
                            .FirstOrDefault(x => x.ApplicationType == FormApplicationTypes.NewApplication)!.CertificatePaymentId);
                }

                var generatedData1 = await NewApplicationReceipt(new Receipt()
                {
                    rrr = response1?.rrr ?? "-",
                    Amount = response1?.amount?.ToString() ?? null,
                    PaymentFor = $"Payment for application for issuance of {data1.Type} certificate",
                    ApplicantName = data1.applicants.Count > 1
                        ? data1.applicants[0]?.Name ?? " " + " et al."
                        : data1.applicants[0]?.Name ?? " ",
                    Title = data1.Type == FileTypes.Design ? data1.TitleOfDesign :
                        data1.Type == FileTypes.Patent ? data1.TitleOfInvention : data1.TitleOfTradeMark,
                    payType = PaymentTypes.NewCreation,
                    Date = response1?.paymentDate == null ? "-" : DateTime.Parse(response1.paymentDate).ToString("f"),
                    FileId = data1.FileId
                }, data1); // Pass 'data1' as the second argument
                return generatedData1;
            case ApplicationLetters.NewApplicationReceipt:
                var data = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                if (data == null)
                {
                    Console.WriteLine("File not found");
                    return null;
                }
                var app = data.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                if (app == null)
                {
                    Console.WriteLine("App not found");
                    return null;
                }
                var remitaResponse = await GetPaymentData(data.Comment, app.PaymentId);
                if (remitaResponse == null)
                {
                    Console.WriteLine("Remita response is null");
                    return null;
                }
                var receiptModel = new Receipt()
                {
                    rrr = remitaResponse?.rrr ?? "-",
                    Amount = remitaResponse?.amount.ToString() ?? "",
                    Date = remitaResponse?.paymentDate ?? "",
                    ApplicantName = data.applicants[0].Name ?? "",
                    payType = PaymentTypes.NewCreation,
                    FileId = data.FileId,
                    Title = data.Type == FileTypes.Design ? data.TitleOfDesign :
                        data.Type == FileTypes.Patent ? data.TitleOfInvention :
                        data.TitleOfTradeMark,
                    Category = data.Type.ToString(),
                    PaymentFor = data.Type.ToString() 
                };
                return await NewApplicationReceipt(receiptModel, data);
            case ApplicationLetters.NewTrademarkAppReceipt:
                var trademarkData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await NewTrademarkAppReceipt(trademarkData, applicationId);
            case ApplicationLetters.NewApplicationCertificateAck:
                var dfd = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                if (dfd == null)
                {
                    Console.WriteLine("file not found");
                    return null;
                }
                var appInfo4 = dfd.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                if (appInfo4 == null)
                {
                    Console.WriteLine("app history not found");
                    return null;
                }
                var drrr = dfd.ApplicationHistory.FirstOrDefault(x => x.id == applicationId).CertificatePaymentId;
                if (drrr == null)
                {
                    Console.WriteLine("no matching certificate id found");
                    return null;
                }
                var remitaResponse4 = await GetPaymentData(dfd.Comment, drrr);
                if (remitaResponse4 == null)
                {
                    Console.WriteLine("remita response is null");
                    return null;
                }
                Console.WriteLine(JsonSerializer.Serialize(remitaResponse4));
                var receiptModel4 = new Receipt()
                {
                    rrr = remitaResponse4?.rrr ?? "-",
                    Amount = remitaResponse4?.amount.ToString(),
                    Date = remitaResponse4?.paymentDate ?? "",
                    ApplicantName = dfd.applicants[0].Name?? "",
                    payType = PaymentTypes.TrademarkCertificate,
                    FileId = dfd.FileId,
                    Title = dfd.Type == FileTypes.Design ? dfd.TitleOfDesign :
                        dfd.Type == FileTypes.Patent ? dfd.TitleOfInvention :
                        dfd.TitleOfTradeMark,
                    Category = dfd.Type.ToString(),
                    PaymentFor = dfd.Type.ToString()
                };
                return await CertificateAcknowledgement(dfd, receiptModel4);
            case ApplicationLetters.NewApplicationAcknowledgement:
                var fileData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                if (fileData == null)
                {
                    Console.WriteLine("File not found");
                    return null;
                }
                var appInfo3 = fileData.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                if (appInfo3 == null)
                {
                    Console.WriteLine("App not found");
                    return null;
                }
                var remitaResponse3 = await GetPaymentData(fileData.Comment, appInfo3.PaymentId);
                if (remitaResponse3 == null)
                {
                    Console.WriteLine("Remita response is null");
                    return null;
                }
                var receiptModel3 = new Receipt()
                {
                    rrr = remitaResponse3?.rrr ?? "-",
                    Amount = remitaResponse3?.amount.ToString() ?? "",
                    Date = remitaResponse3?.paymentDate ?? "",
                    ApplicantName = fileData.applicants[0].Name ?? "",
                    payType = PaymentTypes.LicenseRenew,
                    FileId = fileData.FileId,
                    Title = fileData.Type == FileTypes.Design ? fileData.TitleOfDesign :
                        fileData.Type == FileTypes.Patent ? fileData.TitleOfInvention :
                        fileData.TitleOfTradeMark,
                    Category = fileData.Type.ToString(),
                    PaymentFor = fileData.Type.ToString() 
                };
                return await NewApplicationAcknowledgement(fileData, receiptModel3, applicationId);
            case ApplicationLetters.NewApplicationAcceptance:
                var acceptanceData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await NewApplicationAcceptance(acceptanceData);
            case ApplicationLetters.NewApplicationCertificate:
                var certificateData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await NewApplicationCertificate(certificateData);
            case ApplicationLetters.NewApplicationRejection:
                var rejectionData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await NewApplicationRejection(rejectionData);
            case ApplicationLetters.RenewalReceipt:
                var renew = _fillingCollection.Find(m => m.FileId == fileId).FirstOrDefault();
                return await RenewalReceipt(renew, applicationId);
            case ApplicationLetters.PatentRenewalReceipt:
                var patrenew = _fillingCollection.Find(m => m.FileId == fileId).FirstOrDefault();
                return await RenewalReceipt(patrenew, applicationId);
            case ApplicationLetters.PatentRenewalAcknowlegementLetter:
                var patRenewalFileAck = _fillingCollection.Find(t => t.FileId == fileId).FirstOrDefault();
                var patAppl = patRenewalFileAck.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                if (patAppl == null)
                {
                    Console.WriteLine("Application history not found for the provided ID");
                    return null;
                }
                var patOgpayment = await _remitaPaymentUtils.GetDetailsByRRR(patAppl.PaymentId);
                if (patOgpayment == null)
                {
                    Console.WriteLine("Payment data is null");
                    return null;
                }

                Console.WriteLine(patOgpayment);
                DateTime patPaydate;
                DateTime.TryParse(patOgpayment.paymentDate, out patPaydate);
                Console.WriteLine(patPaydate);
                return await RenewalAcknowledgment(patRenewalFileAck, applicationId, patPaydate);
            case ApplicationLetters.RenewalAck:
                var renewalFileAck = _fillingCollection.Find(t => t.FileId == fileId).FirstOrDefault();
                var appl = renewalFileAck.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                if (appl == null)
                {
                    Console.WriteLine("Application history not found for the provided ID");
                    return null;
                }
                var ogpayment = await _remitaPaymentUtils.GetDetailsByRRR(appl.PaymentId);
                if (ogpayment == null)
                {
                    Console.WriteLine("Payment data is null");
                    return null;
                }

                Console.WriteLine(ogpayment);
                DateTime paydate;
                DateTime.TryParse(ogpayment.paymentDate, out paydate);
                Console.WriteLine(paydate);
                return await RenewalAcknowledgment(renewalFileAck, applicationId, paydate);
            case ApplicationLetters.MergerAck:
                var mergerFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await MergerAck(mergerFile, applicationId);
            case ApplicationLetters.MergerReceipt:
                var merger = _fillingCollection.Find(m => m.FileId == fileId).FirstOrDefault();
                return await MergerReceipt(merger, applicationId);
            case ApplicationLetters.RegisteredUserReceipt:
                var reg = _fillingCollection.Find(m => m.FileId == fileId).FirstOrDefault();
                return await RegisteredUsersReceipt(reg, applicationId);
            case ApplicationLetters.RegisteredUsersAck:
                var regData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await RegisteredUsersAck(regData, applicationId);
            case ApplicationLetters.ChangeOfNameAck:
                var changeOfNameFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await ChangeOfNameAck(changeOfNameFile, applicationId);
            case ApplicationLetters.ChangeOfAddressAck:
                var changeOfAddressFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await ChangeOfAddressAck(changeOfAddressFile, applicationId);
            case ApplicationLetters.ChangeOfAddressReceipt:
                var addy = _fillingCollection.Find(m => m.FileId == fileId).FirstOrDefault();
                return await ChangeOfAddressReceipt(addy, applicationId);
            case ApplicationLetters.ChangeOfNameReceipt:
                var changeOfName = _fillingCollection.Find(m => m.FileId == fileId).FirstOrDefault();
                return await ChangeOfNameReceipt(changeOfName, applicationId);
            case ApplicationLetters.RenewalCertificate:
                var file = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                var application = file.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                if (application == null)
                {
                    Console.WriteLine("Application history not found for the provided ID");
                    return null;
                }
                var payment = await _remitaPaymentUtils.GetDetailsByRRR(application.PaymentId);
                if (payment == null)
                {
                    Console.WriteLine("Payment data is null");
                    return null;
                }

                Console.WriteLine(payment);
                DateTime date;
                DateTime.TryParse(payment.paymentDate, out date);
                Console.WriteLine(date);
                return await RenewalCertificate(file, applicationId, date);
            case ApplicationLetters.PatentRenewalCertificate:
                var patFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                var applicationhis = patFile.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                if (applicationhis == null)
                {
                    Console.WriteLine("Application history not found for the provided ID");
                    return null;
                }
                var paymentdet = await _remitaPaymentUtils.GetDetailsByRRR(applicationhis.PaymentId);
                if (paymentdet == null)
                {
                    Console.WriteLine("Payment data is null");
                    return null;
                }

                Console.WriteLine(paymentdet);
                DateTime patdate;
                DateTime.TryParse(paymentdet.paymentDate, out date);
                Console.WriteLine(date);
                return await RenewalCertificate(patFile, applicationId, date);
            case ApplicationLetters.RecordalReceipt:
                var recordalFileData = _fillingCollection.Find(x => x.Id == fileId).FirstOrDefault();
                return await DataUpdateReceipt(fileId, applicationId, recordalFileData);
            case ApplicationLetters.RecordalAck:
                var recordal = _fillingCollection.Find(t=>t.Id == fileId).FirstOrDefault();
                var applicationInfo = recordal.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
                var remitaRes = await GetPaymentData(recordal.Comment, applicationInfo.PaymentId);
                var receipt = new Receipt()
                {
                    rrr = remitaRes?.rrr ?? "-",
                    Amount = remitaRes?.amount.ToString() ?? "",
                    Date = remitaRes?.paymentDate.ToString(),
                    ApplicantName = recordal.applicants.Count > 1
                        ? recordal.applicants[0].Name + " et al."
                        : recordal.applicants.Count > 0 ? recordal.applicants[0].Name : "",
                    payType = PaymentTypes.LicenseRenew,
                    FileId = recordal.FileId,
                    Title = recordal.Type == FileTypes.Design ? recordal.TitleOfDesign :
                        recordal.Type == FileTypes.Patent ? recordal.TitleOfInvention :
                        recordal.TitleOfTradeMark,
                    Category = recordal.Type.ToString(),
                    PaymentFor = recordal.Type.ToString() + " Recordal"
                };
                return await RecordalAcknowledgment(recordal, applicationId, receipt);
            case ApplicationLetters.RecordalCertificate:
                var recordalData = _fillingCollection.Find(x => x.Id == fileId).FirstOrDefault();
                return await RecordalCert(recordalData, applicationId);
            case ApplicationLetters.AssignmentReceipt:
                var fileData1 = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await AssignmentReceipt(fileData1, applicationId);
            case ApplicationLetters.ClericalUpdateReceipt:
                var clericalFileData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await ClericalUpdateReceipt(clericalFileData, applicationId);
            case ApplicationLetters.AssignmentAck:
                var assgtData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await AssignmentAcknowledgement(assgtData, applicationId);
            case ApplicationLetters.ClericalUpdateAck:
                var cleric = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await ClericalUpdateAck(cleric, applicationId);
            case ApplicationLetters.AssignmentCert:
                var assgnmnt = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await AssignmentCertificate(assgnmnt, applicationId);
            case ApplicationLetters.AssignmentRejection:
                return await AssignmentRejection(fileId, applicationId);
            case ApplicationLetters.NewOppositionReceipt:
                return await OppositionReceipt(oppositionId);
            case ApplicationLetters.NewOppositionAck:
                return await OppositionAcknowledgement(oppositionId);
            case ApplicationLetters.OppositionResponseReceipt:
                return await OppositionResponseReceipt(oppositionId);
            case ApplicationLetters.OppositionResponseAck:
                return await OppositionResponseAck(oppositionId);
            case ApplicationLetters.OppositionResolutionReceipt:
                return await OppositionResolutionReceipt(oppositionId);
            case ApplicationLetters.OppositionResolutionAck:
                return await OppositionResolutionAck(oppositionId);
            case ApplicationLetters.StatusRequestReceipt:
                var statusReq= _statusRequestsCollection.Find(d => d.Id == applicationId).FirstOrDefault();
                var filling = _fillingCollection.Find(f => f.Id == statusReq.fileId).FirstOrDefault();
                return await StatusRequestReceipt(statusReq, filling);
            case ApplicationLetters.StatusRequestAck:
                var statusAckData = _statusRequestsCollection.Find(d => d.Id == applicationId).FirstOrDefault();
                return await StatusRequestAck(statusAckData);
            case ApplicationLetters.StatusSearchReport:
                var statusSearchData = _fillingCollection.Find(d => d.FileId == fileId).FirstOrDefault();
                return await StatusSearchReport(statusSearchData);
            case ApplicationLetters.StatusSearchReceipt:
                var statusSearchReceiptData = _fillingCollection.Find(d => d.FileId == fileId).FirstOrDefault();
                return await StatusSearchReceipt(statusSearchReceiptData, applicationId);  
            case ApplicationLetters.RegisteredUserCertificate:
                var regDat = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await RegisteredUserCert(regDat, applicationId);
            case ApplicationLetters.MergerCert:
                var merg = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await MergerCertificate(merg, applicationId);
            case ApplicationLetters.ChangeOfAddressCert:
                var changeOfAddress = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await ChangeOfAddressCertificate(changeOfAddress, applicationId);
            case ApplicationLetters.ChangeOfNameCert:
                var changeOfNameData = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await ChangeOfNameCertificate(changeOfNameData, applicationId);
            case ApplicationLetters.PublicationStatusUpdateReceipt:
                var pubStatusUpdateReceiptData = _fillingCollection.Find(d => d.FileId == fileId).FirstOrDefault();
                return await PublicationStatusUpdateReceipt(pubStatusUpdateReceiptData, applicationId);
            case ApplicationLetters.PublicationStatusUpdateAcknowledgement: 
                var pubStatusUpdateAckData = _fillingCollection.Find(d => d.FileId == fileId).FirstOrDefault();
                return await PublicationStatusUpdateAcknowledgement(pubStatusUpdateAckData, applicationId);
            case ApplicationLetters.PublicationStatusUpdateApproval:
                var pubStatusUpdateApprovalData = _fillingCollection.Find(d => d.FileId == fileId).FirstOrDefault();
                return await PublicationStatusUpdateApproval(pubStatusUpdateApprovalData, applicationId);
            case ApplicationLetters.PublicationStatusUpdateRefusal:
                var pubStatusUpdateRefusalData = _fillingCollection.Find(d => d.FileId == fileId).FirstOrDefault();
                return await PublicationStatusUpdateRefusal(pubStatusUpdateRefusalData, applicationId);
            case ApplicationLetters.WithdrawalRequestAcknowledgement:
                var withdrawalAckFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await WithdrawalRequestAcknowledgement(withdrawalAckFile, applicationId);
            case ApplicationLetters.WithdrawalRequestApproval:
                var withdrawalApprovalFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await WithdrawalRequestApproval(withdrawalApprovalFile, applicationId);
            case ApplicationLetters.WithdrawalRequestRefusal:
                var withdrawalRefusalFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await WithdrawalRequestRefusal(withdrawalRefusalFile, applicationId);
            case ApplicationLetters.WithdrawalRequestReceipt:
                var withdrawalReceiptFile = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
                return await WithdrawalRequestReceipt(withdrawalReceiptFile, applicationId);
            default:
                return new Dictionary<string, object>() { };
        }
    }

    public async Task<DocumentsDto> DocumentModule(string fileId, string paymentId)
    {
        try
        {
            // Console.WriteLine(fileId);
            Filling file = _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefault();
            if (file == null)
                throw new ArgumentNullException(nameof(file), "File is null");

            ApplicationInfo app = file.ApplicationHistory.FirstOrDefault(x => x.PaymentId == paymentId || x.CertificatePaymentId == paymentId);
            if (app == null)
                throw new ArgumentNullException(nameof(app), "Application history is null");
            if (app.CurrentStatus == ApplicationStatuses.AwaitingPayment)
            {
                throw new Exception("This application is still awaiting payment.");
            }
            PaymentInfo paymentData = await GetPaymentData(file.Comment, paymentId);
            if (paymentData == null)
                throw new ArgumentNullException(nameof(paymentData), "Remita response is null");
            Console.WriteLine("Payment status:" + paymentData.status);
            if(paymentData.status != "00" && paymentData.status != null)
                throw new Exception($"Payment status is not valid: {paymentData.status}");

            List<ApplicationLetters> documents = new List<ApplicationLetters>();

            switch (app.ApplicationType)
            {
                case FormApplicationTypes.NewApplication:
                    if (app.CurrentStatus == ApplicationStatuses.AwaitingSearch || app.CurrentStatus == ApplicationStatuses.AwaitingExaminer || app.CurrentStatus == ApplicationStatuses.Withdrawn || app.CurrentStatus == ApplicationStatuses.Re_conduct)
                    {
                        documents.AddRange(new[]
                        {
                            ApplicationLetters.NewApplicationAcknowledgement,
                            ApplicationLetters.NewApplicationReceipt,
                        });
                    }
                    else if (app.CurrentStatus == ApplicationStatuses.Rejected)
                    {
                        documents.AddRange(new[]
                        {
                            ApplicationLetters.NewApplicationAcknowledgement,
                            ApplicationLetters.NewApplicationReceipt,
                            ApplicationLetters.NewApplicationRejection
                        });
                    }
                    else if(app.CurrentStatus == ApplicationStatuses.Publication || app.CurrentStatus == ApplicationStatuses.AwaitingCertification )
                    {
                        documents.AddRange(new[]
                        {
                            ApplicationLetters.NewApplicationAcknowledgement,
                            ApplicationLetters.NewApplicationReceipt,
                            ApplicationLetters.NewApplicationAcceptance
                        });
                    }
                    else if (app.CurrentStatus == ApplicationStatuses.AwaitingCertificateConfirmation)
                    {
                        documents.AddRange(new[]
                        {
                            ApplicationLetters.NewApplicationAcknowledgement,
                            ApplicationLetters.NewApplicationReceipt,
                            ApplicationLetters.NewApplicationAcceptance,
                            ApplicationLetters.NewApplicationCertificateAck,
                            ApplicationLetters.NewApplicationCertificateReceipt
                        });
                    }
                    else if (app.CurrentStatus == ApplicationStatuses.Active || app.CurrentStatus == ApplicationStatuses.Inactive)
                    {
                        documents.AddRange(new[]
                        {
                            ApplicationLetters.NewApplicationAcknowledgement,
                            ApplicationLetters.NewApplicationReceipt,
                            ApplicationLetters.NewApplicationAcceptance,
                            ApplicationLetters.NewApplicationCertificateAck,
                            ApplicationLetters.NewApplicationCertificate,
                            ApplicationLetters.NewApplicationCertificateReceipt
                        });
                    }
                    break;

                case FormApplicationTypes.ClericalUpdate:
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.ClericalUpdateAck,
                        ApplicationLetters.ClericalUpdateReceipt
                    });
                    break;

                case FormApplicationTypes.LicenseRenewal:
                    if(file.Type == FileTypes.Patent && app.CurrentStatus == ApplicationStatuses.Approved)
                    {
                        documents.AddRange(new[]
                        {
                            ApplicationLetters.PatentRenewalAcknowlegementLetter,
                            ApplicationLetters.PatentRenewalCertificate,
                            ApplicationLetters.PatentRenewalReceipt
                        });
                    }
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.RenewalReceipt,
                        ApplicationLetters.RenewalAck,
                        ApplicationLetters.RenewalCertificate
                    });
                    break;

                case FormApplicationTypes.Assignment:
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.AssignmentAck,
                        ApplicationLetters.AssignmentReceipt,
                        // ApplicationLetters.AssignmentCert
                    });
                    if(app.CurrentStatus == ApplicationStatuses.Approved) documents.Add(ApplicationLetters.AssignmentCert);
                    
                    break;
                
                case FormApplicationTypes.RegisteredUser:
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.RegisteredUsersAck,
                        ApplicationLetters.RegisteredUserReceipt,
                        
                    });
                    if(app.CurrentStatus == ApplicationStatuses.Approved) documents.Add(ApplicationLetters.RegisteredUserCertificate);
                    break;

                case FormApplicationTypes.Merger:
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.MergerAck,
                        ApplicationLetters.MergerReceipt,
                        // ApplicationLetters.MergerCert
                    });
                    if(app.CurrentStatus == ApplicationStatuses.Approved) documents.Add(ApplicationLetters.MergerCert);
                    
                    break;

                case FormApplicationTypes.ChangeOfName:
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.ChangeOfNameAck,
                        ApplicationLetters.ChangeOfNameReceipt
                    });
                    if(app.CurrentStatus == ApplicationStatuses.Approved) documents.Add(ApplicationLetters.ChangeOfNameCert);
                    
                    break;

                case FormApplicationTypes.ChangeOfAddress:
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.ChangeOfAddressAck,
                        ApplicationLetters.ChangeOfAddressReceipt,
                        ApplicationLetters.ChangeOfAddressCert
                    });
                    if(app.CurrentStatus == ApplicationStatuses.Approved) documents.Add(ApplicationLetters.ChangeOfAddressCert);
                    
                    break;

                case FormApplicationTypes.StatusSearch:
                    documents.AddRange(new[]
                    {
                        ApplicationLetters.StatusSearchReceipt,
                        ApplicationLetters.StatusSearchReport
                    });
                    break;
                case FormApplicationTypes.PublicationStatusUpdate:
                    if (app.CurrentStatus == ApplicationStatuses.AwaitingStatusUpdate)
                    {
                        documents.Add(ApplicationLetters.PublicationStatusUpdateReceipt);
                        documents.Add(ApplicationLetters.PublicationStatusUpdateAcknowledgement);
                    }
                    else if (app.CurrentStatus == ApplicationStatuses.Approved)
                    {
                        documents.Add(ApplicationLetters.PublicationStatusUpdateApproval);
                        documents.Add(ApplicationLetters.PublicationStatusUpdateReceipt);
                        documents.Add(ApplicationLetters.PublicationStatusUpdateAcknowledgement);
                    }
                    else if (app.CurrentStatus == ApplicationStatuses.Rejected)
                    { 
                        documents.Add(ApplicationLetters.PublicationStatusUpdateRefusal);
                        documents.Add(ApplicationLetters.PublicationStatusUpdateReceipt);
                        documents.Add(ApplicationLetters.PublicationStatusUpdateAcknowledgement);
                    }
                    break;
                case FormApplicationTypes.WithdrawalRequest:
                    if (app.CurrentStatus == ApplicationStatuses.RequestWithdrawal)
                    {
                        documents.Add(ApplicationLetters.WithdrawalRequestReceipt);
                        documents.Add(ApplicationLetters.WithdrawalRequestAcknowledgement);
                    }
                    else if (app.CurrentStatus == ApplicationStatuses.Approved)
                    {
                        documents.Add(ApplicationLetters.WithdrawalRequestApproval);
                        documents.Add(ApplicationLetters.WithdrawalRequestReceipt);
                        documents.Add(ApplicationLetters.WithdrawalRequestAcknowledgement);
                    }
                    else if (app.CurrentStatus == ApplicationStatuses.Rejected)
                    {
                        documents.Add(ApplicationLetters.WithdrawalRequestRefusal);
                        documents.Add(ApplicationLetters.WithdrawalRequestReceipt);
                        documents.Add(ApplicationLetters.WithdrawalRequestAcknowledgement);
                    }
                    break;

                case FormApplicationTypes.None:
                default:
                    throw new NotSupportedException($"Application type '{app.ApplicationType}' is not supported.");
            }

            return new DocumentsDto
            {
                ApplicationId = app.id,
                PaymentId = paymentId,
                Documents = documents
            };
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            throw;

        }
    }

    public async Task<Dictionary<string, object>> StatusRequestReceipt(StatusRequests data, Filling fileData)
    {
        var payment_data = await GetPaymentData(null,data.paymentId);
        var bytes = new ReceiptModel(new Receipt()
        {
            rrr = payment_data?.rrr??"",
            PaymentFor = "Application status request",
            Amount = payment_data?.amount?.ToString()??"",
            Date = payment_data?.paymentDate??"-",
            payType = PaymentTypes.statusCheck,
            ApplicantName = data.applicantName??"-",
            FileId = data.fileId,
            
        } , "uri", fileData).GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string, object>> StatusRequestAck(StatusRequests data)
    {
        return ReturnDocument(new StatusSearchAck(data).GeneratePdf());
    }

    public async Task<Dictionary<string, object>> NewTrademarkAppReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        var data = new NewTrademarkAppReceipt(file, appId).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> StatusSearchReport(Filling file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        byte[] image = [];

        var representation = file.Attachments?
            .FirstOrDefault(e => e.name == "representation" && e.url != null && e.url.Count > 0);

        if (representation != null &&
            !string.IsNullOrWhiteSpace(representation.url[0]) &&
            !representation.url[0].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                image = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
            }
            catch
            {
                image = [];
            }
        }

        var data = new StatusSearchReport(file, image).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> PublicationStatusUpdateAcknowledgement(Filling file, string? applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);

        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");

        byte[] image = [];

        var representation = file.Attachments?
            .FirstOrDefault(e => e.name == "representation" && e.url != null && e.url.Count > 0);

        if (representation != null &&
            !string.IsNullOrWhiteSpace(representation.url[0]) &&
            !representation.url[0].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                image = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
            }
            catch
            {
                image = [];
            }
        }

        var data = new PublicationStatusUpdateAcknowledgement(file, image, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> PublicationStatusUpdateApproval(Filling file, string applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        byte[] image = [];

        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);

        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");

        var representation = file.Attachments?
            .FirstOrDefault(e => e.name == "representation" && e.url != null && e.url.Count > 0);

        if (representation != null &&
            !string.IsNullOrWhiteSpace(representation.url[0]) &&
            !representation.url[0].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                image = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
            }
            catch
            {
                image = [];
            }
        }

        var data = new PublicationStatusUpdateApproval(file, image, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> PublicationStatusUpdateRefusal(Filling file, string applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        byte[] image = [];

        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);

        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");

        var representation = file.Attachments?
            .FirstOrDefault(e => e.name == "representation" && e.url != null && e.url.Count > 0);

        if (representation != null &&
            !string.IsNullOrWhiteSpace(representation.url[0]) &&
            !representation.url[0].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                image = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
            }
            catch
            {
                image = [];
            }
        }

        var data = new PublicationStatusUpdateRefusal(file, image, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }

    //public async Task<Dictionary<string, object>> StatusSearchReceipt(Filling file)
    //{
    //    if (file == null)
    //        throw new ArgumentNullException(nameof(file), "File data cannot be null");

    //    var data = new StatusSearchReceipt(file).GeneratePdf();
    //    return ReturnDocument(data);
    //}

    public async Task<Dictionary<string, object>> StatusSearchReceipt(Filling file, string? applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);

        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");

        // Pass both file and selectedHistory to the PDF generator
        var data = new StatusSearchReceipt(file, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> PublicationStatusUpdateReceipt(Filling file, string? applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);

        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");

        // Pass both file and selectedHistory to the PDF generator
        var data = new PublicationStatusUpdateReceipt(file, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }



    public async Task<Dictionary<string,object>> NewApplicationReceipt(Receipt data, Filling fileData)
    {
        var bytes= new ReceiptModel(data, "uri", fileData).GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string,object>> NewApplicationAcknowledgement(Filling file, Receipt receipt, string applicationId)
    {
        try
        {
            byte[] data = [];
            if (file.Type is FileTypes.Design)
            {
                List<byte[]> images = [];

                foreach (var url in file.Attachments.FirstOrDefault(x => x.name == "designs").url)
                {
                    
                    if (!string.IsNullOrWhiteSpace(url) && !url.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        var imgBytes = await (new HttpClient()).GetByteArrayAsync(url);
                        if (imgBytes != null && imgBytes.Length > 0)
                        {
                            images.Add(imgBytes);
                        }
                    }
                }
                data = new AcknowledgementModelDesign(file, "uri", images, receipt.Date).GeneratePdf();
            }
            if (file.Type is FileTypes.TradeMark)
            {
                byte[] images = [];
                var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
                if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) && 
                    representation != null && representation.url[0] != "NULL")
                {
                    try
                    {
                        images = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
                    }
                    catch (HttpRequestException)
                    {
                        Console.WriteLine("Image not found or invalid URL.");
                        images = [];
                    }
                }
                data = new AcknowledgementModelTrademark(file, "uri", images, receipt).GeneratePdf();
            }
            if (file.Type is FileTypes.Patent)
            {
                data = new AcknowledgementModelPatent(file, "uri").GeneratePdf();
            }
            return ReturnDocument(data);
        }
        catch(Exception ex)
        {
            throw ex;
        }
       
    }

    public async Task<Dictionary<string, object>> NewApplicationAcceptance(Filling fileData)
    {
        byte[]? data = [];
        List<byte[]> images = [];
        byte[] sigdata = [];
        var examinerName = "";
        if (fileData.Type!=FileTypes.TradeMark)
        {
            examinerName = fileData.ApplicationHistory.First().StatusHistory
                .FirstOrDefault(x => x.afterStatus == ApplicationStatuses.Active)?.User ?? "-";
        }
        else
        {
            examinerName = fileData.ApplicationHistory.First().StatusHistory
                .FirstOrDefault(x => x.afterStatus == ApplicationStatuses.Publication)?.User ?? "-";
        }
        if (fileData.Type is FileTypes.Design)
        {
            foreach (var url in fileData.Attachments.FirstOrDefault(x => x.name == "designs").url)
            {
                images.Add(await (new HttpClient()).GetByteArrayAsync(url));
            }
            data =
                new AcceptanceModelDesign(fileData, $"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", sigdata, images, examinerName).GeneratePdf();
            // new AcceptanceModelDesign(tradeData, "uri", sigdata, images, "ILoduba C.O").GeneratePdf();
        }
        if (fileData.Type is FileTypes.Patent)
        {
            data = new AcceptanceModelPatent(fileData, $"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", sigdata, examinerName).GeneratePdf();
            // data = new AcceptanceModelPatent(tradeData, "uri", sigdata, "Eno-obong Usen").GeneratePdf();
        }

        if (fileData.Type is FileTypes.TradeMark)
        {
            byte[] image = [];
            try
            {
                if ((fileData.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) ||
                    fileData.Attachments.FirstOrDefault(e => e.name == "representation") != null)
                {
                    image = await (new HttpClient()).GetByteArrayAsync(fileData.Attachments
                        .First(r => r.name == "representation").url[0]);
                }
            }
            catch (Exception)
            {
                image = [];
            }

            data = new AcceptanceModelTrademark(fileData, $"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", sigdata, examinerName, image).GeneratePdf();
        }
        return ReturnDocument(data);
    }

    //public async Task<Dictionary<string, object>> NewAppCertReceipt(Filling file, string rrr)
    //{
    //    var dets = await _remitaPaymentUtils.GetDetailsByRRR(rrr);
    //    return SaveOtherReceipt(new Receipt()
    //        {
    //            rrr = rrr,
    //            Amount = dets.amount.ToString(),
    //            Date = DateTime.Now.ToString("f"),
    //            PaymentFor = "Application for issuance of  trademark certificate",
    //            payType = PaymentTypes.Other,
    //            Title = file.TitleOfTradeMark,
    //            FileId = file.FileId
    //        }
    //    );
    //}

    public async Task<Dictionary<string, object>> CertificateAcknowledgement(Filling file, Receipt receipt)
    {
        try
        {
            byte[] data = [];
            //if (file.Type is FileTypes.Design)
            //{
            //    List<byte[]> images = [];

            //    foreach (var url in file.Attachments.FirstOrDefault(x => x.name == "designs").url)
            //    {
            //        images.Add(await (new HttpClient()).GetByteArrayAsync(url));
            //    }
            //    data = new AcknowledgementModelDesign(file, "uri", images).GeneratePdf();
            //}
            if (file.Type is FileTypes.TradeMark)
            {
                byte[] images = [];
                var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
                if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) && 
                    representation != null && representation.url[0] != "NULL")
                {
                    try
                    {
                        images = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
                    }
                    catch (HttpRequestException)
                    {
                        // Log the error if needed
                        images = [];
                    }
                }
                data = new CertificateAcknowledgement(file, images, receipt).GeneratePdf();
            }
            //if (file.Type is FileTypes.Patent)
            //{
            //    data = new AcknowledgementModelPatent(file, "uri").GeneratePdf();
            //}
            return ReturnDocument(data);
        }
        catch (Exception ex)
        {
            throw ex;
        }
    }
    
    private Dictionary<string, object> SaveOtherReceipt(Receipt dataReceipt, Filling fileData)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
        var bytes= new ReceiptModel(dataReceipt, "uri", fileData).GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string,object>> NewApplicationRejection(Filling fileData)
    {
        List<byte[]> images = [];
        byte[]? data = [];
        byte[] sigdata = [];


        if (fileData.Type is FileTypes.Design)
        {
            foreach (var url in fileData.Attachments.FirstOrDefault(x => x.name == "designs").url)
            {
                images.Add(await (new HttpClient()).GetByteArrayAsync(url));
            }
        }

        var examinerName = fileData.ApplicationHistory[0].StatusHistory?.FirstOrDefault(x =>
                x.afterStatus == ApplicationStatuses.Rejected)
            ?.User ?? "Examiner";
        if (fileData.Type is FileTypes.Design)
        {
            data = new RejectionModelDesign(fileData, "uri", sigdata, images, examinerName).GeneratePdf();
        }

        if (fileData.Type is FileTypes.Patent)
        {
            data = new RejectionModelPatent(fileData, "uri", sigdata, examinerName).GeneratePdf();
        }

        if (fileData.Type is FileTypes.TradeMark)
        {
            byte[] image = [];
            try
            {
                if ((fileData.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) ||
                    fileData.Attachments.FirstOrDefault(e => e.name == "representation") != null)
                {
                    image = await (new HttpClient()).GetByteArrayAsync(fileData.Attachments
                        .First(r => r.name == "representation").url[0]);
                }
            }
            catch (Exception)
            {
                image = [];
            }
            data = new RejectionModelTrademark(fileData, "uri", sigdata, examinerName, image).GeneratePdf();
        }

        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> NewApplicationCertificate(Filling fileData)
    {
        byte[]? imageData = [];
        if (fileData == null)
            throw new ArgumentNullException(nameof(fileData), "File data cannot be null");
        if (fileData.ApplicationHistory == null || !fileData.ApplicationHistory.Any())
            throw new ArgumentException("Application history is missing", nameof(fileData));
        if (fileData.Attachments == null)
            throw new ArgumentException("Attachments are missing", nameof(fileData));
        if (fileData.Type == FileTypes.TradeMark &&
            fileData.Attachments.FirstOrDefault(x => x.name == "representation") != null)
        {
            try
            {
                imageData = await (new HttpClient()).GetByteArrayAsync(fileData.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                imageData = null;
            }
        }
        var data = fileData.Type == FileTypes.Design
            ? new DesignCertificate(fileData, fileData.ApplicationHistory[0].ExpiryDate.ToString()).GeneratePdf()
            : fileData.Type == FileTypes.TradeMark
                ? new NewTrademarkCertificate(fileData,$"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", imageData).GeneratePdf()
                : new ApprovedCertificate(fileData, $"https://portal.iponigeria.com/qr?fileId={fileData.FileId}").GeneratePdf();
        return ReturnDocument(data);
    }
    
    public async Task<Dictionary<string,object>> RenewalReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        var app = file.ApplicationHistory.FirstOrDefault(x => x.id == appId);
        if (app == null)
            throw new Exception("Application history not found for provided ID");

        // Get payment details using GetPaymentData (just like DataUpdateReceipt)
        var paymentResponse = await GetPaymentData(file.Comment, app.PaymentId);

        var receipt = new Receipt
        {
            Amount = paymentResponse?.amount?.ToString() ?? "",
            ApplicantName = file.applicants.Count > 1
                ? file.applicants[0]?.Name + " et al."
                : file.applicants.Count == 1 ? file.applicants[0].Name : "",
            rrr = paymentResponse?.rrr ?? "-",
            PaymentFor = file.Type == FileTypes.Patent ? "Patent Renewal" : "Trademark Renewal",
            payType = PaymentTypes.Update,
            FileId = file.FileId,
            Date = paymentResponse?.paymentDate ?? "-"
        };

        byte[] data;
        if (file.Type == FileTypes.TradeMark)
        {
            data = new TrademarkRenewalReceipt(file, appId).GeneratePdf();
        }
        else if (file.Type == FileTypes.Patent)
        {
            data = new PatentRenewalReceipt(receipt, "uri", file).GeneratePdf();
        }
        else
        {
            throw new NotSupportedException("Renewal receipt not supported for this file type.");
        }
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> RenewalAcknowledgment(Filling file, string applicationId, DateTime paydate)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        var app = file.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
        if (app == null)
            throw new Exception("Application history not found for provided ID");
        // Get payment details using GetPaymentData (just like DataUpdateReceipt)
        var paymentResponse = await GetPaymentData(file.Comment, app.PaymentId);

        var receipt = new Receipt
        {
            Amount = paymentResponse?.amount?.ToString() ?? "",
            ApplicantName = file.applicants.Count > 1
                ? file.applicants[0]?.Name + " et al."
                : file.applicants.Count == 1 ? file.applicants[0].Name : "",
            rrr = paymentResponse?.rrr ?? "-",
            PaymentFor = file.Type == FileTypes.Patent ? "Patent Renewal" : "Trademark Renewal",
            payType = PaymentTypes.Update,
            FileId = file.FileId,
            Date = paymentResponse?.paymentDate ?? "-"
        };

        byte[] data;
        if (file.Type == FileTypes.TradeMark)
        {
            byte[] imageData = [];
            if (file.Attachments.FirstOrDefault(x => x.name == "representation") != null)
            {
                try
                {
                    imageData = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                        .First(x => x.name == "representation").url[0]);
                }
                catch (Exception)
                {
                    imageData = null;
                }
            }
            data = new RenewalAck(file, imageData, applicationId, paydate).GeneratePdf();
        }
        else if (file.Type == FileTypes.Patent)
        {
            data = new PatentRenewalAcknowlegementLetter(file, applicationId, receipt).GeneratePdf();
        }
        else
        {
            throw new NotSupportedException("Renewal acknowledgment not supported for this file type.");
        }
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string,object>> RenewalCertificate(Filling fileData, string applicationId, DateTime date)
    {
        if (fileData == null)
            throw new ArgumentNullException(nameof(fileData), "File data cannot be null");

        var app = fileData.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
        if (app == null)
            throw new Exception("Application history not found for provided ID");
        // Get payment details using GetPaymentData (just like DataUpdateReceipt)
        var paymentResponse = await GetPaymentData(fileData.Comment, app.PaymentId);

        var receipt = new Receipt
        {
            Amount = paymentResponse?.amount?.ToString() ?? "",
            ApplicantName = fileData.applicants.Count > 1
                ? fileData.applicants[0]?.Name + " et al."
                : fileData.applicants.Count == 1 ? fileData.applicants[0].Name : "",
            rrr = paymentResponse?.rrr ?? "-",
            PaymentFor = fileData.Type == FileTypes.Patent ? "Patent Renewal" : "Trademark Renewal",
            payType = PaymentTypes.Update,
            FileId = fileData.FileId,
            Date = paymentResponse?.paymentDate ?? "-"
        };

        byte[] data;
        if (fileData.Type == FileTypes.TradeMark)
        {
            byte[] image = [];
            try
            {
                if ((fileData.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) ||
                    fileData.Attachments.FirstOrDefault(e => e.name == "representation") != null)
                {
                    image = await (new HttpClient()).GetByteArrayAsync(fileData.Attachments
                        .First(r => r.name == "representation").url[0]);
                }
            }
            catch (Exception)
            {
                image = [];
            }
            data = new TrademarkRenewalCertificate(fileData, $"https://portal.iponigeria.com/qr?fileId={fileData.FileId}",applicationId, image, date).GeneratePdf();
        }
        else if (fileData.Type == FileTypes.Patent)
        {
            data = new PatentRenewalCertificate(fileData, $"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", receipt).GeneratePdf();
        }
        else
        {
            throw new NotSupportedException("Renewal certificate not supported for this file type.");
        }
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> OtherReceipt(Receipt data, Filling fileData)
    {
        var bytes = new ReceiptModel(data, "uri", fileData).GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string, object>> DataUpdateReceipt(string fileId, string applicationId, Filling fileData)
    {
        var data = _fillingCollection.Find(x => x.Id == fileId).FirstOrDefault();
        var response=await GetPaymentData(data.Comment,
            data.ApplicationHistory.First(x => x.id == applicationId).PaymentId);
        var bytes = new ReceiptModel(new Receipt()
        {
            Amount = response.amount.ToString(),
            ApplicantName = data.applicants.Count > 1 ? data.applicants[0]?.Name + " et al.": data.applicants.Count==1? data.applicants[0].Name:"",
            rrr = response.rrr,
            PaymentFor = "Recordal Data update",
            payType = PaymentTypes.Update,
            FileId = data.FileId,
            Date = response.paymentDate,
        }, "uri", fileData).GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string,object>> RecordalAcknowledgment(Filling file, string applicationId, Receipt receipt)
    {
        //var field = fileData.Revisions.FirstOrDefault(r => r.currentStatus == ApplicationStatuses.Active);
        var data=new RecordalAck(receipt, "", file, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> MergerAck(Filling fileData, string applicationId)
    {
        if (fileData == null)
            throw new ArgumentNullException(nameof(fileData), "File data cannot be null");

        if (fileData.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(fileData.ApplicationHistory), "Application history cannot be null");

        if (fileData.Attachments == null)
            throw new ArgumentNullException(nameof(fileData.Attachments), "Attachments cannot be null");

        byte[] images = [];
        var representation = fileData.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((fileData.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(fileData.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }

        var data = new mergerAck(fileData, images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> MergerReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        var data = new MergerReceipt(file, appId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> RegisteredUsersAck(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");

        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }

        var data = new RegisteredUsersAck(file, images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> ChangeOfNameAck(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");
        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            images = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
        }
        var data = new ChangeOfNameAck(file, images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> ChangeOfAddressAck(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");
        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }
        var data = new ChangeOfAddressAck(file, images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> RegisteredUsersReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        var data = new RegUsersReceipt(file, appId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> ChangeOfNameReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        var data = new ChangeOfNameReceipt(file, appId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> ChangeOfAddressReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        var data = new ChangeOfAddressReceipt(file, appId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> RecordalCert(Filling fileData, string applicationId)
    {
        List<byte[]> images = [];
        // Check if fileData is null
        if (fileData == null)
        {
            throw new ArgumentNullException(nameof(fileData), "File data cannot be null");
        }
        // Generate the certificate using the fileData
        byte[] image = [];
        try
        {
            if ((fileData.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) ||
                fileData.Attachments.FirstOrDefault(e => e.name == "representation") != null)
            {
                image = await (new HttpClient()).GetByteArrayAsync(fileData.Attachments
                    .First(r => r.name == "representation").url[0]);
            }
        }
        catch (Exception)
        {
            image = [];
        }
        var data = new RecordalCertificate(fileData, image, applicationId).GeneratePdf();

        return ReturnDocument(data);
    }
    
    public async Task<Dictionary<string,object>> AssignmentReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        var data = new AssignmentReceipt(file, appId).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> ClericalUpdateReceipt(Filling file, string appId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        var data = new ClericalUpdateReceipt(file, appId).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> ClericalUpdateAck(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");

        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            images = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
        }

        var data = new ClericalUpdateAck(file, images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string,object>> AssignmentAcknowledgement(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");

        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");

        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }

        var data = new AssignmentAcknowledgement(file, images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    
    public async Task<Dictionary<string,object>> AssignmentRejection(string fileId,  string applicationId)
    {
        var assignmentApp=_fillingCollection.Find(x => x.Id == fileId)
            .Project(x => new
            {
                history=x.ApplicationHistory.FirstOrDefault(y => y.id == applicationId),
                fileID = x.FileId,
                type=x.Type,
                title= x.TitleOfDesign??x.TitleOfInvention??x.TitleOfTradeMark,
                name = x.applicants.Count>1? x.applicants[0].Name:x.applicants[0].Name+" et al.",
                corr = x.Correspondence
            }).FirstOrDefault();
        var response=await _remitaPaymentUtils.GetDetailsByRRR(assignmentApp.history.PaymentId);
        var userId = assignmentApp.history.StatusHistory
            .FirstOrDefault(x => x.afterStatus == ApplicationStatuses.Approved).UserId;
        var signatureUrl=_usersCollection.Find(x => x.id == userId).Project(t => t.Signature).FirstOrDefault();
        var signature =
            await (new HttpClient()).GetByteArrayAsync(signatureUrl);
        var data=new AssignmentRejection(new AssignmentCertificateType()
        {
            applicantName = assignmentApp.name,
            fileNumber = assignmentApp.fileID,
            assignmentType = assignmentApp.history.Assignment,
            paymentDate = DateTime.Parse(response.paymentDate),
            CorrespondenceType = assignmentApp.corr,
            examinerName = assignmentApp.history.StatusHistory.FirstOrDefault(x=>x.afterStatus==ApplicationStatuses.Approved).User,
            examinerSignature = signature
        }, assignmentApp.history.StatusHistory.FirstOrDefault(x=>x.afterStatus==ApplicationStatuses.Rejected).Message).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> NewAssignmentCertificate(string fileId, string applicationId)
    {
        var assignmentApp = _fillingCollection.Find(x => x.Id == fileId)
            .Project(x => new
            {
                history = x.ApplicationHistory.FirstOrDefault(y => y.id == applicationId),
                fileID = x.FileId,
                type = x.Type,
                title = x.TitleOfDesign ?? x.TitleOfInvention ?? x.TitleOfTradeMark,
                name = x.applicants.Count > 1 ? x.applicants[0].Name : x.applicants[0].Name + " et al.",
                corr = x.Correspondence
            }).FirstOrDefault();
        
        var response = await _remitaPaymentUtils.GetDetailsByRRR(assignmentApp.history.PaymentId);
        var userId = assignmentApp.history.StatusHistory
            .FirstOrDefault(x => x.afterStatus == ApplicationStatuses.Approved).UserId;
        var signatureUrl = _usersCollection.Find(x => x.id == userId).Project(t => t.Signature).FirstOrDefault();
        var signature =
            await (new HttpClient()).GetByteArrayAsync(signatureUrl);
        var data = new AssignmentCertificate(new AssignmentCertificateType()
        {
            applicantName = assignmentApp.name,
            fileNumber = assignmentApp.fileID,
            assignmentType = assignmentApp.history.Assignment,
            paymentDate = DateTime.Parse(response.paymentDate),
            CorrespondenceType = assignmentApp.corr,
            examinerName = assignmentApp.history.StatusHistory
                .FirstOrDefault(x => x.afterStatus == ApplicationStatuses.Approved).User,
            examinerSignature = signature
        }, assignmentApp.type).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> OppositionReceipt(string oppositionId)
    {
        var opposition = _oppositionCollection.Find(x => x.Id == oppositionId).FirstOrDefault();
        var response=await GetPaymentData(null, opposition.creationPaymentID);
        var bytes = new OppositionReceipt(new OppositionReceiptType()
        {
            amount = response.amount.ToString(),
            paymentId = response.rrr,
            name = opposition.name,
            description = opposition.title,
            date = DateTime.Parse(response.paymentDate)
        }, "uri").GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string, object>> OppositionAcknowledgement(string oppositionId)
    {
        var opposition = _oppositionCollection.Find(x => x.Id == oppositionId).FirstOrDefault();
        var response=await GetPaymentData(null, opposition.creationPaymentID);
        var bytes = new OppositionAcknowledgement(new OppositionAckType()
        {
            address = opposition.address,
            email = opposition.email,
            number = opposition.number,
            paymentId = response.rrr,
            name = opposition.name,
            description = opposition.title,
            date = DateTime.Parse(response.paymentDate)
        }, "uri").GeneratePdf();
        return ReturnDocument(bytes);
    }
    
    public async Task<Dictionary<string, object>> OppositionResponseReceipt(string oppositionId)
    {
        var opposition = _oppositionCollection.Find(x => x.Id == oppositionId).FirstOrDefault();
        var response=await GetPaymentData(null, opposition.responsePaymentId);
        var bytes = new OppositionReceipt(new OppositionReceiptType()
        {
            amount = response?.amount?.ToString()??"",
            paymentId = response?.rrr??"",
            name = opposition.name??"",
            description = $"Counter statement on opposition regarding {opposition.title}",
            date = response?.paymentDate!=null?DateTime.Parse(response.paymentDate): DateTime.Now,
        }, "uri").GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string, object>> OppositionResponseAck(string oppositionId)
    {
        var opposition = _oppositionCollection.Find(x => x.Id == oppositionId).FirstOrDefault();
        var response=await GetPaymentData(null, opposition.responsePaymentId);

        var bytes = new OppositionAcknowledgement(new OppositionAckType()
        {
            address = opposition.address,
            email = opposition.email,
            number = opposition.number,
            paymentId = response?.rrr??"",
            name = opposition.name,
            description = $"Counter statement on opposition regarding {opposition.title}",
            date = response?.paymentDate!=null?DateTime.Parse(response.paymentDate): DateTime.Now,
        }, "uri").GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string, object>> OppositionResolutionReceipt(string oppositionId)
    {
        var opposition = _oppositionCollection.Find(x => x.Id == oppositionId).FirstOrDefault();
        var response=await GetPaymentData(null, opposition.resolutionpaymentId);
        var bytes = new OppositionReceipt(new OppositionReceiptType()
        {
            amount = response?.amount?.ToString()??"",
            paymentId = response?.rrr??"",
            name = opposition?.name??"",
            description = $"Resolution statement regarding {opposition?.title??""}",
            date = response?.paymentDate!=null?DateTime.Parse(response.paymentDate): DateTime.Now,
        }, "uri").GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string, object>> OppositionResolutionAck(string oppositionId)
    {
        var opposition = _oppositionCollection.Find(x => x.Id == oppositionId).FirstOrDefault();
        var response = await GetPaymentData(null, opposition.resolutionpaymentId);
        var bytes = new OppositionAcknowledgement(new OppositionAckType()
        {
            address = opposition.address,
            email = opposition.email,
            number = opposition.number,
            paymentId = response?.rrr ?? "",
            name = opposition.name,
            description = $"Resolution statement regarding {opposition.title}",
            date = response?.paymentDate != null ? DateTime.Parse(response.paymentDate) : DateTime.Now,
        }, "uri").GeneratePdf();
        return ReturnDocument(bytes);
    }

    public async Task<Dictionary<string, object>> RegisteredUserCert(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");
        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }
        var data = new RegisteredUserCert(file,"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> MergerCertificate(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");
        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }
        var data = new MergerCert(file,"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> AssignmentCertificate(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");
        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }
        var data = new AssignmentCert(file,"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> ChangeOfNameCertificate(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");
        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }
        var data = new ChangeOfNameCert(file,"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }
    public async Task<Dictionary<string, object>> ChangeOfAddressCertificate(Filling file, string applicationId)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        if (file.ApplicationHistory == null)
            throw new ArgumentNullException(nameof(file.ApplicationHistory), "Application history cannot be null");
        if (file.Attachments == null)
            throw new ArgumentNullException(nameof(file.Attachments), "Attachments cannot be null");
        byte[] images = [];
        var representation = file.Attachments.FirstOrDefault(e => e.name == "representation");
        if ((file.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
            representation != null && representation.url?[0] != "NULL")
        {
            try
            {
                images = await (new HttpClient()).GetByteArrayAsync(file.Attachments
                    .First(x => x.name == "representation").url[0]);
            }
            catch (Exception)
            {
                images = null;
            }
        }
        var data = new ChangeOfAddressCert(file,"https://portal.iponigeria.com/qr?fileId={fileData.FileId}", images, applicationId).GeneratePdf();
        return ReturnDocument(data);
    }


    public Dictionary<string, object> ReturnDocument(byte[] data)
    {
        var trustedFileName = Path.GetRandomFileName();
        trustedFileName=trustedFileName.Split(".")[0] + $".pdf";
        return new Dictionary<string, object>()
        {
            ["data"]=data,
            ["type"]= "application/pdf",
            ["name"]=trustedFileName
        };
    }

    private record PaymentInfo
    {
        public string? rrr { get; set; }
        public string? paymentDate { get; set; }
        public double? amount { get; set; }
        public string? status { get; set; }
    }

    private async Task<PaymentInfo?> GetPaymentData(string? comment, string? paymentId)
    {
        if (comment is "migrated_trademarks" or "migrated_old_patent" or "migrated_old_design"
            or "migrated_designs" or "migrated_patents")
        {
            var found = _migratedFinanceCollection.Find(x => x.OrderId == paymentId || x.Rrr == paymentId)
                .FirstOrDefault();
            if (found != null)
            {
                return new PaymentInfo()
                {
                    rrr = paymentId,
                    amount = double.Parse(found.Amount.ToString()),
                    paymentDate = found.PaymentDate.ToString(),
                };
            }

            if (found == null)
            {
                // try getting via order id or remita rrr, start by checking payment also
                var second_trial = _financeCollection.Find(x => x.remitaResonse.rrr == paymentId)
                    .Project(d => new { d.total, d.remitaResonse.paymentDate, paymentId, d.remitaResonse.status }).FirstOrDefault();
                if (second_trial != null)
                {
                    return new PaymentInfo()
                    {
                        rrr = paymentId,
                        amount = second_trial.total,
                        paymentDate = second_trial.paymentDate,
                        status = second_trial.status
                    };
                }

                if (second_trial == null)
                {
                    if (paymentId.Contains("IPO"))
                    {
                        var response = await _remitaPaymentUtils.GetDetailsByOrderId(paymentId);
                        if (response != null)
                        {
                            return new PaymentInfo()
                            {
                                rrr = paymentId,
                                amount = response.amount,
                                paymentDate = response.paymentDate,
                                status = response.status
                            };
                        }

                    }
                    else
                    {
                        var rrrResponse = await _remitaPaymentUtils.GetDetailsByRRR(paymentId);
                        if (rrrResponse != null)
                        {
                            return new PaymentInfo()
                            {
                                rrr = paymentId,
                                amount = rrrResponse.amount,
                                paymentDate = rrrResponse.paymentDate,
                                status = rrrResponse.status
                            };
                        }
                    }
                }
            }

            return null;
        }

        else

        {

            var historyData = _financeCollection.Find(x => x.remitaResonse.rrr == paymentId)
                .Project(d => new { d.total, d.remitaResonse.paymentDate, paymentId, d.remitaResonse.status }).FirstOrDefault();
            if (historyData != null)
            {
                return new PaymentInfo()
                {
                    rrr = paymentId,
                    amount = historyData.total,
                    paymentDate=historyData.paymentDate,
                    status = historyData.status
                };
            }

            if (historyData == null)
            {
                if (paymentId.Contains("IPO"))
                {
                    var response = await _remitaPaymentUtils.GetDetailsByOrderId(paymentId);
                    if (response != null)
                    {
                        return new PaymentInfo()
                        {
                            rrr = paymentId,
                            amount = response.amount,
                            paymentDate = response.paymentDate,
                            status = response.status
                        };
                    }
                }
                else
                {
                    var rrrResponse = await _remitaPaymentUtils.GetDetailsByRRR(paymentId);
                    if (rrrResponse != null)
                    {
                        return new PaymentInfo()
                        {
                            rrr = paymentId,
                            amount = rrrResponse.amount,
                            paymentDate = rrrResponse.paymentDate,
                            status = rrrResponse.status
                        };
                    }
                }
            }
        }

        return null;
    }

    public async Task<TrademarkDocsDto> VerifyTmDoc(string fileId)
    {
        try
        {
            var file = await _fillingCollection.Find(x => x.FileId == fileId).FirstOrDefaultAsync();
            if (file == null) throw new Exception("File not found");
            string title;
            if (file.Type == FileTypes.Patent)
            {
                title = file.TitleOfInvention;
            }
            else if (file.Type == FileTypes.Design)
            {
                title = file.TitleOfDesign;
            }
            else
            {
                title = file.TitleOfTradeMark;
            }
            var data = new TrademarkDocsDto
            {
                FileId = file.FileId,
                Title = title,
                FileStatus = file.FileStatus,
                FilingDate = file.FilingDate ?? file.DateCreated
            };
            return data;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<Dictionary<string, object>> WithdrawalRequestAcknowledgement(Filling file, string applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        byte[] image = [];
        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);
        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");
        var representation = file.Attachments?
            .FirstOrDefault(e => e.name == "representation" && e.url != null && e.url.Count > 0);
        if (representation != null &&
            !string.IsNullOrWhiteSpace(representation.url[0]) &&
            !representation.url[0].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                image = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
            }
            catch
            {
                image = [];
            }
        }
        var data = new WithdrawalRequestAcknowledgement(file, image, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> WithdrawalRequestApproval(Filling file, string applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        byte[] image = [];
        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);
        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");
        var representation = file.Attachments?
            .FirstOrDefault(e => e.name == "representation" && e.url != null && e.url.Count > 0);
        if (representation != null &&
            !string.IsNullOrWhiteSpace(representation.url[0]) &&
            !representation.url[0].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                image = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
            }
            catch
            {
                image = [];
            }
        }
        var data = new WithdrawalRequestApproval(file, image, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> WithdrawalRequestRefusal(Filling file, string applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");
        byte[] image = [];
        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);
        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");
        var representation = file.Attachments?
            .FirstOrDefault(e => e.name == "representation" && e.url != null && e.url.Count > 0);
        if (representation != null &&
            !string.IsNullOrWhiteSpace(representation.url[0]) &&
            !representation.url[0].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                image = await (new HttpClient()).GetByteArrayAsync(representation.url[0]);
            }
            catch
            {
                image = [];
            }
        }
        var data = new WithdrawalRequestRefusal(file, image, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }

    public async Task<Dictionary<string, object>> WithdrawalRequestReceipt(Filling file, string applicationId = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file), "File data cannot be null");

        // Get the specific ApplicationHistory by ID
        var selectedHistory = file.ApplicationHistory?
            .FirstOrDefault(h => h.id == applicationId);

        if (selectedHistory == null)
            throw new Exception("Application history not found for provided ID");

        // Pass both file and selectedHistory to the PDF generator
        var data = new WithdrawalRequestReceipt(file, selectedHistory).GeneratePdf();
        return ReturnDocument(data);
    }
}
