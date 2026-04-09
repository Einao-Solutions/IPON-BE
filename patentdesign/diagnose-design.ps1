# PowerShell script to diagnose design file attachments
# Run this to check file F/DS/NT/O/2026/6687

Write-Host "=== Design File Diagnostic Tool ===" -ForegroundColor Cyan
Write-Host ""

$fileId = "F/DS/NT/O/2026/6687"

Write-Host "Checking file: $fileId" -ForegroundColor Yellow
Write-Host ""

# You can use MongoDB Compass or mongosh to run this query:
$mongoQuery = @"
{
  "FileId": "$fileId"
}
"@

Write-Host "MongoDB Query to run:" -ForegroundColor Green
Write-Host $mongoQuery
Write-Host ""

Write-Host "Projection to use:" -ForegroundColor Green
$projection = @"
{
  "FileId": 1,
  "Type": 1,
  "TitleOfDesign": 1,
  "Attachments": 1
}
"@
Write-Host $projection
Write-Host ""

Write-Host "=== Instructions ===" -ForegroundColor Cyan
Write-Host "1. Open MongoDB Compass or mongosh"
Write-Host "2. Connect to your database"
Write-Host "3. Go to the 'files' collection (or your filling collection)"
Write-Host "4. Run the query above with the projection"
Write-Host "5. Look for the 'Attachments' field"
Write-Host "6. Find the attachment with name = 'designs'"
Write-Host "7. Check the 'url' array - each URL should be valid and accessible"
Write-Host ""

Write-Host "=== What to look for ===" -ForegroundColor Yellow
Write-Host "• Attachments should contain an object with name: 'designs'"
Write-Host "• The 'designs' object should have a 'url' array"
Write-Host "• Each URL in the array should:"
Write-Host "  - NOT be empty or whitespace"
Write-Host "  - NOT be the string 'NULL'"
Write-Host "  - Point to a valid, accessible image file"
Write-Host ""

Write-Host "=== Common Issues ===" -ForegroundColor Red
Write-Host "❌ 'designs' attachment missing -> File was submitted without images"
Write-Host "❌ url array is empty [] -> Images were not uploaded"
Write-Host "❌ url contains 'NULL' or empty strings -> Upload failed"
Write-Host "❌ url points to deleted/moved files -> Storage cleanup removed files"
Write-Host "❌ url returns 404/403 errors -> Permission or file not found issues"
Write-Host ""
