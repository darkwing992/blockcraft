@echo off
rem Builds BlockCraft.exe (the launcher) using the C# compiler built into Windows.
rem The game itself is ..\index.html - keep it next to BlockCraft.exe.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:"..\BlockCraft.exe" /win32icon:icon.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll Launcher.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built ..\BlockCraft.exe
