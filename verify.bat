@echo off
rem ============================================================
rem  Manual verification
rem  The overlay is shown at startup so you can test Up/Down,
rem  click, Enter and Esc without pressing Ctrl+E. A debug log is
rem  written to %TEMP% so the state trail is available if anything
rem  looks off. Watch the tray icon go from loading to ready; once
rem  it is ready, Enter runs a real task.
rem ============================================================
cd /d "%~dp0"
echo [verify] overlay at startup + debug log
dotnet run --project src\Forge.App -- --debug-overlay --debug-log %TEMP%\forge-verify.log
