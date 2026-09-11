Option Explicit
Dim shell, command
Set shell = CreateObject("WScript.Shell")
command = "powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File ""D:\AITeam\app\AITeam-Bootstrap.ps1"""
shell.Run command, 0, False
