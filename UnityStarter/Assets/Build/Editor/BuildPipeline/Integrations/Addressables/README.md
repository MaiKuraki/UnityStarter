# Addressables Build Integration

The Addressables integration implements the generic `asset-content` provider contract. It drives supported Unity Addressables Editor APIs, binds content to the pipeline-owned version, publishes a verified artifact tree, and protects both Addressables settings and publication output with durable transactions.

> **Current checkout:** `com.unity.addressables` is not present in `Packages/manifest.json` or `Packages/packages-lock.json`. The integration compiles through a reflection boundary, but no Addressables content build, Player build, or runtime load has been executed for this checkout. The behavior below is the current source contract and static test evidence.

## Responsibilities and boundaries

| Component | Responsibility |
| --- | --- |
| `AddressablesBuildConfig` | Typed content-build, publication, and Content Update authoring |
| `AddressablesContentBuildAdapter` | Provider discovery, preflight, output claims, execution, and Player-session integration |
| `AddressablesBuilder` | Supported Addressables API calls, version binding, artifact collection, validation, and staging |
| `AddressablesVersionBuildProcessor` | Validates the provider-owned `AddressablesVersion.json` before a dependent Player build |
| `AddressablesPlayerBuildIsolation` | Prevents implicit package rebuilds and suppresses stale package hooks when Addressables is not selected |
| `AddressablesSettingsTransaction` | Snapshots and restores Addressables configuration assets and their meta files |
| `AddressablesPublicationTransaction` | Atomically installs or rolls back the target publication |
| `AddressablesRecoveryCoordinator` | Recovers both transaction families without requiring the optional package |

The integration does not install Addressables, create groups or profiles, choose which assets are addressable, implement runtime initialization, upload to a CDN, sign artifacts, or promote a release. Those remain project provisioning, runtime, and release-pipeline responsibilities.

## Installation and availability

The core `Build.Pipeline.Editor` assembly has no compile-time Addressables reference. Availability is discovered from `UnityEditor.AddressableAssets.Settings.AddressableAssetSettings` and the exact Editor API shapes used by the adapter.

1. Install and lock a compatible `com.unity.addressables` package.
2. Create the project Addressables settings and save all settings, groups, schemas, and profile assets.
3. Configure the active profile's local and remote build/load paths.
4. If remote delivery is required, configure both remote catalog build and load paths.
5. Confirm the Editor's active build target matches the requested pipeline target.
6. Open **Build > Pipeline > Workspace Health**, require a clean workspace, then run BuildData Preflight.

A missing package leaves the core assembly usable and makes the provider unavailable. A partially compatible package fails Preflight with the missing API rather than falling back to an unverified call shape. Unsaved Addressables configuration also fails Preflight.

## Typed authoring contract

Create the configuration from **Assets > Create > CycloneGames > Build > Addressables Build Config**, then assign the persistent asset to an enabled `asset-content` invocation in BuildData.

| Field | Contract |
| --- | --- |
| `Build Remote Catalog` | Requests a remote catalog. Both evaluated remote catalog paths must be defined. Required for Incremental. |
| `Copy To Output Directory` | Publishes a durable artifact tree. If disabled, results remain only in Addressables-managed build locations. Required for Incremental. |
| `Publication Root` | Portable project-relative directory. Empty resolves to `Build/AddressablesContent/<invocation-id>` so invocations are isolated by default. |
| `Baseline Asset` | Imported `addressables_content_state.bin` under `Assets/` for an Incremental invocation. Mutually exclusive with Baseline Path. |
| `Baseline Path` | Portable project-relative path restored by CI before Unity starts. Mutually exclusive with Baseline Asset. |
| `Allow External Profile Publication Sources` | Allows evaluated Addressables profile source roots outside the project only when CI owns them. URI, volume-root, protected, and reparse-point paths remain invalid. |
| `Additional Publication Roots` | Explicit project-relative source roots mapped to unique, safe, non-reserved single-folder destinations. |

Additional destination folders cannot be `PlayerData`, `RemoteContent`, `BuildMetadata`, or `AddressablesArtifacts.json`. Sources must not overlap the publication root. The adapter accepts only `Clean` and `Incremental` incrementality.

The invocation's package version is the canonical content version. The Addressables configuration controls provider behavior; it does not own a second release-version field.

## Clean content lifecycle

~~~mermaid
flowchart LR
    P["Preflight package, paths, profile, and saved settings"] --> L["Acquire Addressables build lock"]
    L --> S["Snapshot configuration assets"]
    S --> B["Clear active builder cache and BuildPlayerContent"]
    B --> V["Write AddressablesVersion.json and validate outputs"]
    V --> G["Stage manifest and publication tree"]
    G --> R["Restore exact settings"]
    R --> T["Shared terminal publication barrier"]
    T -->|"pipeline success"| C["Install and complete publication"]
    T -->|"any later failure"| X["Abort and restore previous publication"]
~~~

For a Clean invocation the integration temporarily applies `BuildRemoteCatalog` and the pipeline content version through `OverridePlayerVersion`. It requires an active data builder with a usable `ClearCachedData` implementation, clears that builder's cache, and invokes the supported `BuildPlayerContent` API.

Only files reported by the build result's `FileRegistry`, its explicit output path, the version artifact, and the validated content-state file are eligible for publication. Files outside approved player, remote, metadata, or additional roots fail closed.

Settings are restored before the staged publication is returned. Publication remains deferred until the complete pipeline reaches its terminal decision, so a later content, hot-update, Player, or evidence failure restores the previous owned output.

## Incremental Content Update

Incremental uses the official `ContentUpdateScript.BuildContentUpdate(AddressableAssetSettings, string)` path and has stricter prerequisites:

1. enable remote catalog generation;
2. enable publication;
3. provide exactly one `.bin` baseline through Baseline Asset or Baseline Path;
4. restore the baseline inside the Unity project, outside `.git`, `Library`, `Logs`, `Packages`, `ProjectSettings`, `Temp`, and `UserSettings`;
5. keep the baseline within its original pipeline publication so a parent `AddressablesArtifacts.json` can be found.

Preflight loads the official content-state object and validates target, active profile ID, exact Unity version, remote catalog load path, Addressables player version, file size, and SHA-256 against the artifact manifest. The verified baseline is copied to an invocation-local scratch directory below `Temp/BuildPipeline/Addressables/ContentUpdate/` before the vendor API reads it; the scratch copy is deleted afterward.

Incremental output cannot feed a Player invocation. Use a focused Content invocation for Content Update, or use Clean when producing a new Player baseline. A missing, moved, edited, cross-target, cross-profile, or otherwise incompatible baseline is rejected.

## Player lifecycle and isolation

A Player that directly consumes a Clean Addressables content invocation opens the provider's exclusive `addressables-player-session`:

1. Preflight validates package support and the provider-owned content version.
2. The session snapshots Addressables settings and temporarily selects `DoNotBuildWithPlayer`, preventing an implicit second content build.
3. Addressables streaming-asset injection remains available for the already-built Player data.
4. `AddressablesVersionBuildProcessor` verifies `AddressablesVersion.json` in `Addressables.BuildPath` before Unity builds the Player.
5. Session disposal restores the original setting and exact serialized files.

When Addressables is installed but the selected Player recipe has no Addressables content invocation, the global environment guard also suppresses the package's streaming-asset callback. This prevents unselected or stale Addressables data from entering the Player.

Both paths are fail-closed and require saved configuration. Concurrent Addressables content or isolation sessions are rejected.

## Publication and CI contract

With publication enabled, the default layout is:

~~~text
<UnityProject>/Build/AddressablesContent/<invocation-id>/<BuildTarget>/
  PlayerData/
    AddressablesVersion.json
    ...
  RemoteContent/                 # present when reported by the build and configured
  BuildMetadata/                 # present when ContentStateFilePath is returned
    addressables_content_state.bin
  <AdditionalDestination>/      # optional
  AddressablesArtifacts.json
  .buildpipeline-owner.json
~~~

`AddressablesArtifacts.json` records format version, target, requested content version, incrementality, Unity version, active profile identity, Addressables player version, remote catalog load path, and every published file's kind, portable path, size, and SHA-256. The ownership document binds the publication to its transaction.

For CI:

1. restore the exact Unity Editor, locked Addressables package, saved Addressables settings, and BuildData assets;
2. run Workspace Health before a normal build;
3. activate the requested build target before invoking the pipeline;
4. use the canonical batch entry point and current namespaced options;
5. archive the complete publication, ownership document, artifact manifest, and terminal pipeline result manifest;
6. for a later Incremental job, restore the complete Clean publication at the configured project-relative location before Unity starts.

~~~text
"<UnityEditor>" -batchmode -quit \
  -projectPath "<repo-root>/UnityStarter" \
  -executeMethod Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine \
  -buildTarget "<BuildTarget>" \
  -pipelineProfile "Assets/<path-to-BuildData>.asset"
~~~

Use `-pipelineStepIncrementality "<invocation-id>=Incremental"` only for a configured focused Content Update job. Upload, retention, CDN promotion, and rollback are separate CI stages.

## Persistence and recovery

| Data | Location | Owner and lifecycle |
| --- | --- | --- |
| Addressables settings, groups, schemas, and profiles | Usually `Assets/AddressableAssetsData/` | Project-owned authoring; save and version-control according to project policy |
| `AddressablesBuildConfig` | Project-selected `.asset` below `Assets/` | Persistent typed BuildData input |
| Provider build cache/output | Evaluated Addressables build paths | Rebuildable provider data; not the release publication |
| Published artifact tree | Configured Publication Root | Durable build artifact, transaction-owned |
| Settings recovery state | `.buildpipeline/transactions/addressables-settings/` | Temporary durable snapshots; removed after verified restoration |
| Publication recovery state | `.buildpipeline/transactions/addressables/<invocation-id>/` | Temporary durable stage/backup journal; removed after terminal completion |
| Process lock | `Library/BuildPipeline/Addressables/build.lock` | Local serialization lock, not release evidence |
| Incremental baseline scratch | `Temp/BuildPipeline/Addressables/ContentUpdate/<invocation-id>/` | Ephemeral verified copy, deleted after the invocation |

After a hard interruption, do not manually delete journals, stage directories, backups, ownership files, or configuration snapshots. Open **Build > Pipeline > Workspace Health**, inspect the recorded paths, and run explicit recovery. CI uses the same canonical entry point with `-pipelineRecoverOnly`.

The recovery coordinator can restore settings and publications without the Addressables package or original BuildData profile. Unknown, corrupt, concurrently changed, or unowned state remains untouched and reports a blocked workspace for manual investigation.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Provider is unavailable | Install a compatible Addressables package and allow Editor compilation to finish. |
| Preflight reports a partial or unsupported API | Restore the audited package/API shape; do not bypass the reflection checks. |
| Unsaved configuration is reported | Save or revert every listed settings, group, schema, and profile asset. |
| Active target mismatch | Switch the Editor's active target before running the invocation. |
| Clean reports no usable `ClearCachedData` | Select a compatible active Addressables data builder. |
| Remote catalog files are missing | Verify both remote catalog paths and that `FileRegistry` reports the catalog data and matching `.hash`. |
| Incremental baseline is rejected | Restore the original publication and match target, profile, Unity version, remote load path, file identity, and manifest. |
| Player rejects Incremental content | Remove the Player dependency for that job or change the content invocation to Clean. |
| Publication source/output overlap is reported | Move the source or publication root; do not publish into a provider source tree. |
| Workspace requires recovery | Use Workspace Health or `-pipelineRecoverOnly` before retrying or changing output paths. |

## Validation boundary

EditMode tests cover provider registration, output claims, path policy, official API selectors, Content Update baseline and manifest checks, Player-hook isolation, settings restoration, transactional publication, crash checkpoints, ownership, and recovery failure paths.

Those tests and this source review do not prove an installed Addressables package, a vendor content build, a target Player build, runtime catalog loading, remote hosting, CDN behavior, managed stripping, IL2CPP, or platform-specific file-system behavior. Release qualification requires the locked optional package and, for each supported target, at least:

1. a Clean content build and dependent Player build;
2. inspection of the published manifest and exact file inventory;
3. runtime loading of local and configured remote content;
4. a clean-workspace Incremental build from an archived publication;
5. negative tests for changed target, profile, Unity version, remote load path, baseline bytes, and manifest bytes;
6. an interruption/recovery exercise before artifact promotion.

## Source index

- [AddressablesBuildConfig.cs](../../Authoring/Content/AddressablesBuildConfig.cs)
- [AddressablesBuildConfigEditor.cs](../../Authoring/Content/AddressablesBuildConfigEditor.cs)
- [AddressablesContentBuildAdapter.cs](AddressablesContentBuildAdapter.cs)
- [AddressablesBuilder.cs](AddressablesBuilder.cs)
- [AddressablesVersionBuildProcessor.cs](AddressablesVersionBuildProcessor.cs)
- [AddressablesPlayerBuildIsolation.cs](AddressablesPlayerBuildIsolation.cs)
- [AddressablesPlayerBuildEnvironmentGuard.cs](AddressablesPlayerBuildEnvironmentGuard.cs)
- [AddressablesSettingsTransaction.cs](AddressablesSettingsTransaction.cs)
- [AddressablesPublicationTransaction.cs](AddressablesPublicationTransaction.cs)
- [AddressablesRecoveryCoordinator.cs](AddressablesRecoveryCoordinator.cs)
- [AddressablesArtifactManifestFormat.cs](AddressablesArtifactManifestFormat.cs)
- [Build Foundation](../../../../README.md)
