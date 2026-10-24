@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF"
if not exist "dist" mkdir "dist"
"%CSC%" /nologo /optimize+ /target:winexe /win32manifest:src\app.manifest /out:"dist\UDisplayConfig.exe" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsBase.dll" /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Management.dll src\UDisplayConfig.cs src\DisplayEngine.cs src\CalibrationWindow.cs src\TrayIntegration.cs
if errorlevel 1 goto fail
copy /Y "original\UDisplay_Original_v1.0.2.9.exe" "dist\UDisplay.Original.exe" >nul
if errorlevel 1 goto fail
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Patch-UDisplayBinary.ps1
if errorlevel 1 goto fail
echo Build concluido. Nenhum processo instalado foi alterado.
popd
exit /b 0
:fail
popd
exit /b 1
