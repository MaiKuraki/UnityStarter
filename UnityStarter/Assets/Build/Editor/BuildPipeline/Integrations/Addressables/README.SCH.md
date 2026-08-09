# Addressables 构建集成

Addressables Integration 实现通用 `asset-content` Provider Contract。它调用受支持的 Unity Addressables Editor API，将内容绑定到 Pipeline 拥有的版本，发布经过验证的 Artifact Tree，并通过持久事务保护 Addressables Settings 与 Publication Output。

> **当前 checkout：** `Packages/manifest.json` 与 `Packages/packages-lock.json` 均未包含 `com.unity.addressables`。Integration 通过反射边界保持可编译，但本 checkout 未执行 Addressables Content Build、Player Build 或 Runtime Load。下文描述的是当前源码契约与静态测试证据。

## 职责与边界

| 组件 | 职责 |
| --- | --- |
| `AddressablesBuildConfig` | 强类型 Content Build、Publication 与 Content Update Authoring |
| `AddressablesContentBuildAdapter` | Provider Discovery、Preflight、Output Claim、Execution 与 Player Session Integration |
| `AddressablesBuilder` | 受支持的 Addressables API 调用、版本绑定、Artifact 收集、验证与 Staging |
| `AddressablesVersionBuildProcessor` | 在依赖它的 Player Build 前验证 Provider 拥有的 `AddressablesVersion.json` |
| `AddressablesPlayerBuildIsolation` | 阻止隐式 Package Rebuild，并在未选择 Addressables 时抑制陈旧 Package Hook |
| `AddressablesSettingsTransaction` | 快照并恢复 Addressables Configuration Asset 及其 meta 文件 |
| `AddressablesPublicationTransaction` | 原子安装或回滚目标 Publication |
| `AddressablesRecoveryCoordinator` | 无需 Optional Package 即可恢复两类事务 |

Integration 不负责安装 Addressables、创建 Group 或 Profile、决定哪些 Asset 可寻址、实现 Runtime Initialization、上传 CDN、签名 Artifact 或推进 Release。这些职责分别属于项目 Provisioning、Runtime 与 Release Pipeline。

## 安装与可用性

核心 `Build.Pipeline.Editor` Assembly 没有 Addressables 编译时引用。Availability 由 `UnityEditor.AddressableAssets.Settings.AddressableAssetSettings` 以及 Adapter 使用的精确 Editor API 形状决定。

1. 安装并锁定兼容的 `com.unity.addressables` Package。
2. 创建项目 Addressables Settings，并保存所有 Settings、Group、Schema 与 Profile Asset。
3. 配置 Active Profile 的 Local/Remote Build Path 与 Load Path。
4. 如需 Remote Delivery，同时配置 Remote Catalog Build Path 与 Load Path。
5. 确认 Editor Active Build Target 与 Pipeline 请求的 Target 一致。
6. 打开 **Build > Pipeline > Workspace Health**，要求 Workspace 为 Clean，再运行 BuildData Preflight。

Package 缺失时核心 Assembly 仍可使用，但 Provider 不可用。Package 仅部分兼容时，Preflight 会报告缺失 API，而不会回退到未经验证的调用形状。未保存的 Addressables Configuration 也会使 Preflight 失败。

## 强类型 Authoring 契约

通过 **Assets > Create > CycloneGames > Build > Addressables Build Config** 创建配置，再将持久化 Asset 分配给 BuildData 中已启用的 `asset-content` Invocation。

| 字段 | 契约 |
| --- | --- |
| `Build Remote Catalog` | 请求生成 Remote Catalog。两个求值后的 Remote Catalog Path 都必须已定义。Incremental 必须启用。 |
| `Copy To Output Directory` | 发布持久 Artifact Tree。关闭后，结果只保留在 Addressables 管理的 Build Location。Incremental 必须启用。 |
| `Publication Root` | 可移植的项目相对目录。留空时解析为 `Build/AddressablesContent/<invocation-id>`，默认隔离不同 Invocation。 |
| `Baseline Asset` | 位于 `Assets/` 下、供 Incremental Invocation 使用的已导入 `addressables_content_state.bin`。与 Baseline Path 互斥。 |
| `Baseline Path` | CI 在 Unity 启动前恢复的可移植项目相对路径。与 Baseline Asset 互斥。 |
| `Allow External Profile Publication Sources` | 只有在 CI 明确拥有外部目录时，才允许求值后的 Addressables Profile Source Root 位于项目外。URI、Volume Root、受保护路径和 Reparse Point 仍无效。 |
| `Additional Publication Roots` | 显式项目相对 Source Root，映射到唯一、安全、非保留的单段目标文件夹。 |

Additional Destination Folder 不能是 `PlayerData`、`RemoteContent`、`BuildMetadata` 或 `AddressablesArtifacts.json`。Source 不得与 Publication Root 重叠。Adapter 只接受 `Clean` 与 `Incremental`。

Invocation 的 Package Version 是规范 Content Version。Addressables 配置只控制 Provider 行为，不拥有第二个 Release Version 字段。

## Clean 内容生命周期

~~~mermaid
flowchart LR
    P["Preflight Package、Path、Profile 与已保存 Settings"] --> L["获取 Addressables Build Lock"]
    L --> S["快照 Configuration Asset"]
    S --> B["清理 Active Builder Cache 并调用 BuildPlayerContent"]
    B --> V["写入 AddressablesVersion.json 并验证输出"]
    V --> G["暂存 Manifest 与 Publication Tree"]
    G --> R["精确恢复 Settings"]
    R --> T["共享 Terminal Publication Barrier"]
    T -->|"Pipeline 成功"| C["安装并完成 Publication"]
    T -->|"任一后续失败"| X["中止并恢复旧 Publication"]
~~~

Clean Invocation 会临时通过 `BuildRemoteCatalog` 与 `OverridePlayerVersion` 应用 Pipeline Content Version。它要求 Active Data Builder 提供可用的 `ClearCachedData` 实现，先清理该 Builder Cache，再调用受支持的 `BuildPlayerContent` API。

只有 Build Result 的 `FileRegistry`、显式 Output Path、Version Artifact 和已验证 Content-State File 报告的文件才有资格发布。任何位于批准的 Player、Remote、Metadata 或 Additional Root 之外的文件都会 fail closed。

Settings 会在返回 Staged Publication 前恢复。Publication 会保持 Deferred，直到完整 Pipeline 作出 Terminal Decision；因此后续 Content、Hot Update、Player 或 Evidence 失败都会恢复之前由 Build 拥有的输出。

## Incremental Content Update

Incremental 使用官方 `ContentUpdateScript.BuildContentUpdate(AddressableAssetSettings, string)` 路径，并具有更严格的前置条件：

1. 启用 Remote Catalog；
2. 启用 Publication；
3. 通过 Baseline Asset 或 Baseline Path 提供且仅提供一个 `.bin` Baseline；
4. 将 Baseline 恢复到 Unity 项目内，并避开 `.git`、`Library`、`Logs`、`Packages`、`ProjectSettings`、`Temp` 与 `UserSettings`；
5. 保持 Baseline 位于其原始 Pipeline Publication 内，使父级 `AddressablesArtifacts.json` 可被找到。

Preflight 会加载官方 Content-State Object，并对照 Artifact Manifest 验证 Target、Active Profile ID、精确 Unity Version、Remote Catalog Load Path、Addressables Player Version、文件大小与 SHA-256。Vendor API 读取前，已验证 Baseline 会复制到 `Temp/BuildPipeline/Addressables/ContentUpdate/` 下的 Invocation-local Scratch Directory；调用结束后删除该副本。

Incremental 输出不能供 Player Invocation 使用。执行 Content Update 时应使用聚焦的 Content Invocation；生成新 Player Baseline 时应使用 Clean。Baseline 缺失、移动、修改、跨 Target、跨 Profile 或其他不兼容情况都会被拒绝。

## Player 生命周期与隔离

直接消费 Clean Addressables Content Invocation 的 Player 会打开 Provider 的独占 `addressables-player-session`：

1. Preflight 验证 Package Support 与 Provider 拥有的 Content Version。
2. Session 快照 Addressables Settings，并临时选择 `DoNotBuildWithPlayer`，防止发生第二次隐式 Content Build。
3. 已构建 Player Data 所需的 Addressables Streaming-Asset Injection 保持可用。
4. Unity 构建 Player 前，`AddressablesVersionBuildProcessor` 在 `Addressables.BuildPath` 中验证 `AddressablesVersion.json`。
5. Session Dispose 恢复原始 Setting 与精确 Serialized File。

当 Addressables 已安装，但所选 Player Recipe 不包含 Addressables Content Invocation 时，全局 Environment Guard 还会抑制 Package 的 Streaming-Asset Callback，防止未选择或陈旧的 Addressables Data 进入 Player。

两条路径都采用 fail-closed，并要求 Configuration 已保存。并发 Addressables Content Session 或 Isolation Session 会被拒绝。

## Publication 与 CI 契约

启用 Publication 后，默认布局为：

~~~text
<UnityProject>/Build/AddressablesContent/<invocation-id>/<BuildTarget>/
  PlayerData/
    AddressablesVersion.json
    ...
  RemoteContent/                 # Build 报告且配置启用时存在
  BuildMetadata/                 # 返回 ContentStateFilePath 时存在
    addressables_content_state.bin
  <AdditionalDestination>/      # 可选
  AddressablesArtifacts.json
  .buildpipeline-owner.json
~~~

`AddressablesArtifacts.json` 记录 Format Version、Target、请求的 Content Version、Incrementality、Unity Version、Active Profile Identity、Addressables Player Version、Remote Catalog Load Path，以及每个已发布文件的 Kind、可移植 Path、Size 与 SHA-256。Ownership Document 将 Publication 绑定到其 Transaction。

CI 流程：

1. 恢复精确 Unity Editor、已锁定 Addressables Package、已保存 Addressables Settings 与 BuildData Asset；
2. 在正常构建前运行 Workspace Health；
3. 调用 Pipeline 前激活所请求的 Build Target；
4. 使用规范 Batch Entry Point 与当前命名空间参数；
5. 归档完整 Publication、Ownership Document、Artifact Manifest 与 Terminal Pipeline Result Manifest；
6. 后续 Incremental Job 必须在 Unity 启动前，将完整 Clean Publication 恢复到已配置的项目相对位置。

~~~text
"<UnityEditor>" -batchmode -quit \
  -projectPath "<repo-root>/UnityStarter" \
  -executeMethod Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine \
  -buildTarget "<BuildTarget>" \
  -pipelineProfile "Assets/<path-to-BuildData>.asset"
~~~

只有已配置的聚焦 Content Update Job 才使用 `-pipelineStepIncrementality "<invocation-id>=Incremental"`。Upload、Retention、CDN Promotion 与 Rollback 属于独立 CI Stage。

## 持久化与恢复

| 数据 | 位置 | Owner 与生命周期 |
| --- | --- | --- |
| Addressables Settings、Group、Schema 与 Profile | 通常位于 `Assets/AddressableAssetsData/` | 项目拥有的 Authoring；按项目策略保存并纳入版本控制 |
| `AddressablesBuildConfig` | 项目选择的 `Assets/` 下 `.asset` | 持久强类型 BuildData 输入 |
| Provider Build Cache/Output | 求值后的 Addressables Build Path | 可重建 Provider Data，不是 Release Publication |
| Published Artifact Tree | 已配置 Publication Root | 持久 Build Artifact，由 Transaction 拥有 |
| Settings Recovery State | `.buildpipeline/transactions/addressables-settings/` | 临时持久 Snapshot；确认恢复后删除 |
| Publication Recovery State | `.buildpipeline/transactions/addressables/<invocation-id>/` | 临时持久 Stage/Backup Journal；Terminal Completion 后删除 |
| Process Lock | `Library/BuildPipeline/Addressables/build.lock` | 本机串行化 Lock，不是 Release Evidence |
| Incremental Baseline Scratch | `Temp/BuildPipeline/Addressables/ContentUpdate/<invocation-id>/` | 临时已验证副本，Invocation 后删除 |

进程硬中断后，不要手动删除 Journal、Stage Directory、Backup、Ownership File 或 Configuration Snapshot。打开 **Build > Pipeline > Workspace Health**，检查记录路径并执行显式 Recovery。CI 通过同一个规范 Entry Point 加 `-pipelineRecoverOnly` 执行恢复。

Recovery Coordinator 无需 Addressables Package 或原始 BuildData Profile 即可恢复 Settings 与 Publication。未知、损坏、并发修改或无 Owner 的状态不会被触碰，并会把 Workspace 报告为 Blocked，等待人工调查。

## 故障排查

| 现象 | 处理方式 |
| --- | --- |
| Provider 不可用 | 安装兼容 Addressables Package，并等待 Editor 编译完成。 |
| Preflight 报告 API 不完整或不受支持 | 恢复经审计的 Package/API 形状；不要绕过反射检查。 |
| 报告未保存 Configuration | 保存或回退列出的全部 Settings、Group、Schema 与 Profile Asset。 |
| Active Target 不匹配 | 运行 Invocation 前切换 Editor Active Target。 |
| Clean 报告无可用 `ClearCachedData` | 选择兼容的 Active Addressables Data Builder。 |
| Remote Catalog 文件缺失 | 检查两个 Remote Catalog Path，并确认 `FileRegistry` 报告 Catalog Data 与匹配的 `.hash`。 |
| Incremental Baseline 被拒绝 | 恢复原始 Publication，并匹配 Target、Profile、Unity Version、Remote Load Path、File Identity 与 Manifest。 |
| Player 拒绝 Incremental Content | 为该 Job 移除 Player Dependency，或把 Content Invocation 改为 Clean。 |
| 报告 Publication Source/Output 重叠 | 移动 Source 或 Publication Root；不要发布到 Provider Source Tree。 |
| Workspace 要求恢复 | Retry 或修改 Output Path 前，使用 Workspace Health 或 `-pipelineRecoverOnly`。 |

## 验证边界

EditMode Test 覆盖 Provider Registration、Output Claim、Path Policy、官方 API Selector、Content Update Baseline/Manifest 检查、Player Hook Isolation、Settings Restoration、Transactional Publication、Crash Checkpoint、Ownership 与 Recovery Failure Path。

这些测试与源码审查不证明已安装 Addressables Package、Vendor Content Build、Target Player Build、Runtime Catalog Loading、Remote Hosting、CDN 行为、Managed Stripping、IL2CPP 或平台特定文件系统行为。Release Qualification 需要锁定 Optional Package，并针对每个受支持 Target 至少执行：

1. Clean Content Build 与依赖它的 Player Build；
2. 检查 Published Manifest 与精确 File Inventory；
3. Runtime 加载 Local Content 与已配置 Remote Content；
4. 在 Clean Workspace 中从已归档 Publication 执行 Incremental Build；
5. 对修改后的 Target、Profile、Unity Version、Remote Load Path、Baseline Bytes 与 Manifest Bytes 执行负向测试；
6. 在 Artifact Promotion 前执行一次中断与 Recovery 演练。

## 源码索引

- [AddressablesBuildConfig.cs](../../Authoring/Content/AddressablesBuildConfig.cs)
- [AddressablesBuildConfigEditor.cs](../../Authoring/Content/AddressablesBuildConfigEditor.cs)
- [AddressablesContentBuildAdapter.cs](AddressablesContentBuildAdapter.cs)
- [AddressablesBuilder.cs](AddressablesBuilder.cs)
- [AddressablesVersionBuildProcessor.cs](AddressablesVersionBuildProcessor.cs)
- [AddressablesPlayerBuildIsolation.cs](AddressablesPlayerBuildIsolation.cs)
- [AddressablesPlayerBuildEnvironmentGuard.cs](AddressablesPlayerBuildEnvironmentGuard.cs)
- [AddressablesSettingsTransaction.cs](AddressablesSettingsTransaction.cs)
- [AddressablesPublicationTransaction.cs](AddressablesPublicationTransaction.cs)
- [AddressablesRecoveryCoordinator.cs](AddressablesRecoveryCoordinator.cs)
- [AddressablesArtifactManifestFormat.cs](AddressablesArtifactManifestFormat.cs)
- [Build 构建底座](../../../../README.SCH.md)
