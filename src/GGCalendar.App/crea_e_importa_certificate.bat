@echo off
title Creazione certificato Code Signing
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0crea_e_importa_certificate.ps1"

echo.
echo Script terminato. Premi un tasto per chiudere questa finestra.
pause > nul