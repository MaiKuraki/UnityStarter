# CycloneGames.GameplayTags
[English](./README.md) | 简体中文

上游源码：`https://github.com/BandoWare/GameplayTags`

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

### 4）通过 JSON 文件声明标签

- 在 `ProjectSettings/GameplayTags/` 目录下创建 `.json` 文件。
- 管理器会自动扫描这些文件并注册标签。
- 此方法非常适合设计师或在不修改代码的情况下管理大量标签。

示例 `ProjectSettings/GameplayTags/DamageTags.json`:
```json
{
  "Damage.Physical.Slash": {
    "Comment": "来自挥砍武器的伤害。"
  },
  "Damage.Magical.Fire": {
    "Comment": "来自火焰法术的伤害。"
  }
}
```

## 编辑器功能

`GameplayTagContainer` 的检视面板（Inspector）已得到增强，以提供更好的工作流程：
- **编辑标签按钮**：打开一个弹出窗口，其中包含所有可用标签的可搜索、可筛选的树状视图。
- **直接添加/移除**：在树状视图中勾选或取消勾选标签，即可将其从容器中添加或移除。
- **全部清除**：一个可以快速从容器中移除所有标签的按钮。
- **实时重载**：编辑器会自动监视 `.json` 标签文件的更改并重新加载标签数据库。

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

本包现在整合了上游代码库的许多功能，同时保留了其独特的扩展。

- **统一的注册方式**：现在支持四种注册方法：程序集特性、静态类、运行时动态注册以及 **JSON 文件**。
- **增强的编辑器工作流**：显著改进了 `GameplayTagContainer` 的检视面板 UI，提供了一个弹出式树状视图用于直接管理标签。
- **新增 `GameplayTag.IsValid` 属性**：在 `GameplayTag` 上增加了一个新的布尔属性，用于检查标签是否已注册且有效，这对于处理可能已被重命名或删除的标签非常有用。
- **性能与稳定性**：
  - 包含了 `GameplayTagContainerPool` 以减少运行时的垃圾回收。
  - 在构建过程中，标签会被编译成高效的二进制格式，以便在独立运行的程序中更快地加载。
- **保留的 CycloneGames 扩展**：
  - 静态类注册 (`[assembly: RegisterGameplayTagsFrom(...)]`)。
  - 运行时动态注册 (`RegisterDynamicTag`, `RegisterDynamicTags`)。
  - 通过 `GameplayTagManagerRuntimeInitialization` 实现的运行时提前初始化。
- **一致的 API 行为**：`RequestTag` 对于缺失的标签仍然返回 `GameplayTag.None`，并且 `TryRequestTag` 仍然可用。
