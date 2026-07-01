# GitHub Pages Landing Page Design

## Goal
A single-page static site at `https://rygel.github.io/AIUsageTracker/` showcasing the app's features, supported providers, and screenshots.

## Technology
- Hand-written `index.html` + `style.css` + minimal JS (for mobile nav / lightbox)
- No frameworks, no build tools, no dependencies
- GitHub Pages serves from `docs/landing/` folder

## File Structure
```
docs/landing/
  index.html      — single-page site
  style.css       — all styling
  app.js          — minimal JS (screenshot lightbox, mobile nav toggle)
```

## Page Sections

### 1. Hero
- Left: app icon (favicon.png), app name, tagline, two CTA buttons (Download, GitHub)
- Right: dashboard screenshot
- Dark gradient background

### 2. Provider Grid
- Responsive grid of provider name badges
- Covers all 14+ supported providers from README

### 3. Feature Highlights
- 3-column responsive grid (collapses to 1 column on mobile)
- Six features: Smart Discovery, Live Dashboard, Tray Integration, Pace Projection, Multi-Provider, Auto-Updates

### 4. Screenshot Showcase
- Grid of 6-8 best screenshots with click-to-enlarge (CSS-only lightbox)

### 5. Download Section
- Version badge, releases link, 3-step install instructions

### 6. Footer
- Links: GitHub, Discord, User Manual, CLI docs, Architecture, MIT license

## Visual Design
- Background: `#0f0f23` primary, `#1a1a2e` cards
- Accent: `#e94560` (CTAs, links, highlights)
- Text: `#eee` primary, `#aaa` secondary
- Font: system-ui stack
- Border radius: 12px on cards
- Max content width: 1200px centered

## Deployment
- GitHub Pages settings: serve from `docs/` folder on `main` branch
- `docs/landing/index.html` becomes the site root
- Existing `docs/` screenshots referenced by relative path (already in repo)

## Assets (all existing, no new files needed)
- `docs/screenshot_dashboard_privacy.png` — hero
- `docs/screenshot_settings_providers_privacy.png` — providers
- `docs/screenshot_web_dashboard.png` — web UI
- `docs/screenshot_web_charts.png` — charts
- `docs/card-catalog/card_preset-default.png` — card presets
- `docs/card-catalog/card_pace-off.png` — pace badges
- `AIUsageTracker.Web/wwwroot/favicon.png` — app icon
