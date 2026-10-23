@echo off
chcp 65001 >nul
title Instalador UDisplay Pro (Lemorele LC06 Plus - 60 FPS / BT.709 / Zero-Latency)

:: Solicita elevação automática para Administrador
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Solicitando permissao de Administrador para instalar o driver de video virtual...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo ============================================================================
echo   INSTALADOR UDISPLAY PRO - ADAPTADOR USB-C LEMORELE LC06 PLUS
echo   60 FPS Reais + Cores Calibradas BT.709 + Zero-Latency + Painel Avancado
echo ============================================================================
echo.

set "SRC_DIR=%~dp0"
set "DST_DIR=C:\ProgramData\UDisplay"

echo [1/5] Encerrando processos do UDisplay (se existirem)...
taskkill /F /IM UDisplayConfig.exe >nul 2>&1
taskkill /F /IM UDisplay.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo [2/5] Copiando arquivos do UDisplay Pro para %DST_DIR%...
if not exist "%DST_DIR%" mkdir "%DST_DIR%"
attrib -r "%DST_DIR%\UDisplay.exe" >nul 2>&1
copy /Y "%SRC_DIR%UDisplay.exe" "%DST_DIR%\UDisplay.exe" >nul
copy /Y "%SRC_DIR%UDisplayConfig.exe" "%DST_DIR%\UDisplayConfig.exe" >nul

echo [3/5] Instalando Driver de Monitor Virtual (SG_DISP_BUS) e Servico...
"%DST_DIR%\UDisplay.exe" install_service_and_drv

echo [4/5] Configurando inicializacao automatica e atalho na Area de Trabalho...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "UDisplay" /t REG_SZ /d "\"%DST_DIR%\UDisplay.exe\" daemon" /f >nul
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\Configurador UDisplay Pro.lnk'); $s.TargetPath = 'C:\ProgramData\UDisplay\UDisplayConfig.exe'; $s.WorkingDirectory = 'C:\ProgramData\UDisplay'; $s.Description = 'Painel de Configuracao e Calibracao UDisplay Pro'; $s.Save()"

echo [5/5] Iniciando UDisplay Pro e abrindo o Painel de Configuracao...
start "" "%DST_DIR%\UDisplay.exe" daemon
timeout /t 1 /nobreak >nul
start "" "%DST_DIR%\UDisplay.exe" main
start "" "%DST_DIR%\UDisplayConfig.exe"

echo.
echo ============================================================================
echo   INSTALACAO CONCLUIDA COM SUCESSO!
echo ============================================================================
timeout /t 3
