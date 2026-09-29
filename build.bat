@echo off
rem Builds bin\AppLauncher.exe using the C# compiler that ships with Windows
rem (.NET Framework 4.x). No Visual Studio or .NET SDK required.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find csc.exe. Enable ".NET Framework 4.8" in "Turn Windows features on or off".
    exit /b 1
)

if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /optimize+ ^
    /out:bin\AppLauncher.exe ^
    /win32icon:src\launcher.ico ^
    /win32manifest:src\app.manifest ^
    /resource:src\launcher.png,launcher.png ^
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
    src\*.cs
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo.
echo Built bin\AppLauncher.exe
echo Copy it to a permanent folder and run it once - see README.md.
