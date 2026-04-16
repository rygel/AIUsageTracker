# SonarQube Local Analysis

Run SonarQube scans locally against your self-hosted SonarQube instance without CI/CD pipeline integration.

## Prerequisites

1. **SonarQube server running** at `http://localhost:9000` (or your configured URL)
2. **Authentication token** — generate at: `http://localhost:9000/account/security/`
3. **.NET SonarScanner tool** — install once:
   ```powershell
   dotnet tool install --global dotnet-sonarscanner
   ```

## Setup

1. Copy the example env file:
   ```powershell
   copy .env.example .env
   ```

2. Edit `.env` with your credentials:
   ```
   SONAR_TOKEN=your_token_here
   SONAR_HOST_URL=http://localhost:9000
   ```

   The `.env` file is gitignored — your credentials stay local.

3. Review and run the scan script:
   ```powershell
   .\scripts\sonar.ps1
   ```

## Usage

```powershell
# Full scan (begin + build + test coverage + end)
.\scripts\sonar.ps1

# Scan with custom project key
.\scripts\sonar.ps1 -ProjectKey "MyProject"

# Skip the build step (if already done)
.\scripts\sonar.ps1 -SkipBuild

# Skip coverage collection
.\scripts\sonar.ps1 -SkipCoverage
```

## How It Works

The script:
1. Loads credentials from `.env` into environment variables
2. Runs `dotnet sonarscanner begin` with your project key and server URL
3. Runs `dotnet build` (unless `-SkipBuild`)
4. Runs `dotnet test` with OpenCover collection (unless `-SkipCoverage`)
5. Runs `dotnet sonarscanner end` to upload results

Results appear at `http://localhost:9000` in your project dashboard.

## Troubleshooting

**"Unable to connect to SonarQube server"**
- Verify your SonarQube server is running: `curl http://localhost:9000`
- Check `SONAR_HOST_URL` in `.env`

**"Token is invalid"**
- Regenerate your token at `http://localhost:9000/account/security/`
- Update `SONAR_TOKEN` in `.env`

**"0 lines of code"**
- Ensure you're running from the solution root
- Check that `dotnet build` completes successfully before `sonarscanner end`
