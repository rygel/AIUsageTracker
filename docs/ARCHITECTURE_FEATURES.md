# Architecture Features

This document outlines the architecture, benefits, and drawbacks of the Agent, Web UI, and Slim UI components.

## Agent Service

The Agent is a background HTTP service responsible for data collection and persistence.

**Key Features**
- Runs as a background process (default port 5000, auto-discovery up to 5010).
- Centralized SQLite database for storage (providers, history, snapshots).
- Configurable data refresh intervals (default: 5 minutes).
- Serves data via HTTP API to clients.

**Benefits**
- Decouples data fetching from the user interface.
- Enables continuous monitoring without keeping a heavy UI open.
- Allows multiple clients (Web, Slim UI) to consume the same data source.
- Persists historical usage data independent of UI sessions.

**Drawbacks**
- Adds complexity by requiring a separate background process.
- Potential port conflicts if the default range is occupied.
- Requires management of the service lifecycle (start/stop).

## Web UI

The Web UI is a server-rendered application providing browser-based access to usage data.

**Key Features**
- Built with ASP.NET Core Razor Pages.
- Uses HTMX for efficient, partial page updates (auto-refresh every 60s).
- Read-only access to the Agent's SQLite database.
- Includes 7 built-in themes (Dark, Light, High Contrast, etc.).

**Benefits**
- Cross-platform accessibility via any web browser.
- Lightweight frontend with no client-side installation required.
- Fast initial load times due to server-side rendering.
- Responsive design suitable for various screen sizes.

**Drawbacks**
- Dependent on the Agent service and database availability.
- Less native integration with the operating system compared to desktop apps.
- Strictly read-only; configuration changes must be made elsewhere.

## Slim UI

The Slim UI is a lightweight Windows desktop client designed for minimal resource usage.

**Key Features**
- Native WPF application with a compact footprint.
- Automatically discovers and connects to the running Agent service.
- Provides system tray integration and a simplified dashboard.
- Uses the Agent for all data fetching and preference management.

**Benefits**
- Significantly lower resource consumption than the standard monolithic UI.
- Native Windows experience with system tray support.
- Fast startup and responsive interaction.
- Visual consistency with the main application design.

**Drawbacks**
- Windows-only (WPF dependency).
- Strictly requires the Agent service to be running to function.
- Reduced feature set compared to the full Standard UI (focused on monitoring).
