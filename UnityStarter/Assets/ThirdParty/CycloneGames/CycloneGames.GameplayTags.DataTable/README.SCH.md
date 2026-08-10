# CycloneGames.GameplayTags.DataTable

[English](./README.md) | 简体中文

CycloneGames.GameplayTags.DataTable 将 DataTable row 桥接到 GameplayTags 注册管线。定义 row 提供权威标签目录，引用 row 声明技能、效果、物品等在用的标签名。纯 C#——文件 I/O、workbook 生成和字节解码归产品 composition root 管。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速上手](#快速上手)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [进阶主题](#进阶主题)
- [性能与内存](#性能与内存)
- [故障排查](#故障排查)

## 概述

定义 row 提供标签名称、描述、flags 和启用状态的权威集合。引用 row 报告哪些标签名称被玩法配置消费。两个 source adapter 都消费 `IDataTableRows<TRow>` 或 `IReadOnlyList<TRow>`，不依赖特定序列化器或生成表类型。

当玩法配置位于生成式数据表（包括 Luban 生成的 Excel 数据）中，且希望同一标签词汇表同时驱动编辑和运行时使用本模块。

### 主要特性

- **定义 source** 将表格行映射为权威标签注册，包含名称、描述、flags 和启用状态。
- **引用 source** 从玩法行中的一个或多个字段枚举标签名称。
- **Luban/Excel 集成**，支持生成行类型的显式 key selector。
- **DataTableCatalog** 组合与实例级 `DataTableStore` generation 发布。
- **协调重载**，失败时支持回滚。

## 架构

| 程序集 | 职责 |
| --- | --- |
| `CycloneGames.GameplayTags.DataTable` | Row-to-tag adapter。`autoReferenced: false`。 |
| `CycloneGames.DataTable.Core`（依赖） | 强类型不可变 table 与 catalog 发布 |
| `CycloneGames.GameplayTags.Core`（依赖） | Tag 注册、校验、snapshot 与查询 |
| `CycloneGames.Logging.Core`（测试 assembly 依赖） | 为包测试提供结构化失败捕获；生产 integration assembly 不引用它 |

添加显式 asmdef 引用：

```json
{
  "references": [
    "CycloneGames.DataTable.Core",
    "CycloneGames.GameplayTags.Core",
    "CycloneGames.GameplayTags.DataTable"
  ]
}
```

```csharp
using CycloneGames.DataTable;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Integrations.DataTable;
```

## 快速上手

### 注册权威标签表

```csharp
// 1. 定义 row model
public sealed class GameplayTagRow : IDataRow
{
    public int Id { get; }
    public string Name { get; }
    public string Description { get; }
    public GameplayTagFlags Flags { get; }
    public bool Enabled { get; }

    public GameplayTagRow(int id, string name, string description,
        GameplayTagFlags flags, bool enabled)
    {
        Id = id; Name = name; Description = description;
        Flags = flags; Enabled = enabled;
    }
}

// 2. 构建 table
var table = new DataTable<GameplayTagRow>(new[]
{
    new GameplayTagRow(1, "Ability.Fireball", "Casts a fireball.",
        GameplayTagFlags.None, true),
    new GameplayTagRow(2, "Effect.Burning", "Deals damage over time.",
        GameplayTagFlags.None, true),
});

// 3. 创建并注册 source
var source = new GameplayTagDataTableSource<GameplayTagRow>(
    "00.GameplayTags.Definitions",
    table,
    static row => row.Name,
    static row => row.Description,
    static row => row.Flags,
    static row => row.Enabled);

GameplayTagRuntimePlatform.RegisterProjectTagSource(source);

// 4. 初始化并查询
GameplayTagManager.InitializeIfNeeded();

GameplayTag fireball = GameplayTagManager.RequestTag("Ability.Fireball");
```

所有启动 source 必须在首次调用 `InitializeIfNeeded()` 前注册。

## 核心概念

| 类型 | 职责 |
| --- | --- |
| `DataTable<TKey, TRow>` | 结构不可变的 row sequence 与 key index |
| `GameplayTagDataTableSource<TRow>` | 将一条定义 row 映射为一条权威 tag registration |
| `GameplayTagDataTableReferenceSource<TRow>` | 枚举每条玩法 row 中一个或多个字段引用的 tag name |
| `GameplayTagRuntimePlatform` | 向 GameplayTags 注册具名 project source |
| `DataTableCatalog` | 将一组强类型 table 组成一个不可变 catalog |
| `DataTableStore` / `DataTableReader` | 可选地发布带版本的 Catalog generation，并把 consumer 固定到一个 snapshot |

定义 row 与引用 row 的职责不同。定义 row 持有标签元数据；引用 row 仅报告某个标签名称正在被使用。要求标签集合封闭的项目应在注册前校验每个引用都能在定义表中解析。

## 使用指南

### Luban/Excel 定义表

仓库 schema 生成 `UnityStarter.GameConfig.GameplayTags.GameplayTagDefinitionData`。其字段与 GameplayTags 的映射如下：

| Luban 字段 | 生成成员与类型 | Runtime 契约 |
| --- | --- | --- |
| `id` | `Id: int` | 稳定的表 key；不是标签 identity。 |
| `name` | `Name: string` | 层级式标签 identity。 |
| `comment` | `Comment: string` | 传给 GameplayTags 的开发者说明。 |
| `flags` | `Flags: string` | 精确符号值：`None` 或 `HideInEditor`。 |
| `enabled` | `Enabled: bool` | 控制是否参与注册。 |
| `scope` | `Scope: string` | 构建期分组元数据；不会注册为标签元数据。 |

父标签不需要单独 row。注册 `Ability.Elemental.Fire` 时会创建缺失的 `Ability` 和 `Ability.Elemental` 父节点，这些节点会占用 registry capacity。

```csharp
using System;

using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Integrations.DataTable;

using UnityStarter.GameConfig;
using UnityStarter.GameConfig.GameplayTags;

internal static class GameplayTagDataTableComposition
{
    public static void Register(Tables tables)
    {
        if (tables == null)
            throw new ArgumentNullException(nameof(tables));

        var rows = tables.TbGameplayTagDefinition.DataList;
        for (int i = 0; i < rows.Count; i++)
        {
            GameplayTagDefinitionData row = rows[i];
            if (row == null)
                throw new InvalidOperationException($"Gameplay tag row {i} is null.");

            ParseFlags(row.Flags, row.Name);
        }

        var source = new GameplayTagDataTableSource<GameplayTagDefinitionData>(
            "00.GameplayTags.Definitions",
            rows,
            static row => row.Name,
            static row => row.Comment,
            static row => ParseFlags(row.Flags, row.Name),
            static row => row.Enabled);

        GameplayTagRuntimePlatform.RegisterProjectTagSource(source);
    }

    private static GameplayTagFlags ParseFlags(string value, string tagName)
    {
        switch (value)
        {
            case "None":
                return GameplayTagFlags.None;
            case "HideInEditor":
                return GameplayTagFlags.HideInEditor;
            default:
                throw new InvalidOperationException(
                    $"Gameplay tag '{tagName}' has unsupported flags '{value ?? "<null>"}'.");
        }
    }
}
```

在生成的 `Tables` 实例完成解码后、调用 `GameplayTagManager.InitializeIfNeeded()` 前执行 `Register`。生成 row 不可变，因此完成预检的值不会在校验与注册之间变化。

### 引用 source

```csharp
var abilityReferences = new GameplayTagDataTableReferenceSource<AbilityRow>(
    "10.GameplayTags.AbilityReferences",
    abilityTable,
    static row => row.AbilityTags,
    static row => row.RequiredTags,
    static row => row.BlockedTags);

GameplayTagRuntimePlatform.RegisterProjectTagSource(abilityReferences);
```

Null row 和被 `isEnabled` 拒绝的 row 会被跳过。Null accessor 或 null collection 会被跳过。每个非 null name 都会提交；重复名称解析为同一条 definition 但仍消耗 registration attempt。

### 校验引用与定义的匹配

```csharp
var definedNames = new HashSet<string>(StringComparer.Ordinal);
for (int i = 0; i < tagTable.All.Count; i++)
{
    if (tagTable.All[i].Enabled)
        definedNames.Add(tagTable.All[i].Name);
}

for (int i = 0; i < abilityTable.All.Count; i++)
{
    string[] names = abilityTable.All[i].AbilityTags;
    if (names == null) continue;
    for (int j = 0; j < names.Length; j++)
    {
        if (!definedNames.Contains(names[j]))
            throw new InvalidOperationException($"未定义的玩法标签: {names[j]}");
    }
}
```

该校验应在构建 candidate generation 时执行，而非在逐帧玩法路径中。

### Catalog 发布

```csharp
var catalogBuilder = new DataTableCatalogBuilder(limits, capacity: 2);
catalogBuilder.Add<IDataTableRows<GameplayTagRow>>(tagTable);
catalogBuilder.Add<IDataTableRows<AbilityRow>>(abilityTable);
DataTableCatalog catalog = catalogBuilder.Build();

GameplayTagManager.InitializeIfNeeded();
using var store = new DataTableStore(revisionSequenceFloor: 0);
using DataTableReader reader = store.RegisterReader();
using var candidate = new DataTableCandidate(
    catalog,
    new DataTableRevision(sequence: 1, id: contentSha256));

DataTablePublishResult result = store.TryPublish(candidate, expectedGeneration: 0);
if (!result.IsCommitted)
    throw new InvalidOperationException($"Catalog publication rejected: {result.Status}");

reader.Refresh(); // 只能在没有操作继续使用旧 snapshot 的安全点执行
```

### Source 顺序

Source 按 ordinal name 顺序处理。使用数字前缀：

| 前缀 | 职责 | 示例 |
| --- | --- | --- |
| `00` | 权威定义 | `00.GameplayTags.Definitions` |
| `10` | Ability 引用 | `10.GameplayTags.AbilityReferences` |
| `20` | Item/effect 引用 | `20.GameplayTags.ItemReferences` |

同一名称被多次注册时，保留第一条 definition 的 flags；后续非空 description 只填充原本为空的 description。

## 进阶主题

### 协调重载

修改 active source 前先构建并校验完整 candidate：

1. 把有界 payload 加载到 candidate 独占的 resource scope。
2. 解码 row 并校验 key、name、flags 和 reference。
3. 构建全部 candidate table 与 candidate catalog。
4. 基于 candidate table 创建 definition source 与 reference source。
5. 使用被替换 source 的稳定名称注册每个 candidate source。
6. 调用 `GameplayTagManager.ReloadTags()`。
7. 使用 candidate 工作开始前捕获的 DataTable generation 调用 `TryPublish`。
8. 发布被拒绝时，恢复上一组 tag source 并再次 reload tags；DataTable candidate 仍归调用方。
9. 发布提交成功时，在协调后的安全点刷新每个 DataTable reader。
10. 恢复 dependent consumer。最后一个 reader 离开后，Store 释放 retired backing owner。

Tag reload 失败时回滚：

```csharp
long expectedDataTableGeneration = _dataTableStore.Metadata.Generation;
IGameplayTagSource previousDefinitionSource = _activeDefinitionSource;
IGameplayTagSource nextDefinitionSource = BuildDefinitionSource(candidateCatalog);
using var tableCandidate = new DataTableCandidate(
    candidateCatalog,
    trustedRevision,
    candidateResourceOwner);

GameplayTagRuntimePlatform.RegisterProjectTagSource(nextDefinitionSource);
try
{
    GameplayTagManager.ReloadTags();
}
catch
{
    if (previousDefinitionSource != null)
        GameplayTagRuntimePlatform.RegisterProjectTagSource(previousDefinitionSource);
    else
        GameplayTagRuntimePlatform.UnregisterProjectTagSource(nextDefinitionSource.Name);
    throw;
}

DataTablePublishResult publish = _dataTableStore.TryPublish(
    tableCandidate,
    expectedDataTableGeneration);
if (!publish.IsCommitted)
{
    if (previousDefinitionSource != null)
        GameplayTagRuntimePlatform.RegisterProjectTagSource(previousDefinitionSource);
    else
        GameplayTagRuntimePlatform.UnregisterProjectTagSource(nextDefinitionSource.Name);

    GameplayTagManager.ReloadTags();
    HandleRejectedTableCandidate(publish);
    return;
}

RequestDataTableReaderRefreshAtSafePoint();
_activeDefinitionSource = nextDefinitionSource;
```

`GameplayTagManager.ReloadTags()` 与 `DataTableStore.TryPublish()` 仍是两个独立 publication seam，双方都无法让另一方原子化。Consumer 不能观察到不同 generation 的 tag 与 table 时，应在两个状态转换期间暂停 dependent consumer。把 `Superseded` 与 `NonMonotonicRevision` 当作正常拒绝状态，并在恢复 consumer 前还原上一组 tag source。`OutOfMemoryException` 等进程级 fatal exception 属于 fail-stop 条件，不是普通 rollback 分支。

### 所有权与生命周期

Adapter 会借用 row：

- `DataTable` 复制顶层输入数组但不深复制 row object 或 nested collection。
- `DataTableCatalog` 不持有或 Dispose table instance。
- Caller-owned `DataTableCandidate` 在发布提交前拥有可选 backing resource；发布被拒绝不会转移所有权。
- Candidate 提交后，其 owner 归 `DataTableStore`；每个 `DataTableReader` 会固定一个 generation，直到 Refresh 或 Dispose。

注册和 reload 期间保持 row、nested tag collection 和任何 backing resource 稳定。不得让借用的 `DataTableSnapshot` 跨越 `DataTableReader.Refresh()` 继续存活。最终 reader 状态转换可能在其调用线程触发退役，因此 thread-affine backing resource 需要 owner-thread disposal adapter。

## 性能与内存

Adapter registration 是冷路径线性扫描，不在 update loop 中调用 source registration。

为获得可预测的注册成本：

- 使用 static、non-capturing accessor。
- 让 generated row 暴露 array 或稳定的 indexed list。
- 注册前校验并去重大型 collection。
- 避免在 accessor 中使用 reflection、LINQ 和 iterator state machine。

Runtime tag query 读取不可变 GameplayTags snapshot，不会调用这些 adapter。Load、validation、source replacement、reload、catalog publication 与 retirement 应由唯一 composition owner 协调。

Runtime assembly 设置了 `noEngineReferences: true`。该集成不写入 workbook、generated source、payload、cache、asset 或 preferences。全部 generated artifact 由产品持有。

## 故障排查

| 现象 | 可能原因 | 解决方法 |
| --- | --- | --- |
| 无法请求某个 tag | Source 在初始化前未注册 | 确认 source 在 `InitializeIfNeeded()` 前已注册；检查 `isEnabled` 返回 `true` |
| 权威 flags 丢失 | Definition source 排在 reference source 之后 | 确保 `00` 前缀在 `10`/`20` 之前；校验 workbook `flags` 范围 |
| Play 期间 reload 仍保留已删除 tag | Runtime-index preservation policy | Authoring data 中删除的 tag 保持有效直到下一次 runtime reset |
| Generated row 没有实现 `IDataRow` | Luban 生成的 row 使用任意基类 | 使用带显式 key selector 的 `DataTable<TKey, TRow>` |
| Registration allocation 超出预期 | Iterator accessor、LINQ、captured closure | Registration 与 runtime query 分开测量；使用 static accessor |
| Reload 失败但 source registration 已改变 | 回滚未完成 | 重新注册之前的 source object，Dispose 失败 candidate，保持 catalog 不变 |

## 验证

```text
CycloneGames.GameplayTags.DataTable.Tests.Editor (EditMode)
CycloneGames.GameplayTags.Tests.Editor            (EditMode)
```

覆盖 enabled、disabled、duplicate、invalid、null collection、accessor exception 和 budget overflow。使用生产规模 row 验证，并在每个支持的 Player 配置中测试，包括 IL2CPP/AOT。
