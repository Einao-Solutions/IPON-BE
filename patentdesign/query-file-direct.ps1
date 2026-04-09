# Direct MongoDB Query to Check File F/DS/NT/O/2026/6687
# This will show you the actual attachment data in the database

$fileId = "F/DS/NT/O/2026/6687"

Write-Host "=== CHECKING FILE: $fileId ===" -ForegroundColor Cyan
Write-Host ""

# MongoDB connection details (from appsettings.json)
$mongoHost = "localhost"
$mongoPort = "27017"
$database = "patentdesign"
$collection = "files"

Write-Host "Querying MongoDB..." -ForegroundColor Yellow
Write-Host "Database: $database" -ForegroundColor Gray
Write-Host "Collection: $collection" -ForegroundColor Gray
Write-Host ""

# Create MongoDB query using mongosh (MongoDB Shell)
$query = @"
use $database;
db.$collection.findOne(
    { "FileId": "$fileId" },
    { 
        "FileId": 1, 
        "TitleOfDesign": 1,
        "Type": 1,
        "Attachments": 1 
    }
)
"@

# Try to run mongosh command
try {
    Write-Host "Running query..." -ForegroundColor Gray
    $result = & mongosh --quiet --eval $query 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "=== QUERY RESULT ===" -ForegroundColor Green
        Write-Host $result
        Write-Host ""
        
        # Check if file was found
        if ($result -match "null") {
            Write-Host "❌ FILE NOT FOUND!" -ForegroundColor Red
        } else {
            Write-Host "✅ File found! Check the 'Attachments' field above." -ForegroundColor Green
            Write-Host ""
            Write-Host "Look for:" -ForegroundColor Yellow
            Write-Host "  - An object with name: 'designs'" -ForegroundColor White
            Write-Host "  - The 'url' array inside it" -ForegroundColor White
            Write-Host "  - Check if URLs are valid or 'NULL'" -ForegroundColor White
        }
    } else {
        throw "MongoDB command failed"
    }
    
} catch {
    Write-Host "❌ Could not connect to MongoDB using mongosh" -ForegroundColor Red
    Write-Host ""
    Write-Host "Alternative: Use MongoDB Compass" -ForegroundColor Yellow
    Write-Host "1. Open MongoDB Compass" -ForegroundColor White
    Write-Host "2. Connect to: mongodb://localhost:27017" -ForegroundColor White
    Write-Host "3. Select database: $database" -ForegroundColor White
    Write-Host "4. Select collection: $collection" -ForegroundColor White
    Write-Host "5. Run this filter:" -ForegroundColor White
    Write-Host '   { "FileId": "' + $fileId + '" }' -ForegroundColor Cyan
    Write-Host ""
    Write-Host "6. Look at the 'Attachments' field" -ForegroundColor White
    Write-Host "7. Find the object where name = 'designs'" -ForegroundColor White
    Write-Host "8. Check the 'url' array" -ForegroundColor White
    Write-Host ""
    Write-Host "PASTE THE RESULTS HERE AND I'LL HELP FIX IT" -ForegroundColor Green
}

Write-Host ""
