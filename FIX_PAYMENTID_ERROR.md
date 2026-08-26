╔════════════════════════════════════════════════════════════════════════════╗
║                                                                            ║
║          ❌ PaymentId Not Sent in FormData - FIX REQUIRED                 ║
║                                                                            ║
║                    HTTP 400 Validation Error Resolved                      ║
║                                                                            ║
╚════════════════════════════════════════════════════════════════════════════╝

PROBLEM:
  Error: {"PaymentId":["The PaymentId field is required."]}
  Status: HTTP 400 Bad Request
  Root Cause: Frontend is NOT sending PaymentId in FormData

═══════════════════════════════════════════════════════════════════════════════

DEBUG LOG SHOWS:
  ✅ FileId: NG/TM/O/2024/322714
  ✅ FileType: 2
  ✅ WithdrawalLetter: File(CV_1704306078981.pdf)
  ✅ SupportingDocuments: File(CV_1704306078981.pdf)
  ❌ PaymentId: MISSING!

═══════════════════════════════════════════════════════════════════════════════

FRONTEND FIX REQUIRED:

Your frontend code is not appending PaymentId to FormData.

CURRENT CODE (WRONG):
  const formData = new FormData();
  formData.append('FileId', fileId);
  formData.append('FileType', fileType);
  formData.append('WithdrawalLetter', withdrawalLetterFile);
  // ❌ PaymentId not added!
  supportingDocuments.forEach(doc => {
	formData.append('SupportingDocuments', doc);
  });

CORRECTED CODE:
  const formData = new FormData();
  formData.append('FileId', fileId);
  formData.append('FileType', fileType);
  formData.append('PaymentId', paymentId);  // ✅ ADD THIS LINE
  formData.append('WithdrawalLetter', withdrawalLetterFile);
  supportingDocuments.forEach(doc => {
	formData.append('SupportingDocuments', doc);
  });

═══════════════════════════════════════════════════════════════════════════════

WHERE IS PaymentId?

From your app object:
  {
	applicationDate: '',
	applicationType: 15,
	currentStatus: 0,
	userId: '',
	paymentId: 'IPONMW638628027993694711',  // ← It's here!
	...
  }

The paymentId IS in your app object, but it's NOT being added to FormData!

═══════════════════════════════════════════════════════════════════════════════

SVELTE/SVELTEKIT FIX:

In your +page.svelte file, find the code that creates FormData:

CHANGE FROM:
  const formData = new FormData();
  formData.append('FileId', filingObject.fileId);
  formData.append('FileType', filingObject.type);
  formData.append('WithdrawalLetter', withdrawalLetterFile);

  if (supportingDocuments.length > 0) {
	supportingDocuments.forEach(doc => {
	  formData.append('SupportingDocuments', doc);
	});
  }

CHANGE TO:
  const formData = new FormData();
  formData.append('FileId', filingObject.fileId);
  formData.append('FileType', filingObject.type);
  formData.append('PaymentId', app.paymentId);  // ✅ ADD THIS
  formData.append('WithdrawalLetter', withdrawalLetterFile);

  if (supportingDocuments.length > 0) {
	supportingDocuments.forEach(doc => {
	  formData.append('SupportingDocuments', doc);
	});
  }

═══════════════════════════════════════════════════════════════════════════════

BACKEND CHANGES:

✅ Updated WithdrawalRequestCreateDto
   - Made all properties nullable (string?)
   - This allows FormData binding, but endpoint still validates

✅ Endpoint validates:
   - FileId is required
   - WithdrawalLetter is required
   - PaymentId is required
   - File must exist in database

═══════════════════════════════════════════════════════════════════════════════

COMPLETE FRONTEND EXAMPLE:

async function submitWithdrawalRequest(app, filingObject, withdrawalLetterFile, supportingDocuments) {
  try {
	// Create FormData
	const formData = new FormData();

	// Add required fields
	formData.append('FileId', filingObject.fileId);
	formData.append('FileType', filingObject.type);
	formData.append('PaymentId', app.paymentId);  // ✅ REQUIRED
	formData.append('WithdrawalLetter', withdrawalLetterFile);

	// Add optional supporting documents
	if (supportingDocuments && supportingDocuments.length > 0) {
	  supportingDocuments.forEach(doc => {
		formData.append('SupportingDocuments', doc);
	  });
	}

	console.log('✅ FormData entries:');
	for (let [key, value] of formData.entries()) {
	  console.log(`  ${key}: ${value instanceof File ? `File(${value.name})` : value}`);
	}

	// Send request
	const response = await fetch('/api/files/WithdrawalRequest', {
	  method: 'POST',
	  body: formData
	  // ✅ DO NOT set Content-Type header
	  // Browser will set it automatically with boundary
	});

	if (!response.ok) {
	  const error = await response.json();
	  console.error('❌ Error:', error);
	  return false;
	}

	const result = await response.json();
	console.log('✅ Success:', result);
	return true;
  } catch (error) {
	console.error('❌ Request failed:', error);
	return false;
  }
}

═══════════════════════════════════════════════════════════════════════════════

WHAT TO DO NOW:

1. ✅ Backend changes are already done (made fields nullable)
2. ⚠️  Frontend needs to be updated to send PaymentId
3. 🔄 In your +page.svelte, find where FormData is created
4. ✏️  Add: formData.append('PaymentId', app.paymentId);
5. 🧪 Test the endpoint again

═══════════════════════════════════════════════════════════════════════════════

DEBUGGING TIPS:

Check your console.log output for FormData entries:

BEFORE FIX (WRONG):
  ✅ FileId: NG/TM/O/2024/322714
  ✅ FileType: 2
  ✅ WithdrawalLetter: File(CV_1704306078981.pdf)
  ✅ SupportingDocuments: File(CV_1704306078981.pdf)
  ❌ PaymentId: MISSING

AFTER FIX (CORRECT):
  ✅ FileId: NG/TM/O/2024/322714
  ✅ FileType: 2
  ✅ PaymentId: IPONMW638628027993694711
  ✅ WithdrawalLetter: File(CV_1704306078981.pdf)
  ✅ SupportingDocuments: File(CV_1704306078981.pdf)

═══════════════════════════════════════════════════════════════════════════════

CURL TEST (to verify backend accepts it):

curl -X POST http://localhost:5044/api/files/WithdrawalRequest \
  -F "FileId=NG/TM/O/2024/322714" \
  -F "FileType=2" \
  -F "PaymentId=IPONMW638628027993694711" \
  -F "WithdrawalLetter=@path/to/letter.pdf" \
  -F "SupportingDocuments=@path/to/doc.pdf"

Expected: HTTP 200 with success response

═══════════════════════════════════════════════════════════════════════════════

EXPECTED SUCCESS RESPONSE (HTTP 200):

{
  "success": true,
  "message": "Withdrawal request submitted successfully.",
  "fileId": "NG/TM/O/2024/322714",
  "fileType": "Trademark",
  "withdrawalRequestDate": "2024-XX-XXTXX:XX:XXZ",
  "paymentId": "IPONMW638628027993694711",
  "withdrawalLetterAttachments": [
	{
	  "name": "CV_1704306078981.pdf",
	  "url": "/api/files/GetAttachment?fileId=..."
	}
  ],
  "supportingDocumentAttachments": [
	{
	  "name": "CV_1704306078981.pdf",
	  "url": "/api/files/GetAttachment?fileId=..."
	}
  ]
}

═══════════════════════════════════════════════════════════════════════════════

❌ PROBLEM: Frontend not sending PaymentId
✅ SOLUTION: Add formData.append('PaymentId', app.paymentId);
✅ BACKEND: Ready and waiting for PaymentId

═══════════════════════════════════════════════════════════════════════════════
