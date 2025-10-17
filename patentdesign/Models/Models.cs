using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Contracts;
using System.Net.Mail;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace patentdesign.Models;

public record DesignForm
{
    [Required] public ApplicantInfo ApplicantInfo { get; set; } = new();
     public AssignInfo? AssigneeInfo { get; set; } = new();
     public AssignInfo? AssignorInfo { get; set; } = new();
    public DesignTypes DesignType { get; set; } = DesignTypes.Textile;
    [Required] public string TitleOfDesign { get; set; } = "";
    [Required] public string StatementOfNovelty { get; set; } = "";
     public List<ApplicantInfo> DesignCreators { get; set; } = new();
     public List<CoApplicants> DesigncoApplicants { get; set; } = new();
    [Required] public User TrademarkServiceInfo { get; set; } = new();
    public Dictionary<string, List<string>> Attachments { get; set; } = new();
}

public record PatentDesignDBSettings
{
    public string ConnectionString { get; set; } = null!;
    public string ConnectionStringUp { get; set; } = null!;

    public string DatabaseName { get; set; } = null!;

    public string FilesCollectionName { get; set; } = null!;
    public string AssignmentCollectionName { get; set; } = null!;
    public string CountersCollectionName { get; set; } = null!;
    public string TicketCollectionName { get; set; } = null!;
    public string UsersCollectionName { get; set; } = null!;
    public string FinanceCollectionName { get; set; } = null!;
    public string AttachmentCollectionName { get; set; } = null!;
    public string OppositionCollectionName { get; set; } = null!;
    public string UseSandbox { get; set; } = null!;
    public string LogPath { get; set; } = null!;

}

public record OppositionReceiptType
{
    public string paymentId { get; set; }
    public string amount { get; set; }
    public string description { get; set; }
    public string name { get; set; }
    // public string email { get; set; }
    public DateTime date { get; set; }
}
public record OppositionType
{

    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? creatorId { get; set; }
    public string? fileCreatorId { get; set; }
    public string? fileId { get; set; }
    public string? oppositionFile { get; set; }
    public string? responseFile { get; set; }
    public string? resolutionFile { get; set; }
    public ApplicationLetters? resolutionReceipt { get; set; }
    public ApplicationLetters? resolutionAcknowledgement { get; set; }
    public string? resolutionpaymentId { get; set; }
    public string? name { get; set; }
    public string? email { get; set; }
    public string? title { get; set; }
    public string? creationPaymentID { get; set; }
    public ApplicationLetters? recepitUrl { get; set; }
    public ApplicationLetters? ackUrl { get; set; }
    public string? address { get; set; }
    public string? number { get; set; }
    public DateTime? created { get; set; }
    public List<ApplicationHistory>? history {get; set; }
    public ApplicationStatuses? currentStatus { get; set; }
    public string? responseName { get; set; }
    public ApplicationLetters? responseReceiptUrl { get; set; }
    public ApplicationLetters? responseAckUrl { get; set; }
    public string? responseAddress { get; set; }
    public string? responsePaymentId { get; set; }
    public string? responseEmail { get; set; }
    public string? responseNumber { get; set; }

}
public record SummaryRequestObj
{
    public UserTypes userType { get; set; }
    public string userId { get; set; }
    public List<FileTypes>? types { get; set; }
    public List<ApplicationStatuses>? status { get; set; }
    public DateTime? startDate { get; set; }
    public DateTime? endDate { get; set; }
    public string? Title { get; set; }
    public string? PriorityNumber { get; set; }
    public List<PatentTypes>? patentTypes { get; set; }
    public List<DesignTypes>? designTypes { get; set; }
    public List<string>? applicantCountries { get; set; }
    
    public List<FormApplicationTypes>? applicationTypes { get; set; }
}

public record FileSummary
{
    public string id { get; set; }
    public string? title { get; set; }
    public ApplicationStatuses fileStatus { get; set; }
    

    public string FileId {
        get;
        set;
    }
    public List<FileApplicationSummary> Summaries { get; set; }
    public FileTypes Type { get; set; }
}

public record FileApplicationSummary
{
    public FormApplicationTypes ApplicationType { get; set; }
    public DateTime applicationDate { get; set; }
    public ApplicationStatuses ApplicationStatus { get; set; }
}

public record StatusRequests
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string userId { get; set; }
    public string fileId { get; set; }
    public string paymentId { get; set; }
    public ApplicationStatuses status { get; set; }
    public DateTime date { get; set; }
    public ApplicationLetters? receiptLetter { get; set; } = null;
    public ApplicationLetters? ackLetter { get; set; } = null;
    public string? applicantName { get; set; }
}

public record Filling
{
    
    [BsonId] 
    public  string Id { get; set; } = Guid.NewGuid().ToString();
    public string FileId { get; set; } = "";
    public DateTime LastRequestDate { get; set; }
    public string CreatorAccount { get; set; } = "";
    public ApplicationStatuses FileStatus { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.Now;
    public FileTypes Type { get; set; } = FileTypes.Design;
    public string? FilingCountry { get; set; } = string.Empty;
    public string? FileOrigin { get; set; }
    public string? TitleOfInvention { get; set; }
    public string? PatentAbstract { get; set; } = "";
    public CorrespondenceType? Correspondence { get; set; }
    public DateTime LastRequest { get; set; }
    public List<ApplicantInfo> applicants { get; set; } = new();
    public PatentApplicationTypes? PatentApplicationType { get; set; } = null;
    public List<Revision> Revisions { get; set; } = new();
    public PatentTypes? PatentType { get; set; } 
    public PatentBaseTypes? PatentBaseTypes { get; set; } = null;
    public List<ApplicantInfo> Inventors { get; set; } = new();
    public List<PriorityInfo> PriorityInfo { get; set; } = new();
    public List<PriorityInfo> FirstPriorityInfo { get; set; } = new(); 
    public DesignTypes? DesignType { get; set; } 
    public string? TitleOfDesign { get; set; }
    public string? StatementOfNovelty { get; set; } = "";
    public List<ApplicantInfo> DesignCreators { get; set; } = new();
    public List<AttachmentType> Attachments { get; set; } = new();
    public Dictionary<string, ApplicationStatuses> FieldStatus { get; set; } = [];
    public List<ApplicationInfo>? ApplicationHistory { get; set; }
    public string? TitleOfTradeMark {get;set;}
    public int?  TrademarkClass {get;set;}
    public string? TrademarkClassDescription { get; set; }
    public TradeMarkLogo? TrademarkLogo {get;set;}
    public TradeMarkType?  TrademarkType {get;set;}
    public string? TrademarkDisclaimer {get;set;}
    public string? RtmNumber {get;set;}
    public string? Comment { get; set; } = null;
    [BsonElement("registered_User")]
    public List<RegisteredUser>? Registered_Users { get; set; } 
    public List<RegisteredUser>? RegisteredUsers { get; set; } = [];
    public List<Assignee>? Assignees { get; set; } = [];
    public List<PostRegistrationApp>? PostRegApplications { get; set; } = [];
    public List<ClericalUpdate>? ClericalUpdates { get; set; } = [];
    public string? MigratedPCTNo { get; set; } = null;
    public DateTime? FilingDate { get; set; }
    public List<Appeal>? Appeals = [];
    public DateTime? PublicationDate { get; set; }
    public string? PublicationReason { get; set; }
    public DateTime? PublicationRequestDate { get; set; }
    public DateTime? WithdrawalDate { get; set; }
    public DateTime? WithdrawalRequestDate { get; set; }
    public string? WithdrawalReason { get; set; }
    public List<Opposition>? Oppositions { get; set; } = [];
}

public record ClericalUpdate
{
    public string Id { get; set; } 
    public string UpdateType { get; set; }
    public DateTime FilingDate { get; set; }
    public string? PaymentRRR { get; set; } = string.Empty;
    public string? OldTrademarkLogo { get; set; }
    public string? NewTrademarkLogo { get; set; }
    public string? OldApplicantName { get; set; }
    public string? NewApplicantName { get; set; }
    public string? OldApplicantAddress { get; set; }
    public string? NewApplicantAddress { get; set; }
    public string? OldApplicantNationality { get; set; }
    public string? NewApplicantNationality { get; set; }
    public string? OldApplicantEmail { get; set; }
    public string? NewApplicantEmail { get; set; }
    public string? OldApplicantPhone { get; set; }
    public string? NewApplicantPhone { get; set; }
    public string? OldFileClass { get; set; }
    public string? NewFileClass { get; set; }
    public string? OldClassDescription { get; set; }
    public string? NewClassDescription { get; set; }
    public string? OldFileTitle { get; set; }
    public string? NewFileTitle { get; set; }
    public string? OldCorrespondenceName {get; set; }
    public string? NewCorrespondenceName { get; set; }
    public string? OldCorrespondenceAddress { get; set; }
    public string? NewCorrespondenceAddress { get; set; }
    public string? OldCorrespondenceEmail { get; set; }
    public string? NewCorrespondenceEmail { get; set; }
    public string? OldCorrespondencePhone { get; set; }
    public string? NewCorrespondencePhone { get; set; }
    public string? OldRepresentationUrl { get; set; }
    public string? NewRepresentationUrl { get; set; }
    public string? OldPowerOfAttorneyUrl { get; set; }
    public string? NewPowerOfAttorneyUrl { get; set; }
    public string? OldAttachmentUrl { get; set; }
    public string? NewAttachmentUrl { get; set; }
    public string? OldDisclaimer { get; set; }
    public string? NewDisclaimer { get; set; }

    // Multi-applicant fields (for patents)
    public List<string>? OldApplicantNames { get; set; }
    public List<string>? NewApplicantNames { get; set; }
    public List<string>? OldApplicantAddresses { get; set; }
    public List<string>? NewApplicantAddresses { get; set; }
    public List<string>? OldApplicantNationalities { get; set; }
    public List<string>? NewApplicantNationalities { get; set; }
    public List<string>? OldApplicantEmails { get; set; }
    public List<string>? NewApplicantEmails { get; set; }
    public List<string>? OldApplicantPhones { get; set; }
    public List<string>? NewApplicantPhones { get; set; }
    public List<string>? OldApplicantStates { get; set; }
    public List<string>? NewApplicantStates { get; set; }
    public List<string>? OldApplicantCities { get; set; }
    public List<string>? NewApplicantCities { get; set; }

    // For 3-in-1 inventor update
    public List<ApplicantInfo>? NewInventors { get; set; }

    // For audit/history
    public List<string>? OldInventorNames { get; set; }
    public List<string>? NewInventorNames { get; set; }
    public List<string>? OldInventorPhones { get; set; }
    public List<string>? NewInventorPhones { get; set; }
    public List<string>? OldInventorAddresses { get; set; }
    public List<string>? NewInventorAddresses { get; set; }
    public List<string>? OldInventorEmails { get; set; }
    public List<string>? NewInventorEmails { get; set; }
    public List<string>? OldInventorNationalities { get; set; }
    public List<string>? NewInventorNationalities { get; set; }
    public List<string>? OldInventorStates { get; set; }
    public List<string>? NewInventorStates { get; set; }
    public List<string>? OldInventorCities { get; set; }
    public List<string>? NewInventorCities { get; set; }
}

public record Appeal
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Date { get; set; }
    public DateTime? DateTreated { get; set; }
    public string? Reason { get; set; }
    public List<string> AppealDocs { get; set; } = new ();
}
public record PostRegistrationApp
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RecordalType { get; set; } = "";
    public string FileNumber {  get; set; }
    public string FilingDate { get; set; } = "";
    public string? DateTreated { get; set; } = "";
    public string? Reason { get; set; } = null;
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? dateOfRecordal { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Nationality { get; set; }
    public string? documentUrl { get; set; }
    public string? document2Url { get; set; }
    public string? receiptUrl { get; set; }
    public string? certificateUrl { get; set; }
    public string? rejectionUrl { get; set; }
    public string? acknowledgementUrl { get; set; }
    public string? message { get; set; }
    public string rrr {  get; set; }
}
public record Assignee
{
    [BsonId]
    public string Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; } = "";
    public string? Email { get; set; } = "";
    public string? Phone { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string? rrr { get; set; } = null;
    public string FileId { get; set; }
    public string? AuthorizationLetterUrl { get; set; } = null;
    public string? AssignmentDeedUrl { get; set; } = null;
    public bool? isApproved { get; set; } = false;
}
public record RegisteredUser
{
    [BsonId]
    public string Id { get; set; } 
    public string Name { get; set; } = "";
    public string? Address { get; set; } = "";
    public string? Email { get; set; } = "";
    public string? Phone { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string FileId { get; set; }
    public bool? isApproved { get; set; } = false;

}
public enum TradeMarkLogo
{
    Device, WordMark, WordandDevice
}

public enum TradeMarkType
{
    Local, Foreign
}

public record Counters
{
    [BsonId]
    public string id { get; set; }
    public int currentNumber { get; set; }
}

public record ApplicationInfo
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public FormApplicationTypes ApplicationType { get; set; }
    public ApplicationStatuses CurrentStatus { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? PaymentId { get; set; }
    public string? CertificatePaymentId { get; set; }

    public DateTime ApplicationDate { get; set; }=DateTime.Now;
    public string? LicenseType { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? FieldToChange { get; set; }
    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<string, List<string>>? Letters { get; set; } = [];
    public List<ApplicationHistory> StatusHistory { get; set; } = [];
    public List<ApplicationLetters> ApplicationLetters { get; set; } = [];
    public AssignmentType? Assignment { get; set; }
    public string? RegisteredUser { get; set; } = null;
  

}


public record ApplicationHistory
{
    public DateTime Date { get; set; }
    public string? Message { get; set; }
     public ApplicationStatuses? beforeStatus { get; set; }
     public ApplicationStatuses? afterStatus { get; set; }
    public string? User { get; set; }
    public string? UserId { get; set; }
}

public record AttachmentType
{
    public string name { get; set; }
    public List<string> url { get; set; }
}

public record PerformanceMarker
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    public PerformanceType Type { get; set; }
    public FormApplicationTypes? ApplicationType { get; set; }
    public ApplicationStatuses? beforeStatus { get; set; }
    public ApplicationStatuses? afterStatus { get; set; }
    public DateTime? Date { get; set; }
    public string? user { get; set; }
    public string? fileId { get; set; }
    public FileTypes? fileType { get; set; }
    public PatentTypes? patentType { get; set; }
    public DesignTypes? designType { get; set; }
    public TradeMarkType? tradeMarkType { get; set; }
}

public enum PerformanceType {Staff,Application}

public record OtherPaymentModel
{
    [BsonId] public string? Id { get; set; } = Guid.NewGuid().ToString();
    public string agentId { get; set; }
    public string agentName { get; set; }
    public string? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public string? amount { get; set; }
    public string? rrr { get; set; }
    public string? receiptUrl { get; set; }
    public string? ackUrl { get; set; }
    public DateTime? date { get; set; }
    public string? attachmentUrl { get; set; }
    public string name { get; set; }
    public string email { get; set; }
    public string number { get; set; }
    public string? notes { get; set; }
}

public record PaymentServiceModel
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
    public string total { get; set; }
    public string serviceFee { get; set; }
    public string governmentFee { get; set; }
    public string notes { get; set; }
}

public record ManualPaymentConfirmation
{
    public string fileId { get; set; }
    public string applicationId { get; set; }
    public FormApplicationTypes applicationType { get; set; }
    public string userID { get; set; }
    public string userName { get; set; }
}

public record UserDashBasics
{
    public int Expired { get; set; }
    public int TotalDesigns { get; set; }
    public int TotalPatents { get; set; }
    public int TotalReviews { get; set; }
    public string? Latest { get; set; }
}

public record CorrespondenceType
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string? name { get; set; }
    public string? address {get;set;}
    public string? email {get;set;}
    public string? phone {get;set;}
    public string? state {get;set;}
    public string? Nationality {get;set; }
}


public enum DesignTypes
{
    Textile, NonTextile
}
public enum PatentTypes
{
    Conventional, Non_Conventional, PCT
}
public enum FileTypes
{
    Patent, Design, TradeMark
}
public enum PatentApplicationTypes
{
    Patent, Business_Method, Utility_Model
}

public enum AllLicenseTypes
{
    Initial, Subsequent
}

public enum UserTypes
{
    User, Search_Patent, Search_Design, Advanced, All, Admin, design_examiner, patent_examiner, AppealExaminer
}
public enum UserRoles
{
    PatentExaminer, PatentSearch, TrademarkExaminer, TrademarkSearch, DesignSearch, DesignExaminer, TrademarkOpposition, TrademarkCertification,
    Finance, Tickets, Users, Agent, Productivity, Support, PublicationMenu, OppositionMenu , StaffMenu, BackOffice, AppealExaminer, SuperAdmin, TrademarkAcceptance
}

public record ApplicantInfo 
{
    public string id { get; set; } = Guid.NewGuid().ToString();

     public string? Name { get; set; }
     public string? country { get; set; }
     public string? State { get; set; }
     public string? city { get; set; }
     public string? Phone { get; set; }
     public string? Email { get; set; }
     public string? Address { get; set; }

}
public class SignUpModel
{
    [Required] public string Email { get; set; }
    [Required] public string Password { get; set; }
    [Required] public string ConfirmPassword { get; set; }
    [Required] public string Name { get; set; }
    [Required] public string Number { get; set; }
}

public record CoApplicants : CustomUser
{
    
    public string id { get; set; } = Guid.NewGuid().ToString();
    [Required] public string Name { get; set; }
    [Required] public string Number { get; set; }
    [Required] public string Email { get; set; }

}

public record AssignInfo 
{
    public string id { get; set; } = Guid.NewGuid().ToString();

    [Required] public string Name { get; set; }
    [Required] public string Address { get; set; }
    [Required] public string Nationality { get; set; }
}
 
public record User 
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
    public string? Nationality { get; set; }
    public string? Number { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool? isVerified { get; set; }
    public string? Signature { get; set; }
    public UserTypes UserType { get; set; }
    public List<UserRoles> UserRole { get; set; }
}


public record TicketStats
{
    public int awaitingStaff { get; set; }
    public int awaitingAgent { get; set; }
    public int closed { get; set; }
    public Dictionary<string, int> StaffClosures { get; set; }
}
public record LoginResponse
{
    public bool status { get; set; }
    public string? message { get; set; }
    public User? user { get; set; }
}
public record LoginDetails
{
    public string email { get; set; }
    public string password { get; set; }
    public string id { get; set; }
    public string name { get; set; }
}

public abstract record CustomUser
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    [Required] public string Name { get; set; }
    public string? Nationality { get; set; }
    public string? Number { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Signature { get; set; }
    public string? NbaNumber { get; set; }
    public UserTypes UserType { get; set; }
    public UserRoles? UserRole { get; set; }
    public string LatestTradeId { get; set; }
}

public record SearchHistory
{
    public string id { get; set; }
    public DateTime SearchDate { get; set; }
    public string SearchTerm { get; set; }
    public FileTypes SearchType { get; set; }
    public string PaymentId { get; set; }
    public string UserId { get; set; }
    public string? ReceiptLink  { get; set; }

}

public partial class DBRemitaPayment
{
    [BsonId]
    public String Id { get; set; } = Guid.NewGuid().ToString();

   public DateTime DateCreated { get; set; }
   public bool IsDeleted { get; set; }
   public bool IsActive { get; set; }
   public string? CreatedBy { get; set; }
   public string? DeletedBy { get; set; }
   public string? UpdatedBy { get; set; }
   public DateTime? LastUpdateDate { get; set; }
   public byte[]? RowVersion { get; set; }
   public string? ServiceTypeId { get; set; }
   public decimal? Amount { get; set; }
   public string? OrderId { get; set; }
   public string? PayerName { get; set; }
   public string? PayerEmail { get; set; }
   public string? PayerPhone { get; set; }
   public string? Description { get; set; }
   public string? Rrrcode { get; set; }
   public string? Statuscode { get; set; }
   public string? Rrr { get; set; }
   public string? Status { get; set; }
   public DateTime? PaymentDate { get; set; }
   public decimal? TechFee { get; set; }
   public string? Channel { get; set; }
   public string? TotalAmount { get; set; }
   public int PaymentPurposeId { get; set; }
   public int PaymentStatus { get; set; }
   public string? RemitaPostPayLoad { get; set; }
   public string? RemitaResponsePayLoad { get; set; }
   public int FeeId { get; set; }
   public string? FeeItemName { get; set; }
   public string? RemitaPostVerifyPayLoad { get; set; }
   public string? RemitaResponseVerifyPayLoad { get; set; }
   public DateTime TransactionCompletedDate { get; set; }
   public DateTime TransactionInitiatedDate { get; set; }
   public string? InvoiceNumber { get; set; }
   public string? InterswitchProductId { get; set; }
   public string? InterswitchHash { get; set; }
   public string? XmlData { get; set; }
   public string? InterswitchPaymentParams { get; set; }
   public string? InterswitchSiteRedirectUrl { get; set; }
   public string? InterswitchCustId { get; set; }
   public string? InterswitchPayItemId { get; set; }
   public string? PaymentParams { get; set; }
   public string? TransactionRef { get; set; }
   public string? PaymentSource { get; set; }
   public string? Source { get; set; }
   public int? PaymentSourceApplicationId { get; set; }
   public int? UserId { get; set; }
   public decimal? PercentageFee { get; set; }
   public string? PercentageUsed { get; set; }
}




public enum FormApplicationTypes
{
    NewApplication, LicenseRenewal, DataUpdate, Recapture,
    None, Assignment, Ownership, RegisteredUser,Merger, ChangeOfName,
    ChangeOfAddress,ClericalUpdate, StatusSearch, AppealRequest,
    PublicationStatusUpdate, WithdrawalRequest, NewOpposition
}
public enum ApplicationLetters
{
    NewApplicationReceipt, 
    NewApplicationAcknowledgement, 
    NewApplicationAcceptance, 
    NewApplicationCertificate, 
    NewApplicationRejection, 
    RenewalReceipt, 
    RenewalAck, 
    RenewalCertificate, 
    RecordalReceipt, 
    RecordalAck, 
    RecordalCertificate, 
    AssignmentReceipt, 
    AssignmentAck,
    AssignmentCert,
    AssignmentRejection, 
    NewOppositionReceipt,
    NewOppositionAck,
    OppositionResponseReceipt, OppositionResponseAck, OppositionResolutionReceipt, OppositionResolutionAck, 
    NewApplicationCertificateAck, NewApplicationCertificateReceipt, StatusRequestReceipt, StatusRequestAck, 
    MergerReceipt, MergerAck, MergerCert,
    RegisteredUserReceipt, RegisteredUsersAck, RegisteredUserCertificate,
    ChangeOfAddressAck, ChangeOfNameAck,
    ChangeOfAddressReceipt, ChangeOfNameReceipt, ClericalUpdateReceipt, ClericalUpdateAck, NewTrademarkAppReceipt, StatusSearchReport, StatusSearchReceipt, AppealAck,
    PatentRenewalAcknowlegementLetter, PatentRenewalReceipt, PatentRenewalCertificate,
    PublicationStatusUpdateAcknowledgement, PublicationStatusUpdateReceipt, PublicationStatusUpdateApproval, PublicationStatusUpdateRefusal,
    ChangeOfNameCert, ChangeOfAddressCert, WithdrawalRequestAcknowledgement, WithdrawalRequestReceipt, WithdrawalRequestApproval, WithdrawalRequestRefusal
}
public class SearchInfo
{
    public string id { get; set; }
    public FileTypes Category { get; set; }
    public DateTime DateRegistered { get; set; }
    public string TradeMarkId { get; set; }
    public Dictionary<string, string> BenefitialInformation { get; set; }

    public SearchInfo() { }

    public SearchInfo(string id, FileTypes category, DateTime dateRegistered, Dictionary<string, string> benefitialInformation, string tradeMarkId)
    {
        this.id = id;
        Category = category;
        DateRegistered = dateRegistered;
        BenefitialInformation = benefitialInformation;
        TradeMarkId = tradeMarkId;
    }
}

public record Revision
{
    public string AssociatedTrade {get;set;}
    public dynamic OldValue {get;set;}
    public dynamic  NewValue {get;set;}
    public string Property {get;set;}
    public string  AmountPaid {get;set;}
    public string  TransactionId {get;set;}
    public DateTime DateTime { get; set; } = DateTime.Now;
    public string? userName { get; set; }
    public string? userId { get; set; }
    public ApplicationStatuses? currentStatus { get; set; }
}


public record RevisionChanges
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    // public RevisionStatus Status { get; set; } = RevisionStatus.awaiting_payment;
    public string Message { get; set; } = "Waiting for payment";
    public DateTime Date { get; set; }= DateTime.Now;
}

public record PriorityInfo
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string? ApplicationField { get; set; }
    public string number { get; set; }
    public string Country { get; set; }
    public string Date { get; set; }
}

public record UpdateMany
{
    public string reasons {get;set;}
        public int newStatus {get;set;}
    public string userId {get;set;}
        public string userName {get;set;}
    public List<string> files {get;set;}
}

public enum PatentBaseTypes 
{ 
    Local,
    Foreign
} 

public enum ApplicationStatuses
{
    Active, 
    Inactive, 
    AwaitingPayment, 
    AwaitingSearch, 
    AwaitingExaminer,
    RejectedByExaminer,
    Re_conduct,
    FormalityFail,
    KivSearch,
    KivExaminer,
    Approved,
    Rejected,
    None,
    AutoApproved,
    Publication,
    Opposition,
    AwaitingResponse, AwaitingOppositionStaff, AwaitingResolution,
    Resolved, AwaitingCertification,AwaitingConfirmation, AwaitingSave,
    AwaitingCertificateConfirmation,
    Withdrawn, AwaitingCertificatePayment, 
    AwaitingRecordalProcess, AppealRequest, AwaitingStatusUpdate, RequestWithdrawal, NewOpposition, AwaitingCounter, Amendment
}

public record AssignmentCertificateType
{
    public string fileNumber { get; set; }
    public string applicantName { get; set; }
    public CorrespondenceType CorrespondenceType { get; set; }
    public AssignmentType assignmentType { get; set; }
    public DateTime paymentDate { get; set; }
    public string examinerName { get; set; }
    public byte[] examinerSignature { get; set; }
}

public class TradeFilterModel
{
    public string Title { get; set; } = "";

    public string Owner { get; set; } = "";

    public string id { get; set; } = "";

    public IEnumerable<FileTypes> Category { get; set; }
    public IEnumerable<ApplicationStatuses> SelectedLicenseStatuses { get; set; }
    public IEnumerable<FormApplicationTypes> applicatoinTypes { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public TradeFilterModel()
    {
        this.Category = new List<FileTypes>();
        this.SelectedLicenseStatuses = new List<ApplicationStatuses>();
        applicatoinTypes = new List<FormApplicationTypes>();
    }
}

public enum PaymentTypes
{
    Search, NewCreation, LicenseRenew, Update, Assignment, OppositionCreation,
    Other, TrademarkCertificate, statusCheck, AvailabilitySearch, Merger, ChangeDataRecordal, Renewal, LateRenewal, ClericalUpdate,
    StatusSearch, NonConventional, PatentClericalUpdate, PatentLateRenewal, PublicationStatusUpdate, FileWithdrawal, Opposition
}


public record NotificationTypeModel
{
    public string ApplicantName { get; set; }
    public DateTime date { get; set; } = DateTime.Now;
    public string userId { get; set; }
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string message { get; set; }
    public string redirectUrl { get; set; }
    public string FileId { get; set; }
    public bool Read { get; set; }
    public string? Title { get; set; }
    public string NewStatus { get; set; }
}
public record StaffNotificationTypeModel
{
    public string ApplicantName { get; set; }
    public DateTime date { get; set; } = DateTime.Now;
    public UserTypes UserType { get; set; }
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string message { get; set; }
    public string redirectUrl { get; set; }
    public string FileId { get; set; }
    public bool Read { get; set; }
    public string Title { get; set; }
    public string? TreatedBy { get; set; }
}

public enum TicketState
{
    AwaitingUser, AwaitingStaff, Closed
}

public record TicketCreator
{
    public string Name { get; set; }
    public string Id { get; set; }
}

public record TicketSummary
{
    public string TicketId { get; set; }
    public string Title { get; set; }
    public  TicketState Status { get; set; } 
    public  TicketCreator Creator { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime LastInteraction { get; set; }
    public ResolveInfo? Resolution { get; set; }=null;
}

public record TicketInfo
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; }
    public string creatorId { get; set; }
    public string creatorName { get; set; }
    public List<TicketCorrespondence> Correspondences { get; set; }
    public  TicketState Status { get; set; }
    public ResolveInfo? resolution { get; set; }
    public DateTime Created { get; set; }=  DateTime.Now;
    public List<AffectedFile>? AffectedFiles { get; set; }
}

public record AffectedFile
{
    public string id { get; set; }
    public string fileNumber { get; set; }
}

public record ResolveInfo
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string StaffId { get; set; }
    public string StaffName { get; set; }
}

public  record TicketCorrespondence
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string Message { get; set; }
    public string? Attachment { get; set; }
    public string SenderId { get; set; }
    public string SenderName { get; set; }
    public DateTime DateAdded { get; set; }=  DateTime.Now;
}


public enum SearchTypes
{
    Exact, Similarity
}

public record Receipt
{
    public string? Title { get; set; }
    public string rrr { get; set; }
    public string ApplicantName { get; set; }
    public string PaymentFor { get; set; }
    public string? FileId { get; set; }
    public string Date { get; set; }
    public string? SearchTerm { get; set; }
    public string? Category { get; set; }
    public PaymentTypes? payType { get; set; }
    public string? Amount { get; set; }
}


public record PublicationType
{
    public string Title { get; set; }
    public DateTime Date { get; set; }
    public List<AttachmentType>? Images { get; set; }

    public List<ApplicantInfo> Applicants { get; set; }
    public CorrespondenceType Correspondence { get; set; }
    public string FileId { get;set; }
    public string Id { get; set; }
    public List<PriorityInfo>? PriorityInfos { get; set; }
    public List<ApplicantInfo>? inventorsCreators { get; set; }
    public List<byte[]>? ImagesUrl { get; set; }

}

public record AssignmentType
{
    public string Id { get; set; } = Guid.NewGuid().ToString(); 
    public string assignorName { get; set; }
    public DateTime dateOfAssignment { get; set; }
    public string assigneeName { get; set; }
    public string assigneeAddress { get; set; }
    public string assignorAddress { get; set; }
    public string assignorCountry { get; set; }
    public string assigneeCountry { get; set; }
    public string authorizationLetterUrl { get; set; }
    public string deedOfAgreementUrl { get; set; }
    public string? receiptUrl { get; set; }
    public string? acceptanceUrl { get; set; }
    public string? rejectionUrl { get; set; }
    public string? acknowledgementUrl { get; set; }
    public string? message { get; set; }
}

public record AssignmentHistory
{
    public string reason {get; set; }
    public string userId { get; set; }
    public string userName { get; set; }
    public ApplicationStatuses beforeStatus { get; set; }
    public ApplicationStatuses afterStatus { get; set; }
}

public record UpdateDataType
{
    public string? title { get; set; }
    public string? fileNumber { get; set; }
    public string? applicantName { get; set; }
    public string? amount { get; set; }
    public string? paymentId { get; set; }
    public ApplicationStatuses beforeStatus { get; set; }
    public ApplicationStatuses AfterStatus { get; set; }
    public string message { get; set; }
    public string user { get; set; }
    public string userId { get; set; }
    public FormApplicationTypes? applicationType { get; set; }
    public string fileId { get; set; }
    public string? applicationId { get; set; }
    public string? fieldToUpdate { get; set; }
    public string? newValue { get; set; }
    public FileTypes? FileType { get; set; }
    public List<DateOnly?>? dates { get; set; }

    public string? date { get; set; }
    public string? orderID { get; set; }
    public bool simulate { get; set; }

}

public class FileUpdateHistory
{
    [BsonId] // tells MongoDB this is the document ID
    [BsonRepresentation(BsonType.String)] // store as a string (not ObjectId)
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string FileNumber { get; set; }
    public string Title { get; set; }
    public FileTypes FileType { get; set; }
    public string UpdateType { get; set; } // "File Info", "Payment ID"
   
    public string AdminName { get; set; }
    public DateTime DateUpdated { get; set; } = DateTime.UtcNow;
}

public record PaymentInfo
{
    public string DesignCreationTextileCost { get; set; }
    public string DesignCreationNonTextileCost { get; set; }
    public string AssignmentCost { get; set; }
    public string PatentCreationConventionalCost { get; set; }
    public string PatentCreationNonConventionalCost { get; set; }
    public string DesignSearchCost { get; set; }
    public string PatentSearchCost { get; set; }
    public string DesignUpdateCost { get; set; }
    public string PatentTitleUpdateCost { get; set; }
    public string PatentAttachmentUpdateCost { get; set; }
    public string PatentOtherUpdateCost { get; set; }
    public string DesignTextileRenewCost { get; set; }
    public string DesignNonTextileRenewCost { get; set; }
    public string PatentRenewCost { get; set; }
    public string DesignCreationTextileID { get; set; }
    public string AssignmentID { get; set; }
    public string TrademarkRegistrationCost { get; set; }
    public string TrademarkRegistrationServiceFee { get; set; }
    public string TrademarkRegistrationID { get; set; }
    public string TrademarkApplicantUpdateCost { get; set; }
    public string TrademarkApplicantUpdateServiceFee { get; set; }
    public string TrademarkApplicantUpdateID { get; set; }
    public string TrademarkOtherUpdateCost { get; set; }
    public string TrademarkOtherUpdateServiceFee { get; set; }
    public string TrademarkOtherUpdateID { get; set; }
    public string AssignmentServiceFee { get; set; }
    public string DesignCreationNonTextileID { get; set; }
    public string PatentCreationConventionalID { get; set; }
    public string PatentCreationNonConventionalID { get; set; }
    public string DesignSearchID { get; set; }
    public string PatentSearchID { get; set; }
    public string DesignUpdateID { get; set; }
    public string PatentTitleUpdateID { get; set; }
    public string PatentAttachmentUpdateID { get; set; }
    public string PatentOtherUpdateID { get; set; }
    public string DesignTextileRenewID { get; set; }
    public string DesignNonTextileRenewID { get; set; }
    public string PatentRenewID { get; set; }
    public string DesignCreationTextileServiceFee { get; set; }
    public string DesignCreationNonTextileServiceFee { get; set; }
    public string PatentCreationConventionalServiceFee { get; set; }
    public string PatentCreationNonConventionalServiceFee { get; set; }
    public string DesignSearchServiceFee { get; set; }
    public string PatentSearchServiceFee { get; set; }
    public string DesignUpdateServiceFee { get; set; }
    public string PatentTitleUpdateServiceFee { get; set; }
    public string PatentAttachmentUpdateServiceFee { get; set; }
    public string PatentOtherUpdateServiceFee { get; set; }
    public string DesignTextileRenewServiceFee { get; set; }
    public string DesignNonTextileRenewServiceFee { get; set; }
    public string PatentRenewServiceFee { get; set; }
    public string OppositionCreationCost { get; set; }
    public string OppositionCreationServiceFee { get; set; }
    public string OppositionCreationID { get; set; }
    public string TrademarkCertificate { get; set; }
    public string TrademarkCertificateFee { get; set; }
    public string TrademarkCertificateServiceFee { get; set; }
    public string TrademarkCertificateServiceId { get; set; }

    public string? StatusCost { get; set; }
    public string? StatusServiceId { get; set; }
    public string? StatusServiceFee { get; set; }

    public string? FreeClaricalUpdateCost { get; set; }
    public string? FreeClaricalUpdateServiceFee { get; set; }
    public string? FreeClaricalUpdateServiceId { get; set; }

    //Availability Search
    public string? AvailabilitySearchCost { get; set; }
    public string? AvailabilitySearchServiceFee { get; set; }
    public string? AvailabilitySearchServiceID { get; set; }

    //Merger
    public string? MergerCost { get; set; }
    public string? MergerServiceFee { get; set; }
    public string? MergerServiceID { get; set; }
    // Change Data Recordal
    public string? ChangeDataRecordalCost { get; set; }
    public string? ChangeDataRecordalServiceFee { get; set; }
    public string? ChangeDataRecordalServiceID { get; set; }
    //Renewal App
    public string? TrademarkRenewalID { get; set; }
    public string? LateTrademarkRenewalCost { get; set; }
    public string? LateTrademarkRenewalServiceFee { get; set; }
    public string?  LateTrademarkRenewalID { get; set; }

    public string? AssignmentAppCost { get; set; }
    public string? TrademarkRenewalFee { get; set; }
    public string? TrademarkRenewalServiceFee { get; set; }
    
    //clerical update
    public string? ClericalUpdateCost { get; set; }
    public string? ClericalUpdateServiceFee { get; set; }
    public string? ClericalUpdateServiceID { get;set; }

    // Status Search
    public string StatusSearchCost { get; set; } = string.Empty;
    public string StatusSearchServiceFee { get; set; } = string.Empty;
    public string StatusSearchServiceId { get; set; } = string.Empty;

    //Patent ClericalUpdate
    public string? PatentClericalUpdateCost { get; set; }
    public string? PatentClericalUpdateServiceFee { get; set; }
    public string? PatentClericalUpdateServiceID { get; set; }

    //PatentLateRenewal 
    public string? PatentLateRenewalCost { get; set; }
    public string? PatentLateRenewalServiceFee { get; set; }
    public string? PatentLateRenewalServiceID { get; set; }

    public string? PublicationStatusUpdateCost { get; set; }    
    public string? PublicationStatusUpdateServiceFee { get; set; }
    public string? PublicationStatusUpdateServiceID { get; set; }

    public string? WithdrawalCost { get; set; }
    public string? WithdrawalServiceFee { get; set; }
    public string? WithdrawalServiceID { get; set; }

    //Opposition
    public string OppositionCost { get; set; }
    public string OppositionServiceFee { get; set; }
    public string OppositionServiceID { get; set; }

}

public record PaymentRecord
{
    [BsonId]
    public string Id { get; set; } =  Guid.NewGuid().ToString();
    public string PaymentType { get; set; }
    public DateTime Date { get; set; }
    public string? ApplicationId { get; set; }
    public string FileId { get; set; }
    public RemitaResponseClass RemitaResponse { get; set; }
}
public record MarkInfo
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string id { get; set; }
    public string xID { get; set; }
    public string reg_number { get; set; }
    public string tm_typeID { get; set; }
    public string logo_descriptionID { get; set; }
    public string national_classID { get; set; }
    public string product_title { get; set; }
    public string nice_class { get; set; }
    public string nice_class_desc { get; set; }
    public string sign_type { get; set; }
    public string vienna_class { get; set; }
    public string disclaimer { get; set; }
    public string logo_pic { get; set; }
    public string auth_doc { get; set; }
    public string sup_doc1 { get; set; }
    public string sup_doc2 { get; set; }
    public string log_staff { get; set; }
    public string reg_date { get; set; }
    public string xvisible { get; set; }
    public string xtime { get; set; }
    public string upload_date { get; set; }
}

public record Pwallet
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string id { get; set; }
    public int ID  { get; set; }
    public string validationID { get; set; }
    public string applicantID { get; set; }
    public string log_officer { get; set; }
    public string amt { get; set; }
    public int stage { get; set; }
    public int status { get; set; }
    public string data_status { get; set; }
    public DateTime reg_date { get; set; }
    public int visible {get; set; }
    public string xtime { get; set; }
    public string rtm { get; set; }
    public int acc_p { get; set; }
    public bool Claimed { get; set; } = false;
    public string? TransactionId { get; set; }
}

public record ClaimRequests
{
    
    [BsonId] 
    public  string Id { get; set; } = Guid.NewGuid().ToString();
    public string? FileId { get; set; } = "";
    public DateTime? LastRequestDate { get; set; }
    public string? CreatorAccount { get; set; } = "";
    public ApplicationStatuses? FileStatus { get; set; }
    public DateTime? DateCreated { get; set; } = DateTime.Now;
    public FileTypes? Type { get; set; } = FileTypes.Design;
    public string? FilingCountry { get; set; } = string.Empty;
    public string? FileOrigin { get; set; }
    public string? TitleOfInvention { get; set; }
    public string? PatentAbstract { get; set; } = "";
    public CorrespondenceType? Correspondence { get; set; }
    public DateTime? LastRequest { get; set; }
    public List<ApplicantInfo>? applicants { get; set; } = new();
    public PatentApplicationTypes? PatentApplicationType { get; set; } = null;
    public List<Revision>? Revisions { get; set; } = new();
    public PatentTypes? PatentType { get; set; } 
    public PatentBaseTypes? PatentBaseTypes { get; set; } = null;
    public List<ApplicantInfo>? Inventors { get; set; } = new();
    public List<PriorityInfo>? PriorityInfo { get; set; } = new();
    public List<PriorityInfo>? FirstPriorityInfo { get; set; } = new(); 
    public DesignTypes? DesignType { get; set; } 
    public string? TitleOfDesign { get; set; }
    public string? StatementOfNovelty { get; set; } = "";
    public List<ApplicantInfo>? DesignCreators { get; set; } = new();
    public List<AttachmentType>? Attachments { get; set; } = new();
    public Dictionary<string, ApplicationStatuses>? FieldStatus { get; set; } = [];
    public List<ApplicationInfo>? ApplicationHistory { get; set; }
    public string? TitleOfTradeMark {get;set;}
    public int?  TrademarkClass {get;set;}
    public string? TrademarkClassDescription { get; set; }
    public TradeMarkLogo? TrademarkLogo {get;set;}
    public TradeMarkType?  TrademarkType {get;set;}
    public string? TrademarkDisclaimer {get;set;}
    public string? RtmNumber {get;set;}
    public string? Comment { get; set; } = null;
    [BsonElement("registered_User")]
    public List<RegisteredUser>? Registered_Users { get; set; } = [];
    public List<RegisteredUser>? RegisteredUsers { get; set; } = [];
    public List<Assignee>? Assignees { get; set; } = [];
    public List<PostRegistrationApp>? PostRegApplications { get; set; } = [];
    public List<ClericalUpdate>? ClericalUpdates { get; set; } = [];
    public string? MigratedPCTNo { get; set; } = null;
    public DateTime? FilingDate { get; set; }
    public List<Appeal>? Appeals = [];
    public bool IsMigrated { get; set; } = false;
    public List<string>? ClaimDocuments { get; set; } = new();
}

public record CldxApplicants
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string id { get; set; }
    public string? ID { get; set; }
    public string? Xname { get; set; }
    public string? Xtype { get; set; }
    public string? TaxIdType { get; set; }
    public string? TaxIdNumber { get; set; }
    public string? IndividualIdNumber { get; set; }
    public string? Nationality { get; set; }
    public string? AddressID { get; set; }
    public string? LogStaff { get; set; }
    public DateTime? RegDate { get; set; }
    public string? Visible { get; set; }
    public string? Xold { get; set; }
    public string? Xtime { get; set; }
}

public record CldxAddresses
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string id { get; set; }
    public string? ID { get; set; }
    public string? CountryID { get; set; }
    public string? StateID { get; set; }
    public string? LgaID { get; set; }
    public string? City { get; set; }
    public string? Street { get; set; }
    public string? Zip { get; set; }
    public string? Telephone1 { get; set; }
    public string? Telephone2 { get; set; }
    public string? Email1 { get; set; }
    public string? Email2 { get; set; }
    public string? LogStaff { get; set; }
    public string? RegDate { get; set; }
    public string? Visible { get; set; }
    public string? Xtime { get; set; }
}

public record IpoNgMarkInformations
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? id { get; set; }
    public int? Id { get; set; }
    public string? DateCreated { get; set; }
    public int? IsDeleted { get; set; }
    public int? IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public string? LastUpdateDate { get; set; }
    public int? TradeMarkTypeID { get; set; }
    public string? ProductTitle { get; set; }
    public int? logo_descriptionID { get; set; }
    public string? LogoPicture { get; set; }
    public string? SupportDocument2 { get; set; }
    public string? ApprovalDocument { get; set; }
    public int? NiceClass { get; set; }
    public int? NationClassID { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? SupportDocument1 { get; set; }
    public int? userid { get; set; }
    public int? applicationid { get; set; }
    public string? NiceClassDescription { get; set; }
    public string? ApplicantPhone { get; set; }
    public string? ApplicantEmail { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantAddress { get; set; }
    public int? ApplicantNationality { get; set; }
    public string? Claimsanddisclaimer { get; set; }
    public string? AttorneyName { get; set; }
    public string? AttorneyAddress { get; set; }
    public string? AttorneyEmail { get; set; }
    public string? AttorneyCountry { get; set; }
    public string? NBAnumber { get; set; }
}
public class IpoNgApplication
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? id { get; set; }
    public int? Id { get; set; }
    public string? DateCreated { get; set; }
    public int? IsDeleted { get; set; }
    public int? IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public string? DeletedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public string? LastUpdateDate { get; set; }
    public string? RowVersion { get; set; }
    public int? Applicationtypeid { get; set; }
    public string? TransactionID { get; set; }
    public int? userid { get; set; }
    public string? ApplicationStatus { get; set; }
    public string? DataStatus { get; set; }
    public int? Batchno { get; set; }
    public string? CertificatePayReference { get; set; }
    public string? NextRenewalDate { get; set; }
    public string? RtNumber { get; set; }
    public int? migratedapplicationid { get; set; }
    public string? LastRenewalDate { get; set; }
    public string? AssociatedOldRtNumber { get; set; }
    public string? RegenerateRTM { get; set; }
    public string? RegenerateRTMForPaidM { get; set; }
    public string? RegenerateRTMPaidM { get; set; }
    public string? AssociatedOldRtNumberPaidM { get; set; }
    public string? OldMPaid { get; set; }
    public int? AddressUpdate { get; set; }
}

public class XpayTwallet
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? id { get; set; }
    public int? xid { get; set; }
    public string? transID { get; set; }
    public int? xmemberID { get; set; }
    public string? xmembertype { get; set; }
    public int? xpay_status { get; set; }
    public string? xgt { get; set; }
    public string? ref_no { get; set; }
    public int? xbankerID { get; set; }
    public DateTime? xreg_date { get; set; }
    public int? xvisible { get; set; }
    public int? xsync { get; set; }
    public int? applicantID { get; set; }
}

public class XpayApplicant
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? id { get; set; }
    public string? xid { get; set; }
    public string? xname {get; set;}
    public string? address { get; set; }
    public string? xemail { get; set; }
    public string? xmobile { get; set; }
}

public record Opposition
{
    [BsonId]
    public string? id { get; set; } = Guid.NewGuid().ToString();
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? FileTitle { get; set; }
    public string? Name { get; set; }
    public string? Phone {get; set;}
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Nationality { get; set; }
    public string? Reason { get; set; }
    public List<string>? SupportingDocs { get; set; }
    public ApplicationStatuses? Status { get; set; }
    public DateTime? OppositionDate { get; set; }
    public string? PaymentId { get; set; }
    public bool? IsCountered { get; set; } = false;
    public bool? IsResolved { get; set; } = false;
    public string? CounteredDate { get; set; }
    public bool? IsTreated { get; set; } = false;
    public bool? ApplicantNotified { get; set; } = false;
    public DateTime? ApplicantNotifiedDate { get; set; }
    public DateTime? ResolvedDate { get; set; }
    public bool? Paid { get; set; } = false;
}