# Changelog

## Unreleased

- **GitHub Copilot Integration**:
  - Implemented OAuth Device Flow authentication for GitHub
  - Integrated Copilot usage tracking with API rate limit visualization
  - Display quota as progress bar in "Plans & Quotas" section with color-coded thresholds
  - Show plan type (Individual/Business/Enterprise) and account name
  - GitHub Copilot card now always visible in provider list
  - Added `CodexProvider` for Codex model tracking

- **UI/UX Improvements**:
  - Integrated login/logout functionality directly in GitHub Copilot provider card
  - Improved authentication status display with color-coded indicators

- **Font Settings Improvements**:
  - Implemented System Font Selection: Users can now choose from all installed fonts.
  - Centralized Settings Logic: Standardized how settings are shown and refreshed.

## v1.3.3 (2026-02-06)
- Fixed "white on white" font visibility issue using a custom ControlTemplate for ComboBox.

## v1.3.2 (2026-02-06)
- Fixed font family visibility in Settings (white text on white background).
- Fixed various UI build and syntax errors.
- Disabled Winget submission temporarily.

## v1.3.1 (2026-02-06)
- Fixed cross-platform release asset generation (Linux/macOS).
- Updated installer to support architecture-specific builds (x64/x86).
- Version propagation improvements in CI/CD pipeline.

## v1.3.0 (2026-02-06)
- Modernized UI with icon-based footer.
- Added headless UI testing infrastructure.
- Introduced mock providers for testing.
