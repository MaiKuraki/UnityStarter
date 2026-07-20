# UIFramework Samples

English | [简体中文](README.SCH.md)

These samples demonstrate explicit, provider-free composition with a directly referenced `UIWindowConfiguration`. The sample scripts require only `UniTask` and `CycloneGames.UIFramework.Runtime`. The runnable scene uses explicit camera references: the `UIFramework` prefab owns a dedicated URP Overlay `UICamera`, and the scene-owned Base Camera serializes that camera in its URP camera stack. No `CycloneGames.Services` type or runtime camera lookup participates in this composition.

## Contents

| File | Purpose |
| --- | --- |
| `SampleScene.unity` | Runnable Classic Window scene with an explicit URP Base-to-UI camera stack |
| `UIFrameworkSampleBootstrap.cs` | Creates `UIService`, opens a direct configuration, and awaits shutdown |
| `UIFrameworkMvpSampleBootstrap.cs` | Optional composition using an instance-owned `UIPresenterBinder` |
| `UIWindow_SampleUI.cs` | Window, typed sample view, listener, and presenter |
| `DynamicAtlasLeaseSample.cs` | Bounded Dynamic Atlas ownership with a stable key and explicit lease release |
| `Resources/UIWindow_SampleUI.prefab` | Sample window prefab |
| `Resources/UIWindow_SampleUI_Config.asset` | Configuration with stable ID `UIWindow_SampleUI` |

The sample assembly has `autoReferenced: false`; it does not become a default dependency of unrelated project assemblies.

## Run the Classic sample

1. From the Unity project root, open `Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/Samples/SampleScene.unity`.
2. Select the `Boot` GameObject.
3. Verify that `UI Root` references the `UIRoot` component in the scene.
4. Verify that `First Window Configuration` references `UIWindow_SampleUI_Config`.
5. Select the scene-owned Base Camera and verify that its URP camera stack contains the `UIFramework` prefab's `UICamera` exactly once.
6. Enter Play Mode.

The scene owns the Base Camera. The `UIFramework` prefab owns the Overlay `UICamera`, and its `UIRoot` and root Canvas reference that camera explicitly. The Base-to-Overlay stack relationship is serialized in the scene; the bootstrap does not use `Camera.main`, a scene search, a global camera singleton, or `CycloneGames.Services`. When adapting the sample to another render pipeline, replace this scene-specific camera composition with the equivalent explicit setup for that pipeline.

The bootstrap:

1. creates a bounded `UIServiceOptions`;
2. constructs `UIService` with the explicit root and no asset provider;
3. awaits `OpenAsync(firstWindowConfiguration, token)`;
4. remains alive until `GetCancellationTokenOnDestroy()` is canceled;
5. awaits `ShutdownAsync(UIShutdownMode.Immediate, CancellationToken.None)`.

No `async void` lifecycle method is used.

## Run the MVP composition

Use a separate scene or replace the component on `Boot`:

1. remove `UIFrameworkSampleBootstrap`;
2. add `UIFrameworkMvpSampleBootstrap`;
3. assign the same `UIRoot` and `UIWindow_SampleUI_Config`;
4. enter Play Mode.

The MVP bootstrap creates one `UIPresenterBinder`, registers `SampleUIPresenter` for `UIWindow_SampleUI`, passes the binder to the `UIService` constructor, and uses the same cancellation and shutdown ownership.

`UIWindow_SampleUI.UICmd_PrimaryAction` can be connected directly to a Button `OnClick` event. The view forwards the command through `ISampleUIViewListener`; the presenter handles it without a global lookup.

## Run the Dynamic Atlas lease sample

The Dynamic Atlas sample is an independent component and does not change `SampleScene.unity`:

1. Create or select a Canvas with an `Image` component.
2. Add `DynamicAtlasLeaseSample` to a GameObject.
3. Assign the target `Image` and a rectangular source `Sprite`.
4. If the source belongs to a `SpriteAtlas`, disable rotation and Tight Packing on that atlas.
5. Keep the default stable key or replace it with a namespaced content identity.
6. Enter Play Mode, then disable and re-enable the component to exercise release and reacquisition.
7. Open `Tools > CycloneGames > UI Framework > Dynamic Atlas Debugger` to inspect the page, lease reference count, copy path, and estimated texture bytes.

The component owns one 512-pixel page with a 2 MiB estimated texture budget. It acquires a `DynamicAtlasSpriteLease` in `OnEnable`, clears the `Image` and disposes the lease in `OnDisable`, then disposes the owned service in `OnDestroy`. It uses direct `Sprite` acquisition and therefore needs no location loader. It does not use a global manager or hidden cache.

For scene-hosted composition, add `DynamicAtlasManager` and use its styled Inspector to validate capacity, page-memory budget, active BuildTarget context, and the runtime-only loader/unloader ownership pair. The Inspector does not infer target-device copy support from the BuildTarget name.

See the [Dynamic UI Atlas guide](../Documents~/DynamicAtlas.md) for batching constraints, copy paths, retention, diagnostics, capacity planning, and target-device validation.

## Adapting the sample

- Keep `PrefabReference` and call `OpenAsync(configuration)` when configurations are already referenced by a scene or composition asset.
- For runtime content, construct an `IUIWindowAssetProvider` and call `OpenAsync(windowId)`.
- Add a `UINavigationService` through `UIServiceOptions.NavigationService` when opener/back relationships are required.
- Add independent binders for MVP, DI, analytics, or accessibility before opening any window.
- Keep the composition root responsible for awaiting `ShutdownAsync`.

## Validation boundary

Running the Window scene and the Dynamic Atlas component checks their focused behavior in the current Editor. It does not establish Player, IL2CPP, target-platform, long-session, DrawCall reduction, or performance evidence. Use the package README validation matrix and the Dynamic Atlas guide for those scopes.
