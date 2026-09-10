using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using Resend;

namespace PatentDesign.Tests;

public class EmailDeliveryTests
{
    public static IEnumerable<object[]> TemplateMappings => new[]
    {
        new object[] { EmailType.Opposition, "RESEND_TEMPLATE_OPPOSITION" },
        new object[] { EmailType.RenewalEarlyReminder, "RESEND_TEMPLATE_RENEWAL_EARLY_REMINDER" },
        new object[] { EmailType.RenewalDueNotice, "RESEND_TEMPLATE_RENEWAL_DUE_NOTICE" },
        new object[] { EmailType.CounterStatement, "RESEND_TEMPLATE_COUNTER_STATEMENT" },
        new object[] { EmailType.OppositionConfirmation, "RESEND_TEMPLATE_OPPOSITION_CONFIRMATION" },
        new object[] { EmailType.StatutoryDeclaration, "RESEND_TEMPLATE_STATUTORY_DECLARATION" },
        new object[] { EmailType.WithdrawalNotification, "RESEND_TEMPLATE_WITHDRAWAL_NOTIFICATION" },
        new object[] { EmailType.WithdrawalApproved, "RESEND_TEMPLATE_WITHDRAWAL_APPROVED" },
        new object[] { EmailType.WithdrawalRefused, "RESEND_TEMPLATE_WITHDRAWAL_REFUSED" },
        new object[] { EmailType.WithdrawalApprovedApplicant, "RESEND_TEMPLATE_WITHDRAWAL_APPROVED_APPLICANT" },
        new object[] { EmailType.WithdrawalRefusedApplicant, "RESEND_TEMPLATE_WITHDRAWAL_REFUSED_APPLICANT" },
        new object[] { EmailType.ResetPassword, "RESEND_TEMPLATE_RESET_PASSWORD" },
        new object[] { EmailType.WelcomeVerification, "RESEND_TEMPLATE_WELCOMEVERIFICATION" },
        new object[] { EmailType.StatusUpdate, "RESEND_TEMPLATE_STATUS_UPDATE" }
    };

    [Theory]
    [MemberData(nameof(TemplateMappings))]
    public async Task SendMail_UsesConfiguredTemplateKey(EmailType type, string key)
    {
        using var context = new EmailContext();
        context.Configuration[key] = "alias-for-this-template";
        await context.Service.SendMail(NewEmail(type));
        using var body = JsonDocument.Parse(Assert.Single(context.Handler.Bodies));
        Assert.Equal("alias-for-this-template", body.RootElement.GetProperty("template").GetProperty("id").GetString());
        Assert.Equal("Test subject", body.RootElement.GetProperty("subject").GetString());
        Assert.Equal("IPO Nigeria <noreply@example.com>", body.RootElement.GetProperty("from").GetString());
    }

    [Fact]
    public void ValidateConfiguration_AcceptsCompleteSettings()
    {
        using var context = new EmailContext();
        EmailServices.ValidateConfiguration(context.Configuration);
    }

    [Theory]
    [MemberData(nameof(TemplateMappings))]
    public void ValidateConfiguration_RejectsMissingTemplate(EmailType type, string key)
    {
        using var context = new EmailContext();
        context.Configuration[key] = null;
        var error = Assert.Throws<InvalidOperationException>(() => EmailServices.ValidateConfiguration(context.Configuration));
        Assert.Contains(key, error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("tmpl_xxxxxxxxx")]
    [InlineData("$(RESEND_TEMPLATE_STATUS_UPDATE)")]
    [InlineData("${RESEND_TEMPLATE_STATUS_UPDATE}")]
    public void ValidateConfiguration_RejectsPlaceholders(string value)
    {
        using var context = new EmailContext();
        context.Configuration["RESEND_TEMPLATE_STATUS_UPDATE"] = value;
        Assert.Throws<InvalidOperationException>(() => EmailServices.ValidateConfiguration(context.Configuration));
    }

    [Theory]
    [InlineData("RESEND_APIKEY", "")]
    [InlineData("RESEND_FROM_EMAIL", "not-an-email")]
    public void ValidateConfiguration_RejectsInvalidProviderSettings(string key, string value)
    {
        using var context = new EmailContext();
        context.Configuration[key] = value;
        Assert.Throws<InvalidOperationException>(() => EmailServices.ValidateConfiguration(context.Configuration));
    }

    [Fact]
    public async Task SendMail_RejectsInvalidRecipientBeforeTransport()
    {
        using var context = new EmailContext();
        var email = NewEmail(EmailType.StatusUpdate);
        email.To = "not-an-email";
        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.SendMail(email));
        Assert.Empty(context.Handler.Bodies);
    }

    [Fact]
    public async Task SendMail_RejectsMissingPayloadBeforeTransport()
    {
        using var context = new EmailContext();
        var email = NewEmail(EmailType.StatusUpdate);
        email.StatusUpdateMail = null;
        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.SendMail(email));
        Assert.Empty(context.Handler.Bodies);
    }

    [Fact]
    public async Task SendMail_DoesNotTreatFailedProviderResponseAsSuccess()
    {
        using var context = new EmailContext();
        context.Handler.StatusCodes.Enqueue(HttpStatusCode.UnprocessableEntity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.SendMail(NewEmail(EmailType.StatusUpdate)));
    }

    [Fact]
    public async Task SendMail_PassesIdempotencyKey()
    {
        using var context = new EmailContext();
        await context.Service.SendMail(NewEmail(EmailType.StatusUpdate), "notification-123");
        Assert.Equal("notification-123", Assert.Single(context.Handler.IdempotencyKeys));
    }

    [Fact]
    public async Task RenewalVariables_IncludeDateAliasesAndSupportedTypes()
    {
        using var context = new EmailContext();
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var email = NewEmail(EmailType.RenewalEarlyReminder);
            email.RenewalReminder = new RenewalReminder
            {
                RenewalDue = new DateTime(2030, 2, 3, 0, 0, 0, DateTimeKind.Utc),
                IsExpiryDay = false,
                Type = FileTypes.Patent,
                Class = 7
            };
            await context.Service.SendMail(email);
            using var body = JsonDocument.Parse(Assert.Single(context.Handler.Bodies));
            var variables = body.RootElement.GetProperty("template").GetProperty("variables");
            foreach (var name in new[] { "RenewalDue", "DueDate", "ExpiryDate" })
            {
                Assert.Equal("03 February 2030", variables.GetProperty(name).GetString());
            }
            Assert.Equal("false", variables.GetProperty("IsExpiryDay").GetString());
            Assert.Equal("Patent", variables.GetProperty("Type").GetString());
            Assert.Equal(7, variables.GetProperty("Class").GetInt32());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task SendMail_PreservesNestedBodyWhenOuterBodyIsMissing()
    {
        using var context = new EmailContext();
        var email = NewEmail(EmailType.Opposition);
        email.OppositionMail!.Body = "Opposition details";
        await context.Service.SendMail(email);
        using var body = JsonDocument.Parse(Assert.Single(context.Handler.Bodies));
        Assert.Equal("Opposition details", body.RootElement.GetProperty("template").GetProperty("variables").GetProperty("Body").GetString());
    }

    [Fact]
    public void NotificationDeliveryState_RoundTripsThroughMongoButIsHiddenFromApi()
    {
        var notification = new Notification
        {
            EmailPayload = JsonSerializer.Serialize(NewEmail(EmailType.StatusUpdate)),
            EmailAttempts = 2,
            EmailLeaseId = "lease",
            EmailNextAttemptAt = DateTime.UtcNow,
            RenewalDueDate = new DateTime(2030, 2, 3, 0, 0, 0, DateTimeKind.Utc)
        };
        var saved = BsonSerializer.Deserialize<Notification>(notification.ToBsonDocument());
        Assert.Equal(notification.EmailPayload, saved.EmailPayload);
        Assert.Equal(notification.EmailAttempts, saved.EmailAttempts);
        Assert.Equal(notification.RenewalDueDate, saved.RenewalDueDate);
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain("EmailPayload", json);
        Assert.DoesNotContain("EmailLeaseId", json);
        Assert.DoesNotContain("EmailAttempts", json);
        Assert.DoesNotContain("EmailNextAttemptAt", json);
        Assert.DoesNotContain("EmailSentAt", json);
        Assert.DoesNotContain("RenewalDueDate", json);
    }

    [Fact]
    public void RenewalDeduplication_DistinguishesCycleAndReminderKind()
    {
        var method = typeof(NotificationServices).GetMethod("BuildRenewalReminderId", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Key(DateOnly due, bool expiryDay) => (string)method.Invoke(null, new object[] { "file", "user", due, expiryDay })!;
        var firstCycle = new DateOnly(2030, 2, 3);
        Assert.Equal(Key(firstCycle, false), Key(firstCycle, false));
        Assert.NotEqual(Key(firstCycle, false), Key(firstCycle, true));
        Assert.NotEqual(Key(firstCycle, false), Key(firstCycle.AddYears(1), false));
    }

    [Fact]
    public async Task StatusNotification_PersistsEmailBeforeSendingAndTargetsAccountId()
    {
        using var emailContext = new EmailContext();
        var store = new NotificationStore(emailContext.Service);
        await store.Service.CreateNotificationAsync(new CreateNotificationDto
        {
            Audience = NotificationAudience.User,
            Category = NotificationCategory.StatusUpdate,
            RecipientId = "owner@example.com",
            Title = "Application status changed",
            Message = "Status details",
            ApplicationType = FormApplicationTypes.NewApplication,
            PreviousStatus = ApplicationStatuses.Active,
            NewStatus = ApplicationStatuses.Abandoned
        });
        Assert.NotNull(store.Saved);
        var email = JsonSerializer.Deserialize<EmailDto>(store.Saved!.EmailPayload!)!;
        Assert.Equal(EmailType.StatusUpdate, email.EmailType);
        Assert.Equal("owner@example.com", email.To);
        Assert.Equal("NewApplication", email.StatusUpdateMail!.ApplicationType);
        Assert.Equal("Active", email.StatusUpdateMail.FormerStatus);
        Assert.Equal("Abandoned", email.StatusUpdateMail.NewStatus);
        Assert.Single(emailContext.Handler.Bodies);
        store.HubClients.Verify(x => x.User("account-id"), Times.Once);
        Assert.True(Assert.Single(store.Updates)["$set"].AsBsonDocument.Contains("EmailSentAt"));
    }

    [Fact]
    public async Task FailedEmail_DoesNotPreventProcessingNextPendingEmail()
    {
        using var emailContext = new EmailContext();
        emailContext.Handler.StatusCodes.Enqueue(HttpStatusCode.UnprocessableEntity);
        emailContext.Handler.StatusCodes.Enqueue(HttpStatusCode.OK);
        var store = new NotificationStore(emailContext.Service);
        store.Enqueue("first");
        store.Enqueue("second");
        Assert.Equal(1, await store.Service.RetryPendingEmailsAsync());
        Assert.Equal(2, emailContext.Handler.Bodies.Count);
        Assert.True(store.Updates[0]["$set"].AsBsonDocument.Contains("EmailNextAttemptAt"));
        Assert.False(store.Updates[0]["$set"].AsBsonDocument.Contains("EmailSentAt"));
        Assert.True(store.Updates[1]["$set"].AsBsonDocument.Contains("EmailSentAt"));
        Assert.Equal(new[] { "first", "second" }, emailContext.Handler.IdempotencyKeys);
        Assert.True(store.ClaimFilters.All(x => x.Contains("EmailSentAt") && x.Contains("EmailNextAttemptAt")));
    }

    [Fact]
    public async Task AlreadyClaimedEmail_IsNotSentByAnotherWorker()
    {
        using var emailContext = new EmailContext();
        var store = new NotificationStore(emailContext.Service) { DenyClaims = true };
        store.Enqueue("claimed");
        Assert.Equal(0, await store.Service.RetryPendingEmailsAsync());
        Assert.Empty(emailContext.Handler.Bodies);
        Assert.Empty(store.Updates);
    }

    internal static EmailDto NewEmail(EmailType type) => new()
    {
        To = "owner@example.com",
        Subject = "Test subject",
        EmailType = type,
        OppositionMail = new OppositionMail(),
        RenewalReminder = new RenewalReminder(),
        CounterStatementMail = new CounterStatementMail(),
        OppositionConfirmationMail = new OppositionConfirmationMail(),
        StatutoryDeclarationMail = new StatutoryDeclarationMail(),
        WithdrawalNotificationMail = new WithdrawalNotificationMail(),
        WithdrawalApprovedMail = new WithdrawalApprovedMail(),
        WithdrawalRefusedMail = new WithdrawalRefusedMail(),
        WithdrawalApprovedApplicantMail = new WithdrawalApprovedApplicantMail(),
        WithdrawalRefusedApplicantMail = new WithdrawalRefusedApplicantMail(),
        ResetPasswordMail = new ResetPasswordMail(),
        WelcomeVerificationMail = new WelcomeVerificationMail(),
        StatusUpdateMail = new StatusUpdateMail()
    };

    private sealed class EmailContext : IDisposable
    {
        private readonly HttpClient _httpClient;
        public RecordingHandler Handler { get; } = new();
        public IConfiguration Configuration { get; }
        public EmailServices Service { get; }

        public EmailContext()
        {
            var values = TemplateMappings.ToDictionary(x => (string)x[1], _ => (string?)"test-template");
            values["RESEND_APIKEY"] = "test-key-not-a-real-secret";
            values["RESEND_FROM_EMAIL"] = "noreply@example.com";
            values["RESEND_FROM_NAME"] = "IPO Nigeria";
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var options = new Mock<IOptionsSnapshot<ResendClientOptions>>();
            options.SetupGet(x => x.Value).Returns(new ResendClientOptions
            {
                ApiToken = "test-key-not-a-real-secret",
                ThrowExceptions = false
            });
            _httpClient = new HttpClient(Handler);
            Service = new EmailServices(Options.Create(new EmailSettings()), Configuration,
                new ResendClient(options.Object, _httpClient), NullLogger<EmailServices>.Instance);
        }

        public void Dispose() => _httpClient.Dispose();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public List<string?> IdempotencyKeys { get; } = new();
        public Queue<HttpStatusCode> StatusCodes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            IdempotencyKeys.Add(request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null);
            var status = StatusCodes.Count > 0 ? StatusCodes.Dequeue() : HttpStatusCode.OK;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK
                    ? "{\"id\":\"e2e7ac63-4483-4182-a407-e387f2b45e2d\"}"
                    : "{\"name\":\"validation_error\",\"message\":\"Test rejection\",\"statusCode\":422}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class NotificationStore
    {
        private readonly List<Notification> _pending = new();
        private readonly Queue<Notification> _claims = new();
        public Notification? Saved { get; private set; }
        public bool DenyClaims { get; init; }
        public List<BsonDocument> Updates { get; } = new();
        public List<BsonDocument> ClaimFilters { get; } = new();
        public Mock<IHubClients> HubClients { get; } = new();
        public NotificationServices Service { get; }

        public NotificationStore(EmailServices emailService)
        {
            var notifications = new Mock<IMongoCollection<Notification>>();
            var users = new Mock<IMongoCollection<AppUser>>();
            var database = new Mock<IMongoDatabase>();
            database.Setup(x => x.GetCollection<Notification>("notifications", It.IsAny<MongoCollectionSettings>())).Returns(notifications.Object);
            database.Setup(x => x.GetCollection<AppUser>("appUsers", It.IsAny<MongoCollectionSettings>())).Returns(users.Object);
            database.Setup(x => x.GetCollection<Filling>("files", It.IsAny<MongoCollectionSettings>())).Returns(Mock.Of<IMongoCollection<Filling>>());
            var hub = new Mock<IHubContext<NotificationHub>>();
            hub.SetupGet(x => x.Clients).Returns(HubClients.Object);
            var client = new Mock<IClientProxy>();
            client.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            HubClients.Setup(x => x.User(It.IsAny<string>())).Returns(client.Object);
            users.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<AppUser>>(), It.IsAny<FindOptions<AppUser, AppUser>>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(Cursor(new[] { new AppUser { Id = "account-id", Email = "owner@example.com" } })));
            notifications.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<Notification>>(), It.IsAny<FindOptions<Notification, Notification>>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(Cursor(_pending.ToArray())));
            notifications.Setup(x => x.InsertOneAsync(It.IsAny<Notification>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                .Callback<Notification, InsertOneOptions, CancellationToken>((notification, _, _) => { Saved = notification; _claims.Enqueue(notification); })
                .Returns(Task.CompletedTask);
            notifications.Setup(x => x.FindOneAndUpdateAsync(It.IsAny<FilterDefinition<Notification>>(), It.IsAny<UpdateDefinition<Notification>>(),
                    It.IsAny<FindOneAndUpdateOptions<Notification, Notification>>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Notification>, UpdateDefinition<Notification>, FindOneAndUpdateOptions<Notification, Notification>, CancellationToken>(
                    (filter, _, _, _) => ClaimFilters.Add(filter.Render(BsonSerializer.LookupSerializer<Notification>(), BsonSerializer.SerializerRegistry)))
                .Returns(() => Task.FromResult(DenyClaims ? null! : _claims.Dequeue()));
            notifications.Setup(x => x.UpdateOneAsync(It.IsAny<FilterDefinition<Notification>>(), It.IsAny<UpdateDefinition<Notification>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Notification>, UpdateDefinition<Notification>, UpdateOptions, CancellationToken>(
                    (_, update, _, _) => Updates.Add(update.Render(BsonSerializer.LookupSerializer<Notification>(), BsonSerializer.SerializerRegistry).AsBsonDocument))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
            Service = new NotificationServices(hub.Object, database.Object, NullLogger<NotificationServices>.Instance, emailService);
        }

        public void Enqueue(string id)
        {
            var notification = new Notification
            {
                Id = id,
                EmailPayload = JsonSerializer.Serialize(NewEmail(EmailType.StatusUpdate)),
                EmailNextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
                EmailAttempts = 1
            };
            _pending.Add(notification);
            _claims.Enqueue(notification);
        }

        private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.SetupGet(x => x.Current).Returns(items);
            cursor.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            cursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return cursor.Object;
        }
    }
}
