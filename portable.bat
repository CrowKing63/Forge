@echo off
rem ============================================================
rem  Portable build
rem  Single self-contained exe + portable.txt marker in dist\portable.
rem  The folder is the whole product: copy it anywhere (USB drive) and go.
rem  Settings (settings.json) and models (Models\) then live next to the exe.
rem ============================================================
cd /d "%~dp0"
echo [portable] publishing single-file self-contained build...
dotnet publish src\Forge.App\Forge.App.csproj -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none ^
  -o dist\portable
if errorlevel 1 exit /b 1
type nul > dist\portable\portable.txt
echo [portable] ready: dist\portable
