# Unity Starter Tools

Two cross-platform Go executables that carry every repository tool in-process, split by domain:
`unity-project-tools` (Unity project tools) and `dev-tools` (general-purpose media/file tools).
No PowerShell, no child-process launchers, no runtime downloads, no Go installation required to run
prebuilt binaries.

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

## Overview

```bash
unity-project-tools --list
unity-project-tools rename_project --dry-run
unity-project-tools remove_unity_packages --allow-package com.unity.2d.sprite --dry-run
unity-project-tools unity_project_full_clean --dry-run

dev-tools --list
dev-tools generate_file_tree --ci --depth 2 --target . --o tree.md
dev-tools unity_video_webm_converter --ci --input Assets/Movies --output Assets/Movies/webm --jobs 8
dev-tools audio_volume_normalizer --ci --input Assets/Audio --format ogg --jobs 8
```

The first token selects a tool; every following argument is forwarded unchanged and each tool returns a
portable exit code (`0` success, `1` failure, `2` usage error, `130` cancelled by signal), which makes
both binaries safe to embed in CI pipelines on every platform. `<binary> --help` lists its commands and
`<binary> <command> --help` shows command-specific flags.

## Layout

```text
Tools/
  Scripts/                         # Go module (single language, single module)
    go.mod / go.sum                # pinned dependency set
    internal/
      toolkit/                     # command registry + dispatch contract
      safefs/                      # build-tagged safe filesystem moves
    cmd/
      unity-project-tools/          # Unity project tools binary
      dev-tools/          # general-purpose tools binary
      toolsbuild/                  # cross-compile release bundler
    <tool>/                        # one package per tool, Run(args) int
  Executable/
    <OS>/<GOARCH>/                 # prebuilt unity-project-tools + dev-tools + toolsbuild
```

## Commands

### unity-project-tools (Unity project tools)

| Command | Purpose | Notes |
| --- | --- | --- |
| `rename_project` | Rename a UnityStarter-derived project transactionally | `--dry-run` for a complete read-only plan; journaled with backups and rollback; re-runnable — renaming again reuses the persisted state and fresh fallback detection |
| `remove_unity_packages` | Remove explicitly authorized Unity packages from `Packages/manifest.json` | `--allow-package`, `--allow-referenced-package`, `--profile`, `--apply`, `--dry-run`; fails closed |
| `unity_project_full_clean` | Delete verified caches and Build-owned outputs | `--ci`, `--dry-run`, `--include-build-outputs`; interactive mode types `CLEAN` to confirm; fails closed while Unity is running or recovery evidence exists |

### dev-tools (general-purpose tools)

| Command | Purpose | Notes |
| --- | --- | --- |
| `generate_file_tree` | Generate a Markdown directory tree | `--profile`, `--target`, `--depth`, `--ext`, `--ignore`, `--ci`, `-i` |
| `texture_channel_packer` | Pack images into RGBA texture channels | `-r/-g/-b/-a`, `-o`, `-size`, `-preset`, `-ci`, `--dry-run` |
| `audio_volume_normalizer` | Category-aware audio loudness normalization | `--ci --input <dir> [--format wav\|ogg] [--jobs N]` (`--input` required in CI mode); parallel worker pool (default CPU count), Ctrl+C/SIGTERM cancel; requires FFmpeg |
| `unity_video_webm_converter` | Convert videos to Unity-friendly WebM | `--ci --input <file\|dir> --output <dir> [--preset 1\|2\|3] [--overwrite] [--jobs N]`; parallel conversion pool, graceful cancel; requires FFmpeg |

## Install

### Prebuilt binaries (no Go required)

Each platform bundle under `Tools/Executable/<OS>/<GOARCH>/` contains the standalone
`unity-project-tools` and `dev-tools` executables plus `toolsbuild` for producing further
bundles. On Windows, double-clicking either executable opens an interactive command menu for its
own tool family (pick a tool by number or name, `q` to quit); running them with arguments keeps
the pure CLI behavior. The Windows bundle is
committed; macOS and Linux bundles are produced with one command (below) or downloaded from the CI
workflow's Artifacts (each platform runner uploads the bundle it built and verified).

### Build from source

```bash
cd Tools/Scripts
go build -mod=readonly -trimpath -buildvcs=false -o unity-project-tools.exe ./cmd/unity-project-tools
```

The module declares `go 1.25.0` because the destructive-filesystem tools rely on the Go 1.25 `os.Root`
APIs. Any Go installation 1.21 or newer builds it transparently: the default `GOTOOLCHAIN=auto` mode
downloads the required toolchain on first use. `go.sum` pins the only third-party dependency.

### Release bundles (local or CI)

```bash
cd Tools/Scripts
go run ./cmd/toolsbuild                         # all default targets
go run ./cmd/toolsbuild --targets windows/amd64,darwin/arm64,linux/amd64
go run ./cmd/toolsbuild --verify               # also smoke-test the current platform binary
```

`toolsbuild` cross-compiles static binaries (`CGO_ENABLED=0`, `-trimpath`, `-buildvcs=false`, stripped)
into `Tools/Executable/<OS>/<GOARCH>/` and exits non-zero on any failure, so it is directly usable as a
CI release step without any scripting layer. The distributed `toolsbuild` executable is also runnable
on its own: it locates the module root from its own path (not the working directory), so a terminal
command like `Tools/Executable/windows/amd64/toolsbuild.exe --targets windows/amd64` works from any
directory. It is a CLI program whose console window closes as soon as it finishes, so run it from a
terminal to see its output.

## CI/CD

The workflow at `.github/workflows/unitystarter-tools.yml` runs on every push/PR touching `Tools/`
(plus manual dispatch). Two jobs run in parallel:

- `build`: hosted Ubuntu/Windows/macOS runners build and vet the module, check `gofmt` cleanliness,
  run `go test`, produce and verify the current-platform bundle through `toolsbuild --verify`, run the
  smoke commands, and upload the per-platform bundle as a workflow Artifact (download from the
  workflow run page, kept for 30 days).
- `linux-distros`: the same checks run inside real Debian (bookworm) and Arch Linux containers,
  covering both a stable baseline distribution and a rolling-release distribution.

Runs auto-cancel on newer pushes (`concurrency`), are capped at 25 minutes, and never short-circuit
on the first platform failure (`fail-fast: false`). The same core commands work verbatim on
Jenkins, TeamCity, GitLab CI, or a local shell:

```bash
go build ./... && go vet ./...
go run ./cmd/toolsbuild --targets "$(go env GOOS)/$(go env GOARCH)" --verify
go run ./cmd/unity-project-tools --list
```

## Prerequisites

- `audio_volume_normalizer` and `unity_video_webm_converter` shell out to a compatible FFmpeg binary on
  `PATH`; the other five commands have no external dependencies.
- Interactive commands pause for confirmation unless run with their `--ci`/flag-driven modes, which are
  the supported CI paths.

## Design Notes

- **Two binaries, in-process dispatch**: one artifact per tool family per platform; the Unity
  project tools and the general-purpose tools never share a binary, and the UX is identical everywhere.
- **Deterministic builds**: `-mod=readonly`, pinned `go.sum`, `-trimpath`, `-buildvcs=false`.
- **Fails closed**: destructive tools keep their journal/backup/lease safety machinery, and every command
  returns a portable exit code.
- **Bounded parallelism**: the FFmpeg tools process files through a bounded worker pool (`--jobs`, default
  CPU count, clamped to 1..64) instead of running sequentially or forking unbounded processes.
- **Graceful cancellation**: every long-running tool derives from `signal.NotifyContext`; Ctrl+C/SIGTERM
  cancels in-flight FFmpeg work and exits with code 1 after a clean summary.
- **Structured logs**: diagnostics go to stderr as `slog` text lines (`cmd`, `level`, key=value) while
  user-facing prompts, progress, and summaries stay on stdout, so CI logs stay parseable.
- **Atomic outputs**: the FFmpeg tools write to a unique per-run temporary file (same directory, same
  extension) and rename it into place on success, so interrupted or concurrent runs never leave a
  half-written file at the final path.
- **TTY-aware progress**: progress bars are drawn only when stdout is an interactive terminal; piped
  and CI output stays clean.
