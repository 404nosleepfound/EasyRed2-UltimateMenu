@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ============================================================
echo  Easy Red 2 Ultimate Menu - Community Maintenance Builder
echo ============================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 goto no_dotnet

echo Detected .NET SDK:
dotnet --version
echo.

if defined ER2_GAME_DIR goto validate

set "DEFAULT_ER2=C:\Program Files (x86)\Steam\steamapps\common\Easy Red 2"
if not exist "%DEFAULT_ER2%\BepInEx\interop\Assembly-CSharp.dll" goto ask_path
set "ER2_GAME_DIR=%DEFAULT_ER2%"
echo Found Easy Red 2 at:
echo   %ER2_GAME_DIR%
echo.
goto validate

:ask_path
set /p "ER2_GAME_DIR=Paste your Easy Red 2 installation folder path: "

:validate
if exist "%ER2_GAME_DIR%\BepInEx\interop\Assembly-CSharp.dll" goto check_core
echo.
echo [ERROR] Could not find:
echo   %ER2_GAME_DIR%\BepInEx\interop\Assembly-CSharp.dll
echo.
echo In Steam: Easy Red 2 ^> Manage ^> Browse local files
echo Then copy the folder path and run this builder again.
pause
exit /b 2

:check_core
if exist "%ER2_GAME_DIR%\BepInEx\core\BepInEx.Unity.IL2CPP.dll" goto build
echo.
echo [ERROR] BepInEx IL2CPP core files were not found in the selected game folder.
pause
exit /b 3

:build
echo Building against:
echo   %ER2_GAME_DIR%
echo.

if exist "%~dp0build_output" rmdir /s /q "%~dp0build_output"
mkdir "%~dp0build_output"

dotnet build "%~dp0DebugMenu\ER2_UltimateMenu.csproj" -c Release /p:ER2GameDir="%ER2_GAME_DIR%" > "%~dp0build_output\build.log" 2>&1
set "BUILD_RESULT=%ERRORLEVEL%"

type "%~dp0build_output\build.log"
echo.

if "%BUILD_RESULT%"=="0" goto build_success

echo ============================================================
echo  BUILD FAILED
echo ============================================================
echo Upload build_output\build.log so the compile errors can be fixed.
pause
exit /b %BUILD_RESULT%

:build_success
set "DLL=%~dp0DebugMenu\bin\Release\net6.0\ER2_UltimateMenu.dll"
if exist "%DLL%" goto copy_dll
echo [ERROR] Build reported success but ER2_UltimateMenu.dll was not found.
pause
exit /b 4

:copy_dll
copy /y "%DLL%" "%~dp0build_output\ER2_UltimateMenu.dll" >nul

echo ============================================================
echo  BUILD SUCCESSFUL
echo  DLL: build_output\ER2_UltimateMenu.dll
echo  Log: build_output\build.log
echo ============================================================
echo.
echo Do NOT publish this build yet. Test it locally first.
pause
exit /b 0

:no_dotnet
echo [ERROR] The .NET SDK is not installed or dotnet is not on PATH.
echo.
echo Install the current .NET SDK, then run this file again.
echo Microsoft WinGet command:
echo   winget install Microsoft.DotNet.SDK.10
echo.
pause
exit /b 1
