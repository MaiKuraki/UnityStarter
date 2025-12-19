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

## Dependencies

- `com.cysharp.unitask`
- `com.cyclone-games.assetmanagement`
- `com.cyclone-games.factory`
- `com.cyclone-games.logger`
- `com.cyclone-games.service`

## Quick Start Guide

This guide will walk you through setting up and using the UIFramework step by step. Follow along to create your first UI window!

### Step 1: Scene Setup

1. **Locate the UIFramework Prefab**: Find the `UIFramework.prefab` in the package at `Runtime/Prefabs/UI/UIFramework.prefab`.
2. **Add to Scene**: Either:
   - Drag the prefab directly into your scene, or
   - Load it at runtime using your asset management system
3. **Verify Setup**: The prefab contains:
   - `UIRoot` component with UI Camera
   - Default `UILayer` configurations (Menu, Dialogue, Notification, etc.)

The `UIFramework.prefab` is pre-configured with essential components, so you can start using it immediately.

### Step 2: Create `UILayer` Configurations

`UILayer` configurations define the rendering and input layers for your UI windows. The framework comes with several default layers, but you can create custom ones.

1. **Create a New Layer Configuration**:

   - In the Project window, right-click and select **Create > CycloneGames > UIFramework > UILayer Configuration**
   - Name it descriptively, e.g., `UILayer_Menu`, `UILayer_Dialogue`, `UILayer_Notification`

2. **Configure the Layer**:

   - Open the `UILayerConfiguration` asset in the Inspector
   - Set the `Layer Name` (e.g., "Menu", "Dialogue")
   - Adjust the `Sorting Order` if needed (higher values render on top)

3. **Assign to UIRoot**:
   - Select the `UIRoot` GameObject in your scene
   - In the Inspector, find the `Layer Configurations` list
   - Add your newly created `UILayerConfiguration` assets to the list

**Example Layer Setup:**

```
UILayer_Menu (Sorting Order: 100)
UILayer_Dialogue (Sorting Order: 200)
UILayer_Notification (Sorting Order: 300)
```

### Step 3: Create Your First `UIWindow`

There are two ways to create a `UIWindow`: using the quick creation tool or manually. We'll cover both methods.

#### Method 1: Quick Creation (Recommended for Beginners)

The framework provides a convenient editor tool to create all necessary files at once.

1. **Open the UIWindow Creator**:

   - Go to **Tools > CycloneGames > UIWindow Creator** in the Unity menu bar
   - A window will open with all the creation options

2. **Fill in the Required Information**:

   - **Window Name**: Enter a descriptive name (e.g., `MainMenuWindow`, `HUDWindow`)
   - **Namespace** (Optional): If you use namespaces, enter it here (e.g., `MyGame.UI`)
   - **Script Save Path**: Drag a folder where the C# script will be saved
   - **Prefab Save Path**: Drag a folder where the prefab will be saved
   - **Configuration Save Path**: Drag a folder where the `UIWindowConfiguration` asset will be saved
   - **UILayer Configuration**: Select the `UILayerConfiguration` asset you created in Step 2
   - **Template Prefab** (Optional): You can drag a template prefab to use as a base

3. **Create the UIWindow**:
   - Click the **"Create UIWindow"** button
   - The tool will automatically create:
     - A C# script inheriting from `UIWindow`
     - A prefab with the script attached
     - A `UIWindowConfiguration` asset linking everything together

**Visual Guide:**

- <img src="./Documents~/UIWindowCreator_1.png" alt="UIWindow Creator 1" style="width: 100%; height: auto; max-width: 800px;" />
- <img src="./Documents~/UIWindowCreator_2.png" alt="UIWindow Creator 2" style="width: 100%; height: auto; max-width: 800px;" />

#### Method 2: Manual Creation

If you prefer to create files manually or need more control:

1. **Create the Script**:

   ```csharp
   using CycloneGames.UIFramework.Runtime;
   using UnityEngine;
   using UnityEngine.UI;

   public class MainMenuWindow : UIWindow
   {
       [SerializeField] private Button playButton;
       [SerializeField] private Button settingsButton;
       [SerializeField] private Button quitButton;

       protected override void Awake()
       {
           base.Awake();

           // Initialize button listeners
           if (playButton != null)
               playButton.onClick.AddListener(OnPlayClicked);
           if (settingsButton != null)
               settingsButton.onClick.AddListener(OnSettingsClicked);
           if (quitButton != null)
               quitButton.onClick.AddListener(OnQuitClicked);
       }

       private void OnPlayClicked()
       {
           Debug.Log("Play button clicked!");
           // Add your game start logic here
       }

       private void OnSettingsClicked()
       {
           Debug.Log("Settings button clicked!");
           // Add your settings logic here
       }

       private void OnQuitClicked()
       {
           Debug.Log("Quit button clicked!");
           Application.Quit();
       }
   }
   ```

2. **Create the Prefab**:

   - Create a new UI `Canvas` or `Panel` in your scene
   - Add your `MainMenuWindow` component to the root `GameObject`
   - Design your UI (add buttons, text, images, etc.)
   - Assign UI element references to the serialized fields in the Inspector
   - Save it as a prefab (drag from Hierarchy to Project window)

3. **Create the Configuration**:
   - Right-click in the Project window and select **Create > CycloneGames > UIFramework > UIWindow Configuration**
   - Name it `UIWindow_MainMenu` (the name you'll use to open the window)
   - In the Inspector:
     - Assign your `MainMenuWindow` prefab to the `Window Prefab` field
     - Assign the appropriate `UILayer` (e.g., `UILayer_Menu`) to the `Layer` field

### Step 4: Initialize and Use the `UIService`

The `UIService` is your main interface for opening and closing UI windows. You need to initialize it once at game startup.

#### Basic Initialization (Using Resources)

If you're using Unity's built-in `Resources.Load`:

```csharp
using CycloneGames.UIFramework.Runtime;
using CycloneGames.Factory.Runtime;
using CycloneGames.Service.Runtime;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private IUIService uiService;

    async void Start()
    {
        // Initialize asset management (using Resources)
        IAssetModule module = new ResourcesModule();
        await module.InitializeAsync(new AssetManagementOptions());
        var package = module.CreatePackage("DefaultResources");
        await package.InitializeAsync(default);
        AssetManagementLocator.DefaultPackage = package;

        // Create required services
        var assetPathBuilderFactory = new TemplateAssetPathBuilderFactory();
        var objectSpawner = new DefaultUnityObjectSpawner();
        var mainCameraService = new MainCameraService();

        // Initialize UIService
        uiService = new UIService();
        uiService.Initialize(assetPathBuilderFactory, objectSpawner, mainCameraService);

        // Now you can open UI windows!
        await OpenMainMenu();
    }

    public async UniTask OpenMainMenu()
    {
        // "UIWindow_MainMenu" is the filename of your UIWindowConfiguration asset
        UIWindow window = await uiService.OpenUIAsync("UIWindow_MainMenu");

        if (window != null && window is MainMenuWindow mainMenu)
        {
            Debug.Log("Main menu opened successfully!");
            // You can now interact with the window instance
        }
        else
        {
            Debug.LogError("Failed to open main menu window!");
        }
    }

    public void CloseMainMenu()
    {
        uiService.CloseUI("UIWindow_MainMenu");
    }
}
```

#### Advanced Initialization (Using Asset Packages)

If you're using Addressables, YooAsset, or other asset management systems:

```csharp
using CycloneGames.UIFramework.Runtime;
using CycloneGames.AssetManagement.Runtime;
// ... other using statements

public class GameInitializer : MonoBehaviour
{
    private IUIService uiService;
    private IAssetPackage uiPackage;

    async void Start()
    {
        // Initialize your asset management system
        // This example assumes you have an IAssetPackage instance
        uiPackage = await InitializeYourAssetPackageAsync();

        // Create required services
        var assetPathBuilderFactory = new YourAssetPathBuilderFactory();
        var objectSpawner = new DefaultUnityObjectSpawner();
        var mainCameraService = new MainCameraService();

        // Initialize UIService with package
        uiService = new UIService();
        uiService.Initialize(assetPathBuilderFactory, objectSpawner, mainCameraService, uiPackage);

        // Open UI windows
        await OpenMainMenu();
    }

    // ... rest of your code
}
```

### Step 5: Opening and Closing Windows

Once `UIService` is initialized, opening and closing windows is straightforward:

```csharp
// Open a window asynchronously (recommended)
UIWindow window = await uiService.OpenUIAsync("UIWindow_MainMenu");

// Open a window with callback (fire-and-forget)
uiService.OpenUI("UIWindow_MainMenu", (window) => {
    if (window != null)
        Debug.Log("Window opened!");
});

// Close a window
uiService.CloseUI("UIWindow_MainMenu");

// Close a window asynchronously
await uiService.CloseUIAsync("UIWindow_MainMenu");

// Check if a window is open
bool isOpen = uiService.IsUIWindowValid("UIWindow_MainMenu");

// Get a reference to an open window
UIWindow window = uiService.GetUIWindow("UIWindow_MainMenu");
if (window is MainMenuWindow mainMenu)
{
    // Interact with the window
}
```

### Step 6: Working with Window Lifecycle

Each `UIWindow` has a lifecycle managed by a state machine. You can override methods to hook into different states:

```csharp
public class MyWindow : UIWindow
{
    protected override void Awake()
    {
        base.Awake();
        Debug.Log("Window is being created");
    }

    // Called when window starts opening (before animation)
    protected override void OnStartOpen()
    {
        base.OnStartOpen();
        Debug.Log("Window is opening");
    }

    // Called when window finishes opening (after animation)
    protected override void OnFinishedOpen()
    {
        base.OnFinishedOpen();
        Debug.Log("Window is fully open and interactive");
    }

    // Called when window starts closing (before animation)
    protected override void OnStartClose()
    {
        base.OnStartClose();
        Debug.Log("Window is closing");
    }

    // Called when window finishes closing (after animation, before destruction)
    protected override void OnFinishedClose()
    {
        base.OnFinishedClose();
        Debug.Log("Window is closed and will be destroyed");
    }
}
```

## Dynamic Atlas System Tutorial

After mastering the basics of creating and opening UI windows, you can optimize your UI performance using the **Dynamic Atlas System**. This system reduces draw calls by combining multiple UI textures into a single atlas at runtime.

### What is Dynamic Atlas?

In Unity UI, each sprite texture typically requires a separate draw call. If you have 50 different icons on screen, that's potentially 50 draw calls. The Dynamic Atlas System packs these textures into a single large texture (atlas), allowing Unity to batch them together and reduce draw calls significantly.

**Benefits:**

- **Reduced Draw Calls**: Combine multiple textures into one, reducing CPU overhead
- **Better Performance**: Especially important on mobile devices
- **Runtime Packing**: No need to pre-create atlases - textures are packed on demand
- **Automatic Management**: Reference counting ensures textures are freed when no longer needed

### When to Use Dynamic Atlas?

Use Dynamic Atlas when:

- You have many small UI icons/sprites that change frequently
- You want to reduce draw calls without pre-creating static atlases
- Your UI uses many different textures that aren't always visible together
- You need runtime flexibility (e.g., loading icons from server)

Don't use Dynamic Atlas when:

- You have a small number of static UI elements (pre-created atlases are better)
- Your textures are very large (they'll be scaled down, losing quality)
- You need pixel-perfect rendering (atlas packing may introduce slight offsets)

### Step 1: Understanding the Three Usage Patterns

The Dynamic Atlas System provides three ways to use it, each suited for different scenarios:

#### Pattern 1: DynamicAtlasManager (Simplest - Recommended for Beginners)

This is the easiest way to get started. It uses a singleton pattern and works out of the box.

```csharp
using CycloneGames.UIFramework.DynamicAtlas;
using UnityEngine;
using UnityEngine.UI;

public class MyUIWindow : UIWindow
{
    [SerializeField] private Image iconImage;
    private string currentIconPath;

    protected override void Awake()
    {
        base.Awake();

        // Configure Dynamic Atlas (only needed once, typically in initialization)
        // This is optional - it will use defaults if not called
        DynamicAtlasManager.Instance.Configure(
            load: path => Resources.Load<Texture2D>(path),
            unload: (path, tex) => Resources.UnloadAsset(tex),
            size: 2048,  // Atlas page size in pixels
            autoScaleLargeTextures: true
        );
    }

    public void SetIcon(string iconPath)
    {
        // Release previous icon if any
        if (!string.IsNullOrEmpty(currentIconPath))
        {
            DynamicAtlasManager.Instance.ReleaseSprite(currentIconPath);
        }

        // Get sprite from atlas (automatically loads and packs if needed)
        Sprite sprite = DynamicAtlasManager.Instance.GetSprite(iconPath);

        if (sprite != null && iconImage != null)
        {
            iconImage.sprite = sprite;
            currentIconPath = iconPath;
        }
    }

    protected override void OnDestroy()
    {
        // Always release sprites when window is destroyed
        if (!string.IsNullOrEmpty(currentIconPath))
        {
            DynamicAtlasManager.Instance.ReleaseSprite(currentIconPath);
            currentIconPath = null;
        }
        base.OnDestroy();
    }
}
```

#### Pattern 2: Factory Pattern (Recommended for Dependency Injection)

If you're using a DI framework or want more control over the atlas lifecycle:

```csharp
using CycloneGames.UIFramework.DynamicAtlas;
using UnityEngine;
using UnityEngine.UI;

public class MyUIWindow : UIWindow
{
    [SerializeField] private Image iconImage;
    private IDynamicAtlas atlas;
    private string currentIconPath;

    // Inject atlas through constructor or setter
    public void SetAtlas(IDynamicAtlas atlasService)
    {
        atlas = atlasService;
    }

    public void SetIcon(string iconPath)
    {
        if (atlas == null)
        {
            Debug.LogError("Atlas not initialized!");
            return;
        }

        // Release previous icon
        if (!string.IsNullOrEmpty(currentIconPath))
        {
            atlas.ReleaseSprite(currentIconPath);
        }

        // Get sprite from atlas
        Sprite sprite = atlas.GetSprite(iconPath);

        if (sprite != null && iconImage != null)
        {
            iconImage.sprite = sprite;
            currentIconPath = iconPath;
        }
    }

    protected override void OnDestroy()
    {
        if (atlas != null && !string.IsNullOrEmpty(currentIconPath))
        {
            atlas.ReleaseSprite(currentIconPath);
            currentIconPath = null;
        }
        base.OnDestroy();
    }
}

// In your initialization code:
public class GameInitializer : MonoBehaviour
{
    private IDynamicAtlasFactory atlasFactory;

    void Start()
    {
        // Create factory
        atlasFactory = new DynamicAtlasFactory();

        // Create atlas with custom configuration
        var config = new DynamicAtlasConfig(
            pageSize: 2048,
            autoScaleLargeTextures: true
        );
        IDynamicAtlas atlas = atlasFactory.Create(config);

        // Inject into your UI windows
        // (This depends on your DI framework)
    }
}
```

#### Pattern 3: Direct Service (Advanced)

For maximum control, create the service directly:

```csharp
using CycloneGames.UIFramework.DynamicAtlas;
using UnityEngine;
using UnityEngine.UI;

public class MyUIWindow : UIWindow
{
    [SerializeField] private Image iconImage;
    private IDynamicAtlas atlas;
    private string currentIconPath;

    protected override void Awake()
    {
        base.Awake();

        // Create atlas service directly
        atlas = new DynamicAtlasService(
            forceSize: 2048,
            loadFunc: path => Resources.Load<Texture2D>(path),
            unloadFunc: (path, tex) => Resources.UnloadAsset(tex),
            autoScaleLargeTextures: true
        );
    }

    public void SetIcon(string iconPath)
    {
        if (atlas == null) return;

        // Release previous icon
        if (!string.IsNullOrEmpty(currentIconPath))
        {
            atlas.ReleaseSprite(currentIconPath);
        }

        // Get sprite from atlas
        Sprite sprite = atlas.GetSprite(iconPath);

        if (sprite != null && iconImage != null)
        {
            iconImage.sprite = sprite;
            currentIconPath = iconPath;
        }
    }

    protected override void OnDestroy()
    {
        if (atlas != null)
        {
            // Release sprite
            if (!string.IsNullOrEmpty(currentIconPath))
            {
                atlas.ReleaseSprite(currentIconPath);
            }

            // Dispose atlas (only if you created it directly)
            atlas.Dispose();
        }
        base.OnDestroy();
    }
}
```

### Step 2: Complete Example - Icon List with Dynamic Atlas

Here's a complete example showing how to use Dynamic Atlas in a real scenario - an icon list that loads icons dynamically:

```csharp
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.DynamicAtlas;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class IconListWindow : UIWindow
{
    [SerializeField] private Transform iconContainer;
    [SerializeField] private GameObject iconPrefab; // Prefab with Image component

    private List<IconItem> iconItems = new List<IconItem>();

    private class IconItem
    {
        public GameObject gameObject;
        public Image image;
        public string iconPath;
    }

    protected override void Awake()
    {
        base.Awake();

        // Configure Dynamic Atlas (only once)
        if (DynamicAtlasManager.Instance != null)
        {
            DynamicAtlasManager.Instance.Configure(
                load: path => Resources.Load<Texture2D>(path),
                unload: (path, tex) => Resources.UnloadAsset(tex),
                size: 2048,
                autoScaleLargeTextures: true
            );
        }
    }

    public void LoadIcons(List<string> iconPaths)
    {
        // Clear existing icons
        ClearIcons();

        // Load each icon
        foreach (string iconPath in iconPaths)
        {
            CreateIconItem(iconPath);
        }
    }

    private void CreateIconItem(string iconPath)
    {
        if (iconPrefab == null || iconContainer == null)
            return;

        // Instantiate icon prefab
        GameObject iconObj = Instantiate(iconPrefab, iconContainer);
        Image iconImage = iconObj.GetComponent<Image>();

        if (iconImage == null)
        {
            Debug.LogError("Icon prefab must have an Image component!");
            Destroy(iconObj);
            return;
        }

        // Get sprite from Dynamic Atlas
        Sprite sprite = DynamicAtlasManager.Instance.GetSprite(iconPath);

        if (sprite != null)
        {
            iconImage.sprite = sprite;

            // Track this icon item
            iconItems.Add(new IconItem
            {
                gameObject = iconObj,
                image = iconImage,
                iconPath = iconPath
            });
        }
        else
        {
            Debug.LogWarning($"Failed to load icon: {iconPath}");
            Destroy(iconObj);
        }
    }

    private void ClearIcons()
    {
        // Release all sprites from atlas
        foreach (var item in iconItems)
        {
            if (!string.IsNullOrEmpty(item.iconPath))
            {
                DynamicAtlasManager.Instance.ReleaseSprite(item.iconPath);
            }
            if (item.gameObject != null)
            {
                Destroy(item.gameObject);
            }
        }
        iconItems.Clear();
    }

    protected override void OnDestroy()
    {
        // Clean up all icons
        ClearIcons();
        base.OnDestroy();
    }
}
```

### Step 3: Integrating with Asset Management Systems

If you're using Addressables, YooAsset, or other asset management systems, you can integrate them with Dynamic Atlas:

```csharp
using CycloneGames.UIFramework.DynamicAtlas;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private IAssetPackage assetPackage;

    async void Start()
    {
        // Initialize your asset management system
        assetPackage = await InitializeYourAssetPackageAsync();

        // Configure Dynamic Atlas with custom load/unload functions
        DynamicAtlasManager.Instance.Configure(
            load: async (path) =>
            {
                // Load texture using your asset management system
                var handle = await assetPackage.LoadAssetAsync<Texture2D>(path);
                return handle.Asset;
            },
            unload: (path, tex) =>
            {
                // Unload using your asset management system
                assetPackage.ReleaseAsset(path);
            },
            size: 2048,
            autoScaleLargeTextures: true
        );
    }
}
```

### Step 4: Best Practices and Tips

1. **Always Release Sprites**: When a sprite is no longer needed, call `ReleaseSprite()` to decrement the reference count. This allows the atlas to free space when the count reaches zero.

2. **Release in OnDestroy or OnDisable**: Always release sprites when your UI component is destroyed or disabled:

```csharp
protected override void OnDestroy()
{
    if (!string.IsNullOrEmpty(currentIconPath))
    {
        DynamicAtlasManager.Instance.ReleaseSprite(currentIconPath);
        currentIconPath = null;
    }
    base.OnDestroy();
}
```

3. **Use Appropriate Page Size**:

   - **1024x1024**: For low-end devices or when memory is constrained
   - **2048x2048**: Recommended for most cases (default)
   - **4096x4096**: For high-end devices with plenty of memory

4. **Enable Auto-Scaling**: Set `autoScaleLargeTextures: true` to automatically scale textures that are too large for the atlas. This prevents errors and ensures all textures can be packed.

5. **Monitor Atlas Usage**: In development, you can check how many pages are in use:

```csharp
// This requires accessing internal state, so it's mainly for debugging
// The system automatically creates new pages when needed
```

6. **Texture Requirements**:

   - Textures must be readable (enable "Read/Write Enabled" in texture import settings)
   - Textures should be in a format that supports runtime modification (RGBA32, ARGB32, etc.)
   - Compressed formats (DXT, ETC) may need to be converted

7. **Performance Considerations**:
   - Packing happens on the main thread, so avoid packing many large textures in a single frame
   - Consider pre-loading commonly used icons during loading screens
   - Use the atlas for small-to-medium textures (icons, buttons) rather than large background images

### Step 5: Troubleshooting

**Problem: Sprites appear black or missing**

- Check that textures are readable (Texture Import Settings > Read/Write Enabled)
- Verify the texture path is correct
- Ensure textures are loaded successfully before calling `GetSprite()`

**Problem: Textures are blurry**

- Large textures are being scaled down to fit in the atlas
- Consider using smaller source textures or increasing atlas page size
- Check that `autoScaleLargeTextures` is enabled

**Problem: Memory usage is high**

- Make sure you're calling `ReleaseSprite()` when sprites are no longer needed
- Reduce atlas page size if memory is constrained
- Limit the number of textures packed simultaneously

**Problem: Draw calls not reduced**

- Ensure sprites from the atlas are on the same Canvas
- Check that sprites use the same material/shader
- Verify that Unity's batching is enabled

## Advanced Features

### Custom Transition Drivers

You can override the default open/close animations using `IUIWindowTransitionDriver`. This allows you to use **DOTween**, **LitMotion**, or Unity's **Animator**.

```csharp
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

public class FadeTransitionDriver : IUIWindowTransitionDriver
{
    public async UniTask PlayOpenAsync(UIWindow window, CancellationToken ct)
    {
        CanvasGroup canvasGroup = window.GetComponent<CanvasGroup>();
        if (canvasGroup == null) return;

        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration && !ct.IsCancellationRequested)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
            await UniTask.Yield();
        }

        canvasGroup.alpha = 1f;
    }

    public async UniTask PlayCloseAsync(UIWindow window, CancellationToken ct)
    {
        CanvasGroup canvasGroup = window.GetComponent<CanvasGroup>();
        if (canvasGroup == null) return;

        float duration = 0.3f;
        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < duration && !ct.IsCancellationRequested)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / duration);
            await UniTask.Yield();
        }

        canvasGroup.alpha = 0f;
    }
}

// Assign to a window:
public class MyWindow : UIWindow
{
    protected override void Awake()
    {
        base.Awake();
        SetTransitionDriver(new FadeTransitionDriver());
    }
}
```

### Performance Optimization Tools

#### `OptimizeHierarchy`

Right-click your `UIWindow` component in the Inspector and select **Optimize Hierarchy**. This tool scans your UI hierarchy and disables `RaycastTarget` on non-interactive elements (like decorative Images or Texts), significantly reducing the cost of Unity's event system raycasts.

#### `SetVisible` API

Use `window.SetVisible(bool)` instead of `gameObject.SetActive(bool)`.

- **SetVisible**: Toggles `CanvasGroup.alpha`, `interactable`, and `blocksRaycasts`. This avoids the expensive rebuilding of the UI layout and mesh that happens when enabling/disabling GameObjects.

```csharp
// Instead of:
gameObject.SetActive(false);

// Use:
SetVisible(false);
```

## Architecture Patterns (MVC/MVP)

While `CycloneGames.UIFramework` is architecture-agnostic, it is designed to support structured patterns like **MVC (Model-View-Controller)** or **MVP (Model-View-Presenter)**.

### The View (`UIWindow`)

Your `UIWindow` subclass acts as the **View**. It should:

- Hold references to UI components (Buttons, Texts).
- Expose methods to update the visualization (e.g., `SetHealth(float value)`).
- Expose events for user interactions (e.g., `OnPlayClicked`).
- **Avoid** containing complex business logic.

### The Controller / Presenter

You can implement a separate Controller class or use the `UIWindow` as a lightweight controller.

- **Controller**: Subscribes to `UIWindow` events, interacts with the game model/services, and updates the View.
- **Model**: Pure C# classes holding your game data.

**Example (MVP):**

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

// The View
public class MainMenuWindow : UIWindow
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Text versionText;

    public event Action OnPlayClicked;
    public event Action OnSettingsClicked;

    protected override void Awake()
    {
        base.Awake();
        playButton.onClick.AddListener(() => OnPlayClicked?.Invoke());
        settingsButton.onClick.AddListener(() => OnSettingsClicked?.Invoke());
    }

    public void SetVersion(string version)
    {
        if (versionText != null)
            versionText.text = $"Version {version}";
    }
}

// The Presenter
public class MainMenuController
{
    private MainMenuWindow _view;
    private GameService _gameService;

    public MainMenuController(MainMenuWindow view, GameService gameService)
    {
        _view = view;
        _gameService = gameService;
        _view.OnPlayClicked += HandlePlay;
        _view.OnSettingsClicked += HandleSettings;

        // Update view with model data
        _view.SetVersion(_gameService.GetVersion());
    }

    private void HandlePlay()
    {
        _gameService.StartGame();
        _view.Close();
    }

    private void HandleSettings()
    {
        // Open settings window
        _gameService.OpenSettings();
    }

    public void Dispose()
    {
        if (_view != null)
        {
            _view.OnPlayClicked -= HandlePlay;
            _view.OnSettingsClicked -= HandleSettings;
        }
    }
}
```
