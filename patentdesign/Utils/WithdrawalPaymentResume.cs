using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Utils;

/// <summary>
/// Resumes a WithdrawalRequest application left in AwaitingPayment, using only data already persisted on the file.
/// </summary>
public static class WithdrawalPaymentResume
{
    public const string WithdrawalLetterAttachment = "withdrawal_letter";
    public const string WithdrawalPaymentType = "File Withdrawal";

    public static bool HasPersistedWithdrawalLetter(Filling file) =>
        file.Attachments?.Any(a => a.name == WithdrawalLetterAttachment && a.url != null && a.url.Any(u => !string.IsNullOrWhiteSpace(u))) == true;

    public static async Task<ApplicantPaymentResult> ResumeAsync(
        Filling? file,
        string applicationId,
        Func<string, Task<RemitaResponseClass?>> paymentLookup,
        Func<Filling, ApplicationInfo, RemitaResponseClass, Task> persist,
        string? userName,
        string? userId)
    {
        var application = file?.ApplicationHistory?.FirstOrDefault(a =>
            a.id == applicationId && a.ApplicationType == FormApplicationTypes.WithdrawalRequest);
        if (file == null || application == null)
            return new ApplicantPaymentResult(ApplicantPaymentOutcome.NotFound, "Withdrawal request not found for this file.");

        if (application.CurrentStatus != ApplicationStatuses.AwaitingPayment)
            return new ApplicantPaymentResult(ApplicantPaymentOutcome.AlreadyProcessed,
                "Withdrawal request payment has already been processed.", file);

        if (!HasPersistedWithdrawalLetter(file))
            return new ApplicantPaymentResult(ApplicantPaymentOutcome.Failed,
                "Withdrawal letter was never saved for this request. Please resubmit the withdrawal request with the letter.");

        var verification = await PaymentVerifier.VerifyAsync(paymentLookup, application.PaymentId);
        if (!verification.IsPaid)
        {
            return new ApplicantPaymentResult(
                verification.Outcome == PaymentVerificationOutcome.Unverifiable
                    ? ApplicantPaymentOutcome.Unverifiable
                    : ApplicantPaymentOutcome.NotPaid,
                verification.Message);
        }

        var paymentDate = AppTime.TryParseToUtc(verification.Payment!.paymentDate, out var paidAt) ? paidAt : DateTime.UtcNow;
        ApplyPaid(file, application, paymentDate, userName, userId);
        await persist(file, application, verification.Payment);

        return new ApplicantPaymentResult(ApplicantPaymentOutcome.Confirmed,
            "Payment confirmed. Withdrawal request submitted and awaiting admin review.", file);
    }

    // Same paid state the normal POST /api/files/WithdrawalRequest flow records.
    public static void ApplyPaid(Filling file, ApplicationInfo application, DateTime paymentDate, string? userName, string? userId)
    {
        file.WithdrawalRequestDate ??= DateTime.UtcNow;
        application.StatusHistory ??= new List<ApplicationHistory>();
        application.StatusHistory.Add(new ApplicationHistory
        {
            Date = paymentDate,
            beforeStatus = ApplicationStatuses.AwaitingPayment,
            afterStatus = ApplicationStatuses.RequestWithdrawal,
            Message = "Payment Successful, Withdrawal Request Submitted",
            User = userName ?? "System",
            UserId = userId
        });
        application.CurrentStatus = ApplicationStatuses.RequestWithdrawal;
    }
}
