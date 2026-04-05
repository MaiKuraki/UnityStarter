# CycloneGames.Cheat

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

A lightweight, type-safe runtime cheat system for Unity, built on VitalRouter. Optimized for zero/minimal GC allocation, cross-platform compatibility, and maximum performance. Enables debugging/GM commands and developer tooling with struct/class arguments, async execution, and built-in de-duplication and cancellation.

## Features

- **Type-safe command carriers:** `CheatCommand` family for no-arg, struct-generic, class-generic, and multi-struct-arg variants.
- **Decoupled routing:** Attribute-based routing with VitalRouter `[Route]` so publishers and subscribers are not tightly coupled.
- **Async execution:** Built on Cysharp UniTask to avoid blocking the main thread.
- **Compile-gated stripping:** Entire implementation is gated by `ENABLE_CHEAT`. In release builds without the define, all calls become zero-cost no-ops — no `#if` needed at call sites.
- **Multi-router isolation:** Route commands through domain-specific routers (UI, Gameplay, etc.) for scoped handling.
- **Flexible integration:** Optional logger interface for custom logging integration.

## Installation & Dependencies

- Unity: `2022.3`+
- Dependencies:
  - `com.cysharp.unitask` ≥ `2.0.0`
  - `jp.hadashikick.vitalrouter` ≥ `2.0.0`

Install via UPM or place the package under `Packages`/`Assets`. See `package.json` in this folder for details.

### Enable the System

Add `ENABLE_CHEAT` to **Player Settings → Scripting Define Symbols** for configurations where cheats should be active (typically Debug / Development builds). When absent, `CheatCommandUtility` compiles to empty stubs — IL2CPP inlines them to zero cost.

## Quick Start

A minimal end-to-end example — publish a command and handle it.

### Step 1: Define a Handler

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

### Step 2: Publish a Command

```csharp
using CycloneGames.Cheat.Runtime;
using Cysharp.Threading.Tasks;

// Anywhere in your code:
CheatCommandUtility.PublishCheatCommand("Hello_Cheat").Forget();
```

Attach `MyCheatHandler` to any GameObject, enter Play — pressing the publish code logs `Received: Hello_Cheat`.

## Usage Guide

### No-Arg Commands

The simplest form — just a command ID, zero allocation:

```csharp
CheatCommandUtility.PublishCheatCommand("Protocol_ReloadConfig").Forget();
```

### Struct-Argument Commands

Pass value-type data with zero allocation:

```csharp
public struct SpawnData
{
    public Vector3 Position;
    public int Count;
}

var data = new SpawnData { Position = Vector3.zero, Count = 5 };
CheatCommandUtility.PublishCheatCommand("Enemy_Spawn", data).Forget();
```

Handler side:

```csharp
[Route]
void OnSpawn(CheatCommand<SpawnData> cmd)
{
    Debug.Log($"Spawn {cmd.Arg.Count} enemies at {cmd.Arg.Position}");
}
```

### Multi-Arg Struct Commands

Up to 3 struct arguments, still zero allocation:

```csharp
CheatCommandUtility.PublishCheatCommand("Set_Transform", position, rotation).Forget();

// Handler
[Route]
void OnSetTransform(CheatCommand<Vector3, Quaternion> cmd)
{
    transform.SetPositionAndRotation(cmd.Arg1, cmd.Arg2);
}
```

### Class-Argument Commands

For reference types (strings, complex objects). Allocates on heap — use when struct is not feasible:

```csharp
CheatCommandUtility.PublishCheatCommandWithClass("Log_Message", "Hello from cheat!").Forget();

// Handler
[Route]
void OnLogMessage(CheatCommandClass<string> cmd)
{
    Debug.Log(cmd.Arg);
}
```

### Cancellation

Long-running commands support cooperative cancellation:

```csharp
// Start a long task
CheatCommandUtility.PublishCheatCommand("Protocol_LongTask").Forget();

// Cancel it later
CheatCommandUtility.CancelCheatCommand("Protocol_LongTask");
```

Handler honors the token:

```csharp
[Route]
async UniTask OnLongTask(CheatCommand cmd, CancellationToken ct)
{
    await UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: ct);
    Debug.Log("Task completed");
}
```

### Query Running State

```csharp
bool running = CheatCommandUtility.IsCommandRunning("Protocol_LongTask");
int total = CheatCommandUtility.RunningCommandCount;
```

### Multi-Router Isolation

Route commands through domain-specific routers to isolate handling:

```csharp
// Create dedicated routers
var uiRouter = new Router();
var gameplayRouter = new Router();

// Publish to a specific router — only handlers mapped to that router receive it
CheatCommandUtility.PublishCheatCommand("UI_ShowPopup", uiRouter).Forget();
CheatCommandUtility.PublishCheatCommand("Player_Jump", jumpData, gameplayRouter).Forget();
```

Handler registration:

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

### Logger Integration

By default, `CheatCommandUtility` uses `UnityDebugCheatLogger` which logs to Unity's Debug API. No setup required.

```csharp
// Custom logger
public class CustomCheatLogger : ICheatLogger
{
    public void LogError(string message) { /* ... */ }
    public void LogException(Exception exception) { /* ... */ }
}

CheatCommandUtility.Logger = new CustomCheatLogger();

// Disable logging entirely
CheatCommandUtility.Logger = null;
```

## API Reference

### Command Types

| Type | Allocation | Description |
|------|-----------|-------------|
| `CheatCommand` | Zero | No-arg command |
| `CheatCommand<T>` | Zero | Single struct arg |
| `CheatCommand<T1, T2>` | Zero | Two struct args |
| `CheatCommand<T1, T2, T3>` | Zero | Three struct args |
| `CheatCommandClass<T>` | Heap | Single class arg |

All implement `ICheatCommand : VitalRouter.ICommand` with a `string CommandID` property.

### `CheatCommandUtility` (Static)

| Method | Description |
|--------|-------------|
| `UniTask PublishCheatCommand(string, Router?)` | Publish no-arg command |
| `UniTask PublishCheatCommand<T>(string, T, Router?)` | Publish with struct arg |
| `UniTask PublishCheatCommand<T1,T2>(string, T1, T2, Router?)` | Publish with 2 struct args |
| `UniTask PublishCheatCommand<T1,T2,T3>(string, T1, T2, T3, Router?)` | Publish with 3 struct args |
| `UniTask PublishCheatCommandWithClass<T>(string, T, Router?)` | Publish with class arg |
| `void CancelCheatCommand(string)` | Cancel across all routers |
| `void CancelCheatCommand(string, Router)` | Cancel on specific router |
| `bool IsCommandRunning(string)` | Check if command is in-flight |
| `void ClearAll()` | Cancel and dispose all commands |
| `ICheatLogger Logger { get; set; }` | Custom logger (default: Unity Debug) |
| `int RunningCommandCount { get; }` | Number of in-flight commands |

### `ICheatLogger`

| Method | Description |
|--------|-------------|
| `void LogError(string)` | Log error message |
| `void LogException(Exception)` | Log exception |

## Execution & Concurrency

- **De-duplication:** Key is `(commandId, command type, router instance)`. Subsequent publishes with the same key are ignored until the previous completes.
- **Thread safety:** Uses `ConcurrentDictionary` on native platforms, `Dictionary + lock` on WebGL (single-threaded). Logger access uses `Volatile.Read/Write`.
- **Cancellation:** `CancelCheatCommand` triggers `CancellationTokenSource.Cancel()` → handlers receive cancellation through their `CancellationToken` parameter.

## Compile-Gate: `ENABLE_CHEAT`

| Define | Behavior |
|--------|----------|
| `ENABLE_CHEAT` defined | Full implementation — de-duplication, routing, cancellation |
| `ENABLE_CHEAT` absent | All methods are no-op stubs returning `UniTask.CompletedTask` / `false` / `0`. IL2CPP inlines to zero instructions. No runtime cost. |

Call sites never need `#if` guards — the API surface is identical in both paths.

**Recommended setup:** Add `ENABLE_CHEAT` in **Player Settings → Scripting Define Symbols** only for Debug/Development configurations.

## Best Practices

- **Prefer struct commands** for zero allocation. Use `CheatCommandClass<T>` only when reference semantics are required.
- **Cache command IDs** as `static readonly string` fields to avoid repeated string allocations.
- **Keep handlers light.** Return quickly; use UniTask for long-running work.
- **Honor `CancellationToken`** in async handlers for long-running or interruptible commands.
- **Type matching is exact.** `CheatCommand<int>` and `CheatCommand<float>` are distinct — handler parameter type must match the published command type exactly.
- **Use domain-specific routers** (UI, Gameplay, etc.) for better code organization and isolation.
- **Handle errors in subscribers.** The publisher side swallows exceptions (except cancellation). Log and handle errors in your `[Route]` methods.

## FAQ

- **Handler not triggered?**
  - Check command type match (e.g., `CheatCommand` vs `CheatCommand<GameData>`).
  - Ensure `[Routes]` partial class and `MapTo(router)` are set up.
  - Ensure `ENABLE_CHEAT` is defined in Scripting Define Symbols.

- **Repeated calls do nothing?**
  - Same `(commandId, type, router)` is de-duplicated while running. Wait for previous completion.

- **How to stop a running command?**
  - `CheatCommandUtility.CancelCheatCommand(commandId)`. Handler must honor `CancellationToken`.

- **How to strip cheats in release builds?**
  - Remove `ENABLE_CHEAT` from Scripting Define Symbols. All calls become no-ops at zero cost.
