@echo off
:: AI Token Tracker - Debug Build (Batch wrapper)
:: Smart build script - only builds if source files changed

echo.
echo Building AI Consumption Tracker (debug mode)...
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0debug-build.ps1" %*

if %errorlevel% neq 0 (
    echo.
    echo Build failed. Check output above.
    pause
)
