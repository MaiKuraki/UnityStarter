# CycloneGames.GameplayTags.DataTable

[English](./README.md) | 简体中文

`CycloneGames.GameplayTags.DataTable` 是一个可选集成包，用于把 `CycloneGames.GameplayTags` 与 `CycloneGames.DataTable`、Luban 生成的配置行连接起来。适用场景是：项目的 gameplay tag catalog、ability definitions、effect definitions 或 tag references 来自表格生成数据，而不是只通过 Unity `ScriptableObject` 资产维护。

本文档说明开发者如何安装该集成包、在启动阶段注册生成标签数据、把生成的 tag name 转换为运行时容器，并验证接入结果。基础 `CycloneGames.GameplayTags` 包不依赖 DataTable，因此不使用 DataTable 的项目可以单独引入 GameplayTags，不会因为 missing assembly reference 或 ProjectSettings scripting define symbols 产生额外编译风险。

## 包结构

```text
CycloneGames.GameplayTags.DataTable/
  Runtime/
    CycloneGames.GameplayTags.DataTable.asmdef
    GameplayTagDataTableSource.cs
    GameplayTagDataTableReferenceSource.cs
  Tests/Editor/
    CycloneGames.GameplayTags.DataTable.Tests.Editor.asmdef
    GameplayTagsDataTableIntegrationTests.cs
  README.md
  README.SCH.md
  package.json
```

## 程序集边界

| Assembly | 职责 | Unity 依赖 | Auto-referenced |
| --- | --- | --- | --- |
| `CycloneGames.GameplayTags.DataTable` | 从 DataTable 行到 GameplayTags 注册源的纯 C# 桥接 | 无 | 否 |
| `CycloneGames.GameplayTags.DataTable.Tests.Editor` | 覆盖标签目录行与生成能力行 tag 引用的 EditMode 测试 | Editor test runner | 否 |

Runtime assembly 引用：

- `CycloneGames.GameplayTags.Core`
- `CycloneGames.DataTable.Core`

构建启动 adapter 或生成行 composition 代码的业务程序集应显式引用 `CycloneGames.GameplayTags.DataTable`。该程序集不自动引用，是为了避免只需要基础标签运行时的项目被静默暴露 DataTable API。

## 安装模型

UPM 分发时安装：

```text
com.cyclone-games.gameplay-tags.data-table
```

它的 `package.json` 声明了对 `com.cyclone-games.gameplay-tags` 和 `com.cyclone-games.data-table` 的依赖。

Assets 直放分发时，仅在项目同时包含以下目录时引入本文件夹：

```text
UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags/
UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/
```

不要通过 ProjectSettings scripting define symbols 启用该包。包是否存在就是 composition 边界。

## 核心类型

### `GameplayTagDataTableSource<TRow>`

当一条生成行代表一个 gameplay tag 定义时，使用该 source。

```csharp
DataTable<TagRow> tagTable = DataTableRegistry.Get<DataTable<TagRow>>();

GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableSource<TagRow>(
    "Design.GameplayTags",
    tagTable,
    static row => row.Name,
    static row => row.Comment,
    static row => row.Flags,
    static row => row.Enabled));

GameplayTagManager.InitializeIfNeeded();
```

推荐字段：

| 字段 | 用途 |
| --- | --- |
| `Id` | DataTable 主键，不是 GameplayTags stable ID。 |
| `Name` | 完整 tag 名称，例如 `Ability.Fireball`。 |
| `Comment` | 展示在校验工具和 Editor 工具中的配置说明。 |
| `Flags` | 可选 `GameplayTagFlags` 值。 |
| `Enabled` | 可选的灰度、分阶段内容或废弃开关。 |
| `Owner` | 可选负责人或团队元数据，用于项目校验工具。 |
| `Deprecated` / `RedirectTo` | 可选迁移元数据，由项目侧校验工具处理。 |

### `GameplayTagDataTableReferenceSource<TRow>`

当 ability、effect、item、quest 或其他生成行引用 tag name，但本身不是权威 tag catalog 时，使用该 source。

```csharp
DataTable<AbilityRow> abilityTable = DataTableRegistry.Get<DataTable<AbilityRow>>();

GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityRow>(
    "Design.Abilities",
    abilityTable,
    static row => row.AbilityTags,
    static row => row.ActivationRequiredTags,
    static row => row.ActivationBlockedTags,
    static row => row.ActivationOwnedTags));
```

这适合 `CycloneGames.GameplayAbilities` 中 ability/effect 由生成行直接构建 runtime definition，而不是先转成中间 `ScriptableObject` 资产的项目。

## 启动顺序

建议使用确定性启动顺序：

1. 加载 Luban bytes，并把生成表注册到 `DataTableRegistry`。
2. 为权威 tag catalog 注册 `GameplayTagDataTableSource<TRow>`。
3. 可选地为生成的 ability/effect/item 行注册 `GameplayTagDataTableReferenceSource<TRow>`。
4. 调用 `GameplayTagManager.InitializeIfNeeded()`。
5. 从生成行构建 GameplayAbilities、GameplayEffects 或 gameplay definitions。
6. 在 definition 构建阶段把 tag-name 字段转换为 `GameplayTagContainer` 或 `GameplayTagRequirements`。

转换示例：

```csharp
GameplayTagContainer abilityTags = GameplayTagContainerNameExtensions.FromTagNames(row.AbilityTags);
GameplayTagRequirements activationRequirements = GameplayTagContainerNameExtensions.CreateRequirementsFromTagNames(
    row.ActivationBlockedTags,
    row.ActivationRequiredTags);
```

默认转换模式是严格的。未知或空 tag name 会抛出异常，让错误设计数据在启动或校验阶段失败，而不是在战斗热路径中失败。

## 生产建议

- 稳定 live-service 项目建议保留独立 GameplayTag catalog 表作为权威来源。
- 除非项目明确希望被引用 tag 自动注册，否则 ability/effect 行中的引用更适合作为校验输入。
- 生产客户端尽量在可行时把静态 tag manifest 烘焙为 `GameplayTags.bytes`。
- 热更新或服务器配置流程如果在运行时改变 tags，应比较 `GameplayTagManager.CurrentManifestHash`，并在应用新 gameplay state 前重新同步已复制的 tag containers。
- 不要持久化 runtime indices。应保存 tag names、stable IDs 或 GameplayTags 网络序列化 payload。

## 持久化

该包不写入文件、资产、`EditorPrefs`、`PlayerPrefs`、registry 或隐藏全局偏好。

它只读取宿主项目已经加载的 DataTable 行，并把 tags 注册到内存中的 `GameplayTagManager` snapshot。持久化、缓存失效、schema 迁移和生成表存储仍由 `CycloneGames.DataTable` 或宿主项目负责。

## 验证

Unity 重新生成工程文件后，可运行 CLI 检查：

```bash
dotnet build CycloneGames.GameplayTags.DataTable.csproj -v:minimal
dotnet build CycloneGames.GameplayTags.DataTable.Tests.Editor.csproj -v:minimal
```

推荐 Unity Editor 验证：

1. 从 `<repo-root>/UnityStarter` 打开 Unity 项目。
2. 确认 `CycloneGames.GameplayTags`、`CycloneGames.DataTable` 和 `CycloneGames.GameplayTags.DataTable` 都存在。
3. 运行 `CycloneGames.GameplayTags.DataTable.Tests.Editor` 下的 EditMode tests。
4. 注册一个小型生成风格 tag catalog 表，验证 `GameplayTagManager.RequestTag(...)` 可以解析生成的名称。
5. 注册一个生成 ability/effect 引用源，验证 `GameplayTagContainerNameExtensions` 创建的容器与行数据一致。
