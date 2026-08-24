# CLI (`act`)

Entry: `AIUsageTracker.CLI/Program.cs`. Empty-args banner: `act <command> [options]`. Assembly/installer name: `AIUsageTracker.CLI` (`setup.iss` shortcut `{app}\AIUsageTracker.CLI.exe`). `Main` starts the Monitor before parsing (`MonitorLifecycleService`), so there is no no-op `--help`; `--help` prints `Unknown command`.

| Command | Options | Behavior |
|---|---|---|
| `status` | `--all`, `--json` | Current usage. Without `--all`, rows with `IsAvailable == false` are dropped (`ShowStatusAsync`). |
| `history` | `[days]` (default 7), `--json` | History |
| `list` | `--json` | Configured providers |
| `set-key` | `<provider-id> [api-key]` | Prompt if key omitted |
| `remove-key` | `<provider-id>` | Remove key |
| `scan` | | Discover keys |
| `config` | `[key] [value]` | Show or set a preference |
| `agent` | `start\|stop\|restart\|info` | Monitor process (`ManageAgentAsync`) |

`check [provider-id]` and `export --format csv|json --days N --output <file>` (export defaults: csv, 30 days) are implemented but omitted from the empty-args banner. The banner lists `agent …|log`; `log` hits `Unknown agent command`.

Env vars: `docs/environment_variables.md`.
