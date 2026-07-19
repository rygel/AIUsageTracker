# Work Package 01: Mechanical Style Findings

## Scope

This package contains formatting and ordering changes only. It must not alter control flow, public contracts, database queries, provider parsing, or UI behavior.

## Package 01A: Using Order

Fix `SA1210` in the following non-test files:

- `AIUsageTracker.CLI/Program.cs`
- `AIUsageTracker.Infrastructure/Configuration/JsonConfigLoader.cs`
- `AIUsageTracker.Infrastructure/Configuration/JsonProviderConfigExportBuilder.cs`
- `AIUsageTracker.Infrastructure/Configuration/RooTokenConfigParser.cs`
- `AIUsageTracker.Infrastructure/Configuration/TokenDiscoveryService.cs`
- `AIUsageTracker.Monitor/Program.cs`
- `AIUsageTracker.Monitor/Services/AuthDiagnosticsSnapshotBuilder.cs`
- `AIUsageTracker.Monitor/Services/ConfigService.cs`
- `AIUsageTracker.Monitor/Services/GroupedUsageProjectionService.cs`
- `AIUsageTracker.Monitor/Services/ProviderRefreshCircuitBreakerService.cs`
- `AIUsageTracker.Monitor/Services/ProviderRefreshService.cs`
- `AIUsageTracker.Monitor/Services/ProviderUsagePersistenceService.cs`
- `AIUsageTracker.Monitor/Services/ProviderUsageProcessingPipeline.cs`
- `AIUsageTracker.Monitor/Services/UsageAlertsService.cs`
- `AIUsageTracker.Monitor/Services/UsageDatabase.cs`
- `AIUsageTracker.UI.Slim/App.TrayIcon.cs`
- `AIUsageTracker.UI.Slim/FlatWindowCardBuilder.cs`
- `AIUsageTracker.UI.Slim/GroupedUsageDisplayAdapter.cs`
- `AIUsageTracker.UI.Slim/MainWindow.Rendering.cs`
- `AIUsageTracker.UI.Slim/MainWindowDeterministicFixture.cs`
- `AIUsageTracker.UI.Slim/MainWindowRuntimeLogic.Presentation.cs`
- `AIUsageTracker.UI.Slim/Services/WpfProviderIconService.cs`
- `AIUsageTracker.UI.Slim/SettingsWindow.Providers.cs`
- `AIUsageTracker.UI.Slim/SettingsWindowDeterministicFixture.cs`
- `AIUsageTracker.Web/Services/WebProviderDisplayNameMapper.cs`

Order `System` namespaces first, followed by third-party and project namespaces according to the repository style. Do not add or remove dependencies while reordering imports.

The test-file `SA1210` findings belong to Package 02.

## Package 01B: Layout and Braces

Files and live findings:

- `AIUsageTracker.Monitor/Program.cs`: `SA1501` at lines 36 and 37.
- `AIUsageTracker.Monitor/Services/UsageDatabase.cs`: `SA1503` at 401, 408, 409, 410, 413, 420, and 424; `SA1513` at 414 and 712; `SA1127` at 789; `SA1507` at 830.
- `AIUsageTracker.Monitor/Services/ProviderUsageProcessingPipeline.cs`: `SA1513` at 262.
- `AIUsageTracker.Monitor/Services/DatabaseMigrationService.cs`: `SA1515` at 245.
- `AIUsageTracker.UI.Slim/GroupedUsageDisplayAdapter.cs`: `SA1512` at 11.
- `AIUsageTracker.UI.Slim/MainWindowRuntimeLogic.Presentation.cs`: `SA1512` at 10.
- `AIUsageTracker.UI.Slim/MainWindowDeterministicFixture.cs`: `SA1515` at 65.
- `AIUsageTracker.Tests/UI/ProviderCardPresentationCatalogTests.cs`: `SA1515` at 337. This single test formatting finding may be included here, but do not touch other test warnings.

For omitted-brace findings, add braces without rewriting conditions. For blank-line findings, change whitespace only. Database migration statements must not be reordered or rewritten.

## Acceptance

Run a changed-file gate first:

```powershell
$env:AGENT_OWNER='agent-name'; $env:AGENT_TASK='mechanical-style-gate'
dotnet format AIUsageTracker.sln --verify-no-changes --severity warn --include <all changed .cs files>
```

Then run:

```powershell
$env:AGENT_OWNER='agent-name'; $env:AGENT_TASK='mechanical-style-build'
dotnet build AIUsageTracker.sln --configuration Release --no-restore --no-incremental --verbosity quiet -m:1
```

Done means the assigned `SA` codes are absent from the build output for every scoped file. Do not accept a successful exit code with findings still printed.
