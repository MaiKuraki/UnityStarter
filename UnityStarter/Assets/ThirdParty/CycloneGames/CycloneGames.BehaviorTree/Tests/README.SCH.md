# CycloneGames.BehaviorTree 测试与 Benchmark 指南

[English](README.md)

本文档说明如何验证 `CycloneGames.BehaviorTree`、运行有边界的性能实验，以及如何解释所得证据。先运行功能测试，再执行小规模 benchmark 冒烟测试；只有小负载稳定后，才扩大到矩阵或 soak 测试。

Benchmark 工具生成的是特定 Unity 进程、backend、构建配置、机器和工作负载下的测量结果。测试或预算通过不等于 Player、IL2CPP、AOT、managed stripping、目标平台或长期生产环境认证。

## 五分钟验证

### 1. 运行 Editor 测试

1. 打开 `<repo-root>/UnityStarter` 下的 Unity 项目。
2. 打开 `Window > General > Test Runner`。
3. 选择 `EditMode`。
4. 运行 `CycloneGames.BehaviorTree.Tests.Editor`。
5. 如果 DOD assembly 处于 active 状态，还要运行 `CycloneGames.BehaviorTree.Runtime.DOD.Tests.Editor`。
6. 当前 checkout 同时包含两个本地模块和显式 integration 目录，因此还要运行 `CycloneGames.BehaviorTree.Integrations.DeterministicMath.Tests.Editor`。

这些测试为编译器校验、code-first 构建、runtime 语义、blackboard 契约、调度、DOD 安全、Editor authoring 安全和 benchmark 工具提供最快反馈。

### 2. 运行 PlayMode 测试

1. 在 Test Runner 中切换到 `PlayMode`。
2. 运行 `CycloneGames.BehaviorTree.Tests.PlayMode`。

PlayMode 程序集验证场景内 benchmark runner，以及 `BTRunnerComponent` 在注册、暂停、禁用、停止和重新播放过程中的行为。

### 3. 运行有边界的 Benchmark 冒烟测试

1. 打开 `Tools > CycloneGames > Behavior Tree > Behavior Tree Benchmark`。
2. 选择 `Custom`，并输入以下 smoke workload：`64` 个 agent、`2` 个 leaf、`1` 次 read、`1` 次 write、`0` 层 decorator、`0` 次 work iteration、`4` 个 tracked key、`8` 个 warmup frame、`60` 个 measurement frame、`0` 个 soak frame，以及每帧 `1` 次 tick。
3. 第一次运行时启用 delta flush，并停用 deterministic hash check。
4. 选择 `Run Editor Benchmark`。
5. 确认结果有效、工作负载字段与请求一致，并验证 CSV/JSON 导出。
6. 完成这些检查后，再增加规模、运行矩阵或开始 soak 测试。

## 测试程序集与覆盖范围

测试程序集均为 `autoReferenced: false`。Unity Test Runner 通过测试程序集配置发现它们；普通 runtime 程序集不会因此依赖测试代码。

### Editor 程序集

`CycloneGames.BehaviorTree.Tests.Editor` 包含以下测试类：

| 文件 | 主要覆盖范围 |
| --- | --- |
| `Consistency/BehaviorTreeAuthoringCompilerTests.cs` | Authoring graph 结构、精确 custom emitter 注册、受保护且生成前重新校验的 analysis artifact、setup 语义校验、跨 subtree occurrence 的 node/runtime GUID 唯一性、遍历硬上限、built-in 配置直接生成和稳定 UTF-16 哈希 |
| `Consistency/BehaviorTreeCodeFirstTests.cs` | Fluent builder 契约、确定性随机、setup 冻结与校验、畸形 snapshot/delta 拒绝、node lifecycle reason、Parallel/Switch/有方向 SubTree 语义、time 与 cooldown 边界、owner-thread 检查、Dispose、事务式 runtime graph 校验，以及拒绝后修复重试 |
| `Consistency/BehaviorTreeConsistencyTests.cs` | Stop/wake 行为、selector abort、blackboard 类型存储、observer、schema 约束、确定性序列化、读取时保留 local object、stamp sequence 单调性、严格 snapshot framing 和 snapshot/delta |
| `Consistency/RuntimeBlackboardSafetyTests.cs` | 并发 hash/serialization scratch 排他、bitwise float 变更契约、producer-thread delta signal、原子 SubTree output batch 和复制式 Editor diagnostic |
| `Consistency/BehaviorTreeEditorSafetyTests.cs` | 非变异 graph 填充、显式 root repair、规范诊断、cycle 防护、安全 paste 行为和 benchmark 请求限制 |
| `Consistency/BehaviorTreeTickManagerTests.cs` | 容量校验、terminal 移除、延迟注册、priority 移动/移除、预算校验和 LOD 配置 |
| `Performance/BehaviorTreeBenchmarkTests.cs` | Managed tick 与 blackboard 测量、warmup 后 delta flush/apply 分配守卫、结果评估、批次摘要、预设矩阵和内存预算估算 |

`CycloneGames.BehaviorTree.Runtime.DOD.Tests.Editor` 是独立的条件测试程序集，使用与 DOD runtime 相同的 Burst、Collections 与 Mathematics gate。`DataOriented/BehaviorTreeDataOrientedSafetyTests.cs` 覆盖时间累计、Repeater/Retry/WaitTicks 参数域、state hash v2、不可变 flat-tree ownership、scheduler lease、internal Job 可见性、generation-safe handle、stale action request、公开读取前完成 Job、owner-thread 访问、reactive invalidation 和空 Parallel 规范化。缺少可选 DOD 依赖时，它会自然消失。

`CycloneGames.BehaviorTree.Integrations.DeterministicMath.Tests.Editor` 是独立的显式测试程序集。`Integrations/DeterministicMath/DeterministicMathBlackboardIntegrationTests.cs` 覆盖定点 blackboard 存储、schema 默认值、delta round trip 和确定性随机状态恢复。该 assembly 为 `autoReferenced: false`，直接引用两个本地模块，并在分发 integration 目录时参与编译。

### PlayMode 程序集

`CycloneGames.BehaviorTree.Tests.PlayMode` 包含：

| 文件 | 主要覆盖范围 |
| --- | --- |
| `PlayMode/BehaviorTreePlayModeBenchmarkTests.cs` | Runner 完成、recommended matrix、priority comparison、CSV/JSON 序列化和结果文件写入 |
| `PlayMode/BehaviorTreeRunnerLifecycleTests.cs` | Managed 与 priority-managed runner 在 pause、disable、stop 和 play 迁移期间的注册状态 |

### Networking 测试属于独立包

Networking 验证位于同级 `CycloneGames.BehaviorTree.Networking` 包中。修改 protocol message、receive-state ordering、snapshot、delta、authority 或 transactional apply 行为时，应运行其 `CycloneGames.BehaviorTree.Networking.Tests.Editor` 程序集和 `BehaviorTreeNetworkingIntegrationTests.cs`。该测试覆盖 composite auxiliary state 参与 tree hash、bridge owner-thread/Dispose 规则、protocol 注册、payload 上限与 framing、identity/order/replay 检查、sequence wrap，以及无 partial mutation 的拒绝行为。主包的 blackboard 序列化测试不能替代 adapter 包的 integration test。

## Batchmode 命令模板

需要替换所有尖括号占位符。Unity executable 必须匹配当前 checkout 记录的版本；不要提交某台机器专用的 executable path。

Editor 测试：

```text
"<UnityEditorExecutable>" -batchmode -nographics -quit -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform EditMode -assemblyNames "CycloneGames.BehaviorTree.Tests.Editor" -testResults "<results-path>/behavior-tree-editmode.xml"
```

条件 DOD Editor 测试：

```text
"<UnityEditorExecutable>" -batchmode -nographics -quit -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform EditMode -assemblyNames "CycloneGames.BehaviorTree.Runtime.DOD.Tests.Editor" -testResults "<results-path>/behavior-tree-dod-editmode.xml"
```

PlayMode 测试：

```text
"<UnityEditorExecutable>" -batchmode -nographics -quit -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform PlayMode -assemblyNames "CycloneGames.BehaviorTree.Tests.PlayMode" -testResults "<results-path>/behavior-tree-playmode.xml"
```

当条件 DOD assembly、显式 DeterministicMath integration assembly 和独立 Networking assembly 位于本次 change closure 时，使用各自的 `-assemblyNames` 调用单独运行。保存 Unity 进程退出码、测试 XML、Editor log、checkout revision、backend 和命令行，作为可复现测试记录。

## Benchmark 架构

Benchmark 代码隔离在 `CycloneGames.BehaviorTree.Benchmarks` 中，该程序集同样是 `autoReferenced: false`。测试或工具程序集必须显式引用它。

- `BehaviorTreeBenchmarkSession` 为一份配置持有 synthetic runtime tree 和 delta tracker。`RunImmediate` 同步执行 setup、warmup、measurement、soak、assessment 和 Dispose。
- `BehaviorTreeBenchmarkWindow` 提供有输入保护的 Editor 控件，用于单次运行、scale matrix、full matrix、priority comparison 和 configured budget matrix。
- `BehaviorTreeBenchmarkRunner` 按 PlayMode frame 执行一份配置或矩阵，并可自动导出结果。
- `BehaviorTreeBenchmarkExportUtility` 将单次和批次结果序列化为 CSV 或 JSON。

`BehaviorTreeBenchmarkSession` 是 synthetic workload。它适合对框架路径进行可重复比较，但不会模拟正式游戏中的每个 gameplay system、render workload、network transport、asset stream 或 platform service。

## Benchmark 维度

### 规模预设

- `AiBattle500`
- `AiCrowd1000`
- `AiStress5000`
- `AiExtreme10000`
- `Network100Players500Ai`
- `LongSoak1000`
- `Custom`

### 复杂度档位

- `Light`
- `Medium`
- `Heavy`

### 调度 Profile

| Profile | 工作负载模型 |
| --- | --- |
| `FullRate` | 每个 synthetic agent 在每次模拟 tick 都可执行 |
| `LodCrowd` | 近、中、远分组使用逐级降低的 cadence |
| `PriorityLod` | Priority 与 distance 分组使用不同 cadence |
| `NetworkMixed` | 面向 player 的前部组保持高频，AI 组降频；可选 hash check 模拟同步开销 |
| `FarCrowd` | 对远处 agent 使用更激进的降频 |
| `UltraLod` | 仅较小的近景组保持全频 |
| `PriorityManaged` | 用于与其他 profile 比较的 synthetic priority-budget 行为 |

这些 profile 模拟 benchmark session 内的调度决策，不能证明所有生产级 AI scheduler 的行为或成本。

## 工作负载硬上限

Editor window 会在运行或创建场景前校验 scalar 字段和 derived work。上限用于保护 Editor，避免误发起无边界请求；它们不表示框架承诺支持相应 agent 数量。

| 输入 | 接受范围 |
| --- | ---: |
| Agent 数量 | `1..100000` |
| 每棵树的 leaf node 数量 | `1..512` |
| 每个 leaf/tick 的 blackboard read | `0..256` |
| 每个 leaf/tick 的 blackboard write | `1..256` |
| 每个 leaf 的 decorator layer | `0..64` |
| 每个 leaf 的 simulated work iteration | `0..100000` |
| 每个 agent 的 tracked key | `0..8192` |
| Warmup frame | `0..1000000` |
| Measurement frame | `1..1000000` |
| Soak frame | `0..1000000` |
| Hash-check 与 soak-sample interval | `1..1000000` |
| 每帧 tick 数 | `1..64` |

Derived limit 使用 checked arithmetic 计算：

```text
nodesPerAgent = 1 + leafNodes * (1 + decoratorLayers)
totalRuntimeNodes = nodesPerAgent * agents                         <= 2,000,000
totalTrackedKeys = agents * trackedKeysPerAgent                    <= 20,000,000
frameCount = warmupFrames + measurementFrames + soakFrames
workPerLeaf = 1 + reads + writes + decoratorLayers + workIterations
workUnitsPerFrame = agents * leafNodes * ticksPerFrame * workPerLeaf <= 25,000,000
totalWorkUnits = workUnitsPerFrame * frameCount                     <= 1,000,000,000
```

数值溢出或超过任一 scalar/derived bound 都会在执行前拒绝请求。因此，即使每个字段都低于各自 scalar maximum，组合配置仍可能因为 derived bound 被拒绝。

## 使用 Benchmark Window

打开 `Tools > CycloneGames > Behavior Tree > Behavior Tree Benchmark`。

### 单次运行

1. 选择 preset、complexity 和 scheduling profile，或编辑自定义配置。
2. 确认界面没有 validation error。
3. 选择 `Run Editor Benchmark` 执行同步 Editor 测量。
4. 导出前先检查结果。

大型同步 Editor 运行会在循环完成前让 Editor 无法响应。先建立有边界的 smoke result；需要较长的逐帧观察时，优先使用生成的 PlayMode 场景。

### 矩阵与对照运行

- `Run Scale Matrix For Selected Complexity` 对比同一 complexity 下的推荐规模预设。
- `Run Full Matrix (Scale x Complexity)` 组合推荐预设和所有 complexity tier。
- `Run PriorityManaged Comparison` 针对选定基础配置比较 `FullRate`、`PriorityLod`、`PriorityManaged` 和 `UltraLod`。
- `Run Configured Budget Matrix` 运行源码定义的预算矩阵。矩阵通过只表示配置阈值在该进程和环境中通过。

### 生成 PlayMode 场景

窗口可以创建单次运行、scale matrix、full matrix、priority comparison 或 configured-budget-matrix 场景。替换 active scene 前，它会调用 Unity 的 modified-scene save prompt；取消提示会取消场景创建。新场景会被标记为 dirty，只有用户显式保存或丢弃后才结束未保存状态。

生成的 runner 会在进入 PlayMode 时自动启动。Warmup、measurement 和 soak 都是每个 Unity frame 推进一个 benchmark frame。单次及矩阵 runner 根据各自 CSV/JSON 设置导出。

## 程序化 Session

只有显式引用 `CycloneGames.BehaviorTree.Benchmarks` 的程序集才能直接使用 session：

```csharp
var config = new BehaviorTreeBenchmarkConfig
{
    BenchmarkName = "BehaviorTree Smoke",
    AgentCount = 64,
    LeafNodesPerTree = 2,
    BlackboardReadsPerLeafPerTick = 1,
    WritesPerLeafPerTick = 1,
    DecoratorLayersPerLeaf = 0,
    SimulatedWorkIterationsPerLeaf = 0,
    TrackedKeysPerAgent = 4,
    WarmupFrames = 8,
    MeasurementFrames = 60,
    SoakFrames = 0,
    TicksPerFrame = 1,
    EnableDeltaFlush = true,
    EnableDeterministicHashCheck = false
};

BehaviorTreeBenchmarkResult result =
    BehaviorTreeBenchmarkSession.RunImmediate(config);
```

`RunImmediate` 在 setup 期间包含显式垃圾回收，并且只测量 synthetic session loop。需要受控 frame progression 时，构造 session，调用 `Setup`，按需调用 `RunWarmupFrame`、`RunMeasuredFrame` 和 `RunSoakFrame`，然后调用 `Complete` 并 `Dispose`。

上一节的硬上限由 `BehaviorTreeBenchmarkWindow` 执行。直接调用 `BehaviorTreeBenchmarkSession` 或手动配置 `BehaviorTreeBenchmarkRunner` 时，必须在分配或运行前自行应用可信配置边界。不要把 benchmark 专用强制垃圾回收和 synthetic work 放入生产 gameplay path。

## 导出与持久化

默认输出目录为：

```text
Application.persistentDataPath/BehaviorTreeBenchmarkResults
```

Window 可以把最后一份单次或矩阵结果以 CSV/JSON 导出到指定路径，也可以同时把两种格式写入默认目录。生成的 PlayMode runner 在未修改 serialized folder name 时使用同一默认目录，并在 Unity Console 记录最终路径。

Benchmark 文件是用户本机的测量产物，不是框架配置或事实来源。保留比较所需的环境元数据后，按照项目证据策略归档或删除。生成场景只有在用户把它保存到显式项目路径后才会持久化。

## 解释结果

| 字段 | 含义与限制 |
| --- | --- |
| `PotentialTicks` | 如果所有 agent 在每个配置 tick 都可执行时的理论 tick 数 |
| `ExecutedTicks` | 应用 scheduling profile 后实际执行的 tick 数 |
| `EffectiveTickRatio` | `ExecutedTicks / PotentialTicks`；预期降频会降低该值，应先核对配置，不要直接解释为丢失工作 |
| `AverageActiveAgentsPerFrame` / `PeakActiveAgentsPerFrame` | Measurement frame 内执行 tick 的 synthetic agent 平均值与峰值 |
| `AverageFrameMilliseconds` / `MaxFrameMilliseconds` | Benchmark session measured-frame loop 的耗时，不是完整 Player 渲染帧耗时 |
| `TicksPerSecond` | Executed synthetic tick 除以 measured elapsed time |
| `TotalDeltaFlushes` / `TotalHashChecks` | Session 执行的已启用同步类工作 |
| `ManagedMemoryDeltaBytes` | Session 采样之间的 managed heap 差值；进程噪声和 GC 时机都会影响结果 |
| `PeakManagedMemoryBytes` | 采样到的 managed heap 最高值，不包含 native、GPU、driver 或进程总内存 |
| `SoakManagedMemoryDeltaBytes` | 相对 soak baseline 的 managed memory 采样峰值增长 |
| `Gen0Collections` / `Gen1Collections` / `Gen2Collections` | Session 期间观察到的进程 collection count 差值 |
| `ProductionBudgetPassed` / `BudgetSummary` | 针对配置阈值的评估，不是平台认证结果 |

`MaxManagedMemoryDeltaBytes` 是 session 的 retained-memory budget，不是 per-frame allocation limit。排查 hot-path allocation 时，应结合 Unity Profiler allocation sample、warmup 后的专项分配测试、GC count 和 soak drift。

只有在 revision、Unity version、backend、build type、safety check、hardware、power/thermal state、frame pacing、workload 和 background activity 被记录且足够等价时，结果才适合比较。Editor 与 PlayMode 测量适合发现回归；产品预算需要在代表性设备的 release Player 上验证。

## 证据工作流

1. 先运行聚焦 EditMode 测试，并在性能工作前记录失败。
2. 修改场景注册或调度时，运行 runner lifecycle PlayMode 测试。
3. 建立小规模 benchmark smoke result，并验证导出。
4. 使用固定配置和环境元数据建立可重复 baseline。
5. 只运行回答当前容量或调度问题所需的矩阵。
6. 使用 Unity Profiler 或平台工具调查 CPU sample、managed allocation、native memory 和 frame-time distribution。
7. 在所有需要作出结论的目标硬件档位上，以 release Player 重复代表性工作负载。
8. 按适用范围单独运行 IL2CPP/AOT、managed stripping、headless、WebGL、mobile、desktop 或 console 检查。
9. 使用有边界的 soak 运行检查长期 drift、handle leak 和 collection behavior，并保留起止 capture 和恢复观察。

任何单独步骤都不能替代全部后续步骤。尤其是 Editor benchmark 成功不能证明 Player、IL2CPP、AOT、平台、thermal、battery、long-soak 或跨设备兼容性。

## 新增或修改测试

1. 将纯 runtime/compiler/blackboard/scheduler 契约放入最接近该行为的 Editor 测试类。
2. 将 scene、`MonoBehaviour`、registration、disable/enable 和 frame-bound 行为放入 PlayMode 测试。
3. 将 Editor graph、Undo、copy/paste、asset mutation 和 scene safety 行为放入 `BehaviorTreeEditorSafetyTests`。
4. 将 DOD handle、Job ownership、stale completion 和 flat-tree validation 行为放入 `BehaviorTreeDataOrientedSafetyTests`。
5. 将 DeterministicMath 行为放入其显式 integration test assembly。
6. 将 Networking protocol 与 adapter 行为放入独立 Networking 包测试。
7. 对每个 failure path，同时断言拒绝结果，并在 transactionality 适用时断言没有 partial state mutation。
8. 在 `finally` 或 fixture teardown 中 Dispose runtime tree、blackboard、session、native owner 和 subscription。
9. 测量 allocation-sensitive 代码前先 warm up，为所有 loop 和 payload 设置边界，并将 correctness assertion 与噪声较大的 wall-clock threshold 分开。
10. Suite、assembly、menu path、limit、field 或 persistence behavior 改变时，同时更新英文指南和 `README.SCH.md`。

修改 `BTPriorityTickManagerComponent` 自动发现逻辑时，应加入未定义 player tag 的 PlayMode 用例。预期的 fail-closed 行为是停用自动查找，而不是在 update loop 中反复抛出异常。

## 故障排查

### Test Runner 中缺少测试程序集

- 等待 script compilation 完成，并先检查 Console compile error。
- 确认测试 asmdef 仍包含 `optionalUnityReferences: ["TestAssemblies"]`，并引用所有必要 runtime/integration assembly。
- Burst、Collections 或 Mathematics version define 未激活时，DOD 测试程序集不会出现，这是预期行为。
- DeterministicMath 测试程序集显式直接引用两个本地 assembly。发行布局如果省略任一依赖，应成对省略 integration 目录及其测试目录；当前 checkout 中两者均存在。
- Networking 测试出现在同级包的程序集下，不属于主 BehaviorTree 测试程序集。

### Benchmark 请求被拒绝

- 先检查每个 scalar limit，再计算 derived node、tracked-key、per-frame work 和 total-work 数值。
- 减少 agent、leaf、decorator、tracked key、tick、work iteration 或 frame count。提高文档中的硬上限需要实现与安全评审，不能作为绕过测试限制的方法。

### 创建场景后没有变化

- Modified-scene prompt 可能已被取消。
- 解决 compile error，并使用有效的有边界配置重试。
- 创建成功后，如果场景需要成为项目资产，应显式保存 dirty scene。

### 多次运行的结果波动

- 稳定 warmup、Editor 后台活动、power mode、thermal state 和 frame pacing。
- 比较相同 revision 与配置。
- 使用重复 sample 和 distribution；不要根据一次噪声测量解释回归。

### 找不到导出文件

- 检查 Unity Console 中解析后的路径与 I/O error。
- 根据执行测试的进程与平台解析 `Application.persistentDataPath`。
- 确认写权限和可用存储；不要把结果重定向到 package source folder。

## 最小验证记录

可共享的结果至少记录：

- repository revision 和 local-change status；
- 当前 checkout 记录的 Unity version；
- Editor 或 Player、scripting backend、architecture、build configuration 和 safety setting；
- operating system、device/hardware tier、power 和 thermal condition；
- 完整 benchmark configuration、preset、complexity、scheduling profile 和 budget；
- 测试 command 或 UI path、exit code、XML/log path、CSV/JSON path 和 profiler capture path；
- warmup、sample count、repeated-run distribution、failure 和 excluded evidence；
- 未运行的 platform、IL2CPP/AOT、stripping、memory、leak 和 soak 检查。

该记录使测量可以复现，并防止本机结果被扩大到证据范围之外。
