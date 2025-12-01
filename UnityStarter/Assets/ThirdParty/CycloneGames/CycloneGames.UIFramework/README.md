# CycloneGames.UIFramework

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

A simple, robust, and data-driven UI framework for Unity, designed for scalability and ease of use. It provides a clear architecture for managing UI windows, layers, and transitions, leveraging asynchronous loading and a decoupled animation system.

## Features

- **Asynchronous by Design**: All resource loading and instantiation operations are fully asynchronous using `UniTask`, ensuring a smooth, non-blocking user experience.
- **Data-Driven**: Configure windows and layers with `ScriptableObject` assets for maximum flexibility and designer-friendliness.
- **Robust State Management**: A formal state machine manages the lifecycle of each `UIWindow`, preventing common bugs and race conditions.
- **Extensible Animation System**: Easily create and assign custom transition animations for windows.
- **Service-Based Architecture**: Integrates seamlessly with other services like `AssetManagement`, `Factory`, and `Logger`. Perfectly compatible with DI/IoC.
- **Performance-Minded**: Includes features like prefab caching, instantiation throttling, and a Dynamic Atlas system to maintain high performance.

## Core Architecture

The framework is built upon several key components that work together to provide a comprehensive UI management solution.

### 1. `UIService` (The Facade)
This is the primary public API for interacting with the UI system. Game code should use the `UIService` to open and close windows, abstracting away the underlying complexity. It acts as a clean entry point and handles the initialization of the `UIManager`.

### 2. `UIManager` (The Core)
A persistent singleton that orchestrates the entire UI lifecycle. Its responsibilities include:
- **Asynchronous Loading**: Loads `UIWindowConfiguration` and UI prefabs using `CycloneGames.AssetManagement`.
- **Lifecycle Management**: Manages the creation, destruction, and state transitions of `UIWindow` instances.
- **Resource Caching**: Implements an LRU cache for UI prefabs to optimize performance when reopening frequently used windows.
- **Instantiation Throttling**: Limits the number of UI elements instantiated per frame to prevent performance spikes.

### 3. `UIRoot` & `UILayer` (Scene Hierarchy)
- **`UIRoot`**: A required component in your scene that acts as the root for all UI elements. It contains the UI Camera and manages all `UILayer`s.
- **`UILayer`**: Represents a distinct rendering and input layer (e.g., `Menu`, `Dialogue`, `Notification`). Windows are added to specific layers, which control their sorting order and grouping. `UILayer`s are configured via `ScriptableObject` assets.

### 4. `UIWindow` (The UI Unit)
The base class for all UI panels, pages, or popups. Each `UIWindow` is a self-contained component with its own behavior and lifecycle, managed by a robust state machine:
- **`Opening`**: The window is being created and its opening transition is playing.
- **`Opened`**: The window is fully visible and interactive.
- **`Closing`**: The window's closing transition is playing.
- **`Closed`**: The window is hidden and ready to be destroyed.

### 5. `UIWindowConfiguration` (Data-Driven Configuration)
A `ScriptableObject` that defines the properties of a `UIWindow`. This data-driven approach decouples configuration from code, allowing designers to easily modify UI behavior without touching scripts. Key properties include:
- The UI prefab to instantiate.
- The `UILayer` the window belongs to.

### 6. `IUIWindowTransitionDriver` (Decoupled Animations)
An interface that defines how a window animates when opening and closing. This powerful abstraction allows you to implement transition logic using any animation system (e.g., Unity Animator, LitMotion, DOTween) and apply it to windows without modifying their core logic.

## Dynamic Atlas System

The framework includes a production-grade **Dynamic Atlas System** (`DynamicAtlasService`) to drastically reduce draw calls by batching UI sprites at runtime.

### Key Features
-   **Runtime Packing**: Combines individual textures into a single large texture at runtime.
-   **Multi-Page Support**: Automatically creates new atlas pages when the current one is full.
-   **Reference Counting**: Automatically frees space in the atlas when sprites are no longer in use.
-   **Zero-Config**: Works out of the box with reasonable defaults, or can be customized.

### Usage
```csharp
// Inject or get the service
IDynamicAtlas dynamicAtlas = ...; 

// Get a sprite (automatically loaded and packed)
Sprite sprite = dynamicAtlas.GetSprite("Icons/SkillIcon_01");
myImage.sprite = sprite;

// Release when done (decrements ref count, frees space if 0)
dynamicAtlas.ReleaseSprite("Icons/SkillIcon_01");
```

## Advanced Features

### Custom Transition Drivers
You can override the default open/close animations using `IUIWindowTransitionDriver`. This allows you to use **DOTween**, **LitMotion**, or Unity's **Animator**.

```csharp
public class MyTransitionDriver : IUIWindowTransitionDriver
{
    public async UniTask PlayOpenAsync(UIWindow window, CancellationToken ct) {
        // Example using LitMotion
        await LMotion.Create(0f, 1f, 0.5f)
            .BindToAlpha(window.GetComponent<CanvasGroup>())
            .ToUniTask(ct);
    }
    // ... PlayCloseAsync implementation
}
```

### Performance Optimization Tools

#### `OptimizeHierarchy`
Right-click your `UIWindow` component in the Inspector and select **Optimize Hierarchy**. This tool scans your UI hierarchy and disables `RaycastTarget` on non-interactive elements (like decorative Images or Texts), significantly reducing the cost of Unity's event system raycasts.

#### `SetVisible` API
Use `window.SetVisible(bool)` instead of `gameObject.SetActive(bool)`.
-   **SetVisible**: Toggles `CanvasGroup.alpha`, `interactable`, and `blocksRaycasts`. This avoids the expensive rebuilding of the UI layout and mesh that happens when enabling/disabling GameObjects.

## Dependencies

- `com.cysharp.unitask`
- `com.cyclone-games.assetmanagement`
- `com.cyclone-games.factory`
- `com.cyclone-games.logger`
- `com.cyclone-games.service`

## Quick Start Guide

### 1. Scene Setup
1.  Find the `UIFramework.prefab` in package, place it in the scene or load it runtime, the `UIFramework.prefab` already contains UIRoot, UICamera，default Layers.

### 2. Create `UILayer` Configurations
1.  In the Project window, right-click and select **Create > CycloneGames > UIFramework > UILayer Configuration**.
2.  Create configurations for each layer you need, for example:
    - `UILayer_Menu`
    - `UILayer_Dialogue`
    - `UILayer_Notification`
3.  Assign these `UILayer` assets to the `UIRoot`'s `Layer Configurations` list in the Inspector.

### 3. Create a `UIWindow`
1.  **Create the Script**: Create a new C# script that inherits from `UIWindow`. For example, `MainMenuWindow.cs`.
    ```csharp
    using CycloneGames.UIFramework.Runtime;

    public class MainMenuWindow : UIWindow
    {
        // Add references to your UI elements (buttons, text, etc.) here
    }
    ```
2.  **Create the Prefab**: Create a new UI `Canvas` or `Panel` in your scene. Add your `MainMenuWindow` component to its root `GameObject`. Design your UI, then save it as a prefab.
3.  **Create the Configuration**: Right-click in the Project window and select **Create > CycloneGames > UIFramework > UIWindow Configuration**.
    - Name it something descriptive, like `UIWindow_MainMenu`.
    - Assign your `MainMenuWindow` prefab to the `Window Prefab` field.
    - Assign the appropriate `UILayer` (e.g., `UILayer_Menu`) to the `Layer` field.

### 4. Initialize and Use the `UIService`
In your game's bootstrap or initialization logic, create and initialize the `UIService`.

```csharp
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private IUIService uiService;

    async void Start()
    {
        // Assume other services (asset management, factory, etc.) are already initialized
        // and available through a service locator or dependency injection.
        var assetPathBuilderFactory = ...; // Get from your DI container
        var objectSpawner = ...;         // Get from your DI container
        var mainCameraService = ...;     // Get from your DI container

        uiService = new UIService();
        uiService.Initialize(assetPathBuilderFactory, objectSpawner, mainCameraService);

        // Now you can use the service
        OpenMainMenu();
    }

    public async UniTask OpenMainMenu()
    {
        // "UIWindow_MainMenu" is the filename of your UIWindowConfiguration asset
        UIWindow window = await uiService.OpenUIAsync("UIWindow_MainMenu");
        if (window is MainMenuWindow mainMenu)
        {
            // Interact with your specific window instance
        }
    }

    public void CloseMainMenu()
    {
        uiService.CloseUI("UIWindow_MainMenu");
    }
}

## Architecture Patterns (MVC/MVP)

While `CycloneGames.UIFramework` is architecture-agnostic, it is designed to support structured patterns like **MVC (Model-View-Controller)** or **MVP (Model-View-Presenter)**.

### The View (`UIWindow`)
Your `UIWindow` subclass acts as the **View**. It should:
-   Hold references to UI components (Buttons, Texts).
-   Expose methods to update the visualization (e.g., `SetHealth(float value)`).
-   Expose events for user interactions (e.g., `OnPlayClicked`).
-   **Avoid** containing complex business logic.

### The Controller / Presenter
You can implement a separate Controller class or use the `UIWindow` as a lightweight controller.
-   **Controller**: Subscribes to `UIWindow` events, interacts with the game model/services, and updates the View.
-   **Model**: Pure C# classes holding your game data.

**Example (MVP):**
```csharp
public class MainMenuWindow : UIWindow // The View
{
    [SerializeField] private Button playButton;
    public event Action OnPlayClicked;

    protected override void Awake() {
        base.Awake();
        playButton.onClick.AddListener(() => OnPlayClicked?.Invoke());
    }
}

public class MainMenuController // The Presenter
{
    private MainMenuWindow _view;
    private GameService _gameService;

    public MainMenuController(MainMenuWindow view, GameService gameService) {
        _view = view;
        _gameService = gameService;
        _view.OnPlayClicked += HandlePlay;
    }

    private void HandlePlay() {
        _gameService.StartGame();
        _view.Close();
    }
}
```