# Obfuz Player 构建集成

Obfuz Player Integration 是显式 `player` Extension。持久 Marker Configuration 选择 Vendor 的 Durable Player Pipeline；Adapter 则在 Unity 进入 `BuildPipeline.BuildPlayer` 前验证 Package Availability、可读取 Vendor Setting 与生成的 Encryption VM。

> **当前 checkout：** 在 `Packages/manifest.json`、`Packages/packages-lock.json` 以及本模块扫描的本地 Package Manifest 中均未发现 Obfuz。Reflection Adapter 与 Missing-package Test 可编译，但本 checkout 未执行 Obfuz Provisioning、Obfuscated Player Build 或 Runtime Verification。

## 职责与边界

| 组件 | 职责 |
| --- | --- |
| `ObfuzPlayerBuildExtensionConfiguration` | 显式选择 Provider ID `obfuz` 的持久强类型 Marker |
| `ObfuzPlayerBuildExtensionAdapter` | Selected-extension Validation 与 Project-wide Environment Consistency |
| `ObfuzIntegrator` | 受支持 Obfuz 与 Obfuz4HybridCLR API 形状的窄反射边界 |
| `PlayerBuildConfiguration` | 显式选择的 Player Extension Asset 有序列表 |
| `PlayerBuildStep` | 在 `BuildPlayer` 周围验证、开始并最终展开 Environment/Extension Session |

Integration 不负责安装 Obfuz、生成或编译 Encryption VM、编辑 `ProjectSettings/Obfuz.asset`、选择混淆规则、管理 Key、自行重写 Assembly、暂存 Vendor Intermediate，或实现 Runtime Tamper Protection。

本文描述的 Player Extension 与 HybridCLR + Obfuz Hot-update Provider 相互独立。Player Obfuscation 只要求 Base Obfuz；`Obfuz4HybridCLR` 仅由独立的组合 Hot-update Workflow 使用。

## 安装与可用性

Build Assembly 没有 Obfuz 编译时依赖。Base Availability 同时要求：

- `Obfuz.Settings.ObfuzSettings`；
- `Obfuz.Settings.BuildPipelineSettings`。

Player Readiness 还要求：

- `ObfuzSettings.Instance.buildPipelineSettings` Object 可读取，通常由 `ProjectSettings/Obfuz.asset` Provision；
- 选择 Extension 时，`buildPipelineSettings.enable` 为 `true`；
- 生成的 `Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine` 类型成功编译。

安装并锁定兼容 Obfuz Package，在独立受控步骤中完成项目 Provisioning，编译 Encryption VM，保存 Project Settings，并等待 Unity 编译完成后再运行 BuildData Preflight。Adapter 读取 In-memory Setting；它不会 Hash Settings File，也不会证明当前值已经 Serialize 到磁盘，因此 CI 必须强制执行 Save/Provisioning Step。

Obfuz 缺失时，Player Extension Descriptor 不可用，已选择的现有 Configuration 会在 Preflight 失败。只有部分所需 API 存在时，Validation 会报告 Settings 不可用或不完整，而不会假设兼容。

## 强类型 Authoring

1. 从 Asset 创建菜单或 BuildData Player Card 创建 **CycloneGames > Build > Player Extensions > Obfuz**。
2. 将生成的 `ObfuzPlayerBuildExtensionConfiguration` 保存为 `Assets/` 下的 Main `.asset`。
3. 创建或选择一个 `PlayerBuildConfiguration`。
4. 在其有序 `Extensions` 列表中添加且仅添加一次 Obfuz Configuration。
5. 将该 `PlayerBuildConfiguration` 分配给所选 `player` Invocation。
6. 保存 BuildData、Player Configuration、Obfuz Marker 与 `ProjectSettings/Obfuz.asset`。
7. 运行 Workspace Health 与 Preflight。

Marker 有意不包含 Serialized Tuning Field。Obfuz Rule 与 Vendor Option 属于 Vendor-owned Project Settings；Pipeline Membership 属于 Marker Asset。该分离让选择在 BuildData 中可审查，并避免复制 Vendor Configuration。

Player Extension Provenance 包含稳定 Provider ID `obfuz`、Compatibility ID `obfuz-player`、Configuration Asset Path、GUID/Local File ID、File Hash、Size 与 Unity Dependency Hash。因此 Marker 必须保持为持久 Main Asset，不能是 Transient Object 或 Sub-asset。

Player Extension List 最多允许 64 项，并拒绝重复 Provider ID。不要把 Secret 或 Key 放入 Marker Asset；它没有承载 Secret 的契约。

## Player 构建生命周期

~~~mermaid
flowchart LR
    R["解析持久 Extension Asset"] --> F["捕获 Extension Fingerprint"]
    F --> V["验证 Package、可读取 Setting 与 Encryption VM"]
    V --> E["验证 Project-wide Obfuz Environment"]
    E --> B["BuildPlayer 前立即再次验证"]
    B --> O["Vendor Player Pipeline 在 Unity Build 中执行混淆"]
    O --> T["Pipeline 验证 Player Result 与 Terminal Evidence"]
~~~

Preflight 执行两类相关检查：

1. Selected Adapter 要求兼容 Base Obfuz API、可读取且已启用的 Player-pipeline Setting 与已编译 Encryption VM；
2. Environment Guard 对每次 Player Build 比较 Durable Vendor Setting 与 BuildData Selection。

一致性规则是精确的：

| Durable Obfuz Player Setting | 已选择 Obfuz Extension | 结果 |
| --- | --- | --- |
| Disabled | No | 允许 |
| Enabled | Yes | 其他检查通过后允许 |
| Enabled | No | 阻止，避免发生未选择的混淆 |
| Disabled | Yes | 阻止，因为请求的 Extension 不会运行 |

`BeginEnvironment` 与 `BeginPlayerBuild` 会在 Player Build 前立即重新验证。它们不修改 Settings，也不返回 Restoration Scope。真正的 Assembly Processing 仍由已安装 Obfuz 的 Player Callback 在 Unity `BuildPlayer` 调用期间拥有。

标准 Player Output Transaction、Global Build-state Guard、Result Check 与 Terminal Manifest 仍包围此次构建。它们保护 Pipeline-owned Player Output 与 State，但不证明或重建 Vendor-generated Obfuz Data。

## CI 工作流

将 Obfuz Provisioning 与 Player Production 视为两个独立 Stage：

1. 恢复精确 Unity Editor 与已锁定 Obfuz Package；
2. 恢复版本控制中的 Obfuz Settings，以及 Vendor 要求的项目自有 Generated Source；
3. 运行 Vendor Provisioning/Encryption VM Compilation，并要求 Unity 编译干净；
4. 验证 `ProjectSettings/Obfuz.asset` 已启用 Durable Player Pipeline；
5. 在 CI BuildData Profile 选择的 Player Configuration 中使用版本控制的 Obfuz Marker；
6. 运行 Workspace Health，再使用当前 `-pipelineProfile` 与 `-buildTarget` 参数调用 `Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine`；
7. 归档 Player Output、Terminal Pipeline Result Manifest、Package Lock Evidence，以及产品 Release Policy 要求的 Vendor Report；
8. Promotion 前执行 Runtime Smoke 与面向安全的检查。

不要只在工作站本机状态中切换 Vendor Setting，不要在 Release Build 内隐式生成 Encryption VM，也不要把 Package Presence 当作混淆已运行的证据。

## 持久化与恢复

| 数据 | 位置 | Owner 与生命周期 |
| --- | --- | --- |
| Obfuz Selection Marker | 项目选择的 `Assets/` 下 `.asset` | 持久 BuildData 输入；纳入版本控制 |
| Player Extension List | `Assets/` 下的 `PlayerBuildConfiguration` Asset | 持久有序选择；纳入版本控制 |
| Vendor Settings | `ProjectSettings/Obfuz.asset` | 持久 Vendor Configuration；Build 前 Provision 并保存 |
| Encryption VM 与其他 Generated Input | Vendor 定义的项目位置 | Provisioning Output；生命周期由已安装 Package 与项目策略拥有 |
| Player Output | Pipeline 配置的 Player Output Path | 由标准 Player Output Transaction 保护 |
| Obfuz-specific Recovery Journal | 无 | 此 Adapter 不修改或事务拥有 Vendor Settings/Generated File |

硬中断仍可能留下标准 Build Workspace Evidence。Retry 前使用 **Build > Pipeline > Workspace Health**，或通过规范 Batch Entry Point 加 `-pipelineRecoverOnly`。Workspace Recovery 恢复 Pipeline-owned State 与 Output；它不会重新生成 Encryption VM、编辑 `ProjectSettings/Obfuz.asset` 或修复 Vendor-generated Source。

## 故障排查

| 现象 | 处理方式 |
| --- | --- |
| Player Extension Catalog 未提供 Obfuz | 安装兼容 Base Package，并等待 Editor 编译。 |
| Selected Extension 报告 Base Obfuz 不可用 | 恢复同时公开两个必需 Settings Type 的 Package，或从 Player Configuration 移除 Marker。 |
| Settings 不可用或不完整 | Preflight 前 Provision 并保存 `ProjectSettings/Obfuz.asset`。 |
| Durable Player Pipeline 已禁用 | 启用并保存 Vendor Setting；若该 Player 不应混淆，则移除 Extension。 |
| Durable Player Pipeline 已启用但 Extension 缺失 | 将显式 Marker 添加到所选 Player Configuration，或禁用并保存 Vendor Setting。 |
| Encryption VM 未编译 | 运行 Vendor Provisioning，解决编译错误，再重新 Preflight。 |
| Duplicate Provider Error | Player Extension List 中只保留一个 `obfuz` Marker。 |
| Configuration Fingerprint 失败 | 确认 Marker 是 `Assets/` 下已保存的 Main `.asset`，且构建期间未变化。 |
| Build 成功但 Runtime 失败 | 检查 Vendor Report、Generated VM Compatibility、Stripping/AOT 行为与 Target Runtime Log。 |

## 验证边界

当前 EditMode Evidence 验证通用 Player-extension Discovery/Fingerprinting 与 Missing-package Failure Path。源码审查验证精确 Reflection Type/Member Name 与 Setting-selection Consistency Logic。此 Adapter 不包含 On-disk Obfuz Settings Fingerprint。

这些证据不证明兼容 Obfuz Package 已安装、Encryption VM 正确生成、Assembly 已转换、Runtime 执行成功，也不证明输出满足 Confidentiality、Integrity、Anti-tamper、IL2CPP、Stripping、Performance 或平台要求。

Release Qualification 必须使用已锁定 Vendor Package，并覆盖每个受支持 Target。至少执行：

1. 在 Clean CI Workspace 中 Provision 并编译 Encryption VM；
2. 选择 Extension 构建 Player，并检查 Vendor Evidence；
3. 在 Marker 与 Durable Setting 都关闭时执行 Control Build；
4. 确认 Marker/Setting 不一致的组合在 Preflight 失败；
5. 启动生成的 Player，覆盖与产品相关的 Reflection、Serialization、AOT、Stripping、Exception 与 Update Path；
6. 测量 Build Time、Size、Startup 与 Runtime Cost；
7. 独立验证产品安全需求，不能以 Identifier Obfuscation 代替。

## 源码索引

- [ObfuzPlayerBuildExtension.cs](ObfuzPlayerBuildExtension.cs)
- [ObfuzIntegrator.cs](ObfuzIntegrator.cs)
- [PlayerBuildConfiguration.cs](../../Authoring/Player/PlayerBuildConfiguration.cs)
- [PlayerBuildExtensionContracts.cs](../../Core/Contracts/PlayerBuildExtensionContracts.cs)
- [PlayerBuildStep.Extensions.cs](../../Steps/Player/PlayerBuildStep.Extensions.cs)
- [PlayerBuildStep.cs](../../Steps/Player/PlayerBuildStep.cs)
- [HybridCLR + Obfuz Provider](../HybridCLRObfuz/README.SCH.md)
- [Build 构建底座](../../../../README.SCH.md)
