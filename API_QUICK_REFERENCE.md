# Quick Reference - Backend APIs

## Assignment Editability Endpoint

### Update Assignor/Assignee Information

```http
POST /api/files/UpdateAssignmentHistory
Content-Type: application/json

{
	"fileNumber": "TM2025/001234",
	"applicationId": "550e8400-e29b-41d4-a716-446655440000",
	"assignorName": "John Smith",
	"assignorEmail": "john@example.com",
	"assignorPhone": "+234901234567",
	"assignorNationality": "NG",
	"assignorAddress": "123 Main Street, Lagos",
	"assignorCountry": "Nigeria",
	"assigneeName": "Jane Doe",
	"assigneeEmail": "jane@example.com",
	"assigneePhone": "+234909876543",
	"assigneeNationality": "US",
	"assigneeAddress": "456 Oak Avenue, New York",
	"assigneeCountry": "United States",
	"dateOfAssignment": "2025-01-15"
}
```

**Response (Success):**
```json
{
	"success": true
}
```

**Response (Error):**
```json
{
	"success": false,
	"message": "File or assignment history entry not found."
}
```

**Status Codes:**
- `200 OK` - Successfully updated
- `404 Not Found` - File or application not found

---

## Design Creator Deletion Endpoint

### Update Filing with Modified Creators

```http
PUT /api/files/update-filing
Content-Type: application/json

{
	"fileId": "DESIGN2025/001234",
	"designCreators": [
		{
			"id": "creator-001",
			"name": "Alice Johnson",
			"email": "alice@example.com",
			"phone": "+234901234567",
			"address": "123 Design Street",
			"country": "Nigeria",
			"state": "Lagos"
		},
		{
			"id": "creator-003",
			"name": "Charlie Brown",
			"email": "charlie@example.com",
			"phone": "+234903333333",
			"address": "789 Art Avenue",
			"country": "Nigeria",
			"state": "Abuja"
		}
	],
	"updatedBy": "admin@ipon.com"
}
```

**Response (Success):**
```json
{
	"status": "SUCCESS",
	"message": "Filing record updated successfully.",
	"updatedFile": {
		"fileId": "DESIGN2025/001234",
		"designCreators": [
			{
				"id": "creator-001",
				"name": "Alice Johnson",
				...
			},
			{
				"id": "creator-003",
				"name": "Charlie Brown",
				...
			}
		],
		...
	}
}
```

**Response (Error):**
```json
{
	"status": "ERROR",
	"message": "Filing record not found"
}
```

**Status Codes:**
- `200 OK` - Successfully updated
- `404 Not Found` - Filing not found
- `400 Bad Request` - Invalid input

---

## Data Verification Endpoints

### Get Updated File Details

```http
GET /api/files/GetAllFileDetails?fileNumber=DESIGN2025/001234
```

**Response:**
```json
{
	"fileId": "DESIGN2025/001234",
	"type": "Design",
	"designCreators": [
		{
			"id": "creator-001",
			"name": "Alice Johnson",
			"email": "alice@example.com",
			...
		},
		{
			"id": "creator-003",
			"name": "Charlie Brown",
			"email": "charlie@example.com",
			...
		}
	],
	"applicationHistory": [
		{
			"id": "app-001",
			"assignment": {
				"id": "assign-001",
				"assignorName": "John Smith",
				"assignorEmail": "john@example.com",
				"assigneeName": "Jane Doe",
				"assigneeEmail": "jane@example.com",
				...
			},
			...
		}
	],
	...
}
```

---

## PDF Letter Generation

### Get Available Documents

```http
GET /api/letters/GetDocuments?fileId=DESIGN2025/001234&paymentId=PAY2025001
```

**Response:**
```json
{
	"applicationId": "app-001",
	"paymentId": "PAY2025001",
	"documents": [
		0,    // NewApplicationAcknowledgement
		6,    // NewApplicationAcceptance
		7,    // NewApplicationRejection
		...
	],
	"oppositionId": null
}
```

### Generate Letter PDF

```http
GET /api/letters/generate?fileId=DESIGN2025/001234&letterType=6&applicationId=app-001
```

**Response:**
```
[Binary PDF Data]
Content-Disposition: inline; filename=NewApplicationAcceptance_DESIGN2025001234.pdf
Content-Type: application/pdf
```

**Letter Types for Design:**
- `0` - NewApplicationAcknowledgement
- `6` - NewApplicationAcceptance  
- `7` - NewApplicationRejection

---

## Common Workflows

### Workflow 1: Update Assignment Details

```
1. Get current file details
   GET /api/files/GetAllFileDetails?fileNumber=TM2025/001234
   → Note the applicationId and current assignor/assignee details

2. Update specific fields
   POST /api/files/UpdateAssignmentHistory
   {
	   "fileNumber": "TM2025/001234",
	   "applicationId": "<from step 1>",
	   "assignorName": "New Name"
	   // Other fields null to preserve existing
   }
   → Receive { success: true }

3. Verify update
   GET /api/files/GetAllFileDetails?fileNumber=TM2025/001234
   → Confirm assignor name is updated
```

### Workflow 2: Delete Design Creator

```
1. Get current file details
   GET /api/files/GetAllFileDetails?fileNumber=DESIGN2025/001234
   → Note all current creators

2. Remove desired creator and send complete array
   PUT /api/files/update-filing
   {
	   "fileId": "DESIGN2025/001234",
	   "designCreators": [
		   { creator 1 },
		   { creator 3 }
		   // Creator 2 omitted = deleted
	   ],
	   "updatedBy": "admin"
   }
   → Receive { status: "SUCCESS", updatedFile: {...} }

3. Verify deletion
   GET /api/files/GetAllFileDetails?fileNumber=DESIGN2025/001234
   → Confirm creator 2 is removed

4. Check PDF reflects deletion
   GET /api/letters/generate?fileId=DESIGN2025/001234&letterType=6
   → PDF does NOT show deleted creator
```

### Workflow 3: Edit Assignment AND Delete Creators

```
1. Get current file (has both assignment and creators)
   GET /api/files/GetAllFileDetails?fileNumber=FILE123

2. Update assignment
   POST /api/files/UpdateAssignmentHistory
   { fileNumber, applicationId, assignorName, ... }

3. Delete creators
   PUT /api/files/update-filing
   { fileId, designCreators: [...] }

4. Verify both changes
   GET /api/files/GetAllFileDetails?fileNumber=FILE123
   → Shows updated assignment AND reduced creator list

5. Generate fresh documents
   GET /api/letters/GetDocuments?fileId=FILE123&paymentId=PAY123
   → Shows available letters

   GET /api/letters/generate?fileId=FILE123&letterType=6
   → PDF reflects both changes
```

---

## Error Handling

### Common Errors and Solutions

| Error | Cause | Solution |
|-------|-------|----------|
| `File or assignment history entry not found` | Invalid fileNumber or applicationId | Verify IDs match actual records |
| `Filing record not found` | Invalid fileId in update-filing | Check fileId matches database |
| `400 Bad Request` | Missing required fields | Include fileNumber, applicationId |
| `404 Not Found` | Resource doesn't exist | Verify file/app exists before update |
| `500 Internal Server Error` | Server issue | Check logs and retry |

---

## HTTP Headers

**Recommended for all requests:**
```http
Content-Type: application/json
Accept: application/json
```

**For PDF endpoints:**
```http
GET /api/letters/generate?...
Accept: application/pdf
```

---

## Pagination & Filters

**Not applicable to these endpoints** - Always fetch full records

---

## Rate Limiting

No rate limiting applied - Safe for production use

---

## Caching

**GET /api/files/GetAllFileDetails** - No caching, always fresh from DB
**GET /api/letters/generate** - Generated on-demand, no cache
**POST /api/files/UpdateAssignmentHistory** - Immediate persistence

---

## Field Validation

### UpdateAssignmentHistoryDto
- `fileNumber` (string, required) - TM/PATENT/DESIGN number
- `applicationId` (string, required) - UUID format
- `assignorName` (string, max 255 chars, optional)
- `assignorEmail` (string, valid email format, optional)
- `assignorPhone` (string, optional)
- Email validation: Standard RFC 5322

### DesignCreators Array
- Each creator requires: id, name, email, phone, address, country
- Array can be empty
- IDs must be unique within array
- Email format validated

---

## Rate of Change Limits

| Operation | Limit | Notes |
|-----------|-------|-------|
| UpdateAssignmentHistory | Unlimited | Single update per request |
| update-filing | Unlimited | Replaces entire document |
| GetAllFileDetails | Unlimited | Read-only |
| GenerateLetter | Unlimited | Generated on-demand |

---

## Timestamps

- All responses use UTC/ISO 8601 format
- Database stores timestamps in UTC
- No timezone conversion on client needed

---

## Testing Endpoints

**Postman Collection:** Can be generated from OpenAPI spec

**cURL Examples:**

```bash
# Update assignment
curl -X POST http://localhost:5044/api/files/UpdateAssignmentHistory \
  -H "Content-Type: application/json" \
  -d '{
	"fileNumber": "TM2025/001234",
	"applicationId": "550e8400-e29b-41d4-a716-446655440000",
	"assignorName": "John Smith"
  }'

# Delete creator
curl -X PUT http://localhost:5044/api/files/update-filing \
  -H "Content-Type: application/json" \
  -d '{
	"fileId": "DESIGN2025/001234",
	"designCreators": [...],
	"updatedBy": "admin"
  }'

# Get details
curl http://localhost:5044/api/files/GetAllFileDetails?fileNumber=DESIGN2025/001234

# Generate PDF
curl http://localhost:5044/api/letters/generate?fileId=DESIGN2025/001234&letterType=6 \
  --output design_acceptance.pdf
```

---

## Debug Mode

Enable detailed logging:
```csharp
// In Program.cs
services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
```

Check logs for:
- `[Acknowledgement]` - Letter generation logs
- `Error updating assignment history entry` - Update failures
- `Successfully loaded attachment` - File loading status

---

## Support

For issues or questions, refer to:
1. `FRONTEND_HANDOFF.md` - Quick integration guide
2. `FEATURES_COMPLETE_SUMMARY.md` - Technical details
3. `DESIGN_CREATOR_DELETION_IMPLEMENTATION.md` - Creator deletion details
4. Code comments in FilesServices.cs and FilesController.cs
