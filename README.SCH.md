# Unity 项目启动模板

本项目是一个轻量级的 Unity 启动模板，旨在作为新项目的基础框架，整合了常用的开源包并为多种 DI/IoC 框架提供了支持。

<p align="center">
    <br> <a href="README.md">English</a> | 简体中文
</p>

## 概述
一个轻量级的 Unity 启动模板，旨在作为新项目的基础框架。本仓库整合了以下内容：

- **流行的开源 Unity Package 包**
- **DI/IoC 框架支持**，预配置适配器：(列出的这些 DI/IoC 框架均为作者在商业项目中验证使用过的)
  - [VContainer](https://github.com/hadashiA/VContainer)
  - [StrangeIoC](https://github.com/strangeioc/strangeioc)
  - [Extenject(Zenject)](https://github.com/Mathijs-Bakker/Extenject) (该项目作者宣布不积极维护)
> 由于 [**Zenject**](https://github.com/Mathijs-Bakker/Extenject) 作者宣布[停止对项目的更新](https://github.com/Mathijs-Bakker/Extenject/issues/73)，这里我更推荐尝试 [**VContainer**](https://github.com/hadashiA/VContainer)，如果你希望对项目更高度的自定义，则更推荐 [**StrangeIoC**](https://github.com/strangeioc/strangeioc)。如果你希望使用 [**Zenject**](https://github.com/Mathijs-Bakker/Extenject)，那么 [**MessagePipe**](https://github.com/Cysharp/MessagePipe) 也是一个可以搭配的消息框架。

## DI 框架选择指南 
通过切换 Git 分支可查看各 DI 框架的实现范例<br/>
<img src="./Docs/ProjectDescription/Main/Des_01.png" alt="Branch Select" style="width: 50%; height: auto; max-width: 360px;" />

## 使用项目
如果你打算直接使用此项目作为 Unity 的起始项目。那么推荐使用 Tools/ 文件夹下的 rename_project 的 go 语言脚本或者 exe 可执行文件，把它们复制到 UnityStarter 文件夹下(与 unity_project_full_clean.exe 同级目录) 对项目进行重命名，当然 UnityStarter 需要手动修改。如果不希望使用项目只使用项目中的某些模块，可以直接从 Assets/CycloneGames 目录拿走需要的 Package 包。

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
    │   │   │   ├── Cheat/              # 简单的 Cheat 系统
    │   │   │   ├── Factory/            # 工厂与对象池
    │   │   │   ├── GameplayAbilities/  # 类虚幻引擎的 Gameplay Ability 系统
    │   │   │   ├── GameplayFramework/  # 类虚幻引擎的 Gameplay Framework
    │   │   │   ├── GameplayTags/       # 类虚幻引擎的 Gameplay Tags
    │   │   │   ├── InputSystem/        # 灵活高级的输入绑定系统
    │   │   │   ├── Logger/             # 高性能多线程的日志工具
    │   │   │   ├── Service/            # 通用游戏服务
    │   │   │   ├── UIFramework/        # 简单的层级 UI 框架
    │   │   │   └── Utility/            # 通用工具 (FPS 计数器等)
    │   │   └── ...
    │   └── ...
    ├── Packages/                       # 包清单与配置
    └── ProjectSettings/                # Unity 项目设置
