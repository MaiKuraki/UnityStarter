# Luban DataTable Codegen

English | [简体中文](./README.SCH.md)

This directory contains the Luban-based code generation pipeline for `CycloneGames.DataTable`. Given a set of Excel workbooks and a schema definition, it produces C# table accessors and binary payloads for Unity client, server, and combined targets.

Every generated file is a build product. The workbooks in `Datas/` and `luban.conf` are the authoritative inputs — change those, re-run the pipeline, and the output is deterministic.

## Table of Contents

- [Overview](#overview)
- [Pipeline](#pipeline)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Command Reference](#command-reference)
- [Output Safety](#output-safety)
- [CI Usage](#ci-usage)
- [Troubleshooting](#troubleshooting)

## Overview

The pipeline has three output targets — `client` (Unity-bound C# + StreamingAssets), `server` (standalone C# + data), and `all` (both groups combined). Each target gets its own output root. Two guarded wrapper scripts (`.bat` for Windows, `.sh` for macOS/Linux) validate paths, acquire a filesystem writer lock, and reject unsafe configuration before Luban ever starts. Neither wrapper ships in a Unity Player.

Once the base pipeline works, two optional features unlock:

- **C# constant generation** (`CycloneGames.DataTable.CodeGen`) — reads selected table columns and generates strongly-named C# constants, so gameplay code references `GameplayTags.Ability_Fireball` instead of `"Ability.Fireball"`.
- **Custom templates and bridge files** — copies bounded template output alongside generated code, with ownership tracking to prevent accidental overwrites.

### Directory layout

```text
DataTable/Luban/
  build_config.ini                        # Paths and build settings
  luban.conf                              # Content groups, schema sources, output targets
  DataTableBuildSafety.ps1                # PowerShell safety helper
  gen_code_bin_to_project_lazyload.bat    # Windows entry point
  gen_code_bin_to_project_lazyload.sh     # macOS/Linux entry point
  Datas/                                  # Schema and business workbooks
    __tables__.xlsx
    __beans__.xlsx
    __enums__.xlsx
  Defines/                                # Optional define workbooks
  Generated/                              # Server and combined outputs
    Server/Code/    Server/Data/
    All/Code/       All/Data/
```

Default client output paths:

| Output | Path |
| --- | --- |
| Generated C# | `UnityStarter/Assets/UnityStarter/Scripts/Generated/DataTable/` |
| Binary payloads | `UnityStarter/Assets/StreamingAssets/DataTable/` |

Server and combined outputs stay under `DataTable/Luban/Generated/`. The wrappers only accept these approved roots or their children — editing a path in `build_config.ini` does not expand the approved set.

## Pipeline

```mermaid
flowchart LR
    A[Workbooks and schema] --> B[build_config.ini and luban.conf]
    B --> C[Guarded wrapper (.bat / .sh)]
    C --> D[Validate tools, paths, lock, optional inputs]
    D --> E[Luban generates C# and binaries]
    E --> F[Optional CodeGen transaction]
    F --> G[Optional bridge publication]
    G --> H[Unity import, compile, validate]
```

The wrapper bails at the first failed stage. A successful run means every enabled stage completed — it does not replace Unity compilation or runtime data validation.

## Quick Start

All commands from `<repo-root>`.

### Prerequisites

- Luban at `Tools/DataTable/Luban/Luban.dll` (Windows can also use `Luban.exe` alongside).
- `dotnet` runtime for `Luban.dll`. .NET 8 SDK if CodeGen is enabled.
- Schema workbooks in `DataTable/Luban/Datas/`: `__tables__.xlsx`, `__beans__.xlsx`, `__enums__.xlsx`.
- Every business workbook referenced by `__tables__.xlsx`.

### 1. Validate without generating

Windows:
```bat
DataTable\Luban\gen_code_bin_to_project_lazyload.bat --no-pause --validate-only
```

macOS/Linux:
```bash
bash DataTable/Luban/gen_code_bin_to_project_lazyload.sh --validate-only
```

Checks the target, required tools and workbooks, output roots, writer lock, and optional configs — nothing gets generated or published.

### 2. Generate client code and data

Windows:
```bat
DataTable\Luban\gen_code_bin_to_project_lazyload.bat --no-pause
```

macOS/Linux:
```bash
bash DataTable/Luban/gen_code_bin_to_project_lazyload.sh
```

Default: `client` target, `cs-bin` code, `bin` data.

### 3. Verify in Unity

1. Open Unity, wait for import and compilation.
2. Confirm generated C# is under the configured code root.
3. Confirm payloads are under the configured data root.
4. Run DataTable and product-integration EditMode tests.
5. Load a representative payload through the product composition root.

Generated files are not hand-editable. Change the workbook, schema, template, or config and regenerate.

## Configuration

### luban.conf — targets and groups

```json
{
  "groups": [
    { "names": ["c"], "default": true },
    { "names": ["s"], "default": false }
  ],
  "schemaFiles": [
    { "fileName": "Defines", "type": "" },
    { "fileName": "Datas/__tables__.xlsx", "type": "table" },
    { "fileName": "Datas/__beans__.xlsx", "type": "bean" },
    { "fileName": "Datas/__enums__.xlsx", "type": "enum" }
  ],
  "dataDir": "Datas",
  "targets": [
    { "name": "client", "manager": "Tables", "groups": ["c"], "topModule": "UnityStarter.GameConfig" },
    { "name": "server", "manager": "Tables", "groups": ["s"], "topModule": "UnityStarter.GameConfig" },
    { "name": "all",    "manager": "Tables", "groups": ["c","s"], "topModule": "UnityStarter.GameConfig" }
  ]
}
```

| Field | What it does |
| --- | --- |
| `groups` | Content visibility groups — workbook rows are tagged with a group |
| `schemaFiles` | Define, table, bean, and enum schema sources |
| `dataDir` | Where business workbooks live |
| `targets[].name` | `client`, `server`, or `all` — matches wrapper `-t` value |
| `targets[].manager` | Name of the generated table-set manager class |
| `targets[].groups` | Which groups this target includes |
| `targets[].topModule` | Root namespace for generated code |

Changing `manager`, `topModule`, table names, or groups changes the generated API. Regenerate every affected target and update all consumers in the same commit.

### build_config.ini — paths and settings

Paths are relative to `DataTable/Luban/`.

```ini
[paths]
luban_dll=../../Tools/DataTable/Luban/Luban.dll
client_code_out=../../UnityStarter/Assets/UnityStarter/Scripts/Generated/DataTable/
client_data_out=../../UnityStarter/Assets/StreamingAssets/DataTable/
server_code_out=../../DataTable/Luban/Generated/Server/Code/
server_data_out=../../DataTable/Luban/Generated/Server/Data/
all_code_out=../../DataTable/Luban/Generated/All/Code/
all_data_out=../../DataTable/Luban/Generated/All/Data/

[templates]
custom_template_dir=
bridge_files=

[build]
target=client
code_target=cs-bin
data_target=bin
clean_output=false
clean_orphan_meta=false
line_ending=crlf

[codegen]
codegen_project=../../UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/Tools~/CodeGen/CycloneGames.DataTable.CodeGen.csproj
string_constant_tables=
string_constant_value_column=name
string_constant_comment_column=comment
string_constant_enabled_column=enabled
string_constant_scope_column=scope
string_constant_generated_comment_language=en
```

| Key | What it does |
| --- | --- |
| `luban_dll` | Where Luban lives |
| `client_*_out` / `server_*_out` / `all_*_out` | Code and data roots per target |
| `target` | Default: `client`, `server`, or `all` |
| `code_target` / `data_target` | Luban generator modes |
| `clean_output` | Tells Luban to clean before generating (`true`/`false`/`1`/`0`) |
| `clean_orphan_meta` | Remove orphan Unity `.meta` files after generation |
| `line_ending` | `crlf` or `lf` for generated text files |
| `custom_template_dir` | Template directory under `DataTable/Luban/` |
| `bridge_files` | Comma-separated files below the template directory |
| `codegen_project` | .NET project for optional CodeGen |
| `string_constant_*` | Table and column mapping for constant generation |

### Target outputs

| Target | Groups | Where output goes |
| --- | --- | --- |
| `client` | `c` | Unity generated-code and StreamingAssets |
| `server` | `s` | `DataTable/Luban/Generated/Server/` |
| `all` | `c` and `s` | `DataTable/Luban/Generated/All/` |

Keep these separate. A server should compile its own generated C# and load payloads through its own file services — not depend on Unity asset APIs.

## Command Reference

All commands from `<repo-root>`.

### Options

| Option | Meaning |
| --- | --- |
| `-t client\|server\|all` | Override configured target |
| `-c <code-target>` | Override Luban code generator |
| `-d <data-target>` | Override Luban data generator |
| `--validate-only` | Safety check only — no generation, no cleanup |
| `-h`, `--help` | Print help |

```bash
bash DataTable/Luban/gen_code_bin_to_project_lazyload.sh -t server -c cs-bin -d bin
```

The Windows `.bat` also accepts `--pause` / `--no-pause`. Set `CI=1` or `CYCLONE_DATATABLE_NO_PAUSE=1` to skip interactive pauses; `CYCLONE_DATATABLE_PAUSE=1` forces them. Unknown options or missing values exit with a non-zero code.

## Output Safety

### Cleanup is locked by default

Normal runs keep destructive cleanup off:
```ini
clean_output=false
clean_orphan_meta=false
```

To authorize destructive cleaning in a reviewed, backed-up run:
```text
CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN=1
```

This is a gate, not a safety net. Back up inputs and verify output roots before anyone sets it.

### Filesystem writer lock

Both wrappers acquire `DataTable/Luban/.cyclonegames-datatable-writer.lock/owner.txt` before generation. A lock collision fails closed — the wrappers won't guess whether a stale lock is safe to remove. The default Unity Editor runner goes through the guarded wrapper, so it participates in the same lock. Raw `Luban.dll` or CodeGen invocations bypass the lock — don't run them concurrently against the same output roots.

### Custom templates and bridge files

`custom_template_dir` must be a physical child of `DataTable/Luban/` — no symlinks, junctions, or external paths.

`bridge_files` is a comma-separated list of relative paths below that directory. Each file:
- Uses portable forward-slash paths.
- Gets copied to the code output root by basename.
- Won't overwrite unless the same bytes are already there.
- Caps at 16 MiB per file, 256 files, 64 MiB total.

### String constant generation

Pick a Luban table and column:
```ini
string_constant_tables=GameplayTags.TbGameplayTagDefinition
```

The wrapper runs CodeGen after Luban succeeds when `string_constant_tables` is non-empty (or when a manifest from a previous run exists for reconciliation). Details: [CodeGen README](../../UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/Tools~/CodeGen/README.md).

### Runtime loading

Generated data reaches gameplay code through your composition root:
1. Pick a platform-appropriate byte loader.
2. Enforce `DataTableLoadLimits` before big allocations.
3. Validate `DataTableManifest` version, size, and SHA-256.
4. Build the Luban table set through the integration factory.
5. Validate required tables, row constraints, and cross-table refs.
6. Create a typed `DataTableCatalog` and publish it atomically.

Runtime concepts: [CycloneGames.DataTable README](../../UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/README.md). Tag integration: [GameplayTags.DataTable README](../../UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags.DataTable/README.md).

## CI Usage

A reproducible CI job looks like:

1. Clean checkout.
2. Install pinned .NET and Luban.
3. Confirm `Datas/`, `Defines/`, `luban.conf`, and `build_config.ini` are present.
4. `--validate-only`.
5. Generate with destructive settings off.
6. Compile generated C#.
7. Run DataTable and integration tests.
8. Diff generated output against policy — fail on unexpected changes.
9. Archive logs and output hashes.

Windows CI:
```bat
set CI=1
DataTable\Luban\gen_code_bin_to_project_lazyload.bat --no-pause --validate-only
if errorlevel 1 exit /b %errorlevel%
DataTable\Luban\gen_code_bin_to_project_lazyload.bat --no-pause
```

macOS/Linux CI:
```bash
bash DataTable/Luban/gen_code_bin_to_project_lazyload.sh --validate-only
bash DataTable/Luban/gen_code_bin_to_project_lazyload.sh
```

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Luban.dll` not found | Install to configured path; verify `dotnet` |
| Schema workbook missing | Add `__tables__.xlsx`, `__beans__.xlsx`, `__enums__.xlsx` to `Datas/` |
| Target rejected | Use `client`, `server`, or `all`; configure both output roots |
| Output root rejected | Use an approved root; keep code/data separate; remove symlinks |
| Cleanup refused | Keep defaults or set the env gate for a backed-up run |
| Writer lock exists | Confirm all writers stopped; inspect `owner.txt`; remove lock dir |
| Bridge publish fails | Check template containment, names, size limits, collisions |
| CodeGen doesn't run | Set `string_constant_tables` or check manifest exists |
| Unity compile fails | Check namespaces, backend refs, duplicate output, schema sync |
| Runtime load fails | Check manifest version, payload hash, load limits, table list |