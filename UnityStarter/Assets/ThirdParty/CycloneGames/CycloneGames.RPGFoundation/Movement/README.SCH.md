# RPG Foundation Movement

[English](README.md)

RPGFoundation 使用的 Unity runtime movement component、纯 movement contract、state machine、animation bridge 与可选 integration。

## 有界 Runtime 保留

process-wide `AnimationParameterCache` 最多保留 `MaximumEntryCount`（`65,536`）个 parameter name。每个 3D `MovementComponent` 最多保留 `MaximumIgnoredColliderCount`（`65,536`）个显式 ignored-collider entry。这些值是 implementation safety ceiling，不是建议的产品预算。达到容量后，已有 cache hit、ignored-collider 移除与 owner shutdown 仍然可用；任何活动 collision policy 都不会被自动驱逐。

新的 cache 代码应使用 `TryGetOrAddHash` 或 `TryPreWarm`；新的 collision-policy 代码应使用 `TryIgnoreCollision`。返回 `false` 表示新 entry 未被保留。`TryGetOrAddHash` 仍通过 `out` value 返回确定性的 Animator hash，使调用方可以显式选择 uncached operation。旧 `GetHash`、`PreWarm` 与 `IgnoreCollision` 保持成功路径行为，但在 ceiling 处以 `InvalidOperationException` fail-fast。

`AnimationParameterCache.GetMemorySnapshot()` 与 `MovementComponent.GetMemorySnapshot()` 以 O(1) 暴露 count、capacity 与单调 rejection counter。可重建 animation cache 可被显式清理；ignored-collider entry 属于活动玩法 policy，绝不进行通用 pressure trim。

迁移是 additive 的：将容量敏感的旧调用替换为对应 `Try*` 方法，并由产品 policy 处理拒绝。只有明确需要 fail-fast 时，才将单个调用方回退到旧 API。确实超过单 owner ceiling 的负载应分区 parameter namespace 或 movement owner；修改常量需要经过审查的 framework build，以及具有代表性的 animation/physics 负载验证。

## 持久化与序列化

此契约不新增 serialized field，不重命名类型或字段，不改变 prefab、scene 或 `ScriptableObject` 数据，也不写入持久化状态；无需资产或存档迁移。

## 验证

在 EditMode 运行 `CycloneGames.RPGFoundation.Movement.Tests.Editor`。在每个目标平台验证有代表性的 movement、ignored-collider、animation、Player、IL2CPP 与 stripping 路径。
