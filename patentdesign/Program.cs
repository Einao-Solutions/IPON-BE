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
using patentdesign.Services.Implementation;
using patentdesign.Services.Interface;
using patentdesign.Utils;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using System.Security.Authentication;
using System.Text;

// ------------------ Create Builder ------------------
var builder = WebApplication.CreateBuilder(args);

// ------------------ Load .env ONLY in Development ------------------
if (builder.Environment.IsDevelopment())
{
    DotNetEnv.Env.Load();
}

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
var mongoConnectionString =
    Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
    ?? builder.Configuration["PatentDesignDatabase:ConnectionStringUp"];

if (string.IsNullOrWhiteSpace(mongoConnectionString))
{
    throw new Exception("❌ MongoDB connection string is missing! Check environment variables or appsettings.");
}

builder.Configuration["PatentDesignDatabase:ConnectionStringUp"] = mongoConnectionString;

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
    });

// ------------------ QuestPDF ------------------
QuestPDF.Settings.License = LicenseType.Community;
using var fontStream = File.OpenRead("assets/Certificate.otf");
FontManager.RegisterFont(fontStream);

// ------------------ Mongo Client ------------------
var mongoSettings = MongoClientSettings.FromUrl(new MongoUrl(mongoConnectionString));
mongoSettings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 };
var mongoClient = new MongoClient(mongoSettings);

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
builder.Services.AddSingleton<ILoggerService, LoggerService>();
builder.Services.AddSingleton<PaymentUtils>();
builder.Services.AddSingleton<OppositionService>();
builder.Services.AddSingleton<FileServices>();
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

// ------------------ Build App ------------------
var app = builder.Build();

// ------------------ Pipeline ------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(corsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
