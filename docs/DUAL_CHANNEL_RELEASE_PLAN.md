# Dual Release Channel Implementation Plan

## Overview
Implement Stable and Beta release channels to allow users to opt-in to early access versions while maintaining a separate stable release stream.

---

## Phase 1: Infrastructure Setup

### 1.1 Branch Strategy
**Priority: High | Effort: 30 min**

- [ ] Create `develop` branch from `main`
- [ ] Update branch protection rules:
  - `main`: Requires PR review, all checks must pass
  - `develop`: Allows direct pushes for rapid iteration
- [ ] Document workflow in `CONTRIBUTING.md`

**Git Workflow:**
```
feature branches → develop → main (stable releases)
                      ↓
                   beta releases (tagged from develop)
```

### 1.2 Workflow Trigger Updates
**Priority: High | Effort: 1 hour**

**Files to Modify:**
- `.github/workflows/release.yml` - Lines 1-14
- `.github/workflows/publish.yml` - Lines 1-9

**Changes:**
```yaml
# release.yml - Add trigger patterns
on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Version (e.g., 2.2.25, 2.3.0-beta.1)'
      channel:
        description: 'Release Channel'
        type: choice
        options:
          - stable
          - beta
        default: stable
```

```yaml
# publish.yml - Add tag patterns
on:
  push:
    tags:
      - 'v[0-9]+.[0-9]+.[0-9]+'        # Stable: v2.2.25
      - 'v[0-9]+.[0-9]+.[0-9]+-beta.*'  # Beta: v2.3.0-beta.1
```

---

## Phase 2: Appcast Infrastructure

### 2.1 Create Beta Appcast Files
**Priority: High | Effort: 45 min**

**New Files to Create:**
- `appcast/appcast_beta.xml`
- `appcast/appcast_beta_x64.xml`
- `appcast/appcast_beta_arm64.xml`
- `appcast/appcast_beta_x86.xml`

**Initial Content Template:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss xmlns:dc="http://purl.org/dc/elements/1.1/" 
     xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle" 
     version="2.0">
    <channel>
        <title>AI Usage Tracker (Beta Channel)</title>
        <link>https://github.com/rygel/AIConsumptionTracker/releases</link>
        <description>Beta releases with latest features.</description>
        <language>en</language>
        <!-- Beta releases will be added here -->
    </channel>
</rss>
```

### 2.2 Update Appcast Generation Script
**Priority: High | Effort: 1.5 hours**

**File to Modify:** `scripts/generate-appcast.sh`

**Requirements:**
- Accept `--channel stable|beta` parameter
- Generate appropriate appcast files based on channel
- Handle prerelease versions correctly (strip channel suffix for comparison)
- Update all 4 architecture variants per channel

**Script Changes:**
```bash
#!/bin/bash
set -e

CHANNEL="${2:-stable}"
VERSION="$1"

case "$CHANNEL" in
  stable)
    APPCAST_PREFIX="appcast"
    ;;
  beta)
    APPCAST_PREFIX="appcast_${CHANNEL}"
    ;;
  *)
    echo "Unknown channel: $CHANNEL"
    exit 1
    ;;
esac

# Generate for each architecture
for arch in x64 x86 arm64; do
  generate_appcast "$VERSION" "$APPCAST_PREFIX" "$arch"
done
```

### 2.3 Update Release Workflow
**Priority: Medium | Effort: 1 hour**

**File to Modify:** `.github/workflows/release.yml`

**Changes:**
- Pass channel parameter to `generate-appcast.sh`
- Upload beta appcast files as separate artifacts
- Add channel to validation checks

**Lines to Update:** 160-176
```yaml
- name: Generate Appcast Files
  run: |
    version="${{ inputs.version }}"
    channel="${{ inputs.channel }}"
    bash scripts/generate-appcast.sh "$version" "$channel"

- name: Upload Appcast Artifacts
  uses: actions/upload-artifact@v4
  with:
    name: appcast-files-${{ inputs.channel }}
    path: |
      appcast/appcast*.xml
```

### 2.4 Update Publish Workflow
**Priority: Medium | Effort: 1 hour**

**File to Modify:** `.github/workflows/publish.yml`

**Changes:**
- Detect channel from tag pattern (stable vs beta)
- Generate channel-specific appcasts
- Upload appropriate appcast files to release

**Lines to Update:** 226-246
```yaml
- name: Determine Release Channel
  id: channel
  run: |
    VERSION="${{ steps.get_version.outputs.version }}"
    if [[ "$VERSION" =~ -beta ]]; then
      echo "channel=beta" >> $GITHUB_OUTPUT
    else
      echo "channel=stable" >> $GITHUB_OUTPUT
    fi

- name: Generate Appcast Files
  run: |
    bash scripts/generate-appcast.sh "${{ steps.get_version.outputs.version }}" "${{ steps.channel.outputs.channel }}"

- name: Create GitHub Release
  uses: softprops/action-gh-release@v2
  with:
    files: |
      artifacts/**/*
      appcast/appcast${{ steps.channel.outputs.channel == 'stable' && '' || format('_{0}', steps.channel.outputs.channel) }}.xml
      # ... other appcasts
```

---

## Phase 3: Application Updates

### 3.1 Core Domain Models
**Priority: High | Effort: 30 min**

**File to Create:** `AIUsageTracker.Core/Models/UpdateChannel.cs`

```csharp
namespace AIUsageTracker.Core.Models;

public enum UpdateChannel
{
    Stable,
    Beta
}

public static class UpdateChannelExtensions
{
    public static string ToAppcastSuffix(this UpdateChannel channel)
    {
        return channel switch
        {
            UpdateChannel.Stable => "",
            UpdateChannel.Beta => "_beta",
            _ => ""
        };
    }
}
```

### 3.2 Update Preferences Model
**Priority: High | Effort: 30 min**

**File to Modify:** `AIUsageTracker.Core/Models/AppPreferences.cs`

**Add Property:**
```csharp
public class AppPreferences
{
    // ... existing properties ...
    
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
}
```

### 3.3 Update Preferences Store
**Priority: Medium | Effort: 45 min**

**File to Modify:** `AIUsageTracker.UI.Slim/Services/UiPreferencesStore.cs`

**Changes:**
- Ensure UpdateChannel enum is serialized/deserialized correctly
- Handle migration from old preferences (default to Stable)

### 3.4 Modify GitHubUpdateChecker
**Priority: High | Effort: 1.5 hours**

**File to Modify:** `AIUsageTracker.Infrastructure/Services/GitHubUpdateChecker.cs`

**Changes:**
- Accept UpdateChannel in constructor or CheckForUpdates method
- Modify `GetAppcastUrlForCurrentArchitecture()` to use channel

**Implementation:**
```csharp
public class GitHubUpdateChecker : IUpdateCheckerService
{
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly UpdateChannel _channel;
    
    // Constructor injection of channel
    public GitHubUpdateChecker(
        ILogger<GitHubUpdateChecker> logger, 
        UpdateChannel channel = UpdateChannel.Stable)
    {
        _logger = logger;
        _channel = channel;
    }
    
    private string GetAppcastUrlForCurrentArchitecture()
    {
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
        var suffix = _channel.ToAppcastSuffix();
        
        return _channel switch
        {
            UpdateChannel.Stable => $"https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_{arch}.xml",
            UpdateChannel.Beta => $"https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_beta_{arch}.xml"
        };
    }
}
```

### 3.5 Register Channel-Aware Service
**Priority: Medium | Effort: 30 min**

**File to Modify:** `AIUsageTracker.UI.Slim/App.xaml.cs`

**Changes:**
- Read preferences before creating update checker
- Pass channel to GitHubUpdateChecker

```csharp
// In ConfigureServices
services.AddSingleton<IUpdateCheckerService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<GitHubUpdateChecker>>();
    var preferences = sp.GetRequiredService<AppPreferences>();
    return new GitHubUpdateChecker(logger, preferences.UpdateChannel);
});
```

**Note:** This may require lazy initialization or service locator pattern since preferences are loaded async.

### 3.6 Handle Channel Switching
**Priority: Medium | Effort: 1 hour**

**File to Modify:** `AIUsageTracker.UI.Slim/MainWindow.xaml.cs`

**Changes:**
- Add method to reinitialize update checker when channel changes
- Force update check after channel change

```csharp
private void OnUpdateChannelChanged(UpdateChannel newChannel)
{
    _updateChecker = new GitHubUpdateChecker(
        _loggerFactory.CreateLogger<GitHubUpdateChecker>(), 
        newChannel);
    
    // Force immediate check
    _ = CheckForUpdatesAsync();
}
```

---

## Phase 4: UI Implementation

### 4.1 Settings Window - Add Channel Selector
**Priority: High | Effort: 1 hour**

**File to Modify:** `AIUsageTracker.UI.Slim/SettingsWindow.xaml`

**Add to XAML (in General or Updates section):**
```xml
<StackPanel Orientation="Horizontal" Margin="0,10,0,0">
    <TextBlock Text="Update Channel:" VerticalAlignment="Center" Width="120"/>
    <ComboBox x:Name="UpdateChannelComboBox" Width="150">
        <ComboBoxItem Content="Stable" Tag="Stable"/>
        <ComboBoxItem Content="Beta" Tag="Beta"/>
        <ComboBoxItem Content="Alpha (Experimental)" Tag="Alpha"/>
    </ComboBox>
</StackPanel>

<TextBlock Text="Stable: Well-tested releases"
           FontSize="11" Foreground="#808080" Margin="120,5,0,0"/>
<TextBlock Text="Beta: New features, may have bugs"
           FontSize="11" Foreground="#808080" Margin="120,2,0,0"/>
<TextBlock Text="Alpha: Experimental, frequent updates"
           FontSize="11" Foreground="#808080" Margin="120,2,0,0"/>
```

### 4.2 Settings Window - Code Behind
**Priority: High | Effort: 45 min**

**File to Modify:** `AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs`

**Changes:**
- Load current channel from preferences on window load
- Save channel to preferences on apply/OK
- Trigger update checker reinitialization on change

```csharp
private void LoadPreferences()
{
    // ... existing code ...
    
    // Set update channel
    var channelItems = UpdateChannelComboBox.Items.Cast<ComboBoxItem>();
    var matchingItem = channelItems.FirstOrDefault(item => 
        item.Tag?.ToString() == _preferences.UpdateChannel.ToString());
    if (matchingItem != null)
    {
        UpdateChannelComboBox.SelectedItem = matchingItem;
    }
}

private void SavePreferences()
{
    // ... existing code ...
    
    // Save update channel
    var selectedItem = UpdateChannelComboBox.SelectedItem as ComboBoxItem;
    if (selectedItem != null && Enum.TryParse<UpdateChannel>(selectedItem.Tag?.ToString(), out var channel))
    {
        if (_preferences.UpdateChannel != channel)
        {
            _preferences.UpdateChannel = channel;
            // Notify main window to reinitialize update checker
            OnUpdateChannelChanged?.Invoke(channel);
        }
    }
}
```

### 4.3 Add Visual Indicator
**Priority: Low | Effort: 30 min**

**Optional Enhancement:**
- Add badge or indicator in MainWindow showing current channel (if not Stable)
- Example: Title bar shows "AI Usage Tracker [BETA]"

---

## Phase 5: Documentation

### 5.1 Update User Documentation
**Priority: Medium | Effort: 45 min**

**Files to Update:**
- `README.md` - Add section about release channels
- `docs/RELEASE_CHANNELS.md` - New detailed documentation

**Content:**
- What are release channels
- How to switch channels
- Channel stability expectations
- How to report beta issues

### 5.2 Update Developer Documentation
**Priority: Low | Effort: 30 min**

**Files to Update:**
- `AGENTS.md` - Add release channel workflow
- `CONTRIBUTING.md` - Document branch strategy

---

## Phase 6: Testing Strategy

### 6.1 Unit Tests
**Priority: Medium | Effort: 1 hour**

**Files to Create:**
- `AIUsageTracker.Tests/Infrastructure/GitHubUpdateCheckerChannelTests.cs`

**Test Cases:**
- Stable channel uses correct appcast URL
- Beta channel uses correct appcast URL
- Alpha channel uses correct appcast URL
- Channel suffix generation works correctly

### 6.2 Integration Tests
**Priority: Low | Effort: 1 hour**

**Test Scenarios:**
- Channel switching triggers update check
- Preferences persist channel selection
- Appcast generation produces correct URLs for each channel

### 6.3 Manual Testing
**Priority: High | Effort: 2 hours**

**Test Checklist:**
- [ ] Create test beta release (v2.2.26-beta.1)
- [ ] Verify beta appcast is generated correctly
- [ ] Switch to beta channel in app
- [ ] Verify app detects beta update
- [ ] Switch back to stable
- [ ] Verify stable updates still work
- [ ] Verify channel preference persists after restart

---

## Implementation Timeline

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| Phase 1: Infrastructure | 1.5 hours | None |
| Phase 2: Appcast Infrastructure | 4 hours | Phase 1 |
| Phase 3: Application Updates | 4 hours | Phase 2 |
| Phase 4: UI Implementation | 2 hours | Phase 3 |
| Phase 5: Documentation | 1.5 hours | Phase 4 |
| Phase 6: Testing | 4 hours | Phase 4 |
| **Total** | **~17 hours** | |

---

## Risk Mitigation

### Risks:
1. **Breaking existing stable updates**
   - Mitigation: Keep stable appcast URLs unchanged, only add new beta appcasts

2. **Users accidentally switching to unstable channels**
   - Mitigation: Add confirmation dialog, clear warnings in UI

3. **Complexity in release process**
   - Mitigation: Document clear workflows, automate as much as possible

4. **Beta appcast not maintained**
   - Mitigation: Ensure release workflow updates both stable and beta appcasts when appropriate

---

## Success Criteria

- [ ] Users can select Stable, Beta, or Alpha update channels from Settings
- [ ] Channel selection persists across app restarts
- [ ] Beta releases appear for users on Beta channel but not Stable channel
- [ ] Stable releases appear for all users regardless of channel
- [ ] Existing stable update mechanism continues to work unchanged
- [ ] Documentation clearly explains release channels

---

## Future Enhancements (Out of Scope for Initial Implementation)

- **Automatic rollback** - Detect failed beta updates and offer rollback
- **Canary releases** - Percentage-based rollout within beta channel
- **Channel-specific changelogs** - Show different release notes per channel
- **Opt-in telemetry** - Track beta usage for quality metrics
