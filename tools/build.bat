@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-and-deploy.ps1"
set EXITCODE=%ERRORLEVEL%
echo.
if not "%EXITCODE%"=="0" (echo [FAILED] exit code = %EXITCODE%) else (echo [DONE])
echo.
pause
exit /b %EXITCODE%
