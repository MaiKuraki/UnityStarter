# Unity Starter 工具

两个跨平台 Go 可执行文件，按职责在进程内承载全部仓库工具：`unity-project-tools`（Unity 项目工具）
与 `dev-tools`（通用媒体/文件工具）。不依赖 PowerShell、没有子进程启动器、运行时不下载
任何东西、使用预编译产物无需安装 Go。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## 概览

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

第一个参数选择工具；其余参数原样转发，每个工具返回可移植退出码（`0` 成功、`1` 失败、`2` 用法错误、
`130` 信号取消），因此两个二进制都可以安全嵌入任何平台的 CI 流水线。`<二进制> --help` 列出其全部命令，
`<二进制> <command> --help` 显示各命令专属参数。

## 目录结构

```text
Tools/
  Scripts/                         # Go module（单一语言、单一 module）
    go.mod / go.sum                # 锁定的依赖集合
    internal/
      toolkit/                     # 命令注册表 + 派发契约
      safefs/                      # build-tag 安全文件系统移动
    cmd/
      unity-project-tools/          # Unity 项目工具二进制
      dev-tools/          # 通用工具二进制
      toolsbuild/                  # 交叉编译发布打包器
    <tool>/                        # 每个工具一个 package，Run(args) int
  Executable/
    <OS>/<GOARCH>/                 # 预编译 unity-project-tools + dev-tools + toolsbuild
```

## 命令

### unity-project-tools（Unity 项目工具）

| 命令 | 用途 | 备注 |
| --- | --- | --- |
| `rename_project` | 事务化改名 UnityStarter 派生项目 | `--dry-run` 完整只读预演；`--project <path>` 显式指定项目根；带日志、备份与回滚；可重复执行——再次改名会复用持久化状态并启用全新回退探测 |
| `remove_unity_packages` | 从 `Packages/manifest.json` 删除显式授权的 Unity 包 | `--allow-package`、`--allow-referenced-package`、`--profile`、`--project <path>`、`--apply`、`--dry-run`、`--resolve-stale-transaction`；fail-closed |
| `unity_project_full_clean` | 删除已验证缓存与 Build 所有的输出 | `--ci`、`--dry-run`、`--project <path>`、`--include-build-outputs`；未被 marker 覆盖（非 Build 所有）的产物内容只跳过、绝不删除；交互模式输入 `CLEAN` 确认；Unity 运行中、存在恢复证据或 marker 格式非法时 fail-closed |

### dev-tools（通用工具）

| 命令 | 用途 | 备注 |
| --- | --- | --- |
| `generate_file_tree` | 生成 Markdown 目录树 | `--profile`、`--target`、`--depth`、`--ext`、`--ignore`、`--ci`、`-i` |
| `texture_channel_packer` | 把多张图打包进 RGBA 通道 | `-r/-g/-b/-a`、`-o`、`-size`、`-preset`、`-ci`、`--dry-run` |
| `audio_volume_normalizer` | 按类别做音频响度归一化 | `--ci --input <dir> [--format wav\|ogg] [--jobs N]`（CI 模式 `--input` 必填）；并行 worker 池（默认 CPU 数），Ctrl+C/SIGTERM 可取消；需要 FFmpeg |
| `unity_video_webm_converter` | 把视频转成 Unity 友好的 WebM | `--ci --input <file\|dir> --output <dir> [--preset 1\|2\|3] [--overwrite] [--jobs N] [--ffmpeg-timeout 2h]`；并行转换池、优雅取消；需要 FFmpeg |

### 项目根解析

Unity 项目类命令（`rename_project`、`remove_unity_packages`、`unity_project_full_clean`）
不会盲目信任当前工作目录，而是按以下顺序解析项目根：

1. 显式 `--project <path>`：严格校验，失败即报错，绝不回退到自动探测；
2. 当前目录本身是 Unity 项目根；
3. 当前目录的一级子目录中存在 Unity 项目根（覆盖"仓库根包含 `UnityStarter/`"的布局）；
4. 从当前目录向上逐级查找；
5. 从可执行文件所在目录向上逐级查找，并在每一级同时检查其一级子目录。

第 5 步是 Windows 双击可用的关键：Windows 会把工作目录设为可执行文件自身所在目录
（`Tools/Executable/<OS>/<GOARCH>/`），该目录并不是项目，于是改从可执行文件位置向上找到
仓库根及其 `UnityStarter/` 子目录。若始终找不到，命令会列出所有尝试过的目录并提示使用
`--project <path>`。交互式菜单会在顶部显示 `Current project: <path>`，找不到时给出同样的可操作提示。

每次解析都会记录**来源**（显式 `--project`、当前工作目录、工作目录的子目录、工作目录的祖先、
可执行文件目录回退）。来源之所以重要，是因为“可执行文件目录回退”具有歧义：从任意无关目录运行该
二进制，都会静默解析到随二进制一同发布的仓库。因此：

- 任何写入动作之前，所有命令都会记录 `resolved Unity project root … source=…`（预演里打印
  `Project: <root> (source: …)`），`--ci` 下同样如此；
- **破坏性且非交互**的运行（真实的 `unity_project_full_clean`、真实的 `rename_project`、
  `remove_unity_packages --apply` / 真实的 `--resolve-stale-transaction`）**拒绝**可执行文件回退
  来源，并给出可操作错误：请在项目根运行，或传 `--project <path>`；
- 交互式会话（双击菜单）允许该来源，因为它仍会先打印 `Current project: <path>` 并要求输入
  `CLEAN` 确认；
- `--dry-run` 允许该来源，但会打印醒目警告，说明项目根是被推断出来的。

## 安装

### 预编译产物（无需 Go）

`Tools/Executable/<OS>/<GOARCH>/` 下每个平台包都包含独立的 `unity-project-tools` 与 `dev-tools`
可执行文件，以及用于继续产出更多平台包的 `toolsbuild`。Windows、macOS、Linux 全部平台包均已随仓库提交；
也可以用一条命令重新生成（见下），或从 CI 工作流的 Artifacts 下载（每个平台 runner 都会上传其构建并验证过的平台包）。

在 Windows 上双击任一可执行文件都会打开其自身工具族的交互式命令菜单（按编号或名称选择工具，`q` 退出）；
带参数运行时仍是纯 CLI。带参数运行结束后，双击打开的控制台窗口会保留并显示"Press Enter to exit"提示，
方便查看成功/失败输出。该暂停仅在"进程独占一个新控制台且 stdin/stdout 均为交互终端"时发生；shell、
脚本、重定向输出与 CI 一律不会暂停。可用 `--no-pause` 参数或 `TOOLS_NO_PAUSE=1` 环境变量显式关闭。

### 从源码构建

```bash
cd Tools/Scripts
go build -mod=readonly -trimpath -buildvcs=false -o unity-project-tools.exe ./cmd/unity-project-tools
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
`Tools/Executable/windows/amd64/toolsbuild.exe --targets windows/amd64` 都能工作。从终端运行时与普通 CLI
程序行为一致；在 Windows 上双击运行时控制台会保留到按下回车（同样受上述 `--no-pause` / `TOOLS_NO_PAUSE=1` 约束）。

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
go run ./cmd/unity-project-tools --list
```

## 前置条件

- `audio_volume_normalizer` 与 `unity_video_webm_converter` 会在 `PATH` 上调用兼容的 FFmpeg；其余五个
  命令没有任何外部依赖。
- 交互式命令会等待确认；CI 场景请使用各自的 `--ci`/参数驱动模式。

## 设计说明

- **双二进制、进程内派发**：每个工具族每平台一个产物——Unity 项目工具与通用工具不共享二进制，所有平台体验一致。
- **确定性构建**：`-mod=readonly`、锁定 `go.sum`、`-trimpath`、`-buildvcs=false`。
- **Fail-closed**：破坏性工具保留日志/备份/租约安全机制，每个命令返回可移植退出码。
- **有界并行**：FFmpeg 类工具用有界 worker 池处理文件（`--jobs`，默认 CPU 数，收敛到 1..64），不再顺序执行或无限派生进程。
- **优雅取消**：所有长任务工具都基于 `signal.NotifyContext`；Ctrl+C/SIGTERM 会取消进行中的 FFmpeg 工作，并在输出干净汇总后以退出码 1 结束。
- **结构化日志**：诊断信息以 `slog` 文本行输出到 stderr（含 `cmd`、`level`、key=value），面向用户的提示、进度与汇总保持在 stdout，CI 日志可直接解析。
- **原子化输出**：FFmpeg 类工具先写入每次运行唯一的临时文件（同目录、同扩展名），成功后 rename 就位，中断或并发运行不会在最终路径留下半成品。
- **TTY 感知进度**：仅当 stdout 是交互终端时才绘制进度条；管道与 CI 输出保持干净。
- **所有权感知清理**：`unity_project_full_clean` 始终删除可重建缓存（`Library`、`Logs`、
  `obj`/`Obj`、`.vs`、`.utmp`、`Temp`），但产物目录（`Build`、`Bundles`、`HybridCLRData`、
  `yoo`、`HotUpdateAssetsPreUpload`）只有在显式传入 `--include-build-outputs` **且**存在有效
  Build ownership marker 覆盖时才会删除。未被 marker 覆盖的内容只报为跳过、绝不删除，因此
  手工产出的多余播放器产物不再阻塞缓存清理；而 marker 存在但格式非法时仍 fail-closed。
  `.buildpipeline` 与 `Temp/BuildPipeline/Workspace` 永不删除。
- **大小写不敏感缓存匹配**：Unity 生成的缓存目录大小写随版本与平台变化（程序集中间产物在
  某些版本是 `obj`、另一些是 `Obj`，与项目 `.gitignore` 的 `/[Oo]bj/` 一致），因此缓存根
  按大小写不敏感匹配，Windows/macOS/Linux 三平台清理同一集合。
- **Windows 双击体验**：带参数运行结束后，新开的控制台窗口会保留到按下回车。判定逻辑集中在
  toolkit 的 `ShouldPauseAfterRun`：仅当 stdin/stdout 均为交互终端、当前进程独占该控制台、且
  未通过 `--no-pause` 或 `TOOLS_NO_PAUSE=1` 显式退出时才暂停。管道、脚本、shell 与 CI 一律不暂停。
