using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using patentdesign.Dtos.Response;
using patentdesign.Utils;
using MailKit.Security;

namespace patentdesign.Services;

public class EmailServices
{
    private readonly EmailSettings _settings;

    public EmailServices(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task <bool>NotifyApplicantMail(OppositionEmailDto email)
    {
        string body = PopulateOppositionMail(email);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
        message.To.Add(new MailboxAddress("",email.To));
        message.Subject = email.Subject;
        var builder = new BodyBuilder();
        builder.HtmlBody = body;
        message.Body = builder.ToMessageBody();

        using (var client = new SmtpClient())
        {
            // Ensure we use username/password instead of OAuth if the server doesn't support it
            client.AuthenticationMechanisms.Remove("XOAUTH2");

            // Use MailKit's built-in timeout (ms). Adjust as needed, e.g., 60000 for slower networks.
            client.Timeout = 60000;

            try
            {
                // Auto-negotiates TLS (implicit SSL/STARTTLS) based on port and server capabilities
                await client.ConnectAsync(
                    _settings.SmtpServer,
                    _settings.Port,
                    SecureSocketOptions.Auto);

                // Auth required by Plesk
                await client.AuthenticateAsync(_settings.Username, _settings.Password);

                await client.SendAsync(message);
                return true;
            }
            catch (System.Threading.Tasks.TaskCanceledException ex)
            {
                // Typically indicates network reachability or firewall issues when using timeouts
                throw new ApplicationException($"SMTP connection timed out to '{_settings.SmtpServer}:{_settings.Port}'. " +
                                               "Check DNS resolution, outbound firewall rules, and that the port/TLS mode matches the server.", ex);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                throw new ApplicationException($"Failed to connect to SMTP server '{_settings.SmtpServer}:{_settings.Port}'. " +
                                               $"Verify host, port, firewall, and TLS settings. Details: {ex.Message}", ex);
            }
            catch (MailKit.Security.SslHandshakeException ex)
            {
                throw new ApplicationException("SSL/TLS handshake with SMTP server failed. " +
                                               "This often indicates a TLS mode mismatch (implicit SSL vs STARTTLS) or certificate issues.", ex);
            }
            catch (MailKit.ServiceNotAuthenticatedException ex)
            {
                throw new ApplicationException("SMTP authentication failed. Verify username/password and that SMTP auth is enabled.", ex);
            }
            finally
            {
                if (client.IsConnected)
                    await client.DisconnectAsync(true);
            }
        }
        
    }

    private string PopulateOppositionMail(OppositionEmailDto dto)
    {
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

        return body;
    }
}