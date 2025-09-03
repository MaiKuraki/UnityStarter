# CycloneGames.UIFramework

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

一个为 Unity 设计的简洁、健壮且数据驱动的 UI 框架，旨在实现可扩展性和易用性。它为管理 UI 窗口、层级和过渡动画提供了清晰的架构，并利用了异步加载和解耦的动画系统。

## 核心架构

该框架由几个关键组件构建而成，它们协同工作，提供了一套全面的 UI 管理解决方案。

### 1. `UIService` (门面)
这是与 UI 系统交互的主要公共 API。游戏逻辑代码应通过 `UIService` 来打开和关闭窗口，从而将底层的复杂性抽象出来。它作为一个清晰的入口点，并负责 `UIManager` 的初始化。

### 2. `UIManager` (核心)
一个持久化的单例，负责协调整个 UI 的生命周期。其职责包括：
- **异步加载**: 使用 `CycloneGames.AssetManagement` 异步加载 `UIWindowConfiguration` 和 UI 预制体。
- **生命周期管理**: 管理 `UIWindow` 实例的创建、销毁和状态转换。
- **资源缓存**: 实现了一个 LRU (最近最少使用) 缓存来存储 UI 预制体，以优化重开常用窗口时的性能。
- **实例化节流**: 限制每帧实例化的 UI 元素数量，以防止性能峰值。

### 3. `UIRoot` & `UILayer` (场景层级)
- **`UIRoot`**: 场景中必需的组件，作为所有 UI 元素的根节点。它包含 UI 相机并管理所有的 `UILayer`。
- **`UILayer`**: 代表一个独立的渲染和输入层级（例如 `Menu`, `Dialogue`, `Notification`）。窗口被添加到特定的层级中，由层级控制其排序顺序和分组。`UILayer` 通过 `ScriptableObject` 资产进行配置。

### 4. `UIWindow` (UI 单元)
所有 UI 面板、页面或弹窗的基类。每个 `UIWindow` 都是一个自包含的组件，拥有自己的行为和生命周期，由一个健壮的状态机管理：
- **`Opening`**: 窗口正在被创建，其打开过渡动画正在播放。
- **`Opened`**: 窗口完全可见并可交互。
- **`Closing`**: 窗口的关闭过渡动画正在播放。
- **`Closed`**: 窗口已隐藏并准备被销毁。

### 5. `UIWindowConfiguration` (数据驱动配置)
一个 `ScriptableObject`，用于定义 `UIWindow` 的属性。这种数据驱动的方法将配置与代码解耦，使设计师能够轻松修改 UI 行为而无需接触脚本。关键属性包括：
- 需要实例化的 UI 预制体。
- 窗口所属的 `UILayer`。

### 6. `IUIWindowTransitionDriver` (解耦的动画)
一个接口，定义了窗口在打开和关闭时的动画方式。这个强大的抽象允许您使用任何动画系统（如 Unity Animator, LitMotion, DOTween）来实现过渡逻辑，并将其应用于窗口，而无需修改其核心逻辑。

## 特性

- **原生异步**: 所有资源加载和实例化操作都使用 `UniTask` 完全异步执行，确保流畅、无阻塞的用户体验。
- **数据驱动**: 使用 `ScriptableObject` 资产配置窗口和层级，以实现最大的灵活性和设计师友好性。
- **健壮的状态管理**: 通过正式的状态机管理每个 `UIWindow` 的生命周期，防止常见的错误和竞态条件。
- **可扩展的动画系统**: 轻松为窗口创建和分配自定义的过渡动画。
- **面向服务的架构**: 与 `AssetManagement`, `Factory`, `Logger` 等其他服务无缝集成，接口编程可以完美兼容各 DI/IoC 框架。
- **注重性能**: 包含预制体缓存和实例化节流等功能，以保持高性能。

## 依赖项

- `com.cysharp.unitask`
- `com.cyclone-games.assetmanagement`
- `com.cyclone-games.factory`
- `com.cyclone-games.logger`
- `com.cyclone-games.service`

## 快速上手指南

### 1. 场景设置
1.  找到模块中的 `UIFramework.prefab` 预制体，将其加载或直接放进场景，其中已包含了基础的 UIRoot，UICamera，以及默认层级

### 2. 创建 `UILayer` 配置
1.  在项目窗口中，右键单击并选择 **Create > CycloneGames > UIFramework > UILayer Configuration**。
2.  为您需要的每个层级创建配置，例如：
    - `UILayer_Menu`
    - `UILayer_Dialogue`
    - `UILayer_Notification`
3.  将这些 `UILayer` 资产分配到 Inspector 中 `UIRoot` 的 `Layer Configurations` 列表中。

### 3. 创建 `UIWindow`
1.  **创建脚本**: 创建一个新的 C# 脚本，继承自 `UIWindow`。例如，`MainMenuWindow.cs`。
    ```csharp
    using CycloneGames.UIFramework.Runtime;

    public class MainMenuWindow : UIWindow
    {
        // 在此处添加对您的 UI 元素（按钮、文本等）的引用
    }
    ```
2.  **创建预制体**: 在场景中创建一个新的 UI `Canvas` 或 `Panel`。将其根 `GameObject` 添加 `MainMenuWindow` 组件。设计您的 UI，然后将其另存为预制体。
3.  **创建配置**: 在项目窗口中右键单击，选择 **Create > CycloneGames > UIFramework > UIWindow Configuration**。
    - 为其指定一个描述性的名称，如 `UIWindow_MainMenu`。
    - 将您的 `MainMenuWindow` 预制体分配给 `Window Prefab` 字段。
    - 将适当的 `UILayer`（例如 `UILayer_Menu`）分配给 `Layer` 字段。

### 4. 初始化并使用 `UIService`
在游戏的启动或初始化逻辑中，创建并初始化 `UIService`。

```csharp
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private IUIService uiService;

    async void Start()
    {
        // 假设其他服务（资产管理、工厂等）已经初始化
        // 并可通过服务定位器或依赖注入获得。
        var assetPathBuilderFactory = ...; // 从您的 DI 容器获取
        var objectSpawner = ...;         // 从您的 DI 容器获取
        var mainCameraService = ...;     // 从您的 DI 容器获取

        uiService = new UIService();
        uiService.Initialize(assetPathBuilderFactory, objectSpawner, mainCameraService);

        // 现在您可以使用该服务了
        OpenMainMenu();
    }

    public async UniTask OpenMainMenu()
    {
        // "UIWindow_MainMenu" 是您的 UIWindowConfiguration 资产的文件名
        UIWindow window = await uiService.OpenUIAsync("UIWindow_MainMenu");
        if (window is MainMenuWindow mainMenu)
        {
            // 与您的特定窗口实例进行交互
        }
    }

    public void CloseMainMenu()
    {
        uiService.CloseUI("UIWindow_MainMenu");
    }
}
