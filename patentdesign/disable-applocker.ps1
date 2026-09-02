# PowerShell script to disable AppLocker and allow local DLL execution
# Run as Administrator

Write-Host "🚨 Disabling AppLocker enforcement..." -ForegroundColor Red

# Disable AppLocker service
Write-Host "`n1. Stopping AppLocker service..."
Stop-Service -Name AppIDSvc -Force -ErrorAction SilentlyContinue
Set-Service -Name AppIDSvc -StartupType Disabled -ErrorAction SilentlyContinue
Write-Host "   ✓ AppLocker service disabled"

# Disable policies via Group Policy
Write-Host "`n2. Opening Group Policy Editor (gpedit.msc)..."
Write-Host "   Navigate to: Computer Configuration > Windows Settings > Security Settings > Application Control Policies > AppLocker"
Write-Host "   For each policy (Executable Rules, DLL Rules, etc.):"
Write-Host "   - Double-click the policy"
Write-Host "   - Select 'Not Configured' or 'Disabled'"
Write-Host "   - Click OK"
Write-Host "   - Click 'Apply' and 'OK'"
Write-Host ""
Write-Host "3. After changes, run: `ngpupdate /force`"
Write-Host ""

# Launch Group Policy Editor
Start-Process gpedit.msc

Write-Host "✅ AppLocker has been disabled." -ForegroundColor Green
Write-Host "ℹ️  Run 'gpupdate /force' after making policy changes" -ForegroundColor Yellow
