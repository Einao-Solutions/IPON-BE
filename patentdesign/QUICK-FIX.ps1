# QUICK FIX for file F/DS/NT/O/2026/6687
# Copy images from 'designDrawings' to 'designs'

$fileId = "F/DS/NT/O/2026/6687"
$port = 5044

Write-Host "=== FIXING FILE: $fileId ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Copying images from 'designDrawings' to 'designs'..." -ForegroundColor Yellow
Write-Host ""

try {
    # Properly encode the file ID for URL
    $encodedFileId = [uri]::EscapeDataString($fileId)
    $url = "http://localhost:$port/api/files/copy-to-designs/$encodedFileId`?sourceAttachmentName=designDrawings"

    Write-Host "Calling: $url" -ForegroundColor Gray

    $result = Invoke-RestMethod -Uri $url -Method POST -ContentType "application/json" -ErrorAction Stop

    if ($result.success) {
        Write-Host "`n✅ SUCCESS!" -ForegroundColor Green
        Write-Host ""
        Write-Host $result.message -ForegroundColor White
        Write-Host ""

        if ($result.copiedUrls -and $result.copiedUrls.Count -gt 0) {
            Write-Host "Copied URLs:" -ForegroundColor Cyan
            foreach ($u in $result.copiedUrls) {
                Write-Host "  - $u" -ForegroundColor Gray
            }
            Write-Host ""
            Write-Host "🎉 IMAGES WILL NOW SHOW IN THE ACKNOWLEDGEMENT LETTER!" -ForegroundColor Green
        } else {
            Write-Host "⚠️  No URLs were copied (source might be empty)" -ForegroundColor Yellow
        }
        Write-Host ""
    } else {
        Write-Host "`n❌ Failed: $($result.message)" -ForegroundColor Red

        if ($result.availableAttachments -and $result.availableAttachments.Count -gt 0) {
            Write-Host ""
            Write-Host "Available attachments in file:" -ForegroundColor Yellow
            foreach ($att in $result.availableAttachments) {
                Write-Host "  - $att" -ForegroundColor White
            }
        }
    }

} catch {
    Write-Host "`n❌ ERROR: Cannot connect to app!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please:" -ForegroundColor Yellow
    Write-Host "  1. Press F5 in Visual Studio to start the app" -ForegroundColor White
    Write-Host "  2. Wait for it to fully start (check for 'Now listening on: http://localhost:5044')" -ForegroundColor White
    Write-Host "  3. Run this script again" -ForegroundColor White
    Write-Host ""
    Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
