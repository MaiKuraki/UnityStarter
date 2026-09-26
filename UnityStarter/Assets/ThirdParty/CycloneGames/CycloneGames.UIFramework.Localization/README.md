# CycloneGames.UIFramework.Localization

[English | 简体中文](README.SCH.md)

CycloneGames.UIFramework.Localization is an optional companion module that binds [CycloneGames.Localization](../CycloneGames.Localization/README.md) to [CycloneGames.UIFramework](../CycloneGames.UIFramework/README.md) windows. It gives a window a transactional, window-scoped localization binding and, when TextMeshPro is available, adds per-locale layout snapshots.

Neither package depends on this module. `CycloneGames.UIFramework` compiles without `CycloneGames.Localization`, and `CycloneGames.Localization` compiles without `CycloneGames.UIFramework`. This module is the only place where the two meet.

## Table of Contents

- [CycloneGames.UIFramework.Localization](#cyclonegamesuiframeworklocalization)
  - [Table of Contents](#table-of-contents)
  - [Overview](#overview)
  - [Core Types](#core-types)
  - [Quick Start](#quick-start)
  - [TextMeshPro Layering](#textmeshpro-layering)
  - [Threading and Lifecycle](#threading-and-lifecycle)
  - [Removal](#removal)
  - [Troubleshooting](#troubleshooting)

## Overview

`LocalizationWindowBinder` is an `IUIWindowBinder`. It scans the instantiated window hierarchy once, binds every `ILocalizationBindingTarget` in hierarchy order, rolls back in reverse order if any bind fails, and unbinds in reverse order when the window closes. The binder never rescans on locale change; already-bound targets observe the locale change themselves.

`UILocaleLayout` is an `ILocalizationBindingTarget` that stores per-locale geometry and typography snapshots next to a prefab and applies them when the committed locale changes. It requires TextMeshPro because `TrackedElement.Text` is a `TMP_Text`. See [Localized UI Layouts](Documents~/LocalizedLayouts.md).

```mermaid
flowchart LR
    App["Application composition"] --> Service["UIService with binder list"]
    Service --> Binder["LocalizationWindowBinder"]
    Loc["ILocalizationService"] --> Binder
    Binder --> Window["UIWindow hierarchy"]
    Window --> Targets["ILocalizationBindingTarget components"]
    Targets --> Layout["UILocaleLayout (TextMeshPro only)"]
```

## Core Types

| Type | Assembly | Role |
| --- | --- | --- |
| `LocalizationWindowBinder` | Runtime | `IUIWindowBinder` that binds every `ILocalizationBindingTarget` in a window hierarchy |
| `UILocaleLayout` | Runtime TextMeshPro | `MonoBehaviour` and `ILocalizationBindingTarget` applying per-locale layout snapshots |
| `TrackedElement` | Runtime TextMeshPro | One tracked RectTransform, text, or layout group |
| `ElementSnapshot` | Runtime TextMeshPro | Per-locale captured value for one tracked element |
| `LocaleSnapshot` | Runtime TextMeshPro | All element snapshots for one locale |
| `UIFrameworkLocalizationEditorLog` | Editor | Internal log facade for localization authoring tooling |

## Quick Start

Create the binder on the Unity main thread and pass it to `UIService`. The localization service must already be initialized.

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Localization.Runtime;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations.Localization;
using UnityEngine;

public sealed class GameUiBootstrap : MonoBehaviour
{
    [SerializeField] private UIRoot uiRoot;
    [SerializeField] private UIWindowConfiguration startupWindow;

    private IUIService _ui;

    private async UniTask RunAsync(
        ILocalizationService localization,
        CancellationToken lifetimeToken)
    {
        IUIWindowBinder[] binders = { new LocalizationWindowBinder(localization) };

        _ui = new UIService(uiRoot, options: null, binders: binders);
        await _ui.OpenAsync(startupWindow, cancellationToken: lifetimeToken);
    }
}
```

`LocalizationWindowBinder` also accepts an optional `IAssetPackage`. Pass one when localized assets should resolve through a specific package instead of the default.

Standalone canvases that are not managed by `UIService` can bind `UILocaleLayout` from the product composition root instead. The window binder is not required for that path.

## TextMeshPro Layering

`TrackedElement.Text` is a `TMP_Text`, so `UILocaleLayout` lives in a separate assembly that is excluded when TextMeshPro is unavailable. The TMP-free assembly keeps no compile-time TextMeshPro dependency, so `LocalizationWindowBinder` keeps binding images, audio, and custom targets on every Unity version.

| Unity branch | How TextMeshPro ships | Symbol source |
| --- | --- | --- |
| Unity 2022 LTS and earlier | Standalone `com.unity.textmeshpro` package | First `versionDefines` rule |
| Unity 2023.2 and Unity 6 (6000.0+) | Inside `com.unity.ugui` 2.0.0 | Second `versionDefines` rule |

Never add `CYCLONEGAMES_HAS_TEXTMESHPRO` to Player Settings. A stale manual symbol forces compilation without TextMeshPro and fails the build.

## Threading and Lifecycle

Construction, binding, and disposal are confined to the Unity main thread. The binder captures its owner thread and rejects use from another thread. Component discovery happens once per window instance; locale changes do not rescan the hierarchy.

The binder participates in the `UIService` open transaction. If any target fails to bind, already-created bindings are disposed in reverse order and the open aborts. Window closure disposes in reverse order as well.

## Removal

Delete the module folder, or remove its manifest entry in a `Packages/` project. Both host packages keep compiling. Prefabs and scenes that carried a `UILocaleLayout` report a missing script, and their serialized snapshot data is no longer loaded. Back those assets up before removing the module.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `CS0234: The type or namespace name 'Localization' does not exist in the namespace 'CycloneGames'` | `CycloneGames.Localization` is not installed | Install `CycloneGames.Localization`, or remove this module |
| `CS0012: The type 'IAssetPackage' is defined in an assembly that is not referenced` | A consumer constructs `LocalizationBindingContext` without referencing `CycloneGames.AssetManagement.Runtime` | Add the reference; the optional constructor parameter forces it |
| `CS0122: 'UIFrameworkLocalizationEditorLog' is inaccessible` | The editor TextMeshPro slice cannot see the internal facade | Keep `AssemblyInfo.cs` with `InternalsVisibleTo` for that assembly |
| `UILocaleLayout` reports a missing script | The TextMeshPro slice was excluded | Install TextMeshPro, or on Unity 6 confirm uGUI is 2.0.0 or newer |
| `InvalidOperationException` on `Bind` | The localization service was not initialized | Initialize `ILocalizationService` before opening windows |
