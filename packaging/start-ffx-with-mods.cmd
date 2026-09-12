@echo off
setlocal

rem Starts Final Fantasy X through Fahrenheit. This file belongs in the game
rem directory, next to FFX.exe. It only forwards to fahrenheit\start-fahrenheit.cmd,
rem which checks the .NET runtime and performs the actual launch.

set "STARTER=%~dp0fahrenheit\start-fahrenheit.cmd"

if not exist "%STARTER%" (
  echo [ERROR] "%STARTER%" not found.
  echo The "fahrenheit" folder must sit next to this file, in the game directory with FFX.exe.
  echo.
  pause
  exit /b 1
)

call "%STARTER%" %*
exit /b %ERRORLEVEL%
