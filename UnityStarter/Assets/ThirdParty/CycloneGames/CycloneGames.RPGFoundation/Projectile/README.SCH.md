# CycloneGames.RPGFoundation Projectile

[English](README.md) | 简体中文

一个可复用的抛射物仿真基础模块，适用于 RPG、动作 RPG、横板 2D、俯视角 2D、2.5D 和高密度弹幕游戏。将纯抛射物规则与 Unity 场景绑定、物理适配、确定性定点仿真、表现对象池和网络同步分离。

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

`ProjectileWorld` 是一个固定容量的 dense projectile store，每 tick 遍历活跃 projectile 进行速度积分、制导转向、swept collision 查询、生命周期管理和命中事件触发。Core assembly 不依赖 Unity（`noEngineReferences: true`），使用 `ProjectileVector3` 代替 `UnityEngine.Vector3`。

### 主要特性

- **固定容量仿真** — Dense store，generation-safe handle，O(1) spawn/despawn
- **三种空间模式** — Full3D、SideScroller2D、TopDown2D
- **Homing 和 lead-homing** — 可配置转向速度和目标预测
- **反弹与穿透** — 固定迭代预算，non-alloc sweep continuation
- **Non-alloc 碰撞 adapter** — `SphereCastNonAlloc`（3D），`CircleCastNonAlloc`（2D）
- **可选 DeterministicMath** — 用于 lockstep、rollback、replay 的定点仿真
- **GameplayAbilities 集成** — 支持火球/ability 模式，无硬依赖
- **Editor 工具** — Preset、校验、运行时统计叠加

## 架构

### 模块布局

```text
Projectile/
  Core/               CycloneGames.RPGFoundation.Projectile.Core.asmdef
  Runtime/            CycloneGames.RPGFoundation.Projectile.Runtime.asmdef
    Integrations/DeterministicMath/
  Editor/             CycloneGames.RPGFoundation.Projectile.Editor.asmdef
  Tests/Editor/
```

### 程序集边界

| Assembly | 职责 | UnityEngine |
| --- | --- | --- |
| `Projectile.Core` | Unity-free 契约、`ProjectileWorld`、snapshot、collision contract、hit event | 无 |
| `Projectile.Runtime` | Unity authoring、生命周期、Physics 2D/3D adapter、view pooling | 有 |
| `Projectile.Integrations.DeterministicMath` | Fixed-point state、payload、simulator | 无 |
| `Projectile.Editor` | Inspector 和 authoring support | 仅 Editor |

## 快速上手

通过 `Create > CycloneGames > RPGFoundation > Projectile > Definition` 创建 definition asset。

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

订阅命中事件：

```csharp
projectileSystem.Hit += hit =>
{
    // 解析 target id，应用 GameplayAbilities effect，分发 cue。
};
```

## 核心概念

| 类型 | 用途 |
| --- | --- |
| `ProjectileDefinition` | 不可变配置：速度、加速度、重力倍率、半径、生命周期、制导、穿透、反弹、collision mask、effect payload id。 |
| `ProjectileVector3` | 所有 Core 契约使用的 Unity-free 3D 值类型。 |
| `ProjectileSpawnRequest` | Spawn command，包含 owner id、projectile id、target id、tick、prediction key、seed、position、direction。 |
| `ProjectileHandle` | Slot + generation handle，防止 despawn 后出现 stale reference。 |
| `ProjectileWorld` | 固定容量 dense store，步进路径使用预分配 world storage。 |
| `ProjectileState` | 每 projectile 的可变运行时状态。 |
| `ProjectileHitEvent` | `ProjectileWorld.Step` 期间写入固定容量 buffer 的命中结果。 |
| `IProjectileCollisionWorld` | Non-alloc sweep query 边界 — Unity Physics、deterministic world 或 server collision map。 |
| `IProjectileTargetProvider` | Homing 使用的 target position/velocity 查询接口。 |
| `ProjectileSpaceProfile` | 3D、横板 2D、俯视角 2D 和 2.5D 的平面映射。 |

### 仿真模式

| Profile | Gameplay plane | 典型用途 |
| --- | --- | --- |
| `Full3D` | XYZ | 3D RPG、MMO、动作战斗、飞行导弹 |
| `SideScroller2D` | XY，锁定 Z | 平台动作、Contra 风格横板射击 |
| `TopDown2D` | XZ，锁定 Y | 俯视角 RPG、arena shooter、MOBA-like combat |

2.5D 游戏通常用 `TopDown2D` 作为权威规则层，再在表现层添加视觉高度。

## 使用指南

### GameplayAbilities 集成

Projectile Core 不直接引用 `CycloneGames.GameplayAbilities`。火球类能力应在项目层或可选 integration 层组合：

1. `GameplayAbility` 校验消耗、冷却、tag、目标和 prediction。
2. Ability 创建 `ProjectileSpawnRequest`，写入稳定 id、target id、start tick、prediction key 和 seed。
3. `ProjectileWorld` 负责移动、sweep collision、生命周期、穿透、反弹和 hit emission。
4. Gameplay composition root 将 `ProjectileHitEvent.EffectPayloadId` 映射到 `GameplayEffect` / cue policy。
5. Server 应用权威效果，同步 hit、despawn 和 correction。

### Homing 与弹幕

`Homing` 按受限转向速度将 velocity 旋向 target。`LeadHoming` 额外加入 `TargetVelocity × LeadPredictionTime`。

| Ability | 设置 |
| --- | --- |
| 奥术飞弹 | 短时间内多个 spawn request，使用 `Homing`，中等 turn rate |
| Contra 风格跟踪导弹 | `LeadHoming`，较长 lifetime，较低 turn rate，可见转弯半径 |
| Bullet hell ring | Emitter 根据 `seed`、angle step、definition id、start tick 创建 request |

高密度弹幕应避免每颗 projectile 一个 GameObject。权威规则使用 `ProjectileWorld`，表现层使用 instanced renderer 或 Native/Burst 路径。

### 碰撞与防穿墙

`ProjectileWorld` 要求 collision world 从 `PreviousPosition` 到 `Position` 执行 swept query。Unity adapter 使用 `SphereCastNonAlloc`（3D）和 `CircleCastNonAlloc`（2D）。当 projectile 反弹或穿透时，world 以固定 collision iteration budget（默认 4）继续。

Core 不直接拥有 collider filtering。应在项目专用 adapter 中添加 target ignore list、owner filtering、friendly-fire rule 和 duplicate-hit suppression。

## 进阶主题

### DeterministicMath 集成

由 `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH` 启用。Integration 在 projectile 边界显式选择 zero-vector fallback：zero spawn direction 产生 zero initial velocity，homing target 与 projectile 重合时保留当前方向。

适用于 lockstep/rollback simulation、replay verification、seed-based bullet pattern reconstruction，或跨平台 bit-identical fixed-point homing。

### 数值模型

默认 `ProjectileWorld` 通过 `ProjectileVector3` 使用 `float` — 适合 server-authoritative multiplayer，server 是事实来源。

当同一 projectile path 必须跨机器 building 或 replay 达到 bit-identical 时，使用 `DeterministicMath` integration。不要把 `DeterministicMath` 变成必需依赖 — 很多 RPG projectile 只需要 server authority、stable id 和可靠的 hit message。

### 网络

网络同步位于独立包 `CycloneGames.RPGFoundation.Projectile.Networking`。推荐策略：

- Server 拥有 authoritative spawn、hit、despawn 和 correction。
- Client 可使用 `PredictionKey` 生成 predicted visual。
- 弹幕 pattern 同步 stable definition id、seed 和 start tick。
- 长生命周期 projectile 定期接收 authoritative snapshot。
- Hit message 使用 reliable channel，仅在 server validation 后应用。

### 线程模型

`ProjectileWorld` 是 single-writer 设计。由一个 simulation thread 拥有，在已知 simulation barrier 接收 command。Unity-facing adapter 必须运行在主线程。纯 Core 和 DeterministicMath state 可在 headless/server context 运行。

## 常见场景

### Editor Authoring

`ProjectileDefinitionAssetEditor` 和 `ProjectileSystemBehaviourEditor` 提供分组 authoring 面板、校验消息和 preset（Fireball、Arcane Missile、Homing Missile、Ricochet）。`ProjectileDefinitionAsset` 和 `ProjectileSystemBehaviour` 可被继承。Play Mode 下 `ProjectileWorldStats` 显示 active count、peak count、spawn rejection、hit overflow 和 collision iteration pressure。

### 持久化

本模块不写入文件、资产、`PlayerPrefs` 或 runtime save data。`ProjectileDefinitionAsset` 数据存储在项目创建的 Unity asset 中。Runtime state 保存在内存中，仅当项目需要 save/replay 时才由项目系统捕获。

### 验证

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.DeterministicMath.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
```

测试 physics adapter 时，创建最小场景放置 `ProjectileSystemBehaviour`、definition asset 和对应 layer 的 collider。确认 hit event 只触发一次、terminal hit 后 stale handle 失效，且 pooled view 在清理路径返回池。

## 性能与内存

`ProjectileWorld` 在构造时分配 state array、slot map、free-list 和 hit event buffer — 这是 world storage，不是通用对象池。

预热后：

- Spawn 和 despawn：O(1)。
- Stepping：活跃 projectile 的 O(n)。
- Despawn 使用 swap-remove 和 generation-safe handle。
- Hit event 写入固定容量 buffer。
- Unity adapter 使用 `SphereCastNonAlloc` / `CircleCastNonAlloc`。
- Bounce/pierce 使用固定 iteration budget — 无分配。
- `ProjectileWorldStats` 不分配地记录压力。

容量不足返回 `false`，不会静默扩容。按 encounter、room、server shard 或 bullet-pattern profile 配置容量。

## 故障排查

| 现象                   | 原因                           | 解决方法                                         |
| ---------------------- | ------------------------------ | ------------------------------------------------ |
| Projectile 未生成      | World 容量已满                 | 检查 `ProjectileWorldStats` 的 spawn rejection；扩容 |
| Despawn 后 handle 失效 | 在 projectile 销毁后使用 handle | Generation 校验阻止重用；通过 spawn 获取新 handle |
| 碰撞丢失               | 探测半径太小或穿墙             | 增大 radius，调整 fixed timestep 或 iteration budget |
| Hit event 重复触发     | 缺少 duplicate-hit suppression | 在项目专用 collision adapter 中添加 target ignore list |
| 确定性不匹配           | 浮点精度差异                   | 切换到 `DeterministicMath` integration 实现 bit-identical |
| 性能问题               | 活跃 projectile 数量过多       | 测量 stepping cost；高密度场景使用 instanced renderer |
