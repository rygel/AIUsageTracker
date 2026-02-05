# AI Consumption Tracker - Distribution Packaging Script
# Usage: .\scripts\publish-app.ps1

$projectName = "AIConsumptionTracker.UI"
$projectPath = ".\AIConsumptionTracker.UI\AIConsumptionTracker.UI.csproj"
$publishDir = ".\dist\publish-single"
$zipPath = ".\dist\AIConsumptionTracker.zip"

Write-Host "Cleaning dist folder..." -ForegroundColor Cyan
if (Test-Path ".\dist") { Remove-Item -Recurse -Force ".\dist" }
New-Item -ItemType Directory -Path $publishDir

Write-Host "Publishing $projectName (SingleFile, FrameworkDependent)..." -ForegroundColor Cyan
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $publishDir `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None

Write-Host "Copying documentation..." -ForegroundColor Cyan
Copy-Item ".\README.md" -Destination $publishDir
if (Test-Path ".\LICENSE") { Copy-Item ".\LICENSE" -Destination $publishDir }

Write-Host "Verifying output..." -ForegroundColor Cyan
if (Test-Path "$publishDir\$projectName.exe") {
    Write-Host "Build Successful: $projectName.exe created." -ForegroundColor Green
} else {
    Write-Host "Build Failed: $projectName.exe not found in $publishDir." -ForegroundColor Red
    exit 1
}

Write-Host "Creating Distribution ZIP..." -ForegroundColor Cyan
# We compress the whole publish directory
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# Inno Setup Installer
$isccLocal = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
$isccX86 = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$iscc = if (Test-Path $isccLocal) { $isccLocal } else { $isccX86 }

if ($iscc -and (Test-Path $iscc)) {
    Write-Host "Compiling Inno Setup Installer using $iscc..." -ForegroundColor Cyan
    & $iscc "scripts\setup.iss" /Q
    if ($LASTEXITCODE -eq 0) {
        # Find the created setup file (versioned)
        $setupFile = Get-ChildItem ".\dist\AIConsumptionTracker_Setup_*.exe" | Select-Object -First 1
        Write-Host "Installer created successfully: $($setupFile.FullName)" -ForegroundColor Green
    } else {
        Write-Host "Inno Setup compilation failed." -ForegroundColor Yellow
        exit 1
    }
} else {
    Write-Host "Inno Setup (ISCC.exe) not found. Skipping installer creation." -ForegroundColor Yellow
}

Write-Host "--------------------------------------------------" -ForegroundColor Yellow
Write-Host "Distribution ready at: $zipPath" -ForegroundColor Green
Write-Host "Size: $((Get-Item $zipPath).Length / 1MB) MB" -ForegroundColor Gray
Write-Host "--------------------------------------------------" -ForegroundColor Yellow

