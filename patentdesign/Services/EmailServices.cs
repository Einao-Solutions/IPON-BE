using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Utils;
using Resend;
using System.Reflection;

namespace patentdesign.Services;

public class EmailServices
{
    private readonly EmailSettings _settings;
    private readonly IConfiguration _configuration;
    private readonly IResend _resend;
    private readonly ILogger<EmailServices> _log;

    public EmailServices(
        IOptions<EmailSettings> settings,
        IConfiguration configuration,
        IResend resend,
        ILogger<EmailServices> log)
    {
        _settings = settings.Value;
        _configuration = configuration;
        _resend = resend;
        _log = log;
    }

    /// <summary>
    /// Sends a transactional email using a Resend template.
    /// </summary>
    public async Task SendMail(EmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.To))
        {
            throw new ArgumentException(
                "Email recipient is required.",
                nameof(dto.To));
        }

        _log.LogInformation(
            "Preparing Resend template email to {Recipient}. Email Type: {EmailType}",
            dto.To,
            dto.EmailType);

        var templateId = GetTemplateId(dto.EmailType);
        var variables = BuildTemplateVariables(dto);

        var message = new EmailMessage
        {
            From = $"{GetSenderName()} <{GetSenderEmail()}>",

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
            var response = await _resend.EmailSendAsync(message);

            _log.LogInformation(
                "Resend template email sent successfully to {Recipient}. Template: {TemplateId}",
                dto.To,
                templateId);
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "Failed to send Resend template email to {Recipient}. Template: {TemplateId}",
                dto.To,
                templateId);

            throw;
        }
    }

    /// <summary>
    /// Gets the correct Resend template ID based on EmailType.
    ///
    /// Example:
    /// EmailType.Opposition
    /// becomes
    /// RESEND_TEMPLATE_OPPOSITION
    /// </summary>
    private string GetTemplateId(EmailType emailType)
    {
        var key = $"RESEND_TEMPLATE_{emailType}"
            .ToUpperInvariant();

        var templateId = Environment.GetEnvironmentVariable(key);

        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException(
                $"Resend template ID is missing for email type '{emailType}'. " +
                $"Expected configuration key: '{key}'.");
        }

        return templateId;
    }

    /// <summary>
    /// Builds the variables sent to the Resend template.
    /// The property names in the mail DTO become the Resend variable names.
    /// </summary>
    private Dictionary<string, object> BuildTemplateVariables(EmailDto dto)
    {
        object? source = dto.EmailType switch
        {
            EmailType.Opposition =>
                dto.OppositionMail,

            EmailType.RenewalEarlyReminder =>
                dto.RenewalReminder,

            EmailType.RenewalDueNotice =>
                dto.RenewalReminder,

            EmailType.CounterStatement =>
                dto.CounterStatementMail,

            EmailType.OppositionConfirmation =>
                dto.OppositionConfirmationMail,

            EmailType.StatutoryDeclaration =>
                dto.StatutoryDeclarationMail,

            EmailType.WithdrawalNotification =>
                dto.WithdrawalNotificationMail,

            EmailType.WithdrawalApproved =>
                dto.WithdrawalApprovedMail,

            EmailType.WithdrawalRefused =>
                dto.WithdrawalRefusedMail,

            EmailType.WithdrawalApprovedApplicant =>
                dto.WithdrawalApprovedApplicantMail,

            EmailType.WithdrawalRefusedApplicant =>
                dto.WithdrawalRefusedApplicantMail,

            EmailType.ResetPassword =>
                dto.ResetPasswordMail,

            EmailType.WelcomeVerification =>
                dto.WelcomeVerificationMail,

            EmailType.StatusUpdate =>
                dto.StatusUpdateMail,

            _ => null
        };

        var variables = source != null
            ? ConvertObjectToVariables(source)
            : new Dictionary<string, object>();

        // Common variables available to all Resend templates.
        AddVariable(
            variables,
            "Subject",
            dto.Subject);

        AddVariable(
            variables,
            "Body",
            dto.Body);

        AddVariable(
            variables,
            "CurrentYear",
            DateTime.UtcNow.Year.ToString());

        return variables;
    }

    /// <summary>
    /// Converts public properties from a DTO into Resend template variables.
    ///
    /// Example:
    /// dto.ApplicantName -> {{{ApplicantName}}}
    /// dto.FileNumber    -> {{{FileNumber}}}
    /// </summary>
    private static Dictionary<string, object> ConvertObjectToVariables(
        object source)
    {
        var variables = new Dictionary<string, object>(
            StringComparer.Ordinal);

        var properties = source
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.CanRead &&
                property.GetIndexParameters().Length == 0);

        foreach (var property in properties)
        {
            var value = property.GetValue(source);

            variables[property.Name] = FormatVariableValue(value);
        }

        return variables;
    }

    /// <summary>
    /// Converts null values to an empty string and formats dates.
    /// </summary>
    private static object FormatVariableValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString("dd MMMM yyyy");
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("dd MMMM yyyy");
        }

        return value;
    }

    /// <summary>
    /// Adds or replaces a template variable.
    /// </summary>
    private static void AddVariable(
        Dictionary<string, object> variables,
        string name,
        object? value)
    {
        variables[name] = FormatVariableValue(value);
    }

    /// <summary>
    /// Gets the verified Resend sender email.
    /// </summary>
    private string GetSenderEmail()
    {
        var senderEmail = _configuration["RESEND_FROM_EMAIL"];

        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            senderEmail = _settings.SenderEmail;
        }

        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            throw new InvalidOperationException(
                "Resend sender email is not configured. " +
                "Set RESEND_FROM_EMAIL.");
        }

        return senderEmail;
    }

    /// <summary>
    /// Gets the display name for the sender.
    /// </summary>
    private string GetSenderName()
    {
        var senderName = _configuration["RESEND_FROM_NAME"];

        if (string.IsNullOrWhiteSpace(senderName))
        {
            senderName = _settings.SenderName;
        }

        return string.IsNullOrWhiteSpace(senderName)
            ? "IPONigeria"
            : senderName;
    }
}