# Diagnostic Script for File: F/DS/NT/O/2026/6687
# Run this after starting your application (Press F5 in Visual Studio or run 'dotnet run')

Write-Host "=== DESIGN IMAGE DIAGNOSTIC TOOL ===" -ForegroundColor Cyan
Write-Host ""

$fileId = "F/DS/NT/O/2026/6687"
$port = 5044  # Your app's port from launchSettings.json
$url = "http://localhost:$port/api/files/diagnose-design-images/$fileId"

Write-Host "Checking file: $fileId" -ForegroundColor Yellow
Write-Host "Connecting to: $url" -ForegroundColor Gray
Write-Host ""

# Check if app is running
Write-Host "Testing connection..." -ForegroundColor Gray
try {
    $testResponse = Invoke-WebRequest -Uri "http://localhost:$port/swagger/index.html" -Method HEAD -TimeoutSec 2 -ErrorAction SilentlyContinue
    if ($testResponse.StatusCode -eq 200) {
        Write-Host "✅ App is running!" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ App is NOT running!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please start the application first:" -ForegroundColor Yellow
    Write-Host "  Option 1: Press F5 in Visual Studio" -ForegroundColor White
    Write-Host "  Option 2: Run 'dotnet run' in the terminal" -ForegroundColor White
    Write-Host ""
    Write-Host "Then run this script again." -ForegroundColor Yellow
    exit
}

Write-Host ""
Write-Host "Fetching diagnostic data..." -ForegroundColor Yellow

try {
    $result = Invoke-RestMethod -Uri $url -Method GET -ErrorAction Stop
    
    Write-Host ""
    Write-Host "=== RESULTS ===" -ForegroundColor Cyan
    Write-Host ""
    
    # Display basic info
    Write-Host "File ID: $($result.fileId)" -ForegroundColor White
    Write-Host "File Type: $($result.fileType)" -ForegroundColor White
    Write-Host "Title: $($result.title)" -ForegroundColor White
    Write-Host ""
    
    # Check if file was found
    if ($result.success -eq $false) {
        Write-Host "❌ FILE NOT FOUND IN DATABASE!" -ForegroundColor Red
        Write-Host "This file does not exist in the database." -ForegroundColor Yellow
        exit
    }
    
    # Check attachments
    Write-Host "Total Attachments: $($result.totalAttachments)" -ForegroundColor White
    if ($result.allAttachmentNames) {
        Write-Host "Attachment Names: $($result.allAttachmentNames -join ', ')" -ForegroundColor Gray
    }
    Write-Host ""
    
    # Check design attachment
    if ($result.hasDesignAttachment -eq $false) {
        Write-Host "❌ NO 'DESIGNS' ATTACHMENT FOUND!" -ForegroundColor Red
        Write-Host "This means the user never uploaded design images during application." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "SOLUTION: User needs to upload design images or submit an amendment." -ForegroundColor Yellow
        exit
    }
    
    Write-Host "✅ 'designs' attachment exists" -ForegroundColor Green
    Write-Host "Number of image URLs: $($result.designUrlCount)" -ForegroundColor White
    Write-Host ""
    
    if ($result.designUrlCount -eq 0) {
        Write-Host "❌ NO IMAGE URLs FOUND!" -ForegroundColor Red
        Write-Host "The 'designs' attachment exists but has no URLs." -ForegroundColor Yellow
        Write-Host "SOLUTION: Upload process failed. Need to re-upload images." -ForegroundColor Yellow
        exit
    }
    
    # Check each URL
    Write-Host "=== IMAGE URL ANALYSIS ===" -ForegroundColor Cyan
    Write-Host ""
    
    $accessibleCount = 0
    $brokenCount = 0
    $nullCount = 0
    
    for ($i = 0; $i -lt $result.urlChecks.Count; $i++) {
        $urlCheck = $result.urlChecks[$i]
        $num = $i + 1
        
        Write-Host "Image $num" -ForegroundColor White
        Write-Host "  URL: $($urlCheck.url)" -ForegroundColor Gray
        
        if ($urlCheck.isNullString) {
            Write-Host "  Status: ❌ NULL STRING" -ForegroundColor Red
            Write-Host "  Issue: URL is the text 'NULL' instead of a real URL" -ForegroundColor Yellow
            $nullCount++
        }
        elseif ($urlCheck.isNull) {
            Write-Host "  Status: ❌ EMPTY" -ForegroundColor Red
            Write-Host "  Issue: URL is empty or whitespace" -ForegroundColor Yellow
            $nullCount++
        }
        elseif ($urlCheck.accessible) {
            Write-Host "  Status: ✅ ACCESSIBLE" -ForegroundColor Green
            $accessibleCount++
        }
        else {
            Write-Host "  Status: ❌ NOT ACCESSIBLE" -ForegroundColor Red
            Write-Host "  Error: $($urlCheck.error)" -ForegroundColor Yellow
            $brokenCount++
        }
        Write-Host ""
    }
    
    # Summary
    Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Total Images: $($result.designUrlCount)" -ForegroundColor White
    Write-Host "✅ Accessible: $accessibleCount" -ForegroundColor Green
    Write-Host "❌ Broken/404: $brokenCount" -ForegroundColor Red
    Write-Host "❌ NULL/Empty: $nullCount" -ForegroundColor Red
    Write-Host ""
    
    # Final verdict
    if ($accessibleCount -gt 0) {
        Write-Host "✅ IMAGES WILL SHOW IN ACKNOWLEDGEMENT LETTER" -ForegroundColor Green
        Write-Host "At least $accessibleCount image(s) will be rendered." -ForegroundColor White
    } else {
        Write-Host "❌ NO IMAGES WILL SHOW IN ACKNOWLEDGEMENT LETTER" -ForegroundColor Red
        Write-Host ""
        if ($brokenCount -gt 0) {
            Write-Host "REASON: All image URLs are broken (404/403 errors)" -ForegroundColor Yellow
            Write-Host "SOLUTION: Files were deleted from storage. Need to re-upload or restore from backup." -ForegroundColor Yellow
        }
        elseif ($nullCount -gt 0) {
            Write-Host "REASON: All URLs are NULL or empty" -ForegroundColor Yellow
            Write-Host "SOLUTION: Upload process failed. Need to check upload service and re-upload images." -ForegroundColor Yellow
        }
    }
    Write-Host ""
    
    # Full JSON output
    Write-Host "=== FULL JSON RESPONSE ===" -ForegroundColor Cyan
    $result | ConvertTo-Json -Depth 10 | Write-Host -ForegroundColor Gray
    
} catch {
    Write-Host ""
    Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please check:" -ForegroundColor Yellow
    Write-Host "  1. Application is running" -ForegroundColor White
    Write-Host "  2. Port $port is correct" -ForegroundColor White
    Write-Host "  3. File ID is correct: $fileId" -ForegroundColor White
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
