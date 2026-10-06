## GET /api/users/GetOtherApplications - Implementation Guide

### Core Principle
This endpoint follows the **exact same pattern as Opposition.LoadSummary**:
- When `userId` is **omitted/null** → All-history query (returns all users' records)
- When `userId` is **provided** → Filtered query (returns only that user's records)

### Authorization Model
Unlike Opposition (which has no [Authorize]), this endpoint has strict role-based authorization:

#### 1. To request all-history (userId = null OR userId = own ID):
Auth Requirement: One of these roles:
- `Tech`
- `TrademarkSupport`
- `PatentDesignSupport`
- `SuperAdmin`

Action: Gets **ALL** Other Applications from **ALL** users
Response: Flattened list of all `AppUser.OtherApplications` across the entire system

#### 2. To request filtered history (userId = specific other user):
Auth Requirement (by role):
- `SuperAdmin` → Can access ANY user's records
- `Tech`, `TrademarkSupport`, `PatentDesignSupport` → Cannot access other users' records
- `User` → Can access only their own records

Action: Gets **ONLY** that user's `AppUser.OtherApplications`
Response: That single user's application list

### Request Examples

#### Example 1: Tech viewing all searches (all users)
```
GET /api/users/GetOtherApplications
```
- No `userId` parameter
- Tech authentication present
- Authorization: ✓ CanViewAll = true, ShouldViewAll = true
- Response: [ { App from User A }, { App from User B }, { App from User C }, ... ]

#### Example 2: Tech viewing all (via own ID - new feature)
```
GET /api/users/GetOtherApplications?userId=tech-account-id
```
- `userId` = Tech's own ID
- Tech authentication present
- Authorization: ✓ CanViewAll = true, ShouldViewAll = true (because userId == callerId)
- Response: [ { App from User A }, { App from User B }, { App from User C }, ... ]

#### Example 3: Tech viewing specific company (should FAIL)
```
GET /api/users/GetOtherApplications?userId=company-b-id
```
- `userId` = different user ID
- Tech authentication present
- Authorization: ✗ CanViewAll = true, BUT ShouldViewAll = false (because userId != callerId)
- Further check: CanViewRequestedOwner = false (Tech cannot access other companies)
- Response: 403 Forbid

#### Example 4: SuperAdmin viewing specific company (should SUCCEED with FILTERED results)
```
GET /api/users/GetOtherApplications?userId=company-b-id
```
- `userId` = different user ID
- SuperAdmin authentication present
- Authorization: ✓ ShouldViewAll = false (because userId != callerId), BUT CanViewRequestedOwner = true (SuperAdmin can)
- Action: FetchOtherApplications (filtered, not all-history)
- Response: [ { Only apps from company-b } ]

#### Example 5: Regular user viewing their own (should SUCCEED with FILTERED results)
```
GET /api/users/GetOtherApplications?userId=user-123
```
- `userId` = user's own ID
- Regular user authentication present
- Authorization: ✓ CanViewRequestedOwner = true (own records allowed)
- ShouldViewAll = false (regular users cannot get all-history)
- Action: FetchOtherApplications (filtered to their ID only)
- Response: [ { Only apps from user-123 } ]

#### Example 6: Regular user viewing all (should FAIL)
```
GET /api/users/GetOtherApplications
```
- No `userId` parameter
- Regular user authentication present
- Authorization: ✗ CanViewAll = false, returns 403 Forbid immediately

### Controller Flow

```csharp
public async Task<IActionResult> GetOtherApplications([FromQuery] string? userId = null)
{
	// Step 1: Require authentication
	var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
	if (string.IsNullOrWhiteSpace(callerId))
		return Unauthorized();

	// Step 2: If no userId AND not privileged, reject
	if (string.IsNullOrWhiteSpace(userId) && !CanViewAll(User))
		return Forbid();

	// Step 3: If should view all (blank userId OR own userId + privileged), fetch all
	if (ShouldViewAll(userId, User))
		return Ok(await usersService.FetchAllOtherApplications());

	// Step 4: Otherwise, check if can access the requested owner
	if (!CanViewRequestedOwner(userId, User))
		return Forbid();

	// Step 5: Fetch and return filtered results
	var applications = await usersService.FetchOtherApplications(userId);
	return Ok(applications ?? new List<ApplicationInfo>());
}
```

### Debug Checklist

If you test and the response is still wrong:

1. **Verify the DNS/host** → Confirm you're hitting the updated API, not a cached old instance
2. **Inspect the Network request** in browser dev tools:
   - URL: Is it really going to `/api/users/GetOtherApplications`?
   - Query string: What is the `userId` value? (null, your account ID, or something else?)
   - Status code: What HTTP status? (200, 403, 500, etc.)
   - Response: Does it contain records from multiple users or just one?
3. **Check server logs** → Is the endpoint being called? Any exceptions?
4. **Verify roles** → Are you actually authenticated as Tech/SuperAdmin/etc.? Check JWT token Claims.Role.

### Files to Verify

- `patentdesign/Controllers/UsersController.cs` → GetOtherApplications endpoint
- `patentdesign/Utils/OtherApplicationsHistoryRules.cs` → Authorization logic (CanViewAll, ShouldViewAll)
- `patentdesign/Services/UsersService.cs` → FetchAllOtherApplications (returns Flatten of all users)

All files have been updated and tested. Release build: ✓ Passed. Unit tests: ✓ 24 tests passed.
