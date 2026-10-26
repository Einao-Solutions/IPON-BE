using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Utils;

public static class AvailabilitySearchRules
{
// Matches users whose _id is stored as a string or as a BSON ObjectId.
    public static FilterDefinition<AppUser> OwnerFilter(string userId)
    {
        var filter = Builders<AppUser>.Filter.Eq(x => x.Id, userId);
        if (ObjectId.TryParse(userId, out var objectId))
        {
            filter |= new BsonDocument("_id", objectId);
        }

        return filter;
    }

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
StatusHistory = [new ApplicationHistory { Date = applicationDate ?? DateTime.UtcNow, beforeStatus = ApplicationStatuses.AwaitingPayment, afterStatus = ApplicationStatuses.AwaitingPayment, Message = "Availability search created, awaiting payment", User = "System", UserId = "System" }]
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

    private static readonly System.Text.RegularExpressions.Regex ReferencePattern =
        new("^AVS-[A-Z0-9]{1,10}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Returns the canonical (trimmed, upper-case) reference, or null when it is not a valid AVS reference.
    public static string? NormalizeReference(string? referenceNumber)
    {
        var normalized = referenceNumber?.Trim().ToUpperInvariant();
        return normalized != null && ReferencePattern.IsMatch(normalized) ? normalized : null;
    }

    public static bool MatchesReference(ApplicationInfo application, string normalizedReference)
    {
        if (application.ApplicationType != FormApplicationTypes.AvailabilitySearch)
        {
            return false;
        }

        var stored = application.ReferenceNumber?.Trim();
        var reference = !string.IsNullOrWhiteSpace(stored) ? stored : BuildReferenceNumber(application.id);
        return string.Equals(reference, normalizedReference, StringComparison.OrdinalIgnoreCase);
    }

    public static (AppUser Owner, ApplicationInfo Application)? FindByReference(IEnumerable<AppUser> users, string? referenceNumber)
    {
        var reference = NormalizeReference(referenceNumber);
        if (reference == null) return null;

        foreach (var user in users)
        {
            var app = user.OtherApplications?.FirstOrDefault(a => MatchesReference(a, reference));
            if (app != null) return (user, app);
        }

        return null;
    }

    public static async Task<(AppUser Owner, ApplicationInfo Application)?> FindByReferenceAsync(
        IMongoCollection<AppUser> users, string? referenceNumber)
    {
        var reference = NormalizeReference(referenceNumber);
        if (reference == null) return null;

        var filter = Builders<AppUser>.Filter.ElemMatch(
            u => u.OtherApplications,
            a => a.ApplicationType == FormApplicationTypes.AvailabilitySearch);

        using var cursor = await users.Find(filter).ToCursorAsync();
        while (await cursor.MoveNextAsync())
        {
            var match = FindByReference(cursor.Current, reference);
            if (match != null) return match;
        }

        return null;
    }

    // Applies the editable fields; returns false when nothing was supplied.
    public static bool ApplyUpdate(ApplicationInfo application, DateTime? applicationDate,
        ApplicationStatuses? currentStatus, string? paymentId, string? certificatePaymentId)
    {
        var changed = false;
        if (applicationDate.HasValue) { application.ApplicationDate = applicationDate.Value; changed = true; }
        if (currentStatus.HasValue) { application.CurrentStatus = currentStatus.Value; changed = true; }
        if (!string.IsNullOrWhiteSpace(paymentId)) { application.PaymentId = paymentId; changed = true; }
        if (!string.IsNullOrWhiteSpace(certificatePaymentId)) { application.CertificatePaymentId = certificatePaymentId; changed = true; }
        return changed;
    }

    public static bool IsSuperAdmin(System.Security.Claims.ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && user.IsInRole("SuperAdmin");

    public static patentdesign.Dtos.Request.FileUpdateDto ToFileUpdateDto(ApplicationInfo application)
    {
        EnsureReferenceNumber(application);
        return new patentdesign.Dtos.Request.FileUpdateDto
        {
            FileId = application.ReferenceNumber ?? string.Empty,
            ReferenceNumber = application.ReferenceNumber,
            FileStatus = application.CurrentStatus,
            Comment = application.Title,
            ApplicationHistory = new List<ApplicationInfo> { application }
        };
    }
}