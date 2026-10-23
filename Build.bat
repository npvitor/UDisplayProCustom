@echo off
chcp 65001 >nul
title Build UDisplay Pro (WPF Configurator + Binary Patcher)

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"

if not exist "dist" mkdir "dist"

echo [1/2] Compilando src\UDisplayConfig.cs -> dist\UDisplayConfig.exe ...
"%CSC%" /nologo /optimize+ /target:winexe /out:"dist\UDisplayConfig.exe" ^
    /r:"%WPF%\PresentationCore.dll" ^
    /r:"%WPF%\PresentationFramework.dll" ^
    /r:"%WPF%\WindowsBase.dll" ^
    /r:"System.Xaml.dll" ^
    /r:"System.Drawing.dll" ^
    "src\UDisplayConfig.cs"

if %errorLevel% neq 0 (
    echo [ERRO] Falha na compilacao do UDisplayConfig.exe!
    pause
    exit /b 1
)

echo [2/2] Compilacao concluida com sucesso: dist\UDisplayConfig.exe
echo.
echo Para reconstruir tambem o UDisplay.exe a partir do binario original (original\UDisplay_Original_v1.0.2.9.exe),
echo execute: powershell -ExecutionPolicy Bypass -File tools\Patch-UDisplayBinary.ps1
echo.
pause
