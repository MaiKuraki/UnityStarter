# Build.Pipeline Atlas Pipeline Integration

Thin `IBuildStep` adapter for the CycloneGames Atlas Pipeline. Registers the
`cyclonegames-atlas-pipeline` build step; add it to the `BuildData.asset` recipe
as a prerequisite of `asset-content`.

## Conditional Compilation

The whole integration assembly is gated by the `BUILD_PIPELINE_HAS_ATLAS_PIPELINE`
define, so the build step enables/disables itself automatically:

| How the pipeline is installed | Symbol source | Behavior |
|---|---|---|
| **UPM package** (`com.cyclone-games.atlas-pipeline` visible to the Package Manager) | `versionDefines` in this asmdef | Fully automatic. Package present → step compiled and registered via TypeCache. Package removed → assembly excluded, step vanishes, zero errors. |
| **Folder placed directly under `Assets/`** (not a package) | Manual define | The Package Manager cannot see `Assets/` folders, so `versionDefines` never fires. Add `BUILD_PIPELINE_HAS_ATLAS_PIPELINE` to *Project Settings > Player > Scripting Define Symbols* (Editor section works too). The assembly reference resolves normally because the pipeline asmdef exists in `Assets/`. |
| Pipeline absent entirely | None | Assembly excluded; no errors, no step. |

> Prefer the UPM form even for "vendored into the project" workflows: an embedded
> package (a folder under `Packages/` with a `package.json`) is committed with the
> repo, needs no registry, and keeps the integration fully automatic.
> Do not keep both a `Packages/` copy and an `Assets/` copy of the pipeline —
> duplicate assembly names will conflict.

## Re-enabling after mode switches

Switching install mode changes how the symbol is defined, which triggers an editor
recompile. The step appears/disappears in the BuildData recipe browser after that
recompile.
