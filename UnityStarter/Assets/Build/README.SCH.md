[**English**](README.md) | [**简体中文**]

# Build 模块文档

Build 模块为 Unity 项目提供全面、灵活的构建管线。它支持完整应用构建、代码（通过 HybridCLR）和资源（通过 YooAsset 或 Addressables）的热更新，以及无缝的 CI/CD 集成。系统采用模块化设计，允许您仅使用需要的功能。

## 目录

1. [概述](#概述)
2. [前置条件](#前置条件)
3. [快速上手](#快速上手)
4. [核心概念](#核心概念)
5. [配置](#配置)
6. [构建工作流](#构建工作流)
7. [CI/CD 集成](#cicd-集成)
8. [故障排查](#故障排查)

## 概述

Build 模块由几个关键组件组成：

- **BuildData**: 中央配置 ScriptableObject（所有构建都需要）
- **BuildScript**: 完整应用构建管线
- **HotUpdateBuilder**: 代码和资源的统一热更新工作流
- **HybridCLR 集成**: C# 代码热更新支持（可选）
- **YooAsset 集成**: 资源管理和热更新（可选）
- **Addressables 集成**: Unity 官方资源管理（可选）
- **Buildalon 集成**: 构建自动化辅助工具（可选）

### 主要特性

- ✅ **灵活的包支持**: 可与可选包（HybridCLR、YooAsset、Addressables、Buildalon）配合使用，也可不使用
- ✅ **自动版本控制**: 基于 Git 的版本生成
- ✅ **多平台支持**: 支持 Windows、Mac、Android、iOS、WebGL
- ✅ **热更新就绪**: 代码和资源热更新的完整解决方案
- ✅ **CI/CD 友好**: 用于自动化构建的命令行接口
- ✅ **配置驱动**: 所有设置通过 ScriptableObject 资产

## 前置条件

### 必需

- **Unity 2022.3+**
- **Git**（用于自动版本控制）

### 可选包

Build 系统支持以下可选包。仅安装您需要的包：

- **[HybridCLR](https://github.com/focus-creative-games/hybridclr)** - 用于 C# 代码热更新
- **[YooAsset](https://github.com/tuyoogame/YooAsset)** - 轻量级资源管理系统
- **[Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@latest)** - Unity 官方资源管理（通过 Package Manager）
- **[Buildalon](https://github.com/virtualmaker/Buildalon)** - 构建自动化辅助工具

> **注意**: Build 系统使用反射来检测可选包。如果未安装某个包，相关功能将自动禁用。不会出现编译错误。

## 快速上手

### 步骤 1: 创建 BuildData 资产

**BuildData 是所有构建所必需的。** 您必须为每个项目手动创建此资产。

1. 在 Unity 编辑器中，在项目窗口中右键单击
2. 选择 **Create > CycloneGames > Build > BuildData**
3. 将其命名为 `BuildData`（或您喜欢的任何名称）
4. 将其放置在项目中有意义的位置（例如，`Assets/Config/BuildData.asset`）

> **⚠️ 重要**: 项目中应该只存在**一个** BuildData 资产。系统会自动找到并使用它。

### 步骤 2: 配置 BuildData

选择 BuildData 资产并在 Inspector 中配置：

**基本设置:**

- **Launch Scene**: 将用作构建入口点的场景
- **Application Version**: 版本前缀（例如，`v0.1`）。最终版本将为 `{ApplicationVersion}.{CommitCount}`
- **Output Base Path**: 构建结果的基础目录（相对于项目根目录，例如，`Build`）

**构建管线选项:**

- **Use Buildalon**: 如果已安装 Buildalon 包并想使用其辅助工具，请启用
- **Use HybridCLR**: 如果已安装 HybridCLR 包并想要代码热更新，请启用

**资源管理系统:**

- **None**: 无资源管理（资源直接构建到播放器中）
- **YooAsset**: 使用 YooAsset 进行资源管理和热更新
- **Addressables**: 使用 Unity Addressables 进行资源管理和热更新

### 步骤 3: 创建其他配置资产（如果需要）

根据您选择的选项，您可能需要其他配置资产：

#### 如果使用 HybridCLR

1. 在项目窗口中右键单击
2. 选择 **Create > CycloneGames > Build > HybridCLR Build Config**
3. 配置 HybridCLR 特定设置

#### 如果使用 YooAsset

1. 在项目窗口中右键单击
2. 选择 **Create > CycloneGames > Build > YooAsset Build Config**
3. 配置 YooAsset 特定设置（包版本、构建输出等）

#### 如果使用 Addressables

1. 在项目窗口中右键单击
2. 选择 **Create > CycloneGames > Build > Addressables Build Config**
3. 配置 Addressables 特定设置（内容版本、远程目录等）

> **注意**: 这些配置资产是可选的。如果未找到它们，系统将使用默认值，但建议创建它们以进行正确配置。

### 步骤 4: 构建您的项目

配置 BuildData 后，您可以使用以下方式构建：

**Unity 编辑器菜单:**

- **Build > Game(Release) > Build Android APK (IL2CPP)**
- **Build > Game(Release) > Build Windows (IL2CPP)**
- **Build > Game(Release) > Build Mac (IL2CPP)**
- **Build > Game(Release) > Build WebGL**

**或使用热更新管线:**

- **Build > HotUpdate Pipeline > Full Build (Generate Code + Bundles)**
- **Build > HotUpdate Pipeline > Fast Build (Compile Code + Bundles)**

## 核心概念

### BuildData

`BuildData` 是整个构建系统的中央配置资产。它包含：

- **Launch Scene**: 构建的入口点场景
- **Application Version**: 自动版本控制的版本前缀
- **Output Base Path**: 构建输出的基础目录
- **功能标志**: 启用/禁用可选功能（HybridCLR、Buildalon）
- **资源管理选择**: 在 YooAsset、Addressables 或 None 之间选择

**关键点:**

- ✅ **必需**: 必须存在才能使任何构建工作
- ✅ **单一实例**: 项目中应该只存在一个 BuildData
- ✅ **自动发现**: 系统使用 `AssetDatabase.FindAssets` 自动查找 BuildData
- ✅ **手动创建**: 您必须手动创建此资产（无自动生成）

### 版本系统

构建系统使用 Git 进行自动版本控制：

- **格式**: `{ApplicationVersion}.{CommitCount}`
- **示例**: 如果 `ApplicationVersion = "v0.1"` 且有 123 个提交，最终版本为 `v0.1.123`
- **版本信息**: Git 提交哈希、提交计数和构建日期保存到 `VersionInfoData` ScriptableObject
- **运行时访问**: 版本信息可通过 `VersionInfoData` 资产在运行时访问

### 构建脚本

#### BuildScript

用于完整应用构建的主构建脚本。处理：

- 多平台构建（Windows、Mac、Android、WebGL）
- 自动版本控制
- 可选的 HybridCLR 代码生成
- 可选的资源包构建（YooAsset/Addressables）
- 清理构建选项
- 调试文件管理

#### HotUpdateBuilder

用于热更新构建的统一管线。提供两种模式：

- **Full Build**: 完整的代码生成 + 资源打包
  - `HybridCLR -> GenerateAllAndCopy` + `资源管理 -> Build Bundles`
  - 当 C# 代码结构发生变化或需要干净构建时使用
- **Fast Build**: 快速 DLL 编译 + 资源打包
  - `HybridCLR -> CompileDLLAndCopy` + `资源管理 -> Build Bundles`
  - 当仅方法实现发生变化时使用，支持快速迭代

### 可选包集成

Build 系统使用反射来检测和集成可选包：

- **HybridCLR**: 通过 `HybridCLR.Editor.Commands.PrebuildCommand` 类型检测
- **YooAsset**: 通过 `YooAsset.Editor.AssetBundleBuilder` 类型检测
- **Addressables**: 通过 `UnityEditor.AddressableAssets.Build` 命名空间检测
- **Buildalon**: 通过 `VirtualMaker.Buildalon` 命名空间检测

如果未安装某个包，相关功能将自动禁用，不会出现编译错误。

## 配置

### BuildData 配置

**位置**: 选择 BuildData 资产时的 Inspector

**字段:**

| 字段                  | 类型       | 描述                               | 必需  |
| --------------------- | ---------- | ---------------------------------- | ----- |
| Launch Scene          | SceneAsset | 构建的入口点场景                   | ✅ 是 |
| Application Version   | string     | 版本前缀（例如，"v0.1"）           | ✅ 是 |
| Output Base Path      | string     | 输出的基础目录（相对于项目根目录） | ✅ 是 |
| Use Buildalon         | bool       | 启用 Buildalon 辅助工具            | ❌ 否 |
| Use HybridCLR         | bool       | 启用 HybridCLR 代码热更新          | ❌ 否 |
| Asset Management Type | enum       | None / YooAsset / Addressables     | ❌ 否 |

**验证:**

BuildData 编辑器提供实时验证：

- ✅ 检查是否分配了 Launch Scene
- ✅ 验证 Application Version 格式
- ✅ 检查 Output Base Path 是否存在或可以创建
- ✅ 当启用功能时警告缺少可选配置
- ✅ 为每个资源管理选项显示有用的消息

### HybridCLR Build Config

**何时创建**: 如果 BuildData 中 `Use HybridCLR = true`

**位置**: **Create > CycloneGames > Build > HybridCLR Build Config**

**关键设置:**

- HybridCLR 安装路径
- 代码生成选项
- DLL 编译设置

> **注意**: 有关详细配置，请参阅 HybridCLR 文档。Build 系统提供围绕 HybridCLR 构建命令的包装器。

### YooAsset Build Config

**何时创建**: 如果 BuildData 中 `Asset Management Type = YooAsset`

**位置**: **Create > CycloneGames > Build > YooAsset Build Config**

**关键设置:**

- **Package Version**: 资源包的版本（应与 BuildData ApplicationVersion 匹配）
- **Build Output Directory**: 输出资源包的位置
- **Copy to StreamingAssets**: 是否将包复制到 StreamingAssets
- **Copy to Output Directory**: 是否将包复制到构建输出目录

**版本对齐:**

YooAsset 配置编辑器提供版本对齐警告：

- ⚠️ 如果 Package Version 与 BuildData ApplicationVersion 不匹配，则警告
- ✅ 建议匹配版本以保持一致性
- 💡 提供快速修复按钮以对齐版本

### Addressables Build Config

**何时创建**: 如果 BuildData 中 `Asset Management Type = Addressables`

**位置**: **Create > CycloneGames > Build > Addressables Build Config**

**关键设置:**

- **Content Version**: Addressables 内容的版本（应与 BuildData ApplicationVersion 匹配）
- **Build Remote Catalog**: 是否为 CDN 托管构建远程目录
- **Copy to Output Directory**: 是否将内容复制到构建输出目录
- **Build Output Directory**: 输出 Addressables 内容的位置

**版本对齐:**

与 YooAsset 类似，Addressables 配置编辑器提供版本对齐警告和建议。

## 构建工作流

### 完整应用构建

**目的**: 构建用于分发的完整应用程序

**工作流:**

1. 加载 BuildData 配置
2. 从 Git 生成版本信息
3. （可选）如果启用，运行 HybridCLR 代码生成
4. （可选）如果启用资源管理，构建资源包
5. 构建 Unity 播放器
6. 将版本信息保存到 `VersionInfoData` 资产
7. （可选）将资源包复制到输出目录

**菜单项:**

- `Build > Game(Release) > Build Android APK (IL2CPP)`
- `Build > Game(Release) > Build Windows (IL2CPP)`
- `Build > Game(Release) > Build Mac (IL2CPP)`
- `Build > Game(Release) > Build WebGL`

**输出:**

- 构建的应用程序在 `{OutputBasePath}/{Platform}/{ApplicationName}.{ext}`
- 版本信息在 `Assets/Resources/VersionInfoData.asset`

### 热更新 - 完整构建

**目的**: 完整的热更新构建（代码生成 + 资源打包）

**何时使用:**

- C# 代码结构已更改（新类、方法等）
- 需要从头开始干净构建
- 首次设置热更新

**工作流:**

1. 加载 BuildData
2. **HybridCLR**: 生成所有代码和元数据（`GenerateAllAndCopy`）
3. **资源管理**: 构建所有资源包
4. 输出热更新文件

**菜单项**: `Build > HotUpdate Pipeline > Full Build (Generate Code + Bundles)`

**输出:**

- HybridCLR DLL 在 `HybridCLRData/DllOutput/`
- 资源包在配置的输出目录中

### 热更新 - 快速构建

**目的**: 快速热更新构建（DLL 编译 + 资源打包）

**何时使用:**

- 仅方法实现已更改（无结构更改）
- 开发期间的快速迭代
- 快速错误修复

**工作流:**

1. 加载 BuildData
2. **HybridCLR**: 仅编译 DLL（`CompileDLLAndCopy`）
3. **资源管理**: 构建资源包
4. 输出热更新文件

**菜单项**: `Build > HotUpdate Pipeline > Fast Build (Compile Code + Bundles)`

**输出:**

- 编译的 HybridCLR DLL
- 更新的资源包

### 独立构建操作

您也可以运行单独的构建操作：

**HybridCLR:**

- `Build > HybridCLR > Generate All`

**YooAsset:**

- `Build > YooAsset > Build Bundles (From Config)`

**Addressables:**

- `Build > Addressables > Build Content (From Config)`

## CI/CD 集成

Build 系统为 CI/CD 集成提供命令行接口。

### 命令行构建

**完整应用构建:**

```bash
# 基本构建
-executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI -buildTarget Android -output Build/Android/MyGame.apk

# 带选项
-executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI \
  -buildTarget Android \
  -output Build/Android/MyGame.apk \
  -clean \
  -buildHybridCLR \
  -buildYooAsset

# 带版本覆盖
-executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI \
  -buildTarget StandaloneWindows64 \
  -output Build/Windows/MyGame.exe \
  -clean \
  -version v1.0.0
```

**参数:**

| 参数                 | 类型        | 描述                                        | 必需  |
| -------------------- | ----------- | ------------------------------------------- | ----- |
| `-buildTarget`       | BuildTarget | 目标平台（Android、StandaloneWindows64 等） | ✅ 是 |
| `-output`            | string      | 输出路径（相对于项目根目录）                | ✅ 是 |
| `-clean`             | flag        | 清理构建（删除之前的构建）                  | ❌ 否 |
| `-buildHybridCLR`    | flag        | 运行 HybridCLR 生成                         | ❌ 否 |
| `-buildYooAsset`     | flag        | 构建 YooAsset 包                            | ❌ 否 |
| `-buildAddressables` | flag        | 构建 Addressables 内容                      | ❌ 否 |
| `-version`           | string      | 覆盖版本（默认：来自 Git）                  | ❌ 否 |

**热更新构建:**

```bash
# 完整热更新构建
-executeMethod Build.Pipeline.Editor.HotUpdateBuilder.FullBuild

# 快速热更新构建
-executeMethod Build.Pipeline.Editor.HotUpdateBuilder.FastBuild
```

### CI/CD 示例

**GitHub Actions:**

```yaml
name: Build Game

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup Unity
        uses: game-ci/unity-builder@v4
        with:
          targetPlatform: Android
          buildMethod: Build.Pipeline.Editor.BuildScript.PerformBuild_CI
          buildArgs: -buildTarget Android -output Build/Android/MyGame.apk -clean -buildHybridCLR -buildYooAsset
```

**Jenkins:**

```groovy
pipeline {
    agent any

    stages {
        stage('Build') {
            steps {
                sh '''
                    Unity -batchmode -quit -projectPath . \
                    -executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI \
                    -buildTarget Android \
                    -output Build/Android/MyGame.apk \
                    -clean \
                    -buildHybridCLR \
                    -buildYooAsset
                '''
            }
        }
    }
}
```

## 故障排查

### BuildData 未找到

**错误**: `BuildData not found. Please create a BuildData asset.`

**解决方案:**

1. 创建 BuildData 资产: **Create > CycloneGames > Build > BuildData**
2. 确保项目中只存在一个 BuildData
3. 系统使用 `AssetDatabase.FindAssets` 查找 BuildData - 确保它在 Unity 可以索引的位置

### 配置资产未找到

**错误**: `YooAssetBuildConfig not found` 或类似

**解决方案:**

1. 创建所需的配置资产（YooAssetBuildConfig、AddressablesBuildConfig 或 HybridCLRBuildConfig）
2. 或者，如果您不需要，在 BuildData 中禁用相关功能
3. 如果缺少配置，系统将使用默认值，但某些功能可能无法正常工作

### 版本不匹配警告

**警告**: BuildData 和配置资产之间的版本不匹配

**解决方案:**

1. 对齐版本: 将配置资产版本设置为与 BuildData ApplicationVersion 匹配
2. 使用配置编辑器中的快速修复按钮（如果可用）
3. 或手动更新版本以保持一致性

### HybridCLR 未找到

**警告**: `HybridCLR package not found. Skipping generation.`

**解决方案:**

1. 如果您需要代码热更新，请安装 HybridCLR 包
2. 或者，如果您不需要，在 BuildData 中禁用 `Use HybridCLR`
3. 构建将在没有 HybridCLR 功能的情况下继续

### 资源管理包未找到

**警告**: 未找到资源管理包（YooAsset/Addressables）

**解决方案:**

1. 安装所需的包（YooAsset 或 Addressables）
2. 或者在 BuildData 中设置 `Asset Management Type = None`
3. 确保包已正确导入且可访问

### 构建输出目录问题

**错误**: 无法创建或访问构建输出目录

**解决方案:**

1. 检查 BuildData 中的 `Output Base Path`
2. 确保路径相对于项目根目录（例如，`Build`，而不是 `C:/Build`）
3. 确保您对项目目录有写入权限
4. 检查路径中是否有无效字符

### Git 版本信息缺失

**警告**: 无法获取 Git 版本信息

**解决方案:**

1. 确保 Git 已安装且可从命令行访问
2. 确保项目在 Git 存储库中
3. 检查 Git 是否在系统 PATH 中
4. 如果 Git 不可用，版本将回退到默认值

### 场景未找到

**错误**: `Invalid scene list, please check BuildData configuration.`

**解决方案:**

1. 在 BuildData 中分配 Launch Scene
2. 确保场景存在且未被删除
3. 检查场景是否已添加到 Build Settings（尽管 BuildData 优先）

## 最佳实践

### 1. 单一 BuildData 实例

- ✅ 每个项目只创建**一个** BuildData 资产
- ✅ 将其放置在逻辑位置（例如，`Assets/Config/BuildData.asset`）
- ✅ 如果您在一个 Unity 实例中有多个项目，请使用描述性命名

### 2. 版本对齐

- ✅ 保持 BuildData ApplicationVersion 与配置资产版本对齐
- ✅ 使用语义版本控制（例如，`v1.0`、`v1.1`、`v2.0`）
- ✅ 让系统附加提交计数以实现唯一性

### 3. 配置资产组织

- ✅ 在与 BuildData 相同的目录中创建配置资产
- ✅ 使用描述性名称（例如，`YooAssetBuildConfig_Production.asset`）
- ✅ 记录任何项目特定的配置

### 4. CI/CD 设置

- ✅ 使用命令行方法进行 CI/CD
- ✅ 设置适当的构建目标和输出路径
- ✅ 在设置 CI/CD 之前本地测试构建
- ✅ 仅在必要时使用版本覆盖

### 5. 热更新工作流

- ✅ 对结构更改或干净构建使用**完整构建**
- ✅ 对快速迭代使用**快速构建**
- ✅ 在生产前在开发中测试热更新
- ✅ 保持热更新文件组织有序和版本化

### 6. 可选包

- ✅ 仅安装您实际需要的包
- ✅ 系统优雅地处理缺失的包
- ✅ 使用和不使用可选包测试构建
- ✅ 记录您的项目需要哪些包

## 其他资源

- **HybridCLR 文档**: [HybridCLR GitHub](https://github.com/focus-creative-games/hybridclr)
- **YooAsset 文档**: [YooAsset GitHub](https://github.com/tuyoogame/YooAsset)
- **Addressables 文档**: [Unity Addressables 手册](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
- **Buildalon 文档**: [Buildalon GitHub](https://github.com/virtualmaker/Buildalon)

## 模块结构

```
Assets/Build/
├── Editor/
│   ├── BuildPipeline/
│   │   ├── BuildData.cs              # 中央配置
│   │   ├── BuildDataEditor.cs        # BuildData 检查器
│   │   ├── BuildScript.cs            # 完整应用构建
│   │   ├── HotUpdateBuilder.cs       # 热更新管线
│   │   ├── HybridCLR/                # HybridCLR 集成
│   │   ├── YooAsset/                 # YooAsset 集成
│   │   ├── Addressables/             # Addressables 集成
│   │   ├── Buildalon/                # Buildalon 集成
│   │   └── _Common/                  # 共享工具
│   └── VersionControl/               # 版本控制提供者
└── Runtime/
    └── Data/
        └── VersionInfoData.cs        # 运行时版本信息
```

---
