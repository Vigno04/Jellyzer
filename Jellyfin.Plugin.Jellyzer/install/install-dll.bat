@echo off
setlocal enabledelayedexpansion

set "PLUGIN_NAME=Jellyzer"
set "DLL_NAME=Jellyfin.Plugin.Jellyzer.dll"
set "DLL_SRC=%~dp0..\Jellyfin.Plugin.Jellyzer\bin\Release\net9.0\%DLL_NAME%"
:: Fallback: check same directory as script (for zipped releases)
if not exist "%DLL_SRC%" set "DLL_SRC=%~dp0%DLL_NAME%"

:: Verify the DLL was built
if not exist "%DLL_SRC%" (
  echo [ERROR] DLL not found: %DLL_SRC%
  echo Run "dotnet build --configuration Release" first.
  goto UserInput
)

:: ── Try AppData (user-mode Jellyfin install) ──────────────────────────────────
if exist "%UserProfile%\AppData\Local\jellyfin\plugins\" (
  FOR /F "eol=| delims=" %%I IN ('DIR "%UserProfile%\AppData\Local\jellyfin\plugins\%PLUGIN_NAME%*" /B /O-D /TW 2^>nul') DO (
    SET "DestDir=%UserProfile%\AppData\Local\jellyfin\plugins\%%I"
    GOTO CopyDll
  )
  :: First install – create folder
  SET "DestDir=%UserProfile%\AppData\Local\jellyfin\plugins\%PLUGIN_NAME%_0.0.6"
  GOTO CopyDll
)

:: ── Try ProgramData (service-mode Jellyfin install) ──────────────────────────
if exist "%ProgramData%\Jellyfin\Server\plugins\" (
  FOR /F "eol=| delims=" %%I IN ('DIR "%ProgramData%\Jellyfin\Server\plugins\%PLUGIN_NAME%*" /B /O-D /TW 2^>nul') DO (
    SET "DestDir=%ProgramData%\Jellyfin\Server\plugins\%%I"
    GOTO CopyDll
  )
  :: First install – create folder
  SET "DestDir=%ProgramData%\Jellyfin\Server\plugins\%PLUGIN_NAME%_0.0.6"
  GOTO CopyDll
)

echo [ERROR] Jellyfin plugin directory not found!
echo Expected one of:
echo   %UserProfile%\AppData\Local\jellyfin\plugins\
echo   %ProgramData%\Jellyfin\Server\plugins\
GOTO UserInput

:CopyDll
echo Installing to: "%DestDir%"
if not exist "%DestDir%\" mkdir "%DestDir%"
xcopy /y "%DLL_SRC%" "%DestDir%\"
if errorlevel 1 (
  echo [ERROR] Copy failed. Try running this script as Administrator.
) else (
  echo.
  echo [OK] %DLL_NAME% installed successfully.
  echo Restart Jellyfin to load the updated plugin.
)

:UserInput
echo.
pause
