using System.Security.Authentication;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
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
    private FilesServices _fileServices;
    private PaymentService _paymentService;
    private PaymentUtils _paymentUtils;
    private readonly ILogger<MigrationService> _log;

    public MigrationService(IMongoDatabase db, FilesServices fileServices, PaymentService paymentService, PaymentUtils paymentUtils, ILogger<MigrationService> log)
    {
        _markInfoCollection = db.GetCollection<MarkInfo>("cldxMarkinfo");
        _pwalletCollection = db.GetCollection<Pwallet>("cldxPwallet");
        _fillingCollection = db.GetCollection<Filling>("files");
        _claimRequestsCollection = db.GetCollection<ClaimRequests>("claimRequests");
        _cldxAddressesCollection = db.GetCollection<CldxAddresses>("cldxApplicantAddresses");
        _cldxApplicantsCollection = db.GetCollection<CldxApplicants>("cldxApplicants");
        _ipoNgMarkInformationsCollection = db.GetCollection<IpoNgMarkInformations>("ipongMarkInformations");
        _ipoNgApplicationCollection = db.GetCollection<IpoNgApplication>("ipongApplications");
        _xpayApplicantCollection = db.GetCollection<XpayApplicant>("xpayApplicants");
        _xpayTwalletCollection = db.GetCollection<XpayTwallet>("xpayTwallet");
        _fileServices = fileServices;
        _paymentService = paymentService;
        _paymentUtils = paymentUtils;
        _log = log;
    }
    public async Task<List<MarkInfoDto>> GetFileByRegNumber(string regNumber)
    {
        if (string.IsNullOrWhiteSpace(regNumber))
            throw new Exception("Registration number is required");
        try
        {
            var originalRegNumber = regNumber;
            regNumber = Uri.UnescapeDataString(regNumber).Trim();
            _log.LogInformation("Migration lookup started. Registration number: {RegistrationNumber}; normalized: {NormalizedRegistrationNumber}; original length: {OriginalLength}; normalized length: {NormalizedLength}",
                originalRegNumber, regNumber, originalRegNumber.Length, regNumber.Length);

            var file = await _fillingCollection.Find(f=>f.FileId == regNumber).FirstOrDefaultAsync();
            _log.LogDebug("Current files lookup completed for {RegistrationNumber}. Match found: {MatchFound}", regNumber, file != null);
            if (file != null) throw new Exception("File already exists on current system");
            // Try IPO Nigeria first
            var ipoResult = await GetFileFromIpoNigeria(regNumber);
            if (ipoResult != null && ipoResult.Count > 0)
            {
                _log.LogInformation("Migration lookup found {Count} IPO Nigeria result(s) for {RegistrationNumber}", ipoResult.Count, regNumber);
                return ipoResult;
            }
            // If not found, fallback to local cldxMarkinfo
            var localResult = await GetFromCldxMarkinfo(regNumber);
            if (localResult != null && localResult.Count > 0)
            {
                _log.LogInformation("Migration lookup found {Count} local result(s) for {RegistrationNumber}", localResult.Count, regNumber);
                return localResult;
            }
            _log.LogWarning("Migration lookup found no result for {RegistrationNumber}", regNumber);
            throw new Exception("File not found");
        }
        catch (Exception e)
        {
            _log.LogError(e, "Migration lookup failed for registration number {RegistrationNumber}", regNumber);
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
                _log.LogInformation("Searching IPO Nigeria for registration number {RegistrationNumber} in collection {CollectionName}",
                    fileNumber, _ipoNgMarkInformationsCollection.CollectionNamespace.CollectionName);
                var exFile  = await _fillingCollection.Find(x=>x.FileId == fileNumber).FirstOrDefaultAsync();
                _log.LogDebug("IPO migration existing-file check completed for {RegistrationNumber}. Match found: {MatchFound}", fileNumber, exFile != null);
                if (exFile != null) throw new Exception("File Exists on Current System");
                var mark =  await _ipoNgMarkInformationsCollection.Find(m => m.RegistrationNumber == fileNumber).FirstOrDefaultAsync();
                if (mark == null)
                {
                    _log.LogWarning("No IPO Nigeria mark matched registration number {RegistrationNumber}. The lookup uses an exact, case-sensitive RegistrationNumber match.", fileNumber);
                    return null;
                }

                _log.LogInformation("IPO Nigeria mark matched registration number {RegistrationNumber}. Stored registration number: {StoredRegistrationNumber}; application id: {ApplicationId}",
                    fileNumber, mark.RegistrationNumber, mark.applicationid);
                var app = await _ipoNgApplicationCollection.Find(a=>a.Id == mark.applicationid).FirstOrDefaultAsync();
                _log.LogDebug("IPO Nigeria application lookup completed for application id {ApplicationId}. Match found: {MatchFound}", mark.applicationid, app != null);
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

                string countryName = string.Empty;
                if (mark.ApplicantNationality.HasValue)
                {
                    countryName = ((CldxCountry)mark.ApplicantNationality.Value).ToString();
                }
                var applicant = new ApplicantInfo
                {
                    Name = mark.ApplicantName,
                    Email = mark.ApplicantEmail,
                    Phone = mark.ApplicantPhone?.ToString() ?? string.Empty,
                    Address = mark.ApplicantAddress,
                    country = countryName
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
                _log.LogError(e, "IPO Nigeria migration lookup failed for registration number {RegistrationNumber}", fileNumber);
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
            _log.LogInformation("Searching local mark information for registration number {RegistrationNumber}", regNumber);
            var mark =  await _markInfoCollection.Find(m => m.reg_number.Equals(regNumber)).FirstOrDefaultAsync();
            _log.LogDebug("Local mark information lookup completed for {RegistrationNumber}. Match found: {MatchFound}", regNumber, mark != null);
            if (mark == null)
            {
                _log.LogWarning("No local mark information matched registration number {RegistrationNumber}", regNumber);
                throw new Exception("File Not Found");
            }
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
            _log.LogInformation("Local mark information mapped successfully for registration number {RegistrationNumber}", regNumber);
            return files;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Local mark information lookup failed for registration number {RegistrationNumber}", regNumber);
            throw;
        }
    }
    public async Task<MarkInfoDto> GetPayment(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new Exception("Please provide a payment ID");
        try
        {
            string searchId = paymentId.Contains('-') ? paymentId.Split('-', 2)[0] : paymentId;
            _log.LogInformation("Payment lookup started for payment ID {PaymentId}. Pwallet search ID: {SearchId}", paymentId, searchId);
            var payment = await _pwalletCollection.Find(p => p.TransactionId == searchId).FirstOrDefaultAsync();
            _log.LogDebug("Pwallet lookup completed for search ID {SearchId}. Match found: {MatchFound}", searchId, payment != null);
            var paid = await _paymentService.CheckPayment(paymentId);
            _log.LogDebug("Remita payment lookup completed for payment ID {PaymentId}. Match found: {MatchFound}", paymentId, paid != null);
           
            ApplicationStatuses status;
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
            var xpaySearchIds = new List<string> { searchId, paymentId };
            if (!string.IsNullOrWhiteSpace(paid?.rrr)) xpaySearchIds.Add(paid.rrr);

            var xpay = await _xpayTwalletCollection
                .Find(Builders<XpayTwallet>.Filter.In(x => x.transID, xpaySearchIds.Distinct()))
                .FirstOrDefaultAsync();
            _log.LogDebug("Xpay wallet lookup completed for payment ID {PaymentId}. Match found: {MatchFound}; searched ID count: {SearchIdCount}",
                paymentId, xpay != null, xpaySearchIds.Distinct().Count());

            if (xpay == null && paid == null) throw new Exception("Payment Not Found");

            XpayApplicant applicant = null;
            if (xpay?.applicantID != null)
            {
                string appId = xpay.applicantID.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    applicant = await _xpayApplicantCollection.Find(a => a.xid == appId).FirstOrDefaultAsync();
                    _log.LogDebug("Xpay applicant lookup completed for applicant ID {ApplicantId}. Match found: {MatchFound}", appId, applicant != null);
                }
            }

            var applicants = new List<ApplicantInfo>();
            var app = new ApplicantInfo
            {
                Name = paid?.payerName ?? applicant?.xname ?? string.Empty,
                Email = paid?.payerEmail ?? applicant?.xemail ?? string.Empty,
                Address = applicant?.address ?? string.Empty,
                Phone = paid?.payerPhoneNumber ?? applicant?.xmobile ?? string.Empty,
            };
            applicants.Add(app);

            var history = new List<ApplicationInfo>();
            DateTime parsedPaymentDate = DateTime.MinValue;
            var hasParsedPaymentDate = !string.IsNullOrWhiteSpace(paid?.paymentDate)
                && DateTime.TryParse(paid!.paymentDate, out parsedPaymentDate);

            var fallbackDate = hasParsedPaymentDate ? parsedPaymentDate : DateTime.Now;
            var applicationDate = xpay?.xreg_date ?? fallbackDate;

            var newApp = new ApplicationInfo
            {
                PaymentId = paid?.rrr ?? payment?.TransactionId ?? paymentId,
                ApplicationDate = applicationDate,
                ApplicationType = FormApplicationTypes.NewApplication,
                CurrentStatus = status 
            };
            history.Add(newApp);

            var details = new MarkInfoDto
            {
                FilingDate = applicationDate.ToString(),
                FileStatus = status,
                Applicants = applicants,
                ApplicationHistory = history
            };
            _log.LogInformation("Payment lookup completed for payment ID {PaymentId} with status {Status}", paymentId, status);
            return details;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Payment lookup failed for payment ID {PaymentId}", paymentId);
            throw;
        }
    }
    public async Task<bool> NewClaimRequest(ClaimRequestDto req)
        {
            try
            {
                _log.LogInformation("New claim request started. Attachment count: {AttachmentCount}; mark count: {MarkCount}",
                    req?.Attachments?.Count ?? 0, req?.MarkInfo?.Count ?? 0);
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
                    _log.LogDebug("Claim attachment {AttachmentNumber} uploaded. File name: {FileName}", i + 1, Path.GetFileName(doc.FileName));
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
                
                _log.LogDebug("Building claim request for file number {FileNumber}", mark.FileNumber);
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
                _log.LogInformation("New claim request created for file number {FileNumber}. Claim ID: {ClaimId}; attachment count: {AttachmentCount}",
                    data.FileId, data.Id, claimDocs.Count);
                return true;
            }
            catch (Exception e)
            {
                _log.LogError(e, "New claim request failed");
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
            _log.LogInformation("Retrieved {ClaimRequestCount} unmigrated claim request(s)", claimRequests.Count);

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

            _log.LogDebug("Mapped {ClaimRequestCount} claim request result(s)", result.Count);
            return result;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Failed to retrieve all unmigrated claim requests");
            throw;
        }
    }
    public async Task<ClaimDetailsDto> GetClaimRequest(string fileId)
    {
        try
        {
            _log.LogInformation("Retrieving claim request for file number {FileNumber}", fileId);
            var claimRequest = await _claimRequestsCollection
                .Find(c => c.FileId == fileId)
                .FirstOrDefaultAsync();
            _log.LogDebug("Claim request lookup completed for file number {FileNumber}. Match found: {MatchFound}", fileId, claimRequest != null);
            if (claimRequest == null) throw new Exception("No Claim Found");
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
            _log.LogInformation("Claim request mapped successfully for file number {FileNumber}", fileId);
            return result;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Failed to retrieve claim request for file number {FileNumber}", fileId);
            throw;
        }
    }
    public async Task<bool> MigrateFile(string fileId)
    {
        try
        {
            _log.LogInformation("File migration started for file number {FileNumber}", fileId);
            var claim = await _claimRequestsCollection.Find(c => c.FileId == fileId).FirstOrDefaultAsync();
            _log.LogDebug("Claim lookup for migration completed for file number {FileNumber}. Match found: {MatchFound}", fileId, claim != null);
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
            // Idempotent insert: a previously half-completed migration must still be able to flag the claim.
            var existingFile = await _fillingCollection.Find(f => f.FileId == file.FileId).FirstOrDefaultAsync();
            if (existingFile == null)
            {
                await _fillingCollection.InsertOneAsync(file);
            }
            else
            {
                _log.LogWarning("File {FileNumber} already exists in the files collection; skipping insert and only flagging the claim(s) as migrated.", file.FileId);
            }

            // Flag every claim sharing this FileId, not just the first match, and verify the write actually happened.
            var updateResult = await _claimRequestsCollection.UpdateManyAsync(
                c => c.FileId == fileId,
                Builders<ClaimRequests>.Update.Set(c => c.IsMigrated, true)
            );

            if (updateResult.MatchedCount == 0)
            {
                _log.LogError("Migration flag was not written for file number {FileNumber}. No claim matched the update filter. Claim ID: {ClaimId}", fileId, claim.Id);
                throw new Exception($"Failed to set IsMigrated for file number {fileId}");
            }

            _log.LogInformation("File migration completed for file number {FileNumber}. Claim ID: {ClaimId}; claims matched: {MatchedCount}; claims modified: {ModifiedCount}",
                file.FileId, claim.Id, updateResult.MatchedCount, updateResult.ModifiedCount);
            return true;

        }
        catch (Exception e)
        {
            _log.LogError(e, "File migration failed for file number {FileNumber}", fileId);
            throw;
        }
    }
    
    //Superadmin
    public async Task<bool> AdminUploadAttach(AdminUploadAttachmentDto req)
        {
            try
            {
                _log.LogInformation("Admin attachment upload started for file number {FileNumber}. Attachment name: {AttachmentName}",
                    req?.FileNumber, req?.AttachmentName);
                var file = await _fillingCollection.Find(f => f.FileId == req.FileNumber).FirstOrDefaultAsync();
                _log.LogDebug("File lookup for admin attachment completed for file number {FileNumber}. Match found: {MatchFound}",
                    req.FileNumber, file != null);
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

                _log.LogInformation("Admin attachment upload completed for file number {FileNumber}. Attachment name: {AttachmentName}; total attachments: {AttachmentCount}",
                    req.FileNumber, req.AttachmentName, file.Attachments.Count);
                return true;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Admin attachment upload failed for file number {FileNumber}. Attachment name: {AttachmentName}",
                    req?.FileNumber, req?.AttachmentName);
                throw;
            }
        }
}