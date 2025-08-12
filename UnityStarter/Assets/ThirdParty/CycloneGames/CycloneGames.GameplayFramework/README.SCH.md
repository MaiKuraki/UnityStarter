# CycloneGames.GameplayFramework

[English](./README.md) | 简体中文

一个面向 Unity 的轻量级 UnrealEngine 风格玩法框架。模仿虚幻引擎的 Gameplay Framework 概念（Actor、Pawn、Controller、GameMode 等），易于与 DI 集成。

- Unity: 2022.3+
- 依赖：`com.unity.cinemachine@3`、`com.cysharp.unitask@2`、`com.cyclone-games.factory@1`、`com.cyclone-games.logger@1`

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

请查看 `Samples` 文件夹中的内容