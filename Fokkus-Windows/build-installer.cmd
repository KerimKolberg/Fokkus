@echo off
rem Builds Fokkus.exe and then the installer dist\Fokkus-Setup-<version>.exe (needs Inno Setup 6: https://jrsoftware.org/isinfo.php)
setlocal
cd /d "%~dp0"
call "%~dp0build.cmd" || exit /b 1

set ISCC=
for %%P in ("%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe") do (
  if exist %%P set ISCC=%%P
)
if not defined ISCC (
  where ISCC.exe >nul 2>nul && set ISCC=ISCC.exe
)
if not defined ISCC (
  echo Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup
  exit /b 1
)

%ISCC% /Q "%~dp0installer\Fokkus.iss"
if errorlevel 1 (
  echo Installer build FAILED
  exit /b 1
)
echo Built ..\dist\Fokkus-Setup-*.exe
