# System Architecture Overview

The AI Consumption Tracker has been structured into a modular system.

## Key Components

**Background Monitoring**
The **Agent Service** runs in the background, enabling continuous data collection independent of the dashboard state.

**Remote Access**
The **Web UI** allows dashboard access via a browser on the local network using server-side rendering.

**Desktop Client**
The **Slim UI** is a native Windows application with reduced resource usage compared to the full client. It integrates with the system tray and connects automatically to the Agent.

## Considerations

This architecture involves the following trade-offs:

- **Background Process**: The Agent must be running for the UI components to function.
- **Read-Only Web**: Configuration changes are managed in the desktop application; the Web UI is currently read-only.
- **Platform Specifics**: The Slim UI remains Windows-only to support system tray integration.
