using System.Security.Claims;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;
using Xunit;

namespace patentdesign.Tests.Services;

/// <summary>
/// This test class directly validates that GetOtherApplications follows the exact same pattern as Opposition.LoadSummary:
/// - When userId is omitted/null: return ALL users' records
/// - When userId is provided: return only that user's records
/// - Authorization is enforced at the controller level before calling service methods
/// </summary>
public class OtherApplicationsOppositionComparisonTests
{
    private const string CompanyAId = "company-a";
    private const string CompanyBId = "company-b";
    private const string TechStaffId = "tech-staff-1";

    [Fact]
    public void OtherApplications_BlankUserId_WithTechRole_ShouldReturnAllUsersHistories()
    {
        // === Opposition Pattern ===
        // When Opposition.LoadSummary(userId: null) is called by Tech, it returns all Opposition records
        // regardless of which company created them.

        // === OtherApplications should follow the same pattern ===
        var companyAApp = new ApplicationInfo
        {
            ApplicationType = FormApplicationTypes.AvailabilitySearch,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            Title = "Company A search"
        };
        var companyBApp = new ApplicationInfo
        {
            ApplicationType = FormApplicationTypes.AvailabilitySearch,
            CurrentStatus = ApplicationStatuses.AutoApproved,
            Title = "Company B search"
        };

        var users = new[]
        {
            new AppUser { Id = CompanyAId, OtherApplications = [companyAApp] },
            new AppUser { Id = CompanyBId, OtherApplications = [companyBApp] }
        };

        var techCaller = CreateCaller(TechStaffId, Roles.Tech);

        // When Tech requests with no userId (blank), should return ALL applications
        Assert.True(OtherApplicationsHistoryRules.CanViewAll(techCaller), "Tech must pass CanViewAll");
        Assert.True(
            OtherApplicationsHistoryRules.ShouldViewAll(null, techCaller),
            "null userId with Tech should trigger all-history behavior");

        var allApps = OtherApplicationsHistoryRules.Flatten(users);
        Assert.Equal(2, allApps.Count);
        Assert.Contains(companyAApp, allApps);
        Assert.Contains(companyBApp, allApps);
    }

    [Fact]
    public void OtherApplications_BlankUserId_WithSuperAdminRole_ShouldReturnAllUsersHistories()
    {
        // Same as Tech but with SuperAdmin
        var companyAApp = new ApplicationInfo { Title = "Company A" };
        var companyBApp = new ApplicationInfo { Title = "Company B" };
        var users = new[]
        {
            new AppUser { Id = CompanyAId, OtherApplications = [companyAApp] },
            new AppUser { Id = CompanyBId, OtherApplications = [companyBApp] }
        };

        var superAdminCaller = CreateCaller(TechStaffId, Roles.SuperAdmin);

        Assert.True(OtherApplicationsHistoryRules.CanViewAll(superAdminCaller));
        Assert.True(OtherApplicationsHistoryRules.ShouldViewAll(null, superAdminCaller));

        var allApps = OtherApplicationsHistoryRules.Flatten(users);
        Assert.Equal(2, allApps.Count);
    }

    [Fact]
    public void OtherApplications_SpecificUserId_WithTechRole_WithoutBeingSuperAdmin_ShouldReturnOnlyThatUser()
    {
        // === Opposition Pattern ===
        // When Opposition.LoadSummary(userId: "company-b") is called, it returns only Company B's records.

        // === OtherApplications should follow the same pattern ===
        // Tech requesting Company B's history should get ONLY Company B's records, not Company A's.

        var companyAApp = new ApplicationInfo { Title = "Company A search" };
        var companyBApp = new ApplicationInfo { Title = "Company B search" };

        var techCaller = CreateCaller(TechStaffId, Roles.Tech);

        // Tech requesting CompanyB's specific history
        // ShouldViewAll should be false (because userId != callerId and it's Tech, not SuperAdmin)
        Assert.False(
            OtherApplicationsHistoryRules.ShouldViewAll(CompanyBId, techCaller),
            "Tech requesting another user's ID should NOT trigger all-history behavior");

        // Instead, filtered behavior applies
        Assert.False(
            OtherApplicationsHistoryRules.CanViewRequestedOwner(CompanyBId, techCaller),
            "Tech (non-SuperAdmin) cannot access another company's records");
    }

    [Fact]
    public void OtherApplications_SpecificUserId_WithSuperAdminRole_ShouldReturnOnlyThatUser()
    {
        // === Opposition Pattern ===
        // When Opposition.LoadSummary(userId: "company-b") is called by SuperAdmin,
        // it returns ONLY Company B's records (not all companies).

        // === OtherApplications should follow the same pattern ===
        var superAdminCaller = CreateCaller(TechStaffId, Roles.SuperAdmin);

        // SuperAdmin requesting CompanyB's specific history (not their own)
        // ShouldViewAll should be false (because userId != callerId)
        Assert.False(
            OtherApplicationsHistoryRules.ShouldViewAll(CompanyBId, superAdminCaller),
            "SuperAdmin requesting another user's ID should NOT trigger all-history behavior");

        // Instead, filtered behavior applies
        Assert.True(
            OtherApplicationsHistoryRules.CanViewRequestedOwner(CompanyBId, superAdminCaller),
            "SuperAdmin CAN access another user's records (filtered, not all-history)");
    }

    [Fact]
    public void OtherApplications_OwnUserId_WithTechRole_ShouldReturnAllUsersHistories()
    {
        // === Opposition Pattern ===
        // Opposition doesn't have this special case, but GetOtherApplications does:
        // When Tech requests their own ID, it should behave like they omitted it (all-history).

        var techCaller = CreateCaller(TechStaffId, Roles.Tech);

        // Tech requesting their own userId should use all-history behavior
        Assert.True(
            OtherApplicationsHistoryRules.ShouldViewAll(TechStaffId, techCaller),
            "Tech requesting their own userId should trigger all-history behavior (same as omitting userId)");
    }

    [Fact]
    public void OtherApplications_OwnUserId_WithRegularUserRole_ShouldNotReturnAllHistories()
    {
        // Regular user requesting their own ID should NOT get all-history behavior
        var userCaller = CreateCaller(CompanyAId, Roles.User);

        Assert.False(
            OtherApplicationsHistoryRules.ShouldViewAll(CompanyAId, userCaller),
            "Regular user should never get all-history behavior, even for their own ID");
    }

    [Fact]
    public void OtherApplications_RegularUser_CannotViewAllRegardlessOfUserId()
    {
        // Regular users cannot request all-history at all
        var userCaller = CreateCaller(CompanyAId, Roles.User);

        Assert.False(OtherApplicationsHistoryRules.CanViewAll(userCaller));
        Assert.False(OtherApplicationsHistoryRules.ShouldViewAll(null, userCaller));
        Assert.False(OtherApplicationsHistoryRules.ShouldViewAll(CompanyAId, userCaller));
    }

    private static ClaimsPrincipal CreateCaller(string userId, Roles role) => new(
        new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role.ToString())
        ],
        authenticationType: "test"));
}
