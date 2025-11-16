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

var builder = WebApplication.CreateBuilder(args);

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
string digitalOceanConnectionString =
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
builder.Services.AddControllers();
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

// ✅ CORS should come before authentication/authorization
app.UseCors(corsPolicy);

// ✅ Make sure authentication runs before authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
