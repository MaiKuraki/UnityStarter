# CycloneGames.GameplayFramework.GameplayAbilities

[English](README.md)

## 模块概述

本包连接 GameplayFramework Actor 与 CycloneGames GameplayAbilities，同时不让 GameplayFramework Runtime assembly 依赖 GameplayAbilities。它定义 provider contract，并提供用于解析 `AbilitySystemComponent`、初始化 owner/avatar 信息的专用 composition helper。

本包不创建、Tick、重置或 dispose ability system。这些职责由该 component 的应用级 owner 管理。

## 程序集与依赖

| 程序集 | 用途 | Consumer 引用方式 |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities` | 定义 `IAbilitySystemProvider` 和 Actor composition helper。 | 显式 |
| `CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor` | 验证 provider 发现和 actor-info 初始化。 | 仅 Test Runner |

本包通过 UPM 显式依赖 GameplayFramework 与 GameplayAbilities。Runtime assembly 使用 `autoReferenced: false`；调用 bridge 的项目 assembly 必须显式引用它。

## 安装

### UPM

安装 `com.cyclone-games.gameplay-framework-gameplay-abilities`。Package Manager 会解析声明的两个模块依赖，不需要 Scripting Define Symbol。

### 嵌入 Assets

将本包、`CycloneGames.GameplayFramework` 与 `CycloneGames.GameplayAbilities` 放入项目 Assets 目录。Direct asmdef reference 会在这些 package root 存在时启用 integration。不使用 PlayerSettings symbol 或生成的 capability 文件。

## 提供 Ability System

在 Actor 子类或同一 GameObject 上的 component 中实现 `IAbilitySystemProvider`：

~~~csharp
public sealed class AbilitySystemProvider : MonoBehaviour, IAbilitySystemProvider
{
    public AbilitySystemComponent AbilitySystem { get; private set; }

    public void Initialize(AbilitySystemComponent abilitySystem)
    {
        AbilitySystem = abilitySystem;
    }
}
~~~

在 composition 或 Actor 启动阶段解析并初始化关系：

~~~csharp
if (!actor.InitializeAbilityActorInfo())
{
    // Actor 没有已初始化的 IAbilitySystemProvider。
}
~~~

没有 override 时，helper 会优先使用 `Actor.GetOwner()`，不存在时回退到 Actor 自身，并使用该 Actor 作为 avatar。接收 owner 与 avatar Actor 的 overload 允许应用显式表达持久 owner 和可替换 avatar。

## 所有权与生命周期

- `IAbilitySystemProvider` 暴露已存在的 `AbilitySystemComponent`，不会转移所有权。
- `InitializeAbilityActorInfo` 不调度 `AbilitySystemComponent.Tick`。
- Ability-system owner 选择 clock、转发 Tick 并 dispose component。
- 重新初始化会应用当次传给 helper 的 owner/avatar 值。
- Actor 销毁不会隐式 dispose 一个被独立持有的 ability system。

## 性能与线程

`TryGetAbilitySystem` 先检查 Actor 是否实现 provider，再执行一次 `GetComponent<IAbilitySystemProvider>` 查询。应在 composition 或其他冷路径使用。反复执行 ability activation、tag 检查、effect 处理和 Tick 转发时，应缓存解析得到的 `AbilitySystemComponent`。

Actor 与 Unity component 发现必须在 Unity main thread 执行。Ability system 内部工作遵循 GameplayAbilities 的线程契约；本 bridge 不引入 lock 或跨线程队列。

## 持久化

本 integration 不写文件，也不拥有序列化 runtime 状态。Ability definition、已授予 ability、effect 和存档行为由 GameplayAbilities 与持有 ability system 的应用管理。

## 验证

在两个必需包均可编译后运行以下 EditMode assembly：

~~~text
CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor
~~~

Runtime 验证应覆盖 Actor 子类 provider、component provider、缺少 provider、显式 owner/avatar override、avatar 替换，以及应用 owner 执行 dispose。
