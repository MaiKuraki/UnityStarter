# DataTable CodeGen

[English](./README.md) | 简体中文

`CycloneGames.DataTable.CodeGen` 是 DataTable/Luban 生成流程的跨平台后处理工具。它读取 Luban schema 元数据和 Excel workbook，在 Luban 生成代码之后继续生成 gameplay 代码可直接引用的 C# 辅助代码。

当前工具用于生成字符串常量类。实现上只依赖 .NET 标准库，不依赖 PowerShell、Python 或额外 NuGet 包。`.csproj` 是工具源码的一部分，需要提交；本地 `bin/` 和 `obj/` 是构建缓存，应通过 `.gitignore` 忽略。

## 配置

在 DataTable 工作区的 `build_config.ini` 中配置：

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

| 配置项 | 用途 |
| --- | --- |
| `codegen_project` | CodeGen `.csproj` 路径，按 DataTable 目录解析。 |
| `string_constant_tables` | 需要生成的 Luban 表 `full_name`，多个值用 `,` 或 `;` 分隔。 |
| `string_constant_value_column` | 生成常量值来源列。 |
| `string_constant_comment_column` | 生成常量 XML 文档注释来源列。留空则不生成逐项注释。 |
| `string_constant_enabled_column` | 行值为 `0`、`false` 或 `no` 时跳过。 |
| `string_constant_scope_column` | 可选分组列。每个 scope 生成一个独立类。 |
| `string_constant_generated_comment_language` | 生成文件头注释语言。支持 `en`、`zh-CN`、`zh`、`sch`、`cn`，默认 `en`。 |

## 命名规则

生成命名空间由 Luban target 的 `topModule` 和 `__tables__.xlsx` 中的表 `full_name` 推导。类名由表名和可选 scope 推导。

```text
GameplayTags.TbGameplayTagDefinition + scope=Ability
=> UnityStarter.GameConfig.GameplayTags.GameplayTagAbilityNames
```

常量名会移除 value 路径中匹配的 scope 片段：

```text
Sample.Ability.Movement.Dash + scope=Ability
=> GameplayTagAbilityNames.MOVEMENT_DASH
```

如果两个不同 scope 会生成同一个类名，工具会明确报错。此时应重命名其中一个 scope，或把数据拆到不同表。

## 验证

运行常规 DataTable 生成：

```bat
DataTable\gen_code_bin_to_project_lazyload.bat --no-pause
```

或：

```bash
./DataTable/gen_code_bin_to_project_lazyload.sh
```

成功后，应能在 Luban C# 输出目录中看到生成的 `*Names.cs` 文件。
