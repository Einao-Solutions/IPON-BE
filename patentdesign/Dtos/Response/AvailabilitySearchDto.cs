using patentdesign.Models;

namespace patentdesign.Dtos.Response
{
    public class AvailabilitySearchDto
    {
        public string? FileId { get; set; }
        public CorrespondenceType? Correspondence { get; set; }
        public string? CreatorAccount { get; set; }
        public string? TitleOfInvention { get; set; }
        public string? TitleOfTradeMark { get; set; }
        public string? TitleOfDesign { get; set; }
        public int? TradeMarkClass { get; set; }
        public TradeMarkType? TrademarkType { get; set; }
        public FileTypes? FileTypes { get; set; }
        public string? cost { get; set; }
        public string? rrr { get; set; }
        public string? FileApplicant { get; set; }
        public string? FilingDate { get; set; }
        public TradeMarkLogo? TradeMarkLogo { get; set; }
        public ApplicationStatuses? FileStatus { get; set; }
        public string? LogoUrl { get; set; }
        public string? PatentType { get; set; }
        public string? PatentApplicationType { get; set; }
        public double Similarity { get; set; }
        public string? Disclaimer { get; set; }
        public string? FileOrigin { get; set; }
        public DateTime? PublicationDate { get; set; }
        public DateTime? WithdrawalDate { get; set; }
        public DateTime? WithdrawalRequestDate { get; set; }
        public List<PriorityInfo>? FirstPriorityInfo { get; set; } = new();
        public List<ApplicantInfo>? Applicants { get; set; }
        public bool? IsRenewalEligible { get; set; }
        public string? AppId { get; set; }

    }

    public class AvailabilitySearchPaymentStatusDto
    {
        public string? AppId { get; set; }
        public string? UserId { get; set; }
    }

    public enum AvailabilitySearchPaymentOutcome
    {
        Confirmed,
        AlreadyProcessed,
        NotFound,
        NotPaid,
        Unverifiable,
        Failed
    }

    public class AvailabilitySearchReconcileItem
    {
        public string? UserId { get; set; }
        public string? ApplicationId { get; set; }
        public string? PaymentId { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    public class AvailabilitySearchReconcileReport
    {
        public bool DryRun { get; set; }
        public int Scanned { get; set; }
        public int Confirmed { get; set; }
        public int AlreadyProcessed { get; set; }
        public int StillUnpaid { get; set; }
        public int NotFound { get; set; }
        public int Errors { get; set; }
        public List<AvailabilitySearchReconcileItem> Items { get; set; } = new();
    }

    public record AvailabilitySearchPaymentResult(
        AvailabilitySearchPaymentOutcome Outcome,
        string Message,
        ApplicationInfo? Application = null);
}
