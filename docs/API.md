# IPON Backend API Documentation

ASP.NET Core 8 Web API (`patentdesign`) powering the IP Office of Nigeria (IPON) portal:
trademark / patent / design application filing, payments, publication, opposition,
certificates, support tickets and statistics.

- Solution: `patentdesign/patentdesign.sln`
- Entry point: [patentdesign/Program.cs](../patentdesign/Program.cs)
- Target framework: .NET 8 (C# 12)
- Database: MongoDB
- Realtime: SignalR hub at `/hubs/notifications`
- Interactive docs: Swagger UI at `/swagger` (Development only)

---

## 1. Getting started

```powershell
cd patentdesign
dotnet restore
dotnet build
dotnet run
```

In **Development** the app loads a `.env` file from the working directory
(`DotNetEnv`), enables the developer exception page and Swagger UI.
In other environments all settings must come from real environment variables.

### Required configuration

| Key | Source | Purpose |
| --- | --- | --- |
| `JWT_KEY` | env var | HMAC signing key, **minimum 32 bytes**. App fails to start if missing/short. |
| `JWT_ISSUER` | env var | Token issuer (default `https://portal.iponigeria.com`). |
| `JWT_AUDIENCE` | env var | Token audience (default `https://portal.iponigeria.com`). |
| `MONGODB_CONNECTION_STRING` | env var | Mongo connection (non-Development). Dev falls back to `PatentDesignDatabase:ConnectionString`. |
| `PORTAL_BASE_URL` | env/config | Frontend base URL used in emails/links. Must be absolute, no credentials/query/fragment; HTTPS and non-loopback outside Development. |
| `REDIS_CONNECTION_STRING` | env var | Optional. If absent an in-memory distributed cache is used. |
| `RESEND_APIKEY` | config | Resend transactional email API token. |
| `SMTP_SERVER` / `SMTP_USERNAME` / `SMTP_PASSWORD` | env vars | Override `EmailSettings:*`. |
| `PatentDesignDatabase:LogPath` | config | Serilog rolling-file directory (default `C:\IpoApiLog`). |

### Cross-cutting infrastructure

- **Logging** – Serilog to console + daily rolling file; `Microsoft.AspNetCore` limited to Warning.
- **CORS** – policy `AllowPortal` allows `https://portal.iponigeria.com`,
  `https://test.iponigeria.com` and `http://localhost:5173` with credentials.
- **PDF** – QuestPDF (Community licence) with the bundled `assets/Certificate.otf` font.
- **Mongo serialization** – enums persisted as strings; a permissive `ObjectSerializer`
  is registered so dynamic `object?` fields round-trip correctly.
- **Errors** – `AddProblemDetails()`; failures return RFC 7807 problem responses.
- **Background services** – `NotificationJob` and `OppositionDeadlineService` hosted services.
  A one-off `BackfillOppositionCreatorIds()` runs at startup and only logs a warning on failure.

---

## 2. Authentication & authorization

All endpoints use **JWT bearer** tokens unless marked `[AllowAnonymous]`.

```
Authorization: Bearer <access-token>
```

Issuer, audience, lifetime and signing key are all validated. For the SignalR hub the
token may instead be passed as a query string: `/hubs/notifications?access_token=<token>`.

Role-based authorization uses the `Roles` enum (`patentdesign/Models/Enums.cs`), including
`User`, `Staff`, `SuperAdmin`, `Finance`, `HeadOfUnit`, `Tech`, `PermSec`, `Minister`,
the trademark roles (`TrademarkSearch`, `TrademarkExaminer`, `TrademarkOpposition`,
`TrademarkAcceptance`, `TrademarkCertification`, `TrademarkPublication`,
`TrademarkRegistrar`, `ActingTrademarkRegistrar`, `TrademarkStaff`, `TrademarkSupport`),
the patent/design roles (`PatentSearch`, `PatentExaminer`, `PatentCertification`,
`DesignSearch`, `DesignExaminer`, `DesignCertification`, `PatentDesignRegistrar`,
`ActingPatentDesignRegistrar`, `PatentStaff`, `DesignStaff`, `PatentDesignSupport`),
plus `AppealExaminer`, `EinaoFinance` and `Pebec`.

---

## 3. Endpoint reference

Base path for every controller is shown with its route prefix. Query/body shapes are
documented in Swagger (XML comments are compiled into the OpenAPI document).

### 3.1 Auth — `api/auth`

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `register` | anonymous | Create a portal account. |
| POST | `login` | anonymous | Exchange credentials for access/refresh tokens. |
| POST | `refresh` | anonymous | Refresh an expired access token. |
| POST | `logout` | required | Invalidate the current session. |
| POST | `transfer` | anonymous | Transfer/claim an account. |
| POST | `ResetPasswordRequest` | anonymous | Email a password reset link. |
| POST | `ResetPassword` | anonymous | Complete a password reset. |
| POST | `VerifyEmailRequest` | anonymous | Send email verification. |
| POST | `ResendVerificationEmail` | anonymous | Resend the verification email. |
| POST | `VerifyEmail` | anonymous | Confirm an email token. |
| POST | `change-password` | required | Change password for the signed-in user. |
| POST | `UpdateProfile` | required | Update the signed-in user's profile. |
| GET | `GetUser` | required | Current user profile. |

### 3.2 Users — `api/users`

| Method | Route | Description |
| --- | --- | --- |
| GET | `SearchNameId` | Lookup users by name/id. |
| POST | `LoadUsers` | Paged user list. |
| PUT | `UpdateUserRoles` | Assign/revoke roles. |
| POST | `GetAllUsers` | Full/filtered user listing. |
| GET | `GetUserById` | Single user. |
| GET | `GetOtherApplications` | Applications linked to a user. |

### 3.3 Admin — `api/admin`

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `ChangeStatus` | staff | Change an application status. |
| POST | `ApplicationHistory` | `SuperAdmin` | Query application audit history. |
| POST | `CreateApplicationHistory` | `SuperAdmin` | Record a history entry. |
| GET | `ApplicationHistory/{applicationId}` | `SuperAdmin` | History for one application. |
| PATCH | `ApplicationHistory` | `SuperAdmin` | Amend a history entry. |
| DELETE | `ApplicationHistory` | `SuperAdmin` | Remove a history entry. |
| GET | `diag/assignment` | anonymous | Assignment diagnostics. |
| POST | `SendAnnouncement` | staff | Broadcast an announcement. |
| POST | `ResetPassword` | staff | Admin-initiated password reset. |
| POST | `UploadSignature` | staff | Upload a registrar signature image. |
| GET | `GetUserByEmail` | staff | Lookup a user by email. |

### 3.4 Files (applications) — `api/files`

The largest controller; it owns the application lifecycle for trademarks, patents and
designs. Representative endpoints (see Swagger for the complete list):

| Area | Endpoints |
| --- | --- |
| CRUD | `POST api/files`, `GET api/files/{id}`, `DELETE api/files/{id}`, `POST createNew`, `POST BulkAdd`, `POST summary`, `POST GetListOfIds` |
| Search | `GET search`, `GET GetAvailabilitySearch`, `GET AvailabilitySearchCost`, `GET searchForRenewal` |
| Payments | `POST ValidatePayment`, `GET CertificatePayment`, `POST CertificateValidate`, `GET GetRRRCost`, `GET GenerateOppositionRRR`, `POST ManualPaymentUpdate`, `POST DownloadAllPayments`, `POST updatecost`, `POST RevisionCost`, `GET RenewalCost` |
| Documents | `POST uploadAttachment`, `GET GetAttachment`, `POST DesignCerts`, `GET DesignPDf`, `POST replaceLetters`, `POST ReIssueReceiptAndAck` |
| Status workflow | `POST UpdateApplicationStatus`, `POST updatemanystatus`, `GET GetStatusRequests`, `POST newStatusRequest`, `GET GetStatusFromRequest`, `POST UpdateStatusRequest` |
| Admin/maintenance | `POST AdminUpdateApplication`, `POST ManualUpdate`, `POST SaveDataUpdate`, `POST updateJsonData`, `POST freeupdates`, `POST DeletePending`, `POST DeletePendings`, `POST ReAssign` |
| Dashboard | `GET FileStatistics`, `GET DashboardRenewal`, `GET UserNotifications`, `GET UserTicketTiles` |
| Renewal | `POST CreateFileRenewal` |

### 3.5 Payments — `api/payments`

`POST generate`, `POST AddPayment`, `POST SaveOtherPayment`, `GET GetOtherPayment`,
`POST UpdatePayment`, `GET GetAllPayment`, `GET Check` (Remita RRR status check).

### 3.6 Assignment — `api/assignment`

`POST generate`, `POST SearchForFile`, `POST UpdateAssignment`, `POST PayAssignment`,
`GET ValidationRRR`.

### 3.7 Opposition — `api/opposition`

| Group | Endpoints |
| --- | --- |
| Opposition | `GET OppositionSearch`, `POST NewOpposition`, `POST StaffOpposition`, `POST UpdateOppositionPayment`, `GET GetAllOpposition`, `GET get`, `GET getOppositionDetail`, `GET count`, `GET loadSummary`, `GET stats`, `POST notify` |
| Counter statement | `GET csSearchFile`, `GET CounterStatementSearch`, `GET getCounterStatementFee`, `POST NewCounterStatement`, `POST UpdateCounterStatementPayment` |
| Statutory declaration | `GET StatutoryDeclarationSearch`, `POST NewStatutoryDeclaration`, `POST UpdateStatutoryDeclarationPayment` |
| Decisions | `POST decline`, `POST uphold`, `POST resolve` |
| Amendment / withdrawal | `POST OppositionAmendmentCost`, `POST OppositionAmendment`, `POST TreatAmendment`, `POST NewOppositionWithdrawal`, `POST UpdateOppositionWithdrawalPayment`, `POST TreatWithdrawal` |
| Letters | `POST generate`, `GET oppositionAcknowledgementLetter`, `GET counterStatementLetter`, `GET statutoryDeclarationLetter` |
| Maintenance | `POST backfillPaymentIds`, `POST backfillSdStatuses`, `POST backfillWithdrawnFiles`, `POST backfillWithdrawnApplicationHistory`, `GET debugWithdrawnOppositions` |

### 3.8 Publication — `api/publication`

`GET GetPublication`, `GET GetTrademarkPublication`, `POST SavePublication`,
`POST BatchJournal` (restricted to `TrademarkRegistrar`, `ActingTrademarkRegistrar`,
`TrademarkPublication`, `SuperAdmin`), `GET GetJournals`, `GET GetJournalCost`,
`POST UpdateRequestStatus`.

### 3.9 Letters — `api/letters`

`GET generate`, `GET GetDocuments`, `GET verify-trademark`.

### 3.10 Tickets — `api/tickets`

`POST Create`, `GET {id}`, `POST TicketSummaries`, `POST CloseTicket`,
`POST DeleteTicket`, `POST AddMessage`, `GET GetStats`, `POST Escalate`, `POST Search`.

### 3.11 Notifications — `api/notifications`

`GET GetNotifications`, `GET UnreadCount`, `POST {id}/read`, `POST read-all`,
`POST api/notifications` (create — `SuperAdmin` only).

Realtime delivery is pushed over the SignalR hub `/hubs/notifications`.

### 3.12 Finance — `api/finance`

`POST GetFinanceSummary`.

### 3.13 Migration — `api/migration`

`GET GetMarkInfo`, `GET GetPaymentInfo`, `POST ClaimRequest`, `GET GetAllClaimRequests`,
`GET GetClaimRequest`, `POST AdminUploadAttach` — used to claim legacy records into the portal.

### 3.14 Statistics — `api`

| Method | Route |
| --- | --- |
| GET | `api/statistics/performance/staff` |
| GET | `api/statistics/performance/units` |
| POST | `api/statistics/performance/staff/compare` |
| POST | `api/statistics/performance/units/compare` |
| POST | `api/statistics/finance/compare` |
| POST | `api/statistics/finance/techfee/compare` |
| POST | `api/statistics/operational/compare` |
| POST | `api/statistics/support/performance/compare` |
| POST | `api/statistics/cache/invalidate` |
| GET | `api/units` |
| GET | `api/staff` |

Statistics responses are cached through the distributed cache (Redis when configured);
`cache/invalidate` clears them.

---

## 4. Project layout

```
patentdesign/
├── Program.cs           # host, config, DI, auth, pipeline
├── Controllers/         # HTTP endpoints (one per domain area)
├── Services/            # business logic, injected as scoped services
├── Models/              # Models.cs, APITypes.cs, Helpers.cs, Enums.cs
├── Dtos/                # request/response contracts
├── Utils/               # PaymentUtils, ResendUtils, NotificationJob, PublishTrademarkJob
└── assets/              # fonts, logos, certificate artwork (copied to output)
```

Registered scoped services: `AuthServices`, `AdminServices`, `UsersService`,
`FilesServices`, `LettersServices`, `TicketServices`, `FinanceService`,
`AssignmentService`, `PaymentService`, `MigrationService`, `EmailServices`,
`OppositionService`, `StatisticsService`, `PublicationServices`,
`NotificationServices`, `PaymentUtils`, `ResendUtils`, `IResend`.

`IMongoClient` and `IMongoDatabase` are registered as singletons — services inject
`IMongoDatabase` rather than creating their own client.

---

## 5. Conventions & notes

- JSON binding is case-insensitive (`PropertyNameCaseInsensitive = true`).
- Many "list"/"search" endpoints are `POST` because they accept rich filter bodies.
- `backfill*` and `diag/*` endpoints are maintenance utilities; they are not part of the
  normal client flow and should be restricted in production.
- `UseForwardedHeaders` honours `X-Forwarded-For` / `X-Forwarded-Proto` behind the reverse proxy,
  and HTTPS redirection is always enabled.
- Swagger is exposed only in Development; use the generated XML documentation file for
  endpoint-level detail when updating this document.
