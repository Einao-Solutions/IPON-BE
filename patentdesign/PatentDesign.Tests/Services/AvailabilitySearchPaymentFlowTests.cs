using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using patentdesign.Controllers;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using Xunit;

namespace patentdesign.Tests.Services;

public class AvsFlowFixture : IDisposable
{
    public IMongoDatabase Db { get; }
    public FakeRemita Remita { get; }
    public Mock<NotificationServices> Notifications { get; }
    public FilesServices Files { get; }
    public UsersService Users { get; }
    private readonly MongoClient _client = new("mongodb://localhost:27017");
    private readonly string _dbName = "pd_avs_it_" + Guid.NewGuid().ToString("N");

    public AvsFlowFixture() : this(null, null) { }

    protected AvsFlowFixture(bool? simulate, string? environment)
    {
        Db = _client.GetDatabase(_dbName);
        var settings = Options.Create(new PatentDesignDBSettings
        {
            DatabaseName = _dbName,
            FilesCollectionName = "files",
            AssignmentCollectionName = "assignments",
            CountersCollectionName = "counters",
            TicketCollectionName = "tickets",
            UsersCollectionName = "users",
            FinanceCollectionName = "finance",
            AttachmentCollectionName = "attachments",
            OppositionCollectionName = "opposition",
            CounterStatementsCollectionName = "counterStatements",
            StatutoryDeclarationsCollectionName = "statutory"
        });
        Remita = new FakeRemita();
        var paymentService = new PaymentService(Db, settings, Remita, NullLogger<PaymentService>.Instance);
        var publication = new PublicationServices(Db, null!, null!, NullLogger<AuthServices>.Instance, null!, Remita);
        Notifications = new Mock<NotificationServices>(Mock.Of<IHubContext<NotificationHub>>(), Db,
            NullLogger<NotificationServices>.Instance, null!);
        Notifications.Setup(n => n.CreateNotificationAsync(It.IsAny<CreateNotificationDto>())).Returns(Task.CompletedTask);
        var settingsData = new Dictionary<string, string?>();
        if (simulate != null)
        {
            settingsData["AvailabilitySearch:SimulatePaymentConfirmation"] = simulate.ToString();
        }
        var configSource = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = settingsData };
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Add(configSource).Build();
        var hostEnv = new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>();
        hostEnv.SetupGet(e => e.EnvironmentName).Returns(environment ?? "Production");
        Files = new FilesServices(Db, settings, Remita, NullLogger<FilesServices>.Instance, paymentService, publication, Notifications.Object, config, hostEnv.Object);
        Users = new UsersService(Db, settings, NullLogger<UsersService>.Instance);
    }

    public async Task<string> CreateUser()
    {
        var id = Guid.NewGuid().ToString();
        await Db.GetCollection<AppUser>("appUsers").InsertOneAsync(new AppUser { Id = id, OtherApplications = [] });
        return id;
    }

    public FilesController FilesController(string? userId)
    {
        var controller = new FilesController(Files);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId) } };
        return controller;
    }

    public UsersController UsersController(string userId)
    {
        var controller = new UsersController(Users);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId) } };
        return controller;
    }

    private static ClaimsPrincipal Principal(string? userId) => userId == null
        ? new ClaimsPrincipal(new ClaimsIdentity())
        : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, "User")], "test"));

    public void Dispose() => _client.DropDatabase(_dbName);
}

public class FakeRemita() : PaymentUtils(Options.Create(new PaymentInfo
{
    AvailabilitySearchCost = "1000",
    AvailabilitySearchServiceFee = "100",
    AvailabilitySearchServiceID = "svc"
}), NullLogger<PaymentUtils>.Instance)
{
    private int _counter = 1000;
    public ConcurrentDictionary<string, string> Statuses { get; } = new();
    public int VerifyCalls;
    public bool Unreachable;
    public int Amount = 1000;

    public override Task<string?> GenerateRemitaPaymentId(string total, string serviceFee, string serviceId, string description,
        string applicantName, string applicantEmail, string applicantNumber, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>("RRR" + Interlocked.Increment(ref _counter));
    public override async Task<RemitaResponseClass?> GetDetailsByRRR(string rrr)
    {
        Interlocked.Increment(ref VerifyCalls);
        await Task.Delay(50);
        if (Unreachable) return null;
        amount = Amount };
    }
}

public class AvailabilitySearchPaymentFlowTests(AvsFlowFixture f) : IClassFixture<AvsFlowFixture>
{
    private static T Body<T>(IActionResult result, out int status)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        status = obj.StatusCode ?? 200;
        return (T)obj.Value!;
    }

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<(string user, string rrr)> Create(string term = "term")
    {
        var user = await f.CreateUser();
        var res = await f.FilesController(user).AvailabilitySearchCost("n", "e@x.com", user, term);
        var dto = Body<AvailabilitySearchDto>(res, out _);
        return (user, dto.rrr);
    }

    private async Task<(int status, JsonElement body)> Confirm(string? user, string? rrr)
    {
        var res = await f.FilesController(user).UpdateAvailabilitySearchPayment(rrr, null, null!, f.Users, null!, NullLogger<FilesController>.Instance);
        if (res is UnauthorizedResult) return (401, default);
        if (res is ForbidResult) return (403, default);
        var body = Body<object>(res, out var status);
        return (status, Json(body));
    }

    private async Task<JsonElement> List(string user)
    {
        var res = await f.UsersController(user).GetOtherApplications(user);
        return Json(Body<object>(res, out _));
    }

    [Fact]
    public async Task Cost_PersistsRecordBeforePayment_AndListsIt()
    {
        var (user, rrr) = await Create("brand");
        var items = (await List(user)).EnumerateArray().ToList();
        var item = Assert.Single(items);
        Assert.Equal(29, item.GetProperty("applicationType").GetInt32());
        Assert.Equal((int)ApplicationStatuses.AwaitingPayment, item.GetProperty("currentStatus").GetInt32());
        Assert.Equal(rrr, item.GetProperty("paymentId").GetString());
        Assert.Equal("brand", item.GetProperty("title").GetString());
        Assert.Equal(1, item.GetProperty("statusHistory").GetArrayLength());
    }

    [Fact]
    public async Task Cost_RejectsMismatchedUserId()
    {
        var user = await f.CreateUser();
        var res = await f.FilesController(user).AvailabilitySearchCost("n", "e", "someone-else", "t");
        Assert.IsType<ForbidResult>(res);
    }

    [Fact]
    public async Task Confirm_Success_MovesToAutoApproved_WithOneHistoryEntry()
    {
        var (user, rrr) = await Create();
        f.Remita.Statuses[rrr] = "00";
        var (status, body) = await Confirm(user, rrr);
        Assert.True(status == 200, body.ToString());
        Assert.True(body.GetProperty("success").GetBoolean());
        var item = Assert.Single((await List(user)).EnumerateArray());
        Assert.Equal((int)ApplicationStatuses.AutoApproved, item.GetProperty("currentStatus").GetInt32());
        Assert.Equal(2, item.GetProperty("statusHistory").GetArrayLength());
    }

    [Fact]
    public async Task Confirm_TwiceAndConcurrently_AppliesOnce()
    {
        var (user, rrr) = await Create();
        f.Remita.Statuses[rrr] = "00";
        f.Notifications.Invocations.Clear();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Confirm(user, rrr)));
        Assert.All(results, r => { Assert.Equal(200, r.status); Assert.True(r.body.GetProperty("success").GetBoolean()); });
        var again = await Confirm(user, rrr);
        Assert.Equal(200, again.status);
        Assert.Equal("Availability search payment already processed successfully", again.body.GetProperty("message").GetString());
        var item = Assert.Single((await List(user)).EnumerateArray());
        Assert.Equal(2, item.GetProperty("statusHistory").GetArrayLength());
        f.Notifications.Verify(n => n.CreateNotificationAsync(It.IsAny<CreateNotificationDto>()), Times.Once);
    }

    [Theory]
    [InlineData("021")]
    [InlineData("01")]
    public async Task Confirm_NotPaid_Returns400_AndStaysAwaiting(string code)
    {
        var (user, rrr) = await Create();
        f.Remita.Statuses[rrr] = code;
        var (status, body) = await Confirm(user, rrr);
        Assert.Equal(400, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal($"Unsuccessful payment - status: {code}", body.GetProperty("message").GetString());
        var item = Assert.Single((await List(user)).EnumerateArray());
        Assert.Equal((int)ApplicationStatuses.AwaitingPayment, item.GetProperty("currentStatus").GetInt32());
        Assert.Equal(1, item.GetProperty("statusHistory").GetArrayLength());
    }

    [Fact]
    public async Task Confirm_UnknownRrr_Returns404()
    {
        var user = await f.CreateUser();
        var (status, body) = await Confirm(user, "nope");
        Assert.Equal(404, status);
        Assert.Equal("Payment ID not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Confirm_AnotherUsersRrr_Returns404_AndDoesNotChangeRecord()
    {
        var (_, rrr) = await Create();
        f.Remita.Statuses[rrr] = "00";
        var attacker = await f.CreateUser();
        var (status, _) = await Confirm(attacker, rrr);
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task Confirm_WithoutToken_Returns401()
    {
        var (status, _) = await Confirm(null, "x");
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task Confirm_RemitaUnreachable_Returns503_AndStaysAwaiting()
    {
        var (user, rrr) = await Create();
        f.Remita.Unreachable = true;
        try
        {
            var (status, body) = await Confirm(user, rrr);
            Assert.Equal(503, status);
            Assert.False(body.GetProperty("success").GetBoolean());
        }
        finally { f.Remita.Unreachable = false; }
        var item = Assert.Single((await List(user)).EnumerateArray());
        Assert.Equal((int)ApplicationStatuses.AwaitingPayment, item.GetProperty("currentStatus").GetInt32());
    }

    [Fact]
    public async Task Confirm_LegacyAppIdBody_StillWorks_AndEnforcesOwnership()
    {
        var (user, rrr) = await Create();
        f.Remita.Statuses[rrr] = "00";
        var appId = Assert.Single((await List(user)).EnumerateArray()).GetProperty("id").GetString();
        var attacker = await f.CreateUser();
        var denied = await f.FilesController(attacker).UpdateAvailabilitySearchPayment(null,
            new AvailabilitySearchPaymentStatusDto { AppId = appId, UserId = attacker }, null!, f.Users, null!, NullLogger<FilesController>.Instance);
        Body<object>(denied, out var deniedStatus);
        Assert.Equal(404, deniedStatus);
        var ok = await f.FilesController(user).UpdateAvailabilitySearchPayment(null,
            new AvailabilitySearchPaymentStatusDto { AppId = appId, UserId = user }, null!, f.Users, null!, NullLogger<FilesController>.Instance);
        Body<object>(ok, out var okStatus);
        Assert.Equal(200, okStatus);
    }

    [Fact]
    public async Task GetOtherApplications_IsolatesUsers()
    {
        var (a, _) = await Create("A-search");
        var (b, _) = await Create("B-search");
        var listA = (await List(a)).EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        Assert.Equal(["A-search"], listA);
        Assert.DoesNotContain("B-search", listA);
        Assert.Single((await List(b)).EnumerateArray());
    }
}
