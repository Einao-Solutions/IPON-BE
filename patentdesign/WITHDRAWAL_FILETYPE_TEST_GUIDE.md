# Withdrawal File-Type Fix - Quick Test Guide

## Test the Fixed Endpoint

The `GetFileWithdrawalCost` endpoint now accepts multiple fileType formats.

### API Endpoint
```
GET /api/files/GetFileWithdrawalCost?fileId={fileId}&fileType={fileType}
```

### Test Cases

Run these commands to verify the fix works:

#### 1. **Numeric Format - Patent (0)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=P/2024/001&fileType=0"
```
Expected: ✅ Success (if file exists)

#### 2. **Numeric Format - Design (1)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=D/2024/001&fileType=1"
```
Expected: ✅ Success (if file exists)

#### 3. **Numeric Format - TradeMark (2)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=2"
```
Expected: ✅ Success (if file exists)

#### 4. **Text - Patent (lowercase)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=P/2024/001&fileType=patent"
```
Expected: ✅ Success (if file exists)

#### 5. **Text - Patent (uppercase)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=P/2024/001&fileType=Patent"
```
Expected: ✅ Success (if file exists)

#### 6. **Text - Design (lowercase)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=D/2024/001&fileType=design"
```
Expected: ✅ Success (if file exists)

#### 7. **Text - TradeMark (lowercase)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trademark"
```
Expected: ✅ Success (if file exists)

#### 8. **Text - TradeMark (with space)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trade%20mark"
```
Expected: ✅ Success (if file exists)

#### 9. **Text - TradeMark (with hyphen)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trade-mark"
```
Expected: ✅ Success (if file exists)

#### 10. **Text - TradeMark (shorthand)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=tm"
```
Expected: ✅ Success (if file exists)

#### 11. **Text - TradeMark (uppercase shorthand)**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=TM"
```
Expected: ✅ Success (if file exists)

#### 12. **Invalid Format**
```bash
curl -X GET "http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=invalid"
```
Expected: ❌ 400 Bad Request with message:
```json
{
  "message": "Invalid fileType: 'invalid'. Accepted values: Patent/patent/0, Design/design/1, TradeMark/Trademark/trademark/trade mark/trade-mark/tm/2"
}
```

#### 13. **Empty fileType**
```bash
curl -X GET "http://localhost:5024/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType="
```
Expected: ❌ 400 Bad Request with message about invalid fileType

---

## Success Criteria

All test cases 1-11 should return:
- ✅ Either `200 OK` with withdrawal cost data (if file exists)
- ✅ Or `204 No Content` (if file doesn't exist)

Both are successful normalizations - the difference is file existence.

Test cases 12-13 should return:
- ❌ `400 Bad Request` with clear error message

---

## Using Postman

1. Create a new GET request to: `http://localhost:5000/api/files/GetFileWithdrawalCost`
2. Add Query Parameters:
   - Key: `fileId`, Value: `TM/2024/001` (use actual FileId)
   - Key: `fileType`, Value: `trademark` (or any variant)
3. Click Send
4. Response should show withdrawal cost data

---

## Using Browser

Simply copy-paste in address bar:
```
http://localhost:5000/api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trademark
```

---

## Expected Response (Success)

```json
{
  "amount": "7000",
  "rrr": "12345678901234567890",
  "fileId": "TM/2024/001",
  "fileTitle": "My Brand",
  "applicantName": "John Doe",
  "trademarkClass": "35"
}
```

---

## Expected Response (Invalid Input)

```json
{
  "message": "Invalid fileType: 'invalid'. Accepted values: Patent/patent/0, Design/design/1, TradeMark/Trademark/trademark/trade mark/trade-mark/tm/2"
}
```

---

## Common Mistakes

❌ **Wrong**: `fileType=Trade Mark` (space, uppercase)
✅ **Correct**: `fileType=trade%20mark` (URL encoded space, lowercase)

❌ **Wrong**: `fileType=Trademark` alone without variant forms
✅ **Correct**: `fileType=trademark` or `fileType=Trademark` both work

❌ **Wrong**: Missing fileType parameter
```
GET /api/files/GetFileWithdrawalCost?fileId=TM/2024/001
```
✅ **Correct**: Include fileType parameter
```
GET /api/files/GetFileWithdrawalCost?fileId=TM/2024/001&fileType=trademark
```

---

## Verification Checklist

- [ ] Test case 1 passes (numeric 0)
- [ ] Test case 2 passes (numeric 1)
- [ ] Test case 3 passes (numeric 2)
- [ ] Test case 4 passes (text lowercase)
- [ ] Test case 5 passes (text uppercase)
- [ ] Test case 6 passes (design variant)
- [ ] Test case 7 passes (trademark variant)
- [ ] Test case 8 passes (space variant)
- [ ] Test case 9 passes (hyphen variant)
- [ ] Test case 10 passes (tm shorthand)
- [ ] Test case 11 passes (TM uppercase shorthand)
- [ ] Test case 12 fails with 400 (invalid input)
- [ ] Test case 13 fails with 400 (empty input)

If all pass ✅ - **Fix is working correctly!**

---

## Notes

- All successful tests should return the **same data structure** regardless of which fileType variant was used
- File must exist in database for successful response (otherwise 204 No Content)
- Invalid fileType should **always** return 400 Bad Request with helpful message
- No changes to other endpoints - only GetFileWithdrawalCost is affected
