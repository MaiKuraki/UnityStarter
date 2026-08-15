# CycloneGames Roslyn Analyzers

CycloneGames.Analyzers is a Roslyn analyzer package for UnityStarter. It enforces Unity runtime performance, safety, async, and framework convention rules at compile time.

## Build

```bash
cd <unity-project>/Analyzers
dotnet build CycloneGames.Analyzers.sln -c Release
dotnet test CycloneGames.Analyzers.sln -c Release
```

`<unity-project>` denotes the Unity project directory (the folder that contains `ProjectSettings/ProjectVersion.txt`). The analyzer system is name-agnostic: the source-ownership gate, the test path helpers, and the activation verifier all locate the Unity project through its `ProjectSettings/ProjectVersion.txt` marker instead of a folder name, so the project folder can be renamed or relocated without changing the analyzer project. `CycloneGames.Analyzers.Unity.csproj` defaults `UnityProjectRoot` to two levels above the project file and accepts an explicit `-p:UnityProjectRoot=<path-to-unity-project>` override when the analyzer project itself is relocated. When CycloneGames packages are consumed as UPM packages (`Packages/...`), their source intentionally falls outside repository ownership: host projects never police package code, and package-side governance belongs to the package repository's own analyzer build. **Recorded decision (this iteration):** the analyzer and its `Default.ruleset` are distributed with the project template only, not as a UPM package — UPM consumers run without host-side analyzer governance, which is an accepted boundary, not an omission.

The solution produces two analyzer artifacts from the same rule sources:

```text
CycloneGames.Analyzers/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
CycloneGames.Analyzers.Unity/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
```

The first project remains the development/package build. The Unity-specific project targets the Roslyn dependency level supported by this Unity version, excludes CodeFix/Workspaces dependencies from the Editor artifact, and installs its Release output at `<unity-project>/Assets/Analyzers/CycloneGames.Analyzers.dll`.

## Unity Project Activation

Unity activation requires a compiled analyzer DLL inside the relevant `Assets/` or package scope with the case-sensitive `RoslynAnalyzer` asset label. See the [Unity 2022.3 Roslyn analyzer manual](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html).

This repository activates the analyzer through the committed DLL and `.meta` file in `<unity-project>/Assets/Analyzers/`. The plugin importer remains disabled as a normal managed plugin; the `RoslynAnalyzer` label is the activation contract. A Release build of `CycloneGames.Analyzers.Unity.csproj` refreshes the committed DLL.

Run the real compiler fixture after changing analyzer dependencies, deployment metadata, or diagnostic activation:

```bash
# Any platform: the verifier is a .NET console tool, so no PowerShell is required.
dotnet run --project <unity-project>/Analyzers/CycloneGames.Analyzers.Verifier/CycloneGames.Analyzers.Verifier.csproj -- \
  --unity-editor-path '<path-to-Unity>/Editor/Unity.exe'

# Windows editor path: '<path-to-Unity>/Editor/Unity.exe'
# macOS editor path:   '/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity'
# Linux editor path:   '~/Unity/Hub/Editor/<version>/Editor/Unity'
```

The verifier builds the Unity-compatible analyzer first; pass `--skip-build` to reuse an existing Release build. `--unity-project-root` overrides the marker-based project discovery, and `--help` lists every option.

The verifier creates an isolated temporary Unity project, installs the committed analyzer asset, compiles `Integration/ForbiddenUnityApiViolation.cs.txt`, and succeeds only when Unity loads the analyzer without `CS8032`/load errors and emits `CG0010`. Because the fixture deliberately triggers the Error-severity `CG0010` diagnostic, a passing verification normally ends with Unity aborting batchmode on compiler errors and a non-zero exit code — that failure is the expected signal that the analyzer fired, not a verification failure. `--timeout-seconds` is one end-to-end deadline for the analyzer build, Unity compilation, and bounded cleanup; `--keep-temporary-project` retains the temporary project for diagnosis. Every external process runs with a hard deadline and is killed together with its process tree on timeout; when tree termination cannot be confirmed, the verifier fails closed and retains the temporary project (as it also does on any failure, printing its path). On Windows it additionally stops, best-effort, only the VBCSCompiler servers the editor spawned during the run, leaving pre-existing Editor servers untouched.

Analyzer callbacks accept only repository-owned source. For an absolute source path, the final `Assets/` segment defines a candidate Unity project root; for a canonical relative `Assets/...` path, the current host directory must resolve to that root. The candidate is trusted only when `ProjectSettings/ProjectVersion.txt` is a bounded, regular, non-reparse file with a Unity version marker. The verified root is cached in a bounded process-local cache before the `Assets/ThirdParty/` allowlist is applied. Path-segment comparisons follow the host filesystem: case-insensitive on Windows and case-sensitive on Linux/macOS. This keeps `Assets/Build/`, `Assets/<project-folder>/`, `Assets/ThirdParty/CycloneGames/`, and the optional `Assets/ThirdParty/CycloneGames.MemoryGovernance/` package family governed while rejecting Unity `Library/PackageCache/`, UPM `Packages/`, nested package `Assets/`, non-CycloneGames third-party content, generated paths, and all other unknown non-empty paths. A real Unity checkout below an ancestor named `Packages/<reverse-DNS-name>` remains governed because its own candidate root has the marker. Only an empty path remains in scope without a marker; this is the explicit contract for focused Roslyn hosts and tests that do not provide physical paths.

`<unity-project>/Assets/Default.ruleset` is the committed Unity enforcement policy. `CG0010` remains an error. `CG0011` and `CG0013` remain visible as warnings while existing scene-discovery and timer call sites are migrated; this prevents analyzer activation from converting known migration debt into an unrelated compile outage. The two name-based target lookups in the GameplayAbilities sample are explicit, locally documented `CG0010` exceptions for that sample scene only. Their production replacement boundary is a project-owned targeting service.

The activation flow persists only the committed DLL and `.meta` asset. Each verifier project is claimed with an exclusive random owner marker, and cleanup revalidates that marker before recursive removal. `--keep-temporary-project` retains it for diagnosis; an unconfirmed process-tree shutdown also retains it automatically. Build intermediates below `bin/` and `obj/`, owned verifier projects below the operating-system temporary directory, and Unity import caches are reproducible and safe to delete when no Unity process is using them.

## Implemented Rules

| ID | Rule | Descriptor default severity |
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

`DiagnosticIds` declares 28 ID constants; the table above lists the 24 implemented rules. `CG0015` (`NativeContainerLeak`), `CG0022` (`ActorStartBaseCall`), `CG0023` (`PoolOnDespawnOverride`), and `CG0024` (`GameplayTagImplicitCast`) are reserved for rules that have not been implemented yet and intentionally have no analyzer.

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
- References to the concrete `CycloneGames.Logging.Pipeline.LogPipeline` backend outside logging backend assemblies.

The rule applies only to assemblies whose names start with `CycloneGames.`. The backend assemblies `CycloneGames.Logging.Pipeline`, `CycloneGames.Logging.Unity`, and `CycloneGames.Logging.Unity.Editor` are excluded, as are assemblies or source paths explicitly identified as Tests, Tools, or CodeGen — those sit at verification or host I/O boundaries. Runtime, Editor, Samples, and Benchmarks stay governed. `CycloneGames.Logging.Unity.Samples` may reference `LogPipeline`, but its direct Unity and Console output calls remain governed. Similar names such as `CycloneGames.MemoryGovernance.Logging.Pipeline` or `CycloneGames.MemoryGovernance.Logging.Pipeline.Editor` are not standard backend assemblies and remain in scope.

In governed package code, `CG0049` owns direct logging diagnostics, so the hot-path-only `CG0043` rule does not report the same `Debug.Log*` invocation twice. `CG0043` remains active outside this scope.

`CG0050` keeps channel construction in one discoverable boundary per producing assembly: a top-level `internal static` type whose unique name ends with `Log`, stored at `Diagnostics/<TypeName>.cs`, exposing the standard internal members `Category`, `Channel`, and `Create(ILogWriter logWriter)`. For example, `CycloneGames.Audio.Runtime` owns `Diagnostics/AudioRuntimeLog.cs`; implementation files consume `AudioRuntimeLog.Channel` or `AudioRuntimeLog.Create(logWriter)` instead of calling `LogChannel.Create` directly. Unique facade names avoid type ambiguity when assemblies expose internals to tests or integration assemblies.

Neither rule ships a CodeFix. A safe rewrite depends on module category, exception/context handling, deferred formatting, and logging ownership decisions that a single invocation cannot reveal. `CG0050` verifies the construction boundary, file convention, and standard facade member signatures; package tests and API review own category values and explicit null semantics.

## Suppression

Use the committed Unity rule set for Unity compiler severity:

```xml
<Rule Id="CG0011" Action="Warning" />
```

Use `.editorconfig` for compatible IDE or standalone .NET compiler hosts:

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
