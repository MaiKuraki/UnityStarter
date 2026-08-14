# CycloneGames.GameplayFramework.AssetManagement

[English](README.md)

## 模块概述

本包将 GameplayFramework `WorldSettings` 中的资源位置连接到一个由应用显式持有的 CycloneGames AssetManagement package。它提供一个 resolver，不负责初始化、选择或关闭资源 backend。

当 `WorldSettings` 条目使用 `AssetReference` 时使用本包。Prefab 直接引用不需要该 integration。`PathLocation` 应由应用根据自身寻址规则提供 resolver。

## 程序集与依赖

| 程序集 | 用途 | Consumer 引用方式 |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | 使用 `IAssetPackage` 实现 `IWorldSettingsReferenceResolver`。 | 显式 |
| `CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor` | 验证 Prefab component 解析、失败处理和即时 lease 注册。 | 仅 Test Runner |

本包通过 UPM 显式依赖 GameplayFramework、AssetManagement 和 UniTask。Runtime assembly 使用 `autoReferenced: false`；命名该 resolver 的项目 assembly 必须显式引用它。

## 安装

### UPM

安装 `com.cyclone-games.gameplay-framework-asset-management`。Package Manager 会解析其声明的 host 与 AssetManagement 依赖，不需要 Scripting Define Symbol。

### 嵌入 Assets

将本包、`CycloneGames.GameplayFramework` 与 `CycloneGames.AssetManagement` 放入项目 Assets 目录。Integration asmdef 使用直接 assembly reference，因此这些 package root 存在时会参与编译。不使用 PlayerSettings symbol 或生成的 capability 文件。

## 组合方式

在应用 composition root 中初始化并持有 AssetManagement package，再使用该 package 构造 resolver：

~~~csharp
IAssetPackage assetPackage = applicationAssets;
IWorldSettingsReferenceResolver resolver =
    new AssetManagementWorldSettingsReferenceResolver(assetPackage);

// 将 resolver 传给由应用持有的 GameInstance composition。
~~~

Resolver 支持 `WorldSettingsReferenceSource.AssetReference`。解析 component 类型时，它加载 Prefab `GameObject`，并要求 Prefab 根节点上恰好存在一个匹配 component。解析其他 Unity object 类型时，它直接加载请求的 asset。

## 所有权与失败行为

- 应用拥有 `IAssetPackage`；resolver 不会 dispose 它。
- 每个非空 `IAssetHandle` 在创建后立即注册到 Core 持有的 `IWorldSettingsLeaseRegistrar`，注册发生在读取 task、观察取消或检查 asset 状态之前。
- 每次 resolver 调用最多注册一个 non-null ownership handle。Backend 返回 non-null handle 后，resolver 必须在后续首个 failure point 前注册其 owner。若 backend 需要创建多个子 handle，必须预先创建并注册一个复合 `IDisposable`，再在该 owner 下创建全部子 handle。
- 注册会把唯一 dispose 责任转移给 GameplayFramework。Resolver 不会 dispose 已注册 handle，load result 也不再包含 lease 字段。
- 解析回滚与 World 关闭通过可重试的 GameplayFramework 生命周期 owner dispose 已注册 lease。Dispose 失败的 lease 会继续被持有并允许重试，不会被静默丢弃。
- 取消通过 `OperationCanceledException` 继续传播。
- `OutOfMemoryException`（包括嵌套在 `AggregateException` 中的情况）会在 handle 所有权完成转移后继续传播，不会被转换为普通加载失败。
- 无效位置和不符合要求的 Prefab 内容会返回包含错误信息的失败结果。

## 性能与线程

解析发生在 World 启动或 travel 阶段，不是每帧 API。Prefab component 解析会查询根节点 component，并可能产生分配。Gameplay 热路径应复用已解析的 runtime definition，不应重复解析引用。

资源加载完成可能发生在 Unity main thread 之外。Resolver 会在读取 Unity object 前切换到 main thread。Handle 注册、World 创建、运行时访问、回滚和关闭仍受 GameplayFramework owner thread 约束。

## 持久化

本 integration 不写文件，也不保存偏好。Asset cache、catalog、storage 和恢复行为归所选 AssetManagement backend 及其应用级 owner 管理。

## 验证

在两个必需包均可编译后运行以下 EditMode assembly：

~~~text
CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor
~~~

针对 Player 平台，应验证一个 direct-reference World、一个 asset-reference World、handle 创建后的取消与内存不足传播、可重试 World 关闭，以及关闭后的 backend handle 数量。
