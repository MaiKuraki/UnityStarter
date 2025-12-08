# CycloneGames.Cheat

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

A lightweight, type-safe runtime cheat system for Unity, built on VitalRouter. Optimized for zero/minimal GC allocation, cross-platform compatibility, and maximum performance. Enables debugging/GM commands and developer tooling with struct/class arguments, async execution, and built-in de-duplication and cancellation.

## Features

- **Type-safe command carriers:** `CheatCommand` family for no-arg, struct-generic, class-generic, and multi-struct-arg variants.
- **Decoupled routing:** Attribute-based routing with VitalRouter `[Route]` so publishers and subscribers are not tightly coupled.
- **Async execution:** Built on Cysharp UniTask to avoid blocking the main thread.
- **Flexible integration:** Optional logger interface for custom logging integration.

## Installation & Dependencies

- Unity: `2022.3`+
- Dependencies:
  - `com.cysharp.unitask` ≥ `2.0.0`
  - `jp.hadashikick.vitalrouter` ≥ `2.0.0`

Install via UPM or place the package under `Packages`/`Assets`. See `package.json` in this folder for details.

## Quick Start

### 1) Publish Commands

```csharp
using CycloneGames.Cheat.Runtime;
using Cysharp.Threading.Tasks;

// No-arg command (zero allocation)
CheatCommandUtility.PublishCheatCommand("Protocol_CheatMessage_A").Forget();

// Struct argument (zero allocation for value types)
var data = new GameData(/* ... */);
CheatCommandUtility.PublishCheatCommand("Protocol_GameDataMessage", data).Forget();

// Class argument (allocates on heap)
CheatCommandUtility.PublishCheatCommandWithClass("Protocol_CustomStringMessage", "Hello").Forget();

// Custom router (for domain-specific routing)
CheatCommandUtility.PublishCheatCommand("UI_ShowPopup", customRouter).Forget();
```

### 2) Handle Commands (with VitalRouter)

Use VitalRouter's `[Route]` attribute to subscribe anywhere. The method parameter type is the command type to handle.

```csharp
using CycloneGames.Cheat.Runtime;
using UnityEngine;
using VitalRouter;

public class CheatHandlers
{
    [Route]
    void OnCheat(CheatCommand cmd)
    {
        Debug.Log($"Received: {cmd.CommandID}");
    }

    // Struct argument
    [Route]
    void OnGameData(CheatCommand<GameData> cmd)
    {
        Debug.Log($"GameData received, id={cmd.CommandID}");
    }

    // Async with cancellation support
    [Route]
    async UniTask OnLongRunningCommand(CheatCommand cmd, CancellationToken ct)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: ct);
        Debug.Log("Long task completed");
    }
}
```

> Tip: Ensure VitalRouter is correctly initialized/available so that it can discover methods marked with `[Route]`.

### 3) Optional Logger Integration

```csharp
// Implement custom logger
public class CustomCheatLogger : ICheatLogger
{
    public void LogError(string message) { /* custom logging */ }
    public void LogException(Exception exception) { /* custom logging */ }
}

// Set logger (or leave null to disable logging)
CheatCommandUtility.Logger = new CustomCheatLogger();
```

## API Reference

### Command Interfaces and Types

- `interface ICheatCommand : VitalRouter.ICommand`
  - `string CommandID { get; }` – User-defined command identifier aligned with handling logic.

- `readonly struct CheatCommand`
  - No-arg command. Zero allocation. Ctor: `CheatCommand(string commandId)`.

- `readonly struct CheatCommand<T> where T : struct`
  - Struct-argument command. Zero allocation. Field: `T Arg`. Ctor: `CheatCommand(string commandId, in T arg)`.

- `sealed class CheatCommandClass<T> where T : class`
  - Class-argument command. Allocates on heap. Field: `T Arg` (non-null). Ctor: `CheatCommandClass(string commandId, T arg)`.

- `readonly struct CheatCommand<T1, T2> where T1 : struct where T2 : struct`
  - Two struct arguments. Zero allocation. Fields: `T1 Arg1`, `T2 Arg2`.

- `readonly struct CheatCommand<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct`
  - Three struct arguments. Zero allocation. Fields: `T1 Arg1`, `T2 Arg2`, `T3 Arg3`.

### Publish Utility `CheatCommandUtility`

- `UniTask PublishCheatCommand(string commandId, Router router = null)`
  - Publish a no-arg command. Zero allocation for struct commands.

- `UniTask PublishCheatCommand<T>(string commandId, T inArg, Router router = null) where T : struct`
  - Publish a struct-argument command. Zero allocation.

- `UniTask PublishCheatCommandWithClass<T>(string commandId, T inArg, Router router = null) where T : class`
  - Publish a class-argument command (argument must be non-null). Allocates on heap.

- `void CancelCheatCommand(string commandId)`
  - Cancel the running command with the same `commandId` (if any). Thread-safe.

- `void ClearAll()`
  - Clears all running commands and resets internal state. Use with caution.

- `ICheatLogger Logger { get; set; }`
  - Optional logger interface for custom logging integration. Set to null to disable.

### Logger Interface `ICheatLogger`

- `void LogError(string message)`
  - Log an error message.

- `void LogException(Exception exception)`
  - Log an exception.

## Execution & Concurrency

- The same `commandId` is marked as running and subsequent publishes are ignored until completion.
- Dispatching uses `VitalRouter.Router.Default.PublishAsync(...)` under the hood (or custom router if provided).

## Best Practices

- **Prefer struct commands:** Use `CheatCommand<T>` for value types to achieve zero allocation.
- **Avoid class commands when possible:** `CheatCommandClass<T>` allocates on heap - use sparingly.
- **Cache command IDs:** Store command ID strings as static readonly fields to avoid repeated allocations.
- **Command naming:** Establish consistent prefixes and semantics, e.g., `Protocol_XXX`, for easier discovery and management.
- **Keep handlers light:** Return quickly; use UniTask/Task to split long-running work to avoid blocking.
- **Explicit error handling:** Publisher side swallows exceptions (except cancellation). Handle and log errors in subscribers to avoid silent failures.
- **Cancellability:** For long-running or interruptible commands, honor the `CancellationToken` (provided by VitalRouter) in subscriber logic.
- **Type matching:** Subscriber parameter types must exactly match the published command type (including generic arguments), or the route won't trigger.
- **Custom routers:** Use domain-specific routers (UI, Gameplay, etc.) for better code organization and performance.

## FAQ

- **Subscriber method not triggered?**
  - Ensure you use the correct command type (e.g., `CheatCommand` vs `CheatCommand<GameData>`).
  - Ensure the method has `[Route]` and that VitalRouter can scan the assembly/scene containing it.
  - Ensure publisher/subscriber agree on the `CommandID` semantics; filter as needed on the subscriber side.

- **Repeated clicks do nothing?**
  - The same `commandId` is de-duplicated while running. Try again after the previous run finishes.

- **How to stop a running command?**
  - Call `CheatCommandUtility.CancelCheatCommand(commandId)`. Subscriber code should also honor the cancellation token.