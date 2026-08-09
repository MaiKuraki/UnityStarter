# Obfuz Player Build Integration

The Obfuz Player integration is an explicit `player` extension. A persistent marker configuration selects the vendor's durable Player pipeline, while the adapter verifies package availability, the readable vendor setting, and the generated Encryption VM before Unity enters `BuildPipeline.BuildPlayer`.

> **Current checkout:** Obfuz is not present in `Packages/manifest.json`, `Packages/packages-lock.json`, or the local package manifests scanned for this module. The reflection adapter and missing-package tests compile, but no Obfuz provisioning step, obfuscated Player, or runtime verification has been executed in this checkout.

## Responsibilities and boundaries

| Component | Responsibility |
| --- | --- |
| `ObfuzPlayerBuildExtensionConfiguration` | Persistent, typed marker that explicitly selects Provider ID `obfuz` |
| `ObfuzPlayerBuildExtensionAdapter` | Selected-extension validation and project-wide environment consistency |
| `ObfuzIntegrator` | Narrow reflection boundary for supported Obfuz and Obfuz4HybridCLR API shapes |
| `PlayerBuildConfiguration` | Ordered list of explicitly selected Player extension assets |
| `PlayerBuildStep` | Validates, begins, and finally unwinds environment and extension sessions around `BuildPlayer` |

This integration does not install Obfuz, generate or compile the Encryption VM, edit `ProjectSettings/Obfuz.asset`, choose obfuscation rules, manage keys, rewrite assemblies itself, stage vendor intermediates, or implement runtime tamper protection.

The Player extension described here is independent of the HybridCLR + Obfuz hot-update provider. Base Obfuz is required for Player obfuscation; `Obfuz4HybridCLR` is required only by the separate combined hot-update workflow.

## Installation and availability

The Build assembly has no compile-time Obfuz dependency. Base availability requires both:

- `Obfuz.Settings.ObfuzSettings`;
- `Obfuz.Settings.BuildPipelineSettings`.

Player readiness additionally requires:

- a readable `ObfuzSettings.Instance.buildPipelineSettings` object, normally provisioned through `ProjectSettings/Obfuz.asset`;
- `buildPipelineSettings.enable` set to `true` when the extension is selected;
- the generated `Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine` type to compile successfully.

Install and lock a compatible Obfuz package, perform its project provisioning in a separate controlled step, compile the Encryption VM, save the project settings, and allow Unity compilation to finish before running BuildData Preflight. The adapter reads the in-memory setting; it does not hash the settings file or prove that the current value has been serialized to disk, so CI must enforce the save/provisioning step.

If Obfuz is missing, the Player extension descriptor is unavailable and a selected existing configuration fails Preflight. If only part of the required API exists, validation reports the unavailable or incomplete settings instead of assuming compatibility.

## Typed authoring

1. Create **CycloneGames > Build > Player Extensions > Obfuz** from the Asset creation menu or the BuildData Player card.
2. Save the resulting `ObfuzPlayerBuildExtensionConfiguration` as a main `.asset` below `Assets/`.
3. Create or select a `PlayerBuildConfiguration`.
4. Add the Obfuz configuration to its ordered `Extensions` list exactly once.
5. Assign that `PlayerBuildConfiguration` to the selected `player` invocation.
6. Save BuildData, the Player configuration, the Obfuz marker, and `ProjectSettings/Obfuz.asset`.
7. Run Workspace Health and Preflight.

The marker intentionally has no serialized tuning fields. Obfuz rules and vendor options belong to the vendor-owned project settings; pipeline membership belongs to the marker asset. This separation makes selection reviewable in BuildData and avoids duplicating vendor configuration.

Player extension provenance includes the stable provider ID `obfuz`, compatibility ID `obfuz-player-v1`, configuration asset path, GUID/local file ID, file hash, size, and Unity dependency hash. The marker must therefore remain a persistent main asset, not a transient object or sub-asset.

The Player extension list permits at most 64 entries and rejects duplicate provider IDs. Do not place secrets or keys in the marker asset; it has no secret-bearing contract.

## Player build lifecycle

~~~mermaid
flowchart LR
    R["Resolve persistent extension asset"] --> F["Capture extension fingerprint"]
    F --> V["Validate package, readable setting, and Encryption VM"]
    V --> E["Validate project-wide Obfuz environment"]
    E --> B["Revalidate immediately before BuildPlayer"]
    B --> O["Vendor Player pipeline obfuscates during Unity build"]
    O --> T["Pipeline validates Player result and terminal evidence"]
~~~

Preflight performs two related checks:

1. the selected adapter requires compatible base Obfuz APIs, a readable and enabled Player-pipeline setting, and a compiled Encryption VM;
2. the environment guard compares the durable vendor setting with BuildData selection for every Player build.

The consistency rule is exact:

| Durable Obfuz Player setting | Obfuz extension selected | Result |
| --- | --- | --- |
| Disabled | No | Allowed |
| Enabled | Yes | Allowed after all other checks |
| Enabled | No | Blocked to prevent unselected obfuscation |
| Disabled | Yes | Blocked because the requested extension would not run |

`BeginEnvironment` and `BeginPlayerBuild` re-run validation immediately before the Player build. They do not mutate settings and return no restoration scope. Actual assembly processing remains owned by the installed Obfuz Player callbacks during Unity's `BuildPlayer` call.

The standard Player output transaction, global build-state guard, result checks, and terminal manifest still wrap the build. They protect pipeline-owned Player output and state; they do not prove or reconstruct vendor-generated Obfuz data.

## CI workflow

Treat Obfuz provisioning and Player production as separate stages:

1. restore the exact Unity Editor and locked Obfuz package;
2. restore version-controlled Obfuz settings and any project-owned generated sources required by the vendor;
3. run the vendor provisioning/Encryption VM compilation step and require a clean Unity compilation;
4. verify `ProjectSettings/Obfuz.asset` has the durable Player pipeline enabled;
5. use a version-controlled Obfuz marker in the Player configuration selected by the CI BuildData profile;
6. run Workspace Health, then invoke `Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine` with the current `-pipelineProfile` and `-buildTarget` options;
7. archive the Player output, terminal pipeline result manifest, package lock evidence, and vendor reports needed by the product's release policy;
8. execute runtime smoke and security-focused checks before promotion.

Do not toggle the vendor setting only in workstation-local state, generate the Encryption VM implicitly during the release build, or rely on package presence alone as evidence that obfuscation ran.

## Persistence and recovery

| Data | Location | Owner and lifecycle |
| --- | --- | --- |
| Obfuz selection marker | Project-chosen `.asset` below `Assets/` | Persistent BuildData input; version-controlled |
| Player extension list | `PlayerBuildConfiguration` asset below `Assets/` | Persistent ordered selection; version-controlled |
| Vendor settings | `ProjectSettings/Obfuz.asset` | Durable vendor configuration; provisioned and saved before Build |
| Encryption VM and other generated inputs | Vendor-defined project locations | Provisioning output; lifecycle is owned by the installed package and project policy |
| Player output | Pipeline-configured Player output path | Protected by the standard Player output transaction |
| Obfuz-specific recovery journal | None | This adapter does not mutate or transactionally own vendor settings or generated files |

A hard interruption may still leave standard Build workspace evidence. Use **Build > Pipeline > Workspace Health** or the canonical batch entry point with `-pipelineRecoverOnly` before retrying. Workspace Recovery restores pipeline-owned state and output; it does not regenerate an Encryption VM, edit `ProjectSettings/Obfuz.asset`, or repair vendor-generated sources.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Obfuz is not offered in the Player extension catalog | Install a compatible base package and wait for Editor compilation. |
| Selected extension reports that base Obfuz is unavailable | Restore the package exposing both required settings types, or remove the marker from the Player configuration. |
| Settings are unavailable or incomplete | Provision and save `ProjectSettings/Obfuz.asset` before Preflight. |
| Durable Player pipeline is disabled | Enable and save the vendor setting, or remove the extension if this Player must remain unobfuscated. |
| Durable Player pipeline is enabled but the extension is absent | Add the explicit marker to the selected Player configuration, or disable and save the vendor setting. |
| Encryption VM is not compiled | Run the vendor provisioning step, resolve compile errors, and restart Preflight. |
| Duplicate provider error | Keep only one `obfuz` marker in the Player extension list. |
| Configuration fingerprint fails | Ensure the marker is a saved main `.asset` below `Assets/` and has not changed during the build. |
| Build succeeds but runtime fails | Inspect vendor reports, generated VM compatibility, stripping/AOT behavior, and target runtime logs. |

## Validation boundary

Current EditMode evidence verifies generic Player-extension discovery/fingerprinting and the missing-package failure path. Source inspection verifies the exact reflection type/member names and the setting-selection consistency logic. There is no on-disk Obfuz settings fingerprint in this adapter.

It does not prove that a compatible Obfuz package is installed, that the Encryption VM was generated correctly, that assemblies were transformed, that runtime execution succeeds, or that the output meets confidentiality, integrity, anti-tamper, IL2CPP, stripping, performance, or platform requirements.

Release qualification must use the locked vendor package and each supported target. At minimum:

1. provision and compile the Encryption VM in a clean CI workspace;
2. build a Player with the extension selected and inspect vendor evidence;
3. run a control build with both the marker and durable setting disabled;
4. confirm mismatched marker/setting combinations fail Preflight;
5. launch the produced Player and exercise reflection, serialization, AOT, stripping, exception, and update paths relevant to the product;
6. measure build-time, size, startup, and runtime cost;
7. verify the product's security requirements independently of identifier obfuscation.

## Source index

- [ObfuzPlayerBuildExtension.cs](ObfuzPlayerBuildExtension.cs)
- [ObfuzIntegrator.cs](ObfuzIntegrator.cs)
- [PlayerBuildConfiguration.cs](../../Authoring/Player/PlayerBuildConfiguration.cs)
- [PlayerBuildExtensionContracts.cs](../../Core/Contracts/PlayerBuildExtensionContracts.cs)
- [PlayerBuildStep.Extensions.cs](../../Steps/Player/PlayerBuildStep.Extensions.cs)
- [PlayerBuildStep.cs](../../Steps/Player/PlayerBuildStep.cs)
- [HybridCLR + Obfuz provider](../HybridCLRObfuz/README.md)
- [Build Foundation](../../../../README.md)
