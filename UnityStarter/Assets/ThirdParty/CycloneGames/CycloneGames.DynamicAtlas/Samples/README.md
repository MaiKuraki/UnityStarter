# CycloneGames.DynamicAtlas Samples

[English | 简体中文](README.SCH.md)

The sample is in `CycloneGames.DynamicAtlas.Samples` (`autoReferenced: false`). A host assembly must reference it explicitly.

## Run the lease sample

1. Create or select a GameObject with a `SpriteRenderer`.
2. Add `DynamicAtlasLeaseSample`.
3. Assign **Target Renderer** and a rectangular **Source Sprite**.
4. If the source belongs to a `SpriteAtlas`, disable rotation and Tight Packing on that atlas, or enable the Runtime Extraction preset on the `CycloneGames.AtlasPipeline` rule.
5. Keep the default key `samples/dynamic-atlas/icon`, or replace it with a namespaced content identity.
6. Enter Play Mode; disable and re-enable the component to exercise release and reacquisition.
7. Open `Tools > CycloneGames > Dynamic Atlas > Dynamic Atlas Debugger` to inspect pages, leases, copy path, and estimated texture bytes.

`Awake` creates a `DynamicAtlasService` with a 512-pixel page, 64 entries, a 2 MiB texture budget, `RemoveWhenUnused`, and `AllowSynchronousReadback`. `OnEnable` acquires a `DynamicAtlasSpriteLease`; `OnDisable` releases it; `OnDestroy` disposes the service.

See the [Dynamic Atlas Guide](../Documents~/DynamicAtlas.md) for copy paths, retention, diagnostics, and capacity planning.
