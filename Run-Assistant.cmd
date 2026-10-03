@echo off
set "APP=%~dp0release\app\HappyDDZ.Assistant.exe"
if not exist "%APP%" (
  echo Run tools\Build-Release.ps1 first.
  exit /b 1
)
start "" "%APP%"
