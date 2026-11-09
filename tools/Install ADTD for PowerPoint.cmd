@echo off
rem  ADTD for PowerPoint - install from the extracted release zip, without using a terminal.
rem
rem  -ExecutionPolicy Bypass applies to this one run only: a .ps1 extracted by Explorer carries the mark
rem  of the web, and running one by path is otherwise refused. Nothing on the machine is reconfigured.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Adtd.ps1" %*
echo.
pause
