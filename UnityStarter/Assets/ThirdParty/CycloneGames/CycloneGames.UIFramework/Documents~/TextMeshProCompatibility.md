# TextMeshPro Compatibility

[English | 简体中文](TextMeshProCompatibility.SCH.md)

TextMeshPro is an optional capability, not a package dependency. This document records how Unity ships TextMeshPro on each supported branch, how the capability symbol is derived, and what happens when TextMeshPro is unavailable.

The symbol rules are shared by several CycloneGames modules. Three of the assemblies below live in the companion module `CycloneGames.UIFramework.Localization`, which is what binds `CycloneGames.Localization` to `CycloneGames.UIFramework`; they use exactly the same `versionDefines` and `defineConstraints` configuration as the assemblies in this package.

## Why a capability symbol is required

| Unity branch | How TextMeshPro ships | `com.unity.textmeshpro` | Assembly name |
| --- | --- | --- | --- |
| Unity 2022 LTS and earlier | Standalone UPM package | Present in `Packages/manifest.json`; Package Manager can add or remove it | `Unity.TextMeshPro` |
| Unity 2023.2 and Unity 6 (6000.0+) | Merged into uGUI, shipped as `com.unity.ugui` 2.0.0 | Does not exist; Package Manager cannot install or remove it | `Unity.TextMeshPro` |

The critical asymmetry is on the Unity 6 branch. Because `com.unity.textmeshpro` is no longer a resolvable package there, a `versionDefines` rule keyed on that id silently stops matching. An integration that relies only on that rule compiles on Unity 2022 and silently disappears on Unity 6, even though TextMeshPro is guaranteed to be present.

The managed assembly name is unchanged on both branches, so an asmdef `references` entry for `Unity.TextMeshPro` keeps resolving. Unity also treats an unresolvable assembly reference as a soft reference: a missing assembly does not fail the import, it only narrows which types resolve.

## Capability symbol

`CYCLONEGAMES_HAS_TEXTMESHPRO` is a positive capability symbol. It is derived per assembly by two `versionDefines` rules, one per Unity branch:

```json
"versionDefines": [
    {
        "name": "com.unity.textmeshpro",
        "expression": "",
        "define": "CYCLONEGAMES_HAS_TEXTMESHPRO"
    },
    {
        "name": "com.unity.ugui",
        "expression": "2.0.0",
        "define": "CYCLONEGAMES_HAS_TEXTMESHPRO"
    }
]
```

| Rule | Matches | Meaning |
| --- | --- | --- |
| `com.unity.textmeshpro`, empty expression | Any version of the standalone package | Unity 2022 LTS and earlier |
| `com.unity.ugui` `2.0.0` | uGUI 2.0.0 or newer | Unity 6 and later; TextMeshPro is built into uGUI |

An empty expression means "any version". A bare version means "this version or newer".

The two rules are **OR**, not AND. Unity evaluates each `versionDefines` rule independently and defines the symbol when any single rule matches. One matching rule is enough; both matching is also fine; neither matching leaves the symbol undefined and the assembly excluded. Confirming this on Unity 2022.3 with uGUI 1.0.0 and TextMeshPro 3.0.9: only the first rule matches, the symbol is still defined, and the TextMeshPro slices compile.

The resulting truth table:

| uGUI | `com.unity.textmeshpro` | Symbol | Result |
| --- | --- | --- | --- |
| 1.0.0 | Present (any version) | Defined | TextMeshPro slices compile. This is the normal Unity 2022 LTS case |
| 1.0.0 | Absent | Undefined | TextMeshPro slices excluded; binder keeps working |
| 2.0.0+ | Present | Defined | Both rules match. Compiles |
| 2.0.0+ | Absent | Defined | Unity 6 and later. Compiles; TextMeshPro comes from uGUI |

uGUI 2.0.0 on Unity 2022 is not a supported configuration: `com.unity.ugui` is a built-in package pinned to the editor version, and 2.0.0 declares a Unity 2023.2 minimum. Forcing it into the manifest produces an unsatisfiable dependency. Should that configuration ever resolve, the second rule matches and the module still compiles, because the rule set is OR.

Owner: the asmdef that declares the rules. Scope: that assembly only. Never add `CYCLONEGAMES_HAS_TEXTMESHPRO` to Player Settings scripting define symbols; a stale manual symbol would force compilation without TextMeshPro and fail the build.

## Assemblies that consume the symbol

| Assembly | Owning module | `defineConstraints` | Content |
| --- | --- | --- | --- |
| `CycloneGames.UIFramework.Runtime.Integrations.Localization.TextMeshPro` | `CycloneGames.UIFramework.Localization` | Yes | `UILocaleLayout`, `TrackedElement`, `ElementSnapshot`, `LocaleSnapshot` |
| `CycloneGames.UIFramework.Editor.Integrations.Localization.TextMeshPro` | `CycloneGames.UIFramework.Localization` | Yes | `UILocaleLayoutEditor`, `LocalizeContextMenu` |
| `CycloneGames.UIFramework.Tests.Editor.Integrations.Localization` | `CycloneGames.UIFramework.Localization` | Yes | Locale layout tests |
| `CycloneGames.UIFramework.Tests.Editor` | `CycloneGames.UIFramework` | No (file-level `#if`) | `TemplateProcessor_RemovesPlaceholderWindowAndUpdatesPreferredTmpTitle` |
| `CycloneGames.Localization.Components.TextMeshPro` | `CycloneGames.Localization` | Yes | `LocalizeTMPText` |

`CycloneGames.UIFramework.Editor.Integrations.Localization` keeps the `UIFrameworkLocalizationEditorLog` facade internal, as every log facade in this repository is. Because the TextMeshPro editor slice is a separate assembly, that assembly declares `InternalsVisibleTo("CycloneGames.UIFramework.Editor.Integrations.Localization.TextMeshPro")` in its own `AssemblyInfo.cs`. Without it the slice fails with `CS0122`.

Assemblies that must survive without TextMeshPro keep no compile-time TextMeshPro dependency:

- `CycloneGames.UIFramework.Runtime.Integrations.Localization` — `LocalizationWindowBinder`. Owned by `CycloneGames.UIFramework.Localization`.
- `CycloneGames.UIFramework.Editor.Integrations.Localization` — the shared `UIFrameworkLocalizationEditorLog` facade. Owned by `CycloneGames.UIFramework.Localization`.
- `CycloneGames.UIFramework.Editor` — `UIWindowCreatorWindow` and `UIWindowTemplateProcessor` match TextMeshPro by type name string, not by type reference, so they never need the assembly.
- `CycloneGames.Localization.Components` — `LocalizeImage`.

## Behavior when TextMeshPro is missing

1. Every assembly listed with `defineConstraints` is excluded from compilation. No error, no warning about a missing package.
2. The localization window binder still compiles and still binds every `ILocalizationBindingTarget` in a window hierarchy.
3. `UILocaleLayout` no longer exists as a component. Any prefab or scene that carried one reports a missing script and its serialized snapshot data is not loaded. This is a data-loss path: install TextMeshPro before opening such assets, or back them up first.
4. `LocalizeTMPText` is unavailable; string localization keeps working through image, audio, and custom binding targets.

## Verification

1. Unity 2022 LTS: confirm `com.unity.textmeshpro` is in `Packages/manifest.json`.
2. Unity 6: confirm `com.unity.ugui` resolves to 2.0.0 or newer. `com.unity.textmeshpro` must be absent; its absence is expected, not a failure.
3. On both branches, open the Inspector for the asmdef and read the *Expression outcome* field of both rules. At least one rule should report a match; two matches are also valid.
4. Confirm `Library/ScriptAssemblies/` contains `CycloneGames.UIFramework.Runtime.Integrations.Localization.TextMeshPro.dll`.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| Locale layout scripts exist on Unity 2022 but not on Unity 6 | Only the `com.unity.textmeshpro` rule is present | Add the `com.unity.ugui` `2.0.0` rule |
| `error CS0246: The type or namespace name 'TMPro' could not be found` | `CYCLONEGAMES_HAS_TEXTMESHPRO` was set manually while TextMeshPro is absent | Remove the manual symbol; let `versionDefines` derive it |
| `UILocaleLayout` reports a missing script | The TextMeshPro slice was excluded | Install TextMeshPro, or on Unity 6 confirm uGUI is 2.0.0 or newer |
| An asmdef shows a console warning about `Unity.TextMeshPro` | Soft reference to an absent assembly | Expected. The reference is harmless because `defineConstraints` excludes the assembly |
