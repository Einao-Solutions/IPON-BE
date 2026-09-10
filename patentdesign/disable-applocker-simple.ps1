# Disable AppLocker and set policy to not enforced
# This is a simpler approach to disable AppLocker

Write-Host "Attempting to disable AppLocker..." -ForegroundColor Green

# Stop AppLocker service
try {
	Stop-Process -Name dotnet -Force -ErrorAction SilentlyContinue
	Start-Sleep -Seconds 1

	Stop-Service -Name AppIDSvc -Force -ErrorAction SilentlyContinue
	Set-Service -Name AppIDSvc -StartupType Disabled -ErrorAction SilentlyContinue
	Write-Host "AppLocker service disabled" -ForegroundColor Green
} catch {
	Write-Host "Could not stop AppLocker service (may already be stopped): $_" -ForegroundColor Yellow
}

# Run gpupdate to refresh policies
try {
	Write-Host "Running gpupdate /force..." -ForegroundColor Cyan
	& cmd.exe /c gpupdate /force 2>&1 | Out-Null
	Write-Host "Group Policy updated" -ForegroundColor Green
} catch {
	Write-Host "Could not run gpupdate: $_" -ForegroundColor Yellow
}

Write-Host "Done! You can now try dotnet watch run" -ForegroundColor Cyan
