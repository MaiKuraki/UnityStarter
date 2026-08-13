# CycloneGames.GameplayFramework.AssetManagement

[English](README.md)

## 模块概述

本包将 GameplayFramework `WorldSettings` 中的资源位置连接到一个由应用显式持有的 CycloneGames AssetManagement package。它提供一个 resolver，不负责初始化、选择或关闭资源 backend。

当 `WorldSettings` 条目使用 `AssetReference` 时使用本包。Prefab 直接引用不需要该 integration。`PathLocation` 应由应用根据自身寻址规则提供 resolver。

## 程序集与依赖

| 程序集 | 用途 | Consumer 引用方式 |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | 使用 `IAssetPackage` 实现 `IWorldSettingsReferenceResolver`。 | 显式 |
| `CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor` | 验证 Prefab component 解析、asset 解析、失败处理和 lease 转移。 | 仅 Test Runner |

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
- 成功结果会把 `IAssetHandle` 作为 lease 转移给已解析的 `WorldDefinition`。
- World 关闭时由 GameplayFramework 生命周期 owner dispose 已转移的 lease。
- 解析失败或取消时会释放已获取的 handle。
- 取消通过 `OperationCanceledException` 继续传播。
- 无效位置和不符合要求的 Prefab 内容会返回包含错误信息的失败结果。

## 性能与线程

解析发生在 World 启动或 travel 阶段，不是每帧 API。Prefab component 解析会查询根节点 component，并可能产生分配。Gameplay 热路径应复用已解析的 runtime definition，不应重复解析引用。

资源加载完成可能发生在 Unity main thread 之外。Resolver 在读取 Unity object 或 dispose Unity-backed handle 前切换到 main thread。持有 GameplayFramework 的 composition 也必须在 owner thread 上创建、使用并 dispose 生成的 World。

## 持久化

本 integration 不写文件，也不保存偏好。Asset cache、catalog、storage 和恢复行为归所选 AssetManagement backend 及其应用级 owner 管理。

## 验证

在两个必需包均可编译后运行以下 EditMode assembly：

~~~text
CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor
~~~

针对 Player 平台，应验证一个 direct-reference World、一个 asset-reference World、加载期间取消、World 关闭，以及关闭后的 backend handle 数量。
