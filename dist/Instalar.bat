@echo off
setlocal
chcp 65001 >nul
echo Instale primeiro o driver pelo instalador original do adaptador.
echo Este instalador integra o painel novo ao clique esquerdo no icone da bandeja.
echo A captura pode reiniciar brevemente. Cores e ajustes atuais serao preservados.
echo Mantenha UDisplayConfig.exe e UDisplay.Original.exe juntos nesta pasta.
start "" "%~dp0UDisplayConfig.exe" --install
