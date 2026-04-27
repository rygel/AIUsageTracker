# opencode-tracker

> Unified desktop dashboard for developers to monitor AI provider usage, quotas, and spending across multiple services.

---

## TL;DR (30 seconds)

AI Usage Tracker is a .NET 8.0 WPF application with a background HTTP service (Monitor) that collects usage data from AI providers like OpenAI, Anthropic, GitHub Copilot, and others. It provides a compact always-on-top desktop UI (Slim), a web dashboard, and a CLI for cross-platform access. Developers get real-time visibility into their AI spending and quotas without logging into each provider separately.

---

## Target Users

Individual developers and small teams who subscribe to multiple AI coding services (GitHub Copilot, OpenAI, Anthropic, Cursor, etc.) and need a single pane of glass to track remaining quotas, spending trends, and usage history. They value minimal setup — configure API keys once, then the tracker runs passively in the background.

---

## What Problems Does This Solve?

- **Fragmented usage data**: AI providers each have their own dashboard with different layouts, metrics, and refresh cadences — this consolidates everything into one view.
- **Surprise billing**: No early warning when credits or quotas are running low across providers — the tracker shows real-time remaining percentages with color-coded alerts.
- **Manual monitoring overhead**: Developers waste time checking multiple provider portals — the background Monitor auto-refreshes on a 60-second interval.
- **No historical trends**: Provider portals show current usage only — this stores time-series data indefinitely in SQLite for trend analysis.
- **Cross-platform access**: Web UI and CLI complement the WPF desktop app, allowing remote or non-Windows access to the same data.

---

## Build Sequence

<!-- PHASES:START -->

```yaml
phases:
  - id: phase-0
    slug: "setup"
    label: "Project Setup"
    description: "Project scaffolding, solution structure, build system, CI/CD pipelines"
    status: "Done"
    order: 0
  - id: phase-1
    slug: "core-provider-framework"
    label: "Core Provider Framework"
    description: "Provider abstraction layer, configuration system, database schema, HTTP API for the Monitor service"
    status: "Done"
    order: 1
  - id: phase-2
    slug: "desktop-ui"
    label: "Desktop UI (Slim)"
    description: "Compact WPF always-on-top dashboard with provider cards, progress bars, tooltips, settings dialog, and theme support"
    status: "Done"
    order: 2
  - id: phase-3
    slug: "web-dashboard"
    label: "Web Dashboard"
    description: "ASP.NET Core Razor Pages web app with provider overview, history charts, theme system, and HTMX auto-refresh"
    status: "Done"
    order: 3
  - id: phase-4
    slug: "providers-and-testing"
    label: "Provider Expansion & Testing"
    description: "Additional provider integrations, comprehensive test coverage, screenshot baselines, fixture synchronization"
    status: "In Progress"
    order: 4
  - id: phase-5
    slug: "distribution-and-polish"
    label: "Distribution & Polish"
    description: "Installer (Inno Setup), auto-update via Sparkle appcast, winget submission, CI/CD release pipeline"
    status: "Not Started"
    order: 5
```

<!-- PHASES:END -->

---

## Decisions Locked

*No decisions locked yet. These are added as planning cycles confirm strategic choices.*