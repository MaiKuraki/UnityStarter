# CycloneGames.RPGFoundation Projectile

[English](./README.md) | 简体中文

`CycloneGames.RPGFoundation.Projectile` 为 RPG、动作 RPG、横板 2D、俯视角 2D、2.5D 和高密度弹幕游戏提供可复用的抛射物仿真基础。模块将纯抛射物规则与 Unity 场景绑定、物理适配、确定性定点仿真、表现对象池和网络同步分离。

当前实现优先建立稳定的生产边界：固定容量运行时状态、generation-safe handle、预热后低分配步进、Unity Physics non-alloc adapter、可选 `DeterministicMath` 仿真，以及独立网络包中的 transport-neutral 协议契约。

## 模块布局

```text
Projectile/
  Core/
    CycloneGames.RPGFoundation.Projectile.Core.asmdef
  Runtime/
    CycloneGames.RPGFoundation.Projectile.Runtime.asmdef
    Integrations/DeterministicMath/
  Editor/
    CycloneGames.RPGFoundation.Projectile.Editor.asmdef
  Tests/Editor/
```

| 区域 | 用途 |
| --- | --- |
| `Core/` | Unity-free 的 projectile definition、`ProjectileVector3`、spawn request、handle、snapshot、collision contract、hit event、target contract 和 `ProjectileWorld`。 |
| `Runtime/` | Unity `ScriptableObject` authoring、`MonoBehaviour` 生命周期桥接、Physics 2D/3D non-alloc adapter，以及基于 `CycloneGames.Factory` 的 view pool helper。 |
| `Runtime/Integrations/DeterministicMath/` | 可选 fixed-point simulator 和 payload helper，用于 lockstep、rollback、replay、server verification 和 seed-based bullet pattern reconstruction。 |
| `Editor/` | projectile definition 和 system capacity/profile 的自定义 Inspector。 |
| `Tests/Editor/` | core stepping、homing、hit event、deterministic simulation 和 integration boundary 的 EditMode 测试。 |

## 程序集边界

| Assembly | 职责 | UnityEngine 依赖 |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Projectile.Core` | 纯运行时契约和固定容量 projectile simulation。 | 无 |
| `CycloneGames.RPGFoundation.Projectile.Runtime` | Unity authoring、生命周期、Physics 2D/3D adapter 和 Factory-backed view pooling。 | 有 |
| `CycloneGames.RPGFoundation.Projectile.Integrations.DeterministicMath` | Fixed-point projectile state、payload、simulator 和 conversion helper。 | 无 UnityEngine |
| `CycloneGames.RPGFoundation.Projectile.Editor` | Inspector 和 authoring support。 | 仅 Editor |
| `CycloneGames.RPGFoundation.Projectile.Tests.Editor` | Core 与 Runtime EditMode 测试。 | 仅 Editor |

Core assembly 不引用任何 Unity package，并设置 `noEngineReferences` 为 `true`。它使用 `ProjectileVector3`，不暴露 `UnityEngine.Vector3` 或 `Unity.Mathematics.float3`，因此同一套契约可以被 Unity client、CLI test、headless server、replay tool 和外部仿真程序使用。

## 核心概念

| 类型 | 用途 |
| --- | --- |
| `ProjectileDefinition` | 不可变运行时配置：速度、加速度、重力倍率、半径、生命周期、引导模式、穿透、反弹、碰撞 mask 和 effect payload id。 |
| `ProjectileDefinitionValidator` | Unity-free 校验辅助，会把 issue 写入调用方提供的数组，供 Inspector、CI、批量资产检查和命令行工具复用。 |
| `ProjectileVector3` | Unity-free 的 3D 值类型，被所有 Core contract 和 snapshot 使用。Unity-facing adapter 只在边界处转换。 |
| `ProjectileSpawnRequest` | Spawn-time command，包含 owner id、projectile id、target id、tick、prediction key、seed、position、direction 和可选初始速度。 |
| `ProjectileHandle` | Slot + generation handle，防止 despawn 和 slot reuse 后出现 stale reference。 |
| `ProjectileWorld` | 固定容量 dense projectile store，构造后步进路径不需要额外分配。 |
| `ProjectileWorldStats` | 只读运行时诊断数据，记录 active count、peak count、spawn rejection、hit event overflow、collision query count 和 iteration-limit pressure。 |
| `ProjectileState` | `ProjectileWorld` 拥有的可变单 projectile 运行时状态。 |
| `ProjectileSnapshot` | 面向网络和表现层的 state projection。 |
| `ProjectileHitEvent` | `ProjectileWorld.Step` 期间写入固定容量 event buffer 的命中结果。 |
| `IProjectileCollisionWorld` | Non-alloc sweep query 边界。Unity Physics、deterministic world 或 server collision map 都通过它接入。 |
| `IProjectileTargetProvider` | Homing 和 lead-homing 使用的 target position / velocity 查询接口。 |
| `ProjectileSpaceProfile` | 3D、横板 2D、俯视角 2D 和 2.5D 风格 gameplay space 的平面映射。 |

## 仿真模式

`ProjectileSpaceProfile` 让同一套 projectile logic 支持常见 RPG 摄像机和世界布局。

| Profile | Gameplay plane | 典型用途 |
| --- | --- | --- |
| `Full3D` | XYZ | 3D RPG、MMO、动作战斗、飞行导弹。 |
| `SideScroller2D` | XY，锁定 Z | 平台动作、Contra 风格横板射击。 |
| `TopDown2D` | XZ，锁定 Y | 俯视角 RPG、arena shooter、MOBA-like combat。 |

2.5D 游戏通常用 `TopDown2D` 作为权威规则层，再在表现层添加视觉高度或弧线。除非项目明确需要真实 3D hit volume，否则不要把 cosmetic height 混入 authoritative collision。

## 基础用法

通过以下菜单创建 definition asset：

```text
Create > CycloneGames > RPGFoundation > Projectile > Definition
```

通过场景中的 `ProjectileSystemBehaviour` 发射：

```csharp
using CycloneGames.RPGFoundation.Projectile.Core;
using CycloneGames.RPGFoundation.Projectile.Runtime;
using UnityEngine;

public sealed class WandAttack : MonoBehaviour
{
    [SerializeField] private ProjectileSystemBehaviour Projectiles;
    [SerializeField] private ProjectileDefinitionAsset ArcaneMissile;

    public bool Fire(ulong ownerId, ulong projectileId, Vector3 muzzle, Vector3 direction, ulong targetId)
    {
        return Projectiles.TrySpawn(
            ArcaneMissile,
            muzzle,
            direction,
            ownerId,
            projectileId,
            out ProjectileHandle handle,
            targetId);
    }
}
```

在 gameplay composition root 订阅 hit event，并转发到 damage、ability 或 cue 系统：

```csharp
projectileSystem.Hit += hit =>
{
    // Resolve target id, apply GameplayAbilities effects, then dispatch cues.
};
```

## GameplayAbilities 集成

Projectile Core 不直接引用 `CycloneGames.GameplayAbilities`。类似火球术的能力应在项目层或可选 integration 层组合：

1. `GameplayAbility` 校验消耗、冷却、tag、目标、所有权和 prediction。
2. Ability 创建一个或多个 `ProjectileSpawnRequest`，写入稳定 definition id、owner id、projectile network id、target id、start tick、prediction key 和 seed。
3. `ProjectileWorld` 负责移动、sweep collision、生命周期、穿透、反弹和 hit event。
4. Gameplay composition root 根据 `ProjectileHitEvent.EffectPayloadId` 或 `ProjectileDefinitionId` 映射到 `GameplayEffect` / cue policy。
5. Server 应用权威效果，并通过 networking package 同步 hit、despawn 和 correction。

这种结构让 projectile simulation 可复用于非 GAS 游戏，同时也能支持暗黑类火球、飞弹、陷阱和区域 projectile ability 与 GameplayAbilities 协同。

## Homing 与弹幕

`ProjectileGuidanceMode.Homing` 会按受限转向速度将 projectile velocity 旋向 target position。`LeadHoming` 会额外加入 `TargetVelocity * LeadPredictionTime`，适合需要拦截移动目标的跟踪导弹。

| Ability | 建议设置 |
| --- | --- |
| 奥术飞弹 | 短时间内发出多个 spawn request，使用 `Homing`，中等 turn rate，server-authoritative hit。 |
| Contra 风格跟踪导弹 | 使用 `LeadHoming`，较长 lifetime，较低 turn rate，明显可见的转弯半径，通常关闭 bounce。 |
| Bullet hell ring | 项目自有 emitter 根据 `seed`、angle step、definition id 和 start tick 生成多个 `ProjectileSpawnRequest`。 |

高密度弹幕应避免每颗 projectile 一个 GameObject。权威规则可使用 `ProjectileWorld`，表现层应使用项目专用 instanced renderer 或 Native/Burst 路径。

## 碰撞与防穿墙

`ProjectileWorld` 要求 collision world 执行从 `PreviousPosition` 到 `Position` 的 swept query。Unity adapter 在 3D 中使用 `SphereCastNonAlloc`，在 2D 中使用 `CircleCastNonAlloc`，因此高速 projectile 不只依赖最终位置的 overlap 检查。

当 projectile 反弹或穿透时，world 会沿剩余 sweep distance 继续处理，并使用固定 collision iteration budget。默认 budget 为 `4`，可以覆盖常见的一帧反弹和多目标穿透，同时保证热路径有明确上限。极高速 projectile、很薄的 collider 或大量穿透目标，需要项目同时调节 fixed timestep、projectile radius、collision adapter filtering 和 iteration budget。

Core interface 不直接拥有 Unity collider filtering。Server world、deterministic map 和项目专用 Unity adapter 应在需要时实现 target ignore list、owner filtering、friendly-fire rule 和 duplicate-hit suppression。

## DeterministicMath 集成

可选 deterministic assembly 由 `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH` 启用，并引用 `CycloneGames.DeterministicMath.Core`。

该 integration 在 projectile 边界显式选择 zero-vector fallback：zero spawn direction 会生成 zero initial velocity；当 homing target 与 projectile 位于同一点时，则保留当前飞行方向。此策略只属于 projectile simulation；基础 `FPVector3.Normalized` 契约仍保持 fail-fast。

适用场景：

- Lockstep 或 rollback projectile simulation。
- Replay 或战斗记录校验。
- Server validation of predicted projectile paths。
- 使用 seed-based bullet pattern reconstruction，避免逐颗 projectile snapshot replication。
- 跨平台 bit-identical fixed-point homing 结果。

纯表现 missile 或普通 server-authoritative RPG projectile 不应默认使用 fixed-point，除非产品明确要求 deterministic replay。

## 数值模型

默认 `ProjectileWorld` 通过 `ProjectileVector3` 使用 `float`。这适合 server-authoritative multiplayer：server 是事实来源，client 使用 prediction 和 correction 完成表现。

当同一条 projectile path 必须在不同机器、构建或 replay 中 bit-identical 时，使用 `DeterministicMath` integration。典型场景包括 lockstep、rollback、权威战斗记录回放、从 `seed` 和 `startTick` 确定性重建弹幕，以及通过对比仿真结果进行 anti-cheat verification。

不要把 `DeterministicMath` 变成所有游戏的 Core 必需依赖。Fixed-point 的 authoring 和推理成本高于 float simulation，很多 RPG projectile 只需要 server authority、stable id、reliable hit message 和少量 correction。

## 网络

网络同步位于独立包 `CycloneGames.RPGFoundation.Projectile.Networking`。基础 Projectile 模块保持 network-agnostic。

推荐多人策略：

- Server 拥有 authoritative spawn、hit、despawn 和 correction。
- Client 可使用 `PredictionKey` 生成 predicted visual。
- 弹幕 pattern 在项目策略允许时同步 stable definition id、seed 和 start tick 来重建。
- 长生命周期或追踪 projectile 定期接收 authoritative snapshot。
- Hit message 使用 reliable channel，并且只在 server validation 后应用。

Networking 包通过 protocol manifest、`NetworkVector3`、`NetworkActionResult`、snapshot history、message validator、reconciliation policy 和 authority bridge interface 与 `CycloneGames.Networking` 协同。它保持 transport-neutral，不会把 Projectile.Core 绑定到具体网络后端。

## 性能模型

`ProjectileWorld` 在构造时分配 state array、slot map、free-list、collision hit buffer 和 hit event buffer。这是 world storage，不是通用对象池。它使用 dense store 和 generation-safe handle，是因为 projectile 是短生命周期仿真实体。GameObject 表现层对象池仍交给 `CycloneGames.Factory`。

预热后：

- Spawn 和 despawn 为 O(1)。
- Step active projectile 为 O(n)。
- Despawn 使用 swap-remove 和 generation-safe handle。
- Hit event 写入当前 step 的固定容量 buffer。
- Unity Physics adapter 使用 `SphereCastNonAlloc` 和 `CircleCastNonAlloc`。
- Bounce 和 pierce continuation 使用固定 iteration budget，不产生分配。
- `ProjectileWorldStats` 不分配地记录运行时压力，Inspector、debug overlay 或 server telemetry 可以观察容量和 event buffer 问题。

容量不足会返回 `false`，不会在热路径静默扩容。请按 encounter、room、server shard 或 bullet-pattern profile 配置容量。

## Editor 工具

`ProjectileDefinitionAssetEditor` 和 `ProjectileSystemBehaviourEditor` 使用共享的 `CycloneGames.RPGFoundation.Editor` Inspector UI 工具。它们提供分组 authoring 面板、校验消息、运行时状态和容量提示。

Inspector 设计时考虑了业务扩展：

- `ProjectileDefinitionAsset` 和 `ProjectileSystemBehaviour` 可以被继承。
- `ProjectileDefinitionAsset.BuildDefinition` 是 virtual，产品代码可以扩展 authoring data，而不需要修改 `Projectile.Core`。
- `ProjectileDefinitionAsset.BuildAuthoringDefinition` 暴露 runtime sanitization 前的 raw authoring 值，用于校验。
- `ProjectileSystemBehaviour` 暴露 protected virtual 创建 hook，用于扩展 space profile、projectile world、collision world 和 hit dispatch。
- 自定义 Inspector 使用 `[CustomEditor(typeof(...), true)]`，并在内置分区后绘制未处理的 serialized fields。业务子类可以新增 `[SerializeField]` 字段，Inspector 不会吞掉这些字段。

Projectile definition inspector 提供 Fireball、Arcane Missile、Homing Missile 和 Ricochet preset。Preset 只调整基础 projectile tuning，并保留 definition id、collision mask、effect payload id 和派生类 extension field 等项目身份数据。

Validation 由 Unity-free `ProjectileDefinitionValidator` 提供，因此同一套规则可以复用于自定义 Editor window、CI asset scan、import processor 或外部工具。Runtime 代码在构建 Core definition 前仍会 clamp 不安全的数值。

Play Mode 下，`ProjectileSystemBehaviourEditor` 会显示 `ProjectileWorldStats`，包括 active count、peak active count、spawn rejection pressure、hit overflow 和 collision iteration pressure。

## 线程模型

`ProjectileWorld` 是 single-writer 设计。应由一个 simulation thread 拥有，并在明确 simulation barrier 回放外部 command。其他线程应通过项目自有 command queue 提交 spawn、despawn 和 target update。

Unity-facing `ProjectileSystemBehaviour`、Physics adapter 和 `ProjectileViewPool` 必须运行在 Unity 主线程。纯 Core 和 DeterministicMath state 可以运行在不接触 Unity object 的 headless 或 server context。

## 持久化

本模块不写文件、资产、`PlayerPrefs`、`EditorPrefs`、`SessionState`、隐藏缓存或 runtime save data。`ProjectileDefinitionAsset` authoring data 存储在项目显式创建的 Unity asset 中。Runtime projectile state 保存在内存里，只有项目需要 save/replay 时才应由项目专用系统捕获。

## 验证

修改模块后运行：

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.DeterministicMath.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
```

验证 Unity Physics adapter 时，创建最小 2D 和 3D 场景，放置 `ProjectileSystemBehaviour`、definition asset 和对应 layer 的 collider。确认 hit event 只触发一次、terminal hit 后 stale handle 失效，并且 pooled view 能在清理路径返回池。
