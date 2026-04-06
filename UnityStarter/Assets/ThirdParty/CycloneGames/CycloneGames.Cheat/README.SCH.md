# CycloneGames.Cheat

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

一个基于 VitalRouter 的轻量级、类型安全的运行时 Cheat 系统。针对 GC、跨平台和性能进行了优化。用于在 Unity 中进行调试、GM 指令或开发期便捷控制，支持结构体/类参数与异步执行，并内置同一命令的并发去重与取消能力。

## 特性

- **类型安全的指令载体**：提供 `CheatCommand` 系列类型（无参、结构体泛型、类泛型以及多参数结构体泛型）。
- **解耦的消息路由**：借助 VitalRouter 的 `[Route]` 特性进行分发，无需显式耦合发布方与订阅方。
- **异步执行**：基于 Cysharp UniTask，避免阻塞主线程。
- **编译级剥离**：整个实现由 `ENABLE_CHEAT` 宏门控。发布版不定义该宏时，所有调用编译为零成本空壳 — 调用侧无需 `#if` 包裹。
- **多路由隔离**：可通过领域专用路由（UI、Gameplay 等）实现命令的分域处理。
- **灵活集成**：可选的日志接口，支持自定义日志集成。

## 安装与依赖

- Unity：`2022.3`+
- 依赖包：
  - `com.cysharp.unitask` ≥ `2.0.0`
  - `jp.hadashikick.vitalrouter` ≥ `2.0.0`

可通过 UPM 或将本包放入 `Packages`/`Assets` 进行引用。包信息参考本目录下 `package.json`。

### 启用系统

在 **Player Settings → Scripting Define Symbols** 中添加 `ENABLE_CHEAT`（通常仅在 Debug / Development 配置下添加）。未定义时，`CheatCommandUtility` 编译为空壳 — IL2CPP 会将其内联为零开销。

## 快速上手

一个最简的端到端示例 — 发布命令并处理它。

### 第 1 步：定义处理器

```csharp
using CycloneGames.Cheat.Runtime;
using UnityEngine;
using VitalRouter;

[Routes]
public partial class MyCheatHandler : MonoBehaviour
{
    void Awake() => MapTo(Router.Default);
    void OnDestroy() => UnmapRoutes();

    [Route]
    void OnCheat(CheatCommand cmd)
    {
        Debug.Log($"Received: {cmd.CommandID}");
    }
}
```

### 第 2 步：发布命令

```csharp
using CycloneGames.Cheat.Runtime;
using Cysharp.Threading.Tasks;

// 在代码任意位置：
CheatCommandUtility.PublishCheatCommand("Hello_Cheat").Forget();
```

将 `MyCheatHandler` 挂载到任意 GameObject，进入 Play 模式 — 执行发布代码后控制台输出 `Received: Hello_Cheat`。

## 使用指南

### 无参命令

最简形式 — 仅命令 ID，零分配：

```csharp
CheatCommandUtility.PublishCheatCommand("Protocol_ReloadConfig").Forget();
```

### 结构体参数命令

传递值类型数据，零分配：

```csharp
public struct SpawnData
{
    public Vector3 Position;
    public int Count;
}

var data = new SpawnData { Position = Vector3.zero, Count = 5 };
CheatCommandUtility.PublishCheatCommand("Enemy_Spawn", data).Forget();
```

处理侧：

```csharp
[Route]
void OnSpawn(CheatCommand<SpawnData> cmd)
{
    Debug.Log($"Spawn {cmd.Arg.Count} enemies at {cmd.Arg.Position}");
}
```

### 多参数结构体命令

最多可传递 3 个结构体参数，仍然零分配：

```csharp
CheatCommandUtility.PublishCheatCommand("Set_Transform", position, rotation).Forget();

// 处理器
[Route]
void OnSetTransform(CheatCommand<Vector3, Quaternion> cmd)
{
    transform.SetPositionAndRotation(cmd.Arg1, cmd.Arg2);
}
```

### 引用类型参数命令

用于引用类型（字符串、复杂对象）。会产生堆分配 — 当结构体不可行时使用：

```csharp
CheatCommandUtility.PublishCheatCommandWithClass("Log_Message", "Hello from cheat!").Forget();

// 处理器
[Route]
void OnLogMessage(CheatCommandClass<string> cmd)
{
    Debug.Log(cmd.Arg);
}
```

### 取消机制

长时间运行的命令支持协作式取消：

```csharp
// 启动长任务
CheatCommandUtility.PublishCheatCommand("Protocol_LongTask").Forget();

// 稍后取消
CheatCommandUtility.CancelCheatCommand("Protocol_LongTask");
```

处理器中配合取消令牌：

```csharp
[Route]
async UniTask OnLongTask(CheatCommand cmd, CancellationToken ct)
{
    await UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: ct);
    Debug.Log("Task completed");
}
```

### 查询运行状态

```csharp
bool running = CheatCommandUtility.IsCommandRunning("Protocol_LongTask");
int total = CheatCommandUtility.RunningCommandCount;
```

### 多路由隔离

通过领域专用路由实现命令的分域处理：

```csharp
// 创建专用路由
var uiRouter = new Router();
var gameplayRouter = new Router();

// 发布到指定路由 — 只有映射到该路由的处理器才会收到
CheatCommandUtility.PublishCheatCommand("UI_ShowPopup", uiRouter).Forget();
CheatCommandUtility.PublishCheatCommand("Player_Jump", jumpData, gameplayRouter).Forget();
```

处理器注册：

```csharp
[Routes]
public partial class UICheatHandler
{
    public UICheatHandler(Router uiRouter) => MapTo(uiRouter);

    [Route]
    void OnUICommand(CheatCommand cmd)
    {
        Debug.Log($"UI: {cmd.CommandID}");
    }
}
```

### 日志集成

默认使用 `UnityDebugCheatLogger`，输出到 Unity 的 Debug API。无需额外设置。

```csharp
// 自定义日志器
public class CustomCheatLogger : ICheatLogger
{
    public void LogError(string message) { /* ... */ }
    public void LogException(Exception exception) { /* ... */ }
}

CheatCommandUtility.Logger = new CustomCheatLogger();

// 完全禁用日志
CheatCommandUtility.Logger = null;
```

## API 参考

### 命令类型

| 类型 | 分配 | 说明 |
|------|------|------|
| `CheatCommand` | 零分配 | 无参命令 |
| `CheatCommand<T>` | 零分配 | 单个结构体参数 |
| `CheatCommand<T1, T2>` | 零分配 | 两个结构体参数 |
| `CheatCommand<T1, T2, T3>` | 零分配 | 三个结构体参数 |
| `CheatCommandClass<T>` | 堆分配 | 单个引用类型参数 |

所有类型均实现 `ICheatCommand : VitalRouter.ICommand`，提供 `string CommandID` 属性。

### 发布工具类 `CheatCommandUtility`（静态）

| 方法 | 说明 |
|------|------|
| `UniTask PublishCheatCommand(string, Router?)` | 发布无参命令 |
| `UniTask PublishCheatCommand<T>(string, T, Router?)` | 发布结构体参数命令 |
| `UniTask PublishCheatCommand<T1,T2>(string, T1, T2, Router?)` | 发布双结构体参数命令 |
| `UniTask PublishCheatCommand<T1,T2,T3>(string, T1, T2, T3, Router?)` | 发布三结构体参数命令 |
| `UniTask PublishCheatCommandWithClass<T>(string, T, Router?)` | 发布引用类型参数命令 |
| `void CancelCheatCommand(string)` | 跨所有路由取消 |
| `void CancelCheatCommand(string, Router)` | 在指定路由上取消 |
| `bool IsCommandRunning(string)` | 查询命令是否正在执行 |
| `void ClearAll()` | 取消并释放所有命令 |
| `ICheatLogger Logger { get; set; }` | 自定义日志器（默认：Unity Debug） |
| `int RunningCommandCount { get; }` | 当前运行中的命令数量 |

### 日志接口 `ICheatLogger`

| 方法 | 说明 |
|------|------|
| `void LogError(string)` | 记录错误消息 |
| `void LogException(Exception)` | 记录异常 |

## 执行与并发控制

- **去重**：键为 `(commandId, command type, router instance)`，同一键在执行期间再次调用会被忽略，直到前一次完成。
- **线程安全**：原生平台使用 `ConcurrentDictionary`，WebGL 使用 `Dictionary + lock`（单线程环境）。Logger 访问使用 `Volatile.Read/Write`。
- **取消机制**：`CancelCheatCommand` 触发 `CancellationTokenSource.Cancel()` → 处理器通过 `CancellationToken` 参数接收取消信号。

## 编译开关：`ENABLE_CHEAT`

| 定义情况 | 行为 |
|---------|------|
| 定义 `ENABLE_CHEAT` | 完整实现 — 去重、路由分发、取消 |
| 未定义 `ENABLE_CHEAT` | 所有方法均为空壳，返回 `UniTask.CompletedTask` / `false` / `0`。IL2CPP 内联为零指令，无运行时开销。 |

调用侧无需 `#if` 包裹 — 两条路径的 API 签名完全一致。

**推荐配置**：仅在 Debug / Development 构建配置的 **Player Settings → Scripting Define Symbols** 中添加 `ENABLE_CHEAT`。

## 实战建议

- **优先使用结构体命令**以实现零分配。仅在需要引用语义时使用 `CheatCommandClass<T>`。
- **缓存命令 ID** 为 `static readonly string` 字段，避免重复的字符串分配。
- **轻量处理**：订阅方法应尽量快速返回，耗时逻辑使用 UniTask 切分。
- **尊重 `CancellationToken`**：异步处理器中配合取消令牌，用于长时间运行或可中断的命令。
- **类型匹配是严格的**：`CheatCommand<int>` 和 `CheatCommand<float>` 是不同类型 — 处理器参数类型必须与发布的命令类型完全一致。
- **使用领域专用路由**（UI、Gameplay 等）以获得更好的代码组织和隔离性。
- **在订阅方法内处理错误**：发布侧吞并异常（除取消外）。请在 `[Route]` 方法中自行捕获并记录异常。

## 常见问题（FAQ）

- **处理器没有被触发？**
  - 检查命令类型是否匹配（如 `CheatCommand` vs `CheatCommand<GameData>`）。
  - 确保使用 `[Routes]` partial class 并调用了 `MapTo(router)`。
  - 确保 `ENABLE_CHEAT` 已添加到 Scripting Define Symbols。

- **重复调用无效果？**
  - 同一 `(commandId, type, router)` 在执行期间会被去重。等待前一次完成后才会再次触发。

- **如何中止正在运行的命令？**
  - 调用 `CheatCommandUtility.CancelCheatCommand(commandId)`；处理器需配合处理 `CancellationToken`。

- **如何在发布版中剥离 Cheat？**
  - 从 Scripting Define Symbols 中移除 `ENABLE_CHEAT`。所有调用变为零成本空操作。
