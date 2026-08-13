# CycloneGames.GameplayFramework.Factory

[English](README.md)

## 模块概述

本包把 GameplayFramework 的 `IActorLifetime` seam 连接到 CycloneGames.Factory 的对称 Unity object lifetime 契约。`FactoryActorLifetime` 把 Actor 创建与永久释放委托给 composition root 提供的同一个 `IUnityObjectLifetime`。

GameplayFramework 不依赖 Factory。只有产品已经通过 CycloneGames.Factory 组装 Unity object lifetime 时，才安装并引用本包。

## 程序集与依赖

| 程序集 | 用途 | Consumer 引用方式 |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.Factory` | 把 `IUnityObjectLifetime` 适配为 `IActorLifetime`。 | 显式 |
| `CycloneGames.GameplayFramework.Integrations.Factory.Tests.Editor` | 验证 create/release identity 与 Actor 已销毁时的处理。 | 仅 Test Runner |

本包通过 UPM 显式依赖 GameplayFramework 与 Factory。Runtime assembly 使用 `autoReferenced: false`；构造 `FactoryActorLifetime` 的项目 assembly 必须显式引用它。

## 安装

### UPM

安装 `com.cyclone-games.gameplay-framework-factory`。Package Manager 会解析 `com.cyclone-games.gameplay-framework` 与 `com.cyclone-games.factory`，不需要 Scripting Define Symbol。

### 嵌入 Assets

将本包、`CycloneGames.GameplayFramework` 与 `CycloneGames.Factory` 放入项目 Assets 目录。三个 package root 均存在时，direct asmdef reference 会编译 integration。基于 Assets 的项目不使用 Factory 时，应移除本 integration package root。

## 组合

创建一个 Factory lifetime，并通过与非 DI 模式相同的显式 composition path 传入 adapter：

```csharp
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.Factory;

IUnityObjectLifetime unityLifetime = new DefaultUnityObjectSpawner();
IActorLifetime actorLifetime = new FactoryActorLifetime(unityLifetime);

var composition = new GameplayWorldComposition(actorLifetime);
gameplayWorldHost.Configure(composition);
```

DI container 注册相同 concrete object，并提供相同 constructor。两个 assembly 都不会解析 container，也不使用 Service Locator。

## 所有权与生命周期

- `GameInstance` 把配置的 `IActorLifetime` 传给每个 `World`。
- `World` 成为每个 `Create` 返回 Actor 的唯一 owner。
- Spawn rollback、显式销毁 Actor 与 World shutdown 都通过 `Release` 终止 owned instance。
- 即使 Actor 在 `EndPlay` 中自行销毁，`Release` 仍是终止操作；adapter 会继续转发该 Actor reference，让 Factory lifetime 完成自身 accounting。
- Scene 与 externally registered Actor 不会向 injected lifetime 转移 ownership。显式 `DestroyActor` 请求通过 GameplayFramework core 的 Unity destruction path 终止它们。

## Pooling 边界

`FactoryActorLifetime` 不使用 `IMemoryPool`、`FastObjectPool`、`MonoFastPool`、`Despawn` 或 `Return`。GameplayFramework Actor 在 release 前进入终止生命周期状态，绝不会再次提供复用。Actor pooling 需要独立的 reset、lease invalidation、stale-reference、double-return 和 component-state 契约，本包不提供这些能力。

## 性能与线程

创建与释放是 Unity 主线程上的生命周期冷路径。Adapter 增加一次直接 interface call，不持有 collection、cache、static state、thread、task 或 subscription，也不执行反射或 runtime type discovery。

Unity object 的分配与销毁成本由传入的 `IUnityObjectLifetime` 决定。Spawn burst 与 destruction queue 属于产品性能预算时，应在目标 Player 中测量。

## 持久化

本 integration 不写文件、不保存偏好，也不新增 serialized field 或 asset。它没有 schema 或 migration state。

## 验证

两个必需包均可编译后，运行：

```text
CycloneGames.GameplayFramework.Integrations.Factory.Tests.Editor
```

EditMode tests 验证同一个 Actor reference 跨越 create/release 边界、release 是终止操作，以及 Unity 已销毁 Actor 仍会通知传入的 lifetime。所有权回归还应运行 GameplayFramework core 的 EditMode 与 PlayMode suites。

Player、IL2CPP、stripping 与设备验证由各目标配置的产品验证流程负责。
