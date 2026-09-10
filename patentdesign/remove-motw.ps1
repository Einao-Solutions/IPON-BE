# Remove MOTW (Mark of the Web) from DLLs to bypass AppLocker
# This removes the hidden zone.identifier stream that Windows adds to downloaded files

Write-Host "Removing Mark of the Web from DLL files..." -ForegroundColor Green

$binPath = "C:\Users\DELL\source\repos\IPON-BE\patentdesign\bin\Debug\net8.0"

if (Test-Path $binPath) {
	Get-ChildItem -Path $binPath -Filter "*.dll" -Recurse | ForEach-Object {
		$file = $_.FullName
		# Remove Zone.Identifier alternate data stream
		Remove-Item -Path "$file`:Zone.Identifier" -Force -ErrorAction SilentlyContinue
		Write-Host "  Cleaned: $($_.Name)"
	}
} else {
	Write-Host "  Warning: $binPath does not exist yet"
}

# Also unblock the main DLL explicitly
Get-ChildItem -Path $binPath -Filter "*.dll" -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
	Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
}

Write-Host "Complete!" -ForegroundColor Cyan
