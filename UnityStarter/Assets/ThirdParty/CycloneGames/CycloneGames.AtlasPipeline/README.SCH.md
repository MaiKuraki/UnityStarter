# CycloneGames.AtlasPipeline

面向 Unity 2022.3+ 的数据驱动 Sprite 导入与 `SpriteAtlas`（V2）生成管线。为大型 2D 项目设计：确定性输出、增量索引、批量友好的资产编辑、零分配热路径、构建期校验。

## 核心特性

- **数据驱动规则**：`AtlasImportRule` 把一个源目录映射为精灵导入设置（分平台格式、压缩、mipmap、像素风）与图集策略（`PerSourceFolder` / `PerChildFolder` / `PerSprite` / `None`）。规则存于项目自有的 `AtlasPipelineSettings` 资产，不写在代码里。
- **单次导入**：设置在 `OnPreprocessTexture` 里应用，postprocessor 从不强制重导入，无导入循环。
- **GUID 跟踪目录**：规则源目录按 GUID 存储，Project 窗口里改名后所有规则自动跟随；旧配置（只有路径）自动补写 GUID。
- **确定性输出**：规则解析顺序稳定（最长目录优先、关键字秩、配置索引兜底）；图集键唯一性校验；packables 按 `assetPath + spriteName` 双重标识判等。
- **增量索引**：postprocessor 只更新受影响的图集，全量扫描只在显式重建时发生。编辑器自动处理按**时间预算**（8ms/帧）切片。
- **批量友好**：批量导入与图集生成包在 `AssetDatabase.StartAssetEditing` 内；自身触发的 `projectChanged` 被抑制，杜绝全量重扫反馈回路；batch mode 下所有弹窗降级为 `LogWarning`。
- **安全护栏**：输出目录与源目录重叠在校验期拒绝；超大精灵显式告警（Unity 原本静默丢弃）；孤儿 `.spriteatlasv2` 自动清扫；生成后校验期望图集真实存在，缺失则构建失败。
- **ASCII 命名策略**：可选严格命名（ASCII 字母数字 `_` `-`），走改名审查流程 + 构建校验。

## 安装

本仓库以嵌入式包形式置于 `Packages/`：

```json
"com.cyclone-games.atlas-pipeline": "file:CycloneGames.AtlasPipeline"
```

作为独立 UPM 分发时，把 `CycloneGames.AtlasPipeline` 目录拷进目标项目的 `Packages/`，或作为 git 依赖托管。包无外部依赖。

**直接放 `Assets/` 下使用也支持**：把管线目录（含 asmdef）放到 `Assets/` 任意位置，然后在 *Scripting Define Symbols* 里手动加 `BUILD_PIPELINE_HAS_ATLAS_PIPELINE`，构建集成才会编译。UPM 形式无需此步骤——完整条件编译矩阵见构建集成 README。注意不要同时保留 `Assets/` 和 `Packages/` 两份拷贝（程序集重名会冲突）。

## 快速上手

1. 打开 `Tools/CycloneGames/Atlas Pipeline/Open Atlas Pipeline`。
2. 首次打开会在 `Assets/Settings/AtlasPipelineSettings.asset` 创建项目设置资产。
3. 配置导入规则（从 Project 视图拖入源文件夹）。
4. `Apply Importers` 写入导入设置、`Rebuild Index` 重扫索引、`Regenerate Atlases` 重建全部图集。

图集输出目录由设置资产中的 `Output Atlas Folder` 指定。

## 构建集成

包本身不依赖项目的 Build 模块。薄薄的 `IBuildStep` 适配器位于：

```
Assets/Build/Editor/BuildPipeline/Integrations/AtlasPipeline/
```

注册 `cyclonegames-atlas-pipeline` 构建步骤，且仅在包存在时编译（通过 `com.cyclone-games.atlas-pipeline` 的 `versionDefines` 守卫）。把它加进 `BuildData.asset` 的 recipe，作为 `asset-content` 的前置依赖。

## 测试

EditMode 测试（NUnit）在 `Tests/Editor/`，覆盖纯逻辑：命名策略、规则匹配、平台格式映射、PNG/JPEG 头解析、GUID 跟随目录引用、旋转策略。从 `Window > General > Test Runner` 运行。

## 命名约定

- 程序集：`CycloneGames.AtlasPipeline.Editor`
- 命名空间：`CycloneGames.AtlasPipeline`
- 设置资产（项目自有）：`Assets/Settings/AtlasPipelineSettings.asset`
