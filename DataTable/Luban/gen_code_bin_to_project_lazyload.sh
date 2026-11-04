#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
BUILD_CONFIG="$SCRIPT_DIR/build_config.ini"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[ERROR] .NET SDK 8.0 or newer is required to run the DataTable pipeline." >&2
    exit 1
fi

if [[ ! -f "$BUILD_CONFIG" ]]; then
    echo "[ERROR] Build configuration not found: $BUILD_CONFIG" >&2
    exit 1
fi

# Read codegen_project from the configuration so the path has a single owner.
TOOL_PROJECT="$(sed -n 's/^[[:space:]]*codegen_project[[:space:]]*=[[:space:]]*//p' "$BUILD_CONFIG" | head -n 1 | tr -d '\r')"
if [[ -z "$TOOL_PROJECT" ]]; then
    echo "[ERROR] codegen_project is missing from $BUILD_CONFIG" >&2
    exit 1
fi

case "$TOOL_PROJECT" in
    /*|[A-Za-z]:/*) ;;
    *) TOOL_PROJECT="$SCRIPT_DIR/$TOOL_PROJECT" ;;
esac

if [[ ! -f "$TOOL_PROJECT" ]]; then
    echo "[ERROR] DataTable pipeline project not found: $TOOL_PROJECT" >&2
    exit 1
fi

# The SDK policy lives in the CodeGen folder, so run dotnet from there.
cd -- "$(dirname -- "$TOOL_PROJECT")"

exec dotnet run --project "$TOOL_PROJECT" --configuration Release --verbosity quiet -- \
    pipeline "$@" --config "$BUILD_CONFIG"
