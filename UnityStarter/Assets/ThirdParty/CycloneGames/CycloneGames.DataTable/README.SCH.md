# CycloneGames.DataTable

[English | 简体中文](README.md)

CycloneGames.DataTable 将类型化配置数据——物品定义、Gameplay Tag、技能数值、成长曲线、本地化文本——加载为不可变表快照，按键 O(1) 查询。每次加载的数据量由 `DataTableLoadLimits` 控制，每份数据在任何人读取前都经过 SHA-256 manifest 校验。核心程序集不含 `UnityEngine` 依赖；Luban 和 MessagePack 适配器放在独立的集成程序集中。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速上手](#快速上手)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [进阶主题](#进阶主题)
- [常见场景](#常见场景)
- [性能与内存](#性能与内存)
- [故障排查](#故障排查)

## 概述

模块暴露 `DataTable<TKey, TRow>`（保持源顺序）、`DataTableCatalog`（把多张强类型表组成一个快照）和 `DataTableRegistry`（进程级原子发布入口）。可变游戏状态、存档事务、网络同步、数据库查询和特定 schema 的业务规则，各自归各自的系统管。

### 主要特性

- **不可变表快照**：保持源顺序的 row 和期望 `O(1)` 查询。
- **强类型 Catalog**：按准确 contract type 组合多个表。
- **进程级原子发布**：通过 `DataTableRegistry`，支持 volatile snapshot 读取。
- **有界 payload 加载**：通过 `DataTableBytesCache` 与 `DataTableLoadLimits`。
- **完整性元数据**：通过 `DataTableManifest`（schema 版本、必需表、字节长度、SHA-256）。
- **AOT-safe 注册**：通过显式 `TableDescriptor<TTableSet>` 注册生成表集合，无运行时反射。
- **Luban 与 MessagePack adapter**：与纯 C# Core assembly 隔离。
- **Unity Editor 集成**：`DataTableLubanSettings`、自定义 Inspector 和带安全保护的 Luban 进程 Runner。

## 架构

| 程序集 | 命名空间 | 职责 |
| --- | --- | --- |
| `CycloneGames.DataTable.Core` | `CycloneGames.DataTable` | Table、Catalog、Registry、限制、Manifest、Hash、字节 Cache、Location、日志和 Scope。纯 C#，启用 `noEngineReferences: true`。 |
| `CycloneGames.DataTable.Unity.Runtime` | `CycloneGames.DataTable.Unity` | Unity Runtime 日志引导。 |
| `CycloneGames.DataTable.Unity.Editor` | `CycloneGames.DataTable.Unity.Editor` | `DataTableLubanSettings`、自定义 Inspector、请求校验和外部进程执行。仅 Editor。 |
| `CycloneGames.DataTable.Unity.Runtime.Integrations.Luban` | `CycloneGames.DataTable.Unity.Integrations.Luban` | 有界的 Luban `ByteBuf` 创建和生成表集合构造。 |
| `CycloneGames.DataTable.Unity.Runtime.Integrations.MessagePack` | `CycloneGames.DataTable.Unity.Integrations.MessagePack` | 有界的 MessagePack 行数组解码。 |
| `CycloneGames.DataTable.Unity.Runtime.Integrations.AssetManagement` | `CycloneGames.DataTable.Unity.Integrations.AssetManagement` | 可选的 UniTask `TextAsset` 和 raw-file payload loader；在 asset-style 安装方式下不参与编译。 |

Core 和 Unity Runtime 会自动引用。Editor 与 Integration assembly 使用 `autoReferenced: false`；消费者 asmdef 必须引用实际使用的每个 assembly。Luban 和 MessagePack Integration 还要求对应 package 满足其 asmdef 声明的版本条件。Asset-style AssetManagement 模块不会生成 DataTable Integration 所需的 UPM `versionDefines` capability，因此该 Integration 保持不参与编译；只添加 asmdef reference 不能启用它。

```mermaid
flowchart LR
    A[生成行或 payload 字节] --> B[应用限制并校验完整性]
    B --> C[解码并校验行数据]
    C --> D[构建强类型 DataTable]
    D --> E[构建候选 DataTableCatalog]
    E --> F[校验跨表引用]
    F --> G[注入 Catalog 或原子发布]
    G --> H[只读游戏逻辑消费者]

    classDef input fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef validation fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef snapshot fill:#dcfce7,stroke:#16a34a,color:#052e16
    class A input
    class B,C,F validation
    class D,E,G,H snapshot
```

构造、解码、Hash 和校验属于冷路径工作。游戏逻辑从已经发布的 Table 或 Catalog 中读取数据。

## 快速上手

在消费者 asmdef 中加入 `CycloneGames.DataTable.Core`。纯 C# 消费者不需要 Unity Runtime assembly。

```json
{
  "references": [
    "CycloneGames.DataTable.Core"
  ]
}
```

### 定义整数键行

主键为 `int` 时实现 `IDataRow`。发布后的行值应保持不可变。

```csharp
using CycloneGames.DataTable;

public sealed class ItemRow : IDataRow
{
    public ItemRow(int id, string name, int maxStack)
    {
        Id = id;
        Name = name;
        MaxStack = maxStack;
    }

    public int Id { get; }
    public string Name { get; }
    public int MaxStack { get; }
}
```

### 构建表

```csharp
var items = new DataTable<ItemRow>(new[]
{
    new ItemRow(1001, "Health Potion", 20),
    new ItemRow(1002, "Mana Potion", 20),
});
```

构造函数会复制源数组、保留行顺序并建立 key-to-index Dictionary。遇到 null row、null key 或重复 key 时，构造会失败。

### 查询行

```csharp
ItemRow healthPotion = items.Get(1001);

if (items.TryGet(1002, out ItemRow manaPotion))
{
    UseItem(manaPotion);
}

ItemRow missing = items.GetOrDefault(9999); // 对这个 class row 返回 null

for (int i = 0; i < items.Count; i++)
{
    ItemRow row = items.All[i];
    RegisterItem(row);
}
```

当缺少 key 代表数据契约错误时使用 `Get`，找不到时会抛出 `KeyNotFoundException`。可选查询使用 `TryGet`。只有在 `default(TRow)` 对该行类型含义明确时才使用 `GetOrDefault`。

## 核心概念

### 自定义键类型与生成模型

Row 不必实现 DataTable interface。当生成模型无法修改，或 key 不是 `int` 时，传入 key selector。

```csharp
using System;
using CycloneGames.DataTable;

public sealed class LocalizedTextRow
{
    public LocalizedTextRow(string key, string text)
    {
        Key = key;
        Text = text;
    }

    public string Key { get; }
    public string Text { get; }
}

var texts = new DataTable<string, LocalizedTextRow>(
    new[]
    {
        new LocalizedTextRow("ui.play", "Play"),
        new LocalizedTextRow("ui.quit", "Quit"),
    },
    static row => row.Key,
    StringComparer.Ordinal);
```

应明确选择 key 的等价语义：

- 使用能跨越序列化和内容重建保持稳定的整数、枚举、GUID 或字符串值；
- 大小写敏感的标识符使用 `StringComparer.Ordinal`；
- 只有内容契约明确规定标识符不区分大小写时才使用 `StringComparer.OrdinalIgnoreCase`；
- 持久化内容 key 不使用文化相关比较、Unity object identity 或临时 runtime handle。

Key selector 只在构造时对每一行执行一次，不会在每次查询时执行。

### 行存储与所有权

`DataTable<TKey, TRow>` 在结构上不可变：构造后不能添加、删除或替换行。`All` 按源顺序提供只读 view，查询 Dictionary 保存的是整数行索引，不会在 Dictionary 中保存第二份 row 值。

结构不可变不等于深拷贝 row object。Class row 及其引用的每个可变对象必须在 Table 生命周期内保持不变。调用方提供的 comparer 也必须能安全地支持并发读取。

根据源数据所有权选择构造 API：

| API | 源数据处理 | 推荐用途 |
| --- | --- | --- |
| `new DataTable(... array ...)` | 复制数组 | 调用方继续持有源数组时的安全默认方式。 |
| `new DataTable(... list ...)` | 把 List 元素复制到自有数组 | 已有 `List<TRow>` 的安全默认方式。 |
| `FromEnumerable` | 物化一次，并应用行数限制 | 流式或计算产生的冷路径输入。 |
| `FromOwnedArray` | 不复制，直接接管数组 | Decoder 生成且不存在其他可写 alias 的数组。 |

```csharp
ItemRow[] decodedRows = DecodeAndValidateItems();

DataTable<ItemRow> items = DataTable<ItemRow>.FromOwnedArray(
    decodedRows,
    limits);

decodedRows = null; // 所有权已经转移到 Table
```

`FromOwnedArray` 成功后，任何代码都不能再修改转移的数组。所有权转移只作用于数组容器；其中引用的 class instance 仍需遵守产品的不可变 row 契约。

### Catalog 与发布

`DataTableCatalog` 使用准确的 contract type 组合一组相关表。应先完成整个 Catalog，再把它暴露给游戏系统。

```csharp
DataTable<ItemRow> items = BuildItems();
DataTable<string, PriceRow> prices = new DataTable<string, PriceRow>(
    DecodePrices(),
    static row => row.ItemCode,
    StringComparer.Ordinal);

DataTableCatalog catalog = new DataTableCatalogBuilder(limits, capacity: 2)
    .Add<IDataTable<ItemRow>>(items)
    .Add<IDataTable<string, PriceRow>>(prices)
    .Build();

ValidateItemReferences(catalog);
```

Catalog 按传给 `Add` 的准确类型查询。读取时必须使用同一个 contract type：

```csharp
IDataTable<ItemRow> itemTable = catalog.Get<IDataTable<ItemRow>>();

if (catalog.TryGet<IDataTable<string, PriceRow>>(out var priceTable))
{
    ShowPrice(priceTable.Get("potion.health"));
}
```

`DataTableCatalogBuilder` 只能消费一次。`Build()` 会把内部 map 转交给不可变 Catalog，此后对 Builder 的操作都会失败。Contract type 重复、null entry、实例类型不兼容，或表数量超过 `DataTableLoadLimits.MaxTableCount`，都会在 Catalog 创建前失败。

进程级发布使用 `DataTableRegistry.Publish(catalog)` 原子替换整个 Catalog。一次跨多表操作只读取一次 `Current`——即使随后发布了另一代，已经捕获的引用仍保持内部一致。诊断信息可以记录 `Generation`。`Reset()` 只移除进程级引用，不会 Dispose Table 或 backing resource。

## 使用指南

### 有界 payload 加载

为一组内容创建统一的限制配置。数值应来自真实生成内容、重载峰值内存测量和最低支持硬件档位。

```csharp
var limits = new DataTableLoadLimits(
    maxTableCount: 128,
    maxBytesPerTable: 8 * 1024 * 1024,
    maxTotalBytes: 64L * 1024 * 1024,
    maxRowsPerTable: 250_000,
    maxTableNameLength: 96);
```

`DataTableLoadLimits.Default` 是较宽松的 fail-fast guardrail。正式产品通常应在每个不可信或内存敏感边界采用更严格的配置。

把 payload 保存到有界 Cache：

```csharp
using var payloadCache = new DataTableBytesCache(
    limits,
    capacity: 16,
    dataExtension: ".bytes",
    clearBytesOnDispose: false);

payloadCache.Add("Items", itemPayload);       // 复制 ReadOnlyMemory<byte>
payloadCache.AddOwned("Prices", priceBytes); // 转移 byte[] 所有权
priceBytes = null;

payloadCache.Seal();

ReadOnlyMemory<byte> bytes = payloadCache.GetBytes("Items.bytes");
```

Table name 会被规范化，因此 `Items` 和 `Items.bytes` 指向同一项。Cache identity 不区分大小写，防止两个 entry 在不区分大小写的文件系统上落到同一个原生路径。`Add` 和 `Set` 会复制 payload。`AddOwned` 和 `SetOwned` 避免复制，但要求调用方独占数组所有权。`Seal()` 会禁止修改。Cache 不提供自动淘汰策略；所有 reader 停止后，应 Dispose 对应内容 Scope。

### Manifest 校验

在解码 payload 前使用带版本的 Manifest：

```csharp
var manifest = new DataTableManifest(
    schemaVersion: 3,
    entries: new[]
    {
        new DataTableManifestEntry(
            tableName: "Items",
            location: "Config/Items.bytes",
            expectedByteLength: itemPayload.Length,
            sha256Hex: expectedItemsSha256,
            required: true),
        new DataTableManifestEntry(
            tableName: "Prices",
            location: "Config/Prices.bytes",
            expectedByteLength: pricePayloadLength,
            sha256Hex: expectedPricesSha256,
            required: true),
    },
    limits,
    requireKnownTables: true);

manifest.EnsureSchemaVersionSupported(minimumVersion: 3, maximumVersion: 3);
manifest.ValidateRequiredTables(payloadCache);
manifest.ValidateBytes("Items", payloadCache.GetBytes("Items"));
manifest.ValidateBytes("Prices", payloadCache.GetBytes("Prices"));
```

`ValidateRequiredTables` 检查必需项是否存在。`ValidateBytes` 应用已配置的字节限制，并校验 entry 的预期长度和 SHA-256。SHA-256 用于标识内容和检测损坏；来自不可信来源的内容还需要经过认证的签名和由产品负责的信任策略。

Entry 未设置 `Sha256Hex` 时会有意跳过 Hash 校验。`DataTableHashUtility.Sha256Matches` 要求预期 Hash 非空；预期值缺失时返回 `false`。

### 资源生命周期

生成表或 row view 依赖可 Dispose 的 payload owner 时，使用 `DataTableSetScope`：

```csharp
var scope = new DataTableSetScope(
    root: generatedTables,
    catalog: catalog,
    resourceOwner: payloadCache);

IDataTable<ItemRow> items = scope.Get<IDataTable<ItemRow>>();

// 所有 reader 停止后：
scope.Dispose();
```

Scope 只 Dispose 显式传入的 `resourceOwner`。它会清除自身对 root 和 Catalog 的引用，但不会推断任意 Table instance 的所有权。发布的 row 或生成 view 仍可能访问 backing memory 时，不得 Dispose 对应 owner。

### Luban 集成

在 composition assembly 中引用 `CycloneGames.DataTable.Unity.Runtime.Integrations.Luban`。`com.code-philosophy.luban` package 必须满足 Integration asmdef 声明的版本范围。

准备有界的 `IDataTableBytesProvider`，再创建 Luban 生成的 root：

```csharp
using CycloneGames.DataTable.Unity.Integrations.Luban;

cfg.Tables generatedTables = LubanDataTableSetFactory.Create(
    payloadCache,
    getBytes => new cfg.Tables(getBytes),
    limits);
```

Callback 只在同步 factory call 期间、且仅在调用线程上有效。每个请求的 payload 都会复制到私有 `Luban.ByteBuf` 数组中，因此生成 parser 可以保留 buffer，而不会借用可写的 Cache memory。

生成 parser 直接接收单个 `ByteBuf` 时：

```csharp
Luban.ByteBuf itemBuffer = LubanDataTableSetFactory.CreateOwnedByteBuf(
    payloadCache,
    "Items",
    limits);
```

Catalog 发布前，应校验生成数据的行数、范围、稳定 ID 和跨表引用。完整的管线设置、Windows/macOS/Linux 生成命令、输出所有权和恢复操作见 [Luban 指南](../../../../../DataTable/Luban/README.SCH.md)。

### MessagePack 集成

引用 `CycloneGames.DataTable.Unity.Runtime.Integrations.MessagePack`。Unity client package、`MessagePack.dll`、Annotations 和 Analyzer 应保持匹配版本；IL2CPP/AOT 使用 source-generated row formatter。

定义 MessagePack row contract：

```csharp
using CycloneGames.DataTable;
using MessagePack;

[MessagePackObject]
public sealed class PackedItemRow : IDataRow
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public string Name { get; set; }
}
```

使用显式 resolver 和安全设置解码顶层 row array：

```csharp
using System.Threading;
using CycloneGames.DataTable.Unity.Integrations.MessagePack;
using MessagePack;
using MessagePack.Resolvers;

MessagePackSerializerOptions options =
    MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);

MessagePackSecurity security = MessagePackSecurity.UntrustedData
    .WithMaximumObjectGraphDepth(64)
    .WithMaximumDecompressedSize(limits.MaxBytesPerTable);

DataTable<PackedItemRow> items = MessagePackConfigProvider.Build<PackedItemRow>(
    itemPayload,
    options,
    security,
    limits,
    CancellationToken.None);
```

Adapter 要求有界的 untrusted-data policy，在物化 row 前校验 payload 大小和顶层数组数量，观察 cancellation，并拒绝损坏、截断或带尾随字节的数据。它只使用传给 `Build` 的 options；resolver composition 应在调用点显式配置。

自定义 key 使用带 key selector 和 comparer 的 `Build<TKey, TRow>`。Decoder 已经独占一个完成校验的 `TRow[]` 时，使用 `BuildRows` 把数组直接转移给 Table。

### Unity Editor 与 Luban 生成

通过 `Assets > Create > CycloneGames > DataTable > Luban Settings` 创建 `DataTableLubanSettings`，或者执行 `Tools > CycloneGames > DataTable > Run Luban Build`。如果设置资产不存在，Runner 会在 `Assets/Editor/DataTable/DataTableLubanSettings.asset` 创建。每个设置类型应保留一个权威设置资产。

| 字段 | 含义 |
| --- | --- |
| `LubanProjectDir` | 相对于 Unity 项目根目录的 Luban 目录。默认指向 `../DataTable/Luban`。 |
| `LubanScriptName` | 不带扩展名的脚本名。Runner 在 Windows 添加 `.bat`，在 macOS/Linux 添加 `.sh`。 |
| `LubanScriptArguments` | 附加到生成命令的可选参数。 |
| `LubanTimeoutSeconds` | 外部进程最长执行时间；无效的序列化值回退为 300 秒。 |
| `RefreshAssetsAfterLubanBuild` | 只在成功执行后调用 `AssetDatabase.Refresh()`。 |

Inspector 会显示解析后的路径和校验状态，并提供 refresh、reveal、validate 和 build 操作。启动脚本前会校验项目根目录、工作目录、脚本路径、参数和 timeout。Standard output 和 standard error 会写入有界结果。

Runner 在 Editor 内只允许一个 writer。生成 wrapper 同时使用writer lock，避免 Editor、终端和 CI 并发发布。Timeout 或 cancellation 后如果无法确认子进程已经退出，应停止全部 Generator 进程、检查writer lock 和生成输出、完成恢复，然后重启 Editor，再执行下一次生成。需要派生生成配置的项目可以继承 `DataTableLubanSettings` 并覆盖其 virtual method，包括 `CreateLubanRunRequest()`，无需修改本 Package。

## 进阶主题

### AOT-safe 生成表集合注册

Generator 生成一个包含多个 Table property 的 root object 时，使用显式 descriptor。请把示例中的生成类型和属性替换为项目 Generator 实际输出的名称。

```csharp
using CycloneGames.DataTable;

var descriptors = new[]
{
    new DataTableGeneratedTableCollector.TableDescriptor<MyGeneratedTables>(
        typeof(MyGeneratedItemTable),
        static tables => tables.Items),
    new DataTableGeneratedTableCollector.TableDescriptor<MyGeneratedTables>(
        typeof(MyGeneratedPriceTable),
        static tables => tables.Prices),
};

DataTableCatalog generatedCatalog =
    DataTableGeneratedTableCollector.CreateCatalog(generatedTables, descriptors);
```

Descriptor array 明确指定 Catalog contract type 和 property accessor。该路径不会扫描 runtime assembly，也不使用反射发现，便于 IL2CPP 和 managed stripping 下保持明确的注册图。

### 生产加载与热重载

一套完整的重载顺序如下：

1. 使用产品级 `DataTableLoadLimits` 创建候选 payload owner。
2. 加载必需 payload，并拒绝缺失、空、超限或未知项。
3. 校验 Manifest schema、字节长度、Hash 和内容信任状态。
4. 把全部 Table 解码为候选 generation。
5. 校验字段、稳定 ID、范围、唯一性和跨表引用。
6. 构建一个 `DataTableCatalog`。
7. 把 Catalog 注入新的 composition scope，或调用 `DataTableRegistry.Publish`。
8. 等待上一代数据的 reader 全部退出。
9. Dispose 上一代的 backing resource。

发布前任何步骤失败，都应丢弃候选数据，并保持 active generation 不变。Registry 不追踪 reader lease，因此资源退役必须由应用协调。

### 显式注入 vs. 进程级发布

消费者具有明确 owner 和生命周期时，通过构造函数传入 Catalog：

```csharp
public sealed class ItemService
{
    private readonly IDataTable<ItemRow> _items;

    public ItemService(DataTableCatalog catalog)
    {
        _items = catalog.Get<IDataTable<ItemRow>>();
    }

    public ItemRow GetItem(int id) => _items.Get(id);
}
```

这种方式同时支持直接构造和任意 DI composition root；Core 不依赖具体容器。只有在应用明确需要一个进程级 Catalog generation 时才使用 `DataTableRegistry`。

## 常见场景

### 运行时物品与价格查询

游戏系统需要按不同 key 查询物品定义和价格。构建两份表，组合成 Catalog，注入到 service：

```csharp
public sealed class ShopService
{
    private readonly IDataTable<ItemRow> _items;
    private readonly IDataTable<string, PriceRow> _prices;

    public ShopService(DataTableCatalog catalog)
    {
        _items = catalog.Get<IDataTable<ItemRow>>();
        _prices = catalog.Get<IDataTable<string, PriceRow>>();
    }

    public int GetSellPrice(int itemId)
    {
        ItemRow item = _items.Get(itemId);
        return _prices.Get(item.Name).SoftCurrency;
    }
}
```

两次查询都期望 `O(1)`。Catalog 捕获一个内部一致的快照——即使随后发布了另一代，这个 service 仍读取它构造时的快照。

### 字符串 key 的本地化

本地化系统使用字符串 key（`"ui.play"`、`"ui.quit"`）而非整数 ID：

```csharp
DataTable<string, LocalizedTextRow> texts = new DataTable<string, LocalizedTextRow>(
    LoadLocalizedRows(),
    static row => row.Key,
    StringComparer.Ordinal);

public string Localize(string key)
{
    return texts.TryGet(key, out LocalizedTextRow row) ? row.Text : key;
}
```

`StringComparer.Ordinal` 提供大小写敏感的标识符匹配。Key selector 只在构造时执行一次；查询时不会再次调用。

### Catalog 原子热替换

在线游戏下载新内容 generation，需要不重启就替换 Catalog。在加载 owner 上构建候选 generation，校验后原子发布：

```csharp
public async Task ReloadContentAsync(byte[] manifestBytes, CancellationToken ct)
{
    DataTableBytesCache candidateCache = LoadPayloads(manifestBytes, ct);
    DataTableManifest manifest = ParseManifest(manifestBytes, candidateCache);
    manifest.ValidateRequiredTables(candidateCache);

    DataTableCatalog candidateCatalog = await BuildCatalogAsync(candidateCache, manifest, ct);
    ValidateCrossTableReferences(candidateCatalog);

    DataTableRegistry.Publish(candidateCatalog);

    // 上一代 reader 全部退出后：
    _previousCache?.Dispose();
    _previousCache = candidateCache;
}
```

`DataTableRegistry.Publish` 原子替换整个 Catalog；在替换前捕获 `Current` 的 reader 仍看到上一个快照。应用负责协调上一代 backing resource 的退役。

### 无反射注册生成表集合

代码生成器产出一个 `cfg.Tables` root，包含强类型 Table property。用显式 descriptor 注册，让注册图在 IL2CPP 与 managed stripping 下保持明确：

```csharp
var descriptors = new[]
{
    new DataTableGeneratedTableCollector.TableDescriptor<cfg.Tables>(
        typeof(cfg.TbItem),
        static tables => tables.TbItem),
    new DataTableGeneratedTableCollector.TableDescriptor<cfg.Tables>(
        typeof(cfg.TbPrice),
        static tables => tables.TbPrice),
};

DataTableCatalog catalog =
    DataTableGeneratedTableCollector.CreateCatalog(generatedTables, descriptors);
```

不进行 runtime assembly 扫描或反射发现。

## 性能与内存

| 操作 | 运行时特征 |
| --- | --- |
| 构造完成后的成功 `Get`、`GetOrDefault`、`TryGet` | 期望 `O(1)` Dictionary 查询，无有意的托管分配。 |
| Key 缺失时调用 `Get` | 创建并抛出 `KeyNotFoundException`；不应作为常规热路径控制流。 |
| `All[index]` | 通过只读 view 进行 `O(1)` 访问。 |
| Table 构造 | 冷路径；除非转移所有权，否则复制数组，并分配 row view 和 key index。 |
| Catalog 强类型查询 | 期望 `O(1)` 的 Type-keyed Dictionary 查询。 |
| Registry 读取 | Volatile snapshot 读取，不使用 reader lock。 |
| Registry 发布 | 串行 writer 路径；分配 state object 和诊断消息。 |
| 字节 Cache 查询 | 加载路径，包含名称规范化和 Dictionary 查询。 |
| Hash、Manifest 校验与解码 | 冷路径；分配和处理成本取决于后端。 |

计算重载峰值内存时，应包含所有同时存活的部分：

```text
源 payload
+ Cache 自有 payload
+ Adapter 副本、解压和 Decoder scratch memory
+ Row array 与 Row 引用的 Object
+ Key-to-index Dictionary
+ 发布期间重叠的旧 Generation 与候选 Generation
```

只有能证明所有权时才使用 `FromOwnedArray` 和 `AddOwned`；节省一次复制不应以可写 alias 或 Dispose 后访问风险为代价。Cache 应跟随内容 generation 建立 Scope，不应在进程级永久保留全部 payload。对于大型 value-type row，应把行值复制成本纳入 Profile。只有代表性 Benchmark 证明它是主要瓶颈时，才增加专用访问 API。性能预算应记录 row 形状、key 类型、命中与未命中分布、表规模、后端、Unity scripting backend、目标硬件、预热方式和 GC 测量窗口。

### 线程与生命周期规则

- 在游戏热路径之外，由一个加载 owner 构造 Table、Catalog、Manifest 和 Cache。
- Row、引用对象和 comparer 保持不可变时，已发布的 Table 与 Catalog 可以并发读取。
- `DataTableCatalogBuilder` 和未 Seal 的 `DataTableBytesCache` 是单 owner 可变对象。
- `Seal()` 禁止 Cache 修改，但它本身不是内存发布屏障。
- 不得让 `Dispose()` 与 Cache reader，或依赖该 owner 的 Table view 并发执行。
- `DataTableRegistry` 串行化 writer，并向 reader 暴露 volatile immutable snapshot。
- Luban payload 请求同步执行，并留在 factory owner thread。
- Unity object 和 AssetManagement loader 遵循 Unity main-thread affinity。

线程安全来自已发布不可变快照及其所有权协议。它不会让可变 row object 或第三方 parser state 自动具备并发安全性。

### 平台指南

- **Windows、Linux、macOS：** 使用 Luban 指南中的平台生成 wrapper。Table identity 必须同时兼容大小写敏感和不敏感文件系统。
- **IL2CPP 与 managed stripping：** 使用 source-generated serializer 和显式 `TableDescriptor<TTableSet>` 注册。只对后端确实要求保留的生成类型配置 preservation。
- **iOS 与 Android：** 在 source buffer、adapter copy、解压、解码 row、index Dictionary 和 generation overlap 同时存在时，测量冷加载耗时与峰值内存。
- **WebGL：** 不依赖后台线程。把同步解码工作和托管内存峰值控制在经过测量的帧预算或 Loading Screen 预算内。
- **Dedicated Server：** Server composition 只引用 Core 和纯 C# adapter，排除 Unity Asset Loading 和 Editor assembly。
- **主机平台：** 使用平台持有者工具链验证原生依赖导入、文件名规则、内存限制、AOT 行为和认证要求。

每个支持的 scripting backend 和平台配置都应执行 clean Player build 与代表性内容测试。Editor 结果适合开发阶段检查，但不能替代目标 Runtime Profile。

### 安全、持久化与日志

文件、远程配置、补丁、Mod 和命令行选择的内容都应视为不可信输入。应限制 payload 字节、总字节、行数、表数、解压、嵌套集合、递归深度、字符串、处理时间和诊断数量。发布前校验稳定 ID、数值范围、引用、schema 版本、权限和签名。

Core 不写文件，也不使用 `EditorPrefs` 或 `PlayerPrefs`。

| 数据 | Owner 与生命周期 |
| --- | --- |
| Workbook、schema 与 Generator 配置 | 纳入版本控制的内容源。 |
| 生成 C# 与二进制 payload | 可重建的内容管线输出；按产品管线决定提交或发布。 |
| Manifest 与 schema version | 与匹配的 payload 一起版本化和发布。 |
| `DataTableLubanSettings.asset` | 可见的 Unity 项目配置；保留一个权威资产。 |
| Runtime byte cache | 由 Runtime 内容 Scope 持有，在 reader 退役后 Dispose。 |

`DataTableLogger` 在 Core 中默认使用 `System.Console`。Composition root 可以设置 `LogInfo`、`LogWarning` 和 `LogError`；`CycloneGames.DataTable.Unity.Runtime` 会安装 Unity 日志。关闭 Domain Reload 时，应在 subsystem 注册阶段重置或重新绑定自定义 delegate。日志应包含 table identity、generation、stage、limit 和 failure category，但不能记录 secret 或完整恶意 payload。

## 故障排查

| 现象 | 可能原因 | 解决方法 |
| --- | --- | --- |
| 构造函数报告重复 key | 两行共享同一 key | 修正内容源或 key selector；一个 Table 内每个 key 必须唯一 |
| `Get<TTable>()` 找不到 Catalog entry | Contract type 不正确 | 使用 `DataTableCatalogBuilder.Add<TTable>` 注册时的准确 contract type 查询 |
| `DataTableRegistry.Get<TTable>()` 返回 `null` | 未发布 Catalog，或缺少 contract | 确认包含该 contract 的完整 Catalog 已经发布；检查 `IsInitialized`/`Generation` |
| 大小写不同的名称被 Cache 报告为已存在 | 不区分大小写的 identity | 使用一个规范且可移植的 Table identity；Cache 和 Manifest identity 不区分大小写 |
| 加载后修改 Cache 时抛出异常 | Cache 已 Seal | 为新的内容 generation 创建新的候选 Cache |
| Manifest 拒绝 payload | Schema、长度或 Hash 不匹配 | 检查 schema version、规范化 Table name、预期字节长度、SHA-256 和发布版本 |
| Luban callback 报告线程或生命周期错误 | Callback 被保存或离线程调度 | 所有生成 payload 请求都必须在 `LubanDataTableSetFactory.Create` 内同步调用；不能保存或调度 Callback |
| MessagePack 拒绝 security policy | 安全边界不足 | 从 `MessagePackSecurity.UntrustedData` 开始，保持抗 Hash 碰撞，并把解压大小限制到 `MaxBytesPerTable` |
| MessagePack 无法解码 row | Payload 形状错误或缺少 formatter | 确认 payload 是顶层 `TRow[]`、row formatter 已生成，并且显式 resolver 包含它 |
| Luban Inspector 报告路径无效 | 目录或脚本配置错误 | 校验相对 Unity 项目的目录、平台脚本扩展名、脚本是否存在，以及设置资产是否唯一 |
| Timeout 后 Luban 执行仍处于 blocked | 子进程未干净退出 | 停止 Generator 进程，检查 `.cyclonegames-datatable-writer.lock` 和输出，恢复目录，然后重启 Unity |
| 重载内存高于 Cache Total | Generation 重叠和 Decoder scratch | Profile payload source、copy、解压、decoder object、row object、Dictionary 和新旧 generation overlap |

## 验证

### Core 与 Integration 测试

通过 Unity Test Runner 或项目的 batchmode test 入口运行以下 EditMode test assembly：

- `CycloneGames.DataTable.Tests.Editor`
- 启用 Luban 时运行 `CycloneGames.DataTable.Tests.Editor.Integrations.Luban`
- 启用 MessagePack 时运行 `CycloneGames.DataTable.Tests.Editor.Integrations.MessagePack`
- `CycloneGames.DataTable.Tests.Performance`

产品测试还应加入重复和缺失 key、异常 payload、数量超限、schema 不匹配、跨表引用失败、取消、重载回滚和 backing owner 退役等 fixture。

### Generator 验证

生成内容前校验配置，执行对应平台 wrapper，检查执行摘要，并在提交前审核生成差异。Wrapper 命令和恢复流程见 [Luban 指南](../../../../../DataTable/Luban/README.SCH.md)。Package 内 CodeGen 工具的构建与自测试步骤见 [Tools~/CodeGen/README.SCH.md](./Tools~/CodeGen/README.SCH.md)。

### Player 验证

对每个支持的构建配置执行：

1. 从 clean checkout 构建；
2. 验证 asmdef 条件和 serializer 注册；
3. 加载最小、典型和最大规模的代表性内容；
4. 记录冷加载时间、峰值内存、保留内存和热查询分配；
5. 覆盖损坏、缺失、取消和回滚路径；
6. 使用 shipping scripting backend 和 managed stripping 重复验证。

## API 导航

| 类型 | 主要用途 |
| --- | --- |
| `IDataRow<TKey>` / `IDataRow` | 手写或生成 row 可选实现的稳定主键 contract。 |
| `IDataTableRows<TRow>` | 最小、按源顺序读取的只读 row view。 |
| `IDataTable<TKey, TRow>` / `IDataTable<TRow>` | 只读 keyed table contract。 |
| `DataTable<TKey, TRow>` / `DataTable<TRow>` | 不可变 row storage 与 key-to-index 查询。 |
| `DataTableLoadLimits` | 显式的表数、字节、行数和名称预算。 |
| `DataTableCatalog` | 不可变 Type-indexed table snapshot。 |
| `DataTableCatalogBuilder` | 一次性的候选 Catalog 构造器。 |
| `DataTableRegistry` | 可选的进程级原子发布入口。 |
| `DataTableGeneratedTableCollector` | AOT-safe 生成表集合收集。 |
| `IDataTableBytesProvider` | 借用只读 payload 的访问 contract。 |
| `DataTableBytesCache` | 有界的物化 payload array owner。 |
| `DataTableManifest` / `DataTableManifestEntry` | Schema、存在性、字节长度、位置和 SHA-256 元数据。 |
| `DataTableHashUtility` | SHA-256 计算、规范化和严格匹配。 |
| `DataTableNameUtility` | 可移植的 Table name、扩展名和路径规范化。 |
| `DataTableLocationResolver` | 构造可移植相对 Location。 |
| `DataTableSetScope` | 管理生成 root、Catalog 和可选 backing owner 生命周期。 |
| `DataTableLogger` | 可替换的日志边界。 |
| `LubanDataTableSetFactory` | 创建有界且私有持有的 Luban buffer。 |
| `MessagePackConfigProvider` | 有界 MessagePack row array 解码和 Table 构造。 |
| `DataTableLubanSettings` | 可见的 Unity Editor 生成设置。 |
| `DataTableLubanRunner` | 经过校验的单 writer 外部生成进程。 |

## 相关文档

- [Luban 目录与生成教程](../../../../../DataTable/Luban/README.SCH.md)
- [DataTable CodeGen 教程](./Tools~/CodeGen/README.SCH.md)
- [GameplayTags DataTable 集成](../CycloneGames.GameplayTags.DataTable/README.SCH.md)
