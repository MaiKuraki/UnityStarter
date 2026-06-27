# UnityStarter DataTable Workspace

English | [简体中文](./README.SCH.md)

This folder is a starter template for the Luban + `CycloneGames.DataTable` workflow. It contains cross-platform build scripts and a configurable `build_config.ini`, but it does not include product-specific Excel workbooks. Add the schema and data files required by your project under `DataTable/Datas/`.

## Layout

```text
DataTable/
  build_config.ini
  luban.conf
  gen_code_bin_to_project_lazyload.bat
  gen_code_bin_to_project_lazyload.sh
  Datas/                 # Add __tables__.xlsx, __beans__.xlsx, __enums__.xlsx, and data workbooks here.
  Defines/               # Optional Luban define workbooks.
  Generated/             # Optional server/all outputs.
```

The default client output paths are:

- C# code: `UnityStarter/Assets/UnityStarter/Scripts/Generated/DataTable/`
- Binary data: `UnityStarter/Assets/StreamingAssets/DataTable/`

Edit `build_config.ini` when a project uses a different folder layout.

## Prerequisites

- Install or copy Luban to `Tools/Luban/Luban.dll`, or change `luban_dll` in `build_config.ini`.
- Install the .NET SDK when using the optional `CycloneGames.DataTable.CodeGen` post-process.
- Add your Luban schema workbooks under `DataTable/Datas/` before running a real build.

## Commands

Windows:

```bat
DataTable\gen_code_bin_to_project_lazyload.bat --no-pause
```

macOS/Linux:

```bash
./DataTable/gen_code_bin_to_project_lazyload.sh
```

Optional overrides:

```bash
./DataTable/gen_code_bin_to_project_lazyload.sh -t client -c cs-bin -d bin
```

## CodeGen

`string_constant_tables` is empty by default. To generate string constants from one or more Luban tables, configure:

```ini
[codegen]
codegen_project=../UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/Tools~/CodeGen/CycloneGames.DataTable.CodeGen.csproj
string_constant_tables=GameplayTags.TbGameplayTagDefinition
string_constant_value_column=name
string_constant_comment_column=comment
string_constant_enabled_column=enabled
string_constant_scope_column=scope
string_constant_generated_comment_language=en
```

`string_constant_scope_column` splits a table into multiple generated classes, which keeps large generated APIs easier to browse. `string_constant_generated_comment_language` should stay `en` for UnityStarter-derived projects unless a downstream product deliberately wants another generated header language.

## Validation

1. Run `DataTable\gen_code_bin_to_project_lazyload.bat --no-pause --help` to check Windows script parsing.
2. Run `bash -n DataTable/gen_code_bin_to_project_lazyload.sh` to check shell syntax when Bash is available.
3. After adding real Luban workbooks, run the normal build and confirm Unity imports generated C# without compile errors.
