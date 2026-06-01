using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Dtos.Response;

public class EmailDto
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string? Body { get; set; }
    public string? CarbonCopy { get; set; }
    public EmailType EmailType { get; set; }
    public OppositionMail? OppositionMail { get; set; }
    public ResetPasswordMail? ResetPasswordMail { get; set; }
    public StatusUpdateMail? StatusUpdateMail { get; set; }
    public CounterStatementMail? CounterStatementMail { get; set; }
    public StatutoryDeclarationMail? StatutoryDeclarationMail { get; set; }
    public OppositionConfirmationMail? OppositionConfirmationMail { get; set; }
    public WithdrawalNotificationMail? WithdrawalNotificationMail { get; set; }
    public WithdrawalApprovedMail? WithdrawalApprovedMail { get; set; }
    public WithdrawalRefusedMail? WithdrawalRefusedMail { get; set; }
    public WithdrawalApprovedApplicantMail? WithdrawalApprovedApplicantMail { get; set; }
    public WithdrawalRefusedApplicantMail? WithdrawalRefusedApplicantMail { get; set; }
    public RenewalReminder? RenewalReminder { get; set; }
}

public class BulkEmailDto
{
    public Dictionary<string, string>? Recipients { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; } 
}

public class OppositionMail
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public string ApplicantName { get; set; }
    public string FileNumber { get; set; }
    public string Title { get; set; }
    public string OpposerName { get; set; }
    public string Reason { get; set; }
    public string OppositionDate { get; set; }
    public string? SignatoryName { get; set; }
    public string? OppositionId { get; set; }
}

public class CounterStatementMail
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string OpposerName { get; set; }
    public string FileOwnerName { get; set; }
    public string FileNumber { get; set; }
    public string Title { get; set; }
    public string CounterStatementDate { get; set; }
    public string? SignatoryName { get; set; }
}

public class OppositionConfirmationMail
{
    public string To { get; set; }
    public string OpposerName { get; set; }
    public string OppositionId { get; set; }
    public string FileNumber { get; set; }
    public string FileTitle { get; set; }
    public string DateFiled { get; set; }
    public string PaymentReference { get; set; }
}

public class StatutoryDeclarationMail
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string RecipientName { get; set; }
    public string FilerRole { get; set; }
    public string FileNumber { get; set; }
    public string FileTitle { get; set; }
    public string OppositionId { get; set; }
    public string DateFiled { get; set; }
}
public class WithdrawalNotificationMail
{
    public string To { get; set; }
    public string ApplicantName { get; set; }
    public string OpposerName { get; set; }
    public string FileNumber { get; set; }
    public string FileTitle { get; set; }
    public string OppositionId { get; set; }
    public string WithdrawalDate { get; set; }
}

public class WithdrawalApprovedMail
{
    public string To { get; set; }
    public string RecipientName { get; set; }
    public string FileNumber { get; set; }
    public string FileTitle { get; set; }
    public string OfficerName { get; set; }
    public string Reason { get; set; }
    /// <summary>"opposer" or "applicant"</summary>
    public string RecipientRole { get; set; }
}

public class WithdrawalRefusedMail
{
    public string To { get; set; }
    public string RecipientName { get; set; }
    public string FileNumber { get; set; }
    public string OfficerName { get; set; }
    public string Reason { get; set; }
}

public class WithdrawalApprovedApplicantMail
{
    public string To { get; set; }
    public string RecipientName { get; set; }
    public string FileNumber { get; set; }
    public string FileTitle { get; set; }
    public string OfficerName { get; set; }
}

public class WithdrawalRefusedApplicantMail
{
    public string To { get; set; }
    public string RecipientName { get; set; }
    public string FileNumber { get; set; }
    public string FileTitle { get; set; }
    public string OfficerName { get; set; }
}

public class ResetPasswordMail
{
    public string UserName { get; set; }
    public string ResetLink { get; set; }
}

public class StatusUpdateMail
{
    public string ApplicationType { get; set; }
    public string FormerStatus { get; set; }
    public string NewStatus { get; set; }
    public DateTime DateTreated { get; set; }
    public string? Remarks { get; set; }
}
public class RenewalReminder 
{ 
    public string? ApplicantName { get; set; }
    public string FileNumber { get; set; }
    public string Title { get; set; }
    public DateTime RenewalDue { get; set; }
    public bool IsExpiryDay { get; set; }
    public FileTypes Type { get; set; }
    public int Class { get; set; }
    public string RegistryName { get; set; } = "Trademarks";

}