using patentdesign.Models;
using patentdesign.Enums;

namespace patentdesign.Utils;

public enum PaymentVerificationOutcome
{
    Paid,
    NotPaid,
    Unverifiable,
    ReferenceMismatch
}

public sealed record PaymentVerificationResult(
    PaymentVerificationOutcome Outcome,
    RemitaResponseClass? Payment,
    string Message)
{
    public bool IsPaid => Outcome == PaymentVerificationOutcome.Paid;
}

public enum ApplicantPaymentOutcome
{
    Confirmed,
    AlreadyProcessed,
    NotFound,
    Forbidden,
    Unsupported,
    NotPaid,
    Unverifiable,
    Failed
}

public sealed record ApplicantPaymentResult(ApplicantPaymentOutcome Outcome, string Message, Filling? File = null)
{
    public bool IsSuccess => Outcome is ApplicantPaymentOutcome.Confirmed or ApplicantPaymentOutcome.AlreadyProcessed;
}

public enum RrrCostOutcome
{
    Ok,
    NotFound,
    Forbidden,
    Unverifiable
}

public sealed record RrrCostResult(RrrCostOutcome Outcome, double? Amount = null, string? Message = null);

/// <summary>
/// Single place that decides whether a Remita response proves payment. A frontend redirect or flag is never proof.
/// </summary>
public static class PaymentVerifier
{
    public const string SuccessStatus = "00";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(70);

    // Order ids ("IPO...") and Payx references are verified through other providers, so only plain RRRs
    // can be strictly compared with the reference echoed back by Remita.
    private static bool IsPlainRrr(string rrr) =>
        !rrr.Contains("IPO", StringComparison.OrdinalIgnoreCase) && rrr.Length <= 13 && !rrr.Contains('-');

    public static async Task<PaymentVerificationResult> VerifyAsync(
        Func<string, Task<RemitaResponseClass?>> lookup,
        string? rrr,
        decimal? expectedAmount = null,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(rrr))
        {
            return new PaymentVerificationResult(PaymentVerificationOutcome.ReferenceMismatch, null,
                "Payment reference not found for the application.");
        }

        var reference = rrr.Trim();
        RemitaResponseClass? payment;
        try
        {
            payment = await lookup(reference).WaitAsync(timeout ?? DefaultTimeout);
        }
        catch (Exception)
        {
            payment = null;
        }

        if (payment == null)
        {
            return new PaymentVerificationResult(PaymentVerificationOutcome.Unverifiable, null,
                "Payment could not be verified with Remita. Please try again shortly.");
        }

        var plain = IsPlainRrr(reference);
        if (plain && !string.IsNullOrWhiteSpace(payment.rrr) &&
            !string.Equals(payment.rrr.Trim(), reference, StringComparison.Ordinal))
        {
            return new PaymentVerificationResult(PaymentVerificationOutcome.ReferenceMismatch, payment,
                "Verified payment reference does not match the application.");
        }

        if (!string.Equals(payment.status, SuccessStatus, StringComparison.Ordinal))
        {
            return new PaymentVerificationResult(PaymentVerificationOutcome.NotPaid, payment,
                $"Payment has not been completed (status: {payment.status}).");
        }

        if (payment.amount is <= 0 || (plain && payment.amount == null))
        {
            return new PaymentVerificationResult(PaymentVerificationOutcome.NotPaid, payment,
                "Verified payment amount is invalid.");
        }

        if (expectedAmount.HasValue && payment.amount.HasValue &&
            Convert.ToDecimal(payment.amount.Value) != expectedAmount.Value)
        {
            return new PaymentVerificationResult(PaymentVerificationOutcome.NotPaid, payment,
                "Paid amount does not match the expected amount.");
        }

        return new PaymentVerificationResult(PaymentVerificationOutcome.Paid, payment, "Payment verified.");
    }
}
