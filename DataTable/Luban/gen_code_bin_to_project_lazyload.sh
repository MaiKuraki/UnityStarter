#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
TOOL_DIR="$SCRIPT_DIR/../../UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/Tools~/CodeGen"
TOOL_PROJECT="$TOOL_DIR/CycloneGames.DataTable.CodeGen.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[ERROR] The pinned .NET SDK is required to run the DataTable pipeline." >&2
    exit 1
fi

if [[ ! -f "$TOOL_PROJECT" ]]; then
    echo "[ERROR] DataTable pipeline project not found: $TOOL_PROJECT" >&2
    exit 1
fi

cd -- "$TOOL_DIR"
exec dotnet run --project "$TOOL_PROJECT" --configuration Release -- \
    pipeline "$@" --config "$SCRIPT_DIR/build_config.ini"
