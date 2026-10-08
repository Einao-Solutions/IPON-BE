using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Utils;

public static class AvailabilitySearchRules
{
    public const string ReferencePrefix = "AVS-";
    private const int ReferenceLength = 10;

    // Derived from the immutable application id, so every record (old or new) gets the same
    // reference without a backfill write or an extra database lookup.
    public static string? BuildReferenceNumber(string? applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return null;
        }

        var token = new string(applicationId.Where(char.IsLetterOrDigit).Take(ReferenceLength).ToArray()).ToUpperInvariant();
        return token.Length == 0 ? null : ReferencePrefix + token;
    }

    public static ApplicationInfo EnsureReferenceNumber(ApplicationInfo application)
    {
        if (application.ApplicationType == FormApplicationTypes.AvailabilitySearch &&
            string.IsNullOrWhiteSpace(application.ReferenceNumber))
        {
            application.ReferenceNumber = BuildReferenceNumber(application.id);
        }

        return application;
    }

    public static ApplicationInfo CreateApplication(string searchTerm, string paymentReference, DateTime? applicationDate = null)
    {
        var application = new ApplicationInfo
        {
            ApplicationType = FormApplicationTypes.AvailabilitySearch,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            ApplicationDate = applicationDate ?? DateTime.UtcNow,
            PaymentId = paymentReference,
            Title = searchTerm,
            StatusHistory = []
        };

        return EnsureReferenceNumber(application);
    }

    public static ApplicationInfo? FindAwaitingPaymentApplication(IEnumerable<ApplicationInfo>? applications, string searchTerm) =>
        applications?.FirstOrDefault(application =>
            application.ApplicationType == FormApplicationTypes.AvailabilitySearch &&
            application.CurrentStatus == ApplicationStatuses.AwaitingPayment &&
            application.Title == searchTerm &&
            !string.IsNullOrWhiteSpace(application.PaymentId));

    public static ApplicationInfo? FindOwnedApplication(AppUser? owner, string appId) =>
        owner?.OtherApplications?.FirstOrDefault(application =>
            application.id == appId && application.ApplicationType == FormApplicationTypes.AvailabilitySearch);

    public static bool CanAccessOwner(string ownerId, string? callerId, bool isSuperAdmin) =>
        isSuperAdmin || (!string.IsNullOrWhiteSpace(callerId) && string.Equals(ownerId, callerId, StringComparison.Ordinal));

    public static bool IsAwaitingPayment(ApplicationInfo application) =>
        application.ApplicationType == FormApplicationTypes.AvailabilitySearch &&
        application.CurrentStatus == ApplicationStatuses.AwaitingPayment;

    public static bool IsAlreadyConfirmed(ApplicationInfo application) =>
        application.ApplicationType == FormApplicationTypes.AvailabilitySearch &&
        application.CurrentStatus == ApplicationStatuses.AutoApproved;

    public static bool CanConfirmPayment(ApplicationInfo application, RemitaResponseClass? payment) =>
        IsAwaitingPayment(application) &&
        !string.IsNullOrWhiteSpace(application.PaymentId) &&
        IsSuccessfulPayment(payment, application.PaymentId);

    public static bool IsSuccessfulPayment(RemitaResponseClass? payment, string expectedRrr) =>
        payment != null &&
        payment.status == "00" &&
        string.Equals(payment.rrr, expectedRrr, StringComparison.Ordinal);
}