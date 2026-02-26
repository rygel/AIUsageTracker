param(
    [string]$Runtime = "win-x64",
    [string]$Version = ""
)

# AI Usage Tracker - Distribution Packaging Script
# Usage: .\scripts\publish-app.ps1 -Runtime win-x64 -Version 2.2.15

$isWinPlatform = $Runtime.StartsWith("win-")
$projectName = if ($isWinPlatform) { "AIUsageTracker" } else { "AIUsageTracker.CLI" }
$projectPath = if ($isWinPlatform) { ".\AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj" } else { ".\AIUsageTracker.CLI\AIUsageTracker.CLI.csproj" }
$publishDir = ".\dist\publish-$Runtime"

# If Version passed, synchronize it across all files
if (-not [string]::IsNullOrEmpty($Version)) {
    Write-Host "Synchronizing version $Version across all files..." -ForegroundColor Cyan
    
    # Create a clean version (x.y.z) for AssemblyVersion/FileVersion which don't support semantic suffixes
    $cleanVersion = $Version.Split('-')[0]
    
    # 1. Update shared version source
    if (Test-Path "Directory.Build.props") {
        $propsContent = Get-Content "Directory.Build.props" -Raw
        $newProps = $propsContent -replace "<TrackerVersion>.*?</TrackerVersion>", "<TrackerVersion>$Version</TrackerVersion>"
        $newProps = $newProps -replace "<TrackerAssemblyVersion>.*?</TrackerAssemblyVersion>", "<TrackerAssemblyVersion>$cleanVersion</TrackerAssemblyVersion>"
        Set-Content "Directory.Build.props" $newProps -NoNewline
        Write-Host "  Updated Directory.Build.props" -ForegroundColor Gray
    }

    # 2. Update README.md badge
    if (Test-Path "README.md") {
        $escapedVersion = $Version -replace "-", "--"
        $readmeContent = Get-Content "README.md" -Raw
        $newReadme = [regex]::Replace($readmeContent, "!\[Version\]\(https://img\.shields\.io/badge/version-[^)]+\)", "![Version](https://img.shields.io/badge/version-$escapedVersion-orange)")
        # Update installation instructions version as well
        $newReadme = $newReadme -replace "AIUsageTracker_Setup_v[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.]+)?\.exe", "AIUsageTracker_Setup_v$($Version).exe"
        Set-Content "README.md" $newReadme -NoNewline
        Write-Host "  Updated README.md" -ForegroundColor Gray
    }

    # 3. Update scripts/setup.iss
    if (Test-Path "scripts\setup.iss") {
        $issContent = Get-Content "scripts\setup.iss" -Raw
        $newIss = $issContent -replace '#define MyAppVersion ".*"', "#define MyAppVersion `"$Version`""
        Set-Content "scripts\setup.iss" $newIss -NoNewline
        Write-Host "  Updated scripts/setup.iss" -ForegroundColor Gray
    }

    # 4. Update scripts/publish-app.ps1 (self)
    if (Test-Path "scripts\publish-app.ps1") {
        $selfContent = Get-Content "scripts\publish-app.ps1" -Raw
        $newSelf = $selfContent -replace "-Version [0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.]+)?", "-Version $Version"
        Set-Content "scripts\publish-app.ps1" $newSelf -NoNewline
        Write-Host "  Updated scripts/publish-app.ps1" -ForegroundColor Gray
    }
} else {
    # If Version not passed, extract from shared version source
    if (Test-Path "Directory.Build.props") {
        $propsContent = Get-Content "Directory.Build.props" -Raw
        if ($propsContent -match "<TrackerVersion>(.*?)</TrackerVersion>") {
            $Version = $matches[1]
        }
    }

    # Fallback to project file if shared source is missing
    if ([string]::IsNullOrEmpty($Version)) {
        $projectContent = Get-Content $projectPath -Raw
        if ($projectContent -match "<Version>(.*?)</Version>") {
            $Version = $matches[1]
        }
    }

    if ([string]::IsNullOrEmpty($Version)) {
        $Version = "1.5.1"
    }

    if ($Version -match "^\$\((.*?)\)$") {
        $propertyName = $matches[1]
        if (Test-Path "Directory.Build.props") {
            $propsContent = Get-Content "Directory.Build.props" -Raw
            if ($propsContent -match "<$propertyName>(.*?)</$propertyName>") {
                $Version = $matches[1]
            }
        }
    }
}

$cleanVersion = $Version.Split('-')[0]

$zipPath = ".\dist\AIUsageTracker_v$Version`_$Runtime.zip"

Write-Host "Cleaning dist folder for $Runtime..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

if ($isWinPlatform) {
    $windowsProjects = @(
        @{ Name = "Tracker"; ProjectPath = ".\AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj"; ExeName = "AIUsageTracker.exe" },
        @{ Name = "Monitor"; ProjectPath = ".\AIUsageTracker.Monitor\AIUsageTracker.Monitor.csproj"; ExeName = "AIUsageTracker.Monitor.exe" },
        @{ Name = "Web"; ProjectPath = ".\AIUsageTracker.Web\AIUsageTracker.Web.csproj"; ExeName = "AIUsageTracker.Web.exe" },
        @{ Name = "CLI"; ProjectPath = ".\AIUsageTracker.CLI\AIUsageTracker.CLI.csproj"; ExeName = "AIUsageTracker.CLI.exe" }
    )

    foreach ($app in $windowsProjects) {
        $componentDir = Join-Path $publishDir $app.Name
        New-Item -ItemType Directory -Path $componentDir -Force | Out-Null

        Write-Host "Publishing $($app.Name) for $Runtime (Version: $Version)..." -ForegroundColor Cyan
        dotnet publish $app.ProjectPath `
            -c Release `
            -r $Runtime `
            --self-contained false `
            -o $componentDir `
            -p:PublishReadyToRun=false `
            -p:DebugType=None `
            -p:Version=$Version `
            -p:AssemblyVersion=$cleanVersion `
            -p:FileVersion=$cleanVersion

        $outputExe = Join-Path $componentDir $app.ExeName
        if (Test-Path $outputExe) {
            Write-Host "Build Successful: $($app.Name) ($($app.ExeName)) created." -ForegroundColor Green
        } else {
            Write-Host "Build Failed: $($app.ExeName) not found in $componentDir." -ForegroundColor Red
            exit 1
        }
    }
} else {
    Write-Host "Publishing $projectName for $Runtime (Version: $Version)..." -ForegroundColor Cyan

    # For Linux/Mac (CLI), we want SingleFile but NOT AOT (cross-OS native compilation not supported)
    $singleFileParam = "-p:PublishSingleFile=true"
    $selfContained = "true"
    $aotParam = "-p:PublishAot=false"

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
        -p:AssemblyVersion=$cleanVersion `
        -p:FileVersion=$cleanVersion
}

Write-Host "Copying documentation..." -ForegroundColor Cyan
Copy-Item ".\README.md" -Destination $publishDir
if (Test-Path ".\LICENSE") { Copy-Item ".\LICENSE" -Destination $publishDir }

Write-Host "Verifying output..." -ForegroundColor Cyan
if (-not $isWinPlatform) {
    $exeName = $projectName
    if (Test-Path "$publishDir\$exeName") {
        Write-Host "Build Successful: $exeName created." -ForegroundColor Green
    } else {
        Write-Host "Build Failed: $exeName not found in $publishDir." -ForegroundColor Red
        exit 1
    }
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
            # The name in setup.iss is OutputBaseFilename=AIUsageTracker_Setup_v{#MyAppVersion}
            # So it will be AIUsageTracker_Setup_v1.7.10.exe
            $setupFile = Get-ChildItem "$setupDir\AIUsageTracker_Setup_v*.exe" -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike "*_$Runtime.exe" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            
            if ($setupFile) {
                $newName = "AIUsageTracker_Setup_v$($Version)_$($Runtime).exe"
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



