using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using System.Security.Authentication;

namespace patentdesign.Services;

/// <summary>
/// Background service that enforces the 30-day counter statement deadline.
/// Runs daily and marks files as Abandoned if no counter statement was filed within 30 days.
/// </summary>
public class OppositionDeadlineService : BackgroundService
{
    private readonly ILogger<OppositionDeadlineService> _log;
    private readonly IMongoCollection<Opposition> _oppositionCollection;
    private readonly IMongoCollection<Filling> _fillingCollection;
    private readonly IMongoCollection<CounterStatement> _counterStatementCollection;
    private readonly IMongoCollection<StatutoryDeclaration> _statutoryDeclarationCollection;
    private readonly EmailServices _emailServices;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private const int DeadlineDays = 30;

    public OppositionDeadlineService(
        IMongoDatabase db,
        IOptions<PatentDesignDBSettings> dbSettings,
        ILogger<OppositionDeadlineService> logger,
        EmailServices emailServices)
    {
        _log = logger;
        _emailServices = emailServices;

        var s = dbSettings.Value;
        _oppositionCollection = db.GetCollection<Opposition>(s.OppositionCollectionName);
        _fillingCollection = db.GetCollection<Filling>(s.FilesCollectionName);
        _counterStatementCollection = db.GetCollection<CounterStatement>(s.CounterStatementsCollectionName);
        _statutoryDeclarationCollection = db.GetCollection<StatutoryDeclaration>("statutoryDeclarations");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("OppositionDeadlineService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiredOppositions(stoppingToken);
                await CheckExpiredStatutoryDeclarationDeadlines(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in OppositionDeadlineService");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckExpiredOppositions(CancellationToken ct)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-DeadlineDays);

        // Find all oppositions that are in AwaitingCounter status and applicant was notified > 30 days ago
        var expiredOppositions = await _oppositionCollection.Find(o =>
            o.Status == ApplicationStatuses.AwaitingCounter &&
            o.ApplicantNotified == true &&
            o.ApplicantNotifiedDate != null &&
            o.ApplicantNotifiedDate < cutoffDate &&
            o.Paid == true
        ).ToListAsync(ct);

        if (expiredOppositions.Count == 0)
        {
            _log.LogInformation("OppositionDeadlineService: No expired oppositions found");
            return;
        }

        _log.LogInformation($"OppositionDeadlineService: Found {expiredOppositions.Count} expired opposition(s)");

        foreach (var opp in expiredOppositions)
        {
            if (ct.IsCancellationRequested) break;

            // Check if a counter statement was actually filed for this opposition
            var hasCounterStatement = await _counterStatementCollection
                .Find(cs => cs.OppositionId == opp.id && cs.Paid == true)
                .AnyAsync(ct);

            if (hasCounterStatement)
            {
                _log.LogInformation($"Opposition {opp.id} has a counter statement, skipping");
                continue;
            }

            _log.LogInformation($"Opposition {opp.id} expired — marking file {opp.FileNumber} as Abandoned");

            // Update opposition status to Resolved with Abandoned resolution
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, opp.id),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Resolved),
                    Builders<Opposition>.Update.Set(o => o.IsResolved, true),
                    Builders<Opposition>.Update.Set(o => o.ResolvedDate, DateTime.UtcNow),
                    Builders<Opposition>.Update.Set(o => o.Decision, "Abandoned - No Counter Statement"),
                    Builders<Opposition>.Update.Set(o => o.ResolutionStatement,
                        "The applicant failed to file a counter statement within the statutory 30-day period. The application is deemed abandoned.")),
                cancellationToken: ct);

            // Update the file status to Abandoned
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                Builders<Filling>.Update.Combine(
                    Builders<Filling>.Update.Set(f => f.FileStatus, ApplicationStatuses.Abandoned),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", ApplicationStatuses.Abandoned)),
                cancellationToken: ct);

            // Notify applicant
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync(ct);
                var applicantEmail = file?.applicants?.FirstOrDefault()?.Email;
                var applicantName = file?.applicants?.FirstOrDefault()?.Name ?? "Applicant";
                string fileTitle = file?.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _ => file?.TitleOfTradeMark
                };

                if (!string.IsNullOrEmpty(applicantEmail))
                {
                    await _emailServices.SendMail(new EmailDto
                    {
                        To = applicantEmail,
                        Subject = "Application Abandoned - Counter Statement Deadline Expired",
                        EmailType = EmailType.Opposition,
                        OppositionMail = new OppositionMail
                        {
                            To = applicantEmail,
                            Subject = "Application Abandoned - Counter Statement Deadline Expired",
                            ApplicantName = applicantName,
                            FileNumber = opp.FileNumber,
                            Title = fileTitle ?? "",
                            OpposerName = opp.Name ?? "",
                            Reason = "Your application has been deemed abandoned because no counter statement was filed within the 30-day statutory period.",
                            OppositionDate = opp.OppositionDate?.ToString("dd MMMM yyyy") ?? "",
                            OppositionId = opp.id
                        }
                    });
                    _log.LogInformation($"Abandonment notification sent to applicant {applicantEmail}");
                }

                // Notify opposer
                var opposerEmail = opp.Email;
                if (!string.IsNullOrEmpty(opposerEmail))
                {
                    await _emailServices.SendMail(new EmailDto
                    {
                        To = opposerEmail,
                        Subject = "Opposition Resolved - Application Abandoned",
                        EmailType = EmailType.Opposition,
                        OppositionMail = new OppositionMail
                        {
                            To = opposerEmail,
                            Subject = "Opposition Resolved - Application Abandoned",
                            ApplicantName = opp.Name ?? "Opposer",
                            FileNumber = opp.FileNumber,
                            Title = fileTitle ?? "",
                            OpposerName = opp.Name ?? "",
                            Reason = "The applicant failed to file a counter statement within the 30-day statutory period. The opposition has been resolved in your favour.",
                            OppositionDate = opp.OppositionDate?.ToString("dd MMMM yyyy") ?? "",
                            OppositionId = opp.id
                        }
                    });
                    _log.LogInformation($"Opposition resolved notification sent to opposer {opposerEmail}");
                }
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, $"Failed to send abandonment notification for opposition {opp.id}");
            }
        }
    }

    /// <summary>
    /// Checks for oppositions in StatutoryDeclaration status where the counter statement was filed > 30 days ago
    /// and no paid statutory declaration has been submitted. The opposition is then withdrawn.
    /// </summary>
    private async Task CheckExpiredStatutoryDeclarationDeadlines(CancellationToken ct)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-DeadlineDays);

        // Find oppositions in StatutoryDeclaration status (awaiting SD from opposer)
        var sdPendingOppositions = await _oppositionCollection.Find(o =>
            o.Status == ApplicationStatuses.StatutoryDeclaration &&
            o.Paid == true
        ).ToListAsync(ct);

        if (sdPendingOppositions.Count == 0)
        {
            _log.LogInformation("OppositionDeadlineService: No pending SD deadline oppositions found");
            return;
        }

        int withdrawnCount = 0;

        foreach (var opp in sdPendingOppositions)
        {
            if (ct.IsCancellationRequested) break;

            // Get the paid counter statement for this opposition to determine the deadline start date
            var cs = await _counterStatementCollection
                .Find(c => c.OppositionId == opp.id && c.Paid == true)
                .SortByDescending(c => c.SubmittedDate)
                .FirstOrDefaultAsync(ct);

            if (cs == null) continue; // No CS means status shouldn't be StatutoryDeclaration — skip

            // Check if 30 days have passed since the counter statement was submitted
            if (cs.SubmittedDate > cutoffDate) continue; // Still within deadline

            // Check if a statutory declaration has been filed
            var hasSd = await _statutoryDeclarationCollection
                .Find(sd => sd.OppositionId == opp.id && sd.Paid == true)
                .AnyAsync(ct);

            if (hasSd)
            {
                _log.LogInformation($"Opposition {opp.id} has a statutory declaration, skipping");
                continue;
            }

            _log.LogInformation($"Opposition {opp.id} SD deadline expired — withdrawing opposition");

            // Update opposition: Withdrawn (resolved, opposition fails)
            await _oppositionCollection.UpdateOneAsync(
                Builders<Opposition>.Filter.Eq(o => o.id, opp.id),
                Builders<Opposition>.Update.Combine(
                    Builders<Opposition>.Update.Set(o => o.Status, ApplicationStatuses.Withdrawn),
                    Builders<Opposition>.Update.Set(o => o.IsResolved, true),
                    Builders<Opposition>.Update.Set(o => o.ResolvedDate, DateTime.UtcNow),
                    Builders<Opposition>.Update.Set(o => o.Decision, "Withdrawn - No Statutory Declaration"),
                    Builders<Opposition>.Update.Set(o => o.ResolutionStatement,
                        "The opposer failed to file a statutory declaration within the statutory 30-day period. The opposition is deemed withdrawn.")),
                cancellationToken: ct);

            // Restore file status back to previous status (before opposition) or Publication
            var previousStatus = opp.PreviousFileStatus ?? ApplicationStatuses.Publication;
            await _fillingCollection.UpdateOneAsync(
                Builders<Filling>.Filter.Eq(f => f.FileId, opp.FileNumber),
                Builders<Filling>.Update.Combine(
                    Builders<Filling>.Update.Set(f => f.FileStatus, previousStatus),
                    Builders<Filling>.Update.Set("ApplicationHistory.0.CurrentStatus", previousStatus)),
                cancellationToken: ct);

            withdrawnCount++;

            // Send notifications
            try
            {
                var file = await _fillingCollection.Find(f => f.FileId == opp.FileNumber).FirstOrDefaultAsync(ct);
                var applicantEmail = file?.applicants?.FirstOrDefault()?.Email;
                var applicantName = file?.applicants?.FirstOrDefault()?.Name ?? "Applicant";
                string fileTitle = file?.Type switch
                {
                    FileTypes.Design => file.TitleOfDesign,
                    FileTypes.Patent => file.TitleOfInvention,
                    _ => file?.TitleOfTradeMark
                };

                // Notify applicant (good news — opposition withdrawn)
                if (!string.IsNullOrEmpty(applicantEmail))
                {
                    await _emailServices.SendMail(new EmailDto
                    {
                        To = applicantEmail,
                        Subject = "Opposition Withdrawn - Statutory Declaration Deadline Expired",
                        EmailType = EmailType.StatutoryDeclaration,
                        StatutoryDeclarationMail = new StatutoryDeclarationMail
                        {
                            To = applicantEmail,
                            Subject = "Opposition Withdrawn - Statutory Declaration Deadline Expired",
                            RecipientName = applicantName,
                            FilerRole = "System",
                            FileNumber = opp.FileNumber,
                            FileTitle = fileTitle ?? "",
                            OppositionId = opp.id,
                            DateFiled = DateTime.UtcNow.ToString("dd MMMM yyyy")
                        }
                    });
                    _log.LogInformation($"Opposition withdrawn notification sent to applicant {applicantEmail}");
                }

                // Notify opposer (their opposition was withdrawn due to inaction)
                var opposerEmail = opp.Email;
                if (!string.IsNullOrEmpty(opposerEmail))
                {
                    await _emailServices.SendMail(new EmailDto
                    {
                        To = opposerEmail,
                        Subject = "Opposition Withdrawn - Statutory Declaration Not Filed",
                        EmailType = EmailType.StatutoryDeclaration,
                        StatutoryDeclarationMail = new StatutoryDeclarationMail
                        {
                            To = opposerEmail,
                            Subject = "Opposition Withdrawn - Statutory Declaration Not Filed",
                            RecipientName = opp.Name ?? "Opposer",
                            FilerRole = "System",
                            FileNumber = opp.FileNumber,
                            FileTitle = fileTitle ?? "",
                            OppositionId = opp.id,
                            DateFiled = DateTime.UtcNow.ToString("dd MMMM yyyy")
                        }
                    });
                    _log.LogInformation($"Opposition withdrawn notification sent to opposer {opposerEmail}");
                }
            }
            catch (Exception emailEx)
            {
                _log.LogError(emailEx, $"Failed to send SD deadline notification for opposition {opp.id}");
            }
        }

        _log.LogInformation($"OppositionDeadlineService: Withdrew {withdrawnCount} opposition(s) due to SD deadline expiry");
    }
}
