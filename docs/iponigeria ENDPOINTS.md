# IPON API — Endpoint Reference

Request/response reference for the `patentdesign` ASP.NET Core 8 Web API.

- **Base URL (dev):** `https://localhost:7xxx`
- **Base URL (test):** `https://testbackend.iponigeria.com`
- **Base URL (prod):** `https://integration.iponigeria.com`
- **Swagger (Development only):** `/swagger`

---

## Conventions

### Authentication

All endpoints require a JWT bearer token unless the table says *anonymous*.

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json
```

For the SignalR hub pass the token on the query string instead:
`/hubs/notifications?access_token=<token>`

### Response shapes

Three shapes are used across the API:

1. **Raw payload** — the resource itself (most `GET` endpoints).
2. **Message envelope** — `{ "message": "..." }` for commands.
3. **Typed envelope** — `ApiResponse<T>` / statistics style:

```json
{ "success": true, "message": null, "data": { } }
```

### Error responses

| Status | Shape | When |
| --- | --- | --- |
| 400 | `"Failed to Register"` or `{ "message": "..." }` or `{ "success": false, "error": "..." }` | Validation / business-rule failure. |
| 401 | `"Invalid email or password"` | Missing/invalid token or bad credentials. |
| 403 | empty | Authenticated but role not permitted. |
| 404 | `"File Not Found"` | Resource missing. |
| 500 | ProblemDetails (RFC 7807) | Unhandled exception. |

### Enums

Enums are serialized as **strings**: `ApplicationStatuses`, `FormApplicationTypes`,
`FileTypes`, `Roles`, `TicketState`, `NotificationCategory`, `NotificationAudience`,
`NotificationPriority`, `TradeMarkType`, `DesignTypes`, `PatentTypes`.

---

## 1. Auth — `/api/auth`

| Method | Path | Auth |
| --- | --- | --- |
| POST | `/api/auth/register` | anonymous |
| POST | `/api/auth/login` | anonymous |
| POST | `/api/auth/refresh` | anonymous |
| POST | `/api/auth/logout` | bearer |
| POST | `/api/auth/transfer` | anonymous |
| POST | `/api/auth/ResetPasswordRequest?email=` | anonymous |
| POST | `/api/auth/ResetPassword` | anonymous |
| POST | `/api/auth/VerifyEmailRequest?email=` | anonymous |
| POST | `/api/auth/ResendVerificationEmail` | anonymous |
| POST | `/api/auth/VerifyEmail?email=&token=` | anonymous |
| POST | `/api/auth/change-password` | bearer |
| POST | `/api/auth/UpdateProfile` | bearer |
| GET | `/api/auth/GetUser?userId=` | bearer |

### POST `/api/auth/register`

Creates a portal account. `accountType` is a numeric `AccountType` value;
`businessName` is only relevant for corporate accounts.

```json
{
  "firstName": "Ada",
  "lastName": "Obi",
  "email": "ada.obi@example.com",
  "businessName": "Obi & Co IP",
  "accountType": 1,
  "phone": "08030000000",
  "password": "S3cure!Passw0rd"
}
```

`200 OK`

```json
{ "message": "User created successfully" }
```

`400 Bad Request` → `"Failed to Register"`

### POST `/api/auth/login`

```json
{ "email": "ada.obi@example.com", "password": "S3cure!Passw0rd" }
```

`200 OK` — `AuthUserDto`

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "f1f0a6d6-3a0f-4c6a-9d3f-6f6d2a0e1b77",
  "user": {
	"id": "66f0a1c2d4e5f60012345678",
	"creatorId": null,
	"firstName": "Ada",
	"lastName": "Obi",
	"email": "ada.obi@example.com",
	"phoneNumber": "08030000000",
	"userRoles": ["User"],
	"accountType": "Individual",
	"createdAt": "2025-01-15T09:12:44Z",
	"lastUpdatedAt": "2025-03-02T11:01:09Z"
  }
}
```

`401 Unauthorized` → `"Invalid email or password"`

### POST `/api/auth/refresh`

```json
{ "refreshToken": "f1f0a6d6-3a0f-4c6a-9d3f-6f6d2a0e1b77" }
```

`200 OK` — same `AuthUserDto` shape as login.
`401 Unauthorized` → `"Invalid or expired refresh token"`

### POST `/api/auth/logout`

No body. The user id is read from the `NameIdentifier` claim.

`200 OK` → `{ "message": "Logged out successfully" }`

### POST `/api/auth/ResetPassword`

```json
{
  "email": "ada.obi@example.com",
  "token": "3f29b1c0a84e4c",
  "newPassword": "N3wS3cure!Pass"
}
```

`200 OK` → `{ "message": "Password reset successful" }`

### POST `/api/auth/change-password`

```json
{
  "email": "ada.obi@example.com",
  "newPassword": "N3wS3cure!Pass",
  "confirmPassword": "N3wS3cure!Pass"
}
```

`confirmPassword` must equal `newPassword` (`[Compare]`).
`200 OK` → `{ "message": "Password changed successfully" }`

### POST `/api/auth/ResendVerificationEmail`

Body is a bare JSON string:

```json
"ada.obi@example.com"
```

`200 OK` → `{ "message": "Verification email resent" }`

### POST `/api/auth/UpdateProfile`

```json
{
  "userId": "66f0a1c2d4e5f60012345678",
  "firstName": "Ada",
  "lastName": "Obi",
  "name": "Ada Obi",
  "email": "ada.obi@example.com",
  "phoneNumber": "08030000000",
  "address": "12 Marina Road, Lagos",
  "nationality": "Nigerian",
  "state": "Lagos",
  "accountType": "Individual",
  "userRoles": ["User"]
}
```

`200 OK` → `{ "message": "Profile Updated Successfully" }`

### GET `/api/auth/GetUser?userId=66f0a1c2d4e5f60012345678`

`200 OK` — user document. `404 Not Found` → `"Failed to fetch User Details"`

---

## 2. Users — `/api/users`

| Method | Path | Query / Body |
| --- | --- | --- |
| GET | `/api/users/SearchNameId` | `?nameId=&email=` (both optional) |
| POST | `/api/users/LoadUsers` | `GetUsersRequest` |
| PUT | `/api/users/UpdateUserRoles` | `UserRoleDto` |
| POST | `/api/users/GetAllUsers` | `GetUsersDto` |
| GET | `/api/users/GetUserById` | `?id=` |
| GET | `/api/users/GetOtherApplications` | `?userId=` |

### POST `/api/users/GetAllUsers`

```json
{
  "name": "obi",
  "roles": ["TrademarkExaminer", "TrademarkStaff"],
  "skip": 0,
  "take": 10
}
```

`200 OK` — `PaginatedUsersDto`

```json
{
  "users": [
	{
	  "id": "66f0a1c2d4e5f60012345678",
	  "name": "Ada Obi",
	  "email": "ada.obi@example.com",
	  "phoneNumber": "08030000000",
	  "userRoles": ["TrademarkExaminer"]
	}
  ],
  "totalCount": 37
}
```

### PUT `/api/users/UpdateUserRoles`

```json
{
  "userId": "66f0a1c2d4e5f60012345678",
  "addRoles": ["TrademarkExaminer"],
  "removeRoles": ["User"]
}
```

`200 OK` — updated user.

---

## 3. Admin — `/api/admin`

| Method | Path | Role |
| --- | --- | --- |
| POST | `/api/admin/ChangeStatus` | bearer |
| POST | `/api/admin/CreateApplicationHistory` | `SuperAdmin` |
| POST | `/api/admin/ApplicationHistory` | `SuperAdmin` |
| GET | `/api/admin/ApplicationHistory/{applicationId}` | `SuperAdmin` |
| PATCH | `/api/admin/ApplicationHistory` | `SuperAdmin` |
| DELETE | `/api/admin/ApplicationHistory` | `SuperAdmin` |
| GET | `/api/admin/diag/assignment?fileNumber=` | anonymous |
| POST | `/api/admin/SendAnnouncement` | bearer |
| POST | `/api/admin/ResetPassword?email=` | bearer |
| POST | `/api/admin/UploadSignature` | bearer (multipart) |
| GET | `/api/admin/GetUserByEmail?email=` | bearer |

### POST `/api/admin/ChangeStatus`

```json
{
  "newStatus": "Approved",
  "userId": "66f0a1c2d4e5f60012345678",
  "fileId": "66f1bb02d4e5f60012349999",
  "reason": "Examination completed, no objections."
}
```

### POST `/api/admin/CreateApplicationHistory`

`newValue` may carry an `attachments` array of
`{ fileName, contentType, data: "<base64>" }`; the service stores the binary and replaces
`data` with a downloadable `url`.

```json
{
  "applicationDate": "2025-02-11T00:00:00Z",
  "applicationType": "Assignment",
  "currentStatus": "Pending",
  "userId": "66f0a1c2d4e5f60012345678",
  "paymentId": "340012345678",
  "certificatePaymentId": null,
  "fileNumber": "TM/2024/12345",
  "oldValue": { "applicant": "Obi & Co IP" },
  "newValue": {
	"applicant": "Zenith Holdings Ltd",
	"attachments": [
	  { "fileName": "deed.pdf", "contentType": "application/pdf", "data": "JVBERi0xLjQK..." }
	]
  }
}
```

### PATCH `/api/admin/ApplicationHistory`

```json
{
  "fileNumber": "TM/2024/12345",
  "applicationId": "66f3cc44d4e5f60012341111",
  "applicationDate": "2025-02-12T00:00:00Z",
  "applicationType": "Assignment",
  "currentStatus": "Approved",
  "paymentId": "340012345678",
  "certificatePaymentId": "340099998888"
}
```

### DELETE `/api/admin/ApplicationHistory`

Body carries the identifiers (`DeleteApplicationHistoryDto`), e.g.:

```json
{ "fileNumber": "TM/2024/12345", "applicationId": "66f3cc44d4e5f60012341111" }
```

### POST `/api/admin/SendAnnouncement`

```json
{ "subject": "Registry downtime", "message": "The portal will be offline on Sunday 02:00–04:00." }
```

### POST `/api/admin/UploadSignature` (multipart/form-data)

| Field | Type |
| --- | --- |
| `name` | text |
| `designation` | text |
| `signature` | file (image) |
| `applicationTypes` | repeated text (`FormApplicationTypes`) |

```http
POST /api/admin/UploadSignature
Content-Type: multipart/form-data; boundary=----X

------X
Content-Disposition: form-data; name="name"

Ngozi Eze
------X
Content-Disposition: form-data; name="designation"

Registrar of Trademarks
------X
Content-Disposition: form-data; name="applicationTypes"

NewApplication
------X
Content-Disposition: form-data; name="signature"; filename="sig.png"
Content-Type: image/png

<binary>
------X--
```

---

## 4. Applications / Files — `/api/files`

Largest surface in the API. Verified core endpoints below; the remaining actions follow the
same conventions and are listed in Swagger.

| Method | Path | Body / Query |
| --- | --- | --- |
| POST | `/api/files` | `Filling` |
| GET | `/api/files/{id}` | — |
| DELETE | `/api/files/{id}` | — |
| POST | `/api/files/summary?index=&quantity=` | `SummaryRequestObj` |
| POST | `/api/files/createNew` | `NewCreation1` |
| POST | `/api/files/uploadAttachment` | `List<TT>` |
| POST | `/api/files/SaveDataUpdate` | `DataUpdateReq` |
| GET | `/api/files/CertificatePayment?id=&userId=` | — |
| POST | `/api/files/CertificateValidate?fileId=&rrr=&userId=&userName=` | — |
| GET | `/api/files/GetRRRCost?rrr=` | — |
| GET | `/api/files/search` | query filters |
| GET | `/api/files/FileStatistics` | — |
| POST | `/api/files/UpdateApplicationStatus` | status payload |
| POST | `/api/files/CreateFileRenewal` | renewal payload |
| GET | `/api/files/RenewalCost` | — |
| GET | `/api/files/GetAvailabilitySearch` / `AvailabilitySearchCost` | — |
| POST | `/api/files/newStatusRequest`, `GET GetStatusRequests`, `POST UpdateStatusRequest` | status-request workflow |
| POST | `/api/files/ReAssign`, `AdminUpdateApplication`, `ManualPaymentUpdate` | staff/admin operations |

### POST `/api/files/createNew`

`file` is a **JSON-encoded string** of the `Filling` document; `attachments` carries the
supporting documents.

```json
{
  "file": "{\"fileType\":\"Trademark\",\"applicantName\":\"Obi & Co IP\",\"className\":\"35\",\"markName\":\"OBICARE\"}",
  "attachments": [
	{ "fileName": "poa.pdf", "contentType": "application/pdf", "data": "JVBERi0xLjQK..." }
  ]
}
```

`201 Created` with `Location: /api/files/{id}` and the created `Filling` as body.

### GET `/api/files/{id}`

`200 OK` — the `Filling` document (shape varies by `fileType`):

```json
{
  "id": "66f1bb02d4e5f60012349999",
  "fileNumber": "TM/2024/12345",
  "fileType": "Trademark",
  "status": "Pending",
  "applicantName": "Obi & Co IP",
  "paymentId": "340012345678",
  "createdAt": "2025-01-20T10:11:12Z"
}
```

### DELETE `/api/files/{id}`

`204 No Content` on success, `404 Not Found` otherwise.

### POST `/api/files/summary?index=0&quantity=20`

Paging is on the query string, filters in the body (`SummaryRequestObj`):

```json
{ "userId": "66f0a1c2d4e5f60012345678", "fileType": "Trademark", "status": "Pending" }
```

`200 OK` — paginated summary list.

### GET `/api/files/CertificatePayment?id=66f1bb02...&userId=66f0a1c2...`

`200 OK` — certificate fee breakdown, e.g. `{ "cost": "30000", "rrr": "340012345678" }`

### POST `/api/files/CertificateValidate?fileId=&rrr=&userId=&userName=`

All four values are query parameters; no body. Returns the validation result from Remita.

### POST `/api/files/uploadAttachment`

```json
[
  { "fileName": "deed.pdf", "contentType": "application/pdf", "data": "JVBERi0xLjQK..." }
]
```

`200 OK` — stored attachment metadata (including download URLs).

---

## 5. Payments — `/api/payments`

| Method | Path | Parameters |
| --- | --- | --- |
| POST | `/api/payments/generate` | `?id=&name=&email=&number=` (query) |
| POST | `/api/payments/SaveOtherPayment` | `OtherPaymentModel` |
| GET | `/api/payments/GetOtherPayment` | `?count=&skip=&userId=` |
| POST | `/api/payments/UpdatePayment` | `PaymentServiceModel` |
| GET | `/api/payments/GetAllPayment` | — |
| POST | `/api/payments/AddPayment` | `PaymentServiceModel` |
| GET | `/api/payments/Check` | `?id=` (RRR) |

### POST `/api/payments/generate?id=66f1bb02...&name=Ada%20Obi&email=ada.obi@example.com&number=08030000000`

No body. Generates a Remita RRR for the application.

`200 OK`

```json
{ "rrr": "340012345678", "amount": 45000, "status": "Generated" }
```

### GET `/api/payments/Check?id=340012345678`

`200 OK` — Remita status lookup for the RRR.

### GET `/api/payments/GetOtherPayment?count=20&skip=0&userId=66f0a1c2...`

`userId` is optional; omit it to list across users.

---

## 6. Assignment — `/api/assignment`

| Method | Path | Body |
| --- | --- | --- |
| POST | `/api/assignment/generate` | `AssignmentTypeReq` |
| POST | `/api/assignment/SearchForFile` | `AssReq` |
| POST | `/api/assignment/UpdateAssignment` | `AssUpdateReq` |
| POST | `/api/assignment/PayAssignment` | `AssUpdateReq` |
| GET | `/api/assignment/ValidationRRR?data=` | — |

### POST `/api/assignment/generate`

```json
{
  "fileId": "66f1bb02d4e5f60012349999",
  "type": 0,
  "creatorAccount": "66f0a1c2d4e5f60012345678",
  "userName": "Ada Obi",
  "applicantName": "Zenith Holdings Ltd",
  "applicantEmail": "legal@zenith.example",
  "applicantNumber": "08031111111",
  "assignorName": "Obi & Co IP",
  "assignorAddress": "12 Marina Road, Lagos",
  "assignorCountry": "Nigeria",
  "assigneeName": "Zenith Holdings Ltd",
  "assigneeAddress": "5 Ikoyi Crescent, Lagos",
  "assigneeCountry": "Nigeria",
  "authorizationLetterUrl": "https://cdn.example/auth-letter.pdf",
  "deedOfAgreementUrl": "https://cdn.example/deed.pdf",
  "dateOfAssignment": "2025-03-01T00:00:00Z"
}
```

`200 OK` — generated assignment record (including the payment RRR).

### POST `/api/assignment/SearchForFile`

```json
{ "fileNumber": "TM/2024/12345", "userId": "66f0a1c2d4e5f60012345678" }
```

`200 OK` — list of matching summaries. `404 Not Found` → `"Invalid file Number"`

### GET `/api/assignment/ValidationRRR?data=340012345678`

`200 OK` → `{ "cost": "45000" }`

---

## 7. Opposition — `/api/opposition`

| Method | Path | Input |
| --- | --- | --- |
| GET | `/api/opposition/OppositionSearch?fileNumber=` | query |
| POST | `/api/opposition/NewOpposition` | **multipart** `OppositionRequestDto` |
| POST | `/api/opposition/StaffOpposition` | JSON `OppositionRequestDto` |
| POST | `/api/opposition/UpdateOppositionPayment` | query params |
| GET | `/api/opposition/GetAllOpposition` | — |
| GET | `/api/opposition/count` | — |
| GET | `/api/opposition/loadSummary` | query paging |
| GET | `/api/opposition/get?id=` | — |
| GET | `/api/opposition/getOppositionDetail?oppositionId=&fileNumber=` | — |
| GET | `/api/opposition/stats` | — |
| POST | `/api/opposition/notify?oppId=` | — |
| GET | `/api/opposition/csSearchFile?fileNumber=` / `CounterStatementSearch` | — |
| GET | `/api/opposition/getCounterStatementFee` | — |
| POST | `/api/opposition/NewCounterStatement` | **multipart** `CounterStatementRequestDto` |
| POST | `/api/opposition/UpdateCounterStatementPayment?paymentId=` | — |
| GET | `/api/opposition/StatutoryDeclarationSearch?oppositionId=&fileNumber=` | — |
| POST | `/api/opposition/NewStatutoryDeclaration` | **multipart** `StatutoryDeclarationRequestDto` |
| POST | `/api/opposition/UpdateStatutoryDeclarationPayment` | query params |
| POST | `/api/opposition/generate` | `GenerateOppositionPaymentDto` |
| POST | `/api/opposition/decline?oppositionId=` | — |
| POST | `/api/opposition/uphold?oppositionId=` | — |
| POST | `/api/opposition/resolve` | `ResolveOppositionDto` |
| POST | `/api/opposition/NewOppositionWithdrawal` | **multipart** `OppositionWithdrawalRequestDto` |
| POST | `/api/opposition/UpdateOppositionWithdrawalPayment` | query params |
| POST | `/api/opposition/TreatWithdrawal` | `TreatWithdrawalDto` |
| POST | `/api/opposition/OppositionAmendmentCost` | `OppositionAmendmentReq` |
| POST | `/api/opposition/OppositionAmendment` | `OppositionAmendmentDto` |
| POST | `/api/opposition/TreatAmendment` | `TreatRecordalDto` |
| GET | `/api/opposition/oppositionAcknowledgementLetter?oppositionId=&paymentId=` | PDF |
| GET | `/api/opposition/counterStatementLetter?counterStatementId=&paymentId=` | PDF |
| GET | `/api/opposition/statutoryDeclarationLetter?statutoryDeclarationId=&paymentId=` | PDF |
| POST | `/api/opposition/backfillPaymentIds`, `backfillSdStatuses`, `backfillWithdrawnFiles`, `backfillWithdrawnApplicationHistory` | maintenance |
| GET | `/api/opposition/debugWithdrawnOppositions` | maintenance |

### GET `/api/opposition/OppositionSearch?fileNumber=TM/2024/12345`

`200 OK` — the opposable trademark record plus opposition eligibility/fee data.

### POST `/api/opposition/NewOpposition` (multipart/form-data)

Public filing. Grounds/party fields are form fields, evidence documents are file parts.

```http
POST /api/opposition/NewOpposition
Content-Type: multipart/form-data; boundary=----X

------X
Content-Disposition: form-data; name="fileNumber"

TM/2024/12345
------X
Content-Disposition: form-data; name="opponentName"

Zenith Holdings Ltd
------X
Content-Disposition: form-data; name="grounds"

Likelihood of confusion with prior registration TM/2019/0011.
------X
Content-Disposition: form-data; name="attachments"; filename="evidence.pdf"
Content-Type: application/pdf

<binary>
------X--
```

`200 OK` — created opposition including `oppositionId` and payment RRR.

### POST `/api/opposition/StaffOpposition`

Same contract as above but JSON (staff-captured filings, no file upload).

### POST `/api/opposition/resolve`

```json
{
  "oppositionId": "66f5dd77d4e5f60012342222",
  "decision": "Upheld",
  "reason": "Opponent's prior rights established.",
  "userId": "66f0a1c2d4e5f60012345678"
}
```

### GET `/api/opposition/oppositionAcknowledgementLetter?oppositionId=66f5dd77...`

`200 OK` — `application/pdf` stream (`Content-Disposition: inline`).

---

## 8. Publication — `/api/publication`

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/api/publication/GetPublication?batchVolume=` | returns `journal.pdf` |
| GET | `/api/publication/GetTrademarkPublication?text=&index=&quantity=` | JSON list |
| POST | `/api/publication/SavePublication` | `PublicationDto` |
| POST | `/api/publication/BatchJournal` | `StaffBatchRequest`, roles: `TrademarkRegistrar`, `ActingTrademarkRegistrar`, `TrademarkPublication`, `SuperAdmin` |
| GET | `/api/publication/GetJournals?year=` | JSON list |
| GET | `/api/publication/GetJournalCost?userId=&batch=` | JSON |
| POST | `/api/publication/UpdateRequestStatus` | `{ appId, userId }` |

### GET `/api/publication/GetPublication?batchVolume=2025-01`

```http
200 OK
Content-Type: application/pdf
Content-Disposition: attachment; filename=journal.pdf
```

### POST `/api/publication/UpdateRequestStatus`

```json
{ "appId": "66f1bb02d4e5f60012349999", "userId": "66f0a1c2d4e5f60012345678" }
```

`200 OK` → `{ "success": true, "message": "Journal request approved" }`
`400 Bad Request` → `{ "message": "..." }`

### POST `/api/publication/SavePublication` / `BatchJournal`

`200 OK` with empty body on success; `400 Bad Request` → `{ "message": "<error>" }`.

---

## 9. Letters & documents — `/api/letters`

| Method | Path |
| --- | --- |
| GET | `/api/letters/generate?letterType=&fileId=&applicationId=&oppositionId=&rrr=` |
| GET | `/api/letters/GetDocuments?fileId=&paymentId=` |
| GET | `/api/letters/verify-trademark?fileId=` |

### GET `/api/letters/generate?letterType=2&fileId=66f1bb02d4e5f60012349999`

`letterType` is the **zero-based index** into the `ApplicationLetters` enum; it is required
and must be within range. At least one of `fileId` / `applicationId` / `oppositionId`
must identify the source record.

```http
200 OK
Content-Type: application/pdf
Content-Disposition: inline; filename=acknowledgement.pdf
```

| Status | Body |
| --- | --- |
| 400 | `"Invalid letter type"` |
| 404 | `"No letter could be generated for the provided parameters."` |
| 500 | `{ "message": "...", "stackTrace": "..." }` |

### GET `/api/letters/GetDocuments?fileId=66f1bb02...&paymentId=340012345678`

At least one of the two must be supplied.

`200 OK`

```json
[
  { "name": "Acknowledgement Letter", "type": "application/pdf", "url": "https://cdn.example/ack.pdf" },
  { "name": "Payment Receipt", "type": "application/pdf", "url": "https://cdn.example/receipt.pdf" }
]
```

### GET `/api/letters/verify-trademark?fileId=66f1bb02...`

`200 OK` — verification record. `404 Not Found` → `"File Not Found"`

---

## 10. Tickets — `/api/tickets`

| Method | Path | Body |
| --- | --- | --- |
| POST | `/api/tickets/Create` | `TicketInfo` |
| GET | `/api/tickets/{id}` | — |
| POST | `/api/tickets/TicketSummaries` | `TicketsSummariesType` |
| POST | `/api/tickets/CloseTicket` | `ResolveTicketType` |
| POST | `/api/tickets/DeleteTicket` | — |
| POST | `/api/tickets/AddMessage` | `NewCorrespondenceType` |
| GET | `/api/tickets/GetStats` | `?userId=&category=&registryCategory=&raisedByRegistryStaff=` |
| POST | `/api/tickets/Escalate` | `EscalateTicketRequest` |
| POST | `/api/tickets/Search` | `TicketSearchRequest` |

### POST `/api/tickets/Create`

```json
{
  "id": "66f6ee88d4e5f60012343333",
  "creatorId": "66f0a1c2d4e5f60012345678",
  "fileNumber": "TM/2024/12345",
  "subject": "Status of my trademark application",
  "category": 1,
  "state": "Open",
  "correspondence": [
	{ "senderId": "66f0a1c2d4e5f60012345678", "message": "Any update on my filing?" }
  ]
}
```

`201 Created` with `Location: /api/tickets/{id}` and the ticket as body.

### POST `/api/tickets/Search`

Exactly one of `ticketNumber` / `fileNumber` must be supplied.

```json
{ "ticketNumber": "TCK-000123", "userId": "66f0a1c2d4e5f60012345678" }
```

| Status | Body |
| --- | --- |
| 200 | list of `TicketSummary` |
| 400 | `"Provide either ticketNumber or fileNumber."` |
| 400 | `"Search by either ticketNumber or fileNumber, not both."` |

### POST `/api/tickets/AddMessage`

```json
{
  "ticketId": "66f6ee88d4e5f60012343333",
  "senderId": "66f0a1c2d4e5f60012345678",
  "message": "Attaching the proof of payment.",
  "attachments": []
}
```

`200 OK` — the updated ticket.

### POST `/api/tickets/Escalate`

```json
{
  "ticketId": "66f6ee88d4e5f60012343333",
  "targetCategory": 2,
  "escalatedBy": "66f0a1c2d4e5f60012345678",
  "reason": "Requires registrar attention."
}
```

`200 OK` — updated ticket. `404 Not Found` when the ticket does not exist.

### GET `/api/tickets/GetStats?userId=66f0a1c2...&category=1&raisedByRegistryStaff=false`

All parameters optional.

`200 OK` → `{ "open": 12, "inProgress": 3, "closed": 44 }`

---

## 11. Notifications — `/api/notifications`

| Method | Path | Auth |
| --- | --- | --- |
| GET | `/api/notifications/GetNotifications?userId=` | bearer |
| GET | `/api/notifications/UnreadCount?userId=` | bearer |
| POST | `/api/notifications/{id}/read` | bearer |
| POST | `/api/notifications/read-all?userId=` | bearer |
| POST | `/api/notifications` | `SuperAdmin` |

### GET `/api/notifications/GetNotifications?userId=66f0a1c2...`

`200 OK`

```json
{
  "notifications": [
	{
	  "id": "66f7ff99d4e5f60012344444",
	  "title": "Application approved",
	  "message": "TM/2024/12345 has been approved.",
	  "category": "Application",
	  "priority": "High",
	  "audience": "User",
	  "actionUrl": "/applications/66f1bb02d4e5f60012349999",
	  "isRead": false,
	  "createdAt": "2025-03-10T08:00:00Z"
	}
  ],
  "unreadCount": 1
}
```

### GET `/api/notifications/UnreadCount?userId=...` → `{ "unreadCount": 4 }`

### POST `/api/notifications/{id}/read` → `{ "message": "Notification marked as read" }`

### POST `/api/notifications/read-all?userId=...` → `{ "message": "All notifications marked as read" }`

### POST `/api/notifications` *(SuperAdmin)*

```json
{
  "audience": "AllUsers",
  "category": "System",
  "priority": "Normal",
  "title": "Scheduled maintenance",
  "message": "The portal will be unavailable on Sunday 02:00–04:00.",
  "recipientId": null,
  "actionUrl": null,
  "expiresAt": "2025-04-01T00:00:00Z",
  "createdBy": "66f0a1c2d4e5f60012345678",
  "fileNumber": null,
  "fileType": null,
  "previousStatus": null,
  "newStatus": null,
  "applicationType": null,
  "applicationId": null
}
```

`200 OK` → `{ "message": "Notification created" }`

### Realtime — `/hubs/notifications`

```js
const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://api.iponigeria.com/hubs/notifications", { accessTokenFactory: () => token })
  .build();
connection.on("ReceiveNotification", n => console.log(n));
await connection.start();
```

---

## 12. Migration (legacy claims) — `/api/migration`

| Method | Path | Input |
| --- | --- | --- |
| GET | `/api/migration/GetMarkInfo?regNumber=` | query |
| GET | `/api/migration/GetPaymentInfo?paymentId=` | query |
| POST | `/api/migration/ClaimRequest` | **multipart** |
| GET | `/api/migration/GetAllClaimRequests` | — |
| GET | `/api/migration/GetClaimRequest?fileId=` | — |
| POST | `/api/migration/AdminUploadAttach` | `AdminUploadAttachmentDto` |
| POST | `/api/migration/migrate?fileId=` | query |

### POST `/api/migration/ClaimRequest` (multipart/form-data)

| Field | Type | Notes |
| --- | --- | --- |
| `attachments` | file[] | Supporting evidence |
| `markInfo` | text | **JSON string** of `MarkInfoDto[]` |

```
markInfo = [{"regNumber":"TM/2011/00987","markName":"OBICARE","applicant":"Obi & Co IP"}]
```

`200 OK` → `{ "message": "Claim submitted successfully" }`

### POST `/api/migration/migrate?fileId=66f1bb02...`

`200 OK` → `{ "success": true }` · `400 Bad Request` → `{ "message": "<error>" }`

### GET `/api/migration/GetMarkInfo?regNumber=TM/2011/00987`

`200 OK` — legacy mark record. `400 Bad Request` → `{ "message": "<error>" }`

---

## 13. Finance — `/api/finance`

### POST `/api/finance/GetFinanceSummary`

Body is a `FinanceQueryType` (registry/date-range filter):

```json
{ "registryType": "Trademark", "startDate": "2025-01-01", "endDate": "2025-03-31" }
```

`200 OK` — `List<FinanceSummaryType>`

```json
[
  { "label": "January 2025", "totalAmount": 18450000, "transactionCount": 412 },
  { "label": "February 2025", "totalAmount": 20110000, "transactionCount": 455 }
]
```

---

## 14. Statistics — `/api`

| Method | Path | Input |
| --- | --- | --- |
| GET | `/api/statistics/performance/staff` | `?registryType=&unitId=&periodType=&periodValue=&year=` (**all required**) |
| GET | `/api/statistics/performance/units` | query filters |
| POST | `/api/statistics/performance/staff/compare` | `StaffPerformanceComparisonRequestDto` |
| POST | `/api/statistics/performance/units/compare` | `UnitPerformanceComparisonRequestDto` |
| POST | `/api/statistics/finance/compare` | `FinanceComparisonRequestDto` |
| POST | `/api/statistics/finance/techfee/compare` | `FinanceComparisonRequestDto` |
| POST | `/api/statistics/operational/compare` | `OperationalComparisonRequestDto` |
| POST | `/api/statistics/support/performance/compare` | `SupportPerformanceRequestDto` |
| POST | `/api/statistics/cache/invalidate` | — |
| GET | `/api/units` | — |
| GET | `/api/staff` | `?unitId=&registryType=` |

All statistics endpoints use the `{ success, data }` / `{ success, error }` envelope.

### GET `/api/statistics/performance/staff?registryType=Trademark&unitId=3&periodType=quarter&periodValue=Q1&year=2025`

`200 OK`

```json
{
  "success": true,
  "data": {
	"unitId": 3,
	"period": "Q1 2025",
	"totalProcessed": 1280,
	"staff": [
	  { "userId": "66f0a1c2d4e5f60012345678", "name": "Ada Obi", "processed": 215, "averageDays": 4.2 }
	]
  }
}
```

`400 Bad Request` when any required parameter is missing:

```json
{ "success": false, "error": "Missing required parameter: registryType" }
```

### Period objects

Every `*/compare` endpoint takes a `periods` array of `FinancePeriodRequestDto`. Supply the
fields relevant to the chosen `type` (`year`, `quarter`, `month`, `range`, `offset`, ...):

```json
{
  "registryType": "Trademark",
  "periods": [
	{ "type": "quarter", "value": "Q1", "year": 2025, "label": "Q1 2025" },
	{ "type": "quarter", "value": "Q1", "year": 2024, "label": "Q1 2024" },
	{ "type": "range", "startDate": "2024-07-01", "endDate": "2024-12-31", "label": "H2 2024" },
	{ "type": "offset", "startOffset": -3, "endOffset": 0, "offsetUnit": "month", "label": "Last 3 months" }
  ]
}
```

### POST `/api/statistics/performance/staff/compare`

```json
{
  "registryType": "Trademark",
  "unitId": 3,
  "periods": [
	{ "type": "quarter", "value": "Q1", "year": 2025, "label": "Q1 2025" },
	{ "type": "quarter", "value": "Q4", "year": 2024, "label": "Q4 2024" }
  ]
}
```

### POST `/api/statistics/support/performance/compare`

```json
{
  "scope": "TrademarkSupport",
  "periods": [{ "type": "month", "value": "03", "year": 2025, "label": "March 2025" }]
}
```

### POST `/api/statistics/cache/invalidate`

No body. Clears cached statistics (Redis or in-memory).

`200 OK` → `{ "success": true }`

---

## 15. Quick cURL recipes

Login and capture a token:

```powershell
$body = '{"email":"ada.obi@example.com","password":"S3cure!Passw0rd"}'
$res = Invoke-RestMethod -Uri "https://localhost:7001/api/auth/login" -Method Post -Body $body -ContentType "application/json"
$token = $res.token
```

Call an authenticated endpoint:

```powershell
Invoke-RestMethod -Uri "https://localhost:7001/api/notifications/UnreadCount?userId=$($res.user.id)" `
  -Headers @{ Authorization = "Bearer $token" }
```

Download a letter:

```powershell
Invoke-WebRequest -Uri "https://localhost:7001/api/letters/generate?letterType=2&fileId=66f1bb02d4e5f60012349999" `
  -Headers @{ Authorization = "Bearer $token" } -OutFile acknowledgement.pdf
```
