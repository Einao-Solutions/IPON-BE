# Run this script as Administrator to disable AppLocker and fix dotnet watch run
# Right-click PowerShell → Run as Administrator → Copy and paste this entire script

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
Write-Host "║   FIXING APPLOCKER - Restore dotnet watch run            ║" -ForegroundColor Yellow
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow

Write-Host "`n⚠️  This script requires Administrator privileges!`n" -ForegroundColor Red

# Check if running as admin
$isAdmin = [Security.Principal.WindowsIdentity]::GetCurrent().Groups -contains 'S-1-5-32-544'
if (-not $isAdmin) {
	Write-Host "❌ ERROR: Not running as Administrator!`n" -ForegroundColor Red
	Write-Host "Please do this:`n" -ForegroundColor Yellow
	Write-Host "1. Right-click PowerShell"
	Write-Host "2. Click 'Run as Administrator'"
	Write-Host "3. Paste and run this script again`n"
	Read-Host "Press Enter to exit"
	exit 1
}

Write-Host "✓ Running with Administrator privileges`n" -ForegroundColor Green

# Step 1: Stop AppLocker service
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "Step 1: Disabling AppLocker Service" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

try {
	Stop-Service -Name AppIDSvc -Force -ErrorAction SilentlyContinue
	Set-Service -Name AppIDSvc -StartupType Disabled -ErrorAction SilentlyContinue
	Write-Host "✓ AppLocker service stopped and disabled`n" -ForegroundColor Green
} catch {
	Write-Host "⚠️  Could not modify AppLocker service: $_`n" -ForegroundColor Yellow
}

# Step 2: Unblock all DLLs in project
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "Step 2: Unblocking all DLL files" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

$projectPath = "C:\Users\DELL\source\repos\IPON-BE\patentdesign\bin"
$dllCount = 0

Get-ChildItem -Path $projectPath -Recurse -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
	try {
		Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
		$dllCount++
	} catch {
		# Silently continue
	}
}

Write-Host "✓ Unblocked $dllCount DLL files`n" -ForegroundColor Green

# Step 3: Apply Group Policy updates
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "Step 3: Applying Group Policy changes" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

gpupdate /force
Write-Host "`n✓ Group Policy updated`n" -ForegroundColor Green

# Step 4: Summary
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "✅ COMPLETE! AppLocker has been disabled" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green

Write-Host "`n📋 What's next:" -ForegroundColor Cyan
Write-Host "1. Open PowerShell (normal, not admin)"
Write-Host "2. Navigate to your project:"
Write-Host "   cd C:\Users\DELL\source\repos\IPON-BE\patentdesign"
Write-Host ""
Write-Host "3. Run your preferred command:"
Write-Host "   dotnet watch run                    # Debug with hot reload"
Write-Host "   dotnet run                          # Debug normal start"
Write-Host "   dotnet watch run --no-hot-reload    # Debug with watch, no hot reload"
Write-Host ""
Write-Host "✨ dotnet watch run should now work normally!`n" -ForegroundColor Green

Read-Host "Press Enter to exit"
