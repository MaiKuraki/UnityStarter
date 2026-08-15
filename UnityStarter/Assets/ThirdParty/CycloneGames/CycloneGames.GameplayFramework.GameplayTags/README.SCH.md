# CycloneGames.GameplayFramework.GameplayTags

[English](README.md)

## 模块概述

本包连接 GameplayFramework Actor 与 CycloneGames GameplayTags，同时不把 GameplayTags 类型加入 GameplayFramework Runtime assembly。它提供专用 helper，用于定位 Actor 上的 `GameObjectGameplayTagContainer`，并操作其 `GameplayTagCountContainer`。

本 integration 不创建或持有 tag container。应通过正常的 scene、prefab 或 runtime composition 添加并配置 GameplayTags component。

## 程序集与依赖

| 程序集 | 用途 | Consumer 引用方式 |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayTags` | 提供 Actor 到 GameplayTags 的 extension method。 | 显式 |
| `CycloneGames.GameplayFramework.Integrations.GameplayTags.Tests.Editor` | 验证缺少 container 时的行为和 tag count 操作。 | 仅 Test Runner |

本包通过 UPM 显式依赖 GameplayFramework 与 GameplayTags。Runtime assembly 使用 `autoReferenced: false`；调用 extension 的项目 assembly 必须显式引用它。

## 安装

### UPM

安装 `com.cyclone-games.gameplay-framework-gameplay-tags`。Package Manager 会解析声明的两个模块依赖，不需要 Scripting Define Symbol。

### 嵌入 Assets

将本包、`CycloneGames.GameplayFramework` 与 `CycloneGames.GameplayTags` 放入项目 Assets 目录。Direct asmdef reference 会在这些 package root 存在时启用 integration。不使用 PlayerSettings symbol 或生成的 capability 文件。

## Actor 配置

将 `GameObjectGameplayTagContainer` 添加到 Actor 所在的同一 GameObject。Integration 只查询该 GameObject，不搜索父节点或子节点。

~~~csharp
if (actor.TryGetGameplayTagContainer(out GameplayTagCountContainer tags))
{
    bool hasState = tags.HasTag(stateTag);
}
~~~

Convenience method 包括：

- `TryGetGameplayTagContainer`
- `ActorHasGameplayTag`
- `AddGameplayTag`
- `RemoveGameplayTag`

Actor 或 component 引用缺失时返回 `false`。找到 container 后，验证和计数语义交给 `GameplayTagCountContainer`，包括它对无效 tag 的处理。

## 所有权与生命周期

- `GameObjectGameplayTagContainer` 通过自身 component API 持有对外暴露的 runtime tag-count container。
- Integration 不会 dispose、替换或序列化该 container。
- Actor 与 tag-container 的生命周期应由 scene 或 prefab composition owner 对齐。
- GameplayFramework Actor string tag 与 GameplayTags storage 是独立存储；本包不会在两者之间同步。

## 性能与线程

每次 convenience operation 都会执行 component discovery。应在 composition 阶段使用 helper，并为反复查询或修改缓存返回的 `GameplayTagCountContainer`。Gameplay 热路径应在缓存后直接调用 container API。

Unity component discovery 必须在 Unity main thread 执行。Tag 修改必须遵循 GameplayTags 的 owner-thread 契约。本 bridge 不引入 lock、copy、event stream 或跨线程队列。

## 持久化

本 integration 不写文件，也不保存偏好。Tag definition、生成的 tag data 和 gameplay 存档表示由 GameplayTags 与应用 save system 管理。

## 验证

在两个必需包均可编译后运行以下 EditMode assembly：

~~~text
CycloneGames.GameplayFramework.Integrations.GameplayTags.Tests.Editor
~~~

Runtime 验证应覆盖缺少 component、一个已配置 container、重复 add/remove 的计数行为、无效 tag、Prefab 实例化和 Actor 销毁。
