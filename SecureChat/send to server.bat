@echo off
setlocal enabledelayedexpansion

REM Чтение secrets.env
if exist secrets.env (
    for /f "tokens=1,2 delims==" %%a in (secrets.env) do (
        set "%%a=%%b"
    )
) else (
    echo [ERROR] secrets.env not found!
    pause
    exit /b
)

echo === Configuration ===
echo User:     [%SSH_USER%]
echo Host:     [%SSH_HOST%]
echo Path:     [%REMOTE_PATH%]
echo Password: [********]
echo =====================

REM Проверка на пустоту
if "%SSH_HOST%"=="" (
    echo [ERROR] Variables not loaded. Check secrets.env format.
    pause
    exit /b
)

echo Uploading to server...
REM -batch подавляет интерактивные запросы, -pw передает пароль
pscp -pw "%SSH_PASS%" -batch latest.7z %SSH_USER%@%SSH_HOST%:%REMOTE_PATH%/Storage
pscp -pw "%SSH_PASS%" -batch version.txt %SSH_USER%@%SSH_HOST%:%REMOTE_PATH%

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] File sent!
) else (
    echo [ERROR] PSCP failed with code %ERRORLEVEL%
)

pause