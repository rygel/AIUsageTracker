# AI Usage Tracker - Known Issues & Tasks

## Active Issues

### 🔴 Critical - Main Window Shows Nothing
**Status:** Under Investigation  
**Reported:** 2026-02-28  
**Affected Versions:** v2.2.27-beta.2

**Description:**
User reports that the main window is sometimes blank/empty, showing no provider data. This happens intermittently.

**Symptoms:**
- Main window opens but shows no content
- No provider cards visible
- Not a consistent issue (happens "sometimes")

**Possible Causes:**
- [ ] Monitor service not running or not responding
- [ ] Provider data not loading correctly
- [ ] UI initialization issue
- [ ] Placeholder data filter being too aggressive
- [ ] Race condition in data loading

**Investigation Steps:**
1. [ ] Check if Monitor service is running in system tray
2. [ ] Verify Monitor port and connectivity
3. [ ] Check if providers are configured
4. [ ] Review placeholder data filtering logic
5. [ ] Check for JavaScript/console errors in Web UI
6. [ ] Review Slim UI initialization sequence

**Files to Review:**
- `AIUsageTracker.UI.Slim/MainWindow.xaml.cs` - InitializeAsync
- `AIUsageTracker.UI.Slim/ProviderPanelBuilder.cs` - Panel creation
- `AIUsageTracker.Core/MonitorClient/MonitorService.cs` - Data fetching
- `AIUsageTracker.Monitor/Services/ProviderRefreshService.cs` - Data refresh

**Related Commits:**
- afb041d - fix: filter placeholder data in Monitor and UI
- d49b42f - chore(release): prepare v2.2.27-beta.1

---

## Recently Fixed

### ✅ Window Position Jumping When Closing Settings
**Status:** Fixed in beta.3  
**Fix:** Removed `PositionWindowNearTray()` from `ApplyPreferences()`  
**Test:** Added integration test `CloseSettingsDialog_DoesNotMoveWindowPosition`

### ✅ Version Not Showing "Beta" in Title
**Status:** Fixed in beta.3  
**Fix:** Added version detection with InformationalVersion attribute

---

## Pending Tasks

- [ ] Investigate main window blank issue (see above)
- [ ] Merge window position fix branch into develop
- [ ] Create stable release v2.2.27 after beta testing
- [ ] Update documentation with new Updates tab feature
- [ ] Test dual release channel workflow end-to-end

---

## Notes

**Beta Testing Checklist:**
- [x] Updates tab visible in Settings
- [x] Channel selector works (Stable/Beta)
- [x] Version shows "Beta" in title (beta.3+)
- [x] Window doesn't jump when closing settings (beta.3+)
- [ ] Main window consistently shows provider data
- [ ] App icon visible in Task Manager
- [ ] Update checking works for beta channel

**Questions to Answer:**
1. Does the blank window issue happen on fresh install or only after running for a while?
2. Does refreshing the data (F5 or Refresh button) fix it?
3. Is the Monitor service running when the window is blank?
4. Are there any error messages in the UI?
