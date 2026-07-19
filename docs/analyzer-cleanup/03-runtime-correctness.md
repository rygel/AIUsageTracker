# Work Package 03: Behavior-Sensitive Findings

## Rules

Each subsection is a separate assignment. The agent must understand the hot path and add or identify a regression test before changing behavior. Do not combine these changes with mechanical style cleanup.

## Package 03A: Async Serialization (`VSTHRD103`)

- `AIUsageTracker.CLI/Program.cs`: lines 531 and 600.
- `AIUsageTracker.Monitor.Tests/ConfigServiceExtendedTests.cs`: line 179.
- `AIUsageTracker.Tests/Infrastructure/Services/GitHubAuthServiceTests.cs`: line 52.
- `AIUsageTracker.Tests/Infrastructure/Services/ProviderDiscoveryServiceTests.cs`: line 78.
- `AIUsageTracker.Web.Tests/TestWebHost.cs`: line 31.

Use the asynchronous serializer API and propagate `await`. Preserve cancellation where a token is available. Do not use `Task.Run` to hide synchronous work and do not suppress `VSTHRD103`.

## Package 03B: Broad Exception Handling (`CA1031`)

- `AIUsageTracker.Monitor/Services/UsageDatabase.cs`: line 427.
- `AIUsageTracker.UI.Slim/App.Themes.cs`: line 492.
- `AIUsageTracker.UI.Slim/Services/UpdateInstallerHelper.cs`: line 88.

First enumerate the concrete exceptions produced by the guarded operations. Catch only recoverable exceptions, log every failure with context, and preserve existing fallback behavior only where it is an explicit product requirement. The repository forbids silent catch blocks.

## Package 03C: Thread Ownership (`VSTHRD003`)

- `AIUsageTracker.UI.Slim/MainWindow.xaml.cs`: line 383.

Trace which context creates the task and which context awaits it. This is WPF code: do not add `ConfigureAwait(false)` to UI-affine work. Validate the affected window behavior and relevant UI tests.

## Package 03D: Stream Disposal (`CA2024`)

- `AIUsageTracker.Monitor/Services/ImportService.cs`: line 91.

Ensure the returned stream or reader is disposed on success, failure, and cancellation. Add or identify an import test that exercises the lifetime.

## Package 03E: Nullability (`CS8601`)

- `AIUsageTracker.Infrastructure/Providers/MinimaxProvider.cs`: line 225.

Model the actual nullable contract. Do not use `!`, an empty-string fallback, or a default object merely to silence the compiler. Provider data must reflect the real response state.

## Package 03F: String Equality in Production (`MA0006`)

- `AIUsageTracker.Web/Services/WebProviderUsageMapper.cs`: line 22.

Choose `Ordinal` or `OrdinalIgnoreCase` from the provider-ID contract and add a focused mapper test proving the intended behavior.

## Acceptance

Each package must run its focused tests, the owning project build with `--no-incremental`, and the full Release test suite. Report the exact warning code removed and the regression test that protects the behavior.
