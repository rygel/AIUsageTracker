# WPF async

Production code must not use `.GetAwaiter().GetResult()`, `.Wait(`, or `.Result` (`CodeGuardrailTests.ProductionCode_DoesNotUseSyncOver`). Slim UI code-behind must not use `ConfigureAwait(false)` (`ConfigureAwaitGuardrailTests`).

- Use `async Task` except event handlers (`async void`); those must catch exceptions.
- Core/Infrastructure: `ConfigureAwait(false)`.
- Monitor HTTP timeouts: 8s usage, 3s config (`MonitorService.UsageRequestTimeoutSeconds`, `ConfigRequestTimeoutSeconds`).
- Fire-and-forget (`_ =`) only for non-critical background work.
