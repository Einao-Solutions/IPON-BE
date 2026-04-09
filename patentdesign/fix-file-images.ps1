# Script to check and fix design images for file F/DS/NT/O/2026/6687
# This will check if images exist under a different attachment name and copy them to "designs"

$fileId = "F/DS/NT/O/2026/6687"
$encodedFileId = [System.Web.HttpUtility]::UrlEncode($fileId)
$port = 5044

Write-Host "=== FIX DESIGN IMAGES FOR FILE: $fileId ===" -ForegroundColor Cyan
Write-Host ""

# Check if app is running
Write-Host "Step 1: Checking if app is running..." -ForegroundColor Yellow
try {
    $testResponse = Invoke-WebRequest -Uri "http://localhost:$port/swagger/index.html" -Method HEAD -TimeoutSec 2 -ErrorAction SilentlyContinue
    if ($testResponse.StatusCode -eq 200) {
        Write-Host "✅ App is running!" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ App is NOT running!" -ForegroundColor Red
    Write-Host "Please start the app (Press F5 in Visual Studio) and run this script again." -ForegroundColor Yellow
    exit
}

Write-Host ""
Write-Host "Step 2: Checking current attachments..." -ForegroundColor Yellow

try {
    $url = "http://localhost:$port/api/files/design-attachments/$encodedFileId"
    $result = Invoke-RestMethod -Uri $url -Method GET -ErrorAction Stop
    
    Write-Host "✅ File found!" -ForegroundColor Green
    Write-Host "   Title: $($result.title)" -ForegroundColor White
    Write-Host ""
    
    Write-Host "Current Attachments:" -ForegroundColor Cyan
    foreach ($att in $result.allAttachments) {
        $status = if ($att.hasUrls) { "✅" } else { "❌" }
        Write-Host "  $status $($att.name) - $($att.urlCount) URL(s)" -ForegroundColor White
    }
    Write-Host ""
    
    # Check designs attachment
    if ($result.designUrlCount -gt 0) {
        Write-Host "✅ 'designs' attachment already has $($result.designUrlCount) image(s)" -ForegroundColor Green
        Write-Host "Images should already be showing in acknowledgement letter!" -ForegroundColor Green
        Write-Host ""
        Write-Host "URLs:" -ForegroundColor Gray
        foreach ($url in $result.designUrls) {
            Write-Host "  - $url" -ForegroundColor Gray
        }
        exit
    }
    
    Write-Host "❌ 'designs' attachment is empty or missing" -ForegroundColor Red
    Write-Host ""
    
    # Check for alternative attachments with images
    if ($result.alternativeImageAttachments -and $result.alternativeImageAttachments.Count -gt 0) {
        Write-Host "✅ Found alternative attachments with images:" -ForegroundColor Green
        foreach ($alt in $result.alternativeImageAttachments) {
            Write-Host "  - '$($alt.name)' has $($alt.urlCount) image(s)" -ForegroundColor White
        }
        Write-Host ""
        
        # Use the first alternative with images
        $sourceAttachment = $result.alternativeImageAttachments[0].name
        Write-Host "Step 3: Copying images from '$sourceAttachment' to 'designs'..." -ForegroundColor Yellow
        
        $copyUrl = "http://localhost:$port/api/files/copy-to-designs/$encodedFileId`?sourceAttachmentName=$sourceAttachment"
        $copyResult = Invoke-RestMethod -Uri $copyUrl -Method POST -ErrorAction Stop
        
        if ($copyResult.success) {
            Write-Host "✅ SUCCESS!" -ForegroundColor Green
            Write-Host "   $($copyResult.message)" -ForegroundColor White
            Write-Host ""
            Write-Host "Copied URLs:" -ForegroundColor Cyan
            foreach ($url in $copyResult.copiedUrls) {
                Write-Host "  - $url" -ForegroundColor Gray
            }
            Write-Host ""
            Write-Host "🎉 IMAGES WILL NOW SHOW IN ACKNOWLEDGEMENT LETTER!" -ForegroundColor Green
        } else {
            Write-Host "❌ Failed: $($copyResult.message)" -ForegroundColor Red
        }
        
    } else {
        Write-Host "❌ No alternative attachments with images found" -ForegroundColor Red
        Write-Host ""
        Write-Host "Available attachments:" -ForegroundColor Yellow
        foreach ($att in $result.allAttachments) {
            Write-Host "  - $($att.name) ($($att.urlCount) URLs)" -ForegroundColor White
        }
        Write-Host ""
        Write-Host "SOLUTION: User needs to upload design images through the application." -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Make sure:" -ForegroundColor Yellow
    Write-Host "  1. Application is running (Press F5)" -ForegroundColor White
    Write-Host "  2. File ID is correct: $fileId" -ForegroundColor White
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
