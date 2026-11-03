@echo off
rem ============================================================
rem  Near-real use
rem  The real user flow: tray icon + Ctrl+E hotkey, real model loaded
rem  from settings, no debug scaffolding. This is what a user actually
rem  runs day to day.
rem ============================================================
cd /d "%~dp0"
echo [use] real flow - tray + Ctrl+E, real model
dotnet run --project src\Forge.App
