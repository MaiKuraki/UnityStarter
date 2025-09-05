# Unity 项目启动模板

这是一个轻量级、模块化的 Unity 项目模板，旨在为您的新项目提供一个坚实的基础。它集成了借鉴**虚幻引擎**理念的 **Gameplay Framework**、**Gameplay Tags** 和 **Gameplay Ability System**，以及**高性能、低 GC** 的资源、对象池和音频管理系统。此外，项目还包含实用的 Debug 工具，并支持多种 **DI/IoC 框架**。所有模块均以解耦清晰的 Unity Package 形式开发，且已针对 **Android, iOS, WebGL** 等多平台进行了优化。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

> [!NOTE]
> 如果你觉得这个项目对你有帮助，请点一个 Star ⭐，谢谢！

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/MaiKuraki/UnityStarter)

## 💉 DI / IoC
此模板支持多种依赖注入框架。您可以切换不同的 Git 分支来查看各个框架的专用示例。**GameplayFramework** 和 **Factory** 模块包含了具体的 DI 实现范例。

- **DI/IoC 框架支持**，预配置适配器：(列出的这些 DI/IoC 框架均为作者在大型商业项目中验证使用过的)
  - [VContainer](https://github.com/hadashiA/VContainer)
  - [StrangeIoC](https://github.com/strangeioc/strangeioc)
  - [Extenject(Zenject)](https://github.com/Mathijs-Bakker/Extenject) (该项目作者宣布不积极维护)
> 由于 [**Zenject**](https://github.com/Mathijs-Bakker/Extenject) 作者宣布[停止对项目的更新](https://github.com/Mathijs-Bakker/Extenject/issues/73)，这里我更推荐尝试 [**VContainer**](https://github.com/hadashiA/VContainer)，如果你希望对项目更高度的自定义，则更推荐 [**StrangeIoC**](https://github.com/strangeioc/strangeioc)。如果你希望使用 [**Zenject**](https://github.com/Mathijs-Bakker/Extenject)，那么 [**MessagePipe**](https://github.com/Cysharp/MessagePipe) 也是一个可以搭配的消息框架。

### DI 框架选择指南 
通过切换 Git 分支可查看各 DI 框架的实现范例。<br/>
注意：**GameplayFramework** 与 **Factory** 拥有针对 DI 编写的示例<br/>
<img src="./Docs/ProjectDescription/Main/Des_01.png" alt="Branch Select" style="width: 50%; height: auto; max-width: 360px;" />

---

## ✨ 核心特性

-   **模块化架构**：所有系统都构建为解耦的 Unity Package (包括独立的 asmdef 以及配置完成的 package.json)，让您可以轻松地包含或排除功能。
-   **借鉴于虚幻引擎**：实现了经过验证的设计理念，如游戏玩法框架（Gameplay Framework）、游戏能力系统（GAS）和游戏标签（Gameplay Tags）。
-   **性能优先**：在日志、工厂和音频等关键系统中专注于低/零 GC 分配。
-   **DI/IoC 就绪**：预配置了对 **VContainer**、**StrangeIoC** 和 **Zenject** 的支持。
-   **CI/CD 友好**：包含可通过命令行访问的构建脚本和自动版本控制，可无缝集成到自动化流水线中。
-   **跨平台**：已为桌面、移动端（Android/iOS）和 WebGL 进行了优化。

---

## 核心框架模块

### 🎮 游戏玩法系统
- **[GameplayFramework](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework)** - 虚幻引擎风格的游戏框架，包含 Actor、Pawn、Controller、GameMode 概念。支持 DI 的可扩展游戏项目架构。
- **[GameplayAbilities](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities)** - 强大的数据驱动技能系统，灵感来自虚幻引擎的 GAS。支持复杂技能、属性、状态效果，基于 ScriptableObject 设计。
- **[GameplayTags](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags)** - 基于标签的识别系统，用于技能、效果和游戏状态，灵感来自虚幻引擎的 GameplayTags。支持运行时动态标签注册和自动生成。
- **[RPGFoundation](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation)** - 包含 RPG 类游戏的基础拓展。

### 🏗️ 核心基础设施  
- **[Factory](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Factory)** - 高性能、低 GC 的工厂和对象池工具。线程安全的自动扩缩容池，O(1) 操作复杂度。
- **[Logger](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logger)** - 零/低 GC 日志系统，支持可插拔处理策略。支持线程化工作模式、文件轮转和跨平台兼容（包括 WebGL）。
- **[AssetManagement](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AssetManagement)** - DI 优先的资源管理抽象层，集成 [YooAsset](https://github.com/tuyoogame/YooAsset)。支持下载、缓存、版本管理，兼容 Addressables/[Navigathena](https://github.com/mackysoft/Navigathena)。
- **[Audio](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio)** - 一个高性能、低 GC、类似 Wwise 操作体验的，使用了 Unity 原生 Audio 功能的高级功能拓展。

### 🎯 输入与界面
- **[InputSystem](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem)** - 响应式输入封装，支持上下文栈、本地多人、键盘双人、自动检测新设备接入、基于 YAML 的游戏运行时修改键位配置。使用 R3 Observable 构建。
- **[UIFramework](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework)** - 层级式 UI 管理系统，支持基于层的组织、转场动画和资源集成。

### 🛠️ 工具与服务
- **[Utility](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Utility)** - 通用工具集，包含 FPS 计数器、安全区域适配、文件操作、性能工具和 Unity 启动画面控制。
- **[Service](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Services)** - 游戏服务抽象层，用于摄像机管理、图形设置和设备配置，支持基于 YAML 的设置。
- **[Cheat](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Cheat)** - 类型安全的调试命令管道，集成 [VitalRouter](https://github.com/hadashiA/VitalRouter)。支持异步操作和线程安全执行。
- **[FontAssets](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.FontAssets)** - 多语言字体集合和字符集，支持拉丁文、中文（简体/繁体）、日文和韩文本地化。

### 🌐 网络
- **[Networking](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Networking)** - 网络抽象层，提供 [Mirror](https://github.com/MirrorNetworking/Mirror) 适配器。为传输、序列化和技能系统集成提供接口。

### 🧰 工具
- **[Tools](Tools)** - 一系列实用工具脚本，旨在简化 Unity 开发和常规项目管理中的常见任务。

## 项目结构说明
项目主要源码位于 `UnityStarter/Assets/ThirdParty/` 目录下。采用 **Unity Package** 形式开发，分离 asmdef 设计，可轻易的选择移除不需要的包。

```
.
├── Docs/                               # 项目文档
├── Tools/                              # 实用工具集 (含项目重命名、清理工具等)
└── UnityStarter/                       # Unity 主工程
    ├── Assets/
    │   ├── Editor/
    │   │   ├── BuildScript.cs          # 用于 CI/CD 的构建工具
    │   │   └── ...
    │   ├── ThirdParty/
    │   │   ├── CycloneGames/           # 核心开发套件
    │   │   │   ├── AssetManagement/    # 资源加载和版本管理
    │   │   │   ├── Audio/              # 增强型音频管理系统
    │   │   │   ├── Cheat/              # 调试命令管道系统
    │   │   │   ├── Factory/            # 高性能对象池
    │   │   │   ├── FontAssets/         # 多语言字体集合
    │   │   │   ├── GameplayAbilities/  # 数据驱动技能系统（类似 UnrealEngine GAS）
    │   │   │   ├── GameplayFramework/  # UE 风格游戏架构（类似 UnrealEngine GameplayFramework）
    │   │   │   ├── GameplayTags/       # 基于标签的识别系统（类似 UnrealEngine GameplayTags）
    │   │   │   ├── InputSystem/        # 响应式输入管理，支持上下文栈
    │   │   │   ├── Logger/             # 零 GC 多线程日志
    │   │   │   ├── Networking/         # 网络抽象层
    │   │   │   ├── RPGFoundation/      # RPG 基础组件 (例如, Movement)
    │   │   │   ├── Service/            # 通用游戏服务抽象
    │   │   │   ├── UIFramework/        # 层级式 UI 管理
    │   │   │   └── Utility/            # 性能工具和实用程序
    │   │   └── ...
    │   └── ...
    ├── Packages/                       # 包清单与配置
    └── ProjectSettings/                # Unity 项目设置
```

---

## 🚀 快速上手

### 环境要求
- **Unity 2022.3+**

### 作为完整的项目模板使用
1.  **克隆或下载** 此仓库。
2.  **找到重命名工具**：在 `Tools/Executable` 目录下找到 `rename_project` 可执行文件。
3.  **移动工具**：将该可执行文件复制到项目根目录 (`UnityStarter/`)。
4.  **运行工具**：从命令行执行它。该工具将引导您完成重命名项目文件夹、公司名称和应用名称等所有必要步骤。
5.  **在 Unity 中打开**：现在您可以在 Unity 中打开重命名后的项目文件夹。

### 使用项目中的特定模块
对于现有项目，您可以导入单个模块：

-   **简单方式**：从 `UnityStarter/Assets/ThirdParty/CycloneGames/` 目录中复制所需的包文件夹到您项目的 `Assets` 文件夹中。
-   **推荐方式**：为保持项目整洁，建议将包文件夹移动到 `Assets` 目录之外的位置。然后，在 Unity 中通过 **Package Manager** 的 **"Add package from disk..."** 选项，选择每个模块的 `package.json` 文件进行导入。

---

## ⚙️ 架构与技术细节

### 关键技术栈
- **Unity 2022.3+**
- **异步编程**: [UniTask](https://github.com/Cysharp/UniTask)
- **响应式编程**: [R3](https://github.com/Cysharp/R3) (前身为 UniRx)
- **动画/缓动**: [LitMotion](https://github.com/Cysharp/LitMotion)
- **序列化**: [VYaml](https://github.com/hadashiA/VYaml) 用于 YAML 配置
- **消息总线**: [VitalRouter](https://github.com/hadashiA/VitalRouter)
- **资源管理**: [YooAsset](https://github.com/tuyoogame/YooAsset)
- **网络**: [Mirror](https://github.com/MirrorNetworking/Mirror)

### 架构亮点
- **依赖注入支持**：所有模块都设计为可与 DI 容器无缝协作。
- **程序集定义隔离**：所有模块强制以独立的 asmdef 实现清晰的代码分离。
- **ScriptableObject 配置**：利用数据驱动设计来管理能力、效果和设置。
- **线程安全设计**：`Logger` 和 `Factory` 等核心系统专为多线程环境设计。
- **零/低 GC**：所有模块在关键循环中最小化垃圾回收来优化性能。

---

## 🚀 构建与 CI/CD

此模板专为自动化构建和与 CI/CD 流水线无缝集成而设计。

-   **自动化构建脚本**：项目在 `Assets/Editor/BuildScript.cs` 中包含一个强大的构建脚本。它在 Unity 编辑器中提供菜单项，支持一键为多个平台（Windows、Mac、Android、WebGL）进行构建。

-   **自动版本控制**：构建版本号会根据 Git 的提交次数自动生成。版本号格式为 `vX.Y.CommitCount`（例如 `v0.1.123`），确保每个构建版本都有唯一且可追溯的标识。

-   **运行时版本信息**：每次构建前，脚本会捕获当前的 Git 提交哈希、提交总数和构建日期，并将这些信息保存到 `VersionInfoData` ScriptableObject（位于 `Assets/UnityStarter/Scripts/Build/VersionInfoData.cs`）。这使您可以在应用程序内轻松显示详细的构建信息，便于调试和技术支持。

-   **CI/CD 就绪**：所有构建方法都可以通过命令行触发，从而轻松与 Jenkins、TeamCity 等 CI/CD 系统集成。

## 基于此项目的其他开源项目

- [x] [Rhythm Pulse](https://github.com/MaiKuraki/RhythmPulse) 一款集合所有常见音乐游戏玩法的开源项目，目前还在开发中。
- [x] [Unity Gameplay Ability System Sample](https://github.com/MaiKuraki/UnityGameplayAbilitySystemSample) 为 Unity 设计的类虚幻引擎 GAS 系统的示例项目。
