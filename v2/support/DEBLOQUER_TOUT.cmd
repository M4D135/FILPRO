@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Debloquer-Pack.ps1"
set "FILPRO_RESULT=%ERRORLEVEL%"
if not "%FILPRO_RESULT%"=="0" echo Echec du deblocage. Consulter le message ci-dessus.
pause
exit /b %FILPRO_RESULT%
