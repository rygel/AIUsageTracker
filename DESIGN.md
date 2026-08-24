# Progress bars

In-force UI decision. Change only with an explicit product decision.

Defaults (`AppPreferences`): `ColorThresholdYellow = 60`, `ColorThresholdRed = 80` (used-percentage thresholds). `ShowUsedPercentages` default `false`. JSON `InvertProgressBar` / `InvertCalculations` are load aliases for that flag (`AppPreferences.cs`).

Fill width uses remaining percent unless Show Used is on (`ProviderCardRenderer`). Color uses **used** percent via `UsageMath.GetThresholdTier` (`>=` yellow/red).

Pace-aware colouring (Cards tab): if `IsPaceAdjusted`, OverPace → red, otherwise green — yellow is not used (`ProviderCardRenderer.GetProgressBarColor(PaceColorResult)`).

Dual-bar cards need a `WindowedProviderUsage` with both `WindowKind.Burst` and `WindowKind.Rolling` window cards and matching `QuotaWindows` on the definition (`MainWindowRuntimeLogic.TryBuildDualBarData`). Dual-bar cards suppress the single reset badge (`docs/adr/001-reset-time-presentation.md`).
