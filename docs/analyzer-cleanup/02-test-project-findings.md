# Work Package 02: Test-Project Findings

## Goal

Remove the remaining test-code style and comparison findings without changing production code or weakening assertions.

## Package 02A: Provider Test Qualification and Equality

Files:

- `AIUsageTracker.Tests/Infrastructure/Providers/AnthropicUsageProviderTests.cs`
  - `IDE0009` and `SA1101`: lines 24, 57, 86, 125, 153, 201.
  - `MA0006`: lines 92 and 97.
- `AIUsageTracker.Tests/Infrastructure/Providers/GroqProviderTests.cs`
  - `IDE0009` and `SA1101`: lines 23, 60, 92, 110, 130.
  - `MA0006`: lines 69 and 76.
- `AIUsageTracker.Tests/Infrastructure/Providers/ZaiProviderTests.cs`
  - `MA0006`: lines 246 and 252.

Use `this.` for instance test members where required. Replace equality operators only with an ordinal `string.Equals` form that preserves the existing case sensitivity. Do not loosen assertions or alter fixture values.

## Package 02B: Test Using Order

Fix `SA1210` only in:

- `AIUsageTracker.Monitor.Tests/ConfigServiceScanTests.cs`
- `AIUsageTracker.Monitor.Tests/ProviderUsageProcessingPipelineTests.cs`
- `AIUsageTracker.Tests/Architecture/CodeGuardrailTests.cs`
- `AIUsageTracker.Tests/Architecture/SlimGroupedUsageGuardrailTests.cs`
- `AIUsageTracker.Tests/Infrastructure/ProviderMetadataCatalogTests.cs`
- `AIUsageTracker.Tests/Infrastructure/Providers/AntigravityProviderTests.cs`
- `AIUsageTracker.Tests/Infrastructure/Providers/OpenCodeZenProviderTests.cs`
- `AIUsageTracker.Tests/Services/ConfigServiceSaveValidationTests.cs`
- `AIUsageTracker.Tests/UI/MainWindowDeterministicFixtureTests.cs`
- `AIUsageTracker.Tests/UI/ProviderKeyDeletionEndToEndTests.cs`
- `AIUsageTracker.Tests/UI/ProviderVisualCatalogTests.cs`
- `AIUsageTracker.Tests/UI/SettingsWindowDeterministicFixtureTests.cs`

## Exclusions

The `VSTHRD103` test findings are behavior-sensitive and belong to Package 03. Do not disable threading analyzers for these files; test code already has only the specifically documented test overrides.

## Acceptance

```powershell
$env:AGENT_OWNER='agent-name'; $env:AGENT_TASK='test-analyzer-gate'
dotnet format AIUsageTracker.sln --verify-no-changes --severity warn --include <all changed .cs files>

$env:AGENT_TASK='test-analyzer-tests'
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release
dotnet test AIUsageTracker.Monitor.Tests/AIUsageTracker.Monitor.Tests.csproj --configuration Release
```

Done means the scoped files emit none of `IDE0009`, `SA1101`, `MA0006`, or `SA1210`, and all assertions still pass.
