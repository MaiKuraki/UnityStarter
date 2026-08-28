# CycloneGames.AtlasPipeline

Data-driven sprite import and `SpriteAtlas` (V2) generation pipeline for Unity 2022.3+. Designed for large 2D projects: deterministic output, incremental indexing, batch-friendly asset editing, zero-allocation hot paths, and build-time validation.

## Highlights

- **Data-driven rules** — `AtlasImportRule` maps a source folder to sprite import settings (per-platform formats, compression, mipmaps, pixel art) and an atlas strategy (`PerSourceFolder` / `PerChildFolder` / `PerSprite` / `None`). Rules live in a project-owned `AtlasPipelineSettings` asset, not in code.
- **Single-pass import** — settings are applied in `OnPreprocessTexture`; the postprocessor never force-reimports, so there is no import loop.
- **GUID-tracked folders** — rule source folders are stored by GUID; renaming a folder in the Project window keeps every rule pointed at it. Legacy path-only configs are healed automatically.
- **Deterministic output** — rule resolution order is stable (longest folder first, keyword rank, configuration-index tiebreak); atlas keys are validated for uniqueness; packable identity is compared by `assetPath + spriteName`.
- **Incremental index** — postprocessor events update only affected atlases; full scans run only on explicit rebuilds. Editor auto-processing is time-budgeted (8 ms/frame) instead of count-based.
- **Batch-friendly** — bulk import and atlas generation run inside `AssetDatabase.StartAssetEditing`; self-triggered `projectChanged` events are suppressed to avoid full-rescan feedback loops; all dialogs degrade to `LogWarning` in batch mode.
- **Safety rails** — output folder / source folder overlap is rejected at validation; oversized sprites are reported instead of silently dropped by Unity; orphan `.spriteatlasv2` files are swept; expected atlases are verified after generation and fail the build when missing.
- **ASCII-name policy** — optional strict naming (ASCII letters/digits/`_`/`-`), enforced through a rename review flow and build validation.

## Installation

This repository embeds the package in `Packages/`:

```json
"com.cyclone-games.atlas-pipeline": "file:CycloneGames.AtlasPipeline"
```

For a standalone UPM distribution, copy the `CycloneGames.AtlasPipeline` folder into your project's `Packages/` directory or host it as a git dependency. The package has no external dependencies.

**Using the folder directly under `Assets/`** is also supported: place the pipeline folder (including its asmdef) anywhere under `Assets/`, then add `BUILD_PIPELINE_HAS_ATLAS_PIPELINE` to *Scripting Define Symbols* so the build integration compiles. The UPM form needs no such step — see the build integration README for the full conditional-compilation matrix. Do not keep both an `Assets/` copy and a `Packages/` copy at the same time (duplicate assembly names conflict).

## Getting Started

1. Open `Tools/CycloneGames/Atlas Pipeline/Open Atlas Pipeline`.
2. The first open creates the project settings asset at `Assets/Settings/AtlasPipelineSettings.asset`.
3. Configure import rules (drag source folders from the Project view).
4. Use `Apply Importers` to write import settings, `Rebuild Index` to rescan, and `Regenerate Atlases` to (re)build every configured atlas.

Atlases are generated into `Assets/Settings`-adjacent content folders — the exact location is configured by `Output Atlas Folder` in the settings asset.

## Build Integration

The package itself has no dependency on the project's Build module. A thin `IBuildStep` adapter lives at:

```
Assets/Build/Editor/BuildPipeline/Integrations/AtlasPipeline/
```

It registers the `cyclonegames-atlas-pipeline` build step and compiles only while the package is present (guarded by a `versionDefines` on `com.cyclone-games.atlas-pipeline`). Add it to your `BuildData.asset` recipe as a prerequisite of `asset-content`.

## Testing

EditMode tests (NUnit) live under `Tests/Editor/` and cover the pure logic: naming policy, rule matching, platform format mapping, PNG/JPEG header parsing, GUID-following folder references, and rotation policy. Run them from `Window > General > Test Runner`.

## Naming Conventions

- Assembly: `CycloneGames.AtlasPipeline.Editor`
- Namespace: `CycloneGames.AtlasPipeline`
- Settings asset (project-owned): `Assets/Settings/AtlasPipelineSettings.asset`
