# Unity Starter 工具集

一系列独立的实用工具脚本，旨在简化 Unity 开发工作流和项目管理任务。所有工具均使用 Go 编写，并提供适用于 Windows 的预编译可执行文件。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## 目录

1. [概述](#概述)
2. [快速参考](#快速参考)
3. [工具详情](#工具详情)
4. [安装与设置](#安装与设置)
5. [使用示例](#使用示例)

## 概述

这些工具自动化 Unity 开发中常见但繁琐的任务：

- **项目设置**: 重命名项目、清理包
- **项目维护**: 深度清理临时文件和缓存
- **资源处理**: 标准化音频、转换图像
- **文档生成**: 生成项目结构树

所有工具都是**独立可执行文件** - 无需安装。只需下载并运行。

### 工具分类

| 分类         | 工具                                                | 用途               |
| ------------ | --------------------------------------------------- | ------------------ |
| **项目设置** | `rename_project`、`remove_unity_packages`           | 初始化和配置新项目 |
| **项目维护** | `unity_project_full_clean`                          | 清理临时文件和缓存 |
| **资源处理** | `audio_volume_normalizer`、`texture_channel_packer` | 处理和转换资源     |
| **文档生成** | `generate_file_tree`                                | 生成项目文档       |

## 快速参考

| 工具                         | 用途                                        | 何时使用                         | 位置       |
| ---------------------------- | ------------------------------------------- | -------------------------------- | ---------- |
| **rename_project**           | 重命名 Unity 项目（文件夹、公司、应用名称） | 从模板开始新项目时               | 项目根目录 |
| **remove_unity_packages**    | 从 manifest.json 移除不必要的包             | 创建最小化项目模板时             | 项目根目录 |
| **unity_project_full_clean** | 删除临时文件、缓存、构建产物                | 版本控制前、归档、故障排查       | 项目根目录 |
| **audio_volume_normalizer**  | 批量标准化音频文件（分类别响度目标）        | 处理音频资源以保持一致的响度     | 音频目录   |
| **texture_channel_packer**   | 将多张图片打包到一张纹理的 RGBA 通道        | 创建 HDRP/URP Mask Map、打包纹理 | 任意位置   |
| **generate_file_tree**       | 生成 Markdown 目录树                        | 记录项目结构                     | 项目根目录 |

## 工具详情

### 1. 项目重命名 `rename_project.exe`

**用途**: 自动重命名 Unity 项目，更新所有相关配置文件。专为**安全可重复执行**设计，适用于任何基于 UnityStarter 的派生项目。

**功能**:

- 重命名项目文件夹（`Assets/旧名称` → `Assets/新名称`）
- 使用词边界匹配更新 `.asmdef` 文件（名称、引用）
- 更新 `Assets/` 下**所有** `.asmdef` 的引用（不仅限于项目文件夹内）
- 精确匹配 `BuildScript.cs` 中的常量声明
- 更新 `ProjectSettings.asset`（companyName、productName、applicationIdentifier）
- 更新 `EditorBuildSettings.asset`（场景路径）
- 更新 `.meta` 文件引用

**核心特性**:

- **状态文件**（`.rename_project.json`）：每次重命名后记录当前项目标识，确保后续运行时可靠检测
- **自动备份**：修改前自动创建所有受影响文件的时间戳备份（保留最近 5 次）
- **变更预览**：执行前显示所有计划变更的详细预览
- **即时输入验证**：输入时立即验证每个名称，而非确认后才验证
- **保留当前值**：任何提示中按 Enter 即可保留当前值不变
- **双输出日志**：所有操作同时输出到控制台和 `rename_project.log`
- **精确替换**：asmdef 使用词边界正则（`\b`），BuildScript.cs 使用精确常量匹配，ProjectSettings 使用精确的 Bundle ID 匹配
- **部分失败恢复**：执行过程中保存状态检查点，即使部分失败后重新运行也能正确恢复

**使用场景**: 使用 UnityStarter 作为模板时，将其重命名为您的项目名称。后续可安全地再次运行以更改名称。

**要求**:

- Unity 项目根目录
- 写入权限

**使用方法**:

```bash
# 1. 将可执行文件放置在 Unity 项目根目录（与 Assets 文件夹同级）
# 2. 运行可执行文件
rename_project.exe

# 3. 按照交互式提示操作：
#    步骤 1：输入新的项目文件夹名称（按 Enter 保留当前值）
#    步骤 2：输入新的公司名称（按 Enter 保留当前值）
#    步骤 3：输入新的应用名称（按 Enter 保留当前值）
#    查看变更预览 → 确认 (y/N)
```

**更新的内容**:

- 项目文件夹名称 + `.meta`
- `.asmdef` 文件：名称字段、引用、文件名（词边界安全匹配）
- `Assets/Build/Editor/BuildPipeline/BuildScript.cs`（CompanyName、ApplicationName 常量）
- `ProjectSettings/ProjectSettings.asset`（companyName、productName、所有平台的 applicationIdentifier、metroPackageName、metroApplicationDescription）
- `ProjectSettings/EditorBuildSettings.asset`（场景路径前缀）

**生成的文件**:

- `.rename_project.json` — 状态文件，用于可靠的重复运行（建议提交到版本控制）
- `.rename_backup/` — 时间戳备份目录（建议添加到 `.gitignore`）
- `rename_project.log` — 操作日志

**安全性**:

- 修改前自动备份
- 执行前完整的变更预览
- 拒绝覆盖已存在的目标文件夹（无 `os.RemoveAll`）
- 文件夹重命名后保存状态检查点，确保部分失败后也能安全重新运行
- 词边界匹配防止意外的子字符串替换
- asmdef 修改后进行 JSON 验证

---

### 2. 移除 Unity 包 `remove_unity_packages.exe`

**用途**: 从 `Packages/manifest.json` 中移除不必要的 Unity 包，以创建最小化的项目模板。

**功能**:

- 读取 `Packages/manifest.json` 并展示可移除的包
- 将包分为 8 个分类（Physics、AI、XR、Visual Scripting 等）
- 使用基于文本的替换保留 JSON key 顺序（干净的 git diff）
- 修改前自动创建 `.bak` 备份
- 执行前显示分类预览

**包分类**（8 个分类共 24 个包）:

| 分类 | 包 |
|------|----|
| 2D | `com.unity.2d.tilemap` |
| AI / 导航 | `com.unity.ai.navigation`、`com.unity.modules.ai` |
| 物理 | `com.unity.modules.physics`、`physics2d`、`cloth`、`vehicles`、`wind`、`terrain`、`terrainphysics` |
| 可视化脚本 / 时间线 | `com.unity.timeline`、`com.unity.visualscripting` |
| XR / VR | `com.unity.modules.vr`、`com.unity.modules.xr` |
| 分析 / 服务 | `collab-proxy`、`multiplayer.center`、`unityanalytics` |
| 测试 | `com.unity.test-framework` |
| 其他模块 | `accessibility`、`jsonserialize`、`tilemap`、`uielements`、`umbra`、`video` |

**交互模式**（选择要移除的分类）:

```bash
remove_unity_packages -i
```

**命令行模式**:

```bash
# 移除所有列表中的包（含预览 + 确认）
remove_unity_packages

# 预演模式：仅预览不修改
remove_unity_packages --dry-run

# CI 模式：无提示，移除全部
remove_unity_packages --ci

# 列出所有可移除的包
remove_unity_packages --list
```

**参数**:

| 参数 | 说明 |
|------|------|
| `-i` | 交互式分类选择 |
| `--dry-run` | 仅预览，不修改 |
| `--ci` | 非交互模式 |
| `--list` | 列出所有可移除的包并退出 |

**安全特性**:

- **Unity 项目验证** — 操作前验证
- **自动 `.bak` 备份** manifest.json
- **分类分组预览** — 执行前展示
- **保序 JSON 编辑** — 不打乱 key 顺序
- **确认提示** — 默认: No
- **packages-lock.json 提示** — 提醒打开 Unity 重新生成
- 兼容旧版 `DRY_RUN=1` 环境变量

---

### 3. Unity 项目完全清理 `unity_project_full_clean.exe`

**用途**: 对 Unity 项目执行深度清理，移除所有临时文件、缓存和构建产物。

**删除的内容**:

- **文件夹**: `Library/`、`Temp/`、`obj/`、`Build/`、`.vs/`、`.vscode/`、`Logs/` 等
- **文件**: `.sln`、`.csproj`、`.user`、`.vsconfig`
- **构建产物**: 所有生成的构建输出

**使用场景**:

- 提交到版本控制之前（减小仓库大小）
- 归档项目之前
- 故障排查 Unity 问题（干净状态）
- 准备项目分发

**核心特性**:

- **Unity 项目验证**：删除前验证当前目录是否为 Unity 项目
- **可靠的进程检测**：通过 `EditorInstance.json` + 实际 PID 验证检查 Unity 是否运行中（跨平台）
- **带大小的变更预览**：执行前显示每个待删除项及其大小
- **试运行模式**：`--dry-run` 参数只预览不删除
- **CI 模式**：`--ci` 参数用于非交互自动化
- **并发删除**：使用多个工作线程加速 I/O 密集型清理
- **健壮重试**：通过递归 chmod + 重试处理只读文件和瞬态文件锁
- **删除统计**：显示已删除项数、失败数、释放空间和耗时

**要求**:

- Unity 项目根目录（验证 `Assets/` 和 `ProjectSettings/` 存在）
- **必须关闭 Unity 编辑器**（主动验证进程是否运行，而非仅依据文件存在）
- 写入权限

**使用方法**:

```bash
# 标准模式（交互式）
unity_project_full_clean.exe

# 预览模式（查看将被删除的内容，不做更改）
unity_project_full_clean.exe --dry-run

# CI 模式（非交互式，无需确认）
unity_project_full_clean.exe --ci
```

**删除的内容**:

```
文件夹:
- Library/          (Unity 缓存)
- Temp/             (临时文件)
- obj/              (构建对象)
- Build/            (构建输出)
- .vs/              (Visual Studio 缓存)
- .vscode/          (VS Code 缓存)
- Logs/             (Unity 日志)
- HybridCLRData/    (HybridCLR 缓存)
- Bundles/          (资源包)
- 以及更多...

文件（仅根目录）:
- *.sln             (解决方案文件)
- *.csproj          (C# 项目文件)
- *.user            (用户设置)
- *.vsconfig        (VS 配置)
```

**安全性**:

- 删除前验证 Unity 项目结构
- 验证 Unity 编辑器进程实际存活（非仅依据过期的锁文件）
- 执行前显示详细预览及文件大小
- 需要明确的 `y` 确认（默认为不执行）
- **警告**: 这是破坏性操作。确保 Unity 编辑器已关闭并已备份。

---

### 4. 音频响度标准化 `audio_volume_normalizer.exe`

**用途**: 使用 FFmpeg 批量标准化音频文件到一致的响度水平，针对游戏音频场景优化。

**功能**:

- 递归扫描目录中的音频文件
- **自动检测音频类别**：根据父文件夹名称（Music、SFX、Voice、Ambient）
- **双策略归一化**：长音频（≥ 3s）使用 LUFS 响度归一化，短音效（< 3s）使用峰值归一化
- **可选输出格式**：WAV（无损）或 OGG（Vorbis VBR）
- 使用两遍线性模式 loudnorm，确保最高质量
- 跳过已在目标范围内的文件
- 每文件超时保护，防止损坏文件导致卡死

**类别自动检测**:

工具根据音频文件的父文件夹名称自动识别类别（不区分大小写）：

| 类别   | 匹配文件夹名            | LUFS 目标  | 真实峰值  | 峰值目标（短音效） |
| ------ | ----------------------- | ---------- | --------- | ------------------ |
| 音乐   | `music`, `bgm`          | -14.0 LUFS | -1.0 dBTP | -1.0 dB            |
| 语音   | `voice`, `dialog`, `vo` | -16.0 LUFS | -1.5 dBTP | -1.0 dB            |
| 音效   | `sfx`, `se`, `sound`    | -14.0 LUFS | -1.0 dBTP | -1.0 dB            |
| 环境音 | `ambient`, `env`        | -20.0 LUFS | -1.5 dBTP | -3.0 dB            |
| 默认   | _（其他）_              | -16.0 LUFS | -1.5 dBTP | -1.0 dB            |

**归一化策略**:

- **长音频（≥ 3s）**：两遍 LUFS 归一化 + `linear=true` — 在调整感知响度的同时保留动态范围。适用于音乐、对话和环境音。
- **短音频（< 3s）**：峰值归一化 — LUFS（ITU-R BS.1770）需要 ≥ 400ms 才能产生可靠测量，不适用于按钮点击、脚步声、枪声等短音效。

**输出格式选择**:

启动时可选择输出格式：

| 格式 | 编码            | 适用场景                                          |
| ---- | --------------- | ------------------------------------------------- |
| WAV  | PCM 16-bit 无损 | **推荐**：无损输出，避免 Unity 导入时双重有损压缩 |
| OGG  | Vorbis VBR q6   | 磁盘空间紧张或 Git 仓库体积需要控制时             |

> **说明**：WAV 与 OGG 源文件对 Unity **运行时的内存和 CPU 没有任何区别** — Unity 导入时会根据 AudioClip Import Settings 重新编码所有音频。详见 [音频最佳实践指南](../Docs/AudioBestPractices/AudioBestPractices.md)。

**使用场景**: 处理音频资源以确保所有文件具有一致的音量水平，使用针对游戏音频优化的分类别响度目标。

**要求**:

- **FFmpeg** 已安装并可在系统 PATH 中访问
- 目录中的音频文件（支持：`.wav`、`.mp3`、`.ogg`、`.flac`、`.m4a`、`.aac`、`.wma`、`.opus`）
- 写入权限

**使用方法**:

```bash
# 1. 安装 FFmpeg 并添加到 PATH
#    从以下地址下载: https://ffmpeg.org/download.html

# 2. 将可执行文件放置在包含音频文件的目录中
#    按类别文件夹组织文件：
#    Audio/
#    ├── Music/        (自动检测为 音乐)
#    ├── SFX/          (自动检测为 音效)
#    ├── Voice/        (自动检测为 语音)
#    └── Ambient/      (自动检测为 环境音)
cd /path/to/audio/files

# 3. 运行可执行文件
audio_volume_normalizer.exe

# 4. 选择输出格式（1=WAV, 2=OGG）
# 5. 确认执行

# 工具将：
# - 从文件夹名称自动检测音频类别
# - 根据音频时长选择归一化策略
# - 处理过程中显示进度条
# - 显示摘要（已处理、已跳过、失败及错误详情）
```

**输出**:

- 原始文件: `SFX/gunshot.wav`
- 标准化文件: `SFX/gunshot_normalized.wav`（或 `.ogg`）

**特性**:

- 类别感知的响度目标（从文件夹结构自动检测）
- 双策略：长音频用 LUFS，短音效用 Peak
- 两遍 FFmpeg 处理 + `linear=true` 确保最高质量
- 可选输出格式（WAV 无损 / OGG Vorbis VBR）
- 跳过已标准化文件和静音文件
- 每文件超时保护（10 分钟），处理损坏文件
- 并发处理 + 进度条显示
- 剥离源文件元数据，防止编码问题（WAV 不支持 UTF-8 元数据，非 ASCII 标签会变乱码）
- 详细摘要报告（含失败文件的错误信息）

**安全性**: 创建新文件，从不修改原始文件。可以安全地多次运行。

---

### 5. 纹理通道打包 `texture_channel_packer.exe`

**用途**: 将多张源图片打包到一张输出纹理的 R/G/B/A 通道中。用于创建 HDRP/URP Mask Map 和其他打包纹理。

**功能**:

- 将最多 4 张源图片（或常量填充值）合并为一张 RGBA 纹理
- 从每张源图中提取指定通道（R/G/B/A/Gray）
- 自动从源图检测输出分辨率
- 源图尺寸不同时使用最近邻插值缩放
- 内置 HDRP Mask Map 和 URP Mask Map 预设
- 内存高效：逐张加载源图，顺序处理通道

**使用场景**:

- **HDRP/URP Mask Map**: Metallic(R) + AO(G) + DetailMask(B) + Smoothness(A)
- 将多张灰度图打包为单张 RGBA 纹理
- 减少纹理数量（4 张 → 1 张 = 减少 75% Draw Call）
- CI/CD 纹理管线自动化

**交互模式**（双击运行或不带参数）:

```bash
# 启动引导向导，可选择预设
texture_channel_packer.exe
```

**命令行模式**:

```bash
# 自定义通道打包
texture_channel_packer -r metallic.png -g ao.png -a smoothness.png:A -o mask_map.png --ci

# 使用预设标签（用于预览显示）
texture_channel_packer -r metallic.png -g ao.png -b detail.png -a smoothness.png -o mask.png --preset hdrp-mask --ci

# 预览模式（不写入文件）
texture_channel_packer -r metallic.png -g ao.png --dry-run

# 用常量值填充通道
texture_channel_packer -r metallic.png -g fill:128 -b 0 -a 255 -o packed.png --ci

# 强制输出尺寸
texture_channel_packer -r metallic.png -g ao.png -size 2048x2048 -o mask.png --ci
```

**通道指定符**: 在文件路径后追加 `:R`、`:G`、`:B`、`:A` 或 `:Gray`（默认: Gray）。

**参数**:

| 参数                   | 说明                                                |
| ---------------------- | --------------------------------------------------- |
| `-r`、`-g`、`-b`、`-a` | 各通道源: `file.png[:channel]`、`fill:N` 或 `0-255` |
| `-o`                   | 输出文件路径（默认: `packed.png`）                  |
| `-size`                | 强制输出尺寸 `WxH`（默认: 自动检测）                |
| `-preset`              | 使用预设标签: `hdrp-mask`、`urp-mask`               |
| `--ci`                 | 非交互模式（无确认提示）                            |
| `--dry-run`            | 仅预览，不写入文件                                  |

**支持输入**: PNG、JPEG。**输出**: PNG（无损、保留 Alpha 通道）。

**性能**: 直接像素访问、顺序通道处理、缓冲 I/O、BestSpeed PNG 编码。

**安全性**: 对源图只读。执行前预览。支持预演模式。

---

### 6. 生成文件树 `generate_file_tree.exe`

**用途**: 生成表示项目目录结构的 Markdown 文件，支持多种详细程度配置。

**功能**:

- 递归扫描目标目录，可配置深度限制
- 基于 Profile 的扩展名白名单过滤文件
- 内置 4 种 Profile 对应不同详细程度
- 读取 `.treeignore` 文件实现项目级自定义排除
- 排序输出：目录优先，然后文件，按字母排序
- 被过滤内容显示 `...` 指示符

**Profile 预设**:

| Profile    | 描述               | 显示文件                                            | 深度 |
| ---------- | ------------------ | --------------------------------------------------- | ---- |
| `minimal`  | 快速概览           | 仅文件夹                                            | 3 层 |
| `standard` | 代码与文档（默认） | `.go`、`.cs`、`.md`、`.json`、`.yaml`、`.shader` 等 | 无限 |
| `detailed` | 代码 + Unity 资源  | standard + `.asset`、`.prefab`、`.unity`、`.mat` 等 | 无限 |
| `full`     | 全部文件           | 不过滤                                              | 无限 |

**交互模式**（使用 `-i` 运行）:

```bash
generate_file_tree -i
```

**命令行模式**:

```bash
# 默认: standard profile, 当前目录
generate_file_tree

# 指定 profile 和深度
generate_file_tree -profile minimal -depth 4

# 扫描指定目录
generate_file_tree -target ./Assets -profile detailed -o assets_tree.md

# 自定义扩展名（覆盖 profile）
generate_file_tree -ext .cs,.shader,.hlsl -o code_tree.md

# 添加额外忽略项
generate_file_tree -ignore ThirdParty,Plugins

# 显示文件大小和隐藏项数量
generate_file_tree -profile full --show-size --show-count

# CI 模式（无提示、无等待）
generate_file_tree -profile standard --ci
```

**`.treeignore` 文件**（放在目标目录下）:

```
# 目录（带尾部斜杠）
ThirdParty/
InControl/

# 文件扩展名
*.meta
*.asset

# 精确名称
temp
```

**参数**:

| 参数           | 说明                                                                    |
| -------------- | ----------------------------------------------------------------------- |
| `-profile`     | `minimal`、`standard`、`detailed`、`full`（默认: standard）             |
| `-target`      | 目标目录（默认: 当前目录）                                              |
| `-o`           | 输出文件（默认: `directory_structure.md`，或 `FILE_TREE_OUT` 环境变量） |
| `-depth`       | 最大深度，0=无限（默认: 来自 profile）                                  |
| `-ext`         | 文件扩展名，逗号分隔（覆盖 profile）                                    |
| `-ignore`      | 额外忽略的目录/名称，逗号分隔                                           |
| `-i`           | 交互模式，带 profile 选择菜单                                           |
| `--dirs-only`  | 仅显示目录                                                              |
| `--show-size`  | 显示文件大小                                                            |
| `--show-count` | 在 `...` 行显示隐藏项数量                                               |
| `--ci`         | 非交互模式                                                              |

**输出示例**:

````markdown
# Directory Structure

- **Generated**: 2026-04-02 12:00:00
- **Profile**: standard

​`
MyProject/
├── Assets/
│   ├── Scripts/
│   │   ├── Player.cs
│   │   └── Enemy.cs
│   ├── Scenes/
│   └── ...
├── Tools/
│   └── Scripts/
│       └── generate_file_tree.go
└── README.md
​`
````

**安全性**: 对源目录只读。仅创建输出文件。

## 安装与设置

### 获取工具

**选项 1: 使用预编译可执行文件（推荐）**

1. 导航到 `Tools/Executable/Windows/`
2. 复制您需要的 `.exe` 文件
3. 将它们放置在您 desired 的位置
4. 直接运行（无需安装）

**选项 2: 从源代码构建**

1. 安装 [Go](https://golang.org/dl/) (1.16+)
2. 导航到 `Tools/Scripts/`
3. 构建:
   ```bash
   go build -o rename_project.exe rename_project.go
   go build -o remove_unity_packages.exe remove_unity_packages.go
   # ... 等等，为每个工具构建
   ```

### 前置条件

**所有工具**:

- Windows 操作系统（提供可执行文件）
- 适当的文件权限

**特定工具**:

- **audio_volume_normalizer**: 需要系统 PATH 中的 [FFmpeg](https://ffmpeg.org/download.html)
- **rename_project**: 需要 Unity 项目结构
- **remove_unity_packages**: 需要 `Packages/manifest.json`
- **unity_project_full_clean**: 需要 Unity 项目结构

### 设置 FFmpeg（用于音频标准化器）

1. 从[官方网站](https://ffmpeg.org/download.html)下载 FFmpeg
2. 解压到某个位置（例如，`C:\ffmpeg\`）
3. 添加到系统 PATH:
   - 在 Windows 中打开"环境变量"
   - 编辑"Path"变量
   - 添加 FFmpeg `bin` 目录（例如，`C:\ffmpeg\bin`）
4. 验证: 打开命令提示符并运行 `ffmpeg -version`

## 使用示例

### 示例 1: 设置新项目

**场景**: 您克隆了 UnityStarter 并想将其重命名为 "MyGame"。

```bash
# 步骤 1: 重命名项目
cd UnityStarter
rename_project.exe
# 输入: MyGame (项目名称)
# 输入: MyCompany (公司名称)
# 输入: MyGame (应用名称)
# 确认: 是

# 步骤 2: 移除不必要的包（可选）
remove_unity_packages.exe
# 查看列表，确认移除

# 步骤 3: 清理项目
unity_project_full_clean.exe
# 首先关闭 Unity 编辑器，然后运行
```

### 示例 2: 处理音频资源

**场景**: 您有按类别组织的音频文件，需要保持一致的响度。

```bash
# 步骤 1: 按类别文件夹组织音频文件
# Assets/Audio/
# ├── BGM/           -> 音乐目标 (-14 LUFS)
# ├── SFX/           -> 音效目标 (-14 LUFS / 短片段峰值 -1.0 dB)
# ├── Voice/         -> 语音目标 (-16 LUFS)
# └── Ambient/       -> 环境音目标 (-20 LUFS)

# 步骤 2: 导航到音频根目录
cd Assets/Audio

# 步骤 3: 运行标准化器
audio_volume_normalizer.exe
# 选择输出格式: 1 (WAV) 或 2 (OGG)
# 确认: 是

# 步骤 4: 查看摘要
# - 15 个文件已处理（类别从文件夹名称自动检测）
# - 3 个文件已跳过（已标准化）
# - 2 个文件使用峰值归一化（短音效，< 3s）
# - 0 个文件失败

# 步骤 5: 在项目中使用标准化文件
# gunshot_normalized.wav (或 .ogg)
```

关于 Unity 音频导入设置和优化推荐的详细信息，请参阅 [音频最佳实践指南](../Docs/AudioBestPractices/AudioBestPractices.md)。

### 示例 3: 生成项目文档

**场景**: 您想在 README 中记录项目结构。

```bash
# 步骤 1: 导航到项目根目录
cd /path/to/project

# 步骤 2: 生成文件树
generate_file_tree.exe

# 步骤 3: 查看输出
# 文件已创建: directory_structure.md

# 步骤 4: 将相关部分复制到 README.md
```

### 示例 4: 准备项目进行版本控制

**场景**: 您想在提交到 Git 之前清理项目。

```bash
# 步骤 1: 关闭 Unity 编辑器（重要！）

# 步骤 2: 运行完全清理
unity_project_full_clean.exe

# 步骤 3: 验证 .gitignore 排除：
# - Library/
# - Temp/
# - Build/
# - *.csproj
# - *.sln

# 步骤 4: 提交清理后的项目
git add .
git commit -m "初始项目设置"
```

## 最佳实践

### 1. 运行前备份

- **rename_project**: 备份 `ProjectSettings/` 文件夹
- **remove_unity_packages**: 备份 `Packages/manifest.json`
- **unity_project_full_clean**: 确保 Unity 编辑器已关闭

### 2. 使用试运行模式

对于 `remove_unity_packages`，始终先用试运行测试：

```bash
set DRY_RUN=1
remove_unity_packages.exe
```

### 3. 关闭 Unity 编辑器

运行前始终关闭 Unity 编辑器：

- `unity_project_full_clean`（必需）
- `rename_project`（推荐）
- `remove_unity_packages`（推荐）

### 4. 验证前置条件

- 检查音频标准化器的 FFmpeg 安装
- 验证文件权限
- 确保您在正确的目录中

### 5. 仔细阅读输出

所有工具都提供：

- 更改预览
- 确认提示
- 摘要报告

确认前始终查看。

## 故障排查

### 工具未找到 / 不可执行

**问题**: 可执行文件无法运行或未找到。

**解决方案**:

- 确保您使用的是 `Tools/Executable/Windows/` 中的 Windows 可执行文件
- 检查文件权限（右键 > 属性 > 如有需要则取消阻止）
- 尝试使用完整路径从命令提示符运行

### FFmpeg 未找到（音频标准化器）

**问题**: `audio_volume_normalizer` 失败，提示 "FFmpeg not found"。

**解决方案**:

- 从[官方网站](https://ffmpeg.org/download.html)安装 FFmpeg
- 将 FFmpeg `bin` 目录添加到系统 PATH
- 在命令提示符中使用 `ffmpeg -version` 验证
- 添加到 PATH 后重启命令提示符

### Unity 编辑器正在运行（完全清理）

**问题**: `unity_project_full_clean` 警告 Unity 正在运行。

**解决方案**:

- 完全关闭 Unity 编辑器
- 检查任务管理器中的 Unity 进程
- 关闭后等待几秒钟，然后重试

### 权限被拒绝

**问题**: 工具无法写入文件或删除文件夹。

**解决方案**:

- 以管理员身份运行（右键 > 以管理员身份运行）
- 检查文件/文件夹权限
- 确保文件未被其他进程锁定
- 关闭 Unity 编辑器和其他使用这些文件的应用程序

### 未找到项目根目录（重命名项目）

**问题**: `rename_project` 找不到 Unity 项目。

**解决方案**:

- 将可执行文件放置在 Unity 项目根目录（与 `Assets/` 文件夹同级）
- 确保 `ProjectSettings/` 文件夹存在
- 使用 `cd` 命令检查当前目录
