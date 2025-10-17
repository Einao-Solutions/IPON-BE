using System.Security.Authentication;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Response;
using patentdesign.Models;
using patentdesign.Utils;
namespace patentdesign.Services;
public class MigrationService
{
    private static IMongoCollection<Filling> _fillingCollection;
    private static IMongoCollection<MarkInfo> _markInfoCollection;
    private static IMongoCollection<Pwallet> _pwalletCollection;
    private static IMongoCollection<ClaimRequests> _claimRequestsCollection;
    private static IMongoCollection<CldxApplicants> _cldxApplicantsCollection;
    private static IMongoCollection<CldxAddresses> _cldxAddressesCollection;
    private static IMongoCollection<IpoNgMarkInformations> _ipoNgMarkInformationsCollection;
    private static IMongoCollection<IpoNgApplication> _ipoNgApplicationCollection;
    private static IMongoCollection<XpayApplicant> _xpayApplicantCollection;
    private static IMongoCollection<XpayTwallet> _xpayTwalletCollection;
    private MongoClient _mongoClient;
    private FileServices _fileServices;
    private PaymentService _paymentService;
    private PaymentUtils _paymentUtils;
    
    public MigrationService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, FileServices fileServices, PaymentService paymentService, PaymentUtils paymentUtils)
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
        _markInfoCollection = pdDb.GetCollection<MarkInfo>("cldxMarkinfo");
        _pwalletCollection = pdDb.GetCollection<Pwallet>("cldxPwallet");
        _fillingCollection = pdDb.GetCollection<Filling>("files");
        _claimRequestsCollection = pdDb.GetCollection<ClaimRequests>("claimRequests");
       _cldxAddressesCollection = pdDb.GetCollection<CldxAddresses>("cldxApplicantAddresses");
       _cldxApplicantsCollection =  pdDb.GetCollection<CldxApplicants>("cldxApplicants");
       _ipoNgMarkInformationsCollection = pdDb.GetCollection<IpoNgMarkInformations>("ipongMarkInformations");
       _ipoNgApplicationCollection = pdDb.GetCollection<IpoNgApplication>("ipongApplications");
       _xpayApplicantCollection = pdDb.GetCollection<XpayApplicant>("xpayApplicants");
       _xpayTwalletCollection = pdDb.GetCollection<XpayTwallet>("xpayTwallet");
       _fileServices = fileServices;
       _paymentService = paymentService;
       _paymentUtils = paymentUtils;
    }
    public async Task<List<MarkInfoDto>> GetFileByRegNumber(string regNumber)
    {
        if (string.IsNullOrWhiteSpace(regNumber))
            throw new Exception("Registration number is required");
        try
        {
            var file = await _fillingCollection.Find(f=>f.FileId == regNumber).FirstOrDefaultAsync();
            if (file != null) throw new Exception("File already exists on current system");
            // Try IPO Nigeria first
            var ipoResult = await GetFileFromIpoNigeria(regNumber);
            if (ipoResult != null && ipoResult.Count > 0)
            {
                return ipoResult;
            }
            // If not found, fallback to local cldxMarkinfo
            var localResult = await GetFromCldxMarkinfo(regNumber);
            if (localResult != null && localResult.Count > 0)
            {
                return localResult;
            }
            throw new Exception("File not found");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    private async Task<List<MarkInfoDto>> GetFileFromIpoNigeria(string fileNumber)
        {
            var files = new List<MarkInfoDto>();
            if (string.IsNullOrWhiteSpace(fileNumber))
                return null;
            try
            {
                Console.WriteLine("Searching iponigeria");
                var exFile  = await _fillingCollection.Find(x=>x.FileId == fileNumber).FirstOrDefaultAsync();
                if (exFile != null) throw new Exception("File Exists on Current System");
                var mark =  await _ipoNgMarkInformationsCollection.Find(m => m.RegistrationNumber == fileNumber).FirstOrDefaultAsync();
                if (mark == null) return null;
                var app = await _ipoNgApplicationCollection.Find(a=>a.Id == mark.applicationid).FirstOrDefaultAsync();
                if  (app == null) throw new Exception("Application Not Found");
                TradeMarkType markType;
                if (mark.TradeMarkTypeID == 2)
                {
                    markType = TradeMarkType.Foreign;
                }
                else
                {
                    markType = TradeMarkType.Local;
                }

                TradeMarkLogo logo;
                if (mark.logo_descriptionID == 1)
                {
                    logo = TradeMarkLogo.Device;
                }else if (mark.logo_descriptionID == 2)
                {
                    logo = TradeMarkLogo.WordMark;
                }
                else
                {
                    logo = TradeMarkLogo.WordandDevice;
                }
                int countryId = Convert.ToInt32(mark.ApplicantNationality);
                CldxCountry country = (CldxCountry)countryId;
                var applicant = new ApplicantInfo
                {
                    Name = mark.ApplicantName,
                    Email = mark.ApplicantEmail,
                    Phone = mark.ApplicantPhone.ToString(),
                    Address = mark.ApplicantAddress,
                    country = country.ToString()
                };
                var applicants = new List<ApplicantInfo>();
                var apps = new List<ApplicationInfo>();
                applicants.Add(applicant);
                ApplicationStatuses status = ApplicationStatuses.None;
                switch (app.DataStatus)
                {
                    case "Certificate":
                        status = ApplicationStatuses.AwaitingCertificateConfirmation;
                        break;
                    case "Examiner":
                        status = ApplicationStatuses.AwaitingExaminer;
                        break;
                    case "Refused":
                        status = ApplicationStatuses.Rejected;
                        break;
                    case "Registered":
                        status = ApplicationStatuses.Active;
                        break;
                    case "Opposed":
                        status = ApplicationStatuses.Opposition;
                        break;
                    case "Publication" or "Accepted":
                        status = ApplicationStatuses.Publication;
                        break;
                    case "Search":
                        status = ApplicationStatuses.AwaitingSearch;
                        break;
                    case "Fresh" or "New":
                        status = ApplicationStatuses.AwaitingPayment;
                        break;
                    case "Reconduct-Search" or "kiv":
                        status = ApplicationStatuses.Re_conduct;
                        break;
                    case "Not Opposed":
                        status = ApplicationStatuses.AwaitingCertification;
                        break;
                    default:
                        status = ApplicationStatuses.None;
                        break;
                }
                var corr = new CorrespondenceType
                {
                    name = mark.AttorneyName,
                    email = mark.AttorneyEmail,
                    Nationality = mark.AttorneyCountry,
                    address = mark.ApplicantAddress
                };
                var newApp = new ApplicationInfo
                {
                    ApplicationType = FormApplicationTypes.NewApplication,
                    CurrentStatus = status,
                    PaymentId = app.TransactionID,
                    CertificatePaymentId = app.CertificatePayReference,
                    ApplicationDate = DateTime.Parse(app.DateCreated),
                };
                apps.Add(newApp);
                var file = new MarkInfoDto
                {
                    FileNumber = mark.RegistrationNumber,
                    Title = mark.ProductTitle,
                    Class = mark.NiceClass.ToString(),
                    MarkType = markType,
                    FilingDate = mark.DateCreated,
                    Logo = logo,
                    Description = mark.NiceClassDescription,
                    Applicants = applicants,
                    Correspondence = corr,
                    Disclaimer = mark.Claimsanddisclaimer,
                    FileStatus = status,
                    ApplicationHistory = apps
                };
                files.Add(file);
                return files;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    private async Task<List<MarkInfoDto>> GetFromCldxMarkinfo(string regNumber)
    {
        var files = new List<MarkInfoDto>();
        if (string.IsNullOrWhiteSpace(regNumber))
            return null;
        try
        {
            Console.WriteLine("Searching cldx");
            var mark =  await _markInfoCollection.Find(m => m.reg_number.Equals(regNumber)).FirstOrDefaultAsync();
            if (mark == null) throw new Exception("File Not Found");
            TradeMarkType markType;
            if (mark.tm_typeID == "2")
            {
                markType = TradeMarkType.Foreign;
            }
            else
            {
                markType = TradeMarkType.Local;
            }
    
            TradeMarkLogo logo;
            if (mark.logo_descriptionID == "1")
            {
                logo = TradeMarkLogo.Device;
            }else if (mark.logo_descriptionID == "2")
            {
                logo = TradeMarkLogo.WordMark;
            }
            else
            {
                logo = TradeMarkLogo.WordandDevice;
            }
            
            
            var file = new MarkInfoDto
            {
                FileNumber = mark.reg_number,
                Title = mark.product_title,
                FilingDate = mark.reg_date,
                Class = mark.nice_class,
                MarkType = markType,
                Logo = logo,
                Description = mark.nice_class_desc
            };
            files.Add(file);
            return files;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    public async Task<MarkInfoDto> GetPayment(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new Exception("Please provide a payment ID");
        try
        {
            Console.WriteLine("Searching Pwallet");
            string searchId = paymentId.Contains('-') ? paymentId.Split('-')[0] : paymentId;
            var payment = await _pwalletCollection.Find(p => p.TransactionId == searchId).FirstOrDefaultAsync();
            if (payment == null)
            {
                Console.WriteLine("Payment Not Found in Pwallet");
            }
            else
            {
                Console.WriteLine(payment);
            }
            
            var paid = await _paymentService.CheckPayment(paymentId);
            XpayApplicant applicant = null;
            XpayTwallet xpay = null;
            
            if (paid == null)
            {
                Console.WriteLine("Not a remita id");
                xpay = await _xpayTwalletCollection.Find(x => x.transID == searchId).FirstOrDefaultAsync();
                if (xpay == null) throw new Exception("Payment Not Found");
                
                string appId = xpay.applicantID.ToString() ?? "";
                applicant = await _xpayApplicantCollection.Find(a => a.xid == appId).FirstOrDefaultAsync();
                if (applicant == null) throw new Exception("Applicant Not Found");
            }
           
            ApplicationStatuses status = ApplicationStatuses.None;
            switch (payment?.data_status)
            {
                case "Certified":
                    status = ApplicationStatuses.AwaitingCertificateConfirmation;
                    break;
                case "Refused":
                    status = ApplicationStatuses.Rejected;
                    break;
                case "Registered":
                    status = ApplicationStatuses.Active;
                    break;
                case "Opposed":
                    status = ApplicationStatuses.Opposition;
                    break;
                case "Published" or "Accepted":
                    status = ApplicationStatuses.Publication;
                    break;
                case "Valid":
                    status = ApplicationStatuses.AwaitingSearch;
                    break;
                case "Fresh" or "New":
                    status = ApplicationStatuses.AwaitingPayment;
                    break;
                case "Re-examine" or "kiv":
                    status = ApplicationStatuses.Re_conduct;
                    break;
                case "Not Opposed":
                    status = ApplicationStatuses.AwaitingCertification;
                    break;
                default:
                    status = ApplicationStatuses.AwaitingSearch;
                    break;
            }
            
            var applicants = new List<ApplicantInfo>();
            var app = new ApplicantInfo
            {
                Name = paid?.payerName ?? applicant?.xname,
                Email = paid?.payerEmail ?? applicant?.xemail,
                Address = applicant?.address,
                Phone = paid?.payerPhoneNumber ?? applicant?.xmobile,
            };
            applicants.Add(app);
            
            var history = new List<ApplicationInfo>();
            var altDate = !string.IsNullOrEmpty(paid?.paymentDate) 
                ? DateTime.Parse(paid.paymentDate) 
                : DateTime.Now;

            var newApp = new ApplicationInfo
            {
                PaymentId = paid?.rrr ?? paymentId,
                ApplicationDate = xpay?.xreg_date ?? altDate,
                ApplicationType = FormApplicationTypes.NewApplication,
                CurrentStatus = status 
            };
            history.Add(newApp);
            
            var details = new MarkInfoDto
            {
                FilingDate = xpay?.xreg_date.ToString() ?? altDate.ToString(),
                FileStatus = status,
                Applicants = applicants,
                ApplicationHistory = history
            };
            return details;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    public async Task<bool> NewClaimRequest(ClaimRequestDto req)
        {
            try
            {
                Console.WriteLine("Submitting new claim request");
                if (req.Attachments == null || req.Attachments.Count == 0) throw new Exception("Documents are required");
                var claimDocs = new List<string>();
                foreach (var (doc, i) in req.Attachments.Select((doc, idx) => (doc, idx)))
                {
                    using var ms = new MemoryStream();
                    await doc.CopyToAsync(ms);
                
                    var appealDoc = ms.ToArray();
                    var url = await _fileServices.UploadAttachment(new List<TT>
                    {
                        new TT
                        {
                            contentType = doc.ContentType,
                            data = appealDoc,
                            fileName = Path.GetFileName(doc.FileName),
                            Name = $"Claim Document {i + 1}"
                        }
                    });
                
                    claimDocs.Add(url[0]);
                }
                var mark = req.MarkInfo?.FirstOrDefault();
                if (mark == null)
                    throw new Exception("Mark information is required to submit a claim request.");
                CorrespondenceType corr = new CorrespondenceType
                {
                    Nationality = req.CorrespondenceNationality,
                    name = req.CorrespondenceName,
                    email = req.CorrespondenceEmail,
                    address = req.CorrespondenceAddress,
                    phone = req.CorrespondencePhone
                };
                
                Console.WriteLine("working on mark");
                ClaimRequests data = new ClaimRequests
                {
                    Id = Guid.NewGuid().ToString(),
                    FileId = mark?.FileNumber ?? throw new Exception("FileNumber is required"),
                    FileStatus = mark.FileStatus,
                    FilingDate = string.IsNullOrEmpty(mark.FilingDate) 
                        ? throw new Exception("FilingDate is required") 
                        : DateTime.Parse(mark.FilingDate),
                    TrademarkType = mark.MarkType,
                    TitleOfTradeMark = mark.Title,
                    Type = FileTypes.TradeMark,
                    TrademarkClass = int.TryParse(mark.Class, out var cls) ? cls : throw new Exception("Invalid class"),
                    TrademarkClassDescription = mark.Description,
                    TrademarkLogo = mark.Logo,
                    DateCreated = DateTime.Now,
                    ApplicationHistory = mark.ApplicationHistory,
                    applicants = mark.Applicants,
                    Comment = "claim request",
                    ClaimDocuments = claimDocs,
                    Correspondence = corr
                };

                await _claimRequestsCollection.InsertOneAsync(data);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    public async Task<List<ClaimDetailsDto>> GetAllClaimRequests()
    {
        try
        {
            var claimRequests = await _claimRequestsCollection
                .Find(c => c.IsMigrated == false)
                .ToListAsync();

            var result = claimRequests.Select(c => new ClaimDetailsDto()
            {
                FileNumber = c.FileId,
                FilingDate = c.FilingDate,
                Class = c.TrademarkClass,
                FileStatus = c.FileStatus,
                Title = c.TitleOfTradeMark,
                PaymentId = c.ApplicationHistory?[0].PaymentId,
                RequestDate = c.DateCreated,
            }).ToList();

            return result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    public async Task<ClaimDetailsDto> GetClaimRequest(string fileId)
    {
        try
        {
            Console.WriteLine("Getting claim request...");
            var claimRequest = await _claimRequestsCollection
                .Find(c => c.FileId == fileId)
                .FirstOrDefaultAsync();
            if (claimRequest == null) throw new Exception("No Claim Found");
            Console.WriteLine(claimRequest);
            ClaimDetailsDto result = new ClaimDetailsDto
            {
                FileNumber = claimRequest.FileId,
                FileStatus = claimRequest.FileStatus,
                Class = claimRequest.TrademarkClass,
                RequestDate = claimRequest.DateCreated,
                FilingDate = claimRequest.FilingDate,
                Title = claimRequest.TitleOfTradeMark,
                PaymentId = claimRequest.ApplicationHistory?[0].PaymentId,
                Documents = claimRequest.ClaimDocuments
            };
            return result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    public async Task<bool> MigrateFile(string fileId)
    {
        try
        {
            var claim = await _claimRequestsCollection.Find(c => c.FileId == fileId).FirstOrDefaultAsync();
            if (claim == null) throw new Exception("No Claim Found");

            var file = new Filling
            {
                Id = claim.Id,
                FileId = claim.FileId ?? "",
                LastRequestDate = claim.LastRequestDate ?? DateTime.Now,
                CreatorAccount = claim.CreatorAccount ?? "",
                FileStatus = claim.FileStatus ?? ApplicationStatuses.None,
                DateCreated = claim.DateCreated ?? DateTime.Now,
                Type = claim.Type ?? FileTypes.TradeMark,
                FilingCountry = claim.FilingCountry ?? string.Empty,
                FileOrigin = claim.FileOrigin,
                TitleOfInvention = claim.TitleOfInvention,
                PatentAbstract = claim.PatentAbstract ?? "",
                Correspondence = claim.Correspondence,
                LastRequest = claim.LastRequest ?? DateTime.Now,
                applicants = claim.applicants ?? new List<ApplicantInfo>(),
                PatentApplicationType = claim.PatentApplicationType,
                Revisions = claim.Revisions ?? new List<Revision>(),
                PatentType = claim.PatentType,
                PatentBaseTypes = claim.PatentBaseTypes,
                Inventors = claim.Inventors ?? new List<ApplicantInfo>(),
                PriorityInfo = claim.PriorityInfo ?? new List<PriorityInfo>(),
                FirstPriorityInfo = claim.FirstPriorityInfo ?? new List<PriorityInfo>(),
                DesignType = claim.DesignType,
                TitleOfDesign = claim.TitleOfDesign,
                StatementOfNovelty = claim.StatementOfNovelty ?? "",
                DesignCreators = claim.DesignCreators ?? new List<ApplicantInfo>(),
                Attachments = claim.Attachments ?? new List<AttachmentType>(),
                FieldStatus = claim.FieldStatus ?? new Dictionary<string, ApplicationStatuses>(),
                ApplicationHistory = claim.ApplicationHistory ?? new List<ApplicationInfo>(),
                TitleOfTradeMark = claim.TitleOfTradeMark,
                TrademarkClass = claim.TrademarkClass,
                TrademarkClassDescription = claim.TrademarkClassDescription,
                TrademarkLogo = claim.TrademarkLogo,
                TrademarkType = claim.TrademarkType,
                TrademarkDisclaimer = claim.TrademarkDisclaimer,
                RtmNumber = claim.RtmNumber,
                Comment = claim.Comment,
                Registered_Users = claim.Registered_Users,
                RegisteredUsers = claim.RegisteredUsers ?? new List<RegisteredUser>(),
                Assignees = claim.Assignees ?? new List<Assignee>(),
                PostRegApplications = claim.PostRegApplications ?? new List<PostRegistrationApp>(),
                ClericalUpdates = claim.ClericalUpdates ?? new List<ClericalUpdate>(),
                MigratedPCTNo = claim.MigratedPCTNo,
                FilingDate = claim.FilingDate,
                Appeals = claim.Appeals ?? new List<Appeal>(),
                PublicationDate = null, // Not present in ClaimRequests
                PublicationReason = null, // Not present in ClaimRequests
                PublicationRequestDate = null // Not present in ClaimRequests
            };
            claim.IsMigrated = true;
            await _fillingCollection.InsertOneAsync(file);
            await _claimRequestsCollection.UpdateOneAsync(
                c => c.Id == claim.Id,
                Builders<ClaimRequests>.Update.Set(c => c.IsMigrated, true)
            );
            
            return true;

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    //Superadmin
    public async Task<bool> AdminUploadAttach(AdminUploadAttachmentDto req)
        {
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == req.FileNumber).FirstOrDefaultAsync();
                if (file == null) throw new Exception("File not found");
                if (req.Attachment == null) throw new Exception("Document is required");
        
                using var ms = new MemoryStream();
                await req.Attachment.CopyToAsync(ms);
                var appealDoc = ms.ToArray();
                var urlList = await _fileServices.UploadAttachment(new List<TT>
                {
                    new TT
                    {
                        contentType = req.Attachment.ContentType,
                        data = appealDoc,
                        fileName = Path.GetFileName(req.Attachment.FileName),
                        Name = req.AttachmentName
                    }
                });
        
                var newAtt = new AttachmentType
                {
                    name = req.AttachmentName,
                    url = new List<string> { urlList[0] }
                };
                // Check for existing attachment with the same name
                var existingIndex = file.Attachments.FindIndex(a => a.name == req.AttachmentName);
                if (existingIndex >= 0)
                {
                    // Replace the existing attachment
                    file.Attachments[existingIndex] = newAtt;
                }
                else
                {
                    // Add as new attachment
                    file.Attachments.Add(newAtt);
                }
                // Persist the change to the database
                var update = Builders<Filling>.Update.Set(f => f.Attachments, file.Attachments);
                await _fillingCollection.UpdateOneAsync(f => f.FileId == req.FileNumber, update);
        
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
}