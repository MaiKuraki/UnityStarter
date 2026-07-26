# GameplayFramework UPM Composition

[Simplified Chinese](UPMComposition.SCH.md)

## Purpose

`com.cyclone-games.gameplay-framework` keeps AssetManagement and GameplayAbilities as optional integrations. Neither package is a required dependency of the GameplayFramework package.

## Assembly Gates

| Assembly | Supported UPM package | Capability | Consumer reference |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | `com.cyclone-games.asset-management` `1.x` | `CYCLONEGAMES_HAS_ASSET_MANAGEMENT` | Explicit |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities` | `com.cyclone-games.gameplay-abilities` `1.x` | `CYCLONEGAMES_HAS_GAMEPLAY_ABILITIES` | Explicit |

Each integration asmdef derives its capability with `versionDefines`, consumes it with `defineConstraints`, and uses `autoReferenced: false`. Install the optional package through UPM and reference only the integration assembly used by the consumer. Do not add these capability symbols to PlayerSettings.

If an optional package is missing or outside the supported version range, Unity excludes only that integration. `CycloneGames.GameplayFramework.Runtime` and `CycloneGames.GameplayFramework.Networking` remain available.

Package-derived version defines require packages resolved by UPM. Merely placing sibling packages under `Assets/` does not activate these capabilities.

## Tests

- AssetManagement integration tests: `CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor`
- GameplayAbilities integration tests: `CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor`
- Core EditMode tests: `CycloneGames.GameplayFramework.Tests.Editor`

The core test assembly has no optional-package references. Validate both dependency-absent and dependency-present UPM profiles. In the absent profile, neither optional integration nor its tests should compile. In the present profile, run both gated test assemblies.

## Persistence

This composition design writes no files and introduces no serialized state. Removing an optional package removes its gated assemblies without requiring asset or save migration.

## Troubleshooting

| Symptom | Resolution |
| --- | --- |
| Optional integration assembly is absent | Confirm that UPM resolved the matching package at a supported `1.x` version, then add an explicit consumer asmdef reference. |
| Core package fails after removing an optional package | Check the consumer and test asmdefs for a direct optional-package reference; the core GameplayFramework assemblies do not require either integration. |
| Capability appears in one checkout but not another | Compare `Packages/manifest.json` and `Packages/packages-lock.json`; do not rely on PlayerSettings symbols or sibling `Assets/` manifests. |
