# CycloneGames.Cheat

CycloneGames.Cheat is an internal command layer for debug, QA, GM, automation, and live-ops tools. It uses VitalRouter for typed command routing and separates package-neutral contracts from Unity-facing runtime services through `Core` and `Runtime` assemblies.

This module is not an anti-cheat system. Commercial multiplayer projects must still validate authority on the server, restrict high-privilege commands by environment and identity, audit use, and avoid trusting client-side debug commands as gameplay truth.

## Layout

```text
CycloneGames.Cheat/
  Core/
    CycloneGames.Cheat.Core.asmdef
    CheatCommand.cs
    CheatDuplicatePolicy.cs
    CheatRuntimeMetrics.cs
    ICheatLogger.cs
  Runtime/
    CycloneGames.Cheat.Runtime.asmdef
    CheatCommandRuntime.cs
    CheatCommandExecutionOptions.cs
    ICheatCommandRuntime.cs
    UnityDebugCheatLogger.cs
    Integrations/DI/VContainer/
  Samples/
  Tests/Editor/
```

Namespaces mirror the folder and assembly boundary:

| Layer | Namespace | Responsibility |
| --- | --- | --- |
| Core | `CycloneGames.Cheat.Core` | Command payloads, duplicate policy, metrics, logger contract. |
| Runtime | `CycloneGames.Cheat.Runtime` | VitalRouter publishing, cancellation, lifecycle ownership, Unity logger. |
| Tests | `CycloneGames.Cheat.Tests.Editor` | Core and Runtime contract tests. |

## Runtime Model

`CheatCommandRuntime` is explicitly owned. Create it directly for non-DI projects, or register `ICheatCommandRuntime`, `ICheatCommandPublisher`, and `ICheatCommandControl` in a DI container. Disposing the runtime stops new publishes, requests cancellation for running commands, and leaves each in-flight command state to be disposed by its publishing operation when the handler unwinds.

```csharp
using CycloneGames.Cheat.Runtime;
using Cysharp.Threading.Tasks;

var runtime = new CheatCommandRuntime(new UnityDebugCheatLogger());
await runtime.PublishAsync("World_ReloadConfig");
runtime.Dispose();
```

The package intentionally does not expose a global static facade. Long-lived projects should keep ownership explicit at a scene root, tool owner, service composition root, or DI lifetime scope.

The VContainer installer lives in an optional integration assembly constrained by `VCONTAINER_PRESENT`. Projects without VContainer can remove or ignore that folder without affecting the core runtime.

## Diagnostics

`ICheatLogger` is the minimal error/exception logging contract used by the enabled runtime path.

When `ENABLE_CHEAT` is absent, `CheatCommandRuntime` is a disabled no-op runtime. Publish calls complete without dispatching a VitalRouter command, without incrementing runtime metrics, and without logging from the hot path. Samples and tool UIs should show their own startup diagnostics when they need to explain that a disabled runtime will not produce `Received` logs.

If a sample or tool logs `Publishing` but no matching `Received` log appears, check the compile symbol, target `Router`, command payload type, listener lifetime, and VitalRouter source generation errors.

## Commands

Core command types implement `ICheatCommand` and `VitalRouter.ICommand`:

| Type | Use |
| --- | --- |
| `CheatCommand` | Command ID only. |
| `CheatCommand<T>` | One struct payload. |
| `CheatCommand<T1, T2>` | Two struct payloads. |
| `CheatCommand<T1, T2, T3>` | Three struct payloads. |
| `CheatCommandClass<T>` | One reference-type payload. Prefer struct payloads on hot paths. |

For stable production workflows, prefer dedicated command structs implementing `ICheatCommand` over string-heavy catch-all handlers. Dedicated types give VitalRouter stronger routing and reduce handler-side branching.

## VitalRouter

Handlers can use VitalRouter source-generated routes:

```csharp
using System.Threading;
using CycloneGames.Cheat.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VitalRouter;

[Routes]
public partial class DebugWorldCheatHandler : MonoBehaviour
{
    private void Awake()
    {
        MapTo(Router.Default);
    }

    private void OnDestroy()
    {
        UnmapRoutes();
    }

    [Route]
    private async UniTask OnReload(CheatCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId != "World_ReloadConfig")
        {
            return;
        }

        await UniTask.Yield(cancellationToken);
    }
}
```

Use a dedicated `Router` per tool, scene, world, or authority boundary when commands should not leak across systems.

## Build And CI

`BuildData` contains `Cheat Build Mode`:

| Mode | Behavior |
| --- | --- |
| `Disabled` | Removes `ENABLE_CHEAT` during `BuildScript` player builds. |
| `DevelopmentBuilds` | Enables `ENABLE_CHEAT` only for debug/development builds. |
| `Enabled` | Enables `ENABLE_CHEAT` for all `BuildScript` builds. Use only for protected internal builds. |

CI can override the asset setting with `-enableCheat` or `-disableCheat`. Build support is intentionally decoupled from this package: the Build module only uses string symbols, reflection, and Unity compilation metadata. It detects the `CycloneGames.Cheat.Runtime` assembly contract instead of any package path, so the module can live under `Assets`, an embedded UPM package, the package cache, or another Unity-supported source location. If the runtime assembly is not present in the player compilation domain, `ENABLE_CHEAT` is not applied and normal builds continue.

## Persistence

The Cheat module does not write runtime files, save data, preferences, caches, or assets. It owns only in-memory command state and logger references. Any GM console history, audit trail, remote authorization, or cross-device synchronization should be implemented by the owning product or live-ops layer with explicit storage, schema versioning, access control, and migration.

## Validation

Minimum validation:

1. Run `CycloneGames.Cheat.Tests.Editor`.
2. Compile once with `ENABLE_CHEAT` absent and verify runtime tests cover the no-op path.
3. Compile once with `ENABLE_CHEAT` present and verify commands route through VitalRouter.
4. Run a BuildScript player build with `Cheat Build Mode = Disabled` and confirm original scripting defines are restored.
5. Run a CI dry-run with `-enableCheat` and with `-disableCheat`; passing both should fail before the player build starts.
