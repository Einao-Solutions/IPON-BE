# Email setup and deployment

## Required configuration

Transactional email uses Resend only. SMTP settings are retained for compatibility but are not a fallback transport.

Set the following in the Development `.env` and in both Azure DevOps variable groups (`iponigeria-test-variables` and `iponigeria-production-variables`):

- `RESEND_APIKEY`: a real API key authorized to send from the configured domain. Store it as a secret in Azure DevOps.
- `RESEND_FROM_EMAIL`: an address on a verified Resend sending domain.
- `RESEND_FROM_NAME`: the sender display name.
- Every template setting listed below: a published Resend template ID or alias. Templates must be available to the account used by the API key.

| Email | Configuration key |
| --- | --- |
| Opposition | `RESEND_TEMPLATE_OPPOSITION` |
| Early renewal reminder | `RESEND_TEMPLATE_RENEWAL_EARLY_REMINDER` |
| Renewal due notice | `RESEND_TEMPLATE_RENEWAL_DUE_NOTICE` |
| Counter statement | `RESEND_TEMPLATE_COUNTER_STATEMENT` |
| Opposition confirmation | `RESEND_TEMPLATE_OPPOSITION_CONFIRMATION` |
| Statutory declaration | `RESEND_TEMPLATE_STATUTORY_DECLARATION` |
| Withdrawal notification | `RESEND_TEMPLATE_WITHDRAWAL_NOTIFICATION` |
| Withdrawal approved (opposer) | `RESEND_TEMPLATE_WITHDRAWAL_APPROVED` |
| Withdrawal refused (opposer) | `RESEND_TEMPLATE_WITHDRAWAL_REFUSED` |
| Withdrawal approved (applicant) | `RESEND_TEMPLATE_WITHDRAWAL_APPROVED_APPLICANT` |
| Withdrawal refused (applicant) | `RESEND_TEMPLATE_WITHDRAWAL_REFUSED_APPLICANT` |
| Password reset | `RESEND_TEMPLATE_RESET_PASSWORD` |
| Welcome / verification | `RESEND_TEMPLATE_WELCOMEVERIFICATION` |
| Status update | `RESEND_TEMPLATE_STATUS_UPDATE` |

Startup rejects missing, blank, unresolved (`$(...)`, `${...}`), or placeholder (`tmpl_xxxxxxxxx`) settings. Fill in all real withdrawal templates before starting or deploying. No template IDs or credentials are created automatically.

## Portal URLs

Verification and password-reset emails use `PORTAL_BASE_URL`:

- Development default: `http://localhost:5173` (override in `.env` if needed).
- Test pipeline: `https://test.iponigeria.com`.
- Production pipeline: `https://portal.iponigeria.com`.

Outside Development, the application rejects loopback/non-HTTPS portal URLs. The URL must not contain credentials, query parameters, or a fragment. Check links hardcoded inside hosted Resend templates separately.

`.env` is loaded only in Development and its values are added to application configuration. Test and Production receive settings through IIS app-pool environment variables.

## Pipeline behavior

Both pipelines pass variable-group settings through task environment mappings, not interpolated PowerShell source. They validate required values before modifying IIS and update/add managed variables without clearing unrelated app-pool settings. Logs print setting names, not values. Missing optional SMTP settings are skipped.

The deployment agent needs permission to manage the target IIS app pool. Validate the deployment in Test before Production. Unit tests and compilation do not validate IIS permissions or Resend account configuration.

## Template variables

The selected mail DTO in `Dtos/Response/EmailDto.cs` supplies case-sensitive template variable names. Common variables are `Subject`, `Body`, and `CurrentYear`; a nonempty outer subject also overrides the message subject.

- Welcome verification: `FirstName`, `VerificationLink`.
- Password reset: `UserName`, `ResetLink`.
- Status update: `ApplicationType`, `FormerStatus`, `NewStatus`, `DateTreated`, `Remarks`.
- Renewals: `ApplicantName`, `FileNumber`, `Title`, `RegistryName`, `Class`, `Type`, `IsExpiryDay`, `RenewalDue`, `DueDate`, `ExpiryDate`.

Renewal date aliases all contain the same date in invariant English `dd MMMM yyyy` format. Booleans are strings (`true`/`false`), enums are names, and null values become empty strings. Match these definitions to the published templates. Local `Templates/*.html` files are not rendered by the Resend sending path.

## Delivery and retries

Status and renewal notifications store a JSON email payload snapshot and delivery metadata in the same MongoDB document as the notification. These internal fields are excluded from API/SignalR JSON.

- An initial send is attempted after persistence.
- The background job retries pending emails every minute, up to 100 candidates per pass.
- Failures back off exponentially, capped at 60 minutes, without stopping other recipients.
- Atomic five-minute leases prevent concurrent workers from claiming the same email while a lease is active.
- The notification ID is supplied as the Resend idempotency key. Provider deduplication is limited to Resend's retention window; this is not an unlimited exactly-once guarantee.
- `EmailSentAt` means the provider accepted the request, not that the recipient received it. Monitor bounces and delivery events in Resend.
- Renewal IDs include the file, recipient, expiry date, and reminder kind. Renewal scans run at startup and each UTC day; an early reminder can be created within the 90-day window if the exact 90-day date was missed.
- Existing notifications without an email payload are not replayed as queued emails. Eligible renewal files are evaluated by the new cycle-based deduplication scheme; legacy records do not prove prior email acceptance.

Withdrawal decision emails are awaited independently for each recipient. Failures are logged without undoing the saved decision or preventing the other recipient's send. They are not part of the durable status/renewal retry queue; failed withdrawal sends require follow-up.

## Validation and release checklist

1. Rotate credentials previously exposed in chat or logs, including Resend, SMTP, MongoDB, and JWT signing secrets. Account for existing JWT session invalidation when rotating the signing key.
2. Configure every required value in Development and both variable groups; do not commit `.env` or real secrets.
3. Verify the sender domain and publish all hosted templates with matching variables and environment-appropriate links.
4. Run `dotnet test patentdesign/PatentDesign.Tests/PatentDesign.Tests.csproj` from the repository root. The email regression tests use a fake HTTP handler and mocked MongoDB; they do not start the application, access live data, or send real emails.
5. Deploy to Test and perform authorized test-recipient checks for verification, password reset, status changes, renewals, and withdrawals. Confirm URLs, subjects, formatting, provider events, and retry behavior before Production.
