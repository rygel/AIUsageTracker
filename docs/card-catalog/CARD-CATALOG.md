# Card Settings Catalog

Visual reference for every card display setting. Generated automatically in CI.

| Preview | Setting | Description |
|---------|---------|-------------|
| ![Default](card_preset-default.png) | **Default** | Factory defaults: PaceBadge, UsageRate, StatusText, ResetAbsolute. Pace-aware colours on, background bar on. |
| ![Preset: Compact](card_preset-compact.png) | **Preset: Compact** | Minimal layout: UsedPercent badge only, no secondary info, no status line. ResetAbsolute for reset time. |
| ![Preset: Detailed](card_preset-detailed.png) | **Preset: Detailed** | Full information: PaceBadge, UsageRate, StatusText, ResetAbsolute. |
| ![Preset: Pace Focus](card_preset-pace-focus.png) | **Preset: Pace Focus** | Pace-centred: PaceBadge, ProjectedPercent at end of period, DailyBudget, ResetAbsolute. |
| ![Compact Mode](card_compact-mode.png) | **Compact Mode** | Reduced card height (20px instead of 24px), tighter margins and smaller fonts. |
| ![Background Bar Off](card_no-background-bar.png) | **Background Bar Off** | Progress bar replaced by a thin 3px colour stripe on the left edge of each card. |
| ![Compact + No Background Bar](card_compact-no-bg.png) | **Compact + No Background Bar** | Compact mode combined with colour stripe. Most minimal card appearance. |
| ![Dual Quota Bars](card_dual-bars-on.png) | **Dual Quota Bars** | Providers with burst + rolling windows show two stacked progress bars. Top bar = burst (5h), bottom bar = rolling (weekly). |
| ![Dual Bars Off (Rolling)](card_dual-bars-off-rolling.png) | **Dual Bars Off (Rolling)** | Dual bars disabled, single bar shows the rolling (weekly) window. |
| ![Dual Bars Off (Burst)](card_dual-bars-off-burst.png) | **Dual Bars Off (Burst)** | Dual bars disabled, single bar shows the burst (5-hour) window. |
| ![Show Used Percentages](card_show-used.png) | **Show Used Percentages** | Displays "45% used" instead of "55% remaining". Progress bars fill from left (used) instead of depleting from right. |
| ![Pace Adjustment Off](card_pace-off.png) | **Pace Adjustment Off** | Bar colour based on raw usage percentage. No projection to end of period. A provider at 30% used always shows green, even if only 1 hour remains. |
| ![Relative Reset Time](card_reset-relative.png) | **Relative Reset Time** | Reset time shown as a countdown ("in 4h 23m") instead of an absolute timestamp. |
| ![Reset Date Only](card_reset-date.png) | **Reset Date Only** | Reset info slot shows the date portion only (no time). |
| ![Slots: Account + Auth Source](card_slots-account-auth.png) | **Slots: Account + Auth Source** | Primary badge shows account name (privacy-masked), secondary shows auth source. |
| ![Slots: Remaining + Daily Budget](card_slots-remaining-budget.png) | **Slots: Remaining + Daily Budget** | Shows remaining percentage and daily budget (how many requests per day to stay on pace). |

---

## Permutation Details

### Default

Factory defaults: PaceBadge, UsageRate, StatusText, ResetAbsolute. Pace-aware colours on, background bar on.

![Default](card_preset-default.png)

### Preset: Compact

Minimal layout: UsedPercent badge only, no secondary info, no status line. ResetAbsolute for reset time.

![Preset: Compact](card_preset-compact.png)

### Preset: Detailed

Full information: PaceBadge, UsageRate, StatusText, ResetAbsolute.

![Preset: Detailed](card_preset-detailed.png)

### Preset: Pace Focus

Pace-centred: PaceBadge, ProjectedPercent at end of period, DailyBudget, ResetAbsolute.

![Preset: Pace Focus](card_preset-pace-focus.png)

### Compact Mode

Reduced card height (20px instead of 24px), tighter margins and smaller fonts.

![Compact Mode](card_compact-mode.png)

### Background Bar Off

Progress bar replaced by a thin 3px colour stripe on the left edge of each card.

![Background Bar Off](card_no-background-bar.png)

### Compact + No Background Bar

Compact mode combined with colour stripe. Most minimal card appearance.

![Compact + No Background Bar](card_compact-no-bg.png)

### Dual Quota Bars

Providers with burst + rolling windows show two stacked progress bars. Top bar = burst (5h), bottom bar = rolling (weekly).

![Dual Quota Bars](card_dual-bars-on.png)

### Dual Bars Off (Rolling)

Dual bars disabled, single bar shows the rolling (weekly) window.

![Dual Bars Off (Rolling)](card_dual-bars-off-rolling.png)

### Dual Bars Off (Burst)

Dual bars disabled, single bar shows the burst (5-hour) window.

![Dual Bars Off (Burst)](card_dual-bars-off-burst.png)

### Show Used Percentages

Displays "45% used" instead of "55% remaining". Progress bars fill from left (used) instead of depleting from right.

![Show Used Percentages](card_show-used.png)

### Pace Adjustment Off

Bar colour based on raw usage percentage. No projection to end of period. A provider at 30% used always shows green, even if only 1 hour remains.

![Pace Adjustment Off](card_pace-off.png)

### Relative Reset Time

Reset time shown as a countdown ("in 4h 23m") instead of an absolute timestamp.

![Relative Reset Time](card_reset-relative.png)

### Reset Date Only

Reset info slot shows the date portion only (no time).

![Reset Date Only](card_reset-date.png)

### Slots: Account + Auth Source

Primary badge shows account name (privacy-masked), secondary shows auth source.

![Slots: Account + Auth Source](card_slots-account-auth.png)

### Slots: Remaining + Daily Budget

Shows remaining percentage and daily budget (how many requests per day to stay on pace).

![Slots: Remaining + Daily Budget](card_slots-remaining-budget.png)

