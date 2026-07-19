@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
set "SCRIPT_DIR=%CD%"
set "CONFIG=!SCRIPT_DIR!\build_config.ini"
set "PAUSE_ON_EXIT=false"
set "PAUSE_REASON="
set "EXIT_CODE=1"
set "WRITER_LOCK_DIR=!SCRIPT_DIR!\.cyclonegames-datatable-writer.lock"
set "WRITER_LOCK_OWNER=!WRITER_LOCK_DIR!\owner.txt"
set "WRITER_LOCK_HELD=false"
set "WRITER_LOCK_TOKEN="

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
set "VALIDATE_ONLY=false"
set "SEEN_TARGET=false"
set "SEEN_CODE_TARGET=false"
set "SEEN_DATA_TARGET=false"
set "SEEN_VALIDATE_ONLY=false"

:parse
if "%~1"=="" goto :parsed
if /i "%~1"=="-t" (
    if /i "!SEEN_TARGET!"=="true" (echo [ERROR] Duplicate option: -t & set "EXIT_CODE=1" & goto :finish)
    if "%~2"=="" (echo [ERROR] Option requires a value: -t & set "EXIT_CODE=1" & goto :finish)
    set "SEEN_TARGET=true"
    set "ARG_TARGET=%~2" & shift & shift & goto :parse
)
if /i "%~1"=="-c" (
    if /i "!SEEN_CODE_TARGET!"=="true" (echo [ERROR] Duplicate option: -c & set "EXIT_CODE=1" & goto :finish)
    if "%~2"=="" (echo [ERROR] Option requires a value: -c & set "EXIT_CODE=1" & goto :finish)
    set "SEEN_CODE_TARGET=true"
    set "ARG_CODE=%~2" & shift & shift & goto :parse
)
if /i "%~1"=="-d" (
    if /i "!SEEN_DATA_TARGET!"=="true" (echo [ERROR] Duplicate option: -d & set "EXIT_CODE=1" & goto :finish)
    if "%~2"=="" (echo [ERROR] Option requires a value: -d & set "EXIT_CODE=1" & goto :finish)
    set "SEEN_DATA_TARGET=true"
    set "ARG_DATA=%~2" & shift & shift & goto :parse
)
if /i "%~1"=="--validate-only" (
    if /i "!SEEN_VALIDATE_ONLY!"=="true" (echo [ERROR] Duplicate option: --validate-only & set "EXIT_CODE=1" & goto :finish)
    set "SEEN_VALIDATE_ONLY=true"
    set "VALIDATE_ONLY=true"
    shift
    goto :parse
)
if /i "%~1"=="--pause" (set "ARG_PAUSE=true" & shift & goto :parse)
if /i "%~1"=="/pause" (set "ARG_PAUSE=true" & shift & goto :parse)
if /i "%~1"=="--no-pause" (set "ARG_PAUSE=false" & shift & goto :parse)
if /i "%~1"=="/no-pause" (set "ARG_PAUSE=false" & shift & goto :parse)
if /i "%~1"=="-h" goto :help
if /i "%~1"=="--help" goto :help
echo [ERROR] Unknown option: %~1
echo [ERROR] Run with --help. Refusing to continue because this command can replace generated output.
set "EXIT_CODE=1"
goto :finish
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
if /i "!LINE_ENDING!"=="crlf" (
    set "LINE_ENDING=crlf"
) else if /i "!LINE_ENDING!"=="lf" (
    set "LINE_ENDING=lf"
) else (
    echo [ERROR] line_ending must be crlf or lf: !LINE_ENDING!
    set "EXIT_CODE=1"
    goto :finish
)
set "CLEAN_OUTPUT=!_clean_output!"
if "!CLEAN_OUTPUT!"=="" set "CLEAN_OUTPUT=false"
if /i "!CLEAN_OUTPUT!"=="true" (
    set "CLEAN_OUTPUT_ENABLED=true"
) else if "!CLEAN_OUTPUT!"=="1" (
    set "CLEAN_OUTPUT_ENABLED=true"
) else if /i "!CLEAN_OUTPUT!"=="false" (
    set "CLEAN_OUTPUT_ENABLED=false"
) else if "!CLEAN_OUTPUT!"=="0" (
    set "CLEAN_OUTPUT_ENABLED=false"
) else (
    echo [ERROR] clean_output must be true, false, 1, or 0: !CLEAN_OUTPUT!
    set "EXIT_CODE=1"
    goto :finish
)
set "CLEAN_ORPHAN_META=!_clean_orphan_meta!"
if "!CLEAN_ORPHAN_META!"=="" set "CLEAN_ORPHAN_META=false"
if /i "!CLEAN_ORPHAN_META!"=="true" (
    set "CLEAN_ORPHAN_META_ENABLED=true"
) else if "!CLEAN_ORPHAN_META!"=="1" (
    set "CLEAN_ORPHAN_META_ENABLED=true"
) else if /i "!CLEAN_ORPHAN_META!"=="false" (
    set "CLEAN_ORPHAN_META_ENABLED=false"
) else if "!CLEAN_ORPHAN_META!"=="0" (
    set "CLEAN_ORPHAN_META_ENABLED=false"
) else (
    echo [ERROR] clean_orphan_meta must be true, false, 1, or 0: !CLEAN_ORPHAN_META!
    set "EXIT_CODE=1"
    goto :finish
)
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

call :acquire_writer_lock
if !ERRORLEVEL! neq 0 (
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
for %%i in ("!SCRIPT_DIR!\..\..")                    do set "REPO_ROOT=%%~fi"
for %%i in ("!REPO_ROOT!\UnityStarter\Assets")       do set "UNITY_ASSETS_ROOT=%%~fi"
for %%i in ("!SCRIPT_DIR!\Generated")                 do set "GENERATED_ROOT=%%~fi"
for %%i in ("!REPO_ROOT!\UnityStarter\Assets\UnityStarter\Scripts\Generated\DataTable") do set "UNITY_CLIENT_CODE_ROOT=%%~fi"
for %%i in ("!REPO_ROOT!\UnityStarter\Assets\StreamingAssets\DataTable") do set "UNITY_CLIENT_DATA_ROOT=%%~fi"

REM %%~f preserves a trailing backslash from configuration values. Normalize
REM non-root paths before equality and containment checks so an approved root
REM is treated identically with or without that separator.
if "!CODE_OUT:~-1!"=="\" set "CODE_OUT=!CODE_OUT:~0,-1!"
if "!DATA_OUT:~-1!"=="\" set "DATA_OUT=!DATA_OUT:~0,-1!"
if "!GENERATED_ROOT:~-1!"=="\" set "GENERATED_ROOT=!GENERATED_ROOT:~0,-1!"
if "!UNITY_CLIENT_CODE_ROOT:~-1!"=="\" set "UNITY_CLIENT_CODE_ROOT=!UNITY_CLIENT_CODE_ROOT:~0,-1!"
if "!UNITY_CLIENT_DATA_ROOT:~-1!"=="\" set "UNITY_CLIENT_DATA_ROOT=!UNITY_CLIENT_DATA_ROOT:~0,-1!"

REM ============================================================
REM  Validate
REM ============================================================
echo(!CODE_TARGET!| findstr /R /X "[A-Za-z0-9._-][A-Za-z0-9._-]*" >nul || (
    echo [ERROR] Invalid code target: !CODE_TARGET!
    set "EXIT_CODE=1"
    goto :finish
)
echo(!DATA_TARGET!| findstr /R /X "[A-Za-z0-9._-][A-Za-z0-9._-]*" >nul || (
    echo [ERROR] Invalid data target: !DATA_TARGET!
    set "EXIT_CODE=1"
    goto :finish
)

call :validate_output_root "!CODE_OUT!" "code output"
if !ERRORLEVEL! neq 0 (
    set "EXIT_CODE=1"
    goto :finish
)
call :validate_output_root "!DATA_OUT!" "data output"
if !ERRORLEVEL! neq 0 (
    set "EXIT_CODE=1"
    goto :finish
)
call :paths_overlap "!CODE_OUT!" "!DATA_OUT!"
if !ERRORLEVEL! equ 0 (
    echo [ERROR] Code and data output roots must not contain one another.
    set "EXIT_CODE=1"
    goto :finish
)

call :reject_reparse_chain "!CODE_OUT!" "!REPO_ROOT!"
if !ERRORLEVEL! neq 0 (
    set "EXIT_CODE=1"
    goto :finish
)
call :reject_reparse_chain "!DATA_OUT!" "!REPO_ROOT!"
if !ERRORLEVEL! neq 0 (
    set "EXIT_CODE=1"
    goto :finish
)

if /i "!CLEAN_OUTPUT_ENABLED!"=="true" if not "!CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN!"=="1" (
    echo [ERROR] clean_output=true requires CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN=1.
    echo [ERROR] Keep clean_output=false for normal generation; perform destructive replacement only after an explicit backup/recovery decision.
    set "EXIT_CODE=1"
    goto :finish
)

if /i "!CLEAN_ORPHAN_META_ENABLED!"=="true" if not "!CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN!"=="1" (
    echo [ERROR] clean_orphan_meta=true requires CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN=1.
    set "EXIT_CODE=1"
    goto :finish
)

if not exist "!SCRIPT_DIR!\luban.conf" (
    echo [ERROR] Luban configuration not found: !SCRIPT_DIR!\luban.conf
    set "EXIT_CODE=1"
    goto :finish
)
if not exist "!SCRIPT_DIR!\DataTableBuildSafety.ps1" (
    echo [ERROR] Windows safety helper not found: !SCRIPT_DIR!\DataTableBuildSafety.ps1
    set "EXIT_CODE=1"
    goto :finish
)
if not "!_custom_template_dir!"=="" (
    powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "!SCRIPT_DIR!\DataTableBuildSafety.ps1" -ValidateTemplateRoot -RepoRoot "!REPO_ROOT!" -CustomTemplateRoot "!CUSTOM_DIR!"
    if !ERRORLEVEL! neq 0 (
        set "EXIT_CODE=1"
        goto :finish
    )
)

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

if /i "!LUBAN_USE_DOTNET!"=="true" (
    where dotnet >nul 2>nul
    if !ERRORLEVEL! neq 0 (
        echo [ERROR] dotnet is required to run Luban.dll.
        set "EXIT_CODE=1"
        goto :finish
    )
)

for %%w in (__tables__.xlsx __beans__.xlsx __enums__.xlsx) do (
    if not exist "!SCRIPT_DIR!\Datas\%%w" (
        echo [ERROR] Required Luban schema workbook not found: !SCRIPT_DIR!\Datas\%%w
        set "EXIT_CODE=1"
        goto :finish
    )
)

set "CODEGEN_MANIFEST=!CODE_OUT!\.cyclonegames-datatable-codegen-manifest.json"
set "CODEGEN_REQUIRED=false"
if not "!_string_constant_tables!"=="" set "CODEGEN_REQUIRED=true"
if exist "!CODEGEN_MANIFEST!" set "CODEGEN_REQUIRED=true"

if /i "!CODEGEN_REQUIRED!"=="true" (
    if "!_codegen_project!"=="" (
        echo [ERROR] CodeGen output is configured or previously owned, but codegen_project is empty.
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
)

call :process_bridge_files false
if !ERRORLEVEL! neq 0 (
    set "EXIT_CODE=1"
    goto :finish
)

if /i "!VALIDATE_ONLY!"=="true" (
    if /i "!CODEGEN_REQUIRED!"=="true" (
        set "CODEGEN_CODE_OUT=!CODE_OUT!"
        if "!CODEGEN_CODE_OUT:~-1!"=="\" set "CODEGEN_CODE_OUT=!CODEGEN_CODE_OUT!."
        echo [DataTable.CodeGen] Validating owned string constant outputs
        dotnet run --project "!CODEGEN_PROJECT!" -- --config "!CONFIG!" --luban-conf "!SCRIPT_DIR!\luban.conf" --data-dir "!SCRIPT_DIR!\Datas" --target "!TARGET!" --code-output "!CODEGEN_CODE_OUT!" --line-ending "!LINE_ENDING!" --validate-only
        if !ERRORLEVEL! neq 0 (
            set "EXIT_CODE=!ERRORLEVEL!"
            goto :finish
        )
    )

    echo [Luban] Validation successful. No directories, generated files, or metadata were changed.
    echo [Luban] Code output: !CODE_OUT!
    echo [Luban] Data output: !DATA_OUT!
    set "EXIT_CODE=0"
    goto :finish
)

mkdir "!CODE_OUT!" 2>nul
mkdir "!DATA_OUT!" 2>nul
set "CODE_OUT_ARG=!CODE_OUT!"
if "!CODE_OUT_ARG:~-1!"=="\" set "CODE_OUT_ARG=!CODE_OUT_ARG!."
set "DATA_OUT_ARG=!DATA_OUT!"
if "!DATA_OUT_ARG:~-1!"=="\" set "DATA_OUT_ARG=!DATA_OUT_ARG!."

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

if /i "!CODEGEN_REQUIRED!"=="true" (
    if "!_codegen_project!"=="" (
        echo [ERROR] CodeGen output is configured or previously owned, but codegen_project is empty.
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
    echo [DataTable.CodeGen] Reconciling owned string constant outputs
    dotnet run --project "!CODEGEN_PROJECT!" -- --config "!CONFIG!" --luban-conf "!SCRIPT_DIR!\luban.conf" --data-dir "!SCRIPT_DIR!\Datas" --target "!TARGET!" --code-output "!CODEGEN_CODE_OUT!" --line-ending "!LINE_ENDING!"

    if !ERRORLEVEL! neq 0 (
        echo [DataTable.CodeGen] Build FAILED: exit code !ERRORLEVEL!
        set "EXIT_CODE=!ERRORLEVEL!"
        goto :finish
    )
)

REM Copy validated companion files only after Luban and CodeGen succeed.
REM Luban cleanUpOutputDir may otherwise remove an early bridge copy.
call :process_bridge_files true
if !ERRORLEVEL! neq 0 (
    set "EXIT_CODE=1"
    goto :finish
)

if /i "!CLEAN_ORPHAN_META_ENABLED!"=="true" (
    call :clean_orphan_meta "!CODE_OUT!"
    if !ERRORLEVEL! neq 0 (
        set "EXIT_CODE=1"
        goto :finish
    )
    call :clean_orphan_meta "!DATA_OUT!"
    if !ERRORLEVEL! neq 0 (
        set "EXIT_CODE=1"
        goto :finish
    )
)

echo [Luban] Build SUCCESS
set "EXIT_CODE=0"
goto :finish

:process_bridge_files
set "BRIDGE_COPY_MODE=%~1"
if "!_bridge_files!"=="" exit /b 0
if "!_custom_template_dir!"=="" (
    echo [ERROR] bridge_files requires an existing custom_template_dir.
    exit /b 1
)
if not exist "!CUSTOM_DIR!\" (
    echo [ERROR] bridge_files requires an existing custom_template_dir: !CUSTOM_DIR!
    exit /b 1
)
if /i "!BRIDGE_COPY_MODE!"=="true" (
    powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "!SCRIPT_DIR!\DataTableBuildSafety.ps1" -CopyBridgeFiles -RepoRoot "!REPO_ROOT!" -CustomTemplateRoot "!CUSTOM_DIR!" -OutputRoot "!CODE_OUT!" -BridgeFiles "!_bridge_files!"
) else (
    powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "!SCRIPT_DIR!\DataTableBuildSafety.ps1" -ValidateBridgeFiles -RepoRoot "!REPO_ROOT!" -CustomTemplateRoot "!CUSTOM_DIR!" -OutputRoot "!CODE_OUT!" -BridgeFiles "!_bridge_files!"
)
if !ERRORLEVEL! neq 0 exit /b 1
exit /b 0

:clean_orphan_meta
set "CLEAN_ROOT=%~1"
if "!CLEAN_ROOT!"=="" exit /b 0
if not exist "!CLEAN_ROOT!\" exit /b 0

for %%r in ("!CLEAN_ROOT!") do set "CLEAN_ROOT=%%~fr"
call :validate_output_root "!CLEAN_ROOT!" "orphan-meta cleanup"
if !ERRORLEVEL! neq 0 exit /b 1
call :reject_reparse_chain "!CLEAN_ROOT!" "!REPO_ROOT!"
if !ERRORLEVEL! neq 0 exit /b 1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "!SCRIPT_DIR!\DataTableBuildSafety.ps1" -CleanOrphanMeta -RepoRoot "!REPO_ROOT!" -Root "!CLEAN_ROOT!"
if !ERRORLEVEL! neq 0 exit /b 1
exit /b 0

:acquire_writer_lock
2>nul mkdir "!WRITER_LOCK_DIR!"
if !ERRORLEVEL! neq 0 (
    echo [ERROR] DataTable generation workspace is locked: !WRITER_LOCK_DIR!
    if exist "!WRITER_LOCK_OWNER!" (
        echo [ERROR] Existing lock owner metadata:
        type "!WRITER_LOCK_OWNER!"
    )
    echo [ERROR] Refusing to continue. Do not remove the lock until the prior writer is confirmed stopped and its output is audited.
    exit /b 1
)

set "WRITER_LOCK_HELD=true"
set "WRITER_LOCK_TOKEN=!RANDOM!-!RANDOM!-!RANDOM!"
>"!WRITER_LOCK_OWNER!" (
    echo schema=CycloneGames.DataTable.WriterLock/1
    echo platform=Windows
    echo machine=!COMPUTERNAME!
    echo started=!DATE! !TIME!
    echo target=!TARGET!
    echo token=!WRITER_LOCK_TOKEN!
)
if !ERRORLEVEL! neq 0 (
    echo [ERROR] Failed to write DataTable writer-lock owner metadata.
    2>nul rmdir "!WRITER_LOCK_DIR!"
    set "WRITER_LOCK_HELD=false"
    exit /b 1
)
exit /b 0

:release_writer_lock
if /i not "!WRITER_LOCK_HELD!"=="true" exit /b 0
if not exist "!WRITER_LOCK_OWNER!" (
    echo [ERROR] DataTable writer-lock owner metadata disappeared; leaving the residual lock in place: !WRITER_LOCK_DIR!
    set "WRITER_LOCK_HELD=false"
    exit /b 1
)
findstr /L /X /C:"token=!WRITER_LOCK_TOKEN!" "!WRITER_LOCK_OWNER!" >nul 2>nul
if !ERRORLEVEL! neq 0 (
    echo [ERROR] DataTable writer-lock ownership changed; leaving the residual lock in place: !WRITER_LOCK_DIR!
    set "WRITER_LOCK_HELD=false"
    exit /b 1
)
del /q "!WRITER_LOCK_OWNER!" >nul 2>nul
if exist "!WRITER_LOCK_OWNER!" (
    echo [ERROR] Failed to remove DataTable writer-lock owner metadata; leaving the lock in place: !WRITER_LOCK_DIR!
    set "WRITER_LOCK_HELD=false"
    exit /b 1
)
2>nul rmdir "!WRITER_LOCK_DIR!"
if exist "!WRITER_LOCK_DIR!" (
    echo [ERROR] Failed to release DataTable writer lock; the residual lock will block future writers: !WRITER_LOCK_DIR!
    set "WRITER_LOCK_HELD=false"
    exit /b 1
)
set "WRITER_LOCK_HELD=false"
exit /b 0

:validate_output_root
set "VALIDATE_CANDIDATE=%~f1"
set "VALIDATE_DESCRIPTION=%~2"
if /i "!VALIDATE_CANDIDATE!"=="!UNITY_CLIENT_CODE_ROOT!" exit /b 0
if /i "!VALIDATE_CANDIDATE!"=="!UNITY_CLIENT_DATA_ROOT!" exit /b 0
if /i "!VALIDATE_CANDIDATE!"=="!GENERATED_ROOT!" exit /b 0
call :is_strict_child "!VALIDATE_CANDIDATE!" "!UNITY_CLIENT_CODE_ROOT!"
if !ERRORLEVEL! equ 0 exit /b 0
call :is_strict_child "!VALIDATE_CANDIDATE!" "!UNITY_CLIENT_DATA_ROOT!"
if !ERRORLEVEL! equ 0 exit /b 0
call :is_strict_child "!VALIDATE_CANDIDATE!" "!GENERATED_ROOT!"
if !ERRORLEVEL! equ 0 exit /b 0
echo [ERROR] Refuse !VALIDATE_DESCRIPTION! outside approved generated roots: !VALIDATE_CANDIDATE!
echo [ERROR] Approved roots: !UNITY_CLIENT_CODE_ROOT! ; !UNITY_CLIENT_DATA_ROOT! ; !GENERATED_ROOT!
exit /b 1

:paths_overlap
set "OVERLAP_FIRST=%~f1"
set "OVERLAP_SECOND=%~f2"
if /i "!OVERLAP_FIRST!"=="!OVERLAP_SECOND!" exit /b 0
call :is_strict_child "!OVERLAP_FIRST!" "!OVERLAP_SECOND!"
if !ERRORLEVEL! equ 0 exit /b 0
call :is_strict_child "!OVERLAP_SECOND!" "!OVERLAP_FIRST!"
if !ERRORLEVEL! equ 0 exit /b 0
exit /b 1

:reject_reparse_chain
set "CYCLONE_DATATABLE_PATH_TO_CHECK=%~f1"
set "CYCLONE_DATATABLE_PATH_STOP=%~f2"
where powershell.exe >nul 2>nul
if !ERRORLEVEL! neq 0 (
    echo [ERROR] powershell.exe is required to verify that generated output does not traverse a junction or reparse point.
    exit /b 1
)
powershell.exe -NoProfile -NonInteractive -Command "$p=[IO.Path]::GetFullPath($env:CYCLONE_DATATABLE_PATH_TO_CHECK); $stop=[IO.Path]::GetFullPath($env:CYCLONE_DATATABLE_PATH_STOP); $reached=$false; while($p) { if(Test-Path -LiteralPath $p) { $item=Get-Item -Force -LiteralPath $p; if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { [Console]::Error.WriteLine('[ERROR] Generated output path traverses a reparse point: '+$p); exit 2 } }; if([string]::Equals($p,$stop,[StringComparison]::OrdinalIgnoreCase)) { $reached=$true; break }; $parent=[IO.Directory]::GetParent($p); if($null -eq $parent) { break }; $p=$parent.FullName }; if(-not $reached) { [Console]::Error.WriteLine('[ERROR] Generated output path did not reach its approved repository root.'); exit 3 }; exit 0"
if !ERRORLEVEL! neq 0 exit /b 1
exit /b 0

:is_strict_child
set "CHILD_PROBE=%~f1"
set "CHILD_ROOT=%~f2"
if /i "!CHILD_PROBE!"=="!CHILD_ROOT!" exit /b 1
:is_strict_child_loop
for %%p in ("!CHILD_PROBE!\..") do set "CHILD_PARENT=%%~fp"
if /i "!CHILD_PARENT!"=="!CHILD_ROOT!" exit /b 0
if /i "!CHILD_PARENT!"=="!CHILD_PROBE!" exit /b 1
set "CHILD_PROBE=!CHILD_PARENT!"
goto :is_strict_child_loop

:help
echo Usage: gen_code_bin_to_project_lazyload.bat [options]
echo.
echo Options (override build_config.ini):
echo   -t  Target      (client/server/all)
echo   -c  Code target (cs-bin etc.)
echo   -d  Data target (bin etc.)
echo   --validate-only  Validate tools, inputs, and approved output roots without writing
echo   --pause      Keep the window open after finishing
echo   --no-pause   Exit immediately after finishing
echo   -h  Show this help
set "EXIT_CODE=0"
goto :finish

:finish
call :release_writer_lock
if !ERRORLEVEL! neq 0 if "!EXIT_CODE!"=="0" set "EXIT_CODE=1"
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
