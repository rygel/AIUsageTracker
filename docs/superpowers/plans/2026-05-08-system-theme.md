# System Theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "System" theme option that resolves to Dark or Light based on the OS preference, with real-time reactivity when the OS theme changes.

**Architecture:** `System` is a virtual theme entry in the `AppTheme` enum with no color palette of its own. At runtime, it resolves to Dark or Light. The WPF Slim UI uses the Windows registry + `SystemEvents.UserPreferenceChanged` for detection and reactivity. The Web UI uses `matchMedia('(prefers-color-scheme: dark)')` with a `change` listener.

**Tech Stack:** C#/WPF (registry + SystemEvents), JavaScript (matchMedia), CSS (existing dark/light themes)

---

### Task 1: Add `System` to the `AppTheme` enum

**Files:**
- Modify: `AIUsageTracker.Core/Models/AppTheme.cs:22`

- [ ] **Step 1: Add `System` as the last enum value**

In `AIUsageTracker.Core/Models/AppTheme.cs`, add after `CatppuccinLatte`:

```csharp
    CatppuccinLatte,
    System,
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds. No compile errors.

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.Core/Models/AppTheme.cs
git commit -m "feat: add System theme enum value"
```

---

### Task 2: Add OS theme detection helper to WPF Slim UI

**Files:**
- Modify: `AIUsageTracker.UI.Slim/App.Themes.cs:480-498`

- [ ] **Step 1: Add `using Microsoft.Win32;` to the imports in `App.Themes.cs`**

Add at the top of the file (after existing usings, before the namespace):

```csharp
using Microsoft.Win32;
```

- [ ] **Step 2: Add the static helper methods for OS theme detection**

Add these methods to the `App` partial class, before `ApplyTheme`:

```csharp
public static AppTheme ResolveSystemTheme()
{
    try
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
        if (key?.GetValue("AppsUseLightTheme") is int value)
        {
            return value == 1 ? AppTheme.Light : AppTheme.Dark;
        }
    }
    catch
    {
    }

    return AppTheme.Dark;
}

public static void ApplyTheme(AppTheme theme)
{
    Preferences.Theme = theme;
    var resolved = theme == AppTheme.System ? ResolveSystemTheme() : theme;
    var resources = Current?.Resources;
    if (resources == null)
    {
        return;
    }

    if (!ThemePalettes.TryGetValue(resolved, out var palette))
    {
        palette = ThemePalettes[AppTheme.Dark];
    }

    foreach (var (key, color) in palette)
    {
        SetBrushColor(resources, key, color);
    }
}
```

This replaces the existing `ApplyTheme` method (lines 480-498). The key change: resolve `System` to Dark/Light before palette lookup. Add `ResolveSystemTheme` as a new method above `ApplyTheme`.

- [ ] **Step 3: Build to verify**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add AIUsageTracker.UI.Slim/App.Themes.cs
git commit -m "feat: add OS theme detection and System theme resolution"
```

---

### Task 3: Add real-time OS theme change listener in App.xaml.cs

**Files:**
- Modify: `AIUsageTracker.UI.Slim/App.xaml.cs:144` (subscribe after ApplyTheme)
- Modify: `AIUsageTracker.UI.Slim/App.xaml.cs:153-170` (unsubscribe in OnExit)

- [ ] **Step 1: Subscribe to `SystemEvents.UserPreferenceChanged` after `ApplyTheme` call**

In `App.xaml.cs`, after line 144 (`ApplyTheme(Preferences.Theme);`), add:

```csharp
SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
```

- [ ] **Step 2: Add the event handler method**

Add this method to the `App` partial class (before `OnExit`):

```csharp
private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
{
    if (e.Category == UserPreferenceCategory.General && Preferences.Theme == AppTheme.System)
    {
        ApplyTheme(AppTheme.System);
    }
}
```

- [ ] **Step 3: Unsubscribe in `OnExit`**

In `OnExit`, before `this._trayIcon?.Dispose();` (line 155), add:

```csharp
SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add AIUsageTracker.UI.Slim/App.xaml.cs
git commit -m "feat: add real-time OS theme change listener for System theme"
```

---

### Task 4: Add "System (Auto)" to Settings UI theme dropdown

**Files:**
- Modify: `AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs:593-611`

- [ ] **Step 1: Add System option at the top of `GetThemeOptions()`**

In `SettingsWindow.xaml.cs`, modify `GetThemeOptions()` to add System as the first entry:

```csharp
private IReadOnlyList<ThemeOption> GetThemeOptions()
{
    return new List<ThemeOption>
    {
        new() { Value = AppTheme.System, Label = "System (Auto)" },
        new() { Value = AppTheme.Dark, Label = "Dark" },
        // ... rest unchanged
    };
}
```

Add only the first line (`new() { Value = AppTheme.System, Label = "System (Auto)" },`). The rest stays the same.

- [ ] **Step 2: Build to verify**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs
git commit -m "feat: add System (Auto) option to theme dropdown"
```

---

### Task 5: Handle System theme in screenshot tool

**Files:**
- Modify: `AIUsageTracker.UI.Slim/App.Screenshots.cs:157-162`

- [ ] **Step 1: Resolve System theme in screenshot parsing**

In `App.Screenshots.cs`, after line 162 (`throw new ArgumentException(...)`), change the `selectedTheme` resolution so that if it's `AppTheme.System`, it resolves immediately:

Replace the block at lines 157-162:

```csharp
var selectedTheme = AppTheme.Dark;
var themeArg = GetArgumentValue(args, "--theme");
if (!string.IsNullOrWhiteSpace(themeArg) && !Enum.TryParse<AppTheme>(themeArg, ignoreCase: true, out selectedTheme))
{
    throw new ArgumentException($"Unknown theme '{themeArg}'.", nameof(args));
}

if (selectedTheme == AppTheme.System)
{
    selectedTheme = ResolveSystemTheme();
}
```

This adds the 3-line `if` block after the existing parse logic.

- [ ] **Step 2: Build to verify**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.UI.Slim/App.Screenshots.cs
git commit -m "feat: resolve System theme in screenshot tool"
```

---

### Task 6: Add System theme to Web UI

**Files:**
- Modify: `AIUsageTracker.Web/wwwroot/js/theme.js`
- Modify: `AIUsageTracker.Web/Pages/Shared/_Layout.cshtml:73-88`

- [ ] **Step 1: Add 'system' to the theme list in `theme.js`**

Add `'system'` as the first item in the themes array (between the `GENERATED-THEME-LIST-START` and first entry):

```javascript
    // GENERATED-THEME-LIST-START
    themes: [
        'system',
        'dark',
        'light',
        // ... rest unchanged
    ],
```

- [ ] **Step 2: Add system theme resolution and change listener to `theme.js`**

Replace the entire `init()` and `setupSelect()` methods with updated versions that resolve "system" to "dark"/"light":

```javascript
    init() {
        const saved = localStorage.getItem('theme');
        if (saved && this.themes.includes(saved)) {
            this.applyTheme(saved);
        }
        this.setupSelect();
        this.setupSystemListener();
    },
    
    applyTheme(theme) {
        const resolved = this.resolveTheme(theme);
        document.documentElement.dataset.theme = resolved;
    },
    
    resolveTheme(theme) {
        if (theme === 'system') {
            return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
        return theme;
    },
    
    setupSystemListener() {
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
            if (localStorage.getItem('theme') === 'system') {
                this.applyTheme('system');
            }
        });
    },
    
    setupSelect() {
        const select = document.getElementById('theme-select');
        if (!select) return;
        
        const current = localStorage.getItem('theme') || 'dark';
        select.value = current;
        
        select.addEventListener('change', (e) => {
            const theme = e.target.value;
            localStorage.setItem('theme', theme);
            this.applyTheme(theme);
        });
    }
```

- [ ] **Step 3: Add "System" option to the dropdown in `_Layout.cshtml`**

Add as the first option inside the `GENERATED-THEME-OPTIONS-START` block (after line 73, before the existing "dark" option):

```html
<option value="system">System (Auto)</option>
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add AIUsageTracker.Web/wwwroot/js/theme.js AIUsageTracker.Web/Pages/Shared/_Layout.cshtml
git commit -m "feat: add System theme to Web UI with matchMedia detection"
```

---

### Task 7: Update tests for System theme

**Files:**
- Modify: `AIUsageTracker.Tests/UI/ThemeApplicationTests.cs:191`

- [ ] **Step 1: Verify existing tests handle System gracefully**

The existing test `AllThemes_TextIsReadableAgainstBackground` iterates `Enum.GetValues<AppTheme>()`. It calls `GetPalette(theme)` which returns `null` for `AppTheme.System` (no palette entry). The test already has `if (palette == null) continue;` — so it skips System correctly. No change needed here.

- [ ] **Step 2: Add a test for `ResolveSystemTheme`**

Add a new test class or method in `ThemeApplicationTests.cs`:

```csharp
[Fact]
public void ResolveSystemTheme_ReturnsDarkOrLight()
{
    var result = App.ResolveSystemTheme();
    Assert.True(result == AppTheme.Dark || result == AppTheme.Light,
        $"ResolveSystemTheme returned {result}, expected Dark or Light");
}
```

- [ ] **Step 3: Run all tests**

Run: `dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug --no-build --verbosity normal`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add AIUsageTracker.Tests/UI/ThemeApplicationTests.cs
git commit -m "test: add test for System theme resolution"
```

---

### Task 8: Final validation

- [ ] **Step 1: Run pre-commit checks**

Run: `./scripts/pre-commit-check.sh`
Expected: All checks pass (build, tests, format).

- [ ] **Step 2: Manual smoke test**

1. Run `dotnet run --project AIUsageTracker.UI.Slim`
2. Open Settings, select "System (Auto)" theme
3. Verify it matches the current Windows theme
4. Change Windows theme (Settings > Personalization > Colors)
5. Verify the app switches in real-time
6. Restart the app — verify "System (Auto)" is still selected and resolves correctly
