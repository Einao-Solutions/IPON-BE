using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Utils;
using Resend;
using System.Text.Json;
using static QRCoder.PayloadGenerator;

namespace patentdesign.Services;

public class EmailServices
{
    private readonly EmailSettings _settings;
    private readonly IConfiguration _configuration;
    private readonly IResend _resend;
    private readonly ILogger<EmailServices> _log;
    public EmailServices(IOptions<EmailSettings> settings, IConfiguration configuration, IResend resend, ILogger<EmailServices> log)
    {
        _settings = settings.Value;
        _configuration = configuration;
        _resend = resend;
        _log = log;
    }

    public async Task SendMail(EmailDto dto)
    {
        _log.LogInformation("Preparing email to {Recipient} with subject '{Subject}' (Type: {EmailType})",
            dto.To, dto.Subject, dto.EmailType);

        var templateId = GetTemplateId(dto.EmailType);
        var variables = BuildTemplateVariables(dto);

        var message = new EmailMessage
        {
            From = $"{GetSenderName()} <{GetSenderEmail()}>",
            Subject = dto.Subject,
            Template = new EmailMessageTemplate
            {
                TemplateId = templateId,
                Variables = variables
            }
        };
        message.To.Add(dto.To);
        if (!string.IsNullOrWhiteSpace(dto.CarbonCopy))
        {
            message.Cc.Add(dto.CarbonCopy);
        }

        try
        {
            await _resend.EmailSendAsync(message);
            _log.LogInformation("Email sent successfully via Resend to {Recipient}", dto.To);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Resend email failed for {Recipient}", dto.To);
            throw;
        }
    }

    public async Task SendBulkEmailAsync(BulkEmailDto dto)
    {
        var batchSize = 20;
        var delayMs = 2000;
        _log.LogInformation("Starting bulk email send to {RecipientCount} recipients with subject '{Subject}'",
            dto.Recipients.Count, dto.Subject);
        var templateId = GetTemplateId(EmailType.Announcement);

        try
        {
            var recipients = dto.Recipients;
            int sentCount = 0;
            for (int i = 0; i < recipients.Count; i += batchSize)
            {
                var batch = recipients.Skip(i).Take(batchSize);
                _log.LogDebug("Sending batch starting at index {Index}", i);

                foreach (var recipient in batch)
                {
                    var message = new EmailMessage
                    {
                        From = $"{GetSenderName()} <{GetSenderEmail()}>",
                        Subject = dto.Subject,
                        Template = new EmailMessageTemplate
                        {
                            TemplateId = templateId,
                            Variables = new Dictionary<string, object>
                            {
                                ["UserName"] = recipient.Value,
                                ["Message"] = dto.Body,
                                ["CurrentYear"] = DateTime.UtcNow.Year.ToString()
                            }
                        }
                    };
                    message.To.Add(recipient.Key);

                    await _resend.EmailSendAsync(message);
                    sentCount++;
                }

                await Task.Delay(delayMs);
            }

            _log.LogInformation("Bulk email completed. {SentCount}/{TotalCount} emails sent successfully",
                sentCount, recipients.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Bulk email failed during Resend send operation");
            throw;
        }
    }

    private string GetTemplateId(EmailType emailType)
    {
        var key = $"RESEND_TEMPLATE_{emailType}".ToUpperInvariant();
        var templateId = _configuration[key];
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException($"Resend template id is missing for email type '{emailType}'. Expected config key: '{key}'.");
        }

        return templateId;
    }

    private Dictionary<string, object> BuildTemplateVariables(EmailDto dto)
    {
        object? source = null;
        switch (dto.EmailType)
        {
            case EmailType.Opposition:
                source = dto.OppositionMail;
                break;
            case EmailType.RenewalEarlyReminder:
            case EmailType.RenewalDueNotice:
                source = dto.RenewalReminder;
                break;
            case EmailType.CounterStatement:
                source = dto.CounterStatementMail;
                break;
            case EmailType.OppositionConfirmation:
                source = dto.OppositionConfirmationMail;
                break;
            case EmailType.StatutoryDeclaration:
                source = dto.StatutoryDeclarationMail;
                break;
            case EmailType.WithdrawalNotification:
                source = dto.WithdrawalNotificationMail;
                break;
            case EmailType.WithdrawalApproved:
                source = dto.WithdrawalApprovedMail;
                break;
            case EmailType.WithdrawalRefused:
                source = dto.WithdrawalRefusedMail;
                break;
            case EmailType.WithdrawalApprovedApplicant:
                source = dto.WithdrawalApprovedApplicantMail;
                break;
            case EmailType.WithdrawalRefusedApplicant:
                source = dto.WithdrawalRefusedApplicantMail;
                break;
            case EmailType.ResetPassword:
                source = dto.ResetPasswordMail;
                break;
            case EmailType.WelcomeVerification:
                source = dto.WelcomeVerificationMail;
                break;
            case EmailType.StatusUpdate:
                source = dto.StatusUpdateMail;
                break;
        }

        var data = source is null
            ? new Dictionary<string, object?>()
            : ToDictionary(source);

        data["Subject"] = dto.Subject;
        data["Body"] = dto.Body;
        data["CurrentYear"] = DateTime.UtcNow.Year.ToString();
        return data.ToDictionary(kvp => kvp.Key, kvp => (object)(kvp.Value ?? string.Empty));
    }

    private static Dictionary<string, object?> ToDictionary(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
               ?? new Dictionary<string, object?>();
    }

    private string GetSenderEmail()
    {
        var senderEmail = _configuration["RESEND_FROM_EMAIL"];
        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            senderEmail = _settings.SenderEmail;
        }

        return senderEmail;
    }

    private string GetSenderName()
    {
        var senderName = _configuration["RESEND_FROM_NAME"];
        if (string.IsNullOrWhiteSpace(senderName))
        {
            senderName = _settings.SenderName;
        }

        return senderName;
    }

    private string PopulateOppositionConfirmationMail(OppositionConfirmationMail dto)
    {
        _log.LogDebug("Populating opposition confirmation mail for {Opposer}, file {FileNumber}",
            dto.OpposerName, dto.FileNumber);

        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\OppositionConfirmation.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{OpposerName}",     dto.OpposerName);
        body = body.Replace("{OppositionId}",    dto.OppositionId);
        body = body.Replace("{FileNumber}",      dto.FileNumber);
        body = body.Replace("{FileTitle}",       dto.FileTitle);
        body = body.Replace("{DateFiled}",       dto.DateFiled);
        body = body.Replace("{PaymentReference}", dto.PaymentReference);
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateCounterStatementMail(CounterStatementMail dto)
    {
        _log.LogDebug("Populating counter statement mail template for opposer {Opposer}, file {FileNumber}",
            dto.OpposerName, dto.FileNumber);

        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\CounterStatementNotification.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{OpposerName}", dto.OpposerName);
        body = body.Replace("{FileNumber}", dto.FileNumber);
        body = body.Replace("{Title}", dto.Title);
        body = body.Replace("{FileOwnerName}", dto.FileOwnerName);
        body = body.Replace("{CounterStatementDate}", dto.CounterStatementDate);
        body = body.Replace("{SignatoryName}", dto.SignatoryName ?? "");
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateOppositionMail(OppositionMail dto)
    {
        _log.LogDebug("Populating opposition mail template for applicant {Applicant}, file {FileNumber}",
            dto.ApplicantName, dto.FileNumber);

        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\OppositionNotification.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{ApplicantName}", dto.ApplicantName);
        body = body.Replace("{FileNumber}", dto.FileNumber);
        body = body.Replace("{Title}", dto.Title);
        body = body.Replace("{OpposerName}", dto.OpposerName);
        body = body.Replace("{Reason}", dto.Reason);
        body = body.Replace("{OppositionDate}", dto.OppositionDate);
        body = body.Replace("{SignatoryName}", dto.SignatoryName);
        body = body.Replace("{OppositionId}", dto.OppositionId ?? "");
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string ResetPasswordMail(ResetPasswordMail dto)
    {
        _log.LogDebug("Populating reset password mail template");

        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\PasswordReset.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }

        body = body.Replace("{{ResetLink}}", dto.ResetLink);
        body = body.Replace("{{UserName}}", dto.UserName);
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateStatutoryDeclarationMail(StatutoryDeclarationMail dto)
    {
        _log.LogDebug("Populating statutory declaration mail for {Recipient}, file {FileNumber}",
            dto.RecipientName, dto.FileNumber);

        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\StatutoryDeclarationNotification.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{RecipientName}", dto.RecipientName);
        body = body.Replace("{FilerRole}", dto.FilerRole);
        body = body.Replace("{FileNumber}", dto.FileNumber);
        body = body.Replace("{FileTitle}", dto.FileTitle);
        body = body.Replace("{OppositionId}", dto.OppositionId);
        body = body.Replace("{DateFiled}", dto.DateFiled);
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateWithdrawalNotificationMail(WithdrawalNotificationMail dto)
    {
        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\WithdrawalNotification.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{ApplicantName}", dto.ApplicantName);
        body = body.Replace("{OpposerName}",   dto.OpposerName);
        body = body.Replace("{FileNumber}",    dto.FileNumber);
        body = body.Replace("{FileTitle}",     dto.FileTitle);
        body = body.Replace("{OppositionId}",  dto.OppositionId ?? "");
        body = body.Replace("{WithdrawalDate}",dto.WithdrawalDate);
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateWithdrawalApprovedMail(WithdrawalApprovedMail dto)
    {
        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\WithdrawalApproved.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{RecipientName}", dto.RecipientName);
        body = body.Replace("{FileNumber}",    dto.FileNumber);
        body = body.Replace("{FileTitle}",     dto.FileTitle ?? "");
        body = body.Replace("{OfficerName}",   dto.OfficerName ?? "");
        body = body.Replace("{Reason}",        dto.Reason ?? "");
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateWithdrawalRefusedMail(WithdrawalRefusedMail dto)
    {
        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\WithdrawalRefused.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{RecipientName}", dto.RecipientName);
        body = body.Replace("{FileNumber}",    dto.FileNumber);
        body = body.Replace("{OfficerName}",   dto.OfficerName ?? "");
        body = body.Replace("{Reason}",        dto.Reason ?? "");
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateWithdrawalApprovedApplicantMail(WithdrawalApprovedApplicantMail dto)
    {
        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\WithdrawalApprovedApplicant.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{RecipientName}", dto.RecipientName);
        body = body.Replace("{FileNumber}",    dto.FileNumber);
        body = body.Replace("{FileTitle}",     dto.FileTitle ?? "");
        body = body.Replace("{OfficerName}",   dto.OfficerName ?? "");
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateWithdrawalRefusedApplicantMail(WithdrawalRefusedApplicantMail dto)
    {
        string body = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\WithdrawalRefusedApplicant.html";
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{RecipientName}", dto.RecipientName);
        body = body.Replace("{FileNumber}",    dto.FileNumber);
        body = body.Replace("{FileTitle}",     dto.FileTitle ?? "");
        body = body.Replace("{OfficerName}",   dto.OfficerName ?? "");
        body = ApplyCommonTemplateTokens(body);
        return body;
    }

    private string PopulateRenewalReminder(RenewalReminder dto)
    {
        _log.LogDebug("Populating renewal reminder mail template for applicant {Applicant}, file {FileNumber}",
            dto.ApplicantName, dto.FileNumber);

        string body = string.Empty;
        string filePath = dto.IsExpiryDay ? Directory.GetCurrentDirectory() + @"\Templates\RenewalDueNotification.html" : Directory.GetCurrentDirectory() + @"\Templates\RenewalReminder.html";
        bool isTrademark = dto.Type == Models.FileTypes.TradeMark;
        using (var reader = new StreamReader(filePath))
        {
            body = reader.ReadToEnd();
        }
        body = body.Replace("{{ApplicantName}}", dto.ApplicantName);
        body = body.Replace("{{FileNumber}}", dto.FileNumber);
        body = body.Replace("{{Title}}", dto.Title);
        body = body.Replace("{{DueDate}}", dto.RenewalDue.ToString("dd MMMM, yyyy"));
        body = body.Replace("{{Class}}", dto.Class.ToString());
        body = body.Replace("{{RegistryName}}", isTrademark ? "Trademarks" : "Patents & Designs");
        body = ApplyCommonTemplateTokens(body);

        return body;
    }

    private static string ApplyCommonTemplateTokens(string body)
    {
        return body.Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString());
    }
}