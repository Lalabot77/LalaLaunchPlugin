@echo off
setlocal enabledelayedexpansion

:: === CONFIGURATION ===
set "SOURCE=C:\Projects\GitHub\LalaLaunchPlugin\bin\Release\LaunchPlugin.dll"
set "DESTFOLDER=C:\Users\Andy_\OneDrive\Documents\SimHub Projects\LaunchPluginProjects\Builds"
set "SIMHUBFOLDER=C:\Program Files (x86)\SimHub"
set SIMHUBEXE=SimHubWPF.exe
set "VERSIONFILE=%DESTFOLDER%\BuildVersion.txt"

:: === Check build exists ===
if not exist "%SOURCE%" (
    echo ERROR: LaunchPlugin.dll not found at %SOURCE%
    pause
    exit /b
)

:: === Ensure backup folder exists ===
if not exist "%DESTFOLDER%" (
    mkdir "%DESTFOLDER%"
)

:: === Auto-increment build version ===
if not exist "%VERSIONFILE%" (
    echo 1 > "%VERSIONFILE%"
)
set /p BUILDNUM=<"%VERSIONFILE%"
set /a BUILDNUM+=1
echo %BUILDNUM% > "%VERSIONFILE%"
set AUTOVERSION=build%BUILDNUM%

:: === Prompt for version label (optional) ===
set /p VERSION=Enter version label (or leave blank for auto-version: %AUTOVERSION%): 
if "%VERSION%"=="" set VERSION=%AUTOVERSION%

:: === Generate timestamp ===
for /f "tokens=1-3 delims=/- " %%a in ("%date%") do (
    set YYYY=%%c
    set MM=%%a
    set DD=%%b
)
for /f "tokens=1-2 delims=: " %%x in ("%time%") do (
    set HH=%%x
    set MN=%%y
)

set "TIMESTAMP=%YYYY%-%MM%-%DD%_%HH%-%MN%"
set "DESTFILE=%DESTFOLDER%\LaunchPlugin_%VERSION%_%TIMESTAMP%.dll"

:: === Copy file to backup location ===
copy "%SOURCE%" "!DESTFILE!" >nul
echo Backup created: !DESTFILE!

:: === Log the backup ===
echo [%DATE% %TIME%] Backed up as: !DESTFILE! >> "%DESTFOLDER%\BackupLog.txt"

:: === Prompt to copy to SimHub plugin folder ===
choice /M "Also copy to SimHub plugin folder (LaunchPlugin.dll)?"
if errorlevel 2 (
    echo Skipped SimHub plugin copy.
    goto End
)

:: === Check if SimHub is running ===
set SIMHUB_RUNNING=0
for /f %%P in ('tasklist /FI "IMAGENAME eq %SIMHUBEXE%" ^| findstr /I "%SIMHUBEXE%"') do (
    set SIMHUB_RUNNING=1
)

if "!SIMHUB_RUNNING!"=="1" (
    echo SimHub is currently running.
    choice /M "Close SimHub now to update the plugin?"
    if errorlevel 2 (
        echo Cancelled plugin update. SimHub is still running.
        goto End
    ) else (
        echo Closing SimHub...
        taskkill /IM %SIMHUBEXE% /F >nul 2>&1
        echo Waiting for SimHub to fully close...
        timeout /t 5 >nul
    )
)

:: === Copy to SimHub folder ===
echo Copying plugin to SimHub folder...
copy "%SOURCE%" "%SIMHUBFOLDER%\LaunchPlugin.dll"
if exist "%SIMHUBFOLDER%\LaunchPlugin.dll" (
    echo Copy successful: LaunchPlugin.dll now in SimHub folder.
) else (
    echo Copy failed: LaunchPlugin.dll was not found after copy attempt.
)

:: === Restart SimHub ===
echo Starting SimHub...
start "" "%SIMHUBFOLDER%\%SIMHUBEXE%"

:End

pause
