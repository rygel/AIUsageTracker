param(
    [string]$ProjectKey = "AIUsageTracker",
    [switch]$SkipBuild,
    [switch]$SkipCoverage
)

$ErrorActionPreference = "Stop"

function Invoke-DotNetOrThrow {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args,
        [string]$Step = "dotnet command"
    )

    & dotnet @Args
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$envFile = Join-Path $repoRoot ".env"

if (Test-Path $envFile) {
    Write-Host "Loading environment from .env..."
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#=]+)=(.*)$') {
            $name = $Matches[1].Trim()
            $value = $Matches[2].Trim()
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

$token = $env:SONAR_TOKEN
$hostUrl = $env:SONAR_HOST_URL

if ([string]::IsNullOrEmpty($token)) {
    Write-Error "SONAR_TOKEN not set. Copy .env.example to .env and fill in your credentials."
    exit 1
}

if ([string]::IsNullOrEmpty($hostUrl)) {
    $hostUrl = "http://localhost:9000"
}

Write-Host "Starting SonarQube scan for project: $ProjectKey"
Write-Host "Server: $hostUrl"

Set-Location $repoRoot
$sonarUserHome = Join-Path $repoRoot ".sonar-user-home"
New-Item -ItemType Directory -Path $sonarUserHome -Force | Out-Null

Write-Host "`n--- Beginning analysis ---"
$sonarArgs = @(
    "sonarscanner", "begin",
    "/k:$ProjectKey",
    "/d:sonar.host.url=$hostUrl",
    "/d:sonar.token=$token",
    "/d:sonar.userHome=$sonarUserHome"
)
if (-not $SkipCoverage) {
    $sonarArgs += "/d:sonar.cs.opencover.reportsPaths=TestResults/**/coverage.opencover.xml"
    $sonarArgs += "/d:sonar.coverage.exclusions=**/Migrations/**,**/Program.cs,**/Seeder/**,scripts/**"
}
Invoke-DotNetOrThrow -Args $sonarArgs -Step "SonarScanner begin"

if (-not $SkipBuild) {
    Write-Host "`n--- Building solution ---"
    Invoke-DotNetOrThrow -Args @("build", "AIUsageTracker.sln", "--configuration", "Debug") -Step "dotnet build"
}

if (-not $SkipCoverage) {
    Write-Host "`n--- Collecting coverage ---"
    $testResultsDir = Join-Path $repoRoot "TestResults"
    if (Test-Path $testResultsDir) {
        Write-Host "  Cleaning previous test results..."
        Remove-Item -Path $testResultsDir -Recurse -Force
    }

    $testProjects = @(
        @{ Path = "AIUsageTracker.Tests\AIUsageTracker.Tests.csproj"; Filter = "FullyQualifiedName!~DialogOpenBehaviorTests&FullyQualifiedName!~UpdatePipelineEndToEndTests" },
        @{ Path = "AIUsageTracker.Monitor.Tests\AIUsageTracker.Monitor.Tests.csproj"; Filter = "" },
        @{ Path = "AIUsageTracker.Web.Tests\AIUsageTracker.Web.Tests.csproj"; Filter = "" }
    )

    foreach ($proj in $testProjects) {
        Write-Host "`n  Running tests for $($proj.Path)..."
        $coverageArgs = @(
            "test", $proj.Path,
            "--configuration", "Debug",
            "--results-directory", "TestResults",
            "--collect:XPlat Code Coverage;Format=opencover"
        )
        if (-not $SkipBuild) {
            $coverageArgs += "--no-build"
        }
        if (-not [string]::IsNullOrEmpty($proj.Filter)) {
            $coverageArgs += "--filter"
            $coverageArgs += $proj.Filter
        }

        Invoke-DotNetOrThrow -Args $coverageArgs -Step "dotnet test coverage for $($proj.Path)"
    }

    $coverageFiles = Get-ChildItem -Path "TestResults" -Recurse -Filter "coverage.opencover.xml"
    Write-Host "`n  Coverage files collected: $($coverageFiles.Count)"
    foreach ($f in $coverageFiles) {
        Write-Host "    $($f.FullName)"
    }
}

Write-Host "`n--- Ending analysis and uploading ---"
Invoke-DotNetOrThrow -Args @("sonarscanner", "end", "/d:sonar.token=$token") -Step "SonarScanner end"

Write-Host "`nScan complete. View results at $hostUrl/dashboard?id=$ProjectKey"
