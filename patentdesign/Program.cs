using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Authentication;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using patentdesign.Services.Implementation;
using patentdesign.Services.Interface;


var builder = WebApplication.CreateBuilder(args);

// ------------------ CORS ------------------
const string corsPolicy = "AllowAll";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: corsPolicy, policy =>
    {
        policy
            .AllowAnyOrigin()    
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ------------------ QuestPDF ------------------
QuestPDF.Settings.License = LicenseType.Community;
using var fontStream = File.OpenRead("assets/Certificate.otf");
FontManager.RegisterFont(fontStream);


// ------------------ MongoDB ------------------
string digitalOceanConnectionString = @"mongodb+srv://readmin:W9415L6d27tcB3gv@db-mongodb-lon1-93952-8f46b05e.mongo.ondigitalocean.com/admin?tls=true&authSource=admin";

var mongoSettings = MongoClientSettings.FromUrl(new MongoUrl(digitalOceanConnectionString));
mongoSettings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 };
var mongoClient = new MongoClient(mongoSettings);

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

// ------------------ Build & Configure App ------------------
var app = builder.Build();

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

app.UseCors(corsPolicy); 

app.UseAuthorization();

app.MapControllers();

app.Run();
