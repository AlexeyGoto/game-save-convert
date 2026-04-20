@echo off
:: Pre-launch wrapper for Steam games
:: Usage in Steam Launch Options: "C:\Tools\SaveCompat\pre-launch-steam.cmd" re9 %command%
:: Or without game code (auto-detect): "C:\Tools\SaveCompat\pre-launch-steam.cmd" %command%
:: Automatically detects Steam ID and save path

setlocal

set "GAME=%1"
shift

:: Get active Steam32 ID from registry
for /f "tokens=3" %%a in ('reg query "HKCU\Software\Valve\Steam\ActiveProcess" /v ActiveUser 2^>nul ^| findstr ActiveUser') do set "STEAM32=%%a"

:: Convert hex to decimal
set /a STEAM32=%STEAM32%

if "%STEAM32%"=="0" (
    echo [pre-launch] No active Steam user, skipping save-convert
    goto LAUNCH
)

:: Steam64 = Steam32 + 76561197960265728
:: cmd can't handle 64-bit math, use powershell
for /f %%a in ('powershell -Command "[long]%STEAM32% + 76561197960265728"') do set "STEAM64=%%a"

:: Determine save path based on game code
if /i "%GAME%"=="re9"      set "APPID=3764200"
if /i "%GAME%"=="mhw"      set "APPID=2246340"
if /i "%GAME%"=="dd2"      set "APPID=2054970"
if /i "%GAME%"=="dr"       set "APPID=2527390"
if /i "%GAME%"=="kg"       set "APPID=2510710"
if /i "%GAME%"=="mhs3"     set "APPID=2852190"
if /i "%GAME%"=="mmsf"     set "APPID=3500390"
if /i "%GAME%"=="pragmata" set "APPID=3357650"

:: Auto-detect via Steam environment variable when no game code specified
if "%GAME%"=="" if not "%SteamAppId%"=="" set "APPID=%SteamAppId%"

:: If APPID is still empty, GAME arg might be the %command% itself (no game code provided)
if not defined APPID (
    if not "%SteamAppId%"=="" (
        set "APPID=%SteamAppId%"
    ) else (
        echo [pre-launch] No game code and no SteamAppId, skipping save-convert
        goto LAUNCH
    )
)

:: Steam userdata save path
set "SAVEPATH=%APPDATA%\..\..\Steam\userdata\%STEAM32%\%APPID%\remote\win64_save"

:: Fallback: GSE Saves path (for emulators)
if not exist "%SAVEPATH%" set "SAVEPATH=%APPDATA%\GSE Saves\%APPID%\remote\win64_save"

if not exist "%SAVEPATH%" (
    echo [pre-launch] Save folder not found, skipping
    goto LAUNCH
)

echo [pre-launch] Steam ID: %STEAM64% (Steam32: %STEAM32%)
echo [pre-launch] Save path: %SAVEPATH%
echo [pre-launch] Running save-convert (AppID: %APPID%)...

if defined GAME (
    "C:\Tools\SaveCompat\save-convert.exe" -%STEAM64% -"%SAVEPATH%" -%GAME% -silent
) else (
    "C:\Tools\SaveCompat\save-convert.exe" -%STEAM64% -"%SAVEPATH%" -silent
)
echo [pre-launch] save-convert exit code: %errorlevel%

:LAUNCH
:: Launch the original game (all remaining args = %command%)
%1 %2 %3 %4 %5 %6 %7 %8 %9
