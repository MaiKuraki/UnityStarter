@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
set "SCRIPT_DIR=%~dp0"
set "SCRIPT_DIR=!SCRIPT_DIR:~0,-1!"
set "CONFIG=!SCRIPT_DIR!\build_config.ini"
set "PAUSE_ON_EXIT=false"
set "PAUSE_REASON="

echo(!CMDCMDLINE! | findstr /I /C:" /c " >nul && (
    set "PAUSE_ON_EXIT=true"
    set "PAUSE_REASON=cmd /c launch detected, usually a Windows double-click"
)
if defined CI (
    set "PAUSE_ON_EXIT=false"
    set "PAUSE_REASON=CI environment variable disables pause"
)
if defined CYCLONE_DATATABLE_NO_PAUSE (
    set "PAUSE_ON_EXIT=false"
    set "PAUSE_REASON=CYCLONE_DATATABLE_NO_PAUSE disables pause"
)
if defined CYCLONE_DATATABLE_PAUSE (
    set "PAUSE_ON_EXIT=true"
    set "PAUSE_REASON=CYCLONE_DATATABLE_PAUSE enables pause"
)

for %%a in (%*) do (
    if /i "%%~a"=="--pause" (
        set "PAUSE_ON_EXIT=true"
        set "PAUSE_REASON=--pause argument enables pause"
    )
    if /i "%%~a"=="/pause" (
        set "PAUSE_ON_EXIT=true"
        set "PAUSE_REASON=/pause argument enables pause"
    )
    if /i "%%~a"=="--no-pause" (
        set "PAUSE_ON_EXIT=false"
        set "PAUSE_REASON=--no-pause argument disables pause"
    )
    if /i "%%~a"=="/no-pause" (
        set "PAUSE_ON_EXIT=false"
        set "PAUSE_REASON=/no-pause argument disables pause"
    )
)

if not exist "!CONFIG!" (
    echo [ERROR] Config file not found: !CONFIG!
    set "EXIT_CODE=1"
    goto :finish
)

REM ============================================================
REM  Parse build_config.ini
REM ============================================================
for /f "usebackq eol=# tokens=1,* delims==" %%a in ("!CONFIG!") do (
    set "k=%%a"
    if "!k:~0,1!" neq "[" if not "!k!"=="" if "!k:~0,1!" neq ";" set "_!k!=%%b"
)

REM ============================================================
REM  Parse CLI arguments (override INI values)
REM ============================================================
set "ARG_TARGET="
set "ARG_CODE="
set "ARG_DATA="
set "ARG_PAUSE="

:parse
if "%~1"=="" goto :parsed
if /i "%~1"=="-t" (set "ARG_TARGET=%~2" & shift & shift & goto :parse)
if /i "%~1"=="-c" (set "ARG_CODE=%~2"   & shift & shift & goto :parse)
if /i "%~1"=="-d" (set "ARG_DATA=%~2"   & shift & shift & goto :parse)
if /i "%~1"=="--pause" (set "ARG_PAUSE=true" & shift & goto :parse)
if /i "%~1"=="/pause" (set "ARG_PAUSE=true" & shift & goto :parse)
if /i "%~1"=="--no-pause" (set "ARG_PAUSE=false" & shift & goto :parse)
if /i "%~1"=="/no-pause" (set "ARG_PAUSE=false" & shift & goto :parse)
if /i "%~1"=="-h" goto :help
if /i "%~1"=="--help" goto :help
echo [WARN] Unknown option: %~1
shift
goto :parse
:parsed

if not "!ARG_TARGET!"=="" set "_target=!ARG_TARGET!"
if not "!ARG_CODE!"==""   set "_code_target=!ARG_CODE!"
if not "!ARG_DATA!"==""   set "_data_target=!ARG_DATA!"
if /i "!ARG_PAUSE!"=="true" (
    set "PAUSE_ON_EXIT=true"
    set "PAUSE_REASON=command-line pause option enables pause"
)
if /i "!ARG_PAUSE!"=="false" (
    set "PAUSE_ON_EXIT=false"
    set "PAUSE_REASON=command-line no-pause option disables pause"
)

set "TARGET=!_target!"
if "!TARGET!"=="" set "TARGET=client"
set "CODE_TARGET=!_code_target!"
if "!CODE_TARGET!"=="" set "CODE_TARGET=cs-bin"
set "DATA_TARGET=!_data_target!"
if "!DATA_TARGET!"=="" set "DATA_TARGET=bin"
set "LINE_ENDING=!_line_ending!"
if "!LINE_ENDING!"=="" set "LINE_ENDING=crlf"
set "CLEAN_OUTPUT=!_clean_output!"
if "!CLEAN_OUTPUT!"=="" set "CLEAN_OUTPUT=true"
set "CLEAN_OUTPUT_ENABLED=true"
if /i "!CLEAN_OUTPUT!"=="false" set "CLEAN_OUTPUT_ENABLED=false"
if "!CLEAN_OUTPUT!"=="0" set "CLEAN_OUTPUT_ENABLED=false"
set "CLEAN_ORPHAN_META=!_clean_orphan_meta!"
if "!CLEAN_ORPHAN_META!"=="" set "CLEAN_ORPHAN_META=true"
set "CLEAN_ORPHAN_META_ENABLED=true"
if /i "!CLEAN_ORPHAN_META!"=="false" set "CLEAN_ORPHAN_META_ENABLED=false"
if "!CLEAN_ORPHAN_META!"=="0" set "CLEAN_ORPHAN_META_ENABLED=false"
set "CODE_OUT_CONFIG="
set "DATA_OUT_CONFIG="

if /i "!TARGET!"=="client" (
    set "CODE_OUT_CONFIG=!_client_code_out!"
    set "DATA_OUT_CONFIG=!_client_data_out!"
) else if /i "!TARGET!"=="server" (
    set "CODE_OUT_CONFIG=!_server_code_out!"
    set "DATA_OUT_CONFIG=!_server_data_out!"
) else if /i "!TARGET!"=="all" (
    set "CODE_OUT_CONFIG=!_all_code_out!"
    set "DATA_OUT_CONFIG=!_all_data_out!"
) else (
    echo [ERROR] Unknown target: !TARGET!
    echo [ERROR] Expected one of: client, server, all
    set "EXIT_CODE=1"
    goto :finish
)

if "!CODE_OUT_CONFIG!"=="" (
    echo [ERROR] Missing code output path for target: !TARGET!
    set "EXIT_CODE=1"
    goto :finish
)

if "!DATA_OUT_CONFIG!"=="" (
    echo [ERROR] Missing data output path for target: !TARGET!
    set "EXIT_CODE=1"
    goto :finish
)

REM ============================================================
REM  Resolve paths
REM ============================================================
for %%i in ("!SCRIPT_DIR!\!_luban_dll!")            do set "LUBAN_DLL=%%~fi"
for %%i in ("!SCRIPT_DIR!\!CODE_OUT_CONFIG!")       do set "CODE_OUT=%%~fi"
for %%i in ("!SCRIPT_DIR!\!DATA_OUT_CONFIG!")       do set "DATA_OUT=%%~fi"
for %%i in ("!SCRIPT_DIR!\!_custom_template_dir!")  do set "CUSTOM_DIR=%%~fi"

REM ============================================================
REM  Validate
REM ============================================================
set "LUBAN_EXE=!LUBAN_DLL:.dll=.exe!"
if exist "!LUBAN_EXE!" (
    set "LUBAN_RUNNER=!LUBAN_EXE!"
    set "LUBAN_USE_DOTNET=false"
) else if exist "!LUBAN_DLL!" (
    set "LUBAN_RUNNER=dotnet"
    set "LUBAN_USE_DOTNET=true"
) else (
    echo [ERROR] Neither Luban.exe nor Luban.dll found at: !LUBAN_DLL!
    set "EXIT_CODE=1"
    goto :finish
)

mkdir "!CODE_OUT!" 2>nul
mkdir "!DATA_OUT!" 2>nul
set "CODE_OUT_ARG=!CODE_OUT!"
if "!CODE_OUT_ARG:~-1!"=="\" set "CODE_OUT_ARG=!CODE_OUT_ARG!."
set "DATA_OUT_ARG=!DATA_OUT!"
if "!DATA_OUT_ARG:~-1!"=="\" set "DATA_OUT_ARG=!DATA_OUT_ARG!."

REM ============================================================
REM  Copy bridge files
REM ============================================================
if not "!_custom_template_dir!"=="" if exist "!CUSTOM_DIR!" (
    if not "!_bridge_files!"=="" (
        for %%f in (!_bridge_files!) do (
            if exist "!CUSTOM_DIR!\%%f" (
                echo [Luban] Copying bridge: %%f
                copy /y "!CUSTOM_DIR!\%%f" "!CODE_OUT!\" >nul
            )
        )
    )
)

REM ============================================================
REM  Build Luban arguments
REM ============================================================
set "LUBAN_ARGS=-t !TARGET! -c !CODE_TARGET! -d !DATA_TARGET! --conf !SCRIPT_DIR!\luban.conf -x lineEnding=!LINE_ENDING! -x outputCodeDir=!CODE_OUT_ARG! -x outputDataDir=!DATA_OUT_ARG!"
set "LUBAN_ARGS=!LUBAN_ARGS! -x outputSaver.!CODE_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED! -x outputSaver.!DATA_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!"
set "HAS_CUSTOM_TEMPLATE=false"

if not "!_custom_template_dir!"=="" if exist "!CUSTOM_DIR!" (
    set "HAS_CUSTOM_TEMPLATE=true"
    set "LUBAN_ARGS=!LUBAN_ARGS! --customTemplateDir !CUSTOM_DIR!"
)

REM ============================================================
REM  Run
REM ============================================================
echo [Luban] Target     : !TARGET!
echo [Luban] Code target: !CODE_TARGET!
echo [Luban] Data target: !DATA_TARGET!
echo [Luban] Code output: !CODE_OUT!
echo [Luban] Data output: !DATA_OUT!
echo [Luban] Clean output: !CLEAN_OUTPUT_ENABLED!
echo [Luban] Clean orphan meta: !CLEAN_ORPHAN_META_ENABLED!
echo.
if /i "!LUBAN_USE_DOTNET!"=="true" (
    echo [Luban] Running: dotnet "!LUBAN_DLL!" !LUBAN_ARGS!
) else (
    echo [Luban] Running: "!LUBAN_RUNNER!" !LUBAN_ARGS!
)
echo.

if /i "!LUBAN_USE_DOTNET!"=="true" (
    if /i "!HAS_CUSTOM_TEMPLATE!"=="true" (
        dotnet "!LUBAN_DLL!" -t "!TARGET!" -c "!CODE_TARGET!" -d "!DATA_TARGET!" --conf "!SCRIPT_DIR!\luban.conf" -x "lineEnding=!LINE_ENDING!" -x "outputCodeDir=!CODE_OUT_ARG!" -x "outputDataDir=!DATA_OUT_ARG!" -x "outputSaver.!CODE_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!" -x "outputSaver.!DATA_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!" --customTemplateDir "!CUSTOM_DIR!"
    ) else (
        dotnet "!LUBAN_DLL!" -t "!TARGET!" -c "!CODE_TARGET!" -d "!DATA_TARGET!" --conf "!SCRIPT_DIR!\luban.conf" -x "lineEnding=!LINE_ENDING!" -x "outputCodeDir=!CODE_OUT_ARG!" -x "outputDataDir=!DATA_OUT_ARG!" -x "outputSaver.!CODE_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!" -x "outputSaver.!DATA_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!"
    )
) else (
    if /i "!HAS_CUSTOM_TEMPLATE!"=="true" (
        "!LUBAN_RUNNER!" -t "!TARGET!" -c "!CODE_TARGET!" -d "!DATA_TARGET!" --conf "!SCRIPT_DIR!\luban.conf" -x "lineEnding=!LINE_ENDING!" -x "outputCodeDir=!CODE_OUT_ARG!" -x "outputDataDir=!DATA_OUT_ARG!" -x "outputSaver.!CODE_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!" -x "outputSaver.!DATA_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!" --customTemplateDir "!CUSTOM_DIR!"
    ) else (
        "!LUBAN_RUNNER!" -t "!TARGET!" -c "!CODE_TARGET!" -d "!DATA_TARGET!" --conf "!SCRIPT_DIR!\luban.conf" -x "lineEnding=!LINE_ENDING!" -x "outputCodeDir=!CODE_OUT_ARG!" -x "outputDataDir=!DATA_OUT_ARG!" -x "outputSaver.!CODE_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!" -x "outputSaver.!DATA_TARGET!.cleanUpOutputDir=!CLEAN_OUTPUT_ENABLED!"
    )
)

if !ERRORLEVEL! neq 0 (
    echo [Luban] Build FAILED: exit code !ERRORLEVEL!
    set "EXIT_CODE=!ERRORLEVEL!"
    goto :finish
)

if not "!_string_constant_tables!"=="" (
    if "!_codegen_project!"=="" (
        echo [ERROR] string_constant_tables is configured but codegen_project is empty.
        set "EXIT_CODE=1"
        goto :finish
    )

    for %%i in ("!SCRIPT_DIR!\!_codegen_project!") do set "CODEGEN_PROJECT=%%~fi"
    if not exist "!CODEGEN_PROJECT!" (
        echo [ERROR] DataTable code generator project not found: !CODEGEN_PROJECT!
        set "EXIT_CODE=1"
        goto :finish
    )

    where dotnet >nul 2>nul
    if !ERRORLEVEL! neq 0 (
        echo [ERROR] dotnet SDK is required to run DataTable string constant generation.
        set "EXIT_CODE=1"
        goto :finish
    )

    set "CODEGEN_CODE_OUT=!CODE_OUT!"
    if "!CODEGEN_CODE_OUT:~-1!"=="\" set "CODEGEN_CODE_OUT=!CODEGEN_CODE_OUT!."
    echo [DataTable.CodeGen] Generating string constants
    dotnet run --project "!CODEGEN_PROJECT!" -- --config "!CONFIG!" --luban-conf "!SCRIPT_DIR!\luban.conf" --data-dir "!SCRIPT_DIR!\Datas" --target "!TARGET!" --code-output "!CODEGEN_CODE_OUT!" --line-ending "!LINE_ENDING!"

    if !ERRORLEVEL! neq 0 (
        echo [DataTable.CodeGen] Build FAILED: exit code !ERRORLEVEL!
        set "EXIT_CODE=!ERRORLEVEL!"
        goto :finish
    )
)

if /i "!CLEAN_ORPHAN_META_ENABLED!"=="true" (
    call :clean_orphan_meta "!CODE_OUT!"
    call :clean_orphan_meta "!DATA_OUT!"
)

echo [Luban] Build SUCCESS
set "EXIT_CODE=0"
goto :finish

:clean_orphan_meta
set "CLEAN_ROOT=%~1"
if "!CLEAN_ROOT!"=="" exit /b 0
if not exist "!CLEAN_ROOT!\" exit /b 0

for %%r in ("!CLEAN_ROOT!") do set "CLEAN_ROOT=%%~fr"
if "!CLEAN_ROOT:~1,2!"==":\" if "!CLEAN_ROOT:~3!"=="" (
    echo [WARN] Refuse to clean orphan meta in drive root: !CLEAN_ROOT!
    exit /b 0
)

for /r "!CLEAN_ROOT!" %%m in (*.meta) do (
    set "META_FILE=%%~fm"
    set "PAIR_PATH=%%~dpnm"
    if not exist "!PAIR_PATH!" (
        echo [Luban] Removing orphan meta: !META_FILE!
        del /f /q "!META_FILE!" >nul
    )
)
exit /b 0

:help
echo Usage: gen_code_bin_to_project_lazyload.bat [options]
echo.
echo Options (override build_config.ini):
echo   -t  Target      (client/server/all)
echo   -c  Code target (cs-bin etc.)
echo   -d  Data target (bin etc.)
echo   --pause      Keep the window open after finishing
echo   --no-pause   Exit immediately after finishing
echo   -h  Show this help
set "EXIT_CODE=0"
goto :finish

:finish
if /i "!PAUSE_ON_EXIT!"=="true" (
    echo.
    echo [Luban] Pause reason: !PAUSE_REASON!
    if "!EXIT_CODE!"=="0" (
        echo [Luban] Done. Press any key to close this window.
    ) else (
        echo [Luban] Failed. Press any key to close this window.
    )
    pause >nul
)
exit /b !EXIT_CODE!
