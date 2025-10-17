using MongoDB.Bson.Serialization.Attributes;

namespace patentdesign.Models;

    public record NewCorrespondenceType
    {
        public TicketCorrespondence correspondence { get; set; }
        public string ticketId { get; set; }
        public TicketState newStatus { get; set; }
    }

    public record ResolveTicketType
    {
        public ResolveInfo resolution { get; set; }
        public List<string> ticketId { get; set; }
    }

    public record TicketsSummariesType
    {
        public int? amount { get; set; }
        public int? startIndex { get; set; }
        public string? creatorId { get; set; }
        public string? title { get; set; }
        public TicketState? status { get; set; }
    }

    public record AssReq
    {
        public string fileNumber { get; set; }
        public string? userId { get; set; }
    }

    public record AssUpdateReq
    {
        public ApplicationStatuses newStatus { get; set; }
        public ApplicationStatuses currentStatus { get; set; }
        public string reason { get; set; }
        public string userName { get; set; }
        public string userId { get; set; }
        public string? amount { get; set; }
        public string? name { get; set; }
        public string? description { get; set; }
        public string applicationId { get; set; }
        public string? fileId { get; set; }
        public string? signatureUrl { get; set; }
        public string? paymentId { get; set; }
    }

    public record FinanceSummaryType
    {

        public double? total { get; set; }
        public double? techFee { get; set; }
        public double? ministryFee { get; set; }
        public string? type { get; set; }
        public string? country { get; set; }
    }

    public record OppostionCreateType
    {
        public string description { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string number { get; set; }
        public string address { get; set; }
        public string fileUrl { get; set; }
        public string fileID { get; set; }
        public string title { get; set; }
        public string userId { get; set; }
        public string userName { get; set; }
    }

    public record OppositionAckType
    {
        public DateTime date { get; set; }
        public string? description { get; set; }
        public string? paymentId { get; set; }
        public string? name { get; set; }
        public string? address { get; set; }
        public string? number { get; set; }
        public string? email { get; set; }
        
    }

    public record OppResReq
    {
        public string? fileUrl { get; set; }
        public string? oppositionID { get; set; }
        public string? amount { get; set; }
        public string? description { get; set; }
        public string? paymentId { get; set; }
        public string? name { get; set; }
        public string? address { get; set; }
        public string? email { get; set; }
        public string? number { get; set; }
        public string? userName { get; set; }
        public string? userId { get; set; }
    }
        public record GenerateOpReq
    {
        public string? description { get; set; }
        public string? name { get; set; } 
        public string? email { get; set; }
        public string? number { get; set; }
        public string? type { get; set; }
        public string? oppositionID { get; set; }
    }

    public record AssignmentTypeReq
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? paymentId { get; set; }
        public string creatorAccount { get; set; }
        public string userName { get; set; }
        public ApplicationStatuses? status { get; set; }
        public FileTypes type { get; set; }
        public string FileTitle { get; set; }
        public string fileId { get; set; }
        public string fileNumber { get; set; }
        public string assignorName { get; set; }
        public string assigneeName { get; set; }
        public string assigneeAddress { get; set; }
        public string assignorAddress { get; set; }
        public string assignorCountry { get; set; }
        public string assigneeCountry { get; set; }
        public string deedOfAgreementUrl { get; set; }
        public string authorizationLetterUrl { get; set; }
        public string applicantName { get; set; }
        public string applicantEmail { get; set; }
        public string applicantNumber { get; set; }
        public DateTime dateOfAssignment { get; set; }

    }

    public record TicketStatsReturnType
    {
        public long total { get; set; }
        public long user { get; set; }
        public long staff { get; set; }
        public long closed { get; set; }
    }

    public record UserUpdateReq
    {
        public string userId { get; set; }
        public List<UserRoles> newRoles { get; set; }
    }

    public record ReAssignType()
    {
        public  string? fileId {get;set;}
        public string? userName { get; set; }
        public string? userId { get; set; }
        public  string? newOwner {get;set;}
        public  string? oldId {get;set;}
        public  string? oldName {get;set;}
        public  CorrespondenceType? newCorrespondence {get;set;}
        public  CorrespondenceType? oldCorrespondence {get;set;}
    }

    public record GetUsersRequest
    {
        public string? name { get; set; }
        public List<UserRoles>? Roles { get; set; }
        public int? skip { get; set; } = 0;
        public int? take { get; set; } = 10;
        
    }

    public record UserCreateType 
    {
        [BsonId]
        public string id { get; set; }
        public string? uuid { get; set; }

        public string? Signature { get; set; }
        public List<UserRoles>? Roles { get; set; }
        public CorrespondenceType? DefaultCorrespondence { get; set; }
    
        public string? name { get; set; }
        public string? firstName { get; set; }
        public string? lastName { get; set; }
        public string? middleName { get; set; }
        public string? password { get; set; }
        public string? email { get; set; }
        public bool? verified { get; set; }

    }

    public record CorrReqData
    {
        public UserCreateType? user { get; set; }
        public CorrespondenceType? corr { get; set; } 
    }

    public record FinanceQueryType
    {
        public DateTime? startDate { get; set; }
        public DateTime? endDate { get; set; }
    }

    public record FinanceHistory
    {
        [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
        public string reason { get; set; }
        public string country { get; set; }
        public double total { get; set; }
        public double techFee { get; set; }
        public double ministryFee { get; set; }
        public DateTime date { get; set; }
        public string applicationID { get; set; }
        public string? fileId { get; set; }
        public DesignTypes? DesignType { get; set; }
        public PatentTypes? PatentType { get; set; }
        public FileTypes? Type { get; set; }
        public TradeMarkType? TradeMarkType { get; set; }
        public int? TradeMarkClass { get; set; }
        public RemitaResponseClass? remitaResonse { get; set; }
    }
     
    public record RemitaResponseClass
    {
        public double? amount { get; set; }
        public string? rrr { get; set; }
        public string? orderId { get; set; }
        public string? paymentDate { get; set; }
        public string? status { get; set; }
        public string? serviceTypeId { get; set; }
        public string? payerName { get; set; }
        public string? payerPhoneNumber { get; set; }
        public string? payerEmail { get; set; }
        public List<LineItem>? lineItems { get; set; }
        public string? paymentDescription { get; set; }
    }

    public record PaginatedResponse
    {
        public List<FileSummary> result { get; set; }
        public long count { get; set; }
    }


    public record GetRevisionCost
    {
        public FileTypes type { get; set; }
        public string  fieldToChange { get; set; }
    }



    public record GetRenewalCost
    {
        public string number { get; set; }
        public string applicantEmail { get; set; }
        public string? userName { get; set; }
        public string? userId { get; set; }
        public string? fileId { get; set; }
        public string applicantName { get; set; }
        public FileTypes type { get; set; }
        public DesignTypes? designType { get; set; }
        public PatentTypes? patentType { get; set; }
    }

    public record NewAppReq
    {
        public ApplicationInfo newApp { get; set; }
        public FormApplicationTypes applicationType { get; set; }
        public string fileId { get; set; }
    }

    public record NewCreation
    {
        public Filling file { get; set; }
        // public IFormFile? design1 { get; set; }
        // public IFormFile? design2 { get; set; }
        // public IFormFile? design3 { get; set; }
        // public IFormFile? design4 { get; set; }
        // public IFormFile? nov { get; set; }
        // public IFormFile? pdoc { get; set; }
        // public IFormFile? cs { get; set; }
        // public IFormFile? form2 { get; set; }
        // public IFormFile? pct { get; set; }
        // public IFormFile? any { get; set; }
        // public IFormFile? patentDrawing { get; set; }
    }

    public record TT
    {
        public string Name { get; set; }
        public byte[]? data { get; set; }
        public string fileName { get; set; }
        public string contentType { get; set; }
    }

    public record UpdateSigReq
    {
        public string UserId { get; set; }
        public byte[]? data { get; set; }
        public string fileName { get; set; }
        public string contentType { get; set; }
    }

    public record NewCreation1
    {
        public string file { get; set; }
        public List<TT>? attachments { get; set; }
    }

    public record Tester
    {
        public string where { get; set; }
    }
    public record AttachmentInfo
    {
        [BsonId]
        public string Id { get; set; }
        public string ContentType { get; set; }
        public byte[] Data { get; set; }
    }

    public record Attch
    {
        public string name { get; set; }
        public List<IFormFile> att { get; set; }
    }

    public record AdminUpdateReq
    {
        public string fileId { get; set; }
        public string applicationId { get; set; }
        public FormApplicationTypes applicationType { get; set; }
        public ApplicationStatuses beforeStatus { get; set; }
        public ApplicationStatuses afterStatus { get; set; }
        public string reason { get; set; }
        public string userId { get; set; }
        public string userName { get; set; }
    }
    public record DataUpdateReq
    {
        public string revisionId { get; set; }
        public string fileId { get; set; }
        public string? FileNumber { get; set; }
        public FileTypes fileType { get; set; }
        public string? phone { get; set; }
        public string? applicantName { get; set; }
        public string? email { get; set; }
        public DesignTypes? designType { get; set; }
        public PatentTypes? patentType { get; set; }
        public string oldValue { get; set; }
        public string newValue { get; set; }
        public string fieldToChange { get; set; }
        public string user { get; set; }
        public string userId { get; set; }
        public ApplicationStatuses? currentStatus { get; set; }
    }

    public record BatchRenewReq
    {
        public string userId { get; set; }
        public int? skip { get; set; }
    }

    public record BatchRenewRes
    {
        public long? total { get; set; }
        public List<BatchRenewData> data { get; set; }
    }

    public record BatchRenewData
    {
        public string fileTitle { get; set; }
        public string applicant { get; set; }
        public string fileNumber { get; set; }
        public string title { get; set; }
        public string cost { get; set; }
        public string paymentId { get; set; }
        public string id { get; set; }
        public FileTypes fileType { get; set; }
    }

    public record BatchReqSummary
    {
        public string FileNumber { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }
        public FileTypes Type { get; set; }
        public DesignTypes? DesignType { get; set; }
        public PatentTypes? PatentType { get; set; }
        public string Number { get; set; }
        public string Email { get; set; }
        public List<string> ApplicantNames { get; set; }
    }

    public record UpdateReq
    {
        public string patentChangeType { get; set; }
        public FileTypes fileType { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string number { get; set; }
        
    }
   
    public record SearchRes
    {
        public ApplicationStatuses? FileStatus { get; set; }
        public string? Id { get; set; }
    }

    public record RenewSearchRes
    {
        public ApplicationStatuses? FileStatus { get; set; }
        public string? Id { get; set; }
        public string? title { get; set; }
        public FileTypes fileType { get; set; }
        public DesignTypes? designType { get; set; }
        public string amount { get; set; }
        public string? applicants { get; set; }
    }
    public record LineItem
    {
        public string bankCode { get; set; }
        public string lineItemsId { get; set; }
        public string deductFeeFrom { get; set; }
        public string beneficiaryName { get; set; }
        public string beneficiaryAccount { get; set; }
        public double beneficiaryAmount { get; set; }
    }

    public record   FileStatsRes
    {
        public List<DetailedStats> detailedStats { get; set; }
        public List<FilesCount> fileStats { get; set; }
        public List<dynamic> inactive { get; set; }
    }
    public record FilesCount {
        public FileTypes fileType { get; set; }
        public int count { get; set; }
    }
    public record DetailedStats
    {
        public int count { get; set; }
        public FileTypes fileType { get; set; }
        public FormApplicationTypes type { get; set; }
        public ApplicationStatuses status { get; set; }
    }
   