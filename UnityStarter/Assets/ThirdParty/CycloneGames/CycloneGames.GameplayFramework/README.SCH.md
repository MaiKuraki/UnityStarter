# CycloneGames.GameplayFramework

[English](./README.md) | 简体中文

一个面向 Unity 的轻量级 UnrealEngine 风格玩法框架。模仿虚幻引擎的 Gameplay Framework 概念（Actor、Pawn、Controller、GameMode 等），易于与 DI 集成。

- Unity: 2022.3+
- 依赖：`com.unity.cinemachine@3`、`com.cysharp.unitask@2`、`com.cyclone-games.factory@1`、`com.cyclone-games.logger@1`

## 快速开始

1) 创建 WorldSettings
- 菜单：Create -> CycloneGames -> GameplayFramework -> WorldSettings
- 填写以下 Prefab（需包含对应组件）：
  - `GameMode`
  - `PlayerController`
  - `Pawn`（默认玩家 Pawn）
  - `PlayerState`
  - `CameraManager`
  - `SpectatorPawn`
- 若需在运行时按名称加载，请将该 `WorldSettings` 资源放到 `Resources` 目录下。

2) 提供对象生成器（或接入 DI）
- 框架通过 `IUnityObjectSpawner`（来自 `com.cyclone-games.factory`）实例化对象。简单示例：

```csharp
using CycloneGames.Factory.Runtime;
using UnityEngine;

public class SimpleSpawner : IUnityObjectSpawner
{
    public T Create<T>(T origin) where T : Object
    {
        return origin == null ? null : Object.Instantiate(origin);
    }
}
```

3) 启动引导（Bootstrap）
- 新建一个引导 `MonoBehaviour`，初始化 `World`，加载 `WorldSettings`，实例化 `GameMode` 并启动：

```csharp
using UnityEngine;
using CycloneGames.GameplayFramework;
using CycloneGames.Factory.Runtime;

public class GameBoot : MonoBehaviour
{
    private IUnityObjectSpawner spawner;
    private World world;

    void Start()
    {
        world = new World();
        spawner = new SimpleSpawner();

        var ws = Resources.Load<WorldSettings>("YourWorldSettingsName");
        var gm = spawner.Create(ws.GameModeClass) as GameMode;
        gm.Initialize(spawner, ws);
        world.SetGameMode(gm);
        gm.LaunchGameMode();
    }
}
```

4) 场景放置
- 在场景中至少放置一个 `PlayerStart`（默认使用找到的第一个；也支持通过 `Portal` 名称匹配）。
- 可选：放置 `KillZVolume`（勾选 `BoxCollider` 的 Trigger），用于当角色掉落时自动销毁；下落对象需含 `Collider` 和 `Rigidbody`。
- 确保主摄像机挂载 `CinemachineBrain`，并且场景里至少有一个 `CinemachineCamera`。`CameraManager` 会自动跟随当前视角目标。

## 核心概念

- Actor：基础单元，包含寿命与 Owner。示例：`PlayerStart`、`KillZVolume`、`CameraManager`。
- Pawn：可被控制的 Actor，由 `Controller` 控制/占有。
- Controller：拥有并占有 `Pawn`，包含 `PlayerController` 与 `AIController`。
- PlayerState：玩家相关的持久数据，在 Pawn 切换时保持。
- GameMode：负责生成 `PlayerController/Pawn` 与重生规则。
- WorldSettings：ScriptableObject，配置关键玩法类/Prefab。
- World：轻量级保存 `GameMode` 引用与查询（并非 UE 的 UWorld）。
- PlayerStart：玩家出生点。
- CameraManager：基于 Cinemachine 的摄像机管理器，跟随当前视角目标。
- SpectatorPawn：旁观 Pawn，在占有真实 Pawn 前的默认形态。
- KillZVolume：触发后调用 `FellOutOfWorld`。
- SceneLogic：类似虚幻引擎的关卡蓝图。

## 示例

请查看 `Samples/Sample.PureUnity`：包含可运行场景（`Scene/UnitySampleScene.unity`）、相关 Prefab、`Resources/UnitySampleWorldSettings.asset` 与引导脚本。

## API 速览

- GameMode
  - `Initialize(IUnityObjectSpawner, IWorldSettings)`：注入依赖。
  - `LaunchGameMode()`：生成 `PlayerController` 并在 `PlayerStart` 处重启玩家。
  - `RestartPlayer(...)`：重生流程；使用 `SpawnDefaultPawn*` 系列方法。
- PlayerController
  - 异步初始化时生成 `PlayerState`、`CameraManager`、`SpectatorPawn`。
  - 提供 `GetCameraManager()`、`GetSpectatorPawn()`。
- Controller
  - `Possess(Pawn)`、`UnPossess()`、`SetControlRotation(...)`。
  - 默认 Pawn Prefab 来自 `WorldSettings.PawnClass`。
- Pawn
  - `PossessedBy(Controller)`、`UnPossessed()`、`DispatchRestart()`。
- WorldSettings
  - 配置 `GameMode`、`PlayerController`、`Pawn`、`PlayerState`、`CameraManager`、`SpectatorPawn` 的 Prefab。
- CameraManager
  - 需要主摄像机挂载 `CinemachineBrain`；管理活动的 `CinemachineCamera`。

## 故障排查

- 生成失败 / 空引用
  - 确保 `WorldSettings` 字段引用了含有正确组件的 Prefab。
  - 提供 `IUnityObjectSpawner`（或将你的 DI 容器实现为该接口）。
- 摄像机不跟随
  - 主摄像机添加 `CinemachineBrain`，场景内存在至少一个 `CinemachineCamera`，并确保 `CameraManager` 已生成。
- 玩家出现在原点
  - 在场景中放置 `PlayerStart`，或校验使用了正确的名称/Portal。
- KillZ 无效
  - `KillZVolume` 需要触发器碰撞体；下落对象需要 `Collider` + `Rigidbody`。