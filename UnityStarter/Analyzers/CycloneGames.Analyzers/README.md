# CycloneGames Roslyn Analyzers

CycloneGames.Analyzers is a Roslyn analyzer package for UnityStarter. It enforces Unity runtime performance, safety, async, and framework convention rules at compile time.

## Build

```bash
cd UnityStarter/Analyzers
dotnet build CycloneGames.Analyzers.sln -c Release
```

The output assembly is:

```text
CycloneGames.Analyzers/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
```

## Unity Project Activation

Add the analyzer to Unity-generated C# projects through a team-owned `Directory.Build.props`, a NuGet analyzer package, or a Unity project-generation hook. Avoid relying on untracked per-user setup for CI or production teams.

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

Rules that depend on hot path detection should stay conservative. Prefer false negatives over broad false positives that make teams suppress the analyzer.

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
- Runtime, Editor, Samples, and Tests path behavior defined.
- Positive and negative analyzer test cases.
- A low false-positive profile for existing UnityStarter modules.
- A CodeFix only when the rewrite is safe across common Unity code patterns.
