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
using DotNetEnv;

Env.Load();
var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration after loading .env
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "https://portal.iponigeria.com";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "https://portal.iponigeria.com";

if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException("JWT_KEY environment variable is missing or invalid. It must be at least 32 characters long.");
}

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("JWT_KEY must produce at least 32 bytes when encoded as UTF-8.");
}


builder.Configuration["Jwt:Key"] = jwtKey;
builder.Configuration["Jwt:Issuer"] = jwtIssuer;
builder.Configuration["Jwt:Audience"] = jwtAudience;

var mongoConnectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING") 
    ?? builder.Configuration["PatentDesignDatabase:ConnectionStringUp"];
if (!string.IsNullOrWhiteSpace(mongoConnectionString))
{
    builder.Configuration["PatentDesignDatabase:ConnectionStringUp"] = mongoConnectionString;
}

// Override SMTP settings
var smtpServer = Environment.GetEnvironmentVariable("SMTP_SERVER") ?? builder.Configuration["EmailSettings:SmtpServer"];
var smtpUsername = Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? builder.Configuration["EmailSettings:Username"];
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
            .WithOrigins("https://portal.iponigeria.com") // your frontend domain
            .WithOrigins("http://localhost:5173")
            .WithOrigins("https://link.einaotest.com")
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

// ------------------ MongoDB ------------------
string digitalOceanConnectionString = builder.Configuration["PatentDesignDatabase:ConnectionStringUp"] ??
    @"mongodb+srv://readmin:W9415L6d27tcB3gv@db-mongodb-lon1-93952-8f46b05e.mongo.ondigitalocean.com/admin?tls=true&authSource=admin";

var mongoSettings = MongoClientSettings.FromUrl(new MongoUrl(digitalOceanConnectionString));
mongoSettings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 };
var mongoClient = new MongoClient(mongoSettings);

// ------------------ Configurations ------------------
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

// ------------------ Services ------------------
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

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

// ------------------ Build the App ------------------
var app = builder.Build();

// ------------------ Configure Pipeline ------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseExceptionHandler("/error");
    app.UseStatusCodePages();
}

app.UseHttpsRedirection();

app.UseRouting();

// CORS 
app.UseCors(corsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
