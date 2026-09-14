using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Utils;
using Resend;
using System.Globalization;
using System.Net.Mail;
using System.Reflection;

namespace patentdesign.Services;

public class EmailServices
{
    private static readonly IReadOnlyDictionary<EmailType, string> TemplateKeys = new Dictionary<EmailType, string>
    {
        [EmailType.Opposition] = "RESEND_TEMPLATE_OPPOSITION",
        [EmailType.RenewalEarlyReminder] = "RESEND_TEMPLATE_RENEWAL_EARLY_REMINDER",
        [EmailType.RenewalDueNotice] = "RESEND_TEMPLATE_RENEWAL_DUE_NOTICE",
        [EmailType.CounterStatement] = "RESEND_TEMPLATE_COUNTER_STATEMENT",
        [EmailType.OppositionConfirmation] = "RESEND_TEMPLATE_OPPOSITION_CONFIRMATION",
        [EmailType.StatutoryDeclaration] = "RESEND_TEMPLATE_STATUTORY_DECLARATION",
        [EmailType.WithdrawalNotification] = "RESEND_TEMPLATE_WITHDRAWAL_NOTIFICATION",
        [EmailType.WithdrawalApproved] = "RESEND_TEMPLATE_WITHDRAWAL_APPROVED",
        [EmailType.WithdrawalRefused] = "RESEND_TEMPLATE_WITHDRAWAL_REFUSED",
        //[EmailType.WithdrawalApprovedApplicant] = "RESEND_TEMPLATE_WITHDRAWAL_APPROVED_APPLICANT",
        //[EmailType.WithdrawalRefusedApplicant] = "RESEND_TEMPLATE_WITHDRAWAL_REFUSED_APPLICANT",
        [EmailType.ResetPassword] = "RESEND_TEMPLATE_RESET_PASSWORD",
        [EmailType.WelcomeVerification] = "RESEND_TEMPLATE_WELCOMEVERIFICATION",
        [EmailType.StatusUpdate] = "RESEND_TEMPLATE_STATUS_UPDATE"
    };

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
    public async Task SendMail(EmailDto dto, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (!MailAddress.TryCreate(dto.To, out _))
        {
            throw new ArgumentException(
                "A valid email recipient is required.",
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

        if (!string.IsNullOrWhiteSpace(dto.Subject))
        {
            message.Subject = dto.Subject;
        }

        if (!string.IsNullOrWhiteSpace(dto.CarbonCopy))
        {
            if (!MailAddress.TryCreate(dto.CarbonCopy, out _))
            {
                throw new ArgumentException("A valid CC email address is required.", nameof(dto.CarbonCopy));
            }
            message.Cc.Add(dto.CarbonCopy);
        }

        try
        {
            var response = string.IsNullOrWhiteSpace(idempotencyKey)
                ? await _resend.EmailSendAsync(message, cancellationToken)
                : await _resend.EmailSendAsync(idempotencyKey, message, cancellationToken);
            if (!response.Success)
            {
                throw new InvalidOperationException("Resend rejected the email request.", response.Exception);
            }

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
    /// Gets the configured Resend template ID or alias for the email type.
    /// </summary>
    private string GetTemplateId(EmailType emailType)
    {
        if (!TemplateKeys.TryGetValue(emailType, out var key))
        {
            throw new NotSupportedException($"Email type '{emailType}' has no configured template mapping.");
        }

        return RequireSetting(_configuration, key);
    }

    public static void ValidateConfiguration(IConfiguration configuration)
    {
        RequireSetting(configuration, "RESEND_APIKEY");
        var sender = RequireSetting(configuration, "RESEND_FROM_EMAIL");
        if (!MailAddress.TryCreate(sender, out _))
        {
            throw new InvalidOperationException("RESEND_FROM_EMAIL must be a valid email address.");
        }

        foreach (var key in TemplateKeys.Values)
        {
            RequireSetting(configuration, key);
        }
    }

    private static string RequireSetting(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Contains("$(") || value.Contains("${") ||
            System.Text.RegularExpressions.Regex.IsMatch(value, "^tmpl_x+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            throw new InvalidOperationException($"Configure a real value for '{key}'; missing values and placeholders are not supported.");
        }

        return value;
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

        if (source == null)
        {
            throw new ArgumentException($"The mail payload for '{dto.EmailType}' is required.", nameof(dto));
        }

        var variables = ConvertObjectToVariables(source);
        if (source is RenewalReminder renewal)
        {
            AddVariable(variables, "DueDate", renewal.RenewalDue);
            AddVariable(variables, "ExpiryDate", renewal.RenewalDue);
        }

        // Common variables available to all Resend templates.
        AddVariable(
            variables,
            "Subject",
            dto.Subject);

        if (dto.Body != null || !variables.ContainsKey("Body"))
        {
            AddVariable(variables, "Body", dto.Body);
        }

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
            return dateTime.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is Enum)
        {
            return value.ToString()!;
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

        if (!MailAddress.TryCreate(senderEmail, out _))
        {
            throw new InvalidOperationException(
                "A valid Resend sender email is not configured. " +
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