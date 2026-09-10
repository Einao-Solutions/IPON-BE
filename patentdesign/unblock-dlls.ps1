# PowerShell script to unblock DLLs and rebuild
# This resolves: "An Application Control policy has blocked this file" (0x800711C7)

Write-Host "🔓 Unblocking DLL files..." -ForegroundColor Green
Get-ChildItem -Path "bin", "obj" -Recurse -Filter "*.dll" -ErrorAction SilentlyContinue | 
	ForEach-Object { 
		Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
		Write-Host "  ✓ Unblocked: $($_.Name)"
	}

Write-Host "🗑️  Cleaning build artifacts..." -ForegroundColor Green
Remove-Item -Path "bin", "obj" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  ✓ Cleaned bin and obj directories"

Write-Host "🔨 Rebuilding project..." -ForegroundColor Green
dotnet build --configuration Debug

Write-Host "✅ Complete! You can now run: dotnet watch run" -ForegroundColor Cyan
