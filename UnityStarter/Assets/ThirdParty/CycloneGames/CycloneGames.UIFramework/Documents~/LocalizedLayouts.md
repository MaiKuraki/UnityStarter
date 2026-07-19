# Localized UI Layouts

[English | 简体中文](LocalizedLayouts.SCH.md)

Localized UI Layouts apply locale-specific geometry and typography overrides to a UI prefab when the committed locale changes. The integration stores visual differences alongside the prefab and applies them deterministically, without replacing localized strings, sprites, fonts, or asset resolution.

## Table of Contents

- [Overview](#overview)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Troubleshooting](#troubleshooting)

## Overview

Localized content can change a UI's required space and reading direction. `UILocaleLayout` stores those visual differences as per-locale snapshots and applies them only when the locale changes, targeting `UIWindow` prefabs managed by `UIService` and standalone canvases composed explicitly by the product layer.

### Key Features

- **Per-locale snapshots**: `RectTransform` anchors, pivot, position, size, scale; TMP font metrics, alignment, RTL state; and `LayoutGroup.childAlignment`.
- **Deterministic fallback**: exact locale, then language-only, then prefab base; every switch restores the base before applying an override.
- **Window-scoped binding**: `LocalizationWindowBinder` scopes binding targets to the window session and releases them in reverse order.
- **Authoring support**: capture, validation, temporary preview with automatic restore, and schema normalization through `SerializedProperty` and Undo-aware editing.
- **Event-driven updates**: locale changes are event-driven; there is no per-frame hierarchy scan.

### Quick Start

**1. Configure Localization**

Create the product's `LocalizationSettings` and `Locale` assets through the Localization authoring workflow. Initialize a single `ILocalizationService` owned by the application or session scope before any UI binding is created.

```csharp
var localization = new LocalizationService();
localization.Initialize(localizationSettings.ToOptions());
```

**2. Register the window binder**

Create one `LocalizationWindowBinder` and pass it to `UIService` with the other window binders. The supported composition order is: create and initialize `ILocalizationService`, construct `LocalizationWindowBinder` on the Unity main thread, construct `UIService` with that binder, then open windows or bind standalone targets.

```csharp
IUIWindowBinder[] binders =
{
    new LocalizationWindowBinder(localization, assetPackage),
};

var uiService = new UIService(
    uiRoot,
    assetProvider,
    options: uiOptions,
    binders: binders);
```

**3. Add the layout component and track elements**

Add `UILocaleLayout` to the `UIWindow` prefab root or to a stable child that owns the localized region. Use one of these component context menus to track elements:

- `TMP_Text > Track Locale Layout`
- `Image > Track Locale Layout`
- `RectTransform > Track Locale Layout`
- `LayoutGroup > Track Locale Layout`

If no `UILocaleLayout` exists, the action offers to add one to the nearest `UIWindow` root or canvas root. The action records Undo, updates parallel snapshot data safely, preserves Prefab Overrides, and selects the layout component.

## Core Concepts

### Data model

One `UILocaleLayout` owns three serialized collections:

| Data | Meaning |
| --- | --- |
| Base locale | Locale code represented by the prefab's authored hierarchy. No override snapshot is required for it. |
| Tracked elements | Stable ordered references to a `RectTransform` and optional TMP text and `LayoutGroup`. |
| Locale snapshots | Per-locale values stored in the same index order as tracked elements. |

The tracked-element order is part of the serialized contract. Use the Inspector actions to add, remove, clean, or reorder elements so every snapshot remains aligned with that order.

### Captured values

| Component | Captured properties |
| --- | --- |
| `RectTransform` | `anchorMin`, `anchorMax`, `pivot`, `anchoredPosition`, `sizeDelta`, `localScale` |
| `TMP_Text` | `fontSize`, `lineSpacing`, `characterSpacing`, `alignment`, `isRightToLeftText` |
| `LayoutGroup` | `childAlignment` |

References are optional except for `RectTransform`. A tracked entry without TMP text does not change text settings. A tracked entry without a `LayoutGroup` does not change child alignment.

### Locale fallback

Layout lookup uses a small deterministic chain:

1. exact locale override;
2. language-only override;
3. prefab base layout.

Examples with base locale `en`:

| Requested locale | Available overrides | Result |
| --- | --- | --- |
| `ja-JP` | `ja-JP`, `ja` | `ja-JP` |
| `ja-JP` | `ja` | `ja` |
| `en-US` | none | base prefab layout |
| `de-DE` | none | base prefab layout |

Comparisons are ordinal and case-insensitive for authoring resilience. If a snapshot has fewer values than tracked elements, or a current-schema entry has no value, the unmatched element uses its captured base layout. Switching locales cannot leave unmatched elements with geometry from the previously applied locale.

## Usage Guide

### Author locale overrides

The prefab hierarchy is the base-locale layout. Author it first, then create only the overrides that differ.

1. Set `Base Locale` to the prefab's authored locale.
2. Add an override with `Add from Localization Settings` or enter a BCP 47-style code manually.
3. Select the override in `Editing Locale`.
4. Click `Apply for Editing`.
5. Adjust the tracked `RectTransform`, TMP text, and `LayoutGroup` properties in their normal Inspectors.
6. Review the difference status.
7. Click `Capture Current Hierarchy`.
8. Repeat for other locales.

`Capture Current Hierarchy` writes a complete current-schema snapshot for the selected locale. It is an explicit authoring commit point; normal scene changes are not silently copied into locale data.

### Preview

`Preview Snapshot` uses Unity `AnimationMode` and registers every affected serialized property before applying temporary values. Preview does not create Undo records, Scene dirty state, or Prefab Overrides. `Exit Preview` stops the animation preview and restores the serialized hierarchy values.

Only one Unity animation preview can own `AnimationMode`. Locale preview is refused while Timeline, Animation Window, or another tool owns it. Preview is also restored automatically before prefab save, entering Play mode, domain reload, Undo or hierarchy replacement, and closing or replacing the Inspector. Preview is not a persistence path; use `Apply for Editing` and `Capture Current Hierarchy` for authored changes.

### Standalone UI composition

A locale-aware canvas outside a `UIWindow` can bind explicitly. `Bind` and `Unbind` are main-thread-only and idempotent for the same service relationship. A disabled explicitly bound component unsubscribes and reapplies the current locale when enabled again. Do not combine explicit binding and a window binder unless duplicate delivery is understood.

```csharp
public sealed class MenuComposition : MonoBehaviour
{
    [SerializeField] private UILocaleLayout localeLayout;

    private ILocalizationService _localization;

    public void Initialize(ILocalizationService localization)
    {
        _localization = localization;
        var context = new LocalizationBindingContext(localization);
        localeLayout.Bind(in context);
    }

    private void OnDestroy()
    {
        if (localeLayout != null)
        {
            localeLayout.Unbind();
        }
        _localization = null;
    }
}
```

### Undo, Prefab Overrides, and multiple selection

Persistent Inspector actions record the affected component or UI objects with Unity Undo, call `SerializedObject.ApplyModifiedProperties()`, and record Prefab Instance property modifications. The tool explicitly marks a Scene dirty only for persistent actions; preview restores the temporary hierarchy values.

When multiple `UILocaleLayout` components are selected, the Inspector permits safe base-locale editing but disables index-sensitive element and snapshot actions.

## Advanced Topics

### Custom localization binding targets

Window children can implement `ILocalizationBindingTarget` when locale changes require behavior beyond a serialized layout snapshot. The target owns its service subscription and releases it from `Unbind`. `LocalizationWindowBinder` discovers binding targets once during window binding and keeps the resulting list for that window lifetime. It binds in hierarchy order; a failed target causes reverse-order `Unbind`, including the target whose `Bind` call failed. Each target must make `Unbind` idempotent and keep Unity mutations on the main thread.

```csharp
public sealed class LocaleSpecificIconPolicy : MonoBehaviour, ILocalizationBindingTarget
{
    private ILocalizationService _localization;

    public void Bind(in LocalizationBindingContext context)
    {
        Unbind();
        _localization = context.Localization;
        _localization.Changed += HandleLocalizationChanged;
        Apply(_localization.CurrentLocale);
    }

    public void Unbind()
    {
        if (_localization != null)
        {
            _localization.Changed -= HandleLocalizationChanged;
            _localization = null;
        }
    }

    private void HandleLocalizationChanged(LocalizationChange change)
    {
        Apply(change.CurrentLocale);
    }

    private void Apply(LocaleId locale)
    {
        // Apply a feature-owned, main-thread-only presentation policy.
    }
}
```

Targets that load localized assets receive the optional `IAssetPackage` through `LocalizationBindingContext`; they own cancellation and handles acquired during their binding lifetime. Locale commit ordering and reentrancy are owned by `LocalizationService`. Presentation targets observe only committed, revisioned `LocalizationChange` events.

### Right-to-left and alignment behavior

For tracked TMP text, snapshots store both `alignment` and `isRightToLeftText`. For tracked `LayoutGroup` components, snapshots store `childAlignment`. These settings support common Arabic, Hebrew, and mixed-direction authoring workflows without runtime reflection.

The integration does not reverse arbitrary hierarchy order, navigation order, animation direction, or product-specific semantic layout. For a region that needs structural mirroring, use a dedicated optional component or separate prefab variant owned by that feature. Keep a single authority for each layout property so Unity layout components, animation, and locale snapshots do not continuously overwrite one another.

### Serialization and schema compatibility

The component serializes base locale, tracked elements, and locale snapshots in Prefabs or Scenes. It writes no `EditorPrefs`, `PlayerPrefs`, `SessionState`, registry data, or standalone cache file.

| Schema state | Runtime behavior |
| --- | --- |
| Current schema | Applies all captured geometry, TMP, RTL, and layout-group values. Entries without a captured value use base layout. |
| Schema `0` | Restores base values for current-schema properties, then applies font size, line spacing, character spacing, anchored position, and size delta. |
| Future unsupported schema | Editor validation reports an error. Do not publish until the module understands that schema. |

Runtime treats an unsupported future schema as unavailable and restores the captured base layout. The Editor fails closed for the entire `UILocaleLayout`: all editing actions are disabled.

`Migrate and Normalize Snapshots` is an explicit Editor migration action. It aligns every snapshot with the tracked-element count, preserves schema `0` values, captures the current hierarchy for newly represented anchor, pivot, scale, alignment, and RTL values, and marks migrated snapshots with the current schema. Review every locale after migration because the current hierarchy supplies properties that were not present in schema `0`.

### Runtime cost and memory ownership

`UILocaleLayout` has no `Update`, `LateUpdate`, coroutine, worker thread, lock, or polling loop.

| Operation | Cost and ownership |
| --- | --- |
| Layout initialization | Allocates one base `ElementSnapshot[]` per layout instance, sized to tracked elements and owned by that component. |
| Window binding | Scans `MonoBehaviour` children once, allocates one binding-target list, and binds in hierarchy order. |
| Locale change | Each subscribed target handles the committed event; each layout scans only its tracked elements. No hierarchy discovery occurs. |
| Window close or rollback | Calls target `Unbind` in reverse order and clears binding references. |

The successful locale-application loop contains no intentional managed allocation. Large UI prefabs should track only locale-sensitive elements; avoid capturing decoration that never changes. Canvas rebuild and Unity layout-system costs depend on hierarchy shape and cannot be inferred from the number of snapshots alone.

### Threading and platform boundary

Construction, `Bind`, `Unbind`, window binding disposal, localization mutations, and every Unity UI mutation are confined to the Unity main thread. `LocalizationService` captures the mutation owner during initialization and rejects off-owner mutation. Its immutable lookup snapshot can serve pure managed queries concurrently, but this UI integration never accesses Unity objects from a worker thread and does not maintain a secondary dispatcher or lock.

The implementation uses managed C#, Unity UI, TMP, and explicit asmdef references. It has no native plugin, file I/O, dynamic code generation, runtime reflection, unsafe code, or worker-thread requirement.

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Locale change updates text but not geometry | `LocalizationWindowBinder` not registered, or standalone UI not bound | Register the binder; for standalone UI call `Bind` |
| A locale uses base geometry | No override captured | Add an exact or language-only override and capture it |
| Reorder buttons disabled | Schema `0` or length-mismatched data, or future schema | Run `Migrate and Normalize Snapshots` then review each locale; for future schema use a compatible module version |
| An element is listed twice | Duplicate tracked element | Run `Remove Missing and Duplicate` |
| Preview appears to remain active | Preview not exited | Select the layout and click `Exit Preview`; prefab save, Play mode, reload, and Inspector close also restore it |
| RTL text direction correct but child order not | Structural mirroring not supported | Author a feature-specific structural mirroring solution |
| Preview refused | Another tool owns `AnimationMode` | Stop Timeline or Animation Window preview first |
| Future-schema component locked | Editor does not understand the schema | Open the asset with a compatible module version or restore a compatible Prefab/Scene revision |

### Verification

Run EditMode tests for `CycloneGames.UIFramework.Tests.Editor.LocalizationIntegrationTests` and `CycloneGames.UIFramework.Tests.Editor.LocalizationEditorTests`. The focused suites cover snapshot application, language fallback, base restoration, schema `0` and future-schema behavior, missing values, binding ownership, hierarchy-order bind, reverse-order disposal, off-owner mutation rejection, reentrant locale commit ordering, `AnimationMode` preview restoration, and future-schema authoring guards.

Manual Editor checks: open a UI prefab in Prefab Mode, track a TMP element, add two locale overrides, apply/edit/capture each override, preview each locale and verify automatic restoration, verify Undo/Redo for capture/apply/add/remove/clean/migration, instantiate the prefab and verify Prefab Overrides, select multiple layout components and confirm index-sensitive actions remain disabled, enter Play mode and confirm layout changes occur once per locale event, and confirm locale Preview remains disabled while an Animation Window or Timeline preview owns `AnimationMode`.
