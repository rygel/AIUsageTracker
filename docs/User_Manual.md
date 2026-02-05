# AI Consumption Tracker - User Manual

Welcome to the **AI Consumption Tracker** user manual. This guide will help you understand the features of the graphical user interface and how to manage your AI provider consumption effectively.

## Dashboard Overview

The main dashboard provides a real-time overview of your AI usage across various providers. It is designed to be lightweight, stay out of your way, and provide at-a-glance information.

### Top Bar & Window Controls
- **Draggable Header**: You can move the window by clicking and dragging the top dark bar.
- **Close (X)**: Closes the dashboard window. The application continues running in the system tray if configured.

---

## User Interface Features

The footer of the dashboard contains several toggles and buttons to customize your experience:

### 1. Show All
- **Toggle**: Filters the list of providers.
- **Behavior**: When enabled, all configured providers are shown. When disabled, only providers with active usage or specific alerts are displayed, keeping your list clean and focused.

### 2. Top (Always on Top)
- **Toggle**: Controls window layering.
- **Behavior**: When checked, the dashboard will stay above all other windows, ensuring your consumption data is always visible while you work.

### 3. Pin (Stay Open)
- **Toggle**: Controls auto-hide behavior.
- **Behavior**: 
    - **Pinned**: The window remains open until you manually close it.
    - **Unpinned**: The window will automatically hide when it loses focus (e.g., when you click into another application), making it perfect for quick checks.

### 4. Compact View
- **Toggle**: Adjusts the layout density.
- **Behavior**: When enabled, the UI uses a more condensed layout with smaller fonts and tighter spacing, ideal for keeping the window small on your screen.

### 5. Refresh
- **Button**: Triggers an immediate update.
- **Behavior**: Fetches the latest usage data from all configured API providers. The dashboard also performs periodic background refreshes.

### 6. Settings
- **Button**: Opens the **Provider Settings** window.
- **Features**:
    - **API Keys**: Configure your keys for OpenAI, Anthropic, Gemini, etc.
    - **Scan for Keys**: Automatically searches your environment variables and files for existing AI API keys to speed up setup.
    - **Save/Cancel**: Apply your changes or discard them without saving.

---

## System Tray Integration
The application runs primarily in the system tray (near the clock). You can right-click the icon to:
- Open the Dashboard.
- Open Settings directly.
- Exit the application completely.
