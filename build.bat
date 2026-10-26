@echo off
echo [%DATE% %TIME%] Starting Rust project build...

:: Собираем проект в режиме Release (максимальная оптимизация)
cargo build --release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed! Check errors above.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==========================================
echo [SUCCESS] Build completed successfully!
echo Executable is located in: target\release\
echo ==========================================
pause
