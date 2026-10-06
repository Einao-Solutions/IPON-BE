using Xunit;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;

namespace patentdesign.Tests.Services;

public class AvailabilitySearchReferenceTests
{
    [Fact]
    public void CreateApplication_AssignsReferenceNumber()
    {
        var application = AvailabilitySearchRules.CreateApplication("ACME", "RRR1");

        Assert.NotNull(application.ReferenceNumber);
        Assert.StartsWith(AvailabilitySearchRules.ReferencePrefix, application.ReferenceNumber);
        Assert.Equal(AvailabilitySearchRules.BuildReferenceNumber(application.id), application.ReferenceNumber);
    }

    [Fact]
    public void BuildReferenceNumber_IsDeterministicUppercaseAndUrlSafe()
    {
        var first = AvailabilitySearchRules.BuildReferenceNumber("3f9a1c7b-2d4e-4f60-8a1b-0c2d3e4f5a6b");
        var second = AvailabilitySearchRules.BuildReferenceNumber("3f9a1c7b-2d4e-4f60-8a1b-0c2d3e4f5a6b");

        Assert.Equal("AVS-3F9A1C7B2D", first);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("---")]
    public void BuildReferenceNumber_ReturnsNullWithoutUsableId(string? id)
    {
        Assert.Null(AvailabilitySearchRules.BuildReferenceNumber(id));
    }

    [Fact]
    public void Flatten_BackfillsReferenceForExistingSearchesOnly()
    {
        var existingSearch = new ApplicationInfo { ApplicationType = FormApplicationTypes.AvailabilitySearch };
        var otherType = new ApplicationInfo { ApplicationType = FormApplicationTypes.StatusSearch };
        var stored = AvailabilitySearchRules.CreateApplication("STORED", "RRR2");
        var storedReference = stored.ReferenceNumber;
        var user = new AppUser { Id = "u1", OtherApplications = [existingSearch, otherType, stored] };

        var results = OtherApplicationsHistoryRules.Flatten([user]);

        Assert.Equal(AvailabilitySearchRules.BuildReferenceNumber(existingSearch.id), results[0].ReferenceNumber);
        Assert.Null(results[1].ReferenceNumber);
        Assert.Equal(storedReference, results[2].ReferenceNumber);
    }

    [Fact]
    public void FlattenApplications_SkipsUsersWithoutApplications()
    {
        var search = new ApplicationInfo { ApplicationType = FormApplicationTypes.AvailabilitySearch };

        var results = OtherApplicationsHistoryRules.FlattenApplications([null, [], [search]]);

        Assert.Single(results);
    }
}
