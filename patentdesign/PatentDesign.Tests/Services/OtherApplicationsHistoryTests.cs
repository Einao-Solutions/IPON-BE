using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using patentdesign.Controllers;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using Xunit;

namespace patentdesign.Tests.Services;

public class OtherApplicationsHistoryTests
{
    private const string StaffId = "staff-1";
    private const string OwnerId = "owner-1";

    [Fact]
    public void GetOtherApplications_UserIdQueryParameter_IsOptional()
    {
        var parameter = typeof(UsersController)
            .GetMethod(nameof(UsersController.GetOtherApplications))!
            .GetParameters()
            .Single();

        Assert.True(parameter.HasDefaultValue);
        Assert.Null(parameter.DefaultValue);
        Assert.IsType<FromQueryAttribute>(parameter.GetCustomAttribute<FromQueryAttribute>());
    }

    [Fact]
    public void OwnerHistory_IncludesUnpaidAvailabilitySearch()
    {
        var unpaidSearch = new ApplicationInfo
        {
            ApplicationType = FormApplicationTypes.AvailabilitySearch,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            PaymentId = "rrr-1",
            Title = "Search term"
        };
        var owner = new AppUser { Id = OwnerId, OtherApplications = [unpaidSearch] };

        var results = OtherApplicationsHistoryRules.Flatten([owner]);

        var result = Assert.Single(results);
        Assert.Same(unpaidSearch, result);
        Assert.Equal(ApplicationStatuses.AwaitingPayment, result.CurrentStatus);
    }

    [Theory]
    [InlineData(Roles.Tech)]
    [InlineData(Roles.TrademarkSupport)]
    [InlineData(Roles.PatentDesignSupport)]
    [InlineData(Roles.SuperAdmin)]
    public void StaffWithoutUserId_CanViewAllIncludingOwnAndOtherUsersHistories(Roles role)
    {
        var ownRecord = new ApplicationInfo { ApplicationType = FormApplicationTypes.StatusSearch };
        var otherUsersSearch = new ApplicationInfo
        {
            ApplicationType = FormApplicationTypes.AvailabilitySearch,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            PaymentId = "rrr-2",
            Title = "Other user's search"
        };
        var caller = CreateCaller(StaffId, role);
        var users = new[]
        {
            new AppUser { Id = StaffId, OtherApplications = [ownRecord] },
            new AppUser { Id = OwnerId, OtherApplications = [otherUsersSearch] }
        };

        Assert.True(OtherApplicationsHistoryRules.CanViewAll(caller));
        var results = OtherApplicationsHistoryRules.Flatten(users);

        Assert.Equal(2, results.Count);
        Assert.Contains(ownRecord, results);
        Assert.Contains(otherUsersSearch, results);
    }

    [Theory]
    [InlineData(Roles.Tech)]
    [InlineData(Roles.TrademarkSupport)]
    [InlineData(Roles.PatentDesignSupport)]
    [InlineData(Roles.SuperAdmin)]
    public void StaffRequestingOwnUserId_UsesAllHistoryBehavior(Roles role)
    {
        var caller = CreateCaller(StaffId, role);

        Assert.True(OtherApplicationsHistoryRules.ShouldViewAll(StaffId, caller));
    }

    [Fact]
    public void SuperAdminRequestingDifferentUserId_RetainsFilteredBehavior()
    {
        var caller = CreateCaller(StaffId, Roles.SuperAdmin);

        Assert.False(OtherApplicationsHistoryRules.ShouldViewAll(OwnerId, caller));
        Assert.True(OtherApplicationsHistoryRules.CanViewRequestedOwner(OwnerId, caller));
    }

    [Fact]
    public void RegularUserRequestingOwnUserId_DoesNotUseAllHistoryBehavior()
    {
        var caller = CreateCaller(OwnerId, Roles.User);

        Assert.False(OtherApplicationsHistoryRules.ShouldViewAll(OwnerId, caller));
        Assert.False(OtherApplicationsHistoryRules.CanViewRequestedOwner(StaffId, caller));
    }

    [Fact]
    public void RegularUserWithoutUserId_CannotViewAll()
    {
        var caller = CreateCaller(StaffId, Roles.User);

        Assert.False(OtherApplicationsHistoryRules.CanViewAll(caller));
    }

    [Fact]
    public void UnrelatedStaffRoleWithoutUserId_CannotViewAll()
    {
        var caller = CreateCaller(StaffId, Roles.Staff);

        Assert.False(OtherApplicationsHistoryRules.CanViewAll(caller));
    }

    [Fact]
    public void UnauthenticatedPrincipal_CannotViewAll()
    {
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, nameof(Roles.SuperAdmin))
        ]));

        Assert.False(OtherApplicationsHistoryRules.CanViewAll(caller));
    }

    [Fact]
    public void UserIdRequest_PreservesOwnerAndSuperAdminAuthorization()
    {
        var owner = CreateCaller(OwnerId, Roles.User);
        var staff = CreateCaller(StaffId, Roles.Tech);
        var superAdmin = CreateCaller(StaffId, Roles.SuperAdmin);

        Assert.True(OtherApplicationsHistoryRules.CanViewRequestedOwner(OwnerId, owner));
        Assert.False(OtherApplicationsHistoryRules.CanViewRequestedOwner(OwnerId, staff));
        Assert.True(OtherApplicationsHistoryRules.CanViewRequestedOwner(OwnerId, superAdmin));
    }

    [Fact]
    public void AvailabilitySearchHistory_SerializesFrontendCompatibleFields()
    {
        var application = new ApplicationInfo
        {
            ApplicationType = FormApplicationTypes.AvailabilitySearch,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            PaymentId = "rrr-3",
            ApplicationDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Title = "Trademark search",
            StatusHistory = [new ApplicationHistory { afterStatus = ApplicationStatuses.AwaitingPayment }]
        };

        var json = JsonSerializer.Serialize(application, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var fields = document.RootElement;

        Assert.Equal((int)FormApplicationTypes.AvailabilitySearch,
            fields.GetProperty("applicationType").GetInt32());
        Assert.Equal((int)ApplicationStatuses.AwaitingPayment,
            fields.GetProperty("currentStatus").GetInt32());
        Assert.Equal("rrr-3", fields.GetProperty("paymentId").GetString());
        Assert.Equal("Trademark search", fields.GetProperty("title").GetString());
        Assert.True(fields.TryGetProperty("applicationDate", out _));
        Assert.True(fields.TryGetProperty("statusHistory", out _));
    }

    [Theory]
    [InlineData("2837bddd-2d4b-4919-bf2f-7a1f41049f61", false)]
    [InlineData("65a1b2c3d4e5f6a7b8c9d0e1", false)]
    [InlineData("65a1b2c3d4e5f6a7b8c9d0e1", true)]
    public async Task Endpoint_OwnerGetsExistingAvailabilitySearch_WithCompatibleResponse(string ownerId, bool objectId)
    {
        BsonValue storedId = objectId ? new BsonObjectId(ObjectId.Parse(ownerId)) : new BsonString(ownerId);
        var ownDocument = UserDocument(storedId, "own-search");
        var before = ownDocument.ToJson();
        var store = new HistoryStore(ownDocument, UserDocument(StaffId, "other-search"));

        var response = await store.Controller(CreateCaller(ownerId, Roles.User)).GetOtherApplications(ownerId);

        var applications = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(response).Value);
        var application = Assert.Single(applications);
        Assert.Equal("own-search", application.id);
        Assert.Equal(FormApplicationTypes.AvailabilitySearch, application.ApplicationType);
        Assert.Equal(ApplicationStatuses.AwaitingPayment, application.CurrentStatus);
        Assert.Equal(before, ownDocument.ToJson());
        var filter = Assert.Single(store.Filters);
        if (ObjectId.TryParse(ownerId, out var parsed))
        {
            Assert.Equal(new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("_id", ownerId), new BsonDocument("_id", parsed)
            }), filter);
        }
        else
        {
            Assert.Equal(new BsonDocument("_id", ownerId), filter);
        }

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(applications,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        var fields = json.RootElement[0];
        Assert.Equal("own-search", fields.GetProperty("id").GetString());
        Assert.Equal("Search own-search", fields.GetProperty("title").GetString());
        Assert.Equal("rrr-own-search", fields.GetProperty("paymentId").GetString());
        Assert.Equal((int)FormApplicationTypes.AvailabilitySearch, fields.GetProperty("applicationType").GetInt32());
        Assert.Equal((int)ApplicationStatuses.AwaitingPayment, fields.GetProperty("currentStatus").GetInt32());
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), fields.GetProperty("applicationDate").GetDateTime());
        Assert.Equal(JsonValueKind.Array, fields.GetProperty("statusHistory").ValueKind);
    }

    [Theory]
    [InlineData(Roles.Tech)]
    [InlineData(Roles.TrademarkSupport)]
    [InlineData(Roles.PatentDesignSupport)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Endpoint_PrivilegedOwnId_ReturnsOwnAndAllOtherUsersSearches(Roles role)
    {
        var documents = new[]
        {
            UserDocument(StaffId, "staff-search"),
            UserDocument(OwnerId, "owner-search"),
            UserDocument(ObjectId.Parse("65a1b2c3d4e5f6a7b8c9d0e1"), "legacy-search"),
            new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "OtherApplications", BsonNull.Value } }
        };
        var before = documents.Select(document => document.ToJson()).ToArray();
        var store = new HistoryStore(documents);

        var response = await store.Controller(CreateCaller(StaffId, role)).GetOtherApplications(StaffId);

        var applications = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(new[] { "staff-search", "owner-search", "legacy-search" }, applications.Select(a => a.id));
        Assert.All(applications, a =>
        {
            Assert.Equal(FormApplicationTypes.AvailabilitySearch, a.ApplicationType);
            Assert.Equal(ApplicationStatuses.AwaitingPayment, a.CurrentStatus);
        });
        // Staff listing only reads accounts that actually have applications.
        Assert.True(Assert.Single(store.Filters).Contains("OtherApplications.0"));
        Assert.Equal(before, documents.Select(document => document.ToJson()).ToArray());
    }

    [Theory]
    [InlineData(Roles.User)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Finance)]
    [InlineData(Roles.TrademarkRegistrar)]
    public async Task Endpoint_UnprivilegedOwnId_IsFilteredEvenWithStaffRoleInStoredDocument(Roles role)
    {
        var document = UserDocument(OwnerId, "own-search");
        document["UserRoles"] = new BsonArray { (int)Roles.SuperAdmin };
        var store = new HistoryStore(document, UserDocument(StaffId, "other-search"));

        var response = await store.Controller(CreateCaller(OwnerId, role)).GetOtherApplications(OwnerId);

        var applications = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("own-search", Assert.Single(applications).id);
        Assert.Equal(new BsonDocument("_id", OwnerId), Assert.Single(store.Filters));
    }

    [Theory]
    [InlineData(Roles.User, StaffId)]
    [InlineData(Roles.User, null)]
    [InlineData(Roles.Staff, null)]
    [InlineData(Roles.Finance, StaffId)]
    [InlineData(Roles.Tech, StaffId)]
    [InlineData(Roles.TrademarkSupport, StaffId)]
    [InlineData(Roles.PatentDesignSupport, StaffId)]
    public async Task Endpoint_UnauthorizedScope_IsForbiddenBeforeReadingMongo(Roles role, string? requestedId)
    {
        var store = new HistoryStore(UserDocument(StaffId, "other-search"));

        var response = await store.Controller(CreateCaller(OwnerId, role)).GetOtherApplications(requestedId);

        Assert.IsType<ForbidResult>(response);
        Assert.Empty(store.Filters);
    }

    [Fact]
    public async Task Endpoint_SuperAdminTargetingAnotherOwner_PreservesFilteredBehavior()
    {
        var store = new HistoryStore(UserDocument(OwnerId, "owner-search"), UserDocument(StaffId, "staff-search"));

        var response = await store.Controller(CreateCaller(StaffId, Roles.SuperAdmin)).GetOtherApplications(OwnerId);

        var applications = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("owner-search", Assert.Single(applications).id);
        Assert.Equal(new BsonDocument("_id", OwnerId), Assert.Single(store.Filters));
    }

    [Fact]
    public async Task Endpoint_ExistingOwnerWithoutHistory_ReturnsEmptyArray()
    {
        var store = new HistoryStore(new BsonDocument { { "_id", OwnerId }, { "OtherApplications", BsonNull.Value } });

        var response = await store.Controller(CreateCaller(OwnerId, Roles.User)).GetOtherApplications(OwnerId);

        Assert.Empty(Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(response).Value));
    }

    [Fact]
    public async Task Endpoint_MissingOwner_RemainsNotFound()
    {
        var store = new HistoryStore();

        var response = await store.Controller(CreateCaller(OwnerId, Roles.User)).GetOtherApplications(OwnerId);

        Assert.IsType<NotFoundObjectResult>(response);
    }

    [Fact]
    public async Task Endpoint_RequiresAuthorizationAndCallerId()
    {
        Assert.NotNull(typeof(UsersController).GetMethod(nameof(UsersController.GetOtherApplications))!
            .GetCustomAttribute<AuthorizeAttribute>());
        var store = new HistoryStore();

        var response = await store.Controller(new ClaimsPrincipal(new ClaimsIdentity())).GetOtherApplications(OwnerId);

        Assert.IsType<UnauthorizedResult>(response);
        Assert.Empty(store.Filters);
    }

    [Theory]
    [InlineData(Roles.Tech)]
    [InlineData(Roles.TrademarkSupport)]
    [InlineData(Roles.PatentDesignSupport)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Endpoint_StaffWithUserIdOmitted_ReturnsAllUsersApplications(Roles role)
    {
        var store = new HistoryStore(UserDocument(StaffId, "staff-search"), UserDocument(OwnerId, "owner-search"));

        var response = await store.Controller(CreateCaller(StaffId, role)).GetOtherApplications();

        var applications = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(new[] { "staff-search", "owner-search" }, applications.Select(a => a.id));
    }

    [Fact]
    public async Task Endpoint_SearchCreatedByStaffA_AppearsInStaffBListAndOwnList()
    {
        const string staffA = "staff-a";
        const string staffB = "staff-b";
        var search = AvailabilitySearchRules.CreateApplication("acme", "rrr-acme");
        var staffADocument = new BsonDocument
        {
            { "_id", staffA }, { "OtherApplications", new BsonArray { search.ToBsonDocument() } }
        };
        var store = new HistoryStore(staffADocument, UserDocument(staffB, "b-search"));

        var forB = await store.Controller(CreateCaller(staffB, Roles.Tech)).GetOtherApplications(staffB);
        var forA = await store.Controller(CreateCaller(staffA, Roles.TrademarkSupport)).GetOtherApplications(staffA);

        var listB = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(forB).Value);
        var listA = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(forA).Value);
        Assert.Contains(listB, a => a.id == search.id);
        Assert.Contains(listA, a => a.id == search.id);
    }

    [Fact]
    public async Task Endpoint_NewAwaitingPaymentSearch_AppearsInOwnerAndStaffLists()
    {
        var search = AvailabilitySearchRules.CreateApplication("new term", "rrr-new");
        var ownerDocument = new BsonDocument
        {
            { "_id", OwnerId }, { "OtherApplications", new BsonArray { search.ToBsonDocument() } }
        };
        var store = new HistoryStore(ownerDocument, UserDocument(StaffId, "staff-search"));

        var forOwner = await store.Controller(CreateCaller(OwnerId, Roles.User)).GetOtherApplications(OwnerId);
        var forStaff = await store.Controller(CreateCaller(StaffId, Roles.Tech)).GetOtherApplications(StaffId);

        var ownerList = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(forOwner).Value);
        var created = Assert.Single(ownerList);
        Assert.Equal(ApplicationStatuses.AwaitingPayment, created.CurrentStatus);
        Assert.Equal("rrr-new", created.PaymentId);
        Assert.Equal("new term", created.Title);
        var staffList = Assert.IsType<List<ApplicationInfo>>(Assert.IsType<OkObjectResult>(forStaff).Value);
        Assert.Contains(staffList, a => a.id == search.id);
    }

    private static BsonDocument UserDocument(BsonValue id, string applicationId) => new()
    {
        { "_id", id },
        { "OtherApplications", new BsonArray
            {
                new ApplicationInfo
                {
                    id = applicationId,
                    ApplicationType = FormApplicationTypes.AvailabilitySearch,
                    CurrentStatus = ApplicationStatuses.AwaitingPayment,
                    PaymentId = "rrr-" + applicationId,
                    Title = "Search " + applicationId,
                    ApplicationDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    StatusHistory = []
                }.ToBsonDocument()
            }
        }
    };

    private sealed class HistoryStore
    {
        private readonly UsersService _service;
        public List<BsonDocument> Filters { get; } = new();

        public HistoryStore(params BsonDocument[] documents)
        {
            var collection = new Mock<IMongoCollection<AppUser>>(MockBehavior.Strict);
            collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<AppUser>>(),
                    It.IsAny<FindOptions<AppUser, List<ApplicationInfo>>>(), It.IsAny<CancellationToken>()))
                .Returns<FilterDefinition<AppUser>, FindOptions<AppUser, List<ApplicationInfo>>, CancellationToken>((filter, _, _) =>
                {
                    var rendered = filter.Render(BsonSerializer.LookupSerializer<AppUser>(), BsonSerializer.SerializerRegistry);
                    Filters.Add(rendered);
                    var applicationLists = documents.Where(document => Matches(rendered, document))
                        .Select(document => BsonSerializer.Deserialize<AppUser>(document).OtherApplications).ToList();
                    var cursor = new Mock<IAsyncCursor<List<ApplicationInfo>>>();
                    cursor.SetupGet(c => c.Current).Returns(applicationLists);
                    cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
                    return Task.FromResult(cursor.Object);
                });
            var database = new Mock<IMongoDatabase>();
            database.Setup(db => db.GetCollection<AppUser>("appUsers", It.IsAny<MongoCollectionSettings>()))
                .Returns(collection.Object);
            _service = new UsersService(database.Object, Options.Create(new PatentDesignDBSettings()), NullLogger<UsersService>.Instance);
        }

        public UsersController Controller(ClaimsPrincipal caller) => new(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = caller } }
        };

        private static bool Matches(BsonDocument filter, BsonDocument document) =>
            filter.ElementCount == 0 ||
            (filter.TryGetValue("$or", out var alternatives)
                ? alternatives.AsBsonArray.Any(alternative => Matches(alternative.AsBsonDocument, document))
                : filter.Contains("OtherApplications.0")
                    ? document.TryGetValue("OtherApplications", out var applications) &&
                      applications.IsBsonArray && applications.AsBsonArray.Count > 0
                    : filter["_id"] == document["_id"]);
    }

    private static ClaimsPrincipal CreateCaller(string userId, Roles role) => new(
        new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role.ToString())
        ],
        authenticationType: "test"));
}
