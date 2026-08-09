# Performance Testing Build-Asset Guard

This integration protects project files that Unity Performance Testing 3.5.x temporarily creates and deletes around a Player build. It snapshots the exact pre-build state, adopts the package-generated image, and restores user-owned files and preference state after the package cleanup callback.

> **Current checkout:** `com.unity.test-framework.performance` is a direct dependency at `3.5.0` in both `Packages/manifest.json` and its direct `packages-lock.json` entry. The source gate audits the `3.5.x` contract. Transaction tests are present, but no real Player build or package callback sequence was executed as part of this documentation change.

## Responsibilities and boundaries

| Component | Responsibility |
| --- | --- |
| `PerformanceTestingPackageGate` | Detects the installed package and permits only the audited `3.5.x` version shape |
| `PerformanceTestingBuildAssetEarlyProcessor` | Starts protection before the package's order-zero preprocess callback |
| `PerformanceTestingBuildAssetLateProcessor` | Adopts generated assets after preprocess and restores originals after package cleanup |
| `PerformanceTestingBuildAssetTransaction` | Journals, snapshots, verifies, restores, and exposes readiness/recovery APIs |
| `PerformanceTestingBuildAssetRecoveryParticipant` | Registers the transaction directory with central Workspace Recovery |
| `PerformanceTestingBuildAssetReadiness` | Typed, read-only status for diagnostics and tooling |

The guard does not create or run performance tests, configure test metadata, select scenes, collect measurements, publish benchmark results, or change the Performance Testing package. It protects only the package's temporary Player-build asset behavior.

Because the processors implement Unity's global build callbacks, the guard wraps every Player build while Performance Testing 3.5.x is installed, including Player builds started outside the composable BuildData pipeline.

## Availability and package gate

No authoring switch enables this integration:

- package missing: both callbacks are no-ops;
- installed `3.5.x`: the guard is active;
- any other installed version shape: the Player build is blocked until the contract is reviewed and the guard is updated.

The current direct dependency is `3.5.0`. The gate strips prerelease/build suffixes and still requires exactly three numeric segments with major `3` and minor `5`. For example, `3.5.99-preview.1` is accepted by the source gate, while `3.4.9`, `3.6.0`, `3.5`, and non-version text are rejected.

Do not bypass the version gate. Package callback ordering and temporary asset behavior are the assumptions being protected.

## Typed contract and configuration

This integration intentionally has no `ScriptableObject`, BuildData card, serialized field, environment-variable toggle, or project preference of its own. Installation at an audited version is the complete activation contract.

Tooling can inspect:

| API | Meaning |
| --- | --- |
| `PerformanceTestingBuildAssetTransaction.InspectReadiness(projectRoot)` | Zero-write inspection when no state directory exists |
| `PerformanceTestingBuildAssetReadinessStatus.Clean` | No recovery evidence is pending |
| `PerformanceTestingBuildAssetReadinessStatus.RecoveryRequired` | Evidence is valid and explicit recovery can run |
| `PerformanceTestingBuildAssetReadinessStatus.Blocked` | Evidence or current files cannot be proven safe to recover |
| `PerformanceTestingBuildAssetTransaction.Recover(projectRoot)` | Explicit recovery entry used by the registered participant |

Normal project configuration remains in version-controlled BuildData and package manifests. The `PT_ResourcesCleanup` Editor preference belongs to Unity Performance Testing; this guard only snapshots, temporarily changes, and restores that vendor key. It is not used as module configuration or a source of build intent.

## Protected state

The transaction protects these exact paths:

~~~text
Assets/Resources/PerformanceTestRunInfo.json
Assets/Resources/PerformanceTestRunInfo.json.meta
Assets/Resources/PerformanceTestRunSettings.json
Assets/Resources/PerformanceTestRunSettings.json.meta
Assets/Resources.meta
~~~

It also records whether `Assets/Resources/` originally existed and whether `PT_ResourcesCleanup` originally existed, including its Boolean value.

Existing files are snapshotted with bounded reads and identity evidence. The implementation limits each protected file to 1 MiB and the aggregate original snapshot to 4 MiB. It rejects reparse points, unsafe roots, invalid journal inventory, changed file identities, and unknown directory entries rather than overwriting or deleting them.

If `Assets/Resources/` did not exist, the transaction creates it with a transaction-owned meta GUID. It removes that directory only when restoration proves that it is still the owned, empty directory. User-created or unknown entries stop cleanup.

## Build lifecycle

~~~mermaid
sequenceDiagram
    participant E as Early guard (int.MinValue)
    participant P as Performance Testing (order 0)
    participant L as Late guard (int.MaxValue)
    participant U as Unity Player build

    E->>E: Gate package and snapshot exact original state
    E->>E: Set PT_ResourcesCleanup=false and ensure Resources
    P->>P: Create temporary run-info/settings assets
    L->>L: Verify and adopt generated file identities
    L->>U: Continue BuildPlayer
    U-->>P: Enter postprocess callbacks
    P->>P: Run package cleanup
    L->>L: Restore originals, preference, and owned directory state
    L->>L: Verify restoration and remove journal evidence
~~~

The early processor uses `callbackOrder = int.MinValue`. It resets the in-memory ownership flag, checks the package version, refuses pending evidence, writes a durable journal and snapshots, temporarily sets `PT_ResourcesCleanup` to `false`, ensures the Resources directory, refreshes the Asset Database, and marks the current build as owned.

The late preprocess callback uses `int.MaxValue`. After the package's order-zero callback, it requires both generated JSON files, captures the generated JSON/meta identities, and advances the journal to `Adopted`.

The late postprocess callback also uses `int.MaxValue`. After package cleanup, it restores the exact pre-build files, file metadata, Resources-directory state, and vendor preference; refreshes assets; verifies the restored state again; then deletes only transaction-owned evidence.

If package callbacks produce an unexpected image or restoration cannot be proven safe, the build fails and durable evidence is retained.

## Persistence and recovery

| Data | Location | Lifecycle |
| --- | --- | --- |
| Protected user/package files | Exact `Assets/Resources...` paths listed above | Restored byte-for-byte with recorded metadata when originally present |
| Vendor cleanup preference | Editor preference key `PT_ResourcesCleanup` | Temporarily `false`; original presence/value restored |
| Durable journal root | `.buildpipeline/transactions/performance-testing/` | Active journal, lock, owner, and snapshot evidence; removed after verified completion |
| Transaction snapshots | Transaction-owned child below the journal root | Temporary durable recovery inputs |
| Committed build artifact | None | This guard does not publish content or Player output |

A normal new build never recovers prior evidence implicitly. Pending evidence causes the next `Begin` to fail and requires an explicit workspace operation.

After an Editor crash, agent termination, or machine interruption:

1. stop normal build retries;
2. open **Build > Pipeline > Workspace Health** and refresh the snapshot;
3. inspect the participant and evidence path;
4. run Recovery only when the snapshot reports `RecoveryRequired` and `CanRecover`;
5. re-run Workspace Health and require `Clean` before building again.

CI performs the same operation through `Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine` with `-pipelineRecoverOnly`. Do not manually delete the journal or protected files. A `Blocked` result means the current file image, preference, journal, or directory inventory cannot be reconciled safely; preserve the workspace for investigation.

## CI workflow

1. restore the Unity project and exact package lock;
2. verify the direct Performance Testing dependency remains in the audited `3.5.x` range;
3. run Workspace Health or a recovery-only job before a normal Player job;
4. invoke the canonical Build entry point with a version-controlled `-pipelineProfile` and required `-buildTarget`;
5. treat any protection, adoption, restoration, or terminal evidence failure as a failed Player job;
6. after success, verify no non-lock transaction evidence remains below the guard's journal root;
7. archive build logs and terminal result evidence, not the transient transaction directory.

The guard is automatic; CI must not create `PerformanceTestRunInfo.json` or `PerformanceTestRunSettings.json` itself, pre-set `PT_ResourcesCleanup` as a substitute for the transaction, or delete `Assets/Resources` after the build.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Build is blocked by an unsupported package version | Restore the audited `3.5.x` package or review and update the guard before upgrading. |
| Early protection could not start | Inspect pending evidence, reparse points, file size limits, and Resources/meta consistency. |
| Expected generated output is missing | Verify the installed package's callback behavior and stop; do not fabricate the JSON files. |
| Generated image is reported unsafe | Check for concurrent tools changing the protected files between callbacks. |
| Restoration fails and evidence is retained | Use Workspace Health; do not retry a normal build first. |
| Readiness is `Blocked` | Preserve the workspace and compare the journal, snapshots, protected files, preference, and unknown directory entries. |
| `Assets/Resources.meta` exists without the directory | Repair the orphaned project asset state deliberately before starting a build. |
| User files appeared in a transaction-created Resources directory | Move or review them manually; the guard deliberately refuses to delete the directory. |

## Validation boundary

EditMode tests cover the `3.5.x` package gate, exact round-trip restoration, originally absent Resources ownership, vendor preference restoration, explicit recovery, pending-evidence behavior, unknown concurrent file images, unknown directory entries, callback ordering, participant registration, and zero-write clean inspection.

The tests use temporary file-system fixtures and a fake preference store for transaction logic. The installed `3.5.0` dependency and static callback source do not prove a real Unity Player build, the actual vendor callback sequence, performance-test execution, result collection, target-platform behavior, domain reload behavior, or recovery after a real process crash.

Qualification for each supported Unity/package combination requires:

1. a clean Player build with Performance Testing installed;
2. a pre-existing-file round trip in a disposable project copy;
3. a project with no `Assets/Resources` directory;
4. an interruption after the early callback and explicit recovery;
5. a negative concurrent-change case that remains blocked without data loss;
6. confirmation that test-run metadata and benchmark results remain correct;
7. repetition on every supported build target and CI agent OS.

## Source index

- [PerformanceTestingBuildAssetTransaction.cs](PerformanceTestingBuildAssetTransaction.cs)
- [BuildWorkspaceService.cs](../../Core/Recovery/BuildWorkspaceService.cs)
- [BuildWorkspaceHealthWindow.cs](../../Presentation/BuildWorkspaceHealthWindow.cs)
- [BuildEntryPoints.cs](../../EntryPoints/BuildEntryPoints.cs)
- [PerformanceTestingBuildAssetTransactionTests.cs](../../../../Tests/Editor/PerformanceTestingBuildAssetTransactionTests.cs)
- [Build Pipeline manual](../../../../README.md)
