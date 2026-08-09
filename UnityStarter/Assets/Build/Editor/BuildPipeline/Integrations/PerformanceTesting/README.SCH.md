# Performance Testing 构建资产守护

此 Integration 保护 Unity Performance Testing 3.5.x 在 Player Build 前后临时创建和删除的项目文件。它快照精确 Pre-build State，接管 Package-generated Image，并在 Package Cleanup Callback 后恢复 User-owned File 与 Preference State。

> **当前 checkout：** `Packages/manifest.json` 与 `packages-lock.json` 的 Direct Entry 均将 `com.unity.test-framework.performance` 锁定为 `3.5.0`。源码 Gate 审计 `3.5.x` 契约。仓库包含 Transaction Test，但本次文档改动未执行真实 Player Build 或 Package Callback Sequence。

## 职责与边界

| 组件 | 职责 |
| --- | --- |
| `PerformanceTestingPackageGate` | 检测已安装 Package，并只允许已审计的 `3.5.x` Version Shape |
| `PerformanceTestingBuildAssetEarlyProcessor` | 在 Package 的 Order-zero Preprocess Callback 前启动保护 |
| `PerformanceTestingBuildAssetLateProcessor` | Preprocess 后接管 Generated Asset，并在 Package Cleanup 后恢复原始状态 |
| `PerformanceTestingBuildAssetTransaction` | Journal、Snapshot、Verify、Restore，并公开 Readiness/Recovery API |
| `PerformanceTestingBuildAssetRecoveryParticipant` | 向中央 Workspace Recovery 注册 Transaction Directory |
| `PerformanceTestingBuildAssetReadiness` | 面向 Diagnostics 与 Tooling 的强类型只读状态 |

Guard 不创建或运行 Performance Test，不配置 Test Metadata，不选择 Scene，不收集 Measurement，不发布 Benchmark Result，也不修改 Performance Testing Package。它只保护该 Package 的临时 Player-build Asset 行为。

Processor 实现 Unity 全局 Build Callback，因此安装 Performance Testing 3.5.x 后，它会包围每次 Player Build，包括从 Composable BuildData Pipeline 之外启动的 Player Build。

## 可用性与 Package Gate

此 Integration 没有 Authoring 开关：

- Package 缺失：两个 Callback 都是 No-op；
- 已安装 `3.5.x`：Guard 自动启用；
- 安装其他 Version Shape：Player Build 被阻止，直到重新审查契约并更新 Guard。

当前 Direct Dependency 是 `3.5.0`。Gate 会剥离 Prerelease/Build Suffix，但仍要求恰好三个数字 Segment，且 Major 为 `3`、Minor 为 `5`。例如，源码 Gate 接受 `3.5.99-preview.1`，拒绝 `3.4.9`、`3.6.0`、`3.5` 与非版本文本。

不要绕过 Version Gate。受保护的假设正是 Package Callback Ordering 与 Temporary Asset Behavior。

## 强类型契约与配置

此 Integration 有意不提供 `ScriptableObject`、BuildData Card、Serialized Field、Environment-variable Toggle 或自己的 Project Preference。安装处于已审计版本就是完整 Activation Contract。

Tooling 可检查：

| API | 含义 |
| --- | --- |
| `PerformanceTestingBuildAssetTransaction.InspectReadiness(projectRoot)` | State Directory 不存在时为 Zero-write Inspection |
| `PerformanceTestingBuildAssetReadinessStatus.Clean` | 没有待处理 Recovery Evidence |
| `PerformanceTestingBuildAssetReadinessStatus.RecoveryRequired` | Evidence 有效，可以执行显式 Recovery |
| `PerformanceTestingBuildAssetReadinessStatus.Blocked` | 无法证明 Evidence 或当前文件可安全恢复 |
| `PerformanceTestingBuildAssetTransaction.Recover(projectRoot)` | Registered Participant 使用的显式 Recovery Entry |

正常项目配置仍位于版本控制的 BuildData 与 Package Manifest。`PT_ResourcesCleanup` Editor Preference 属于 Unity Performance Testing；Guard 只会快照、临时修改并恢复该 Vendor Key。它不是 Module Configuration，也不是 Build Intent 来源。

## 受保护状态

Transaction 保护以下精确路径：

~~~text
Assets/Resources/PerformanceTestRunInfo.json
Assets/Resources/PerformanceTestRunInfo.json.meta
Assets/Resources/PerformanceTestRunSettings.json
Assets/Resources/PerformanceTestRunSettings.json.meta
Assets/Resources.meta
~~~

它还记录 `Assets/Resources/` 最初是否存在，以及 `PT_ResourcesCleanup` 最初是否存在和其 Boolean Value。

现有文件通过有界读取与 Identity Evidence 进行快照。实现限制每个受保护文件最多 1 MiB，原始 Snapshot 总量最多 4 MiB。遇到 Reparse Point、不安全 Root、无效 Journal Inventory、已变化 File Identity 或未知 Directory Entry 时，它会拒绝继续，而不会覆盖或删除。

如果 `Assets/Resources/` 最初不存在，Transaction 会使用 Transaction-owned Meta GUID 创建它。只有在恢复能够证明该目录仍是由 Transaction 拥有的空目录时才会删除它。User-created 或未知 Entry 会阻止 Cleanup。

## 构建生命周期

~~~mermaid
sequenceDiagram
    participant E as Early Guard (int.MinValue)
    participant P as Performance Testing (order 0)
    participant L as Late Guard (int.MaxValue)
    participant U as Unity Player Build

    E->>E: Gate Package 并快照精确原始状态
    E->>E: 设置 PT_ResourcesCleanup=false 并确保 Resources
    P->>P: 创建临时 Run-info/Settings Asset
    L->>L: 验证并接管 Generated File Identity
    L->>U: 继续 BuildPlayer
    U-->>P: 进入 Postprocess Callback
    P->>P: 执行 Package Cleanup
    L->>L: 恢复原始文件、Preference 与 Owned Directory State
    L->>L: 验证恢复并移除 Journal Evidence
~~~

Early Processor 使用 `callbackOrder = int.MinValue`。它重置 In-memory Ownership Flag、检查 Package Version、拒绝 Pending Evidence、写入 Durable Journal 与 Snapshot、临时把 `PT_ResourcesCleanup` 设为 `false`、确保 Resources Directory、刷新 Asset Database，并把当前 Build 标记为 Owned。

Late Preprocess Callback 使用 `int.MaxValue`。在 Package 的 Order-zero Callback 之后，它要求两个 Generated JSON File 都存在，捕获 Generated JSON/meta Identity，并将 Journal 推进到 `Adopted`。

Late Postprocess Callback 同样使用 `int.MaxValue`。在 Package Cleanup 后，它恢复精确 Pre-build File、File Metadata、Resources-directory State 与 Vendor Preference；刷新 Asset；再次验证 Restored State；最后仅删除 Transaction-owned Evidence。

如果 Package Callback 生成了意外 Image，或无法证明恢复安全，Build 会失败并保留 Durable Evidence。

## 持久化与恢复

| 数据 | 位置 | 生命周期 |
| --- | --- | --- |
| Protected User/Package File | 上文列出的精确 `Assets/Resources...` Path | 原本存在时按记录 Metadata 逐字节恢复 |
| Vendor Cleanup Preference | Editor Preference Key `PT_ResourcesCleanup` | 临时设为 `false`；恢复原始存在性与 Value |
| Durable Journal Root | `.buildpipeline/transactions/performance-testing/` | Active Journal、Lock、Owner 与 Snapshot Evidence；验证完成后删除 |
| Transaction Snapshot | Journal Root 下由 Transaction 拥有的 Child | 临时持久 Recovery Input |
| Committed Build Artifact | 无 | Guard 不发布 Content 或 Player Output |

正常的新 Build 永远不会隐式恢复之前的 Evidence。Pending Evidence 会使下一个 `Begin` 失败，并要求显式 Workspace Operation。

Editor Crash、Agent Termination 或机器中断后：

1. 停止正常 Build Retry；
2. 打开 **Build > Pipeline > Workspace Health** 并刷新 Snapshot；
3. 检查 Participant 与 Evidence Path；
4. 只有 Snapshot 报告 `RecoveryRequired` 且 `CanRecover` 时才运行 Recovery；
5. 再次运行 Workspace Health，并在重新构建前要求 `Clean`。

CI 通过 `Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine` 加 `-pipelineRecoverOnly` 执行相同操作。不要手动删除 Journal 或 Protected File。`Blocked` 表示当前 File Image、Preference、Journal 或 Directory Inventory 无法安全协调；应保留 Workspace 供调查。

## CI 工作流

1. 恢复 Unity Project 与精确 Package Lock；
2. 验证 Direct Performance Testing Dependency 仍位于已审计 `3.5.x` 范围；
3. 在正常 Player Job 前运行 Workspace Health 或 Recovery-only Job；
4. 使用版本控制的 `-pipelineProfile` 与必需 `-buildTarget` 调用规范 Build Entry Point；
5. 将任意 Protection、Adoption、Restoration 或 Terminal Evidence Failure 视为 Player Job 失败；
6. 成功后确认 Guard Journal Root 下没有非 Lock Transaction Evidence；
7. 归档 Build Log 与 Terminal Result Evidence，不归档临时 Transaction Directory。

Guard 自动运行；CI 不得自行创建 `PerformanceTestRunInfo.json` 或 `PerformanceTestRunSettings.json`，不得预设 `PT_ResourcesCleanup` 来替代 Transaction，也不得在 Build 后删除 `Assets/Resources`。

## 故障排查

| 现象 | 处理方式 |
| --- | --- |
| Unsupported Package Version 阻止 Build | 恢复已审计 `3.5.x` Package；升级前先审查并更新 Guard。 |
| Early Protection 无法启动 | 检查 Pending Evidence、Reparse Point、File Size Limit 与 Resources/meta 一致性。 |
| 预期 Generated Output 缺失 | 验证已安装 Package 的 Callback 行为并停止；不要伪造 JSON File。 |
| Generated Image 被报告为不安全 | 检查 Callback 之间是否有并发 Tool 修改受保护文件。 |
| Restoration 失败并保留 Evidence | 使用 Workspace Health；不要先 Retry 正常 Build。 |
| Readiness 为 `Blocked` | 保留 Workspace，并对比 Journal、Snapshot、Protected File、Preference 与未知 Directory Entry。 |
| `Assets/Resources.meta` 存在但目录不存在 | 启动 Build 前，有意识地修复 Orphaned Project Asset State。 |
| Transaction-created Resources 中出现 User File | 手动移动或审查；Guard 会有意拒绝删除该目录。 |

## 验证边界

EditMode Test 覆盖 `3.5.x` Package Gate、精确 Round-trip Restoration、原本不存在的 Resources Ownership、Vendor Preference Restoration、Explicit Recovery、Pending-evidence Behavior、Unknown Concurrent File Image、Unknown Directory Entry、Callback Ordering、Participant Registration 与 Zero-write Clean Inspection。

Transaction Logic Test 使用临时文件系统 Fixture 与 Fake Preference Store。已安装的 `3.5.0` Dependency 和静态 Callback Source 不证明真实 Unity Player Build、实际 Vendor Callback Sequence、Performance-test Execution、Result Collection、Target-platform Behavior、Domain Reload Behavior 或真实进程 Crash 后的 Recovery。

每个受支持 Unity/Package 组合的 Qualification 都要求：

1. 安装 Performance Testing 后执行 Clean Player Build；
2. 在 Disposable Project Copy 中执行 Pre-existing-file Round Trip；
3. 使用不存在 `Assets/Resources` Directory 的项目；
4. 在 Early Callback 后中断并执行显式 Recovery；
5. 执行 Concurrent-change 负向用例，确认其保持 Blocked 且无数据丢失；
6. 确认 Test-run Metadata 与 Benchmark Result 仍正确；
7. 在每个受支持 Build Target 与 CI Agent OS 上重复。

## 源码索引

- [PerformanceTestingBuildAssetTransaction.cs](PerformanceTestingBuildAssetTransaction.cs)
- [BuildWorkspaceService.cs](../../Core/Recovery/BuildWorkspaceService.cs)
- [BuildWorkspaceHealthWindow.cs](../../Presentation/BuildWorkspaceHealthWindow.cs)
- [BuildEntryPoints.cs](../../EntryPoints/BuildEntryPoints.cs)
- [PerformanceTestingBuildAssetTransactionTests.cs](../../../../Tests/Editor/PerformanceTestingBuildAssetTransactionTests.cs)
- [Build Pipeline 手册](../../../../README.SCH.md)
