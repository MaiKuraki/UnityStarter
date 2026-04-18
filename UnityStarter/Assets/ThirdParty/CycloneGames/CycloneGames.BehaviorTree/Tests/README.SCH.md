# CycloneGames.BehaviorTree 测试与 Benchmark 使用说明

这个目录包含 `CycloneGames.BehaviorTree` 的正式验证、性能基准、真实调度模拟与 soak 压测工作流。

## 包含内容

- `Editor` 测试
  - 运行时语义一致性检查
  - blackboard / snapshot 确定性检查
  - DOD 编译 fail-fast 检查
  - 编辑器侧 benchmark 基准测试
- `PlayMode` 测试
  - runtime benchmark runner 冒烟验证
  - 导出格式验证
- benchmark 运行时工具
  - 可复用 benchmark session
  - 场景内 benchmark runner
  - CSV / JSON 导出工具
  - 数量级与复杂度矩阵
  - 真实调度策略模拟
  - soak 内存采样
- 编辑器工具
  - benchmark 控制面板
  - benchmark 场景生成

## 数量级预设

- `500 AI Battle`
- `1000 AI Crowd`
- `5000 AI Stress`
- `10000 AI Extreme`
- `100 Players + 500 AI`
- `Long Soak 1000 AI`

## 复杂度档位

- `Light`
- `Medium`
- `Heavy`

## 调度策略

- `FullRate`
  - 所有代理每帧都 tick
  - 适合小规模战斗或 Boss 级验证
- `LodCrowd`
  - 近景代理每帧 tick
  - 中景代理降频
  - 远景代理进一步降频
- `PriorityLod`
  - 高优先级代理保持高频
  - 中优先级代理中频
  - 环境/远景代理低频
- `NetworkMixed`
  - 前排玩家和关键对象每帧 tick
  - AI 分层降频
  - 周期性执行 deterministic hash check
- `FarCrowd`
  - 更激进的远景人群降频
  - 适合验证 5000+ 背景代理
- `UltraLod`
  - 只有很小一部分近景代理保持全频
  - 适合极端人群存在感压测
- `PriorityManaged`
  - 对优先级预算式 tick 的 benchmark 近似模型
  - 适合和 `FullRate / PriorityLod` 做专项对照

现在这套 benchmark 会同时衡量三个维度：

- 数量级预设
- 复杂度档位
- 调度策略

## 如何运行 Editor 测试

1. 打开 Unity。
2. 打开 `Window > General > Test Runner`。
3. 切到 `EditMode`。
4. 运行程序集 `CycloneGames.BehaviorTree.Tests.Editor`。

主要文件：

- `Consistency/BehaviorTreeConsistencyTests.cs`
- `Performance/BehaviorTreeBenchmarkTests.cs`

## 如何运行 PlayMode 测试

1. 打开 Unity Test Runner。
2. 切到 `PlayMode`。
3. 运行程序集 `CycloneGames.BehaviorTree.Tests.PlayMode`。

主要文件：

- `PlayMode/BehaviorTreePlayModeBenchmarkTests.cs`

## 如何使用 Benchmark 面板

1. 打开 `Tools > CycloneGames > Behavior Tree Benchmark`。
2. 选择数量级预设、复杂度档位、调度策略，或者手动调整 benchmark 配置。
3. 点击 `Run Editor Benchmark`，运行单次编辑器 benchmark。
4. 点击 `Run Scale Matrix For Selected Complexity`，比较当前复杂度下全部数量级预设。
5. 点击 `Run Full Matrix (Scale x Complexity)`，运行完整数量级乘复杂度矩阵。矩阵会自动使用各预设推荐的调度策略。
6. 点击 `Run PriorityManaged Comparison`，对当前配置执行 `FullRate / PriorityLod / PriorityManaged / UltraLod` 专项对照。
7. 点击 `Create PlayMode Benchmark Scene`，按当前配置生成 PlayMode benchmark 场景。
8. 点击 `Create Scene From Preset`，按当前预设和复杂度生成场景。
9. 点击 `Create Scale Matrix Scene`，生成一个会自动顺序执行当前复杂度全部数量级预设的场景。
10. 点击 `Create Full Matrix Scene`，生成一个会自动顺序执行完整矩阵的场景。
11. 点击 `Create PriorityManaged Comparison Scene`，生成一个会自动运行调度对照批次的场景。
12. 进入 PlayMode，runner 会自动开始执行。

重要配置项：

- `Deterministic Hash Check`
  - 模拟联机或确定性校验流程中的周期性 blackboard 哈希验证
- `Hash Check Interval`
  - 控制哈希验证的频率
- `Soak Frames`
  - 在正式测量结束后继续运行多少帧，用于观察长时间内存漂移
- `Soak Sample Interval`
  - soak 阶段每隔多少帧采样一次托管内存

PlayMode 场景 runner 行为：

- `Create PlayMode Benchmark Scene` 和 `Create Scene From Preset` 生成单次运行 runner。
- `Create Scale Matrix Scene` 生成批跑 runner，会顺序执行当前复杂度下的全部推荐数量级预设。
- `Create Full Matrix Scene` 生成完整矩阵 runner，会顺序执行全部推荐数量级预设与全部复杂度组合。
- 生成的 runner 会自动把 CSV / JSON 导出到 `Application.persistentDataPath/BehaviorTreeBenchmarkResults`。
- 如果 `Soak Frames > 0`，runner 会在正式测量后继续执行 soak，再导出结果。

主要文件：

- `Runtime/PerformanceTest/BehaviorTreeBenchmarkModels.cs`
- `Runtime/PerformanceTest/BehaviorTreeBenchmarkSession.cs`
- `Runtime/PerformanceTest/BehaviorTreeBenchmarkRunner.cs`
- `Editor/BehaviorTreeBenchmarkWindow.cs`

## 如何导出 CSV / JSON

当 benchmark 面板里已经有单次结果后：

1. 点击 `Export Last Result as CSV` 或 `Export Last Result as JSON`。
2. 选择导出路径。
3. 面板会写入文件，并在系统文件管理器中定位它。

当矩阵跑完后：

1. 点击 `Export Last Matrix as CSV` 或 `Export Last Matrix as JSON`。
2. 每一行或每个 JSON 项对应一个数量级、复杂度、调度策略组合的结果。

对于 PlayMode 自动生成的场景，导出是自动完成的，runner 会把最终导出路径打印到 Unity Console。

## 关键结果字段说明

- `PotentialTicks`
  - 如果所有代理都每帧 tick，理论上会发生多少次 tick
- `ExecutedTicks`
  - 在当前调度策略下，实际执行了多少次 tick
- `EffectiveTickRatio`
  - 实际 tick 占理论 tick 的比例
- `AverageActiveAgentsPerFrame`
  - 正式测量阶段每帧平均有多少代理真正参与 tick
- `PeakActiveAgentsPerFrame`
  - 正式测量阶段单帧内参与 tick 的最大代理数
- `TotalHashChecks`
  - 整轮执行了多少次 deterministic blackboard 哈希校验
- `PeakManagedMemoryBytes`
  - 本轮观测到的最高托管内存
- `SoakManagedMemoryDeltaBytes`
  - 从 soak 起点到峰值的托管内存增长量

## 推荐使用流程

1. 开发运行时逻辑时，先用 `EditMode` 测试做快速回归验证。
2. 用 benchmark 面板快速调参，观察数量级、复杂度、调度策略三者的组合影响。
3. 想回答“某种复杂度最多能撑到多少 AI”时，优先用 `Run Scale Matrix For Selected Complexity`。
4. 想做产品级容量评估时，优先用 `Run Full Matrix (Scale x Complexity)`。
5. 想观察真实帧表现、设备差异、LOD 收益、联机混合调度成本或长时间 soak 时，用生成的 PlayMode benchmark 场景。
6. 把每次结果导出成 CSV / JSON，方便做历史对比、不同设备档位对比和版本回归分析。
