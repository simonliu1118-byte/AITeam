@echo off
setlocal
if "%~1"=="" (
    start "" /b wscript.exe "D:\AITeam\AITeam.vbs"
    exit /b 0
)
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "D:\AITeam\app\AITeam-Bootstrap.ps1" %*
