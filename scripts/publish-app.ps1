param(
    [string]$Runtime = "win-x64"
)

# AI Consumption Tracker - Distribution Packaging Script
# Usage: .\scripts\publish-app.ps1 -Runtime win-x64

$isWinPlatform = $Runtime.StartsWith("win-")
$projectName = if ($isWinPlatform) { "AIConsumptionTracker.UI" } else { "AIConsumptionTracker.CLI" }
$projectPath = if ($isWinPlatform) { ".\AIConsumptionTracker.UI\AIConsumptionTracker.UI.csproj" } else { ".\AIConsumptionTracker.CLI\AIConsumptionTracker.CLI.csproj" }
$publishDir = ".\dist\publish-$Runtime"
$zipPath = ".\dist\AIConsumptionTracker_$Runtime.zip"

Write-Host "Cleaning dist folder for $Runtime..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "Publishing $projectName for $Runtime (SingleFile, FrameworkDependent)..." -ForegroundColor Cyan
dotnet publish $projectPath `
    -c Release `
    -r $Runtime `
    --self-contained false `
    -o $publishDir `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None

Write-Host "Copying documentation..." -ForegroundColor Cyan
Copy-Item ".\README.md" -Destination $publishDir
if (Test-Path ".\LICENSE") { Copy-Item ".\LICENSE" -Destination $publishDir }

Write-Host "Verifying output..." -ForegroundColor Cyan
$exeName = if ($isWinPlatform) { "$projectName.exe" } else { $projectName }
if (Test-Path "$publishDir\$exeName") {
    Write-Host "Build Successful: $exeName created." -ForegroundColor Green
} else {
    Write-Host "Build Failed: $exeName not found in $publishDir." -ForegroundColor Red
    exit 1
}

Write-Host "Creating Distribution ZIP..." -ForegroundColor Cyan
# We compress the whole publish directory
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# Inno Setup Installer (Only for Windows)
if ($isWinPlatform) {
    $isccLocal = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    $isccX86 = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    $iscc = if (Test-Path $isccLocal) { $isccLocal } else { $isccX86 }

    if ($iscc -and (Test-Path $iscc)) {
        Write-Host "Compiling Inno Setup Installer using $iscc..." -ForegroundColor Cyan
        # Pass the architecture to the ISS script if needed, or rely on internal logic
        # For now, we assume the ISS handles x64/x86 via setup.iss modifications if needed
        # But we can pass architecture as a param to ISCC if we update setup.iss
        & $iscc "scripts\setup.iss" /Q "/DSourcePath=..\dist\publish-$Runtime"
        if ($LASTEXITCODE -eq 0) {
            # Move and rename the created setup to include architecture
            $setupDir = ".\dist"
            $setupFile = Get-ChildItem "$setupDir\AIConsumptionTracker_Setup_v*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($setupFile) {
                $newName = $setupFile.Name.Replace(".exe", "_$Runtime.exe")
                Rename-Item $setupFile.FullName -NewName $newName
                Write-Host "Installer created successfully: $newName" -ForegroundColor Green
            }
        } else {
            Write-Host "Inno Setup compilation failed." -ForegroundColor Yellow
            exit 1
        }
    } else {
        Write-Host "Inno Setup (ISCC.exe) not found. Skipping installer creation." -ForegroundColor Yellow
    }
}

Write-Host "--------------------------------------------------" -ForegroundColor Yellow
Write-Host "Distribution ready at: $zipPath" -ForegroundColor Green
Write-Host "Size: $((Get-Item $zipPath).Length / 1MB) MB" -ForegroundColor Gray
Write-Host "--------------------------------------------------" -ForegroundColor Yellow

