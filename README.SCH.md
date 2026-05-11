![Unity Project Starter](<https://capsule-render.vercel.app/api?type=waving&height=220&color=gradient&text=Unity%20Project%20Starter&section=header&reversal=false&textBg=false&desc=GameplayFramework%20│%20GameplayAbility(GAS)%20│%20UIFramework%20│%20HotUpdate%20Ready%20(HybridCLR)%20│%20CI/CD%20Ready&descAlign=50&descAlignY=58&descSize=16&fontAlignY=30&fontSize=72>)

一个生产就绪、模块化的 Unity 全栈游戏框架，借鉴**虚幻引擎**架构，专为大型商业游戏开发设计。性能优先的系统、完善的工具链、现代化的 CI/CD 工作流。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

> [!NOTE]
> 如果你觉得这个项目对你有帮助，请点一个 Star ⭐，谢谢！

![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity)
![Unity6](https://img.shields.io/badge/Unity-6000.3%20LTS-black?logo=unity)
![License](https://img.shields.io/badge/License-MIT-green.svg)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/MaiKuraki/UnityStarter)

## 目录

1. [概述](#概述)
2. [架构设计](#架构设计)
3. [模块目录](#模块目录)
4. [快速上手](#快速上手)
5. [技术栈](#技术栈)
6. [相关项目](#相关项目)

## 概述

UnityStarter 是一套经过验证的专业游戏开发基础——不是零散的实用工具集合，而是一个完整、一致的全栈游戏框架。它提供：

- **完整游戏架构** — Actor/Pawn/Controller/GameMode 的虚幻风格架构
- **全套战斗系统** — GAS 风格技能系统：Ability、Attribute、Effect、Cooldown、Cost
- **生产级 AI** — 可视化行为树编辑器，Burst/DOD 方案扩展至 10,000+ 智能体
- **双模网络同步** — 状态同步 + Lockstep 回滚，5 种 AOI 策略
- **性能优先** — 所有核心系统热路径零 GC
- **模块化设计** — 24+ 个自包含 Package，按需导入
- **热更新就绪** — HybridCLR（C#）+ YooAsset/Addressables（资源）+ Obfuz（代码保护）
- **开发者工具** — 自研 Roslyn Analyzer、构建管线、Cheat 控制台、UI 性能分析器
- **跨引擎核心** — 关键模块面向 `netstandard2.0`，零 Unity API，已为 Godot 做好准备

## 架构设计

```text
UnityStarter/
├── Assets/
│   ├── Build/                  # 构建管线与 CI/CD（HybridCLR、Obfuz、YooAsset/Addressables）
│   ├── Plugins/                # 第三方原生插件
│   ├── Settings/               # URP PipelineAssets（Performant/Balanced/HighFidelity）
│   ├── ThirdParty/CycloneGames/ # 核心框架（22+ 模块、80+ asmdef）
│   └── UnityStarter/           # 项目特有代码与场景
├── Analyzers/                  # Roslyn 分析器（22 条已实现规则）[独立 .sln]
├── Packages/                   # UPM 清单
├── ProjectSettings/            # Unity 项目配置
└── Tools/                      # Go 工具脚本
```

每个模块遵循相同的内部结构：`Runtime/`（核心）→ `Editor/`（工具）→ 可选 `Core/`（引擎无关）→ 可选 `Integrations/`（跨模块桥接）→ `Samples/`。每个模块均有中英双语 `README.md` + `README.SCH.md`。

## 模块目录

> **📚** 每个模块目录下都有详细文档，点击下方的链接直接跳转。

### 🎮 游戏玩法系统

| 模块 | 描述 | 文档 |
| --- | --- | --- |
| **GameplayFramework** | UE 风格 Actor/Pawn/Controller/GameMode 架构。DI 友好，高度可扩展。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/README.SCH.md) |
| **GameplayAbilities** | 数据驱动技能系统（GAS）。ScriptableObject 技能、属性、效果、消耗/冷却、GameplayCue。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/README.SCH.md) |
| **GameplayAbilities.Networking** | 网络化 GAS：客户端预测、回滚、状态复制、14 种网络消息、漂移检测。 | 模块目录 |
| **GameplayTags** | 层级标签系统。Source Generator 生成，零分配查询，Unity Editor 集成。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags/README.SCH.md) |
| **BehaviorTree** | 可视化 AI 行为树。GraphView 编辑器、DOD/Burst 批量仿真、30+ 节点、网络同步。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree/README.SCH.md) |
| **AIPerception** | Burst 加速 AI 感知。视觉/深度/接近查询、空间网格、每 Tick 零 GC。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AIPerception/README.SCH.md) |
| **RPGFoundation** | RPG 扩展：背包、属性、任务、2D/3D 移动控制器。 | 模块目录 |
| **UIFramework** | 层级式 UI 框架。窗口状态机、MVP 支持、多 Tween 后端、动态图集。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/README.SCH.md) |

### 🏗️ 核心基础设施

| 模块 | 描述 | 文档 |
| --- | --- | --- |
| **Factory** | 高性能对象池。线程安全、自动扩缩、O(1) 操作、DOD 变体。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Factory/README.SCH.md) |
| **Logger** | 结构化日志。多线程、文件轮转、可插拔处理器、WebGL 支持。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logger/README.SCH.md) |
| **AssetManagement** | 资源加载抽象层。W-TinyLFU 缓存、YooAsset/Addressables/Resources 三后端。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AssetManagement/README.SCH.md) |
| **DataTable** | 配置数据管线。Luban/MessagePack 双后端、零 GC O(1) 查询、Core 引擎无关。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/README.SCH.md) |
| **Audio** | 音频管理。类似 Wwise 的 API、Bank 系统、平台 Profile、语音策略。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio/README.SCH.md) |
| **Localization** | BCP 47 本地化框架。CLDR 复数规则（25+ 语言）、回退链、伪本地化 QA。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Localization/README.SCH.md) |

### 🌐 网络

| 模块 | 描述 | 文档 |
| --- | --- | --- |
| **Networking** | 网络抽象层。可插拔传输（Mirror/Mirage）、QoS 通道、4 种序列化器、Burst AOI。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Networking/README.SCH.md) |

### 🕹️ 输入与设备

| 模块 | 描述 | 文档 |
| --- | --- | --- |
| **InputSystem** | 响应式输入封装。上下文栈、本地多人、设备自动检测、运行时改键。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem/README.SCH.md) |
| **DeviceFeedback** | 跨平台设备反馈。手机振动（Android/iOS/WebGL）、手柄震动、灯条控制。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DeviceFeedback/README.SCH.md) |

### 🧰 开发者工具

| 模块 | 描述 | 文档 |
| --- | --- | --- |
| **Analyzers** | 自研 Roslyn 分析器。22 条已实现规则覆盖 5 大类别，2 个 CodeFix。 | [README.SCH](UnityStarter/Analyzers/CycloneGames.Analyzers/README.SCH.md) |
| **Cheat** | 类型安全调试命令控制台。VitalRouter 集成、异步命令、线程安全执行。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Cheat/README.SCH.md) |
| **Utility** | 通用工具集：FPS 计数器、安全区域适配、启动画面、文件操作。 | 模块目录 |
| **Services** | 服务抽象：摄像机管理、图形设置、设备配置。 | 模块目录 |
| **FontAssets** | 多语言字体。拉丁文、中日韩（简体/繁体/日文/韩文）。 | 模块目录 |

### 🎯 2D 与平台

| 模块 | 描述 | 文档 |
| --- | --- | --- |
| **Foundation2D** | 2D 游戏基础。性能基准测试、平台跳跃组件、Sprite 管理。 | 模块目录 |

### 🔧 构建与部署

| 模块 | 描述 | 文档 |
| --- | --- | --- |
| **Build** | 完整构建管线。HybridCLR + Obfuz + 资源管理。CLI 驱动，CI/CD 就绪。 | [README.SCH](UnityStarter/Assets/Build/README.SCH.md) |

## 快速上手

### 前置条件

- **Unity 2022.3 LTS** 或更高版本（同时支持 6000.3 LTS）
- **Git**（Build 模块自动版本控制需要）

### 快速开始

```bash
git clone https://github.com/MaiKuraki/UnityStarter.git
```

1. **在 Unity 中打开** — 将 `UnityStarter/` 目录添加到 Unity Hub
2. **探索架构** — 从 `GameplayFramework` 开始了解项目架构
3. **阅读文档** — 每个模块均有 `README.md` + `README.SCH.md`
4. **配置构建** — 详见 [Build 模块文档](UnityStarter/Assets/Build/README.SCH.md)

### 在现有项目中使用单个模块

从 `UnityStarter/Assets/ThirdParty/CycloneGames/` 复制需要的模块文件夹到你自己的项目。查看模块的 README 了解依赖关系——大多数模块是自包含的。

## 技术栈

### 运行时

| 包 | 用途 |
| --- | --- |
| `UniTask` (Cysharp) | 零分配 async/await |
| `R3` (Cysharp) | 响应式数据流 |
| `VContainer` (hadashiA) | DI/IoC 容器 |
| `VitalRouter` (hadashiA) | 消息路由 |
| `VYaml` (hadashiA) | YAML 序列化 |
| `LitMotion` (annulusgames) | Tween 动画 |
| `PrimeTween` | 备选 Tween 后端 |
| `MessagePack-CSharp` | 二进制序列化 |
| `Unity Debug Sheet` (harumak) | 游戏内调试面板 |

### 构建与管线

| 工具 | 用途 |
| --- | --- |
| `HybridCLR` | C# 代码热更新 |
| `YooAsset` / `Addressables` | 资源热更新与管理 |
| `Obfuz` / `Obfuz4HybridCLR` | 代码混淆 |
| `Mirror` / `Mirage` | 网络传输层 |
| `Navigathena` | 场景管理 |
| `Unity MCP` | AI 辅助开发 |

## 相关项目

- **[Rhythm Pulse](https://github.com/MaiKuraki/RhythmPulse)** — 音乐游戏机制合集
- **[Unity GAS Sample](https://github.com/MaiKuraki/UnityGameplayAbilitySystemSample)** — GAS 系统演示项目

---

**许可证**: [MIT](LICENSE) · **支持**: [GitHub Issues](https://github.com/MaiKuraki/UnityStarter/issues)
