# CycloneGames.Logging

`CycloneGames.Logging` is the engine-independent producer contract used by CycloneGames packages. It standardizes severity, category naming, deferred message construction, exception reporting, null defaults, explicit injection, and process-level backend replacement without depending on Unity or a concrete sink.

## Responsibilities

- `ILogWriter` is the producer-only backend boundary.
- `LogChannel` binds a stable category to either an explicit writer or the process fallback.
- `LogRuntime` atomically installs or replaces the process fallback but never owns or disposes it.
- `NullLogWriter` keeps independently installed packages safe and silent when no backend is present.

Sink registration, queues, threads, file output, Unity delivery, flushing, and shutdown belong to a backend such as `CycloneGames.Logger`.

## Assembly and package model

```mermaid
flowchart LR
    subgraph Assembly["Each log-producing asmdef"]
        Consumer["Implementation"] --> Facade["Diagnostics/<FeatureName>Log"]
    end
    Facade --> Contract["CycloneGames.Logging"]
    Host["Application composition root"] --> Contract
    Host -. "optional" .-> Backend["CycloneGames.Logger"]
    Backend --> Contract
```

Every assembly that produces records has a direct asmdef reference to `CycloneGames.Logging`, and its package declares `com.cyclone-games.logging`. Business assemblies never reference `CycloneGames.Logger`. The backend is selected only by the host, so removing it does not change producer source or public contracts.

This design deliberately does not use PlayerSettings scripting symbols or distributed source-level `#if` blocks. Local packages under `Assets/` cannot use their `package.json` as an automatic presence condition, while a mandatory, Unity-free contract is small and deterministic in both UPM and asset-style layouts. Optional third-party backend adapters should remain in separate integration assemblies.

## Uniform contract

| Type | Contract |
| --- | --- |
| `LogSeverity` | Ordered `Trace` through `Fatal`; `None` is a filtering sentinel and is not emitted |
| `ILogWriter` | Admission check, string/deferred/generic-state writes, and structured exception writes |
| `LogChannel` | Stable category plus either an explicit writer or the current process fallback |
| `LogRuntime` | Atomic fallback installation/replacement; no ownership, flush, or disposal |
| `NullLogWriter` | Allocation-free disabled default when a backend is absent |

Categories use `CycloneGames.<Package>[.<Area>]`, for example `CycloneGames.Audio.Editor`. Keep them stable because filters and dashboards may treat them as identifiers. Message text does not repeat the category. Use `WriteException`/`Error(exception, message)` rather than flattening an exception to `Message`, so a backend can preserve type, stack, and inner-exception evidence.

## Assembly-local log facade

Every non-test package assembly that emits records owns one internal facade at `Diagnostics/<FeatureName>Log.cs`, including Samples and Benchmarks. Copyable examples follow the same contract as production code so importing a sample cannot reintroduce a competing logging style. The type name must be unique across the package and end with `Log`; using the same generic `ModuleLog` name in several assemblies can become ambiguous when tests or integrations receive internals access.

Each facade provides the same minimum surface:

```csharp
internal static class AudioRuntimeLog
{
    internal const string Category = "CycloneGames.Audio";
    internal static readonly LogChannel Channel = LogChannel.Create(Category);

    internal static LogChannel Create(ILogWriter logWriter)
    {
        return LogChannel.Create(
            Category,
            logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
    }
}
```

- `Category` owns the stable default category for that assembly.
- `Channel` is the ambient channel for static and Unity-owned entry points.
- `Create(ILogWriter logWriter)` creates an explicitly bound channel for constructed services.
- An assembly with multiple established categories may add clearly named members such as `EditorChannel` or `CreateForCategory`, but keeps the minimum surface above.

Only facade files call `LogChannel.Create`. Implementation files use the facade, which makes category changes, writer injection, and policy audits discoverable without introducing a package-specific logger interface. `CG0050` in `CycloneGames.Analyzers` checks the construction boundary and file convention.

## Usage

Use explicit injection for plain C# services:

```csharp
private readonly LogChannel _log;

public NetworkSession(ILogWriter logWriter)
{
    _log = NetworkingRuntimeLog.Create(logWriter);
}
```

Static or Unity-owned entry points may use the process fallback:

```csharp
private static readonly LogChannel Log = AudioRuntimeLog.Channel;

Log.Warning("Voice budget was exhausted.");
```

Use `Log` for an ambient static field, `_log` for an injected instance field, and `logWriter` for the constructor or factory parameter. Calls use the same six severity names in every package: `Trace`, `Debug`, `Info`, `Warning`, `Error`, and `Fatal`. Every severity also accepts `(Exception exception, string message = null)`. Messages should not repeat the category as a text prefix. Use deferred or generic-state overloads on hot paths so filtered messages do not allocate.

Explicit injection does not treat `null` as an implicit policy choice. A caller that deliberately wants silence passes `NullLogWriter.Instance`:

```csharp
public CacheService(ILogWriter logWriter)
{
    _log = AssetManagementLog.Create(logWriter);
}

var cache = new CacheService(NullLogWriter.Instance);
```

Do not cache `LogRuntime.Writer` in an ambient channel: `LogChannel.Create(category)` intentionally resolves it on every call so a controlled backend replacement is observed. Explicit channels remain bound to the injected writer.

## Lifecycle and ownership

The composition root creates the concrete backend, installs it with `LogRuntime.TryInstallWriter` or `LogRuntime.ReplaceWriter`, then drains and shuts down the previous backend it owns. `LogRuntime` deliberately performs no disposal. CycloneGames packages must not initialize, flush, restart, or shut down the process backend.

`ILogWriter` implementations must document thread affinity and be safe for every thread from which their consumers can write. `LogRuntime` replacement is atomic, but it is not a handoff protocol: the composition root must stop new producers, replace/reset the writer, drain the backend it owns, and only then dispose it. Never dispose the value returned by `ReplaceWriter` unless ownership is independently known.

Invalid categories fail when a channel is created. A missing backend is not an error and stays silent. Writer-specific queue overflow, sink failure, persistence, and shutdown behavior are intentionally outside this contract and must be reported by that backend.

## Persistence

This package writes no files or preferences and owns no serialized assets. File output and its retention policy are backend concerns. The package has no cache and requires no cleanup.

The runtime uses no reflection, dynamic code generation, Unity object, or implicit lifecycle discovery. Its static fallback contains only an interface reference updated with `Interlocked`/`Volatile`; IL2CPP, stripping, and platform validation still belong to the consuming Player build.

## UPM composition

Packages that produce logs depend on `com.cyclone-games.logging`. They do not depend on `com.cyclone-games.logger` unless they are a host or concrete backend integration. Removing the backend therefore leaves packages compilable and routes ambient channels to `NullLogWriter`.

Package-specific logger interfaces and compatibility adapters are not part of the logging architecture. Every package uses `ILogWriter`/`LogChannel` through its assembly-local facade. Breaking migrations remove old logging entry points instead of retaining parallel APIs.

## Validation

1. Compile `CycloneGames.Logging` without Unity engine references.
2. Run `CycloneGames.Logging.Tests.Editor`.
3. Verify a channel follows `LogRuntime.ReplaceWriter` while an explicitly bound channel remains isolated.
4. Verify a package compiles with only `com.cyclone-games.logging` installed.
5. Run `CycloneGames.Analyzers` tests and verify `CG0050` rejects `LogChannel.Create` outside `Diagnostics/<FeatureName>Log.cs`.
