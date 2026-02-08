param(
    [string]$Runtime = "win-x64",
    [string]$Version = ""
)

# AI Consumption Tracker - Distribution Packaging Script
# Usage: .\scripts\publish-app.ps1 -Runtime win-x64 -Version 1.7.5

$isWinPlatform = $Runtime.StartsWith("win-")
$projectName = if ($isWinPlatform) { "AIConsumptionTracker.UI" } else { "AIConsumptionTracker.CLI" }
$projectPath = if ($isWinPlatform) { ".\AIConsumptionTracker.UI\AIConsumptionTracker.UI.csproj" } else { ".\AIConsumptionTracker.CLI\AIConsumptionTracker.CLI.csproj" }
$publishDir = ".\dist\publish-$Runtime"

# If Version not passed, extract from project file
if ([string]::IsNullOrEmpty($Version)) {
    $projectContent = Get-Content $projectPath -Raw
    if ($projectContent -match "<Version>(.*?)</Version>") {
        $Version = $matches[1]
    } else {
        $Version = "1.5.1"
    }
}

$zipPath = ".\dist\AIConsumptionTracker_v$Version`_$Runtime.zip"

Write-Host "Cleaning dist folder for $Runtime..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "Publishing $projectName for $Runtime (Version: $Version)..." -ForegroundColor Cyan

# For Linux/Mac (CLI), we want SingleFile but NOT AOT (cross-OS native compilation not supported)
# For Windows (UI), we just use standard publish (installer handles the rest)
$singleFileParam = if (-not $isWinPlatform) { "-p:PublishSingleFile=true" } else { "" }
$selfContained = if (-not $isWinPlatform) { "true" } else { "false" }

# Explicitly disable AOT for cross-platform builds to avoid "Cross-OS native compilation is not supported" error
$aotParam = if (-not $isWinPlatform) { "-p:PublishAot=false" } else { "" }

dotnet publish $projectPath `
    -c Release `
    -r $Runtime `
    --self-contained $selfContained `
    -o $publishDir `
    $singleFileParam `
    $aotParam `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version

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
        
        $archDef = if ($Runtime -like "*x64") { "x64" } elseif ($Runtime -like "*arm64") { "arm64" } else { "x86" }
        
        & $iscc "scripts\setup.iss" "/DSourcePath=..\dist\publish-$Runtime" "/DMyAppVersion=$Version" "/DMyAppArch=$archDef"
        if ($LASTEXITCODE -eq 0) {
            # Move and rename the created setup to include architecture
            $setupDir = ".\dist"
            # The name in setup.iss is OutputBaseFilename=AIConsumptionTracker_Setup_v{#MyAppVersion}
            # So it will be AIConsumptionTracker_Setup_v1.7.10.exe
            $setupFile = Get-ChildItem "$setupDir\AIConsumptionTracker_Setup_v*.exe" -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike "*_$Runtime.exe" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            
            if ($setupFile) {
                $newName = "AIConsumptionTracker_Setup_v$($Version)_$($Runtime).exe"
                if ($setupFile.Name -ne $newName) {
                    Rename-Item $setupFile.FullName -NewName $newName -Force
                    Write-Host "Installer created and renamed: $newName" -ForegroundColor Green
                } else {
                    Write-Host "Installer created: $newName" -ForegroundColor Green
                }
            } else {
                Write-Host "Error: Could not find generated setup file to rename." -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "Inno Setup compilation failed." -ForegroundColor Yellow
            exit 1
        }
    } else {
        Write-Host "Error: Inno Setup (ISCC.exe) not found. This is required for Windows builds." -ForegroundColor Red
        exit 1
    }
}

Write-Host "--------------------------------------------------" -ForegroundColor Yellow
Write-Host "Distribution ready at: $zipPath" -ForegroundColor Green
Write-Host "Size: $((Get-Item $zipPath).Length / 1MB) MB" -ForegroundColor Gray
Write-Host "--------------------------------------------------" -ForegroundColor Yellow

