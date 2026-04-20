# CycloneGames.GameplayTags

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

上游源码：`https://github.com/BandoWare/GameplayTags`

面向 **Unity** 的生产级 **Gameplay Tags** 系统，灵感来自虚幻引擎。基于纯 C# 引擎无关核心（零 Unity 依赖），加上可选的 Unity 集成层（ECS、Burst/Jobs、编辑器工具）。

## 与上游差异（BandoWare/GameplayTags）

| 领域 | 上游 | CycloneGames |
|------|------|-------------|
| **线程安全** | 无 | COW 快照 + Volatile.Read/Write，无锁读取 |
| **注册方式** | 仅程序集特性 | 4 种方式：特性、静态类、运行时动态、JSON 文件 |
| **性能** | 标准容器 | 256 位 `GameplayTagMask`、`GameplayTagMaskLarge`、位集加速容器 |
| **网络** | 无 | `GameplayTagNetSerializer`（全量 + 增量 + 掩码） |
| **ECS** | 无 | `GameplayTagMaskComponent`、`NativeGameplayTagMask`、Burst 兼容 |
| **热更新** | 不支持 | `RegisterDynamicTag`、`RegisterDynamicTagsFromAssembly`、HybridCLR 感知 |
| **标签迁移** | 无 | `GameplayTagRedirector` 带链式扁平化 |
| **构建流水线** | 无 | v1 二进制格式 + CRC32 校验 |
| **编辑器** | 基础 | 搜索、右键菜单、验证工具、Source Generator |
| **对象池** | 无 | `GameplayTagContainerPool`、`Pools.ListPool<T>`、`Pools.DictionaryPool<K,V>` |

## 特性

### 🏗️ 架构

| 特性 | 说明 |
|------|------|
| **引擎无关核心** | `CycloneGames.GameplayTags.Core` 设置 `noEngineReferences: true` — 纯 C#，可移植到任何 .NET 运行时 |
| **Copy-on-Write 快照** | `TagDataSnapshot` 通过 `Volatile.Write` 发布，任意线程无锁读取 |
| **三程序集设计** | Core（纯 C#）→ Unity（Burst/Jobs/ECS）→ Editor（IMGUI 工具），依赖关系清晰 |
| **条件编译** | 通过 `#if CYCLONE_HAS_*` 支持 Unity.Collections、Burst、Entities — 未安装时零开销 |
| **Source Generator** | 编译时生成常量标签引用 — 无魔法字符串，无运行时拼写错误 |

### ⚡ 性能

| 特性 | 说明 |
|------|------|
| **256 位位掩码** | `GameplayTagMask` — 可 blittable 的 32 字节结构体，4 条位运算指令完成 HasTag/HasAll/HasAny O(1) |
| **动态大掩码** | `GameplayTagMaskLarge` — 自动增长的 `ulong[]`，支持 256+ 标签，仍然 O(1) 单标签操作 |
| **位集加速容器** | `GameplayTagContainer` 在 ≥64 标签时激活 `int[]` 位集实现 O(1) 查询 |
| **零 GC 枚举** | 自定义 `GameplayTagEnumerator` 结构体，池化 `List<T>` 用于批量操作 |
| **对象池** | `GameplayTagContainerPool` 配合 `Pools.ListPool<T>` / `Pools.DictionaryPool<K,V>` 消除分配尖峰 |
| **二进制构建格式** | v1 版本化二进制 + CRC32 校验 — 构建包中近乎瞬时加载标签 |

### 🌐 网络

| 特性 | 说明 |
|------|------|
| **紧凑二进制协议** | `GameplayTagNetSerializer` — 全量快照：`4 + 2N` 字节；增量：`6 + 2(A+R)` 字节 |
| **增量序列化** | 只发送变化的标签 — 通过 `GetDiffExplicitTags()` 自动差异计算 |
| **自动检测包类型** | `Deserialize()` 通过标记字节自动识别全量 vs 增量包 |
| **掩码序列化** | 256 位 `GameplayTagMask` 精确序列化为 32 字节（`unsafe memcpy`） |
| **框架无关** | 适用于 Netcode for GameObjects、Mirror、FishNet 或自定义传输层 |

### 🧵 线程安全

| 特性 | 说明 |
|------|------|
| **不可变快照** | `TagDataSnapshot` — 一旦发布永不修改；读者捕获引用后可自由使用 |
| **ReadOnlyGameplayTagContainer** | 容器状态的不可变线程安全快照 — 可安全用于工作线程和 Jobs |
| **无锁读取** | 所有标签查询使用 `Volatile.Read` 获取快照 — 零竞争 |
| **标签重定向** | `GameplayTagRedirector` 写操作用 `lock`，读操作无锁 `Dictionary.TryGetValue` |

### 🎮 ECS / DOD（条件编译）

| 特性 | 说明 |
|------|------|
| **NativeGameplayTagMask** | 基于 `NativeBitArray` 的掩码，Burst/Jobs 兼容（`#if CYCLONE_HAS_COLLECTIONS`） |
| **GameplayTagMaskComponent** | 256 位 `GameplayTagMask` 的 `IComponentData` 包装（`#if CYCLONE_HAS_ENTITIES`） |
| **变化检测** | `GameplayTagMaskPrevious` + `GameplayTagsDirty`（IEnableableComponent）用于增量复制 |
| **主线程桥接** | `CopyFrom(GameplayTagMask)`、`CopyFrom(IGameplayTagContainer)`、`CopyFrom(ReadOnlyGameplayTagContainer)` |

### 🛠️ 编辑器工具

| 特性 | 说明 |
|------|------|
| **Gameplay Tag 管理器窗口** | 可搜索的树状视图，带添加/删除/创建子标签右键菜单 |
| **Inspector 标签选择器** | 带搜索栏的弹出窗口、过滤树、清除按钮 — 适用于 `GameplayTag` 和 `GameplayTagContainer` 字段 |
| **标签验证工具** | 扫描所有 Prefab、ScriptableObject 和已打开场景中的无效标签引用，支持一键修复 |
| **JSON 文件实时监控** | `ProjectSettings/GameplayTags/` 中 `.json` 文件变更时自动重新加载 |
| **Source Generator** | 编译时生成 `GameplayTag` 常量的静态类 — 代码中零成本引用 |

---

## 程序集结构

```
CycloneGames.GameplayTags.Core        ← 纯 C#（无 Unity 引用，allowUnsafeCode）
    ↑
CycloneGames.GameplayTags.Unity       ← Unity 集成（Burst/Jobs/ECS，条件编译）
    ↑
CycloneGames.GameplayTags.Editor      ← IMGUI 编辑器窗口 & Inspector
    ↑
CycloneGames.GameplayTags.SourceGenerator  ← Roslyn Source Generator（编译时）
```

## 安装

### 要求

- Unity 2022.3+
- .NET Standard 2.1

### 可选包（自动检测）

| 包 | 宏定义 | 解锁功能 |
|----|--------|----------|
| `com.unity.collections` ≥ 1.0.0 | `CYCLONE_HAS_COLLECTIONS` | `NativeGameplayTagMask` |
| `com.unity.burst` ≥ 1.0.0 | `CYCLONE_HAS_BURST` | 掩码操作的 `[BurstCompile]` |
| `com.unity.entities` ≥ 1.0.0 | `CYCLONE_HAS_ENTITIES` | `GameplayTagMaskComponent`（IComponentData） |

---

## 标签注册

支持四种方式。所有标签在初始化后通过 `GameplayTagManager` 可用。

### 1. 程序集特性

```csharp
[assembly: GameplayTag("Damage.Fatal")]
[assembly: GameplayTag("Damage.Miss")]
[assembly: GameplayTag("CrowdControl.Stunned")]
[assembly: GameplayTag("CrowdControl.Slow")]
```

### 2. 静态类注册（推荐用于热更新项目）

```csharp
using CycloneGames.GameplayTags.Runtime;

[assembly: RegisterGameplayTagsFrom(typeof(ProjectGameplayTags))]

public static class ProjectGameplayTags
{
    public const string Damage_Fatal       = "Damage.Fatal";
    public const string Damage_Miss        = "Damage.Miss";
    public const string CrowdControl_Stun  = "CrowdControl.Stunned";
}
```

管理器在初始化时扫描 `public const string` 字段并注册。非常适合 HybridCLR 热更新工作流 — 标签集中在生成/静态类中，无需修改资产。

### 3. 运行时动态注册

适用于服务器驱动或运行时生成的标签：

```csharp
// 单个标签
GameplayTagManager.RegisterDynamicTag("Runtime.ServerBuff.SpeedBoost");

// 批量注册
GameplayTagManager.RegisterDynamicTags(new[] { "Event.Seasonal.Summer", "Event.Seasonal.Winter" });

// 从热加载程序集注册（HybridCLR）
GameplayTagManager.RegisterDynamicTagsFromAssembly(hotLoadedAssembly);
```

### 4. JSON 文件（设计师友好）

在 `ProjectSettings/GameplayTags/` 中创建 `.json` 文件：

```json
{
  "Damage.Physical.Slash": {
    "Comment": "来自挥砍武器的伤害。"
  },
  "Damage.Magical.Fire": {
    "Comment": "来自火焰法术的伤害。"
  },
  "Status.Burning": {
    "Comment": "受到持续火焰伤害时施加。"
  }
}
```

编辑器会监控文件变更并自动重新加载。

---

## 核心 API

### GameplayTag

基本单元 — 轻量级结构体，通过名称标识，解析为 `RuntimeIndex` 以实现 O(1) 操作。

```csharp
using CycloneGames.GameplayTags.Runtime;

// 请求标签（未找到时返回 GameplayTag.None）
GameplayTag stunTag = GameplayTagManager.RequestTag("CrowdControl.Stunned");

// 安全请求
if (GameplayTagManager.TryRequestTag("CrowdControl.Stunned", out GameplayTag tag))
{
    Debug.Log($"标签: {tag.Name}, 索引: {tag.RuntimeIndex}");
}

// 层级查询
bool isChild  = stunTag.IsChildOf(ccTag);        // "CrowdControl.Stunned".IsChildOf("CrowdControl") → true
bool isParent = ccTag.IsParentOf(stunTag);        // "CrowdControl".IsParentOf("CrowdControl.Stunned") → true
int  depth    = tagA.MatchesTagDepth(tagB);       // 匹配的层级数

// 属性
string name        = stunTag.Name;                // "CrowdControl.Stunned"
string label       = stunTag.Label;               // "Stunned"
int    level       = stunTag.HierarchyLevel;      // 2
bool   isLeaf      = stunTag.IsLeaf;              // true（无子标签）
bool   isValid     = stunTag.IsValid;             // true 表示已注册
GameplayTag parent = stunTag.ParentTag;           // "CrowdControl"

// 基于 Span 的层级访问（零分配）
ReadOnlySpan<GameplayTag> parents   = stunTag.ParentTags;     // ["CrowdControl"]
ReadOnlySpan<GameplayTag> children  = ccTag.ChildTags;        // ["CrowdControl.Stunned", "CrowdControl.Slow", ...]
ReadOnlySpan<GameplayTag> hierarchy = stunTag.HierarchyTags;  // ["CrowdControl", "CrowdControl.Stunned"]
```

### GameplayTagContainer

可序列化的、层级感知的标签集合。自动维护隐式父标签。

```csharp
// 创建并填充
var container = new GameplayTagContainer();
container.AddTag(GameplayTagManager.RequestTag("Damage.Physical.Slash"));
container.AddTag(GameplayTagManager.RequestTag("Status.Burning"));

// 查询（层级感知：添加 "Damage.Physical.Slash" 会隐式添加 "Damage" 和 "Damage.Physical"）
bool hasDamage  = container.HasTag(damageTag);         // true — 父标签匹配
bool hasExact   = container.HasTagExact(slashTag);     // true — 仅精确匹配
bool hasAll     = container.HasAll(requiredTags);       // 是否包含所有必需标签？
bool hasAny     = container.HasAny(checkTags);          // 是否包含任一标签？

// 移除
container.RemoveTag(slashTag);
container.Clear();

// 枚举（零 GC 结构体枚举器）
foreach (GameplayTag tag in container.GetExplicitTags()) { /* ... */ }
foreach (GameplayTag tag in container.GetTags())         { /* 包含隐式父标签 */ }

// 克隆 / 拷贝
var clone = container.Clone();
container.CopyFrom(otherContainer);

// 差异计算（用于网络同步）
container.GetDiffExplicitTags(previousContainer, addedList, removedList);
```

### GameplayTagCountContainer

维护每个标签的引用计数并提供事件回调 — 非常适合 GAS（Gameplay Ability System）中的 Buff/Debuff 叠层。

```csharp
var countContainer = new GameplayTagCountContainer();

// 在添加标签之前注册回调
countContainer.RegisterTagEventCallback(
    stunTag,
    GameplayTagEventType.NewOrRemoved,
    (tag, count) => Debug.Log($"{tag.Name} {(count > 0 ? "施加" : "移除")}")
);

countContainer.RegisterTagEventCallback(
    stunTag,
    GameplayTagEventType.AnyCountChange,
    (tag, count) => Debug.Log($"{tag.Name} 叠层数: {count}")
);

// 全局事件
countContainer.OnAnyTagNewOrRemove += (tag, count) => { /* 任意标签添加/移除 */ };
countContainer.OnAnyTagCountChange += (tag, count) => { /* 任意计数变化 */ };

// 添加/移除（计数追踪）
countContainer.AddTag(stunTag);      // 计数: 0→1，触发 NewOrRemoved + AnyCountChange
countContainer.AddTag(stunTag);      // 计数: 1→2，仅触发 AnyCountChange
countContainer.RemoveTag(stunTag);   // 计数: 2→1，仅触发 AnyCountChange
countContainer.RemoveTag(stunTag);   // 计数: 1→0，触发 NewOrRemoved + AnyCountChange

// 查询计数
int explicitCount = countContainer.GetExplicitTagCount(stunTag);
int totalCount    = countContainer.GetTagCount(stunTag);  // 包含层级

// 清理
countContainer.RemoveAllTagEventCallbacks();
```

### GameplayTagQuery

支持嵌套布尔逻辑的复杂标签匹配：

```csharp
// 简单：是否包含所有这些标签？
var queryAll = GameplayTagQuery.BuildQueryAll(requiredTags);
bool matches = queryAll.Matches(container);

// 简单：是否包含任一标签？
var queryAny = GameplayTagQuery.BuildQueryAny(optionalTags);

// 复杂：(HasAll[Damage, Status.Burning]) AND (HasNone[Status.Immune])
var query = new GameplayTagQuery
{
    RootExpression = new GameplayTagQueryExpression
    {
        Operator = EGameplayTagQueryExprOperator.All,
        Expressions = new List<GameplayTagQueryExpression>
        {
            new() { Operator = EGameplayTagQueryExprOperator.All, Tags = damageTags },
            new() { Operator = EGameplayTagQueryExprOperator.None, Tags = immuneTags },
        }
    }
};

bool result = query.Matches(container);
```

### GameplayTagRequirements

用于技能激活的简单"必需 + 禁止"检查模式：

```csharp
var requirements = new GameplayTagRequirements
{
    m_RequiredTags  = requiredContainer,   // 必须包含所有这些
    m_ForbiddenTags = forbiddenContainer,  // 必须不包含任何这些
};

// 单容器检查
bool canActivate = requirements.Matches(ownerTags);

// 拆分静态 + 动态容器（如固有标签 + Buff 标签）
bool canActivate2 = requirements.Matches(staticTags, dynamicTags);
```

### GameplayTagRedirector

透明的标签重命名和迁移：

```csharp
// 注册重定向（如版本迁移时）
GameplayTagRedirector.AddRedirect("OldTag.Damage.Fire", "Damage.Magical.Fire");

// 链式重定向自动扁平化：A→B→C 变为 A→C
GameplayTagRedirector.AddRedirect("LegacyTag.Burn", "OldTag.Damage.Fire");
// 现在 "LegacyTag.Burn" 直接解析到 "Damage.Magical.Fire"

// 所有 RequestTag / TryRequestTag 调用自动应用重定向
var tag = GameplayTagManager.RequestTag("OldTag.Damage.Fire"); // → 解析为 "Damage.Magical.Fire"

// 批量注册
GameplayTagRedirector.AddRedirects(new Dictionary<string, string>
{
    { "Old.A", "New.A" },
    { "Old.B", "New.B" },
});

// 查询 / 管理
bool hasRedirect = GameplayTagRedirector.HasRedirect("OldTag.Damage.Fire");
IReadOnlyDictionary<string, string> all = GameplayTagRedirector.GetAllRedirects();
GameplayTagRedirector.RemoveRedirect("OldTag.Damage.Fire");
GameplayTagRedirector.ClearAll();
```

---

## 性能 API

### GameplayTagMask（256 位可 Blittable 位掩码）

32 字节值类型，用于超快速标签操作。适用于 ECS、热循环和网络序列化。

```csharp
// 从标签创建
var mask = GameplayTagMask.FromTag(stunTag);
var mask2 = GameplayTagMask.FromTagWithHierarchy(stunTag); // 包含所有父标签
var mask3 = GameplayTagMask.FromContainer(container);

// O(1) 操作 — 单条位运算指令
mask.AddTag(burnTag);
mask.RemoveTag(burnTag);
bool has     = mask.HasTag(stunTag);
bool hasHier = mask.HasTagHierarchical(stunTag); // 同时检查子标签

// O(1) 批量操作 — 4 条 AND/OR 运算
bool hasAll = mask.HasAll(requiredMask);
bool hasAny = mask.HasAny(checkMask);
bool hasNo  = mask.HasNone(immuneMask);

// 集合代数 — 全部 O(1)
var union = GameplayTagMask.Union(maskA, maskB);
var inter = GameplayTagMask.Intersection(maskA, maskB);
var diff  = GameplayTagMask.Difference(maskA, maskB);

// 遍历已设置的位（通过 RuntimeIndex 产出 GameplayTag）
foreach (GameplayTag tag in mask)
{
    Debug.Log(tag.Name);
}

// 底层位访问（public）
mask.SetBit(42);
mask.ClearBit(42);
bool isSet = mask.IsSet(42);
ulong word = mask.GetWord(0); // 字索引 0–3
```

### GameplayTagMaskLarge（动态大小）

适用于超过 256 种标签类型的项目：

```csharp
var largeMask = new GameplayTagMaskLarge(capacity: 2048);
largeMask.AddTag(someTag);
largeMask.AddTagWithHierarchy(someTag);

bool has    = largeMask.HasTag(someTag);
bool hasAll = largeMask.HasAll(otherLargeMask);
bool hasAny = largeMask.HasAny(otherLargeMask);
```

### ReadOnlyGameplayTagContainer（线程安全快照）

为工作线程、Jobs 或跨帧缓存创建不可变快照：

```csharp
// 创建快照（主线程）
ReadOnlyGameplayTagContainer snapshot = container.CreateSnapshot();

// 在任意线程上使用 — 完全不可变
bool has    = snapshot.HasTag(stunTag);       // ≥64 标签时 O(1)（位集），否则 O(log n)
bool exact  = snapshot.HasTagExact(stunTag);  // O(log n) 二分查找
bool hasAll = snapshot.HasAll(otherContainer);
bool hasAny = snapshot.HasAny(otherSnapshot);

// 访问原始索引（用于自定义处理）
ReadOnlySpan<int> implicit_ = snapshot.GetImplicitIndices();
ReadOnlySpan<int> explicit_ = snapshot.GetExplicitIndices();

// 网络就绪的序列化
byte[] packet = snapshot.Serialize();
```

---

## 网络序列化

### GameplayTagNetSerializer

框架无关的二进制序列化，适用于任何网络层：

```csharp
// --- 全量快照 ---
byte[] fullPacket = GameplayTagNetSerializer.SerializeFull(container);
// 线路格式: [version:1][marker:0xFE][count:2][index:2 × N] = 4 + 2N 字节

// 远端反序列化
GameplayTagNetSerializer.DeserializeFull(remoteContainer, fullPacket);

// --- 增量（仅变化的标签）---
byte[] deltaPacket = GameplayTagNetSerializer.SerializeDelta(currentContainer, previousContainer);
// 线路格式: [version:1][marker:0xFD][addCount:2][idx...][removeCount:2][idx...] = 6 + 2(A+R) 字节

// 远端应用增量
GameplayTagNetSerializer.ApplyDelta(remoteContainer, deltaPacket);

// --- 自动检测 ---
GameplayTagNetSerializer.Deserialize(remoteContainer, packet);
// 通过标记字节自动检测全量 vs 增量

// --- 预分配缓冲区 ---
int size = GameplayTagNetSerializer.GetFullSerializedSize(container);
byte[] buffer = new byte[size];
int written = GameplayTagNetSerializer.SerializeFull(container, buffer, offset: 0);

// --- 256 位掩码（精确 32 字节，unsafe memcpy）---
byte[] maskBuffer = new byte[32];
GameplayTagNetSerializer.SerializeMask(mask, maskBuffer, offset: 0);
GameplayTagMask remoteMask = GameplayTagNetSerializer.DeserializeMask(maskBuffer, offset: 0);
```

**集成示例（Mirror）**：

```csharp
public class TagSyncBehaviour : NetworkBehaviour
{
    private GameplayTagContainer _serverTags = new();
    private GameplayTagContainer _previousTags = new();

    [Server]
    void UpdateTags()
    {
        byte[] delta = GameplayTagNetSerializer.SerializeDelta(_serverTags, _previousTags);
        _previousTags.CopyFrom(_serverTags);
        RpcApplyTagDelta(delta);
    }

    [ClientRpc]
    void RpcApplyTagDelta(byte[] data)
    {
        GameplayTagNetSerializer.Deserialize(_clientTags, data);
    }
}
```

---

## ECS / Burst / Jobs

> 需要 `com.unity.collections`、`com.unity.burst` 和/或 `com.unity.entities`。功能通过 `versionDefines` 自动启用。

### GameplayTagMaskComponent（ECS）

```csharp
// 添加到实体
EntityManager.AddComponentData(entity, new GameplayTagMaskComponent
{
    Mask = GameplayTagMask.FromContainer(container)
});

// 在 System 中查询
foreach (var (tags, entity) in SystemAPI.Query<RefRW<GameplayTagMaskComponent>>().WithEntityAccess())
{
    if (tags.ValueRO.HasTag(stunTag))
    {
        // 跳过被眩晕实体的移动
    }

    tags.ValueRW.SetTag(burnTag);    // 添加标签
    tags.ValueRW.ClearTag(burnTag);  // 移除标签

    bool all = tags.ValueRO.HasAll(requiredMask);
    bool any = tags.ValueRO.HasAny(checkMask);
}
```

### 网络复制的变化检测

```csharp
// 实体同时拥有 GameplayTagMaskPrevious 和 GameplayTagsDirty 组件

foreach (var (current, previous, dirty, entity) in
    SystemAPI.Query<RefRO<GameplayTagMaskComponent>, RefRW<GameplayTagMaskPrevious>,
                    RefRW<GameplayTagsDirty>>()
    .WithEntityAccess())
{
    if (current.ValueRO.Mask != previous.ValueRO.Mask)
    {
        // 掩码已变化 — 标记为脏，等待复制
        SystemAPI.SetComponentEnabled<GameplayTagsDirty>(entity, true);
        previous.ValueRW.Mask = current.ValueRO.Mask;
    }
}
```

### NativeGameplayTagMask（Burst/Jobs）

```csharp
// 在主线程创建
var nativeMask = new NativeGameplayTagMask(Allocator.TempJob);
nativeMask.Set(stunTag);
nativeMask.Set(burnTag);

// 或从托管数据拷贝
nativeMask.CopyFrom(managedMask);        // 从 GameplayTagMask
nativeMask.CopyFrom(container);           // 从 IGameplayTagContainer
nativeMask.CopyFrom(readOnlySnapshot);    // 从 ReadOnlyGameplayTagContainer

// 在 Jobs 中使用
var job = new TagCheckJob { RequiredTags = nativeMask };
job.Schedule(entityCount, 64).Complete();

// 位运算（Burst 兼容）
NativeGameplayTagMask.And(in maskA, in maskB, Allocator.Temp, out var result);
NativeGameplayTagMask.Or(in maskA, in maskB, Allocator.Temp, out var result2);

bool containsAll = nativeMask.ContainsAll(otherMask);
bool containsAny = nativeMask.ContainsAny(otherMask);

// 清理
nativeMask.Dispose();
result.Dispose();
```

---

## 编辑器工具

### Gameplay Tag 管理器窗口

**菜单**：`Tools > CycloneGames > Gameplay Tag Manager`

- 所有已注册标签的完整树状视图
- 搜索栏快速过滤
- 右键上下文菜单：**添加标签**、**删除标签**、**创建子标签**（自动预填父路径）
- 刷新按钮重新加载标签并触发 Source Generator

### Inspector 集成

`GameplayTag` 和 `GameplayTagContainer` 字段拥有自定义属性绘制器：

- **GameplayTag**：带可搜索弹出窗口的下拉菜单，显示完整标签树
- **GameplayTagContainer**："编辑标签"按钮打开带复选框的弹出窗口，附带"全部清除"和逐标签移除按钮
- 无效标签以红色高亮显示

### 标签验证工具

**菜单**：`Tools > CycloneGames > GameplayTags > Tag Validation Window`

- 扫描所有 Prefab、ScriptableObject 和已打开场景中的无效 `GameplayTagContainer` 引用
- 显示资产路径、无效标签名称和上下文对象
- **修复**按钮移除单个无效标签
- **全部修复**按钮批量清理

### Source Generator

`GameplayTagsSourceGenerator` 自动生成带编译时 `GameplayTag` 常量的静态类：

```csharp
// 生成的代码（示例）
public static class GTags
{
    public static readonly GameplayTag Damage_Physical_Slash = GameplayTagManager.RequestTag("Damage.Physical.Slash");
    public static readonly GameplayTag CrowdControl_Stunned  = GameplayTagManager.RequestTag("CrowdControl.Stunned");
    // ...
}

// 使用 — 零魔法字符串，IDE 自动完成，拼写错误时编译报错
if (container.HasTag(GTags.CrowdControl_Stunned)) { /* ... */ }
```

---

## 构建流水线

标签在 `IPreprocessBuildWithReport` 期间编译为高效二进制格式：

```
[byte version=1] [int tagCount] [string tagName × tagCount] [uint crc32]
```

- **版本字节**确保前向兼容
- **CRC32 校验和**检测数据损坏（磁盘 I/O 错误、截断）
- 运行时，`BuildGameplayTagSource` 从 `Resources` 读取此二进制 — 无 JSON 解析开销

---

## HybridCLR / 热更新支持

系统为热更新工作流设计：

```csharp
// 加载热更新程序集后：
GameplayTagManager.RegisterDynamicTagsFromAssembly(hotLoadedAssembly);

// 或从热更新程序集中的类型注册：
GameplayTagManager.RegisterDynamicTagsFromType(typeof(HotUpdateTags));

// 或从服务器配置注册：
GameplayTagManager.RegisterDynamicTags(serverTagList);
```

动态注册会自动重建 `TagDataSnapshot` 并原子发布。

---

## 依赖

- **无**（Core 程序集是纯 C#）

### 可选

| 包 | 用途 |
|----|------|
| `com.unity.collections` | `NativeGameplayTagMask` |
| `com.unity.burst` | 原生掩码操作的 `[BurstCompile]` |
| `com.unity.entities` | `GameplayTagMaskComponent`（IComponentData） |
| Newtonsoft.Json | JSON 标签文件解析（仅编辑器） |
