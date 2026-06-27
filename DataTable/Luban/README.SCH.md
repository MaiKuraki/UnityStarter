# UnityStarter DataTable 工作区

[English](./README.md) | 简体中文

此目录是 Luban + `CycloneGames.DataTable` 工作流的 starter 模板。它包含跨平台构建脚本和可配置的 `build_config.ini`，但不包含具体产品的 Excel 表。新工程需要在 `DataTable/Datas/` 下添加自己的 schema 和数据 workbook。

## 目录结构

```text
DataTable/
  build_config.ini
  luban.conf
  gen_code_bin_to_project_lazyload.bat
  gen_code_bin_to_project_lazyload.sh
  Datas/                 # 在这里添加 __tables__.xlsx、__beans__.xlsx、__enums__.xlsx 和业务表。
  Defines/               # 可选 Luban define workbook。
  Generated/             # 可选 server/all 输出目录。
```

默认 client 输出路径：

- C# 代码：`UnityStarter/Assets/UnityStarter/Scripts/Generated/DataTable/`
- 二进制数据：`UnityStarter/Assets/StreamingAssets/DataTable/`

如果项目目录不同，修改 `build_config.ini` 即可。

## 前置条件

- 将 Luban 安装或复制到 `Tools/Luban/Luban.dll`，或修改 `build_config.ini` 的 `luban_dll`。
- 如果启用可选的 `CycloneGames.DataTable.CodeGen` 后处理，需要安装 .NET SDK。
- 真实构建前，需要在 `DataTable/Datas/` 下添加 Luban schema workbook。

## 命令

Windows:

```bat
DataTable\gen_code_bin_to_project_lazyload.bat --no-pause
```

macOS/Linux:

```bash
./DataTable/gen_code_bin_to_project_lazyload.sh
```

可选覆写：

```bash
./DataTable/gen_code_bin_to_project_lazyload.sh -t client -c cs-bin -d bin
```

## CodeGen

`string_constant_tables` 默认留空。需要从一个或多个 Luban 表生成字符串常量时，配置：

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

`string_constant_scope_column` 会把一张表拆成多个生成类，避免单个生成 API 文件过长。UnityStarter 派生项目默认应保持 `string_constant_generated_comment_language=en`，除非下游产品明确需要其他生成文件头语言。

## 验证

1. 运行 `DataTable\gen_code_bin_to_project_lazyload.bat --no-pause --help` 检查 Windows 脚本解析。
2. 如果环境有 Bash，运行 `bash -n DataTable/gen_code_bin_to_project_lazyload.sh` 检查 shell 语法。
3. 添加真实 Luban workbook 后，执行正常生成，并确认 Unity 导入生成 C# 后没有编译错误。
