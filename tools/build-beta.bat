@echo off
rem Builds the portable beta exe: publish\SonicScrewdriver-beta.exe (single file, same options as the release).
rem Close a running beta first, Windows cannot overwrite an exe that is running.
setlocal
cd /d "%~dp0.."

set "TMPOUT=%TEMP%\ss-beta-build"
if exist "%TMPOUT%" rmdir /s /q "%TMPOUT%"

echo Building...
dotnet publish src\SonicScrewdriver -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -p:Version=1.0.0-beta ^
  -o "%TMPOUT%" --nologo -v quiet
if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  exit /b 1
)

if not exist publish mkdir publish
copy /y "%TMPOUT%\SonicScrewdriver.exe" "publish\SonicScrewdriver-beta.exe" >nul
if errorlevel 1 (
  echo.
  echo Could not write publish\SonicScrewdriver-beta.exe: is the beta still running? Close it and run this again.
  exit /b 1
)
rmdir /s /q "%TMPOUT%"

echo.
echo Done: %CD%\publish\SonicScrewdriver-beta.exe
for %%F in ("publish\SonicScrewdriver-beta.exe") do echo Size: %%~zF bytes
endlocal
