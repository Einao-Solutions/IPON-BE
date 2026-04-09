# Design Images Diagnosis Summary for File: F/DS/NT/O/2026/6687

## Problem
Design representation images are not showing in acknowledgement letters for some file numbers, even though the database shows they uploaded images during application.

## Root Cause
The acknowledgement letter code **does** attempt to render design images, but they may not appear if:

1. **The attachment named "designs" doesn't exist** in the file's `Attachments` array
2. **The URL list is empty** `[]`
3. **The URLs contain "NULL" strings** or empty values  
4. **The URLs point to deleted/moved files** (404/403 errors)
5. **Network/storage issues** prevent image loading

## Changes Made

### 1. Enhanced Logging in LettersServices.cs
Added detailed console logging to track image loading:
- Logs when design URLs are empty or "NULL"
- Logs successful image loads with URL
- Logs errors with full exception messages
- Helps identify which files have broken images

### 2. Diagnostic Endpoint Created
**Endpoint:** `GET /api/files/diagnose-design-images/{fileId}`

**Example:**
```http
GET /api/files/diagnose-design-images/F/DS/NT/O/2026/6687
```

**Response:**
```json
{
  "success": true,
  "fileId": "F/DS/NT/O/2026/6687",
  "fileType": "Design",
  "title": "Sample Design Title",
  "hasAttachments": true,
  "totalAttachments": 5,
  "hasDesignAttachment": true,
  "designUrlCount": 3,
  "urlChecks": [
    {
      "url": "https://storage.example.com/design1.jpg",
      "isNull": false,
      "isNullString": false,
      "accessible": true,
      "error": null
    },
    {
      "url": "https://storage.example.com/design2.jpg",
      "isNull": false,
      "isNullString": false,
      "accessible": false,
      "error": "HTTP 404: Not Found"
    },
    {
      "url": "NULL",
      "isNull": false,
      "isNullString": true,
      "accessible": false,
      "error": null
    }
  ],
  "allAttachmentNames": ["designs", "form2", "pdoc", "nov"]
}
```

## How to Diagnose File F/DS/NT/O/2026/6687

### Option 1: Use the API Endpoint (Recommended)
1. Start your application
2. Call the endpoint:
   ```
   GET http://localhost:5000/api/files/diagnose-design-images/F/DS/NT/O/2026/6687
   ```
3. Review the response to see:
   - If the file exists
   - If it has a "designs" attachment
   - How many URLs are in the designs attachment
   - Which URLs are accessible and which are broken

### Option 2: Check Database Directly
Use the PowerShell script `diagnose-design.ps1` or MongoDB Compass:

**MongoDB Query:**
```javascript
{
  "FileId": "F/DS/NT/O/2026/6687"
}
```

**Projection:**
```javascript
{
  "FileId": 1,
  "Type": 1,
  "TitleOfDesign": 1,
  "Attachments": 1
}
```

Look for:
- `Attachments` array
- Find object with `name: "designs"`
- Check the `url` array
- Verify URLs are valid and accessible

### Option 3: Check Console Logs
When generating the acknowledgement letter, watch the console output for messages like:
- `[F/DS/NT/O/2026/6687] Successfully loaded design image from: https://...`
- `[F/DS/NT/O/2026/6687] ERROR: Failed to load design image from URL: https://... Error: 404 Not Found`
- `[F/DS/NT/O/2026/6687] No 'designs' attachment found or URL list is null`

## Expected Behavior

### Design Images WILL Show When:
✅ `Attachments` contains an object with `name: "designs"`  
✅ The `url` array is not empty  
✅ URLs are valid and accessible (not "NULL", not empty)  
✅ URLs return HTTP 200 responses  
✅ Images are not too large (handled by `.FitArea()`)

### Design Images WON'T Show When:
❌ No "designs" attachment exists  
❌ `url` array is empty `[]`  
❌ All URLs are "NULL" or empty strings  
❌ All URLs return 404/403/500 errors  
❌ Network timeout prevents loading

## Next Steps

1. **Run the diagnostic endpoint** for file `F/DS/NT/O/2026/6687`
2. **Review the results** to identify the specific issue
3. **Based on findings:**

   - **If no "designs" attachment exists:**
     - User never uploaded design images during application
     - Need to allow re-upload or amendment

   - **If URLs are "NULL" or empty:**
     - Upload process failed to save correct URLs
     - Check file upload service/code
     - May need database repair

   - **If URLs return 404:**
     - Files were deleted from storage
     - Need to restore from backup or re-upload
     - Check storage retention policies

   - **If URLs are inaccessible (403/network errors):**
     - Check storage permissions
     - Verify network connectivity to storage
     - Check firewall/security group rules

## Files Created

1. **`diagnose-design.ps1`** - PowerShell script with MongoDB query instructions
2. **`DiagnoseDesignFile.cs`** - C# standalone diagnostic tool
3. **`Services/FilesServices.cs`** - Added `DiagnoseDesignImagesAsync()` method
4. **`Controllers/FilesController.cs`** - Added `GET /api/files/diagnose-design-images/{fileId}` endpoint
5. **`Services/LettersServices.cs`** - Enhanced logging in `NewApplicationAcknowledgement()` method

## Quick Test Command

After starting your application, run:

```powershell
# PowerShell
Invoke-RestMethod -Uri "http://localhost:5000/api/files/diagnose-design-images/F/DS/NT/O/2026/6687" -Method GET | ConvertTo-Json -Depth 5
```

or

```bash
# curl
curl http://localhost:5000/api/files/diagnose-design-images/F/DS/NT/O/2026/6687 | jq
```

This will show you exactly why images aren't displaying for this specific file.
