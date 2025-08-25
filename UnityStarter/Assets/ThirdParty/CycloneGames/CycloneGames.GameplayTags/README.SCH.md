# CycloneGames.GameplayTags
[English](./README.md) | 简体中文

来源：`https://github.com/BandoWare/GameplayTags`

本包在上游基础上做了适配与扩展，保持接近 Unreal Engine 的标签工作流，并增强了运行时特性与初始化流程。

本代码库在标签注册流程中，拓展了静态类注册方式，以及运行时动态创建方式，以便更好的适配兼容热更新游戏以及网络游戏。

## 概览

- 目标：更好地兼容热更新与灵活配置的设计。
- 代码库扩展：静态类标签注册、运行时动态注册、运行时提前初始化。

## 注册方式

### 1）通过程序集特性声明标签

```csharp
[assembly: GameplayTag("Damage.Fatal")]
[assembly: GameplayTag("Damage.Miss")]
[assembly: GameplayTag("CrowdControl.Stunned")]
[assembly: GameplayTag("CrowdControl.Slow")]
```

### 2）通过静态类声明标签（常量字符串）（本代码库独有拓展，推荐使用此方式在项目中使用）

在程序集添加指向静态类的特性：

```csharp
using CycloneGames.GameplayTags.Runtime;

[assembly: RegisterGameplayTagsFrom(typeof(ProjectGameplayTags))]

public static class ProjectGameplayTags
{
    public const string Damage_Fatal = "Damage.Fatal";
    public const string Damage_Miss = "Damage.Miss";
}
```

管理器会在初始化时扫描该类的 public const string 字段并注册为标签。
强烈推荐在需要热更新的项目中使用该方式：可将标签集中在生成/静态类中，无需修改资产即可生效。

### 3）在运行时动态注册标签（本代码库独有拓展，目的是兼容运行时临时多出的动态标签，可能由服务器生成）

```csharp
GameplayTagManager.RegisterDynamicTag("Runtime.Dynamic.Tag");
GameplayTagManager.RegisterDynamicTags(new [] { "A.B", "A.C" });
```

## 用法

### 请求并使用标签

```csharp
var tag = GameplayTagManager.RequestTag("Damage.Fatal");
GameplayTagCountContainer container = new GameplayTagCountContainer();
container.AddTag(tag);

container.RegisterTagEventCallback(tag, GameplayTagEventType.AnyCountChange, (t, count) =>
{
    UnityEngine.Debug.Log($"{t.Name} 计数 -> {count}");
});
```

### 容器与查询

- `GameplayTagContainer`：可序列化集合，带编辑器检视与实用方法。
- `GameplayTagCountContainer`：维护计数与回调。
- `GameplayTagQuery` / `GameplayTagQueryExpression`：构建并评估复杂标签条件。

## 与上游差异（BandoWare/GameplayTags）

- 命名空间/包：`CycloneGames.GameplayTags` vs `BandoWare.GameplayTags`。
- 新增运行时动态注册：
  - `GameplayTagManager.RegisterDynamicTag(string name, string? description = null, GameplayTagFlags flags = GameplayTagFlags.None)`
  - `GameplayTagManager.RegisterDynamicTags(IEnumerable<string> tags)`
- 静态类自动注册：
  - `[assembly: RegisterGameplayTagsFrom(typeof(YourStaticClass))]` 扫描常量字符串并注册。
- 启动安全：
  - `GameplayTagManagerRuntimeInitialization` 在 `BeforeSceneLoad` 提前初始化，避免构建后反序列化时机问题。
- 行为调整：
  - `RequestTag` 对未注册标签不再警告，返回 `GameplayTag.None`；提供 `TryRequestTag` 便于无异常探测。
