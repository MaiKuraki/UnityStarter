# CycloneGames Roslyn Analyzers

CycloneGames.Analyzers is a Roslyn analyzer package for UnityStarter. It enforces Unity runtime performance, safety, async, and framework convention rules at compile time.

## Build

```bash
cd UnityStarter/Analyzers
dotnet build CycloneGames.Analyzers.sln -c Release
dotnet test CycloneGames.Analyzers.sln -c Release
```

The output assembly is:

```text
CycloneGames.Analyzers/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
```

## Unity Project Activation

Building the analyzer source does not activate it in the Unity Editor. Unity activation requires a compiled analyzer DLL inside the relevant `Assets/` or package scope with the case-sensitive `RoslynAnalyzer` asset label. See the [Unity 2022.3 Roslyn analyzer manual](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html).

This repository currently keeps only the analyzer source project and does not publish or activate that Unity asset. Do not treat a successful analyzer build as proof that Unity compilation is enforcing the rules.

For IDE and command-line validation of Unity-generated C# projects, use a team-owned `Directory.Build.props`, an analyzer package, or a Unity project-generation hook. Avoid relying on untracked per-user setup for CI or production teams.

Example `Directory.Build.props` at `UnityStarter/`:

```xml
<Project>
    <ItemGroup>
        <ProjectReference Include="$(MSBuildThisFileDirectory)Analyzers\CycloneGames.Analyzers\CycloneGames.Analyzers.csproj"
                          ReferenceOutputAssembly="false"
                          OutputItemType="Analyzer" />
    </ItemGroup>
</Project>
```

## Implemented Rules

| ID | Rule | Severity |
| -- | ---- | -------- |
| CG0001 | `foreach` in hot path | Warning |
| CG0002 | LINQ in hot path | Warning |
| CG0003 | string construction in hot path | Warning |
| CG0004 | `Camera.main` in hot path | Warning |
| CG0010 | `GameObject.Find` in production code | Error |
| CG0011 | scene-wide `FindObjectOfType` APIs | Error |
| CG0012 | `SendMessage` / `BroadcastMessage` | Error |
| CG0013 | `MonoBehaviour.Invoke` APIs | Error |
| CG0014 | `Resources.Load` | Warning |
| CG0030 | public instance field on `MonoBehaviour` | Warning |
| CG0031 | `using static` in Runtime code | Warning |
| CG0032 | `#region` in Runtime code | Info, disabled by default |
| CG0033 | `[Obsolete]` in CycloneGames framework code | Warning |
| CG0040 | `async void` in Runtime code | Error |
| CG0041 | chained `component.transform` access in hot path | Warning |
| CG0042 | `UnityEditor` usage outside Editor folders | Warning |
| CG0043 | `Debug.Log` in hot path | Warning |
| CG0044 | `GetComponent<T>` in hot path | Warning |
| CG0045 | boxing conversion in hot path | Warning |
| CG0046 | lambda or anonymous method in hot path | Warning |
| CG0047 | `async Task` when UniTask is referenced | Warning |
| CG0048 | static class circular dependency risk | Warning |
| CG0049 | direct logging API bypass in governed CycloneGames assemblies | Error |
| CG0050 | `LogChannel.Create` outside the assembly log facade | Error |

## Code Fixes

| Diagnostic | Fix |
| ---------- | --- |
| CG0001 | Converts `foreach` to `for` only when the collection has `Count` or `Length` plus an `int` indexer. |
| CG0004 | Adds a cached `Camera` field and an `Awake` assignment, then replaces `Camera.main`. |

## Hot Path Detection

The default hot path method names are:

```text
Update, LateUpdate, FixedUpdate, OnGUI,
Tick, OnTick, OnUpdate, PreUpdate, PostUpdate,
OnPreTick, OnPostTick
```

Hot-path rules stay conservative: prefer false negatives over broad false positives that push teams to suppress the analyzer.

## Unified Logging Enforcement

`CG0049` prevents governed CycloneGames package assemblies from bypassing the shared logging contract. This includes Runtime, Editor, Samples, and Benchmarks because copyable example code is part of the package's API guidance. The rule uses resolved Roslyn symbols rather than short type names and reports:

- `UnityEngine.Debug.Log*`, `UnityEngine.Debug.Assert*`, and `Debug.unityLogger` access.
- `UnityEngine.MonoBehaviour.print`.
- `System.Console.Write*` and access to `Console.Out` or `Console.Error`.
- References to the concrete `CycloneGames.Logging.LogPipeline` backend outside logging backend assemblies.

The rule applies only to assemblies whose names start with `CycloneGames.`. The backend assemblies `CycloneGames.Logging.Pipeline`, `CycloneGames.Logging.Unity`, and `CycloneGames.Logging.Unity.Editor` are excluded, as are assemblies or source paths explicitly identified as Tests, Tools, or CodeGen — those sit at verification or host I/O boundaries. Runtime, Editor, Samples, and Benchmarks stay governed. `CycloneGames.Logging.Unity.Samples` may reference `LogPipeline`, but its direct Unity and Console output calls remain governed. Similar names such as `CycloneGames.MemoryGovernance.Logging.Pipeline` or `CycloneGames.MemoryGovernance.Logging.Pipeline.Editor` are not standard backend assemblies and remain in scope.

In governed package code, `CG0049` owns direct logging diagnostics, so the hot-path-only `CG0043` rule does not report the same `Debug.Log*` invocation twice. `CG0043` remains active outside this scope.

`CG0050` keeps channel construction in one discoverable boundary per producing assembly: a top-level `internal static` type whose unique name ends with `Log`, stored at `Diagnostics/<TypeName>.cs`, exposing the standard internal members `Category`, `Channel`, and `Create(ILogWriter logWriter)`. For example, `CycloneGames.Audio.Runtime` owns `Diagnostics/AudioRuntimeLog.cs`; implementation files consume `AudioRuntimeLog.Channel` or `AudioRuntimeLog.Create(logWriter)` instead of calling `LogChannel.Create` directly. Unique facade names avoid type ambiguity when assemblies expose internals to tests or integration assemblies.

Neither rule ships a CodeFix. A safe rewrite depends on module category, exception/context handling, deferred formatting, and logging ownership decisions that a single invocation cannot reveal. `CG0050` verifies the construction boundary, file convention, and standard facade member signatures; package tests and API review own category values and explicit null semantics.

## Suppression

Use `.editorconfig` for project-level severity:

```ini
[*.cs]
dotnet_diagnostic.CG0014.severity = none
dotnet_diagnostic.CG0001.severity = error
```

Use local suppression only when the allocation or API cost is intentional and documented:

```csharp
#pragma warning disable CG0014
var config = Resources.Load<GameConfig>("Config");
#pragma warning restore CG0014
```

## Quality Bar

Before a rule is enabled by default, it should have:

- A semantic check where syntax alone would be fragile.
- Runtime, Editor, Samples, and Benchmarks governed, including the narrow pipeline-composition exception for `CycloneGames.Logging.Unity.Samples` while raw output remains prohibited.
- Positive and negative analyzer test cases.
- A low false-positive profile for existing UnityStarter modules.
- A CodeFix only when the rewrite is safe across common Unity code patterns.
