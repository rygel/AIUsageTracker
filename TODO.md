# TODO

## Feature Backlog

- [x] Slim UI logging migration (Priority: P1, Effort: M): Replace ad-hoc `Debug.WriteLine`/`Console.WriteLine` diagnostics with `ILogger` (structured levels, timestamped output, centralized configuration).
- [ ] Monitor logging format unification (Priority: P1, Effort: S/M): Migrate Monitor `[DIAG]`/custom diagnostic output to `ILogger` with timestamped, structured logs so Monitor and Slim UI diagnostics use one consistent format.
- [x] Burn-rate forecasting (Priority: P1, Effort: M): Estimate days until quota or budget exhaustion from recent usage trends.
- [x] Anomaly detection (experimental) (Priority: P1, Effort: M): Detect and flag unusual spikes or drops in provider usage.
- [x] Provider reliability panel (Priority: P2, Effort: M): Show provider latency, failure rate, and last successful sync timestamp.
- [ ] OpenCode CLI local provider (Priority: P2, Effort: M): Read detailed usage data from `opencode stats` CLI (sessions, messages, per-model breakdown, daily history) instead of just API credits endpoint
- [ ] Budget policies (Priority: P2, Effort: M): Add weekly/monthly provider budget caps with warning levels and optional soft-lock behavior.
- [ ] Comparison views (Priority: P3, Effort: S/M): Add period-over-period comparisons (day/week/month) and provider leaderboard by cost and growth.
- [ ] Data portability (Priority: P3, Effort: S): Support CSV/JSON export and import, plus scheduled encrypted SQLite backups.
- [ ] Plugin-style provider SDK (Priority: P3, Effort: L): Add a provider extension model with shared auth/HTTP/parsing helpers and conformance tests.
- [ ] Alert rules and notifications (Priority: P4, Effort: S/M): Add per-provider thresholds for remaining quota, spend percentage, and API failures with desktop and webhook notifications.
