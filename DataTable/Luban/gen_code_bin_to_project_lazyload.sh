#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${LC_ALL:-}" && -z "${LANG:-}" ]]; then
    if command -v locale >/dev/null 2>&1 && locale -a 2>/dev/null | grep -qi '^C\.UTF-8$'; then
        export LANG=C.UTF-8
    elif command -v locale >/dev/null 2>&1 && locale -a 2>/dev/null | grep -qi '^en_US\.UTF-8$'; then
        export LANG=en_US.UTF-8
    fi
fi

SCRIPT_DIR="$(cd -P "$(dirname "$0")" && pwd -P)"
CONFIG="$SCRIPT_DIR/build_config.ini"
WRITER_LOCK_DIR="$SCRIPT_DIR/.cyclonegames-datatable-writer.lock"
WRITER_LOCK_OWNER="$WRITER_LOCK_DIR/owner.txt"
WRITER_LOCK_HELD="false"
WRITER_LOCK_TOKEN=""

MAXIMUM_BRIDGE_FILES=256
MAXIMUM_BRIDGE_PATH_CHARACTERS=1024
MAXIMUM_BRIDGE_SEGMENT_CHARACTERS=255
MAXIMUM_BRIDGE_FILE_BYTES=$((16 * 1024 * 1024))
MAXIMUM_TOTAL_BRIDGE_BYTES=$((64 * 1024 * 1024))

if [[ ! -f "$CONFIG" ]]; then
    echo "[ERROR] Config file not found: $CONFIG"
    exit 1
fi

release_writer_lock() {
    if [[ "$WRITER_LOCK_HELD" != "true" ]]; then
        return 0
    fi

    if [[ ! -f "$WRITER_LOCK_OWNER" || -L "$WRITER_LOCK_OWNER" ]] ||
       ! grep -Fqx "token=$WRITER_LOCK_TOKEN" "$WRITER_LOCK_OWNER"; then
        echo "[ERROR] DataTable writer-lock ownership changed; leaving the residual lock in place: $WRITER_LOCK_DIR" >&2
        WRITER_LOCK_HELD="false"
        return 1
    fi

    if ! rm -f "$WRITER_LOCK_OWNER"; then
        echo "[ERROR] Failed to remove DataTable writer-lock owner metadata; leaving the lock in place: $WRITER_LOCK_DIR" >&2
        WRITER_LOCK_HELD="false"
        return 1
    fi
    if ! rmdir "$WRITER_LOCK_DIR"; then
        echo "[ERROR] Failed to release DataTable writer lock; residual content will block future writers: $WRITER_LOCK_DIR" >&2
        WRITER_LOCK_HELD="false"
        return 1
    fi

    WRITER_LOCK_HELD="false"
    return 0
}

writer_lock_on_exit() {
    local original_status="$1"
    trap - EXIT HUP INT TERM
    if ! release_writer_lock; then
        original_status=1
    fi
    exit "$original_status"
}

writer_lock_on_signal() {
    local signal_name="$1"
    local signal_status="$2"
    trap - EXIT HUP INT TERM
    if [[ "$WRITER_LOCK_HELD" == "true" ]]; then
        echo "[ERROR] Received $signal_name while DataTable generation held the writer lock." >&2
        echo "[ERROR] The residual lock is intentionally retained. Confirm that all prior writer processes stopped and audit generated output before manual removal: $WRITER_LOCK_DIR" >&2
    fi
    exit "$signal_status"
}

acquire_writer_lock() {
    if ! mkdir "$WRITER_LOCK_DIR" 2>/dev/null; then
        echo "[ERROR] DataTable generation workspace is locked: $WRITER_LOCK_DIR" >&2
        if [[ -f "$WRITER_LOCK_OWNER" && ! -L "$WRITER_LOCK_OWNER" ]]; then
            echo "[ERROR] Existing lock owner metadata:" >&2
            sed 's/^/[ERROR]   /' "$WRITER_LOCK_OWNER" >&2 || true
        fi
        echo "[ERROR] Refusing to continue. Never auto-break this lock; first confirm the prior writer stopped and audit its output." >&2
        return 1
    fi

    WRITER_LOCK_TOKEN="$$-$(date -u '+%Y%m%dT%H%M%SZ')-${RANDOM:-0}"
    if ! {
        printf '%s\n' 'schema=CycloneGames.DataTable.WriterLock/1'
        printf 'platform=%s\n' "$(uname -s 2>/dev/null || printf 'Unix')"
        printf 'host=%s\n' "$(hostname 2>/dev/null || printf 'unknown')"
        printf 'pid=%s\n' "$$"
        printf 'started_utc=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
        printf 'target=%s\n' "$TARGET"
        printf 'token=%s\n' "$WRITER_LOCK_TOKEN"
    } > "$WRITER_LOCK_OWNER"; then
        echo "[ERROR] Failed to write DataTable writer-lock owner metadata." >&2
        rm -f "$WRITER_LOCK_OWNER" 2>/dev/null || true
        if ! rmdir "$WRITER_LOCK_DIR" 2>/dev/null; then
            echo "[ERROR] Failed to roll back the incomplete writer lock; it remains fail-closed: $WRITER_LOCK_DIR" >&2
        fi
        return 1
    fi

    WRITER_LOCK_HELD="true"
    trap 'writer_lock_on_exit $?' EXIT
    trap 'writer_lock_on_signal HUP 129' HUP
    trap 'writer_lock_on_signal INT 130' INT
    trap 'writer_lock_on_signal TERM 143' TERM
    return 0
}

# ============================================================
#  Parse build_config.ini
# ============================================================
while IFS='=' read -r key value; do
    key="${key%$'\r'}"
    # Skip comments, section headers, empty lines
    [[ "$key" =~ ^[[:space:]]*[\#\;] ]] && continue
    [[ "$key" =~ ^[[:space:]]*\[ ]] && continue
    [[ -z "${key// }" ]] && continue
    value="${value%$'\r'}"
    # Trim and assign
    k="${key// }"
    case "$k" in
        luban_dll|client_code_out|client_data_out|server_code_out|server_data_out|all_code_out|all_data_out|custom_template_dir|bridge_files|target|code_target|data_target|clean_output|clean_orphan_meta|line_ending|codegen_project|string_constant_tables|string_constant_value_column|string_constant_comment_column|string_constant_enabled_column|string_constant_scope_column|string_constant_generated_comment_language)
            printf -v "$k" '%s' "$value"
            ;;
        *)
            echo "[WARN] Ignoring unsupported build_config.ini key: $k"
            ;;
    esac
done < "$CONFIG"

# ============================================================
#  Parse CLI arguments (override INI values)
# ============================================================
VALIDATE_ONLY="false"
SEEN_TARGET="false"
SEEN_CODE_TARGET="false"
SEEN_DATA_TARGET="false"
SEEN_VALIDATE_ONLY="false"
while [[ $# -gt 0 ]]; do
    case "$1" in
        -t|-c|-d)
            if [[ $# -lt 2 || -z "$2" ]]; then
                echo "[ERROR] Option requires a value: $1" >&2
                exit 1
            fi
            case "$1" in
                -t)
                    if [[ "$SEEN_TARGET" == "true" ]]; then
                        echo "[ERROR] Duplicate option: -t" >&2
                        exit 1
                    fi
                    SEEN_TARGET="true"
                    target="$2"
                    ;;
                -c)
                    if [[ "$SEEN_CODE_TARGET" == "true" ]]; then
                        echo "[ERROR] Duplicate option: -c" >&2
                        exit 1
                    fi
                    SEEN_CODE_TARGET="true"
                    code_target="$2"
                    ;;
                -d)
                    if [[ "$SEEN_DATA_TARGET" == "true" ]]; then
                        echo "[ERROR] Duplicate option: -d" >&2
                        exit 1
                    fi
                    SEEN_DATA_TARGET="true"
                    data_target="$2"
                    ;;
            esac
            shift 2
            ;;
        --validate-only)
            if [[ "$SEEN_VALIDATE_ONLY" == "true" ]]; then
                echo "[ERROR] Duplicate option: --validate-only" >&2
                exit 1
            fi
            SEEN_VALIDATE_ONLY="true"
            VALIDATE_ONLY="true"
            shift
            ;;
        -h|--help)
            echo "Usage: gen_code_bin_to_project_lazyload.sh [options]"
            echo ""
            echo "Options (override build_config.ini):"
            echo "  -t  Target      (client/server/all)"
            echo "  -c  Code target (cs-bin etc.)"
            echo "  -d  Data target (bin etc.)"
            echo "  --validate-only  Validate tools, inputs, and approved output roots without writing"
            echo "  -h  Show this help"
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown option: $1" >&2
            echo "[ERROR] Run with --help. Refusing to continue because this command can replace generated output." >&2
            exit 1
            ;;
    esac
done

TARGET="${target:-client}"
CODE_TARGET="${code_target:-cs-bin}"
DATA_TARGET="${data_target:-bin}"
CLEAN_OUTPUT="${clean_output:-false}"
case "$CLEAN_OUTPUT" in
    true|True|TRUE|1) CLEAN_OUTPUT_ENABLED="true" ;;
    false|False|FALSE|0) CLEAN_OUTPUT_ENABLED="false" ;;
    *)
        echo "[ERROR] clean_output must be true, false, 1, or 0: $CLEAN_OUTPUT" >&2
        exit 1
        ;;
esac
CLEAN_ORPHAN_META="${clean_orphan_meta:-false}"
case "$CLEAN_ORPHAN_META" in
    true|True|TRUE|1) CLEAN_ORPHAN_META_ENABLED="true" ;;
    false|False|FALSE|0) CLEAN_ORPHAN_META_ENABLED="false" ;;
    *)
        echo "[ERROR] clean_orphan_meta must be true, false, 1, or 0: $CLEAN_ORPHAN_META" >&2
        exit 1
        ;;
esac
LINE_ENDING="${line_ending:-crlf}"
case "$LINE_ENDING" in
    crlf|CRLF) LINE_ENDING="crlf" ;;
    lf|LF) LINE_ENDING="lf" ;;
    *)
        echo "[ERROR] line_ending must be crlf or lf: $LINE_ENDING" >&2
        exit 1
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

acquire_writer_lock

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

canonical_path() {
    local candidate="$1"
    local existing="$candidate"
    local -a missing_segments=()

    while [[ ! -e "$existing" ]]; do
        local parent
        parent="$(dirname -- "$existing")"
        if [[ "$parent" == "$existing" ]]; then
            echo "[ERROR] Cannot resolve path: $candidate" >&2
            return 1
        fi

        missing_segments=("$(basename -- "$existing")" "${missing_segments[@]}")
        existing="$parent"
    done

    local canonical
    if [[ -d "$existing" ]]; then
        canonical="$(cd -P "$existing" && pwd)"
    else
        canonical="$(cd -P "$(dirname -- "$existing")" && pwd)/$(basename -- "$existing")"
    fi

    local segment
    for segment in "${missing_segments[@]}"; do
        [[ -z "$segment" || "$segment" == "." ]] && continue
        if [[ "$segment" == ".." ]]; then
            echo "[ERROR] Unresolved path traversal is not allowed: $candidate" >&2
            return 1
        fi
        canonical="$canonical/$segment"
    done

    printf '%s\n' "$canonical"
}

is_strict_child() {
    local candidate="$1"
    local root="$2"
    [[ "$candidate" == "$root/"* ]]
}

paths_overlap() {
    local first="$1"
    local second="$2"
    [[ "$first" == "$second" || "$first" == "$second/"* || "$second" == "$first/"* ]]
}

resolve_custom_template_root() {
    local configured="$1"
    local relative="$configured"
    if [[ "$configured" == /* ]]; then
        if [[ "$configured" != "$SCRIPT_DIR/"* ]]; then
            echo "[ERROR] custom_template_dir must be below the repository-owned DataTable/Luban directory: $configured" >&2
            return 1
        fi
        relative="${configured#"$SCRIPT_DIR/"}"
    fi
    if [[ -z "$relative" || "$relative" == *\\* || "$relative" == *:* ]]; then
        echo "[ERROR] custom_template_dir must use a portable relative slash-separated path below: $SCRIPT_DIR" >&2
        return 1
    fi

    local probe="$SCRIPT_DIR"
    local component
    local -a template_segments=()
    IFS='/' read -ra template_segments <<< "$relative"
    for component in "${template_segments[@]}"; do
        if [[ -z "$component" || "$component" == "." || "$component" == ".." ||
              ! "$component" =~ ^[A-Za-z0-9._-]+$ ||
              ${#component} -gt $MAXIMUM_BRIDGE_SEGMENT_CHARACTERS ||
              "$component" == *. ]]; then
            echo "[ERROR] custom_template_dir contains a non-portable or traversal segment: $component" >&2
            return 1
        fi
        probe="$probe/$component"
        if [[ -L "$probe" ]]; then
            echo "[ERROR] custom_template_dir refuses symbolic links: $probe" >&2
            return 1
        fi
    done

    if [[ ! -d "$probe" ]]; then
        echo "[ERROR] custom_template_dir does not exist: $probe" >&2
        return 1
    fi
    local canonical
    canonical="$(canonical_path "$probe")" || return 1
    if ! is_strict_child "$canonical" "$SCRIPT_DIR"; then
        echo "[ERROR] custom_template_dir must be a strict child of: $SCRIPT_DIR" >&2
        return 1
    fi
    printf '%s\n' "$canonical"
}

validate_output_root() {
    local candidate="$1"
    local description="$2"
    if [[ "$candidate" == "$UNITY_CLIENT_CODE_ROOT" ||
          "$candidate" == "$UNITY_CLIENT_DATA_ROOT" ||
          "$candidate" == "$GENERATED_ROOT" ]] ||
       is_strict_child "$candidate" "$UNITY_CLIENT_CODE_ROOT" ||
       is_strict_child "$candidate" "$UNITY_CLIENT_DATA_ROOT" ||
       is_strict_child "$candidate" "$GENERATED_ROOT"; then
        return 0
    fi

    echo "[ERROR] Refuse $description outside approved generated roots: $candidate" >&2
    echo "[ERROR] Approved roots: $UNITY_CLIENT_CODE_ROOT ; $UNITY_CLIENT_DATA_ROOT ; $GENERATED_ROOT" >&2
    return 1
}

REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd -P)"
UNITY_ASSETS_ROOT="$(canonical_path "$REPO_ROOT/UnityStarter/Assets")"
GENERATED_ROOT="$(canonical_path "$SCRIPT_DIR/Generated")"
UNITY_CLIENT_CODE_ROOT="$(canonical_path "$REPO_ROOT/UnityStarter/Assets/UnityStarter/Scripts/Generated/DataTable")"
UNITY_CLIENT_DATA_ROOT="$(canonical_path "$REPO_ROOT/UnityStarter/Assets/StreamingAssets/DataTable")"
LUBAN_DLL="$(canonical_path "$(resolve "$luban_dll")")"
CODE_OUT="$(canonical_path "$(resolve "$CODE_OUT_CONFIG")")"
DATA_OUT="$(canonical_path "$(resolve "$DATA_OUT_CONFIG")")"

CUSTOM_DIR=""
if [[ -n "${custom_template_dir:-}" ]]; then
    CUSTOM_DIR="$(resolve_custom_template_root "$custom_template_dir")"
fi

file_size_bytes() {
    local size
    size="$(LC_ALL=C wc -c < "$1")" || return 1
    size="${size//[[:space:]]/}"
    if [[ ! "$size" =~ ^[0-9]+$ ]]; then
        echo "[ERROR] Could not determine a bounded file size: $1" >&2
        return 1
    fi
    printf '%s\n' "$size"
}

publish_bridge_file() {
    local source="$1"
    local destination="$2"
    local display_name="$3"
    local expected_size="$4"

    if [[ ! -f "$source" || -L "$source" ]]; then
        echo "[ERROR] Bridge source changed after validation: $source" >&2
        return 1
    fi
    local current_size
    current_size="$(file_size_bytes "$source")" || return 1
    if (( current_size != expected_size || current_size > MAXIMUM_BRIDGE_FILE_BYTES )); then
        echo "[ERROR] Bridge source size changed after validation: $source" >&2
        return 1
    fi

    if [[ -e "$destination" || -L "$destination" ]]; then
        if [[ -f "$destination" && ! -L "$destination" ]] && cmp -s "$source" "$destination"; then
            echo "[Luban] Bridge already current: $display_name"
            return 0
        fi
        echo "[ERROR] Bridge output exists without matching content or ownership proof; refusing to overwrite: $destination" >&2
        return 1
    fi

    local destination_name temporary_path temporary_size
    destination_name="$(basename "$destination")"
    temporary_path="$(mktemp "$CODE_OUT/.${destination_name}.cyclonegames-bridge.XXXXXX")" || {
        echo "[ERROR] Failed to create a same-directory bridge staging file: $destination" >&2
        return 1
    }

    if ! cp "$source" "$temporary_path" || ! chmod 0644 "$temporary_path"; then
        echo "[ERROR] Failed to stage bridge file: $display_name" >&2
        rm -f "$temporary_path" 2>/dev/null || true
        return 1
    fi
    temporary_size="$(file_size_bytes "$temporary_path")" || {
        rm -f "$temporary_path" 2>/dev/null || true
        return 1
    }
    if (( temporary_size != expected_size )) || ! cmp -s "$source" "$temporary_path"; then
        echo "[ERROR] Bridge source changed or staging verification failed: $source" >&2
        rm -f "$temporary_path" 2>/dev/null || true
        return 1
    fi

    if [[ -e "$destination" || -L "$destination" ]]; then
        if [[ -f "$destination" && ! -L "$destination" ]] && cmp -s "$temporary_path" "$destination"; then
            echo "[Luban] Bridge became current during publication: $display_name"
            rm -f "$temporary_path" || return 1
            return 0
        fi
        echo "[ERROR] Bridge output appeared during publication; refusing to overwrite: $destination" >&2
        rm -f "$temporary_path" 2>/dev/null || true
        return 1
    fi

    echo "[Luban] Publishing bridge: $display_name"
    if ! ln "$temporary_path" "$destination"; then
        if [[ -f "$destination" && ! -L "$destination" ]] && cmp -s "$temporary_path" "$destination"; then
            echo "[Luban] Bridge became current during atomic publication: $display_name"
            rm -f "$temporary_path" || return 1
            return 0
        fi
        echo "[ERROR] Atomic bridge publication failed without overwriting the destination: $destination" >&2
        rm -f "$temporary_path" 2>/dev/null || true
        return 1
    fi
    if ! rm -f "$temporary_path"; then
        echo "[ERROR] Bridge was published, but its recoverable staging hard link remains: $temporary_path" >&2
        return 1
    fi
    return 0
}

process_bridge_files() {
    local copy_mode="$1"
    [[ -z "${bridge_files:-}" ]] && return 0
    if [[ -z "$CUSTOM_DIR" || ! -d "$CUSTOM_DIR" || -L "$CUSTOM_DIR" ]]; then
        echo "[ERROR] bridge_files requires a physical custom_template_dir below DataTable/Luban." >&2
        return 1
    fi
    if (( ${#bridge_files} > MAXIMUM_BRIDGE_FILES * (MAXIMUM_BRIDGE_PATH_CHARACTERS + 1) )) ||
       [[ "$bridge_files" == ,* || "$bridge_files" == *, || "$bridge_files" == *,,* ]]; then
        echo "[ERROR] bridge_files exceeds its bounded grammar or contains an empty item." >&2
        return 1
    fi

    local -a sources=()
    local -a display_names=()
    local -a destination_names=()
    local -a destination_paths=()
    local -a source_sizes=()
    local -a configured_files=()
    IFS=',' read -ra configured_files <<< "$bridge_files"
    if (( ${#configured_files[@]} > MAXIMUM_BRIDGE_FILES )); then
        echo "[ERROR] Bridge file count ${#configured_files[@]} exceeds the limit $MAXIMUM_BRIDGE_FILES." >&2
        return 1
    fi

    local raw name bridge_path destination_name destination_key existing_destination probe component component_base component_upper source_size
    local total_size=0
    for raw in "${configured_files[@]}"; do
        name="${raw#"${raw%%[![:space:]]*}"}"
        name="${name%"${name##*[![:space:]]}"}"
        if [[ -z "$name" || ${#name} -gt $MAXIMUM_BRIDGE_PATH_CHARACTERS ||
              "$name" == /* || "$name" == *\\* || "$name" == *:* ]]; then
            echo "[ERROR] Bridge file must use a bounded portable relative slash-separated path: $name" >&2
            return 1
        fi

        probe="$CUSTOM_DIR"
        local -a bridge_segments=()
        IFS='/' read -ra bridge_segments <<< "$name"
        for component in "${bridge_segments[@]}"; do
            component_base="${component%%.*}"
            component_upper="$(printf '%s' "$component_base" | LC_ALL=C tr '[:lower:]' '[:upper:]')"
            if [[ -z "$component" || "$component" == "." || "$component" == ".." ||
                  ! "$component" =~ ^[A-Za-z0-9._-]+$ ||
                  ${#component} -gt $MAXIMUM_BRIDGE_SEGMENT_CHARACTERS ||
                  "$component" == *. ]]; then
                echo "[ERROR] Bridge path segment is not portable across Windows, macOS, and Linux: $component" >&2
                return 1
            fi
            case "$component_upper" in
                CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])
                    echo "[ERROR] Bridge path uses a Windows-reserved device name: $component" >&2
                    return 1
                    ;;
            esac
            probe="$probe/$component"
            if [[ -L "$probe" ]]; then
                echo "[ERROR] Bridge file traverses a symbolic link: $probe" >&2
                return 1
            fi
        done

        bridge_path="$(canonical_path "$CUSTOM_DIR/$name")" || return 1
        if ! is_strict_child "$bridge_path" "$CUSTOM_DIR"; then
            echo "[ERROR] Bridge file escapes custom_template_dir: $name" >&2
            return 1
        fi
        if [[ ! -f "$bridge_path" || -L "$bridge_path" ]]; then
            echo "[ERROR] Bridge source must be a physical file: $bridge_path" >&2
            return 1
        fi
        source_size="$(file_size_bytes "$bridge_path")" || return 1
        if (( source_size > MAXIMUM_BRIDGE_FILE_BYTES ||
              total_size > MAXIMUM_TOTAL_BRIDGE_BYTES - source_size )); then
            echo "[ERROR] Bridge file size budget exceeded: $bridge_path" >&2
            return 1
        fi
        total_size=$((total_size + source_size))

        destination_name="$(basename "$bridge_path")"
        if [[ -L "$CODE_OUT" || -L "$CODE_OUT/$destination_name" ]]; then
            echo "[ERROR] Bridge output refuses a symbolic-link destination: $CODE_OUT/$destination_name" >&2
            return 1
        fi
        destination_key="$(printf '%s' "$destination_name" | LC_ALL=C tr '[:upper:]' '[:lower:]')"
        for existing_destination in "${destination_names[@]}"; do
            if [[ "$existing_destination" == "$destination_key" ]]; then
                echo "[ERROR] Bridge files collide at output name: $destination_name" >&2
                return 1
            fi
        done
        destination_names+=("$destination_key")
        sources+=("$bridge_path")
        display_names+=("$name")
        destination_paths+=("$CODE_OUT/$destination_name")
        source_sizes+=("$source_size")

        if [[ -e "$CODE_OUT/$destination_name" || -L "$CODE_OUT/$destination_name" ]]; then
            if [[ ! -f "$CODE_OUT/$destination_name" || -L "$CODE_OUT/$destination_name" ]] ||
               ! cmp -s "$bridge_path" "$CODE_OUT/$destination_name"; then
                echo "[ERROR] Bridge output exists without matching content or ownership proof; refusing to overwrite: $CODE_OUT/$destination_name" >&2
                return 1
            fi
        fi
    done

    if [[ "$copy_mode" == "true" ]]; then
        if [[ ! -d "$CODE_OUT" || -L "$CODE_OUT" ]]; then
            echo "[ERROR] Bridge output root must be an existing physical directory: $CODE_OUT" >&2
            return 1
        fi
        local index
        for index in "${!sources[@]}"; do
            publish_bridge_file \
                "${sources[$index]}" \
                "${destination_paths[$index]}" \
                "${display_names[$index]}" \
                "${source_sizes[$index]}" || return 1
        done
    fi
}

# ============================================================
#  Validate
# ============================================================
if [[ ! "$CODE_TARGET" =~ ^[A-Za-z0-9._-]+$ || ! "$DATA_TARGET" =~ ^[A-Za-z0-9._-]+$ ]]; then
    echo "[ERROR] Code/data targets may contain only letters, digits, '.', '_' and '-'."
    exit 1
fi

validate_output_root "$CODE_OUT" "code output" || exit 1
validate_output_root "$DATA_OUT" "data output" || exit 1
if paths_overlap "$CODE_OUT" "$DATA_OUT"; then
    echo "[ERROR] Code and data output roots must not contain one another."
    exit 1
fi

if [[ "$CLEAN_OUTPUT_ENABLED" == "true" && "${CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN:-}" != "1" ]]; then
    echo "[ERROR] clean_output=true requires CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN=1." >&2
    echo "[ERROR] Keep clean_output=false for normal generation; perform destructive replacement only after an explicit backup/recovery decision." >&2
    exit 1
fi

if [[ "$CLEAN_ORPHAN_META_ENABLED" == "true" && "${CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN:-}" != "1" ]]; then
    echo "[ERROR] clean_orphan_meta=true requires CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN=1." >&2
    exit 1
fi

if [[ ! -f "$SCRIPT_DIR/luban.conf" ]]; then
    echo "[ERROR] Luban configuration not found: $SCRIPT_DIR/luban.conf"
    exit 1
fi

if [[ ! -f "$LUBAN_DLL" ]]; then
    echo "[ERROR] Luban.dll not found: $LUBAN_DLL"
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[ERROR] dotnet is required to run Luban."
    exit 1
fi

for required_workbook in __tables__.xlsx __beans__.xlsx __enums__.xlsx; do
    if [[ ! -f "$SCRIPT_DIR/Datas/$required_workbook" ]]; then
        echo "[ERROR] Required Luban schema workbook not found: $SCRIPT_DIR/Datas/$required_workbook"
        exit 1
    fi
done

CODEGEN_MANIFEST="$CODE_OUT/.cyclonegames-datatable-codegen-manifest.json"
CODEGEN_REQUIRED=false
if [[ -n "${string_constant_tables:-}" || -f "$CODEGEN_MANIFEST" ]]; then
    CODEGEN_REQUIRED=true
fi

if [[ "$CODEGEN_REQUIRED" == "true" ]]; then
    if [[ -z "${codegen_project:-}" ]]; then
        echo "[ERROR] CodeGen output is configured or previously owned, but codegen_project is empty"
        exit 1
    fi
    CODEGEN_PROJECT="$(canonical_path "$(resolve "$codegen_project")")"
    if [[ ! -f "$CODEGEN_PROJECT" ]]; then
        echo "[ERROR] DataTable code generator project not found: $CODEGEN_PROJECT"
        exit 1
    fi
fi

process_bridge_files false

if [[ "$VALIDATE_ONLY" == "true" ]]; then
    if [[ "$CODEGEN_REQUIRED" == "true" ]]; then
        echo "[DataTable.CodeGen] Validating owned string constant outputs"
        dotnet run --project "$CODEGEN_PROJECT" -- \
            --config "$CONFIG" \
            --luban-conf "$SCRIPT_DIR/luban.conf" \
            --data-dir "$SCRIPT_DIR/Datas" \
            --target "$TARGET" \
            --code-output "$CODE_OUT" \
            --line-ending "$LINE_ENDING" \
            --validate-only
    fi

    echo "[Luban] Validation successful. No directories, generated files, or metadata were changed."
    echo "[Luban] Code output: $CODE_OUT"
    echo "[Luban] Data output: $DATA_OUT"
    exit 0
fi

mkdir -p "$CODE_OUT" "$DATA_OUT"

# ============================================================
#  Build Luban arguments
# ============================================================
LUBAN_ARGS=(
    -t "$TARGET"
    -c "$CODE_TARGET"
    -d "$DATA_TARGET"
    --conf "$SCRIPT_DIR/luban.conf"
    -x "lineEnding=$LINE_ENDING"
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

if [[ "$CODEGEN_REQUIRED" == "true" ]]; then
    if [[ -z "${codegen_project:-}" ]]; then
        echo "[ERROR] CodeGen output is configured or previously owned, but codegen_project is empty"
        exit 1
    fi

    if [[ -z "${CODEGEN_PROJECT:-}" ]]; then
        CODEGEN_PROJECT="$(canonical_path "$(resolve "$codegen_project")")"
    fi
    if [[ ! -f "$CODEGEN_PROJECT" ]]; then
        echo "[ERROR] DataTable code generator project not found: $CODEGEN_PROJECT"
        exit 1
    fi

    if ! command -v dotnet >/dev/null 2>&1; then
        echo "[ERROR] dotnet SDK is required to run DataTable string constant generation"
        exit 1
    fi

    echo "[DataTable.CodeGen] Reconciling owned string constant outputs"
    dotnet run --project "$CODEGEN_PROJECT" -- \
        --config "$CONFIG" \
        --luban-conf "$SCRIPT_DIR/luban.conf" \
        --data-dir "$SCRIPT_DIR/Datas" \
        --target "$TARGET" \
        --code-output "$CODE_OUT" \
        --line-ending "$LINE_ENDING"
fi

# Copy validated companion files only after Luban and CodeGen have succeeded.
# Luban cleanUpOutputDir may otherwise delete bridges copied before generation.
process_bridge_files true

cleanup_orphan_meta() {
    local root="$1"
    if [[ -z "$root" || ! -d "$root" ]]; then
        return 0
    fi

    local full_root
    full_root="$(canonical_path "$root")"
    validate_output_root "$full_root" "orphan-meta cleanup" || return 1

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
