@echo off
rem Builds Fokkus.exe with the C# compiler that ships with Windows (.NET Framework 4.8) - nothing to install.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x
  exit /b 1
)
if not exist bin mkdir bin
set ICON=
if exist assets\fokkus.ico set ICON=/win32icon:assets\fokkus.ico

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu /out:bin\Fokkus.exe /win32manifest:app.manifest %ICON% ^
  /resource:assets\logo-512.png,Fokkus.logo.png ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED
  exit /b 1
)
echo Built bin\Fokkus.exe
