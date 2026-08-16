# Unity Starter 工具

一个跨平台 Go 可执行文件（`unitystarter_tools`），在进程内承载全部仓库工具。
不依赖 PowerShell、没有子进程启动器、运行时不下载任何东西、使用预编译产物无需安装 Go。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## 概览

```bash
unitystarter_tools --list
unitystarter_tools rename_project --dry-run
unitystarter_tools remove_unity_packages --allow-package com.unity.2d.sprite --dry-run
unitystarter_tools unity_project_full_clean --dry-run
unitystarter_tools generate_file_tree --ci --depth 2 --target . --o tree.md
unitystarter_tools unity_video_webm_converter --ci --input Assets/Movies --output Assets/Movies/webm --jobs 8
unitystarter_tools audio_volume_normalizer --ci --input Assets/Audio --format ogg --jobs 8
```

第一个参数选择工具；其余参数原样转发，每个工具返回可移植退出码（`0` 成功、`1` 失败、`2` 用法错误、
`130` 信号取消），因此二进制可以安全嵌入任何平台的 CI 流水线。`unitystarter_tools --help` 列出全部命令，
`unitystarter_tools <command> --help` 显示各命令专属参数。

## 目录结构

```text
Tools/
  Scripts/                         # Go module（单一语言、单一 module）
    go.mod / go.sum                # 锁定的依赖集合
    internal/
      toolkit/                     # 命令注册表 + 派发契约
      safefs/                      # build-tag 安全文件系统移动
    cmd/
      unitystarter_tools/          # 唯一发布的二进制
      toolsbuild/                  # 交叉编译发布打包器
    <tool>/                        # 每个工具一个 package，Run(args) int
  Executable/
    <OS>/<GOARCH>/                 # 预编译 unitystarter_tools + toolsbuild
```

## 命令

| 命令 | 用途 | 备注 |
| --- | --- | --- |
| `rename_project` | 事务化改名 UnityStarter 派生项目 | `--dry-run` 完整只读预演；带日志、备份与回滚；可重复执行——再次改名会复用持久化状态并启用全新回退探测 |
| `remove_unity_packages` | 从 `Packages/manifest.json` 删除显式授权的 Unity 包 | `--allow-package`、`--allow-referenced-package`、`--profile`、`--apply`、`--dry-run`；fail-closed |
| `unity_project_full_clean` | 删除已验证缓存与 Build 所有的输出 | `--ci`、`--dry-run`、`--include-build-outputs` |
| `generate_file_tree` | 生成 Markdown 目录树 | `--profile`、`--target`、`--depth`、`--ext`、`--ignore`、`--ci`、`-i` |
| `texture_channel_packer` | 把多张图打包进 RGBA 通道 | `-r/-g/-b/-a`、`-o`、`-size`、`-preset`、`-ci`、`--dry-run` |
| `audio_volume_normalizer` | 按类别做音频响度归一化 | `--ci --input <dir> [--format wav\|ogg] [--jobs N]`（CI 模式 `--input` 必填）；并行 worker 池（默认 CPU 数），Ctrl+C/SIGTERM 可取消；需要 FFmpeg |
| `unity_video_webm_converter` | 把视频转成 Unity 友好的 WebM | `--ci --input <file\|dir> --output <dir> [--preset 1\|2\|3] [--overwrite] [--jobs N]`；并行转换池、优雅取消；需要 FFmpeg |

## 安装

### 预编译产物（无需 Go）

`Tools/Executable/<OS>/<GOARCH>/` 下每个平台包都包含独立的 `unitystarter_tools` 可执行文件以及用于
继续产出更多平台包的 `toolsbuild`。Windows 包随仓库提交；macOS/Linux 包用一条命令即可生成（见下），
或从 CI 工作流的 Artifacts 下载（每个平台 runner 都会上传其构建并验证过的平台包）。

### 从源码构建

```bash
cd Tools/Scripts
go build -mod=readonly -trimpath -buildvcs=false -o unitystarter_tools.exe ./cmd/unitystarter_tools
```

模块声明 `go 1.25.0`，因为破坏性文件系统工具依赖 Go 1.25 的 `os.Root` API。任何 1.21 及以上的 Go
安装都能透明构建：默认的 `GOTOOLCHAIN=auto` 会在首次使用时自动下载所需工具链。`go.sum` 锁定唯一
第三方依赖。

### 发布打包（本地或 CI）

```bash
cd Tools/Scripts
go run ./cmd/toolsbuild                         # 全部默认目标
go run ./cmd/toolsbuild --targets windows/amd64,darwin/arm64,linux/amd64
go run ./cmd/toolsbuild --verify               # 顺带冒烟测试当前平台产物
```

`toolsbuild` 交叉编译静态二进制（`CGO_ENABLED=0`、`-trimpath`、`-buildvcs=false`、strip）到
`Tools/Executable/<OS>/<GOARCH>/`，任何失败都返回非零，可直接作为 CI 发布步骤使用，无需任何脚本层。
分发的 `toolsbuild` 可执行文件也可独立运行：它按自身所在路径（而非工作目录）定位模块根，因此在任意目录下执行
`Tools/Executable/windows/amd64/toolsbuild.exe --targets windows/amd64` 都能工作。它是 CLI 程序，控制台窗口
会在结束后立即关闭，请在终端中运行以查看输出。

## CI/CD

`.github/workflows/unitystarter-tools.yml` 会在每次触及 `Tools/` 的 push/PR（以及手动触发）时运行，
两个 job 并行：

- `build`：托管的 Ubuntu/Windows/macOS runner 构建并 vet 模块、检查 `gofmt` 清洁度、运行 `go test`、
  通过 `toolsbuild --verify` 产出并验证当前平台包、运行冒烟命令，并把各平台包上传为工作流 Artifact
  （在工作流运行页下载，保留 30 天）。
- `linux-distros`：同样的检查在真实的 Debian（bookworm）与 Arch Linux 容器内再跑一遍，同时覆盖
  稳定基线发行版与滚动更新发行版。

运行采用 `concurrency` 自动取消旧推送、25 分钟超时上限、`fail-fast: false`（不会因首个平台失败而截断
其余平台的结果）。同样的核心命令可在 Jenkins、TeamCity、GitLab CI 或本地 shell 原样运行：

```bash
go build ./... && go vet ./...
go run ./cmd/toolsbuild --targets "$(go env GOOS)/$(go env GOARCH)" --verify
go run ./cmd/unitystarter_tools --list
```

## 前置条件

- `audio_volume_normalizer` 与 `unity_video_webm_converter` 会在 `PATH` 上调用兼容的 FFmpeg；其余五个
  命令没有任何外部依赖。
- 交互式命令会等待确认；CI 场景请使用各自的 `--ci`/参数驱动模式。

## 设计说明

- **单二进制、进程内派发**：每平台一个产物，所有平台体验一致。
- **确定性构建**：`-mod=readonly`、锁定 `go.sum`、`-trimpath`、`-buildvcs=false`。
- **Fail-closed**：破坏性工具保留日志/备份/租约安全机制，每个命令返回可移植退出码。
- **有界并行**：FFmpeg 类工具用有界 worker 池处理文件（`--jobs`，默认 CPU 数，收敛到 1..64），不再顺序执行或无限派生进程。
- **优雅取消**：所有长任务工具都基于 `signal.NotifyContext`；Ctrl+C/SIGTERM 会取消进行中的 FFmpeg 工作，并在输出干净汇总后以退出码 1 结束。
- **结构化日志**：诊断信息以 `slog` 文本行输出到 stderr（含 `cmd`、`level`、key=value），面向用户的提示、进度与汇总保持在 stdout，CI 日志可直接解析。
- **原子化输出**：FFmpeg 类工具先写入每次运行唯一的临时文件（同目录、同扩展名），成功后 rename 就位，中断或并发运行不会在最终路径留下半成品。
- **TTY 感知进度**：仅当 stdout 是交互终端时才绘制进度条；管道与 CI 输出保持干净。
