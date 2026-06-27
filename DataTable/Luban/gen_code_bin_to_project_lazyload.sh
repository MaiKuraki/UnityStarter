#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${LC_ALL:-}" && -z "${LANG:-}" ]]; then
    if command -v locale >/dev/null 2>&1 && locale -a 2>/dev/null | grep -qi '^C\.UTF-8$'; then
        export LANG=C.UTF-8
    elif command -v locale >/dev/null 2>&1 && locale -a 2>/dev/null | grep -qi '^en_US\.UTF-8$'; then
        export LANG=en_US.UTF-8
    fi
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CONFIG="$SCRIPT_DIR/build_config.ini"

if [[ ! -f "$CONFIG" ]]; then
    echo "[ERROR] Config file not found: $CONFIG"
    exit 1
fi

# ============================================================
#  Parse build_config.ini
# ============================================================
while IFS='=' read -r key value; do
    # Skip comments, section headers, empty lines
    [[ "$key" =~ ^[[:space:]]*[\#\;] ]] && continue
    [[ "$key" =~ ^[[:space:]]*\[ ]] && continue
    [[ -z "${key// }" ]] && continue
    value="${value%$'\r'}"
    # Trim and assign
    k="${key// }"
    printf -v "$k" '%s' "$value"
done < "$CONFIG"

# ============================================================
#  Parse CLI arguments (override INI values)
# ============================================================
while [[ $# -gt 0 ]]; do
    case "$1" in
        -t) target="$2";   shift 2 ;;
        -c) code_target="$2"; shift 2 ;;
        -d) data_target="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: gen_code_bin_to_project_lazyload.sh [options]"
            echo ""
            echo "Options (override build_config.ini):"
            echo "  -t  Target      (client/server/all)"
            echo "  -c  Code target (cs-bin etc.)"
            echo "  -d  Data target (bin etc.)"
            echo "  -h  Show this help"
            exit 0
            ;;
        *) echo "[WARN] Unknown option: $1"; shift ;;
    esac
done

TARGET="${target:-client}"
CODE_TARGET="${code_target:-cs-bin}"
DATA_TARGET="${data_target:-bin}"
CLEAN_OUTPUT="${clean_output:-true}"
CLEAN_OUTPUT_ENABLED="true"
case "$CLEAN_OUTPUT" in
    false|False|FALSE|0)
        CLEAN_OUTPUT_ENABLED="false"
        ;;
esac
CLEAN_ORPHAN_META="${clean_orphan_meta:-true}"
CLEAN_ORPHAN_META_ENABLED="true"
case "$CLEAN_ORPHAN_META" in
    false|False|FALSE|0)
        CLEAN_ORPHAN_META_ENABLED="false"
        ;;
esac
CODE_OUT_CONFIG=""
DATA_OUT_CONFIG=""

case "$TARGET" in
    client)
        CODE_OUT_CONFIG="${client_code_out:-}"
        DATA_OUT_CONFIG="${client_data_out:-}"
        ;;
    server)
        CODE_OUT_CONFIG="${server_code_out:-}"
        DATA_OUT_CONFIG="${server_data_out:-}"
        ;;
    all)
        CODE_OUT_CONFIG="${all_code_out:-}"
        DATA_OUT_CONFIG="${all_data_out:-}"
        ;;
    *)
        echo "[ERROR] Unknown target: $TARGET"
        echo "[ERROR] Expected one of: client, server, all"
        exit 1
        ;;
esac

if [[ -z "$CODE_OUT_CONFIG" ]]; then
    echo "[ERROR] Missing code output path for target: $TARGET"
    exit 1
fi

if [[ -z "$DATA_OUT_CONFIG" ]]; then
    echo "[ERROR] Missing data output path for target: $TARGET"
    exit 1
fi

# ============================================================
#  Resolve paths
# ============================================================
resolve() {
    local path="$1"
    if [[ "$path" = /* ]]; then
        echo "$path"
    else
        echo "$SCRIPT_DIR/$path"
    fi
}

LUBAN_DLL="$(resolve "$luban_dll")"
CODE_OUT="$(resolve "$CODE_OUT_CONFIG")"
DATA_OUT="$(resolve "$DATA_OUT_CONFIG")"

CUSTOM_DIR=""
if [[ -n "${custom_template_dir:-}" ]]; then
    CUSTOM_DIR="$(resolve "$custom_template_dir")"
fi

# ============================================================
#  Validate
# ============================================================
if [[ ! -f "$LUBAN_DLL" ]]; then
    echo "[ERROR] Luban.dll not found: $LUBAN_DLL"
    exit 1
fi

mkdir -p "$CODE_OUT" "$DATA_OUT"

# ============================================================
#  Copy bridge files
# ============================================================
if [[ -n "${custom_template_dir:-}" && -d "$CUSTOM_DIR" && -n "${bridge_files:-}" ]]; then
    IFS=',' read -ra FILES <<< "$bridge_files"
    for f in "${FILES[@]}"; do
        f="${f// /}"
        if [[ -f "$CUSTOM_DIR/$f" ]]; then
            echo "[Luban] Copying bridge: $f"
            cp "$CUSTOM_DIR/$f" "$CODE_OUT/"
        fi
    done
fi

# ============================================================
#  Build Luban arguments
# ============================================================
LUBAN_ARGS=(
    -t "$TARGET"
    -c "$CODE_TARGET"
    -d "$DATA_TARGET"
    --conf "$SCRIPT_DIR/luban.conf"
    -x "lineEnding=${line_ending:-crlf}"
    -x "outputCodeDir=$CODE_OUT"
    -x "outputDataDir=$DATA_OUT"
    -x "outputSaver.$CODE_TARGET.cleanUpOutputDir=$CLEAN_OUTPUT_ENABLED"
    -x "outputSaver.$DATA_TARGET.cleanUpOutputDir=$CLEAN_OUTPUT_ENABLED"
)

if [[ -n "${custom_template_dir:-}" && -d "${CUSTOM_DIR:-}" ]]; then
    LUBAN_ARGS+=(--customTemplateDir "$CUSTOM_DIR")
fi

# ============================================================
#  Run
# ============================================================
echo "[Luban] Target     : $TARGET"
echo "[Luban] Code target: $CODE_TARGET"
echo "[Luban] Data target: $DATA_TARGET"
echo "[Luban] Code output: $CODE_OUT"
echo "[Luban] Data output: $DATA_OUT"
echo "[Luban] Clean output: $CLEAN_OUTPUT_ENABLED"
echo "[Luban] Clean orphan meta: $CLEAN_ORPHAN_META_ENABLED"
echo ""

exit_code=0
dotnet "$LUBAN_DLL" "${LUBAN_ARGS[@]}" || exit_code=$?

if [[ $exit_code -ne 0 ]]; then
    echo "[Luban] Build FAILED: exit code $exit_code"
    exit "$exit_code"
fi

if [[ -n "${string_constant_tables:-}" ]]; then
    if [[ -z "${codegen_project:-}" ]]; then
        echo "[ERROR] string_constant_tables is configured but codegen_project is empty"
        exit 1
    fi

    CODEGEN_PROJECT="$(resolve "$codegen_project")"
    if [[ ! -f "$CODEGEN_PROJECT" ]]; then
        echo "[ERROR] DataTable code generator project not found: $CODEGEN_PROJECT"
        exit 1
    fi

    if ! command -v dotnet >/dev/null 2>&1; then
        echo "[ERROR] dotnet SDK is required to run DataTable string constant generation"
        exit 1
    fi

    echo "[DataTable.CodeGen] Generating string constants"
    dotnet run --project "$CODEGEN_PROJECT" -- \
        --config "$CONFIG" \
        --luban-conf "$SCRIPT_DIR/luban.conf" \
        --data-dir "$SCRIPT_DIR/Datas" \
        --target "$TARGET" \
        --code-output "$CODE_OUT" \
        --line-ending "${line_ending:-crlf}"
fi

cleanup_orphan_meta() {
    local root="$1"
    if [[ -z "$root" || ! -d "$root" ]]; then
        return 0
    fi

    local full_root
    full_root="$(cd "$root" && pwd)"
    if [[ "$full_root" == "/" ]]; then
        echo "[WARN] Refuse to clean orphan meta in filesystem root: $full_root"
        return 0
    fi

    while IFS= read -r -d '' meta_file; do
        local pair_path="${meta_file%.meta}"
        if [[ ! -e "$pair_path" ]]; then
            echo "[Luban] Removing orphan meta: $meta_file"
            rm -f "$meta_file"
        fi
    done < <(find "$full_root" -type f -name '*.meta' -print0)
}

if [[ "$CLEAN_ORPHAN_META_ENABLED" == "true" ]]; then
    cleanup_orphan_meta "$CODE_OUT"
    cleanup_orphan_meta "$DATA_OUT"
fi

echo "[Luban] Build SUCCESS"
