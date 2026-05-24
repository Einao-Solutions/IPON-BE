using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Utils;
using static QRCoder.PayloadGenerator;

namespace patentdesign.Services;

public class EmailServices
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailServices> _log;
    public EmailServices(IOptions<EmailSettings> settings, ILogger<EmailServices> log)
    {
        _settings = settings.Value;
        _log = log;
    }

    public async Task SendMail(EmailDto dto)
    {
        _log.LogInformation("Preparing email to {Recipient} with subject '{Subject}' (Type: {EmailType})",
            dto.To, dto.Subject, dto.EmailType);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
        message.To.Add(new MailboxAddress("",dto.To));
        message.Subject = dto.Subject;
        string body = "";
        switch (dto.EmailType)
        { 
            case EmailType.Opposition:
                body = PopulateOppositionMail(dto.OppositionMail);
                break;
            case EmailType.CounterStatement:
                body = PopulateCounterStatementMail(dto.CounterStatementMail);
                break;
            case EmailType.OppositionConfirmation:
                body = PopulateOppositionConfirmationMail(dto.OppositionConfirmationMail);
                break;
            case EmailType.StatutoryDeclaration:
                body = PopulateStatutoryDeclarationMail(dto.StatutoryDeclarationMail);
                break;
            case EmailType.WithdrawalNotification:
                body = PopulateWithdrawalNotificationMail(dto.WithdrawalNotificationMail);
                break;
            case EmailType.ResetPassword:
                body = ResetPasswordMail(dto.ResetPasswordMail);
                break;
            case EmailType.StatusUpdate:
                break;
        }

        var builder = new BodyBuilder();
        builder.HtmlBody = body;
        message.Body = builder.ToMessageBody();

        using (var client = new SmtpClient(new MailKit.ProtocolLogger(Console.OpenStandardError())))
        {
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            client.Timeout = 60000;

            try
            {
                _log.LogDebug("Connecting to SMTP server {Server}:{Port}", _settings.SmtpServer, _settings.Port);

                await client.ConnectAsync(
                    _settings.SmtpServer,
                    _settings.Port,
                    SecureSocketOptions.SslOnConnect);

                await client.AuthenticateAsync(_settings.Username, _settings.Password);

                await client.SendAsync(message);
                _log.LogInformation("Email sent successfully to {Recipient}", dto.To);
            }
            catch (System.Threading.Tasks.TaskCanceledException ex)
            {
                _log.LogError(ex, "SMTP connection timed out to {Server}:{Port}", _settings.SmtpServer, _settings.Port);
                throw new ApplicationException(
                    $"SMTP connection timed out to '{_settings.SmtpServer}:{_settings.Port}'. " +
                    "Check DNS resolution, outbound firewall rules, and that the port/TLS mode matches the server.", ex);
            }
            catch(System.Net.Sockets.SocketException ex)
            {
                _log.LogError(ex, "Socket error connecting to SMTP server {Server}:{Port}", _settings.SmtpServer, _settings.Port);
                throw new ApplicationException(
                    $"Failed to connect to SMTP server '{_settings.SmtpServer}:{_settings.Port}'. " +
                    $"Verify host, port, firewall, and TLS settings. Details: {ex.Message}", ex);
            }
            catch (SslHandshakeException ex)
            {
                _log.LogError(ex, "SSL/TLS handshake failed with SMTP server {Server}:{Port}", _settings.SmtpServer, _settings.Port);
                throw new ApplicationException(
                    "SSL/TLS handshake with SMTP server failed. " +
                    "This often indicates a TLS mode mismatch (implicit SSL vs STARTTLS) or certificate issues.", ex);
            }
            catch (MailKit.ServiceNotAuthenticatedException ex)
            {
                _log.LogError(ex, "SMTP authentication failed for user {Username}", _settings.Username);
                throw new ApplicationException(
                    "SMTP authentication failed. Verify username/password and that SMTP auth is enabled.", ex);
            }
            finally
            {
                if (client.IsConnected)
                    await client.DisconnectAsync(true);
            }
        }
        
    }
    public async Task SendBulkEmailAsync(BulkEmailDto dto)
    {
        var batchSize = 20;
        var delayMs = 2000;
        _log.LogInformation("Starting bulk email send to {RecipientCount} recipients with subject '{Subject}'",
            dto.Recipients.Count, dto.Subject);

        string template = string.Empty;
        string filePath = Directory.GetCurrentDirectory() + @"\Templates\Announcement.html";
        using (var reader = new StreamReader(filePath))
        {
            template = reader.ReadToEnd();
        }

        using var client = new SmtpClient();
        try
        {
            _log.LogDebug("Connecting to SMTP server {Server}:{Port}", _settings.SmtpServer, _settings.Port);
            await client.ConnectAsync(
                _settings.SmtpServer,
                _settings.Port,
                SecureSocketOptions.SslOnConnect);

            await client.AuthenticateAsync(_settings.Username, _settings.Password);

            var recipients = dto.Recipients;
            int sentCount = 0;
            for (int i = 0; i < recipients.Count; i += batchSize)
            {
                var batch = recipients.Skip(i).Take(batchSize);
                _log.LogDebug("Sending batch starting at index {Index}", i);

                foreach (var recipient in batch)
                {
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
                    message.To.Add(new MailboxAddress(recipient.Value, recipient.Key));
                    message.Subject = dto.Subject;

                    var html = template.Replace("{{UserName}}", recipient.Value)
                        .Replace("{{Message}}", dto.Body);

                    message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();
                    await client.SendAsync(message);
                    sentCount++;
                }

                await Task.Delay(delayMs);
            }

            _log.LogInformation("Bulk email completed. {SentCount}/{TotalCount} emails sent successfully",
                sentCount, recipients.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Bulk email failed during send to {Server}:{Port}", _settings.SmtpServer, _settings.Port);
            throw;
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true);
        }
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
        return body;
    }

    private string PopulateWithdrawalNotificationMail(WithdrawalNotificationMail dto)
    {
        return $@"
<html><body style='font-family:Arial,sans-serif;color:#333;'>
<p>Dear {dto.ApplicantName},</p>
<p>We wish to inform you that the opposer <strong>{dto.OpposerName}</strong> has submitted a 
<strong>Withdrawal Request</strong> for the opposition filed against your trademark application.</p>
<table style='border-collapse:collapse;width:100%;margin-top:16px;'>
  <tr><td style='padding:8px;border:1px solid #ddd;font-weight:bold;'>File Number</td><td style='padding:8px;border:1px solid #ddd;'>{dto.FileNumber}</td></tr>
  <tr><td style='padding:8px;border:1px solid #ddd;font-weight:bold;'>File Title</td><td style='padding:8px;border:1px solid #ddd;'>{dto.FileTitle}</td></tr>
  <tr><td style='padding:8px;border:1px solid #ddd;font-weight:bold;'>Opposition ID</td><td style='padding:8px;border:1px solid #ddd;'>OPP-{dto.OppositionId?.Substring(0, Math.Min(8, dto.OppositionId?.Length ?? 0)).ToUpper()}</td></tr>
  <tr><td style='padding:8px;border:1px solid #ddd;font-weight:bold;'>Withdrawal Date</td><td style='padding:8px;border:1px solid #ddd;'>{dto.WithdrawalDate}</td></tr>
</table>
<p style='margin-top:16px;'>This withdrawal request is currently pending review by the Registry. 
You will be notified once a decision has been made.</p>
<p>Regards,<br/>IPON Registry</p>
</body></html>";
    }
}