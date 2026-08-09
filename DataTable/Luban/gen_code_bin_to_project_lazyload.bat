@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "TOOL_DIR=%SCRIPT_DIR%..\..\UnityStarter\Assets\ThirdParty\CycloneGames\CycloneGames.DataTable\Tools~\CodeGen"
set "TOOL_PROJECT=%TOOL_DIR%\CycloneGames.DataTable.CodeGen.csproj"
set "BUILD_CONFIG=%SCRIPT_DIR%build_config.ini"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] The pinned .NET SDK is required to run the DataTable pipeline.
    exit /b 1
)

if not exist "%TOOL_PROJECT%" (
    echo [ERROR] DataTable pipeline project not found: %TOOL_PROJECT%
    exit /b 1
)

pushd "%TOOL_DIR%"
dotnet run --project "%TOOL_PROJECT%" --configuration Release -- pipeline %* --config "%BUILD_CONFIG%"
set "EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %EXIT_CODE%
