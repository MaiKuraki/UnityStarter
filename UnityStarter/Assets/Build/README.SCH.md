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
- **Obfuz 集成**: 代码混淆，用于保护您的代码（可选）
- **YooAsset 集成**: 资源管理和热更新（可选）
- **Addressables 集成**: Unity 官方资源管理（可选）
- **Buildalon 集成**: 构建自动化辅助工具（可选）

### 主要特性

- ✅ **灵活的包支持**: 可与可选包（HybridCLR、Obfuz、YooAsset、Addressables、Buildalon）配合使用，也可不使用
- ✅ **自动版本控制**: 基于 Git 的版本生成
- ✅ **多平台支持**: 支持 Windows、Mac、Linux、Android、iOS、WebGL
- ✅ **热更新就绪**: 代码和资源热更新的完整解决方案
- ✅ **代码保护**: 集成 Obfuz 混淆以保护您的代码
- ✅ **CI/CD 友好**: 用于自动化构建的命令行接口
- ✅ **配置驱动**: 所有设置通过 ScriptableObject 资产

## 前置条件

### 必需

- **Unity 2022.3+**
- **Git**（用于自动版本控制）

### 可选包

Build 系统支持以下可选包。仅安装您需要的包：

- **[HybridCLR](https://github.com/focus-creative-games/hybridclr)** - 用于 C# 代码热更新
- **[Obfuz](https://github.com/Code-Philosophy/Obfuz)** - 代码混淆，用于保护您的代码
- **[Obfuz4HybridCLR](https://github.com/Code-Philosophy/Obfuz4HybridCLR)** - Obfuz 的 HybridCLR 热更新程序集扩展
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
- **Use Obfuz**: 如果已安装 Obfuz 包并想要代码混淆，请启用（详见下面的 [Obfuz 配置](#obfuz-配置)）

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

#### 如果使用 Obfuz

**Obfuz 同时支持 HybridCLR 和非 HybridCLR 项目。** 主要控制开关是 **BuildData.UseObfuz**。

**对于所有项目:**

1. 在 BuildData 中启用 **Use Obfuz**（这是主要控制开关）
2. 在 Unity 编辑器中配置 ObfuzSettings（Obfuz 菜单）
3. 构建管线将在构建期间自动应用混淆

**HybridCLR 项目的额外步骤:**

- 如果您使用 HybridCLR，也可以在 HybridCLRBuildConfig 中启用 **Enable Obfuz** 以混淆热更新程序集
- **注意**: BuildData.UseObfuz 优先级更高。如果 BuildData.UseObfuz 已启用，HybridCLRBuildConfig.enableObfuz 会自动被视为已启用

> **注意**: 这些配置资产是可选的。如果未找到它们，系统将使用默认值，但建议创建它们以进行正确配置。

### 步骤 4: 构建您的项目

配置 BuildData 后，您可以使用以下方式构建：

**Unity 编辑器菜单:**

**Release 构建:**

- **Build > Game(Release) > Build Android APK (IL2CPP)**
- **Build > Game(Release) > Build Windows (IL2CPP)**
- **Build > Game(Release) > Build Mac (IL2CPP)**
- **Build > Game(Release) > Build Linux (IL2CPP)**
- **Build > Game(Release) > Build iOS (IL2CPP)**
- **Build > Game(Release) > Build WebGL**
- **Build > Game(Release) > Export Android Project (IL2CPP)**

**Release 快速构建（不清理）:**

- **Build > Game(Release) > Fast > Build Android APK (Fast)**
- **Build > Game(Release) > Fast > Build Windows (Fast)**
- **Build > Game(Release) > Fast > Build Mac (Fast)**
- **Build > Game(Release) > Fast > Build Linux (Fast)**
- **Build > Game(Release) > Fast > Build iOS (Fast)**
- **Build > Game(Release) > Fast > Build WebGL (Fast)**
- **Build > Game(Release) > Fast > Export Android Project (Fast)**

**Debug 构建:**

- **Build > Game(Debug) > Build Android APK (Debug)**
- **Build > Game(Debug) > Build Windows (Debug)**
- **Build > Game(Debug) > Build Mac (Debug)**
- **Build > Game(Debug) > Build Linux (Debug)**
- **Build > Game(Debug) > Build iOS (Debug)**
- **Build > Game(Debug) > Build WebGL (Debug)**
- **Build > Game(Debug) > Export Android Project (Debug)**

**Debug 快速构建（不清理）:**

- **Build > Game(Debug) > Fast > Build Android APK (Debug Fast)**
- **Build > Game(Debug) > Fast > Build Windows (Debug Fast)**
- **Build > Game(Debug) > Fast > Build Mac (Debug Fast)**
- **Build > Game(Debug) > Fast > Build Linux (Debug Fast)**
- **Build > Game(Debug) > Fast > Build iOS (Debug Fast)**
- **Build > Game(Debug) > Fast > Build WebGL (Debug Fast)**
- **Build > Game(Debug) > Fast > Export Android Project (Debug Fast)**

**调试信息:**

- **Build > Print Debug Info** - 打印当前构建配置详情

**或使用热更新管线:**

- **Build > HotUpdate Pipeline > Full Build (Generate Code + Bundles)**
- **Build > HotUpdate Pipeline > Fast Build (Compile Code + Bundles)**

## 核心概念

### BuildData

`BuildData` 是整个构建系统的中央配置资产。它包含：

- **Launch Scene**: 构建的入口点场景
- **Application Version**: 自动版本控制的版本前缀
- **Output Base Path**: 构建输出的基础目录
- **功能标志**: 启用/禁用可选功能（HybridCLR、Obfuz、Buildalon、Cheat 暴露）
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

- 多平台构建（Windows、Mac、Linux、Android、iOS、WebGL）
- 自动版本控制
- 可选的 HybridCLR 代码生成
- 可选的资源包构建（YooAsset/Addressables）
- 面向内部调试构建的 `ENABLE_CHEAT` 构建期 define 控制
- 清理构建选项（完整构建）和快速构建选项（不清理）
- Debug 构建选项（支持开发模式和 Profiler）
- 调试文件管理
- 构建配置调试信息（Print Debug Info）

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
- **Obfuz**: 通过 `Obfuz.Settings.ObfuzSettings` 类型检测（基础包）
- **Obfuz4HybridCLR**: 通过 `Obfuz4HybridCLR.ObfuscateUtil` 类型检测（HybridCLR 扩展）
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
| Use Obfuz             | bool       | 启用 Obfuz 代码混淆                | ❌ 否 |
| Cheat Build Mode      | enum       | Disabled / DevelopmentBuilds / Enabled，用于控制 `ENABLE_CHEAT` | ❌ 否 |
| Asset Management Type | enum       | None / YooAsset / Addressables     | ❌ 否 |

`Cheat Build Mode` 只影响 `BuildScript` 执行的 Player 构建。构建管线会在构建期间为选定的 `NamedBuildTarget` 添加或移除 `ENABLE_CHEAT`，构建结束后恢复原始 define 列表。CI 可以通过 `-enableCheat` 或 `-disableCheat` 覆盖单次构建。Build 模块不引用 CycloneGames.Cheat assembly；它通过反射和 Unity 编译元数据检测可选的 `CycloneGames.Cheat.Runtime` assembly。如果 player 编译域中不存在该 runtime assembly，则不会应用该 define，普通构建继续执行。

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

**热更新配置:**

- **Hot Update Assemblies**: 拖拽需要热更新的 `.asmdef` 文件（必需）
- **Hot Update DLL Output Directory**: 热更新 DLL 的输出目录（必需）

**Cheat/Debug DLL 配置（可选）:**

- **Cheat Assemblies**: 拖拽用于作弊/调试模块的 `.asmdef` 文件（可选）
- **Cheat DLL Output Directory**: Cheat DLL 的输出目录（可选，如果配置了 Cheat Assemblies 则建议配置）

**AOT DLL 配置:**

- **AOT DLL Output Directory**: AOT DLL 的输出目录，用于元数据生成（必需）

**Obfuz 设置:**

- **Enable Obfuz**: 为热更新程序集启用混淆（可选）

**主要特性:**

- ✅ **多 DLL 支持**: 可配置多个热更新和 Cheat 程序集
- ✅ **JSON 列表**: 生成 `HotUpdate.bytes` 和 `Cheat.bytes` 列表文件供运行时加载
- ✅ **独立输出**: HotUpdate、Cheat 和 AOT DLL 可输出到不同目录

**⚠️ 重要配置说明:**

**HybridCLR 需要在其 Settings 窗口中手动配置。**

- ✅ **配置源**: 所有 DLL 列表（Hot Update、Cheat、AOT）均在 `HybridCLRBuildConfig` 中配置
- ⚠️ **需要手动设置**: 您**必须**手动配置 HybridCLR 的 Settings 以匹配您的 `HybridCLRBuildConfig`
- 📋 **如何配置 HybridCLR Settings**:
  1. 打开 Unity 菜单: `HybridCLR -> Settings`
  2. 在 `Hot Update Assembly Definitions` 列表中，添加 `HybridCLRBuildConfig` 中的所有 `.asmdef` 文件
  3. 确保 HybridCLR Settings 中的 asmdefs 与您的 `HybridCLRBuildConfig` 中的完全匹配
- ✅ **为什么需要两个配置**: `HybridCLRBuildConfig` 被构建系统用来确定要复制哪些 DLL。HybridCLR 的 Settings 被 HybridCLR 用于编译。两者必须匹配。

**📦 Package 程序集处理:**

- ✅ **只有 Assets/ 文件夹下的程序集可以作为热更新 DLL**: HybridCLR 只会将 `Assets/` 文件夹下的程序集编译为热更新 DLL。所有 Package Manager 包（位于 `Packages/`、`Library/PackageCache/` 或外部路径）都是 AOT 程序集，不能热更新。
- ✅ **Package Manager 包程序集是 AOT**: 这些包应该通过 `link.xml` 来防止 IL2CPP 代码裁剪，而不是编译为热更新 DLL。
- ⚠️ **如果您需要 Package 代码可热更新**: 将包代码复制到 `Assets/` 文件夹中，并为其创建新的 asmdef。

> **⚠️ 重要**: 您必须手动配置 HybridCLR 的 Settings（通过 `HybridCLR -> Settings` 菜单）以匹配您的 `HybridCLRBuildConfig` 中的 asmdefs。构建系统使用 `HybridCLRBuildConfig` 来确定要复制哪些 DLL。运行时加载使用 JSON 列表文件（`HotUpdate.bytes`、`Cheat.bytes`）来加载多个 DLL。

**JSON 列表文件格式:**

构建系统会生成 JSON 列表文件（`.bytes` 扩展名）供运行时 DLL 加载。JSON 结构如下：

```json
{
  "assemblies": [
    "Assets/YourProject/CompiledDLLs/HotUpdate/YourProject.HotUpdate.dll.bytes",
    "Assets/YourProject/CompiledDLLs/HotUpdate/AnotherHotUpdate.dll.bytes"
  ]
}
```

**运行时加载示例代码:**

以下示例代码展示如何在运行时从 JSON 列表文件加载 DLL：

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;
using YooAsset;

// JSON 结构用于程序集列表
[Serializable]
private class AssemblyList
{
    public List<string> assemblies;
}

// 从 JSON 列表加载 HotUpdate DLL
private async UniTask<bool> LoadHotUpdateDllsAsync(IAssetModule yooAssetModule, CancellationToken cancellationToken = default)
{
    try
    {
        var rawFilePackage = yooAssetModule.GetPackage("RawFilePackage");
        if (rawFilePackage == null)
        {
            Debug.LogError("RawFilePackage 未找到。");
            return false;
        }

        // 加载 JSON 列表文件（调整路径以匹配您的输出目录配置）
        string listPath = "Assets/YourProject/CompiledDLLs/HotUpdate/HotUpdate.bytes";
        var listHandle = rawFilePackage.LoadRawFileAsync(listPath, cancellationToken);
        await listHandle.Task;

        if (!string.IsNullOrEmpty(listHandle.Error))
        {
            Debug.LogError($"加载列表文件失败: {listHandle.Error}");
            listHandle.Dispose();
            return false;
        }

        // 解析 JSON
        byte[] listBytes = listHandle.ReadBytes();
        listHandle.Dispose();
        string jsonText = Encoding.UTF8.GetString(listBytes);
        AssemblyList list = JsonUtility.FromJson<AssemblyList>(jsonText);

        if (list == null || list.assemblies == null || list.assemblies.Count == 0)
        {
            Debug.LogError("程序集列表为空。");
            return false;
        }

        // 从列表加载每个 DLL
        foreach (var dllPath in list.assemblies)
        {
            var dllHandle = rawFilePackage.LoadRawFileAsync(dllPath, cancellationToken);
            await dllHandle.Task;

            if (!string.IsNullOrEmpty(dllHandle.Error))
            {
                Debug.LogError($"加载 DLL 失败: {dllPath}, 错误: {dllHandle.Error}");
                dllHandle.Dispose();
                continue;
            }

            byte[] dllBytes = dllHandle.ReadBytes();
            dllHandle.Dispose();

            if (dllBytes != null && dllBytes.Length > 0)
            {
                Assembly assembly = Assembly.Load(dllBytes);
                Debug.Log($"已加载 DLL: {assembly.GetName().FullName}");
                // 存储程序集引用供后续使用
            }
        }

        return true;
    }
    catch (Exception ex)
    {
        Debug.LogError($"加载 DLL 时发生异常: {ex.Message}");
        return false;
    }
}
```

> **注意**: 此示例代码展示了基本的加载模式。请调整 `listPath` 以匹配您的 HybridCLR Build Config 输出目录配置。在实际使用中，您应该更优雅地处理错误，验证 DLL 格式，并妥善管理程序集引用。

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

### Obfuz 配置

**什么是 Obfuz？**

Obfuz 是一个代码混淆工具，通过使代码更难被逆向工程来保护您的 C# 代码。Build 系统集成 Obfuz 以在构建过程中自动混淆您的代码。

**两种操作模式：**

1. **非 HybridCLR 项目**: 使用 Obfuz 的原生构建管线集成。在 BuildData 中启用 **Use Obfuz** 即可激活。
2. **HybridCLR 项目**: 在编译后混淆热更新程序集，然后重新生成方法桥接和 AOT 泛型引用。在 BuildData 中启用 **Use Obfuz**（也可选择在 HybridCLRBuildConfig 中启用 **Enable Obfuz**）。

**必需的包：**

- **Obfuz**（基础包）- 所有混淆都需要
- **Obfuz4HybridCLR**（扩展）- 仅 HybridCLR 项目需要

**配置步骤：**

**步骤 1: 安装 Obfuz 包**

通过 Package Manager 或 Git URL 安装：

- `com.code-philosophy.obfuz`
- `com.code-philosophy.obfuz4hybridclr`（用于 HybridCLR 项目）

**步骤 2: 在 BuildData 中启用（主要控制）**

1. 选择您的 BuildData 资产
2. 启用 **Use Obfuz** 复选框
3. 系统将自动检测 Obfuz 包
4. **这是主要控制开关** - Obfuz 将根据此设置启用

> **重要**: BuildData.UseObfuz 是主要控制。对于 HybridCLR 项目，如果 BuildData.UseObfuz 已启用，HybridCLRBuildConfig.enableObfuz 会自动被视为已启用。

**步骤 3: 配置 ObfuzSettings（必需）**

1. 在 Unity 编辑器中，转到 **Obfuz** 菜单
2. 打开 **ObfuzSettings** 窗口
3. 配置要混淆的程序集：
   - 将程序集添加到 `assembliesToObfuscate`（对于非 HybridCLR：主程序集；对于 HybridCLR：热更新程序集）
   - 如果 `Assembly-CSharp` 引用了混淆的程序集（如 Obfuz.Runtime），将其添加到 `NonObfuscatedButReferencingObfuscatedAssemblies`
4. 保存 ObfuzSettings

> **注意**: Build 系统会自动配置引用列表中的 `Assembly-CSharp`，但您应该在 ObfuzSettings 中验证这一点。

**步骤 4: 对于 HybridCLR 项目（可选的额外控制）**

1. 创建或选择 **HybridCLR Build Config**（如果尚未创建）
2. 可选择启用 **Enable Obfuz** 复选框（如果 BuildData.UseObfuz 已启用，此选项会自动被视为已启用）
3. 确保在 ObfuzSettings 中已配置热更新程序集

**构建期间发生的情况：**

**对于非 HybridCLR 项目（当 BuildData.UseObfuz 启用时）：**

1. 构建预处理器配置 ObfuzSettings
2. 生成加密 VM 和密钥文件（如需要）
3. Obfuz 的原生 `ObfuscationProcess` 在构建期间运行
4. 代码在编译前被混淆

**对于 HybridCLR 项目（当 BuildData.UseObfuz 启用时）：**

1. 构建预处理器配置 ObfuzSettings
2. 生成加密 VM 和密钥文件（如需要）
3. HybridCLR 编译热更新 DLL
4. **混淆**热更新程序集（使用混淆后的 DLL）
5. **重新生成**方法桥接和反向 P/Invoke 包装器（使用混淆后的程序集）
6. **重新生成**AOT 泛型引用（使用混淆后的程序集）
7. 将混淆后的 DLL 复制到输出目录

> **注意**: 控制优先级为：**BuildData.UseObfuz** > HybridCLRBuildConfig.enableObfuz。如果 BuildData.UseObfuz 已启用，无论 HybridCLRBuildConfig 设置如何，Obfuz 都会工作。

**重要提示：**

- ⚠️ **混淆不可逆**: 始终保留未混淆的备份
- ⚠️ **充分测试**: 混淆可能会破坏基于反射的代码
- ✅ **自动前置条件**: Build 系统自动生成加密 VM 和密钥
- ✅ **HybridCLR 集成**: 混淆后重新生成方法桥接以确保兼容性

## 构建工作流

### 完整应用构建

**目的**: 构建用于分发的完整应用程序

**工作流:**

1. 加载 BuildData 配置
2. 从 Git 生成版本信息
3. （可选）如果启用 Obfuz，配置 ObfuzSettings
4. （可选）如果启用，运行 HybridCLR 代码生成
5. （可选）如果启用资源管理，构建资源包
6. 构建 Unity 播放器（对于非 HybridCLR 项目，Obfuz 混淆自动运行）
7. 将版本信息保存到 `VersionInfoData` 资产
8. （可选）将资源包复制到输出目录

**菜单项:**

**Release 构建:**

- `Build > Game(Release) > Build Android APK (IL2CPP)`
- `Build > Game(Release) > Build Windows (IL2CPP)`
- `Build > Game(Release) > Build Mac (IL2CPP)`
- `Build > Game(Release) > Build Linux (IL2CPP)`
- `Build > Game(Release) > Build iOS (IL2CPP)`
- `Build > Game(Release) > Build WebGL`
- `Build > Game(Release) > Export Android Project (IL2CPP)`

**Release 快速构建:**

- `Build > Game(Release) > Fast > Build [平台] (Fast)` - 跳过清理构建以加快迭代速度

**Debug 构建:**

- `Build > Game(Debug) > Build [平台] (Debug)` - 包含开发模式、调试符号和 Profiler 支持

**Debug 快速构建:**

- `Build > Game(Debug) > Fast > Build [平台] (Debug Fast)` - 不清理的 Debug 构建

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
2. **Obfuz**: 如果 BuildData.UseObfuz 已启用，生成前置条件（加密 VM、密钥、配置设置）
3. **HybridCLR**: 生成所有代码和元数据（`GenerateAllAndCopy`）
4. **Obfuz**: 如果 BuildData.UseObfuz 已启用且使用 HybridCLR，混淆热更新程序集
5. **Obfuz**: 如果应用了混淆，重新生成方法桥接和 AOT 泛型引用
6. **HybridCLR**: 复制 DLL 到输出目录并生成 JSON 列表文件（`HotUpdate.bytes`、`Cheat.bytes`）
7. **资源管理**: 构建所有资源包
8. 输出热更新文件

**菜单项**: `Build > HotUpdate Pipeline > Full Build (Generate Code + Bundles)`

**输出:**

- 热更新 DLL 在配置的输出目录，包含 `HotUpdate.bytes` 列表文件
- Cheat DLL 在配置的输出目录，包含 `Cheat.bytes` 列表文件（如果已配置）
- AOT DLL 在配置的输出目录，用于元数据生成
- 资源包在配置的输出目录

### 热更新 - 快速构建

**目的**: 快速热更新构建（DLL 编译 + 资源打包）

**何时使用:**

- 仅方法实现已更改（无结构更改）
- 开发期间的快速迭代
- 快速错误修复

**工作流:**

1. 加载 BuildData
2. **Obfuz**: 如果 BuildData.UseObfuz 已启用，生成前置条件（加密 VM、密钥、配置设置）
3. **HybridCLR**: 仅编译 DLL（`CompileDLLAndCopy`）
4. **Obfuz**: 如果 BuildData.UseObfuz 已启用且使用 HybridCLR，混淆热更新程序集
5. **Obfuz**: 如果应用了混淆，重新生成方法桥接和 AOT 泛型引用
6. **HybridCLR**: 复制 DLL 到输出目录并更新 JSON 列表文件
7. **资源管理**: 构建资源包
8. 输出热更新文件

**菜单项**: `Build > HotUpdate Pipeline > Fast Build (Compile Code + Bundles)`

**输出:**

- 编译的热更新 DLL 及更新的列表文件
- 更新的 Cheat DLL（如果已配置）
- 更新的资源包

### 构建配置调试信息

**目的**: 打印详细的构建配置信息，用于故障排查和验证

**菜单项**: `Build > Print Debug Info`

**显示的信息:**

- **基本构建配置**: 应用版本、输出基础路径、当前构建目标
- **场景配置**: 构建场景列表
- **Buildalon 配置**: 是否启用 Buildalon
- **HybridCLR 配置**: HybridCLR 状态、配置资产可用性、AOT DLL 输出目录
- **Obfuz 配置**: Obfuz 状态、包可用性（基础和 HybridCLR 扩展）、有效的混淆状态
- **资源管理配置**: 选择的资源管理系统（YooAsset/Addressables/None）和配置资产可用性
- **版本控制配置**: 版本控制类型、提交哈希、提交计数、完整构建版本
- **构建目标配置**: 当前构建目标、脚本后端、API 兼容性级别

**使用场景:**

- 构建前验证构建配置
- 排查缺失的配置资产
- 检查包可用性
- 验证功能启用状态
- 调试配置不匹配问题

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
  -buildYooAsset \
  -enableCheat

# 带版本覆盖
-executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI \
  -buildTarget StandaloneWindows64 \
  -output Build/Windows/MyGame.exe \
  -clean \
  -version v1.0.0
```

**参数:**

| 参数                 | 类型        | 描述                                                                                   | 必需  |
| -------------------- | ----------- | -------------------------------------------------------------------------------------- | ----- |
| `-buildTarget`       | BuildTarget | 目标平台（Android、StandaloneWindows64、StandaloneOSX、StandaloneLinux64、iOS、WebGL） | ✅ 是 |
| `-output`            | string      | 输出路径（相对于项目根目录）                                                           | ✅ 是 |
| `-clean`             | flag        | 清理构建（删除之前的构建）                                                             | ❌ 否 |
| `-buildHybridCLR`    | flag        | 运行 HybridCLR 生成                                                                    | ❌ 否 |
| `-buildYooAsset`     | flag        | 构建 YooAsset 包                                                                       | ❌ 否 |
| `-buildAddressables` | flag        | 构建 Addressables 内容                                                                 | ❌ 否 |
| `-enableCheat`       | flag        | 强制本次 CI 构建启用 `ENABLE_CHEAT`                                                    | ❌ 否 |
| `-disableCheat`      | flag        | 强制本次 CI 构建关闭 `ENABLE_CHEAT`                                                    | ❌ 否 |
| `-version`           | string      | 覆盖版本（默认：来自 Git）                                                             | ❌ 否 |

`-enableCheat` 和 `-disableCheat` 互斥。若两个参数同时传入，CI 构建会在 Player 构建开始前失败。
如果 `CycloneGames.Cheat.Runtime` assembly 不可用，`-enableCheat` 会记录 warning，并在不定义 `ENABLE_CHEAT` 的情况下继续构建。

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

### HybridCLR 配置问题

**警告**: `HybridCLRBuildConfig not found` 或缺少必需的设置

**解决方案:**

1. 创建 HybridCLR Build Config: **Create > CycloneGames > Build > HybridCLR Build Config**
2. 配置 **Hot Update Assemblies**（必需）: 拖拽需要热更新的 `.asmdef` 文件
3. 配置 **Hot Update DLL Output Directory**（必需）: 拖拽输出文件夹
4. 配置 **AOT DLL Output Directory**（必需）: 拖拽用于 AOT 元数据 DLL 的文件夹
5. 可选配置 **Cheat Assemblies** 和 **Cheat DLL Output Directory** 用于调试模块
6. **手动配置 HybridCLR Settings**: 打开 `HybridCLR -> Settings`，将所有 asmdefs 从您的 `HybridCLRBuildConfig` 添加到 `Hot Update Assembly Definitions` 列表

**⚠️ 重要**: 先在 `HybridCLRBuildConfig` 中配置 DLL 列表，然后手动确保 HybridCLR Settings（通过 `HybridCLR -> Settings` 菜单）与之匹配。构建系统使用 `HybridCLRBuildConfig` 来确定要复制哪些 DLL，而 HybridCLR 使用其 Settings 进行编译。

### Obfuz 未找到

**警告**: `Obfuz package not found. Skipping obfuscation.`

**解决方案:**

1. 如果您需要代码混淆，请安装 Obfuz 包：
   - `com.code-philosophy.obfuz`（基础包，必需）
   - `com.code-philosophy.obfuz4hybridclr`（用于 HybridCLR 项目，必需）
2. 或者，如果您不需要，在 BuildData 中禁用 `Use Obfuz`
3. 构建将在没有混淆的情况下继续

### Obfuz 配置问题

**警告**: Obfuz 混淆失败或程序集未配置

**解决方案:**

1. 验证 BuildData 中已启用 **Use Obfuz**（这是主要控制）
2. 在 Unity 编辑器中打开 **Obfuz > ObfuzSettings**
3. 验证程序集已添加到 `assembliesToObfuscate`
4. 如果需要，确保 `Assembly-CSharp` 在 `NonObfuscatedButReferencingObfuscatedAssemblies` 中
5. 检查是否生成了加密 VM 和密钥文件（Obfuz 菜单）
6. 对于 HybridCLR: 如果 BuildData.UseObfuz 已启用，HybridCLRBuildConfig.enableObfuz 会自动被视为已启用

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
- ✅ 通过 `Cheat Build Mode`、`-enableCheat` 或 `-disableCheat` 明确选择 Cheat 策略
- ✅ 仅在必要时使用版本覆盖

### 5. 热更新工作流

- ✅ 对结构更改或干净构建使用**完整构建**
- ✅ 对快速迭代使用**快速构建**
- ✅ 在 HybridCLR Build Config 中配置所有必需的输出目录
- ✅ 手动配置 HybridCLR Settings（通过 `HybridCLR -> Settings`）以匹配您的 `HybridCLRBuildConfig`
- ✅ JSON 列表文件（`HotUpdate.bytes`、`Cheat.bytes`）会自动生成
- ✅ 在生产前在开发中测试热更新
- ✅ 保持热更新文件组织有序和版本化

### 6. 可选包

- ✅ 仅安装您实际需要的包
- ✅ 系统优雅地处理缺失的包
- ✅ 使用和不使用可选包测试构建
- ✅ 记录您的项目需要哪些包

## 其他资源

- **HybridCLR 文档**: [HybridCLR GitHub](https://github.com/focus-creative-games/hybridclr)
- **Obfuz 文档**: [Obfuz GitHub](https://github.com/Code-Philosophy/Obfuz)
- **Obfuz4HybridCLR 文档**: [Obfuz4HybridCLR GitHub](https://github.com/Code-Philosophy/Obfuz4HybridCLR)
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
│   │   ├── Obfuz/                    # Obfuz 混淆集成
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
