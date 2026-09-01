# Withdrawal File-Type Contract Fix

## Summary

Fixed the withdrawal API to accept and normalize inconsistent fileType values from the frontend. The API now accepts both numeric (0, 1, 2) and text variants (Patent/patent, Design/design, TradeMark/Trademark/trademark/trade mark/trade-mark/tm) and normalizes them to consistent enum values before processing.

---

## Problem

The withdrawal endpoints were receiving inconsistent fileType values from the frontend, causing validation failures:

### Variant Examples
- **Patent**: "Patent", "patent", "0"
- **Design**: "Design", "design", "1"  
- **TradeMark**: "TradeMark", "Trademark", "trademark", "trade mark", "trade-mark", "tm", "2"

### Impact
- HTTP 400 Bad Request errors
- Validation failures in GetFileWithdrawalCost endpoint
- Inconsistent handling of text vs. numeric inputs

---

## Solution

Created a **FileTypeNormalizer** utility class that:

1. **Accepts multiple formats** - Both numeric (0, 1, 2) and text variants
2. **Normalizes consistently** - All variants map to the same FileTypes enum value
3. **Handles spacing/casing** - Removes hyphens, spaces; case-insensitive matching
4. **Provides two methods**:
   - `TryNormalizeFileType()` - Returns bool, doesn't throw
   - `NormalizeFileType()` - Throws on invalid input

---

## Files Changed

### 1. New File: `patentdesign/Utils/FileTypeNormalizer.cs`

```csharp
namespace patentdesign.Utils;

public static class FileTypeNormalizer
{
	/// <summary>
	/// Normalizes fileType from various formats to FileTypes enum
	/// Accepts: Patent/patent/0, Design/design/1, TradeMark/trademark/tm/2
	/// </summary>
	public static bool TryNormalizeFileType(string? fileTypeInput, out FileTypes normalizedType)
```

**Key Methods:**
- `TryNormalizeFileType()` - Safe normalization, returns bool
- `NormalizeFileType()` - Throws on invalid, simpler usage
- `IsValidFileTypeNumeric()` - Validates numeric values (0, 1, 2)

**Supported Values:**
```
Patent/patent → FileTypes.Patent (0)
Design/design → FileTypes.Design (1)
TradeMark/Trademark/trademark/trade mark/trade-mark/tm → FileTypes.TradeMark (2)
```

---

### 2. Updated: `patentdesign/Controllers/FilesController.cs`

**Added using:**
```csharp
using patentdesign.Utils;
```

**Changed Endpoint:**
```csharp
[HttpGet("GetFileWithdrawalCost")]
public async Task<IActionResult> GetFileWithdrawalCost(
	[FromQuery] string fileId, 
	[FromQuery] string fileType)  // Changed from FileTypes to string
{
	// Normalize fileType from various formats
	if (!FileTypeNormalizer.TryNormalizeFileType(fileType, out var normalizedFileType))
	{
		return BadRequest(new { message = "Invalid fileType: ..." });
	}

	var decodedFileId = Uri.UnescapeDataString(fileId);
	var res = await fileService.GetFileWithdrawalCost(decodedFileId, normalizedFileType);
	// ... rest of method
}
```

---

## API Behavior

### Before Fix
```
GET /api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trademark
↓
400 Bad Request - Cannot convert "trademark" to FileTypes enum
```

### After Fix
```
GET /api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trademark
↓
✅ Normalizes "trademark" → FileTypes.TradeMark
✅ Processes request successfully
↓
200 OK with withdrawal cost data

GET /api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=2
↓
✅ Normalizes "2" → FileTypes.TradeMark
✅ Same response as above

GET /api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trade mark
↓
✅ Normalizes "trade mark" → FileTypes.TradeMark
✅ Same response as above
```

---

## Supported Input Variants

### Patent (enum value = 0)
- Numeric: `"0"`
- Text: `"patent"`, `"Patent"`

### Design (enum value = 1)
- Numeric: `"1"`
- Text: `"design"`, `"Design"`

### TradeMark (enum value = 2)
- Numeric: `"2"`
- Text: `"trademark"`, `"Trademark"`, `"TradeMark"`
- Variants: `"trade mark"`, `"trade-mark"`, `"tm"`, `"TM"`

All variants are normalized to their respective enum values with case-insensitive matching.

---

## Implementation Details

### Normalization Logic

```csharp
// Step 1: Trim and lowercase
var input = fileTypeInput.Trim().ToLowerInvariant();

// Step 2: Try numeric values
if (input == "0") → FileTypes.Patent
if (input == "1") → FileTypes.Design
if (input == "2") → FileTypes.TradeMark

// Step 3: Normalize spacing/hyphens for text
input = input.Replace("-", "").Replace(" ", "");

// Step 4: Match normalized text
if (input == "patent") → FileTypes.Patent
if (input == "design") → FileTypes.Design
if (input == "trademark" || input == "tm") → FileTypes.TradeMark
```

---

## Affected Endpoints

### Primary (Fixed)
- `GET /api/files/GetFileWithdrawalCost` - Now accepts normalized fileType

### Secondary (Unaffected - No fileType parameter)
- `POST /api/files/withdrawal-request` - Doesn't use fileType
- `POST /api/files/WithdrawalRequest` - Doesn't use fileType  
- `GET /api/files/withdrawal-details/{fileId}` - FileId-based, no fileType

---

## Error Handling

**Invalid Input:**
```csharp
GET /api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=invalid
↓
400 Bad Request
{
  "message": "Invalid fileType: 'invalid'. Accepted values: Patent/patent/0, 
			 Design/design/1, TradeMark/Trademark/trademark/trade mark/trade-mark/tm/2"
}
```

**Valid but Insufficient Data:**
```csharp
GET /api/files/GetFileWithdrawalCost?fileId=INVALID&fileType=trademark
↓
204 No Content  // File not found, but fileType normalized successfully
```

---

## Testing

### Test Cases
```csharp
// Numeric inputs
TryNormalizeFileType("0", out var type) → true, type = FileTypes.Patent
TryNormalizeFileType("1", out var type) → true, type = FileTypes.Design
TryNormalizeFileType("2", out var type) → true, type = FileTypes.TradeMark

// Case variants
TryNormalizeFileType("Patent", out var type) → true, type = FileTypes.Patent
TryNormalizeFileType("DESIGN", out var type) → true, type = FileTypes.Design

// Spacing variants
TryNormalizeFileType("trade mark", out var type) → true, type = FileTypes.TradeMark
TryNormalizeFileType("trade-mark", out var type) → true, type = FileTypes.TradeMark

// Shorthand
TryNormalizeFileType("tm", out var type) → true, type = FileTypes.TradeMark
TryNormalizeFileType("TM", out var type) → true, type = FileTypes.TradeMark

// Invalid
TryNormalizeFileType("invalid", out var type) → false
TryNormalizeFileType("", out var type) → false
TryNormalizeFileType(null, out var type) → false
```

---

## Backward Compatibility

✅ **Fully backward compatible**

- Existing valid numeric inputs (0, 1, 2) continue to work
- Existing pascal-case text inputs (Patent, Design, TradeMark) continue to work
- New variants are additions, not replacements
- No changes to payment logic or other endpoints

---

## No Changes Made To

✅ **Out of scope** - Do NOT modify:
- Payment flow logic
- Other endpoints not related to withdrawal
- WithdrawalRequestAsync service method
- WithdrawalRequestDto class
- Any code outside of withdrawal file-type validation

---

## Implementation Checklist

- [x] Created FileTypeNormalizer.cs utility class
- [x] Added TryNormalizeFileType() method
- [x] Added NormalizeFileType() method  
- [x] Added IsValidFileTypeNumeric() method
- [x] Updated GetFileWithdrawalCost endpoint to use string fileType
- [x] Added normalization call in endpoint
- [x] Added error handling for invalid input
- [x] Added using statement to FilesController
- [x] Verified no compilation errors
- [x] Ensured backward compatibility
- [x] Documented all supported variants

---

## Usage Examples

### In Frontend - Calling the API

**Option 1: Numeric**
```javascript
fetch(`/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=2`)
```

**Option 2: Text (uppercase)**
```javascript
fetch(`/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=TradeMark`)
```

**Option 3: Text (lowercase)**
```javascript
fetch(`/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trademark`)
```

**Option 4: Variant with spaces**
```javascript
fetch(`/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trade%20mark`)
```

All result in the same normalized value and identical responses.

### In Code - Using the Normalizer

```csharp
// Safe usage (recommended for endpoints)
if (FileTypeNormalizer.TryNormalizeFileType(userInput, out var fileType))
{
	// Handle valid fileType
	await fileService.GetFileWithdrawalCost(fileId, fileType);
}
else
{
	// Return error to user
	return BadRequest("Invalid fileType");
}

// Direct usage (for internal code)
var fileType = FileTypeNormalizer.NormalizeFileType(userInput);
// Throws if invalid - let it bubble up
```

---

## Performance

- **No performance impact** - Simple string operations, no database queries added
- **Minimal overhead** - O(1) normalization per request
- **Efficient** - Single pass through normalization logic

---

## Security

✅ **No security risks introduced**

- Input validation improved (rejects invalid values)
- No SQL injection risk (enum values only)
- No privilege escalation vectors
- String normalization is deterministic

---

## Summary of Changes

| File | Change | Impact |
|------|--------|--------|
| FileTypeNormalizer.cs | Created | Normalization logic |
| FilesController.cs | Updated GetFileWithdrawalCost | Accepts normalized input |
| FilesController.cs | Added using statement | Enable FileTypeNormalizer |

**Lines Modified**: ~20 lines total  
**Endpoints Affected**: 1 (GetFileWithdrawalCost)  
**Backward Compatible**: Yes ✅  
**Tests Required**: Manual testing with variant inputs  
**Deployment Notes**: None - drop-in fix

