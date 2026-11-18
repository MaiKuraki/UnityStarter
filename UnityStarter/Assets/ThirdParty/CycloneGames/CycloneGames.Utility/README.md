# CycloneGames.Utility

[English | 简体中文](README.SCH.md)

A set of small Unity helpers: invariant formatting, collection operations, color and vector math, safe-area fitting, authored Transform lookup, FPS diagnostics, singletons, and a few Editor drawers. Each helper is independent. Add the asmdef reference and use the one you need; there is no framework to initialize.

## Requirements

| Package | Used for |
| --- | --- |
| `com.cyclone-games.hash` | Transform key lookup |
| `com.cyclone-games.logging` | Diagnostics; `NullLogWriter` is the default when no backend is installed |
| `com.unity.ugui` | `EmptyGraphic`, safe-area and FPS components |

Versions are declared in `package.json`.

| Assembly | Platform | `autoReferenced` | Direct references |
| --- | --- | ---: | --- |
| `CycloneGames.Utility.Runtime` | All Unity targets | `true` | `CycloneGames.Hash.Core`, `CycloneGames.Logging.Core`, `UnityEngine.UI` |
| `CycloneGames.Utility.SplashScreenModifier.Runtime` | All Unity targets | `true` | None |
| `CycloneGames.Utility.Editor` | Editor only | `true` | `CycloneGames.Utility.Runtime`, `CycloneGames.Logging.Core` |

```csharp
using CycloneGames.Utility.Runtime;
```

Diagnostics use `LogChannel` categories `CycloneGames.Utility` and `CycloneGames.Utility.Editor`. Each diagnostic-producing asmdef owns an internal `<FeatureName>Log` facade under `Diagnostics/`.

## Quick start

Three helpers that need no scene setup:

```csharp
// Invariant byte formatting into a caller-owned buffer, no intermediate string.
Span<char> buffer = stackalloc char[64];
if (FormatUtil.TryFormatBytes(1_572_864, buffer, out int written, decimalPlaces: 1))
{
    ConsumeText(buffer.Slice(0, written)); // "1.5 MB"
}

// O(1) removal where order does not matter.
if (activeProjectiles.TrySwapRemoveAt(index))
{
    // index now holds the former last element.
}

// Hex parsing and WCAG contrast.
if (Colors.TryParseHex("#4A90E2FF".AsSpan(), out Color color))
{
    float contrast = color.GetContrastRatio(Color.black);
}
```

## Runtime helpers

### Invariant formatting

`FormatUtil` and `MemoryFormatExtensions` use `CultureInfo.InvariantCulture`. String APIs (`FormatBytes`, `FormatNumber`, `FormatDuration`, `FormatPercent`) allocate the returned string. `Try*` overloads write into a caller-owned `Span<char>` and return `false` for invalid input or insufficient capacity. `AppendFormatted*` extensions avoid an intermediate string, though `StringBuilder` growth can still allocate.

| Rule | Value |
| --- | --- |
| Decimal places | `[0, 5]` |
| Byte counts | Non-negative, base-1024 units `B`, `KB`, `MB`, `GB`, `TB` |
| Number values | Base-1000 suffixes `K`, `M`, `B`, `T` |
| Percent ratios | Finite and in `[0, 1]` |
| Durations | Finite, fit in an `Int64` number of whole seconds |
| Throwing string APIs | `ArgumentOutOfRangeException` for invalid contracts |

A HUD that updates every frame should write into a reusable `Span<char>` and call `ToString()` only when the displayed value changes. `FormatPercent` allocates one string per call:

```csharp
float ratio = totalBytes > 0 ? bytesDownloaded / (float)totalBytes : 0f;
string percent = FormatUtil.FormatPercent(ratio, decimalPlaces: 0); // "73%"
```

### Collection helpers

`CollectionUtils` provides bounds-checked access (`TryGetElementAtIndex`, `TryFirst`, `TryLast`, `TryPop`), `IsNullOrEmpty` checks for `List<T>`, arrays, `Dictionary` and `HashSet`, dictionary `GetOrDefault`, and `LinkedList` node helpers.

Swap-remove is O(1) and does not preserve order. The array overload requires a caller-owned logical count and clears the released slot so references are not retained:

```csharp
public void DespawnAt(int index)
{
    if (!active.TrySwapRemoveAt(index))
    {
        return; // index was already invalidated this frame
    }

    // index now holds the former last element; update any cached indices.
}
```

`Shuffle` is an in-place Fisher-Yates. The default RNG is lazily allocated per thread; pass a seeded `System.Random` for repeatable output. `System.Random` is not a network protocol or cross-runtime deterministic guarantee, so replay and lockstep need the product's versioned simulation RNG. Mutable operations are not thread-safe.

### Colors

`Colors` holds named colors, hex parsing/formatting, stable `0xRRGGBBAA`, BT.601 luminance and WCAG relative luminance/contrast. `GetLuminance` uses BT.601 weighted luma; accessibility decisions should use `GetRelativeLuminance` and `GetContrastRatio`. Contrast ignores alpha, so composite translucent colors before testing. `Colors.RandomColor(System.Random)` uses caller-owned RNG state; the parameterless overload consumes Unity's global random state and belongs on main-thread, non-deterministic presentation paths only.

### Vector helpers

`Vector3Extensions` provides finite-input helpers with explicit behavior for invalid ranges. These are float helpers, not a deterministic simulation boundary.

### Safe area

`SafeAreaUtility.CalculatePixelRect` is pure pixel/anchor math and can be tested without a scene:

```csharp
var policy = new SafeAreaPolicy(
    extendIntoBottomSafeArea: false,
    enforceVerticalSymmetry: true,
    enforceHorizontalSymmetry: true,
    paddingPixels: new Vector4(x: 8, y: 8, z: 8, w: 8));

Rect usablePixels = SafeAreaUtility.CalculatePixelRect(
    Screen.safeArea,
    Screen.width,
    Screen.height,
    in policy);
```

`Vector4` padding order is left, bottom, right, top. Non-finite or negative padding becomes zero; over-constrained padding is proportionally fitted so anchors cannot invert. Bottom extension is applied first, then top-inset balancing runs and can add bottom inset back to balance a top cutout.

Attach `AdaptiveSafeAreaFitter` to a `RectTransform` for the driven component. It owns a `DrivenRectTransformTracker`, caches the last screen state, and recalculates only when screen state, configuration, hierarchy dimensions or driven values change. On a phone in portrait, `enforceVerticalSymmetry: true` adds a matching bottom inset for a top notch so layout stays centered. With the device rotated, `enforceHorizontalSymmetry: true` mirrors a side notch.

### Transform key registry

`TransformKeyRegistry` maps authored string keys to Transforms. Build the index during initialization, then look up:

```csharp
registry.BuildIndex(); // cold path, during initialization

if (registry.TryGetTransform("Weapon.Muzzle", out Transform muzzle))
{
    SpawnAt(muzzle);
}
```

Behavior:

- Empty keys and missing Transform references are ignored. The first valid authored duplicate key wins; later duplicates are counted and ignored.
- Up to 16 indexed entries use linear lookup; larger sets use a hash-sorted array and binary search. String lookup verifies the ordinal key after hashing. A hash-only lookup returns `false` when a distinct-key collision is detected.
- Active and enabled nested registries are flattened with an iterative depth-first traversal during build; disabled or inactive registries do not participate.
- The first lookup can build the index if `Build On Awake` was disabled, which allocates and scans the hierarchy. The optional `Transform.Find` fallback is also a cold path.
- Keys are authored strings, not network IDs, save IDs, Unity instance IDs or ECS entity identities.

The `Collect Direct Children` Editor action adds missing direct-child entries through `SerializedObject`, retaining Unity's Undo and Prefab Override behavior.

### FPS diagnostics

Attach `FPSCounter` to a diagnostic GameObject owned by the product and call `SetVisible` or assign `IsVisible` through a direct reference. It does not discover or create a global instance. The moving average uses a fixed bounded sample ring. Instant-only and average-only modes lazily cache observed numeric strings; combined mode can allocate a composed string on display updates. IMGUI has engine-owned costs, so this is a diagnostic tool, not a production HUD. `Persist Across Scenes` applies `DontDestroyOnLoad` to the entire GameObject, so use a dedicated owner with no unrelated components.

### Singleton and MonoSingleton

`Singleton<T>` is a CLR lazy construction helper. Construction and publication are thread-safe; mutable state inside `T` is not. The instance lives for the AppDomain and has no reset, cancellation, `Dispose` or shutdown ordering. Use it for small parameterless objects that do not own Unity objects, subscriptions, threads, native handles or session state.

```csharp
public sealed class TelemetryNames : Singleton<TelemetryNames>
{
    public string BuildFlavor { get; } = "production";
}

string flavor = TelemetryNames.Instance.BuildFlavor;
```

`MonoSingleton<T>` is a separate main-thread-only Unity convenience. `Instance` resolves an existing active or inactive scene component, or creates a dedicated GameObject during Play Mode. `TryGetInstance` returns only the already registered cache; it neither searches nor creates. Multiple pre-existing candidates fail instead of selecting arbitrary authority. A duplicate component is disabled and destroyed without deleting its GameObject or sibling components. Static registration is cleared at `SubsystemRegistration`, including when Domain Reload is disabled. Derived lifecycle overrides must call the base implementation.

A global authored `MonoSingleton<T>` must sit on a root GameObject; otherwise the implementation logs an error and retains Scene lifetime instead of moving an unrelated hierarchy. Use explicit construction for services with dependencies, owned resources, ordered shutdown or scene/world-scoped multi-instance systems.

## Editor helpers

### String constant selector

Use a constants class to offer known values while keeping the serialized value a plain string:

```csharp
public static class ControlPaths
{
    public const string Keyboard = "<Keyboard>";
    public const string Gamepad = "<Gamepad>";
}

[StringAsConstSelector(typeof(ControlPaths), UseMenu = true, AllowCustom = true)]
[SerializeField] private string controlPath;
```

The drawer reflects public `const string` fields once per constants type and domain reload, sorts names ordinally, ignores null/empty constants, and keeps the first field for duplicate values. `UseMenu` builds hierarchical menu paths from the field-name separator (`Separator`, default `_`). `AllowCustom` preserves values from open external contracts. Multi-object edits use `SerializedObject`/`SerializedProperty` and retain Undo and Prefab Override behavior.

### Property groups

The Runtime attributes describe a top-level serialized-field segment:

```csharp
[PropertyGroup("Timing", groupAllFieldsUntilNextGroupAttribute: true, groupColorIndex: 24)]
[SerializeField] private float delay;
[SerializeField] private float duration;

[EndPropertyGroup]
[SerializeField] private bool playOnEnable;
```

Opt the target in with a target-specific Editor adapter:

```csharp
[CustomEditor(typeof(MyComponent), true)]
[CanEditMultipleObjects]
internal sealed class MyComponentEditor : PropertyGroupInspectorDrawer
{
}
```

The renderer follows Unity's top-level `SerializedProperty` order. A continuous group ends before the field marked with `EndPropertyGroup`, at the next `PropertyGroup`, or at the end of the object. A non-continuous group contains only its attributed field. Repeated names form independent segments. Arrays, lists, nested serializable objects and managed references can be grouped as whole top-level properties; attributes inside nested serialized types are not recursively interpreted. Invalid or ambiguous metadata fails open by drawing the property normally.

There is no global fallback Editor, so unrelated Unity and third-party Inspectors are never intercepted. Foldout state belongs to the current Editor instance, starts from `ClosedByDefault`, and is not persisted. All edits flow through `SerializedObject`/`SerializedProperty`.

### Custom inspectors and Colors Preview

`FPSCounter`, `AdaptiveSafeAreaFitter` and `TransformKeyRegistry` have explicit custom inspectors with cached styles, grouped sections, multi-object support and serialized editing. The generic paired-field renderer is exposed only through `PropertyGroupInspectorDrawer`; its traversal, metadata cache and visual helpers stay internal.

Colors Preview derives color names from Runtime public color fields during an Editor-only reflection pass, so Player builds do not carry a duplicated table. `Tools/CycloneGames/Open Persistent Data Path` uses `EditorUtility.RevealInFinder`; it creates the directory if needed and reports failures with an Editor dialog and log.

## Splash screen submodule

`Runtime/Scripts/SplashScreenModifier/` is a physically isolated assembly enabled by presence, with no call site. Adding this package registers `SkipUnitySplashScreen` at `BeforeSplashScreen`; Player builds request `SplashScreen.Stop(StopImmediate)`. Editor and Dedicated Server builds are no-ops. The assembly uses `AlwaysLinkAssembly` so Unity's linker processes its runtime-init root. `SplashScreen.Stop` requires a license that permits it.

## Performance notes

| Path | Allocation behavior |
| --- | --- |
| `TryFormat*` destination APIs | Caller owns the span; no result string. |
| String formatting APIs | One managed string for the return value. |
| Collection helpers | No hidden locks. `ClearAndResize` can allocate on capacity growth; default shuffle lazily creates one RNG per thread. |
| Safe-area math | Struct-only calculation; no global cache. |
| Transform registry build | Component owns arrays, a reusable key set and iterative traversal buffers; capacity or hierarchy changes can allocate. Peak capacity is retained for the component lifetime and references are cleared after build. |
| Transform registry lookup | No rebuild allocation after a valid explicit build; O(n) for `n <= 16`, otherwise O(log n + collisions). |
| FPS counter | Component owns bounded sample arrays and GUI caches; combined text and IMGUI can allocate. |
| `Singleton<T>.Instance` | CLR initializes once per closed type; warm access is a static read. |
| `MonoSingleton<T>.Instance` | Warm registered access has no result allocation. First resolution can allocate a search result array, cached name, GameObject and component. |
| Editor drawers | Reflection/menu data is cached until domain reload; menu opening is a cold allocation path. |
| Property groups | Type/field metadata is reflected once per Editor domain; each Inspector owns only fold state. |

Profile the target backend and hardware before choosing a helper for a hot path.

## Threading and platform notes

- Unity objects, `Screen`, IMGUI, UGUI, `Transform` and Editor APIs are main-thread only here.
- `Singleton<T>` construction/publication follows CLR static initialization semantics, including WebGL's single-threaded runtime. The mutable object is not made thread-safe.
- `MonoSingleton<T>` is main-thread confined and creates only during Play Mode. Its guard prevents worker-thread Unity API access.
- Collection helpers do not claim thread safety. If mutable data crosses threads, the owner defines one synchronization policy around all access, capacity, cancellation and shutdown.
- The Runtime assemblies contain no background tasks, native plugins, unsafe blocks, runtime reflection discovery or dynamic code generation.
- The Splash submodule calls Unity on its startup callback thread. WebGL has no thread-dependent fallback path.
- Dedicated Server builds should not attach UGUI, safe-area or IMGUI diagnostic components. The Splash callback is a compile-time no-op under `UNITY_SERVER`.

This package does not create or write preferences, settings, cache indexes, save data, registry keys, plist values or standalone project configuration assets. It does not use `EditorPrefs`, `PlayerPrefs` or `SessionState`. Inspector edits write Unity-serialized authoring changes through `SerializedObject`. `TransformKeyRegistry`, `FPSCounter` and `AdaptiveSafeAreaFitter` contain Unity-serialized fields; renamed fields use `FormerlySerializedAs` where declared.

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| `TryFormat*` returns `false` | Invalid input or destination span too small | Validate decimal places in `[0, 5]`, non-negative byte count, ratio in `[0, 1]`; grow the span. |
| `Singleton<T>` holds stale state across sessions | AppDomain has not been reloaded | Use explicit construction for data with session lifetime. |
| `MonoSingleton<T>` logs an error about root GameObject | Authored global component is on a non-root GameObject | Move it to a root GameObject, or set `IsGlobal` to `false` for scene-scoped use. |
| `TransformKeyRegistry.TryGetTransform` returns `false` for a known key | Index not built, or a distinct-key hash collision | Call `BuildIndex()` during initialization; verify the registry is active and enabled. |
| Transform registry lookup is slow on first call | `Build On Awake` disabled; first lookup triggers a build | Build explicitly during load, or accept the one-time cost. |
| `AdaptiveSafeAreaFitter` does not update after rotation | Component disabled, or hierarchy dimensions unchanged | Verify the component is enabled; force a recalculation by changing screen state or configuration. |
| `FPSCounter` allocates every frame | Combined display mode with IMGUI rendering | Switch to instant-only or average-only mode. |
| Property group does not render | Target Editor does not derive from `PropertyGroupInspectorDrawer` | Add a custom Editor that inherits `PropertyGroupInspectorDrawer`. |
| Splash screen is not stopped on device | License does not permit `SplashScreen.Stop`, or the registration type was stripped | Verify license eligibility; confirm `AlwaysLinkAssembly` is preserved; check target Player logs. |
| `StringAsConstSelector` loses a custom value | `AllowCustom` is `false` | Set `AllowCustom = true` to preserve values from open external contracts. |
| Colors Preview is empty in Player build | Colors Preview is Editor-only | Access colors through the Runtime `Colors` class directly. |

## Running the tests

| Assembly | Platform |
| --- | --- |
| `CycloneGames.Utility.Tests.Editor` | EditMode |
| `CycloneGames.Utility.Tests.PlayMode` | PlayMode |
| `CycloneGames.Utility.Tests.Performance` | Editor, when `com.unity.test-framework.performance >= 3.0.0` is installed |

```text
<UnityEditor> -batchmode -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -assemblyNames CycloneGames.Utility.Tests.Editor -testResults <result-path> -quit
```
