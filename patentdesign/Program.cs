using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using Serilog;
using System.Security.Authentication;
using System.Text;

// ------------------ Create Builder ------------------
var builder = WebApplication.CreateBuilder(args);

// ------------------ Load .env ONLY in Development ------------------
if (builder.Environment.IsDevelopment())
{
    var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (File.Exists(envPath))
        DotNetEnv.Env.Load(envPath);
}
// ------------------ Serilog ------------------
var logPath = builder.Configuration["PatentDesignDatabase:LogPath"] ?? @"C:\IpoApiLog";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logPath, ".txt"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();
// ------------------ JWT Config ------------------
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "https://portal.iponigeria.com";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "https://portal.iponigeria.com";

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new Exception("JWT_KEY environment variable is missing!");
}

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new Exception("JWT_KEY must be at least 32 bytes!");
}

builder.Configuration["Jwt:Key"] = jwtKey;
builder.Configuration["Jwt:Issuer"] = jwtIssuer;
builder.Configuration["Jwt:Audience"] = jwtAudience;

// ------------------ MongoDB Config ------------------
string? mongoConnectionString;

if (builder.Environment.IsDevelopment())
{
    mongoConnectionString = builder.Configuration["PatentDesignDatabase:ConnectionString"];
    Log.Information("Using local MongoDB connection string for development.");
}
else
{
    mongoConnectionString =
        Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
        ?? builder.Configuration["PatentDesignDatabase:ConnectionStringUp"];

    // Guard against the unresolved ${...} placeholder
    if (string.IsNullOrWhiteSpace(mongoConnectionString) ||
        mongoConnectionString.StartsWith("${"))
    {
        throw new Exception("❌ MongoDB connection string is missing! Check environment variables.");
    }
}
builder.Configuration["PatentDesignDatabase:ConnectionString"] = mongoConnectionString;
builder.Configuration["PatentDesignDatabase:ConnectionStringUp"] = mongoConnectionString;
// ------------------ Redis Cache Config ------------------
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "patentdesign:";
    });
    Log.Information("Redis cache configured with instance name {InstanceName}", "patentdesign:");
}
else
{
    builder.Services.AddDistributedMemoryCache();
    Log.Information("Redis connection string not found. Using in-memory cache.");
}

// ------------------ SMTP Overrides ------------------
var smtpServer = Environment.GetEnvironmentVariable("SMTP_SERVER");
var smtpUsername = Environment.GetEnvironmentVariable("SMTP_USERNAME");
var smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD");

if (!string.IsNullOrWhiteSpace(smtpServer))
    builder.Configuration["EmailSettings:SmtpServer"] = smtpServer;

if (!string.IsNullOrWhiteSpace(smtpUsername))
    builder.Configuration["EmailSettings:Username"] = smtpUsername;

if (!string.IsNullOrWhiteSpace(smtpPassword))
    builder.Configuration["EmailSettings:Password"] = smtpPassword;

// ------------------ CORS ------------------
const string corsPolicy = "AllowPortal";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: corsPolicy, policy =>
    {
        policy
            .WithOrigins(
                "https://portal.iponigeria.com",
                "http://localhost:5173",
                "https://link.einaotest.com"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ------------------ JWT Authentication ------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            )
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// ------------------ QuestPDF ------------------
QuestPDF.Settings.License = LicenseType.Community;
var fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "Certificate.otf");
using var fontStream = File.OpenRead(fontPath);
FontManager.RegisterFont(fontStream);

// ------------------ Mongo Client (single, shared) ------------------
var mongoUrl = new MongoUrl(mongoConnectionString);
var mongoSettings = MongoClientSettings.FromUrl(mongoUrl);
if (!builder.Environment.IsDevelopment())
{
    mongoSettings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 };
}

var mongoClient = new MongoClient(mongoSettings);
var mongoDatabaseName = mongoUrl.DatabaseName
    ?? builder.Configuration["PatentDesignDatabase:DatabaseName"]
    ?? throw new InvalidOperationException("Mongo database name is not configured.");
var mongoDatabase = mongoClient.GetDatabase(mongoDatabaseName);

// Register once; every service injects IMongoDatabase instead of building its own client.
builder.Services.AddSingleton<IMongoClient>(mongoClient);
builder.Services.AddSingleton<IMongoDatabase>(mongoDatabase);

// ------------------ Config Bindings ------------------
builder.Services.Configure<PatentDesignDBSettings>(builder.Configuration.GetSection("PatentDesignDatabase"));
builder.Services.Configure<PaymentInfo>(builder.Configuration.GetSection("PaymentInfo"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// ------------------ Mongo Enum Serializers ------------------
BsonSerializer.RegisterSerializer(typeof(ApplicationStatuses), new EnumSerializer<ApplicationStatuses>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(FileTypes), new EnumSerializer<FileTypes>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(DesignTypes), new EnumSerializer<DesignTypes>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(PerformanceType), new EnumSerializer<PerformanceType>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(PatentApplicationTypes), new EnumSerializer<PatentApplicationTypes>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(PatentTypes), new EnumSerializer<PatentTypes>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(TicketState), new EnumSerializer<TicketState>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(FormApplicationTypes), new EnumSerializer<FormApplicationTypes>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(TradeMarkType), new EnumSerializer<TradeMarkType>(BsonType.String));
BsonSerializer.RegisterSerializer(typeof(TradeMarkLogo), new EnumSerializer<TradeMarkLogo>(BsonType.String));

// ------------------ Controllers & Swagger ------------------
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});
builder.Services.AddProblemDetails();

// ------------------ Services ------------------
//builder.Services.AddSingleton<ILoggerService, LoggerService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<PaymentUtils>();
builder.Services.AddSingleton<OppositionService>();
builder.Services.AddSingleton<FilesServices>();
builder.Services.AddSingleton<LettersServices>();
builder.Services.AddSingleton<TicketServices>();
builder.Services.AddSingleton<UsersService>();
builder.Services.AddSingleton<FinanceService>();
builder.Services.AddSingleton<AssignmentService>();
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<MigrationService>();
builder.Services.AddSingleton<EmailServices>();
builder.Services.AddSingleton<AuthServices>();
builder.Services.AddSingleton<AdminServices>();
builder.Services.AddSingleton<StatisticsService>();
builder.Services.AddSingleton<PublicationServices>();

//------------------- Background Jobs ------------------
builder.Services.AddHostedService<PublishTrademarkJob>();
builder.Services.AddHostedService<OppositionDeadlineService>();

// ------------------ Build App ------------------
var app = builder.Build();

// ------------------ One-off DB backfill ------------------
try
{
    var oppSvc = app.Services.GetRequiredService<OppositionService>();
    await oppSvc.BackfillOppositionCreatorIds();
}
catch (Exception ex)
{
    Log.Warning(ex, "Opposition backfill failed on startup \u2014 continuing");
}

// ------------------ Pipeline ------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(corsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
