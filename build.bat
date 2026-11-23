@echo off
setlocal
cd /d "%~dp0"
title Building Glass Sidebar

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo Could not find the C# compiler that ships with Windows - .NET Framework 4.x.
  echo Turn on ".NET Framework 4.8 Advanced Services" in "Turn Windows features on or off".
  pause & exit /b 1
)

echo Closing any running copy of Glass...
taskkill /im GlassSidebar.exe /f >nul 2>&1
timeout /t 1 >nul

echo Compiling with %FW%\csc.exe ...
"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /unsafe /codepage:65001 ^
  /out:GlassSidebar.exe ^
  /lib:"%FW%\WPF" ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:System.dll /r:System.Core.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll ^
  /r:System.Security.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /resource:src\ui.xaml,ui.xaml ^
  src\*.cs

if errorlevel 1 (
  echo.
  echo Build failed - copy the errors above and send them over.
  pause & exit /b 1
)

echo.
echo Built GlassSidebar.exe  -  launching it now.
start "" "%~dp0GlassSidebar.exe"
timeout /t 3 >nul
