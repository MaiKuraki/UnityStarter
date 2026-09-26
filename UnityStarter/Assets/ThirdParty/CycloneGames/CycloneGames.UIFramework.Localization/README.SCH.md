# CycloneGames.UIFramework.Localization

[English | 简体中文](README.md)

CycloneGames.UIFramework.Localization 是可选的 companion 模块，负责把 [CycloneGames.Localization](../CycloneGames.Localization/README.SCH.md) 绑定到 [CycloneGames.UIFramework](../CycloneGames.UIFramework/README.SCH.md) 的窗口上。它为窗口提供事务性、窗口作用域的本地化绑定，并在 TextMeshPro 可用时额外提供按语言区分的布局快照。

两个包都不依赖本模块。`CycloneGames.UIFramework` 在没有 `CycloneGames.Localization` 时照样编译，`CycloneGames.Localization` 在没有 `CycloneGames.UIFramework` 时也照样编译。本模块是二者唯一的交汇点。

## 目录

- [CycloneGames.UIFramework.Localization](#cyclonegamesuiframeworklocalization)
  - [目录](#目录)
  - [概述](#概述)
  - [核心类型](#核心类型)
  - [快速上手](#快速上手)
  - [TextMeshPro 分层](#textmeshpro-分层)
  - [线程与生命周期](#线程与生命周期)
  - [移除](#移除)
  - [故障排查](#故障排查)

## 概述

`LocalizationWindowBinder` 是一个 `IUIWindowBinder`。它对已实例化的窗口层级只扫描一次，按层级顺序绑定所有 `ILocalizationBindingTarget`，任一绑定失败时按逆序回滚，窗口关闭时同样按逆序解绑。绑定完成后语言切换不会重新扫描层级，已绑定的目标自行响应语言变更。

`UILocaleLayout` 是一个 `ILocalizationBindingTarget`，它把按语言区分的几何与排版快照存放在预制体旁，并在提交的语言变更时应用。因为 `TrackedElement.Text` 是 `TMP_Text`，它依赖 TextMeshPro。详见[本地化界面布局](Documents~/LocalizedLayouts.SCH.md)。

```mermaid
flowchart LR
    App["应用组合根"] --> Service["UIService，持有 binder 列表"]
    Service --> Binder["LocalizationWindowBinder"]
    Loc["ILocalizationService"] --> Binder
    Binder --> Window["UIWindow 层级"]
    Window --> Targets["ILocalizationBindingTarget 组件"]
    Targets --> Layout["UILocaleLayout（仅 TextMeshPro）"]
```

## 核心类型

| 类型 | 程序集 | 职责 |
| --- | --- | --- |
| `LocalizationWindowBinder` | Runtime | `IUIWindowBinder`，绑定窗口层级内所有 `ILocalizationBindingTarget` |
| `UILocaleLayout` | Runtime TextMeshPro | `MonoBehaviour` 兼 `ILocalizationBindingTarget`，应用按语言的布局快照 |
| `TrackedElement` | Runtime TextMeshPro | 单个被追踪的 RectTransform、文本或布局组 |
| `ElementSnapshot` | Runtime TextMeshPro | 单个被追踪元素在某语言下的捕获值 |
| `LocaleSnapshot` | Runtime TextMeshPro | 某语言下的全部元素快照 |
| `UIFrameworkLocalizationEditorLog` | Editor | 本地化编辑工具使用的内部日志门面 |

## 快速上手

在 Unity 主线程上创建 binder，并把它传给 `UIService`。本地化服务必须已经初始化。

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Localization.Runtime;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations.Localization;
using UnityEngine;

public sealed class GameUiBootstrap : MonoBehaviour
{
    [SerializeField] private UIRoot uiRoot;
    [SerializeField] private UIWindowConfiguration startupWindow;

    private IUIService _ui;

    private async UniTask RunAsync(
        ILocalizationService localization,
        CancellationToken lifetimeToken)
    {
        IUIWindowBinder[] binders = { new LocalizationWindowBinder(localization) };

        _ui = new UIService(uiRoot, options: null, binders: binders);
        await _ui.OpenAsync(startupWindow, cancellationToken: lifetimeToken);
    }
}
```

`LocalizationWindowBinder` 还接受一个可选的 `IAssetPackage`。当本地化资源需要通过指定包解析、而不是走默认包时传入。

不由 `UIService` 管理的独立 Canvas 可以改由产品层组合根直接绑定 `UILocaleLayout`，这条路径不需要窗口 binder。

## TextMeshPro 分层

`TrackedElement.Text` 是 `TMP_Text`，因此 `UILocaleLayout` 位于独立程序集中，TextMeshPro 不可用时该程序集被整体排除。不含 TMP 的程序集不持有任何编译期 TextMeshPro 依赖，所以 `LocalizationWindowBinder` 在所有 Unity 版本上都能继续绑定图片、音频和自定义目标。

| Unity 分支 | TextMeshPro 的提供方式 | 宏来源 |
| --- | --- | --- |
| Unity 2022 LTS 及更早 | 独立的 `com.unity.textmeshpro` 包 | 第一条 `versionDefines` 规则 |
| Unity 2023.2 与 Unity 6（6000.0+） | 内置于 `com.unity.ugui` 2.0.0 | 第二条 `versionDefines` 规则 |

不要把 `CYCLONEGAMES_HAS_TEXTMESHPRO` 加进 Player Settings。陈旧的手写宏会在没有 TextMeshPro 的情况下强制编译，直接把构建打挂。

## 线程与生命周期

构造、绑定与释放都限制在 Unity 主线程。binder 会捕获自己的属主线程，并拒绝来自其它线程的调用。组件发现每个窗口实例只做一次，语言切换不会重新扫描层级。

binder 参与 `UIService` 的开启事务。任一目标绑定失败时，已创建的绑定按逆序释放，开启操作整体中止。窗口关闭同样按逆序释放。

## 移除

删除模块目录；在 `Packages/` 布局的项目里则是移除对应的 manifest 条目。两个宿主包都会继续编译。曾经挂过 `UILocaleLayout` 的预制体和场景会报 missing script，其序列化快照数据不再被加载。移除前先备份这些资产。

## 故障排查

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `CS0234: The type or namespace name 'Localization' does not exist in the namespace 'CycloneGames'` | 没有安装 `CycloneGames.Localization` | 安装 `CycloneGames.Localization`，或移除本模块 |
| `CS0012: The type 'IAssetPackage' is defined in an assembly that is not referenced` | 调用方构造 `LocalizationBindingContext` 但没有引用 `CycloneGames.AssetManagement.Runtime` | 补上该引用；可选构造参数会强制要求它 |
| `CS0122: 'UIFrameworkLocalizationEditorLog' is inaccessible` | Editor 的 TextMeshPro 切片看不到内部日志门面 | 保留为该程序集声明 `InternalsVisibleTo` 的 `AssemblyInfo.cs` |
| `UILocaleLayout` 报 missing script | TextMeshPro 切片被排除了 | 安装 TextMeshPro；Unity 6 上确认 uGUI 为 2.0.0 或更高 |
| `Bind` 抛 `InvalidOperationException` | 本地化服务尚未初始化 | 开窗前先初始化 `ILocalizationService` |
