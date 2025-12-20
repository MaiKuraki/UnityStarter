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

| 分类         | 工具                                         | 用途               |
| ------------ | -------------------------------------------- | ------------------ |
| **项目设置** | `rename_project`、`remove_unity_packages`    | 初始化和配置新项目 |
| **项目维护** | `unity_project_full_clean`                   | 清理临时文件和缓存 |
| **资源处理** | `audio_volume_normalizer`、`image_to_base64` | 处理和转换资源     |
| **文档生成** | `generate_file_tree`                         | 生成项目文档       |

## 快速参考

| 工具                         | 用途                                        | 何时使用                     | 位置       |
| ---------------------------- | ------------------------------------------- | ---------------------------- | ---------- |
| **rename_project**           | 重命名 Unity 项目（文件夹、公司、应用名称） | 从模板开始新项目时           | 项目根目录 |
| **remove_unity_packages**    | 从 manifest.json 移除不必要的包             | 创建最小化项目模板时         | 项目根目录 |
| **unity_project_full_clean** | 删除临时文件、缓存、构建产物                | 版本控制前、归档、故障排查   | 项目根目录 |
| **audio_volume_normalizer**  | 批量标准化音频文件至 -16 LUFS               | 处理音频资源以保持一致的响度 | 音频目录   |
| **image_to_base64**          | 将图像转换为 Base64 字符串                  | 在代码/配置文件中嵌入图像    | 任意位置   |
| **generate_file_tree**       | 生成 Markdown 目录树                        | 记录项目结构                 | 项目根目录 |

## 工具详情

### 1. 项目重命名 `rename_project.exe`

**用途**: 自动重命名 Unity 项目，更新所有相关配置文件。

**功能**:

- 重命名项目文件夹
- 更新 `ProjectSettings.asset` 中的公司名称
- 更新 `ProjectSettings.asset` 和 `EditorBuildSettings.asset` 中的应用名称
- 更新 `.meta` 文件引用
- 更新构建脚本引用

**使用场景**: 使用 UnityStarter 作为模板时，将其重命名为您的项目名称。

**要求**:

- Unity 项目根目录
- 写入权限

**使用方法**:

```bash
# 1. 将可执行文件放置在 Unity 项目根目录（与 Assets 文件夹同级）
# 2. 运行可执行文件
rename_project.exe

# 3. 按照交互式提示操作：
#    - 输入新的项目文件夹名称
#    - 输入新的公司名称
#    - 输入新的应用名称
#    - 确认更改
```

**更新的内容**:

- 项目文件夹名称
- `ProjectSettings/ProjectSettings.asset` (companyName, productName)
- `ProjectSettings/EditorBuildSettings.asset` (productName)
- `.meta` 文件（文件夹引用）
- 构建脚本引用（如果使用 UnityStarter Build 模块）

**安全性**: 在做出更改前会提示确认。可以安全地多次运行。

---

### 2. 移除 Unity 包 `remove_unity_packages.exe`

**用途**: 从 `Packages/manifest.json` 中移除不必要的 Unity 包，以创建最小化的项目模板。

**功能**:

- 读取 `Packages/manifest.json`
- 移除预定义的非必要包列表（Timeline、Visual Scripting 等）
- 将更新的清单写回文件

**使用场景**: 通过移除未使用的 Unity 包来创建最小化的项目模板。

**移除的包**（默认列表）:

- `com.unity.timeline`
- `com.unity.visualscripting`
- `com.unity.modules.physics`
- `com.unity.modules.physics2d`
- `com.unity.modules.terrain`
- 以及 20+ 个其他非必要包

**要求**:

- Unity 项目根目录
- `Packages/manifest.json` 文件存在
- 写入权限

**使用方法**:

```bash
# 标准模式（应用更改）
remove_unity_packages.exe

# 试运行模式（预览更改而不应用）
set DRY_RUN=1
remove_unity_packages.exe
```

**试运行模式**:

- 在运行前设置环境变量 `DRY_RUN=1`
- 显示将要移除的内容，但不修改文件
- 用于预览更改

**安全性**: 包含试运行模式。运行前始终备份 `manifest.json`。

---

### 3. Unity 项目完全清理 `unity_project_full_clean.exe`

**用途**: 对 Unity 项目执行深度清理，移除所有临时文件、缓存和构建产物。

**删除的内容**:

- **文件夹**: `Library/`、`Temp/`、`obj/`、`Build/`、`.vs/`、`.vscode/`、`Logs/` 等
- **文件**: `.sln`、`.csproj`、`.user`、`.vsconfig` 等
- **构建产物**: 所有生成的构建输出

**使用场景**:

- 提交到版本控制之前（减小仓库大小）
- 归档项目之前
- 故障排查 Unity 问题（干净状态）
- 准备项目分发

**要求**:

- Unity 项目根目录
- **必须关闭 Unity 编辑器**（会检查运行中的实例）
- 写入权限

**使用方法**:

```bash
# 1. 关闭 Unity 编辑器（工具会检查并在检测到运行时警告）
# 2. 将可执行文件放置在 Unity 项目根目录
# 3. 运行可执行文件
unity_project_full_clean.exe

# 工具将：
# - 检查 Unity 是否正在运行（如果检测到会警告）
# - 列出要删除的文件/文件夹
# - 提示确认
# - 使用并发工作线程删除（快速）
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

文件:
- *.sln             (解决方案文件)
- *.csproj          (C# 项目文件)
- *.user            (用户设置)
- *.vsconfig        (VS 配置)
```

**安全性**:

- 检查运行中的 Unity 编辑器
- 删除前列出所有项目
- 需要确认
- **警告**: 这是破坏性操作。确保 Unity 编辑器已关闭并已备份。

---

### 4. 音频响度标准化 `audio_volume_normalizer.exe`

**用途**: 使用 FFmpeg 批量标准化音频文件到一致的响度水平。

**功能**:

- 递归扫描目录中的音频文件
- 将每个文件标准化为 **-16.0 LUFS**（响度单位全标度）
- 将真实峰值设置为 **-1.5 dBTP**
- 创建带有 `_normalized` 后缀的新 `.ogg` 文件
- 跳过已在目标范围内的文件

**使用场景**: 处理音频资源以确保所有文件具有一致的音量水平。

**要求**:

- **FFmpeg** 已安装并可在系统 PATH 中访问
- 目录中的音频文件（支持：`.wav`、`.mp3`、`.ogg`、`.flac` 等）
- 写入权限

**使用方法**:

```bash
# 1. 安装 FFmpeg 并添加到 PATH
#    从以下地址下载: https://ffmpeg.org/download.html

# 2. 将可执行文件放置在包含音频文件的目录中
cd /path/to/audio/files

# 3. 运行可执行文件
audio_volume_normalizer.exe

# 工具将：
# - 递归扫描音频文件
# - 显示要处理的文件预览
# - 提示确认
# - 处理每个文件（创建 _normalized.ogg 文件）
# - 显示摘要（已处理、已跳过、失败）
```

**输出**:

- 原始文件: `sound_effect.wav`
- 标准化文件: `sound_effect_normalized.ogg`

**特性**:

- 双通道 FFmpeg 处理流程，实现准确的标准化
- 跳过已在目标范围内的文件（节省时间）
- 保留原始文件（创建新的标准化版本）
- 详细的摘要报告

**安全性**: 创建新文件，从不修改原始文件。可以安全地多次运行。

---

### 5. 图像转 Base64 `image_to_base64.exe`

**用途**: 将图像文件转换为 Base64 编码字符串，以便嵌入代码或配置文件。

**功能**:

- 读取图像文件（支持常见格式）
- 编码为 Base64 字符串
- 自动将字符串复制到剪贴板
- 将字符串保存到 `.txt` 文件作为备份

**使用场景**:

- 在代码中嵌入图像（数据 URI）
- 在 JSON/YAML 配置文件中存储图像
- 创建内联图像资源

**要求**:

- 图像文件路径（支持拖放）
- 剪贴板访问权限

**使用方法**:

```bash
# 1. 运行可执行文件
image_to_base64.exe

# 2. 提示时，将图像文件拖放到终端
#    或输入/粘贴文件路径

# 3. Base64 字符串自动：
#    - 复制到剪贴板
#    - 保存到 {filename}_base64.txt
```

**输出**:

- 剪贴板: Base64 字符串（准备粘贴）
- 文件: `{original_filename}_base64.txt`（备份）

**支持的格式**: `.png`、`.jpg`、`.jpeg`、`.gif`、`.bmp`、`.webp` 等。

**安全性**: 只读操作。从不修改原始图像。

---

### 6. 生成文件树 `generate_file_tree.exe`

**用途**: 生成表示项目目录结构的 Markdown 文件。

**功能**:

- 递归扫描当前目录
- 使用白名单（扩展名）和黑名单（文件夹）过滤文件
- 生成带有树状视图的 `directory_structure.md`
- 折叠排除的目录以获得更清晰的输出

**使用场景**:

- 记录项目结构
- 创建 README 文件树
- 可视化代码库组织

**特性**:

- **白名单**: 仅包含指定的文件扩展名（`.cs`、`.md`、`.go` 等）
- **黑名单**: 排除常见文件夹（`.git/`、`node_modules/`、`Library/` 等）
- **折叠**: 隐藏排除的目录以获得更清晰的视图

**要求**:

- 目录的读取权限
- 输出文件的写入权限

**使用方法**:

```bash
# 1. 将可执行文件放置在要映射的目录中
cd /path/to/project

# 2. 运行可执行文件
generate_file_tree.exe

# 3. 输出文件已创建: directory_structure.md
```

**输出示例**:

```markdown
.
├── Assets/
│ ├── Scripts/
│ │ └── Player.cs
│ └── Scenes/
│ └── Main.unity
├── Packages/
└── README.md
```

**自定义**: 编辑 Go 源代码以修改白名单/黑名单过滤器。

**安全性**: 只读操作。仅创建新文件，从不修改现有文件。

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

**场景**: 您有一个需要一致音量的音效文件夹。

```bash
# 步骤 1: 导航到音频目录
cd Assets/Audio/SoundEffects

# 步骤 2: 运行标准化器
audio_volume_normalizer.exe

# 步骤 3: 查看摘要
# - 15 个文件已处理
# - 3 个文件已跳过（已标准化）
# - 0 个文件失败

# 步骤 4: 在项目中使用标准化文件
# 文件: sound_effect_normalized.ogg
```

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
