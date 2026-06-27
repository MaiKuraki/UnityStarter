# DataTable CodeGen

English | [简体中文](./README.SCH.md)

`CycloneGames.DataTable.CodeGen` is a cross-platform post-generation tool for the DataTable/Luban pipeline. It reads Luban schema metadata and Excel workbooks, then generates additional C# helper code that can be referenced by gameplay code.

The tool currently generates string constant classes. It is intentionally implemented with the .NET standard library only: no PowerShell runtime, no Python, and no extra NuGet packages. The `.csproj` is source code and should be committed. Local `bin/` and `obj/` folders are build caches and should be ignored.

## Configuration

Configure the tool in the DataTable workspace `build_config.ini`:

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

| Key | Purpose |
| --- | --- |
| `codegen_project` | Path to the CodeGen `.csproj`, resolved relative to the DataTable directory. |
| `string_constant_tables` | Luban table `full_name` values to generate, separated by `,` or `;`. |
| `string_constant_value_column` | Column that stores the generated constant value. |
| `string_constant_comment_column` | Column used for XML documentation on each generated constant. Leave empty to skip comments. |
| `string_constant_enabled_column` | Rows with `0`, `false`, or `no` are skipped. |
| `string_constant_scope_column` | Optional grouping column. Each scope generates a separate class. |
| `string_constant_generated_comment_language` | Generated header language. Supported values: `en`, `zh-CN`, `zh`, `sch`, `cn`. Default is `en`. |

## Naming

The generated namespace is inferred from the Luban target `topModule` and the table `full_name` in `__tables__.xlsx`. The class name is inferred from the table name plus the optional scope.

```text
GameplayTags.TbGameplayTagDefinition + scope=Ability
=> UnityStarter.GameConfig.GameplayTags.GameplayTagAbilityNames
```

Constant names remove the matching scope segment from the value path:

```text
Sample.Ability.Movement.Dash + scope=Ability
=> GameplayTagAbilityNames.MOVEMENT_DASH
```

If two different scopes produce the same class name, generation fails with a clear error. Rename one scope or split the data into separate tables.

## Validation

Run the normal DataTable build:

```bat
DataTable\gen_code_bin_to_project_lazyload.bat --no-pause
```

or:

```bash
./DataTable/gen_code_bin_to_project_lazyload.sh
```

On success, the configured Luban C# output directory contains the generated `*Names.cs` files.
