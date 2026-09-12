@echo off
setlocal

set "_PAUSE_ON_EXIT=1"
if /I "%~1"=="--no-pause" set "_PAUSE_ON_EXIT="

set "GAME_EXE=%~dp0..\FFX.exe"
set "STAGE0=%~dp0bin\fhstage0.exe"
set "_DOTNET_ROOT_X86=%DOTNET_ROOT(x86)%"
set "_RUNTIME_PRECHECK_MISSING="

if not exist "%GAME_EXE%" (
  set "GAME_EXE=%~dp0FFX.exe"
  if not exist "%GAME_EXE%" (
    echo [ERROR] FFX.exe not found. Expected either:
    echo   "%~dp0..\FFX.exe"
    echo   "%~dp0FFX.exe"
    set "RC=1"
    goto :finish
  )
)

if not exist "%STAGE0%" (
  echo [ERROR] fhstage0.exe not found: "%STAGE0%"
  set "RC=1"
  goto :finish
)

set "_HAS_FXR10="
call :check_fxr10 "%ProgramFiles(x86)%\dotnet\host\fxr"
call :check_fxr10 "%ProgramFiles%\dotnet\host\fxr"
call :check_fxr10 "%DOTNET_ROOT%\host\fxr"
call :check_fxr10 "%_DOTNET_ROOT_X86%\host\fxr"
call :check_dotnet_runtime10
call :check_registry_runtime10
if not defined _HAS_FXR10 (
  echo.
  echo [WARN] Microsoft .NET Runtime 10 is missing.
  call :prompt_install_runtime
  set "_HAS_FXR10="
  call :check_fxr10 "%ProgramFiles(x86)%\dotnet\host\fxr"
  call :check_fxr10 "%ProgramFiles%\dotnet\host\fxr"
  call :check_fxr10 "%DOTNET_ROOT%\host\fxr"
  call :check_fxr10 "%_DOTNET_ROOT_X86%\host\fxr"
  call :check_dotnet_runtime10
  call :check_registry_runtime10
  if not defined _HAS_FXR10 (
    echo [WARN] Runtime 10 still not detected by precheck. Continuing launch anyway.
    echo        If launch fails with load_hostfxr, install manually from:
    echo          https://dotnet.microsoft.com/en-us/download/dotnet/10.0
    set "_RUNTIME_PRECHECK_MISSING=1"
  )
)

pushd "%~dp0bin"
.\fhstage0.exe ..\..\FFX.exe
set "RC=%ERRORLEVEL%"
popd

if not "%RC%"=="0" (
  echo.
  echo [ERROR] Fahrenheit startup failed with exit code %RC%.
  if defined _RUNTIME_PRECHECK_MISSING (
    echo Runtime 10 precheck was inconclusive or missing.
  )
  echo If output contains "load_hostfxr() failed", rerun this script and allow runtime install,
  echo or install manually from:
  echo   https://dotnet.microsoft.com/en-us/download/dotnet/10.0
  goto :finish
)

goto :finish

:prompt_install_runtime
set "_INSTALL_RT="
set /p "_INSTALL_RT=Install Microsoft .NET Runtime 10 now using winget? [Y/n]: "
if /I "%_INSTALL_RT%"=="N" (
  echo [INFO] Skipping auto-install. Launch will continue.
  exit /b 0
)
where winget >NUL 2>&1
if errorlevel 1 (
  echo [WARN] winget is not available on this system.
  echo Install .NET Runtime 10 manually from:
  echo   https://dotnet.microsoft.com/en-us/download/dotnet/10.0
  exit /b 0
)
echo Installing Microsoft .NET Runtime 10 (x86 preferred)...
winget install --id Microsoft.DotNet.Runtime.10 --exact --architecture x86 --accept-package-agreements --accept-source-agreements
if errorlevel 1 (
  echo.
  echo x86 install failed or unavailable. Retrying default architecture...
  winget install --id Microsoft.DotNet.Runtime.10 --exact --accept-package-agreements --accept-source-agreements
)
if errorlevel 1 (
  echo.
  echo [WARN] Runtime installation failed. Launch will continue.
  exit /b 0
)
echo Runtime installation complete.
exit /b 0

:check_dotnet_runtime10
where dotnet >NUL 2>&1
if errorlevel 1 goto :eof
for /f "tokens=1,2,*" %%A in ('dotnet --list-runtimes 2^>NUL ^| findstr /R /I "^Microsoft\.NETCore\.App 10\."') do (
  set "_HAS_FXR10=1"
)
goto :eof

:check_registry_runtime10
reg query "HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App" /s /f 10. /d 2>NUL | findstr /I "10." >NUL && set "_HAS_FXR10=1"
reg query "HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.NETCore.App" /s /f 10. /d 2>NUL | findstr /I "10." >NUL && set "_HAS_FXR10=1"
reg query "HKLM\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.NETCore.App" /s /f 10. /d 2>NUL | findstr /I "10." >NUL && set "_HAS_FXR10=1"
goto :eof

:finish
if not defined RC set "RC=0"
if defined _PAUSE_ON_EXIT (
  echo.
  if "%RC%"=="0" (
    echo Fahrenheit launcher finished. Press any key to close this window.
  ) else (
    echo Launcher exited with code %RC%. Press any key to close this window.
  )
  pause >NUL
)
exit /b %RC%

:check_fxr10
if "%~1"=="" goto :eof
if not exist "%~1" goto :eof
for /d %%D in ("%~1\10.*") do (
  if exist "%%~fD\hostfxr.dll" set "_HAS_FXR10=1"
)
goto :eof
