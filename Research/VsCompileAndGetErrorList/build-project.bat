@echo off
echo Building VsCompileAndGetErrorList project...
echo.

REM Setup Visual Studio environment
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"

if %errorlevel% neq 0 (
    echo Error: Failed to setup Visual Studio environment
    pause
    exit /b 1
)

echo Visual Studio environment setup complete.
echo Building project with MSBuild...
echo.

REM Build the project with warnings level
msbuild VsCompileAndGetErrorList.sln /p:Configuration=Debug /p:Platform="Any CPU" /v:minimal /flp:verbosity=normal;logfile=build.log

if %errorlevel% equ 0 (
    echo.
    echo Build completed successfully!
) else (
    echo.
    echo Build failed! Check build.log for details.
)

pause