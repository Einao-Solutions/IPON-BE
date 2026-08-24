using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using patentdesign.Models;
using Resend;
using System.Linq;

namespace patentdesign.Utils;

public class ResendUtils
{
    private const string SegmentName = "IPO Nigeria Users";
    private readonly IConfiguration _configuration;
    private readonly ResendClient _client;
    private readonly ILogger<ResendUtils> _log;

    public ResendUtils(IConfiguration configuration, ResendClient client, ILogger<ResendUtils> log)
    {
        _configuration = configuration;
        _client = client;
        _log = log;
    }

    public async Task ListAudiencesAsync()
    {
        _log.LogInformation("Listing Resend audiences");

        var audiences = await _client.AudienceListAsync();
        foreach (var audience in audiences.Content)
        {
            _log.LogInformation("Audience: {AudienceName} ({AudienceId})", audience.Name, audience.Id);
        }
    }

    private async Task<Guid> GetOrCreateSegmentIdAsync()
    {
        _log.LogInformation("Resolving Resend segment {SegmentName}", SegmentName);
        var segmentsResponse = await _client.SegmentListAsync(null);
        var existing = segmentsResponse.Content.Data.FirstOrDefault(s =>
            string.Equals(s.Name, SegmentName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return existing.Id;
        }

        _log.LogInformation("Creating Resend segment {SegmentName}", SegmentName);
        var created = await _client.SegmentCreateAsync(new SegmentData { Name = SegmentName });
        return created.Content;
    }

    public async Task<string> CreateContactAsync(AppUser user)
    {
        _log.LogInformation("Creating Resend contact for {Email}", user.Email);
        Guid contactId;
        try
        {
            var created = await _client.ContactAddAsync(new ContactData
            {
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                IsUnsubscribed = false
            });

            contactId = created.Content;
        }
        catch (Exception ex) when (IsDuplicateContactError(ex))
        {
            _log.LogInformation("Resend contact already exists for {Email}. Updating existing contact.", user.Email);

            await _client.ContactUpdateByEmailAsync(user.Email, new ContactData
            {
                FirstName = user.FirstName,
                LastName = user.LastName
            });

            var existing = await _client.ContactRetrieveByEmailAsync(user.Email);
            contactId = existing.Content.Id;
        }

        var segmentId = await GetOrCreateSegmentIdAsync();
        try
        {
            await _client.ContactAddToSegmentAsync(contactId, segmentId);
        }
        catch (Exception ex) when (IsAlreadyInSegmentError(ex))
        {
            _log.LogInformation("Contact {ContactId} is already in segment {SegmentName}", contactId, SegmentName);
        }

        _log.LogInformation("Resend contact upserted for {Email} with contact id {ContactId} and added to segment {SegmentName}", user.Email, contactId, SegmentName);
        return contactId.ToString();
    }

    public async Task ListContactsAsync()
    {
        _log.LogInformation("Listing Resend contacts");
        var contacts = await _client.ContactListAsync(null);
        foreach (var c in contacts.Content.Data)
        {
            _log.LogInformation("Contact: {FirstName} {LastName} <{Email}> (unsubscribed: {Unsubscribed})", c.FirstName, c.LastName, c.Email, c.IsUnsubscribed);
        }
    }

    public async Task UpdateContactAsync(string contactId, string firstName)
    {
        if (!Guid.TryParse(contactId, out var parsedContactId))
        {
            throw new ArgumentException("Invalid contact id format.", nameof(contactId));
        }

        _log.LogInformation("Updating Resend contact {ContactId}", contactId);
        var existing = await _client.ContactRetrieveAsync(parsedContactId);

        await _client.ContactUpdateAsync(parsedContactId, new ContactData
        {
            FirstName = firstName,
            LastName = existing.Content.LastName,
            IsUnsubscribed = existing.Content.IsUnsubscribed
        });
        _log.LogInformation("Resend contact {ContactId} updated", contactId);
    }

    public async Task RemoveContactAsync(string contactId)
    {
        if (!Guid.TryParse(contactId, out var parsedContactId))
        {
            throw new ArgumentException("Invalid contact id format.", nameof(contactId));
        }

        _log.LogInformation("Removing Resend contact {ContactId}", contactId);
        await _client.ContactDeleteAsync(parsedContactId);
        _log.LogInformation("Resend contact {ContactId} removed", contactId);
    }

    public async Task AddUserToResendAsync(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(_configuration["RESEND_APIKEY"]) &&
            string.IsNullOrWhiteSpace(_configuration["RESEND_API_KEY"]) &&
            string.IsNullOrWhiteSpace(_configuration["RESEND_APITOKEN"]))
        {
            _log.LogWarning("Skipping Resend contact sync for {Email}: API key not configured", user.Email);
            return;
        }

        try
        {
            _log.LogInformation("Adding user {Email} to Resend segment {Segment}", user.Email, SegmentName);
            await CreateContactAsync(user);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to sync user {Email} to Resend", user.Email);
        }
    }

    private static bool IsDuplicateContactError(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("already", StringComparison.OrdinalIgnoreCase)
               && message.Contains("contact", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyInSegmentError(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("already", StringComparison.OrdinalIgnoreCase)
               && message.Contains("segment", StringComparison.OrdinalIgnoreCase);
    }
}