# Project Architecture & Philosophy

This document outlines the architectural design and project structure of the AI Consumption Tracker.

## Core Philosophy: Clean Architecture

The project follows the principles of **Clean Architecture** to maintain a separation of concerns, improve testability, and ensure platform independence where possible.

### 1. Separation of Intent from Implementation
We separate **What** the application does (Core) from **How** it does it (Infrastructure).
- Interfaces are defined in **Core**.
- Concrete implementations (Database, API Clients, Windows Services) are in **Infrastructure**.

### 2. Dependency Rule
Dependencies always point inwards.
- `UI`, `Agent`, and `CLI` depend on `Infrastructure` and `Core`.
- `Infrastructure` depends on `Core`.
- `Core` has **zero** dependencies on other project layers.

---

## Project Structure

### [AIUsageTracker.Core](file:///c:/Develop/Claude/opencode-tracker/AIUsageTracker.Core)
The **Domain Layer**. It contains:
- **Models**: Plain Data Objects (DTOs) used across the entire solution (e.g., `ProviderUsage`, `AgentInfo`).
- **Interfaces**: Definitions for services (e.g., `INotificationService`, `IUsageDatabase`).
- **Shared Logic**: Utility classes that are platform-agnostic.
- **Philosophy**: This project should remain "pure" .NET without any Windows-specific or third-party library dependencies (except for basic logging or JSON).

### [AIUsageTracker.Infrastructure](file:///c:/Develop/Claude/opencode-tracker/AIUsageTracker.Infrastructure)
The **Implementation Layer**. It contains:
- **Providers**: The actual logic for connecting to AI Service APIs (OpenAI, Anthropic, etc.).
- **Data Access**: SQLite implementation using Dapper for usage history.
- **Services**: Concrete implementations of Core interfaces (e.g., `WindowsNotificationService`).
- **Configuration**: Logic for loading/saving `auth.json` and `providers.json`.
- **Philosophy**: This is where the "heavy lifting" happens. It Target's `net8.0-windows` because it uses Windows-specific APIs.

### [AIUsageTracker.Monitor](file:///c:/Develop/Claude/opencode-tracker/AIUsageTracker.Monitor)
The **Background Engine**.
- It runs as a low-privilege background process.
- It is responsible for periodic data collection from all AI providers.
- It exposes a **Local REST API** (typically on port 5000) that the UI and CLI consume.
- **Philosophy**: Centrally manage data collection to prevent multiple applications from hitting API rate limits simultaneously.

### [AIUsageTracker.UI](file:///c:/Develop/Claude/opencode-tracker/AIUsageTracker.UI) & [AIUsageTracker.UI.Slim](file:///c:/Develop/Claude/opencode-tracker/AIUsageTracker.UI.Slim)
The **Presentation Layer**.
- **UI**: The full-featured management dashboard.
- **UI.Slim**: A lightweight tray-based monitor for quick status checks.
- Both communicate with the **Agent** via the local API.

### [AIUsageTracker.CLI](file:///c:/Develop/Claude/opencode-tracker/AIUsageTracker.CLI)
The **Management Tool**.
- Provides a command-line interface `act` for users who prefer the terminal.
- Allows for scripting and headless status checks.

### [AIUsageTracker.Web](file:///c:/Develop/Claude/opencode-tracker/AIUsageTracker.Web)
The **Alternative Dashboard**.
- A web-based view of the usage data, useful for remote monitoring or as a lightweight alternative to the WPF apps.

---

## Why This Matters

1. **Robustness**: If the UI crashes, the Agent continues to track data.
2. **Performance**: The UI stays responsive because heavy network/DB work is offloaded to the Agent.
3. **Flexibility**: You can use the CLI, the Slim UI, or the Full UI interchangeably—they all talk to the same source of truth (the Agent).
4. **Maintainability**: Adding a new AI provider only requires a new class in `Infrastructure`, without touching the UI logic.


