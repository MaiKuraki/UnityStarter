# CycloneGames.Cheat

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

A lightweight, type-safe runtime cheat system for Unity, built on VitalRouter. It enables debugging/GM commands and developer tooling with struct/class arguments, async execution, and built-in de-duplication and cancellation for the same command ID.

## Features

- **Type-safe command carriers:** `CheatCommand` family for no-arg, struct-generic, class-generic, and multi-struct-arg variants.
- **Decoupled routing:** Attribute-based routing with VitalRouter `[Route]` so publishers and subscribers are not tightly coupled.
- **Thread-safe and cancellable:** Uses `ConcurrentDictionary` to track execution state and `CancellationTokenSource` per command; same `CommandID` is de-duplicated while running.
- **Async execution:** Built on Cysharp UniTask to avoid blocking the main thread.

## Installation & Dependencies

- Unity: `2022.3`+
- Dependencies:
  - `com.cysharp.unitask` ≥ `2.0.0`
  - `jp.hadashikick.vitalrouter` ≥ `1.6.0`

Install via UPM or place the package under `Packages`/`Assets`. See `package.json` in this folder for details.

## Quick Start

### 1) Publish Commands

```csharp
using CycloneGames.Cheat;
using Cysharp.Threading.Tasks;

// No-arg command
CheatCommandUtility.PublishCheatCommand("Protocol_CheatMessage_A").Forget();

// Struct argument (example: custom struct GameData)
var data = new GameData(/* ... */);
CheatCommandUtility.PublishCheatCommand("Protocol_GameDataMessage", data).Forget();

// Class argument (example: string)
CheatCommandUtility.PublishCheatCommand("Protocol_CustomStringMessage", "Hello").Forget();
```

### 2) Handle Commands (with VitalRouter)

Use VitalRouter's `[Route]` attribute to subscribe anywhere. The method parameter type is the command type to handle.

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

    // Struct argument
    [Route]
    void OnGameData(CheatCommand<GameData> cmd)
    {
        Debug.Log($"GameData received, id={cmd.CommandID}");
    }
}
```

> Tip: Ensure VitalRouter is correctly initialized/available so that it can discover methods marked with `[Route]`.

## API Reference

### Command Interfaces and Types

- `interface ICheatCommand : VitalRouter.ICommand`
  - `string CommandID { get; }` – User-defined command identifier aligned with handling logic.

- `readonly struct CheatCommand`
  - No-arg command. Ctor: `CheatCommand(string commandId)`.

- `readonly struct CheatCommand<T> where T : struct`
  - Struct-argument command. Field: `T Arg`. Ctor: `CheatCommand(string commandId, in T arg)`.

- `sealed class CheatCommandClass<T> where T : class`
  - Class-argument command. Field: `T Arg` (non-null). Ctor: `CheatCommandClass(string commandId, T arg)`.

- `readonly struct CheatCommand<T1, T2> where T1 : struct where T2 : struct`
  - Two struct arguments. Fields: `T1 Arg1`, `T2 Arg2`.

- `readonly struct CheatCommand<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct`
  - Three struct arguments. Fields: `T1 Arg1`, `T2 Arg2`, `T3 Arg3`.

### Publish Utility `CheatCommandUtility`

- `UniTask PublishCheatCommand(string commandId)`
  - Publish a no-arg command.

- `UniTask PublishCheatCommand<T>(string commandId, T inArg) where T : struct`
  - Publish a struct-argument command.

- `UniTask PublishCheatCommand<T>(string commandId, T inArg, bool isClass = true) where T : class`
  - Publish a class-argument command (argument must be non-null). `isClass` only exists to avoid overload ambiguity.

- `void CancelCheatCommand(string commandId)`
  - Cancel the running command with the same `commandId` (if any).

## Execution & Concurrency

- The same `commandId` is marked as running and subsequent publishes are ignored until completion (de-duplication).
- Each publish creates a dedicated `CancellationTokenSource` which is cleaned up on completion/exception/cancellation.
- Dispatching uses `VitalRouter.Router.Default.PublishAsync(...)` under the hood.

## Best Practices

- **Command naming:** Establish consistent prefixes and semantics, e.g., `Protocol_XXX`, for easier discovery and management.
- **Keep handlers light:** Return quickly; use UniTask/Task to split long-running work to avoid blocking.
- **Explicit error handling:** Publisher side swallows exceptions (except cancellation). Handle and log errors in subscribers to avoid silent failures.
- **Cancellability:** For long-running or interruptible commands, honor the `CancellationToken` (provided by VitalRouter) in subscriber logic.
- **Type matching:** Subscriber parameter types must exactly match the published command type (including generic arguments), or the route won't trigger.

## FAQ

- **Subscriber method not triggered?**
  - Ensure you use the correct command type (e.g., `CheatCommand` vs `CheatCommand<GameData>`).
  - Ensure the method has `[Route]` and that VitalRouter can scan the assembly/scene containing it.
  - Ensure publisher/subscriber agree on the `CommandID` semantics; filter as needed on the subscriber side.

- **Repeated clicks do nothing?**
  - The same `commandId` is de-duplicated while running. Try again after the previous run finishes.

- **How to stop a running command?**
  - Call `CheatCommandUtility.CancelCheatCommand(commandId)`. Subscriber code should also honor the cancellation token.
