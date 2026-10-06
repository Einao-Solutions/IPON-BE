using System.Security.Claims;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Utils;

public static class OtherApplicationsHistoryRules
{
    public static bool CanViewAll(ClaimsPrincipal caller) =>
        caller.Identity?.IsAuthenticated == true &&
        (caller.IsInRole(nameof(Roles.Tech)) ||
         caller.IsInRole(nameof(Roles.TrademarkSupport)) ||
         caller.IsInRole(nameof(Roles.PatentDesignSupport)) ||
         caller.IsInRole(nameof(Roles.SuperAdmin)));

    public static bool CanViewRequestedOwner(string ownerId, ClaimsPrincipal caller) =>
        AvailabilitySearchRules.CanAccessOwner(
            ownerId,
            caller.FindFirstValue(ClaimTypes.NameIdentifier),
            caller.IsInRole(nameof(Roles.SuperAdmin)));

    public static bool ShouldViewAll(string? requestedUserId, ClaimsPrincipal caller)
    {
        if (!CanViewAll(caller))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestedUserId))
        {
            return true;
        }

        var callerId = caller.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.Equals(requestedUserId, callerId, StringComparison.Ordinal);
    }

    public static List<ApplicationInfo> Flatten(IEnumerable<AppUser> users) =>
        FlattenApplications(users.Select(user => user.OtherApplications));

    public static List<ApplicationInfo> FlattenApplications(IEnumerable<List<ApplicationInfo>?> applicationLists) =>
        applicationLists
            .SelectMany(list => list ?? Enumerable.Empty<ApplicationInfo>())
            .Select(AvailabilitySearchRules.EnsureReferenceNumber)
            .ToList();
}
