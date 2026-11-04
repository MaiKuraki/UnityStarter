@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "BUILD_CONFIG=%SCRIPT_DIR%build_config.ini"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK 8.0 or newer is required to run the DataTable pipeline. 1>&2
    exit /b 1
)

if not exist "%BUILD_CONFIG%" (
    echo [ERROR] Build configuration not found: %BUILD_CONFIG% 1>&2
    exit /b 1
)

REM Resolve relative paths against this script, not the caller's working directory.
pushd "%SCRIPT_DIR%" >nul

REM Read codegen_project from the configuration so the path has a single owner.
set "TOOL_PROJECT="
for /f "usebackq tokens=1,* delims==" %%a in ("%BUILD_CONFIG%") do (
    if /i "%%a"=="codegen_project" set "TOOL_PROJECT=%%b"
)

if not defined TOOL_PROJECT (
    echo [ERROR] codegen_project is missing from %BUILD_CONFIG% 1>&2
    popd >nul
    exit /b 1
)

for %%I in ("%TOOL_PROJECT%") do set "TOOL_PROJECT=%%~fI"
if not exist "%TOOL_PROJECT%" (
    echo [ERROR] DataTable pipeline project not found: %TOOL_PROJECT% 1>&2
    popd >nul
    exit /b 1
)

REM The SDK policy lives in the CodeGen folder, so run dotnet from there.
for %%I in ("%TOOL_PROJECT%") do set "TOOL_DIR=%%~dpI"

pushd "%TOOL_DIR%" >nul
dotnet run --project "%TOOL_PROJECT%" --configuration Release --verbosity quiet -- pipeline %* --config "%BUILD_CONFIG%"
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul

popd >nul
exit /b %EXIT_CODE%
