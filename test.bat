@echo off
rem ============================================================
rem  Quick test
rem  Canned demo result - no model download or load needed, so this
rem  works on any machine immediately. The overlay is also shown at
rem  startup for navigation testing. Good for exercising the result
rem  editor streaming and diff without waiting on a model.
rem ============================================================
cd /d "%~dp0"
echo [test] canned demo + overlay at startup (no model needed)
dotnet run --project src\Forge.App -- --demo --debug-overlay
