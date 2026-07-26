# RPG Foundation Movement

[English](README.md)

RPGFoundation 使用的 Unity runtime movement component、纯 movement contract、state machine、animation bridge 与可选 integration。

## 有界 Runtime 保留

process-wide `AnimationParameterCache` 最多保留 `MaximumEntryCount`（`65,536`）个 parameter name。每个 3D `MovementComponent` 最多保留 `MaximumIgnoredColliderCount`（`65,536`）个显式 ignored-collider entry。这些值是 implementation safety ceiling，不是建议的产品预算。达到容量后，已有 cache hit、ignored-collider 移除与 owner shutdown 仍然可用；任何活动 collision policy 都不会被自动驱逐。

新的 cache 代码应使用 `TryGetOrAddHash` 或 `TryPreWarm`；新的 collision-policy 代码应使用 `TryIgnoreCollision`。返回 `false` 表示新 entry 未被保留。`TryGetOrAddHash` 仍通过 `out` value 返回确定性的 Animator hash，使调用方可以显式选择 uncached operation。旧 `GetHash`、`PreWarm` 与 `IgnoreCollision` 保持成功路径行为，但在 ceiling 处以 `InvalidOperationException` fail-fast。

`AnimationParameterCache.GetMemorySnapshot()` 与 `MovementComponent.GetMemorySnapshot()` 以 O(1) 暴露 count、capacity 与单调 rejection counter。可重建 animation cache 可被显式清理；ignored-collider entry 属于活动玩法 policy，绝不进行通用 pressure trim。

迁移是 additive 的：将容量敏感的旧调用替换为对应 `Try*` 方法，并由产品 policy 处理拒绝。只有明确需要 fail-fast 时，才将单个调用方回退到旧 API。确实超过单 owner ceiling 的负载应分区 parameter namespace 或 movement owner；修改常量需要经过审查的 framework build，以及具有代表性的 animation/physics 负载验证。

## 可选 DeterministicMath 集成

`CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath` 支持由 UPM 解析的 `com.cyclone-games.deterministic-math` `1.x`。其 Runtime 与 EditMode test asmdef 生成 `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH`，把该 capability 作为 constraint，并使用 `autoReferenced: false`。基础 Movement assembly 在没有 DeterministicMath 时仍可编译。请通过 UPM 安装 package 并显式添加 integration asmdef reference；不要在 PlayerSettings 中添加 capability symbol。

## 可选 GameplayAbilities 集成

`CycloneGames.RPGFoundation.Movement.Integrations.GameplayAbilities` 支持由 UPM 解析的 `com.cyclone-games.gameplay-abilities` 与 `com.cyclone-games.gameplay-tags` `1.x`。它生成并约束 `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_ABILITIES` 与 `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_TAGS` 两个 capability，并使用 `autoReferenced: false`。任一 package 缺失时 bridge 都不会参与编译。请安装 GameplayAbilities 及其 GameplayTags 依赖，再显式添加 integration asmdef reference；不要在 PlayerSettings 中添加任一 capability symbol。

## 持久化与序列化

此契约不新增 serialized field，不重命名类型或字段，不改变 prefab、scene 或 `ScriptableObject` 数据，也不写入持久化状态；无需资产或存档迁移。

## 验证

在 EditMode 运行 `CycloneGames.RPGFoundation.Movement.Tests.Editor`。解析到受支持的 DeterministicMath package 时，还应运行 `CycloneGames.RPGFoundation.Movement.DeterministicMath.Tests.Editor`。对每组可选 integration 依赖分别执行一次“存在”和“缺失”编译，并确认只生成预期的 integration assembly。在每个目标平台验证有代表性的 movement、ignored-collider、animation、Player、IL2CPP 与 stripping 路径。
