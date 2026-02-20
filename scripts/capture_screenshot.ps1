# Screenshot Capture Script
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

# Start the application
$process = Start-Process "c:\Develop\Claude\opencode-tracker\AIConsumptionTracker.UI.Slim\bin\Debug\net8.0-windows10.0.17763.0\AIConsumptionTracker.exe" -PassThru
Start-Sleep -Seconds 10 # Wait for load and refresh

# Take screenshot of Dashboard
$screen = [System.Windows.Forms.Screen]::PrimaryScreen
$bitmap = New-Object System.Drawing.Bitmap($screen.Bounds.Width, $screen.Bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($screen.Bounds.Location, [System.Drawing.Point]::Empty, $screen.Bounds.Size)
$bitmap.Save("c:\Develop\Claude\opencode-tracker\docs\screenshot_dashboard_privacy.png")

# Try to open settings
[void][System.Reflection.Assembly]::LoadWithPartialName('Microsoft.VisualBasic')
$shell = New-Object -ComObject WScript.Shell
if ($shell.AppActivate("AI Consumption Tracker")) {
    Start-Sleep -Seconds 1
    # Tab through buttons: ShowAll, Top, Pin, Compact, Privacy, Refresh, Settings
    # 7 Tabs to reach Settings button
    $shell.SendKeys("{TAB}{TAB}{TAB}{TAB}{TAB}{TAB}{TAB}{ENTER}")
    Start-Sleep -Seconds 5 # Wait for settings window to open and populate
    
    # Capture Settings window
    $graphics.CopyFromScreen($screen.Bounds.Location, [System.Drawing.Point]::Empty, $screen.Bounds.Size)
    $bitmap.Save("c:\Develop\Claude\opencode-tracker\docs\screenshot_settings_privacy.png")
}

# Cleanup
Stop-Process -Id $process.Id -Force
Write-Host "Screenshots saved to docs folder."
