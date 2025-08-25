# CycloneGames.Cheat
<div align="left"><a href="./README.md">English</a> | 简体中文</div>

一个基于 VitalRouter 的轻量级、类型安全的运行时 Cheat 系统。用于在 Unity 中进行调试、GM 指令或开发期便捷控制，支持结构体/类参数与异步执行，并内置同一命令的并发去重与取消能力。

## 特性

- **类型安全的指令载体**：提供 `CheatCommand` 系列类型（无参、结构体泛型、类泛型以及多参数结构体泛型）。
- **解耦的消息路由**：借助 VitalRouter 的 `[Route]` 特性进行分发，无需显式耦合发布方与订阅方。
- **线程安全与可取消**：使用 `ConcurrentDictionary` 管理命令执行状态与 `CancellationTokenSource`；同一 `CommandID` 在执行中会被去重；支持取消。
- **异步执行**：基于 Cysharp UniTask，避免阻塞主线程。

## 安装与依赖

- Unity：`2022.3`+
- 依赖包：
  - `com.cysharp.unitask` ≥ `2.0.0`
  - `jp.hadashikick.vitalrouter` ≥ `1.6.0`

可通过 UPM 或将本包放入 `Packages`/`Assets` 进行引用。包信息参考本目录下 `package.json`。

## 快速上手

### 1) 发布指令（Publish）

```csharp
using CycloneGames.Cheat;
using Cysharp.Threading.Tasks;

// 无参指令
CheatCommandUtility.PublishCheatCommand("Protocol_CheatMessage_A").Forget();

// 结构体参数（示例：自定义结构体 GameData）
var data = new GameData(/* ... */);
CheatCommandUtility.PublishCheatCommand("Protocol_GameDataMessage", data).Forget();

// 引用类型参数（示例：string）
CheatCommandUtility.PublishCheatCommand("Protocol_CustomStringMessage", "Hello").Forget();
```

### 2) 处理指令（Handle）

使用 VitalRouter 的 `[Route]` 属性在任意类/方法中声明订阅。方法参数类型即为要处理的命令类型。

```csharp
using CycloneGames.Cheat;
using UnityEngine;
using VitalRouter;

public class CheatHandlers
{
    [Route]
    void OnCheat(CheatCommand cmd)
    {
        Debug.Log($"Received: {cmd.CommandID}");
    }

    // 结构体参数
    [Route]
    void OnGameData(CheatCommand<GameData> cmd)
    {
        Debug.Log($"GameData received, id={cmd.CommandID}");
    }
}
```

> 提示：确保 VitalRouter 在工程中已正确初始化/可用，使路由系统可以扫描到 `[Route]` 标记的方法。

## API 参考

### 命令接口与类型

- `interface ICheatCommand : VitalRouter.ICommand`
  - `string CommandID { get; }`：命令标识，用户自定义并与处理逻辑对应。

- `readonly struct CheatCommand`
  - 无参数命令，构造：`CheatCommand(string commandId)`。

- `readonly struct CheatCommand<T> where T : struct`
  - 携带结构体参数，字段：`T Arg`；构造：`CheatCommand(string commandId, in T arg)`。

- `sealed class CheatCommandClass<T> where T : class`
  - 携带引用类型参数，字段：`T Arg`（非空校验）；构造：`CheatCommandClass(string commandId, T arg)`。

- `readonly struct CheatCommand<T1, T2> where T1 : struct where T2 : struct`
  - 双结构体参数，字段：`T1 Arg1`、`T2 Arg2`。

- `readonly struct CheatCommand<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct`
  - 三结构体参数，字段：`T1 Arg1`、`T2 Arg2`、`T3 Arg3`。

### 发布工具类 `CheatCommandUtility`

- `UniTask PublishCheatCommand(string commandId)`
  - 发布无参命令。

- `UniTask PublishCheatCommand<T>(string commandId, T inArg) where T : struct`
  - 发布结构体参数命令。

- `UniTask PublishCheatCommand<T>(string commandId, T inArg, bool isClass = true) where T : class`
  - 发布引用类型参数命令（`inArg` 不能为空）。`isClass` 仅用于区分重载，无需关心其值。

- `void CancelCheatCommand(string commandId)`
  - 取消正在执行的该 `commandId` 命令（如果存在）。

### 执行与并发控制

- 同一 `commandId` 在执行期间会记录为“正在执行”，再次调用将被忽略（去重）。
- 每次发布会创建独立的 `CancellationTokenSource`，在完成/异常/取消后统一清理。
- 实际分发通过 `VitalRouter.Router.Default.PublishAsync(...)` 完成。

## 实战建议（Best Practices）

- **命令命名**：统一约定前缀与语义，例如 `Protocol_XXX`，便于检索与管理。
- **轻量处理**：订阅方法应尽量快速返回。耗时逻辑可继续使用 UniTask/Task 切分，以免阻塞。
- **显式错误处理**：发布侧对异常采用吞并策略（除取消外）。建议在订阅方法内自行捕获并记录异常，避免静默失败。
- **可取消性**：长时间运行或可中断的作弊指令，应在订阅逻辑内部尊重 `CancellationToken`（由 VitalRouter 传入）。
- **类型匹配**：订阅方法参数类型需与发布的命令类型完全一致（包括泛型参数），否则不会触发。

## 常见问题（FAQ）

- **订阅方法没有被触发？**
  - 检查是否使用了正确的命令类型（如 `CheatCommand` vs `CheatCommand<GameData>`）。
  - 确保方法带有 `[Route]`，且所在程序集/场景能被 VitalRouter 扫描到。
  - 确保发布与订阅的 `CommandID` 语义一致，订阅侧按需求过滤处理。

- **重复点击无效果？**
  - 同一 `commandId` 的命令在执行期间会被去重。待上一次执行结束后才会再次触发。

- **如何中止正在运行的命令？**
  - 调用 `CheatCommandUtility.CancelCheatCommand(commandId)`；订阅端需配合处理取消令牌。
