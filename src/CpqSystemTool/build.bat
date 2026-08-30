@echo off
setlocal
chcp 65001 >nul 2>&1
cd /d "%~dp0"

echo [1/3] Building 系统清理与优化工具 (Release)...
set "PROJ=CpqSystemTool.csproj"

REM --- try dotnet build (SDK present) ---
where dotnet >nul 2>&1
if %errorlevel%==0 (
    dotnet build %PROJ% -c Release
    if %errorlevel%==0 goto :deploy
)

REM --- fallback: MSBuild via vswhere ---
echo [1/3] dotnet build failed, trying MSBuild...
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "MSB="
if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\Current\Bin\MSBuild.exe`) do set "MSB=%%i"
)
if defined MSB (
    "%MSB%" %PROJ% /p:Configuration=Release
    if %errorlevel%==0 goto :deploy
)

echo [!] Build failed. Please open the folder in Visual Studio and Build ^> Rebuild, or check the errors above.
pause
exit /b 1

:deploy
echo [2/3] Build OK. Locating output exe...
set "OUT=%~dp0bin\Release\net48\系统清理与优化工具.exe"
if not exist "%OUT%" set "OUT=%~dp0bin\Release\系统清理与优化工具.exe"
if not exist "%OUT%" set "OUT=%~dp0bin\Debug\net48\系统清理与优化工具.exe"
if not exist "%OUT%" set "OUT=%~dp0bin\Debug\系统清理与优化工具.exe"
if not exist "%OUT%" (
    echo [!] Cannot find built exe under: %~dp0bin\
    pause
    exit /b 1
)

REM --- 从 csproj 自动读取 AssemblyVersion（如 1.0.16.0 → v1.16）---
set "VER=v0.00"
for /f "tokens=*" %%v in ('findstr /i "<FileVersion>" "%~dp0CpqSystemTool.csproj"') do (
    for /f "tokens=2 delims=<>" %%a in ("%%v") do set "FV=%%a"
)
if defined FV (
    for /f "tokens=1,2 delims=." %%m in ("%FV%") do (
        set "MAJOR=%%m"
        set "MINOR=%%n"
    )
    REM 补零：确保 MINOR 是两位（如 1.6 → 06，1.16 → 16）
    if "%MINOR:~1,1%"=="" set "MINOR=0%MINOR%"
    set "VER=v%MAJOR%.%MINOR%"
)
echo [3/3] Deploying to %~dp0..\系统清理与优化工具_%VER%.exe ...
copy /Y "%OUT%" "%~dp0..\系统清理与优化工具_%VER%.exe" >nul
if %errorlevel%==0 (
    echo Done. New exe deployed: 系统清理与优化工具_%VER%.exe
) else (
    echo [!] Copy failed (file in use?). Close the running exe first, then re-run this script.
)
pause
