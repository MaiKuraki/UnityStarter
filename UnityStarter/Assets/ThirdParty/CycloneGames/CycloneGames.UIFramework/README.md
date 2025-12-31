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

---

## Transition Animation System

The UIFramework provides a flexible, extensible transition animation system supporting **LitMotion** and **DOTween**. You can use built-in presets or create custom animations.

### Built-in Configurations

| Config                              | Effect               | Usage                |
| ----------------------------------- | -------------------- | -------------------- |
| `FadeConfig.Default`                | Fade in/out          | Dialogs, popups      |
| `ScaleConfig.Default`               | Scale from 80%       | Modal windows        |
| `SlideConfig.Left/Right/Top/Bottom` | Slide from direction | Side panels, drawers |
| `CompositeConfig.FadeScale`         | Fade + Scale         | Premium popups       |
| `CompositeConfig.FadeSlideBottom`   | Fade + Slide up      | Mobile-style sheets  |

### Quick Usage

```csharp
// Using LitMotion (requires LIT_MOTION_PRESENT define)
var driver = new LitMotionTransitionDriver(FadeConfig.Default);
window.SetTransitionDriver(driver);

// Using DOTween (requires DO_TWEEN_PRESENT define)
var driver = new DOTweenTransitionDriver(CompositeConfig.FadeScale);
window.SetTransitionDriver(driver);
```

### Custom Configuration

```csharp
// Custom scale animation
var config = new ScaleConfig(scaleFrom: 0.5f, duration: 0.4f);
window.SetTransitionDriver(new LitMotionTransitionDriver(config));

// Custom slide from bottom
var slideConfig = new SlideConfig(
    direction: SlideDirection.Bottom,
    offset: 0.3f,
    duration: 0.35f
);
window.SetTransitionDriver(new DOTweenTransitionDriver(slideConfig));

// Composite: Fade + Scale + Slide
var compositeConfig = new CompositeConfig(
    fade: true,
    scale: new ScaleConfig(0.9f),
    slide: new SlideConfig(SlideDirection.Bottom, 0.2f),
    duration: 0.3f
);
window.SetTransitionDriver(new LitMotionTransitionDriver(compositeConfig));
```

### Different Open/Close Animations

```csharp
var openConfig = CompositeConfig.FadeScale;
var closeConfig = FadeConfig.Default;

window.SetTransitionDriver(new LitMotionTransitionDriver(
    openConfig: openConfig,
    closeConfig: closeConfig,
    easeIn: LitMotion.Ease.OutBack,
    easeOut: LitMotion.Ease.InQuad
));
```

### Setup Requirements

#### LitMotion

1.  **Install LitMotion**:
    - Open **Window > Package Manager**
    - Click **+ > Add package from git URL...**
    - Enter `https://github.com/annulusgames/LitMotion.git`
2.  **Done!**
    - The `CycloneGames.UIFramework.Runtime.asmdef` handles definitions automatically (`LIT_MOTION_PRESENT`).
    - You can now use `LitMotionTransitionDriver`.

#### DOTween

1.  **Install DOTween**: Import from Asset Store or Package Manager.
2.  **Setup**: Run **Tools > Demigiant > DOTween Utility Panel** and click **Create ASMDEF**.
3.  **Done!**
    - The `CycloneGames.UIFramework.Runtime.asmdef` handles definitions automatically (`DO_TWEEN_PRESENT`).
    - You can now use `DOTweenTransitionDriver`.

### Extending the Animation System

External projects can create custom transitions by inheriting from the base drivers:

```csharp
// 1. Create a custom config class
public class RotateConfig : TransitionConfigBase
{
    public float Angle { get; }
    public RotateConfig(float angle = 180f, float duration = 0.3f) : base(duration)
    {
        Angle = angle;
    }
}

// 2. Extend the driver to handle your config
public class MyTransitionDriver : LitMotionTransitionDriver
{
    public MyTransitionDriver(TransitionConfigBase config) : base(config) { }

    protected override async UniTask AnimateConfigAsync(
        TransitionContext ctx, TransitionConfigBase config, bool isOpen, Ease ease, CancellationToken ct)
    {
        if (config is RotateConfig rotate)
        {
            // Custom rotation animation
            float from = isOpen ? rotate.Angle : 0f;
            float to = isOpen ? 0f : rotate.Angle;
            var handle = LMotion.Create(from, to, rotate.Duration)
                .WithEase(ease)
                .Bind(v => ctx.Transform.rotation = Quaternion.Euler(0, 0, v));
            await handle.ToUniTask(cancellationToken: ct);
        }
        else
        {
            await base.AnimateConfigAsync(ctx, config, isOpen, ease, ct);
        }
    }
}
```

### Performance Notes

- **Zero GC after warmup**: Both drivers use struct-based context and cached animations
- **Proper cleanup**: Tweens are killed on cancellation to prevent memory leaks
- **Unscaled time**: Animations use unscaled time, working correctly during Time.timeScale = 0

---

## Performance Optimization Tools

### `OptimizeHierarchy`

Right-click your `UIWindow` component in the Inspector and select **Optimize Hierarchy**. This tool scans your UI hierarchy and disables `RaycastTarget` on non-interactive elements (like decorative Images or Texts), significantly reducing the cost of Unity's event system raycasts.

### `SetVisible` API

Use `window.SetVisible(bool)` instead of `gameObject.SetActive(bool)`.

- **SetVisible**: Toggles `CanvasGroup.alpha`, `interactable`, and `blocksRaycasts`. This avoids the expensive rebuilding of the UI layout and mesh that happens when enabling/disabling GameObjects.

```csharp
// Instead of:
gameObject.SetActive(false);

// Use:
SetVisible(false);
```

---

## Architecture Patterns (MVP with Auto-Binding)

CycloneGames.UIFramework provides **optional** MVP (Model-View-Presenter) support with automatic Presenter lifecycle management. You can use the traditional approach (all logic in UIWindow) or the new MVP pattern with automatic binding.

### Usage Levels

| Level  | Pattern                                          | Use Case                  |
| ------ | ------------------------------------------------ | ------------------------- |
| **L0** | `class MyUI : UIWindow`                          | Simple windows, beginners |
| **L1** | `class MyUI : UIWindow` + manual Presenter       | Manual control            |
| **L2** | `class MyUI : UIWindow<TPresenter>`              | Auto-binding, no DI       |
| **L3** | `class MyUI : UIWindow<TPresenter>` + VContainer | Full DI integration       |

---

### Level 0: Traditional (No Presenter)

Write all logic directly in the UIWindow - simple and straightforward.

```csharp
public class UIWindowSimple : UIWindow
{
    [SerializeField] private Button closeBtn;

    protected override void Awake()
    {
        base.Awake();
        closeBtn.onClick.AddListener(() => Close());
    }
}
```

---

### Level 2: Auto-Binding (No DI Framework Required)

Use `UIWindow<TPresenter>` to automatically create and manage Presenters.

#### Step 1: Define View Interface

```csharp
public interface IInventoryView
{
    void SetGold(int amount);
    void SetItemCount(int count);
}
```

#### Step 2: Create the View (UIWindow)

```csharp
using CycloneGames.UIFramework.Runtime;
using UnityEngine;
using UnityEngine.UI;

public class UIWindowInventory : UIWindow<InventoryPresenter>, IInventoryView
{
    [SerializeField] private Text goldText;
    [SerializeField] private Text itemCountText;

    public void SetGold(int amount) => goldText.text = amount.ToString("N0");
    public void SetItemCount(int count) => itemCountText.text = count.ToString();
}
```

#### Step 3: Create the Presenter

```csharp
using CycloneGames.UIFramework.Runtime;

public class InventoryPresenter : UIPresenter<IInventoryView>
{
    // Auto-injected from UIServiceLocator (no DI framework needed)
    [UIInject] private IInventoryService InventoryService { get; set; }

    public override void OnViewOpened()
    {
        View.SetGold(InventoryService.Gold);
        View.SetItemCount(InventoryService.ItemCount);
    }

    public override void OnViewClosing()
    {
        // Save or cleanup logic
    }

    public override void Dispose()
    {
        // Cleanup if needed
    }
}
```

> [!NOTE]
>
> `[UIInject]` is **optional**. If your Presenter works without external dependencies, or if you use a full DI framework (Level 3) that handles injection differently, you do not need to use this attribute.

#### Step 4: Register Services (No DI Framework)

```csharp
using CycloneGames.UIFramework.Runtime;

public class GameBootstrap : MonoBehaviour
{
    void Awake()
    {
        // Register services for [UIInject] to work
        UIServiceLocator.Register<IInventoryService>(new InventoryService());
        UIServiceLocator.Register<IAudioService>(new AudioService());
    }

    void OnDestroy()
    {
        UIServiceLocator.Clear();
    }
}
```

#### Lifecycle

The Presenter lifecycle is fully automatic and maps 1:1 to UIWindow:

| UIWindow Event      | Presenter Call    | Description            |
| ------------------- | ----------------- | ---------------------- |
| `Awake()`           | `SetView()`       | View binding           |
| `OnStartOpen()`     | `OnViewOpening()` | Before open animation  |
| `OnFinishedOpen()`  | `OnViewOpened()`  | Fully interactive      |
| `OnStartClose()`    | `OnViewClosing()` | Before close animation |
| `OnFinishedClose()` | `OnViewClosed()`  | After close animation  |
| `OnDestroy()`       | `Dispose()`       | Cleanup                |

---

### Level 3: VContainer Integration

For VContainer users, add `VCONTAINER_PRESENT` to your scripting define symbols.

#### Step 1: Add Scripting Define

In **Project Settings > Player > Scripting Define Symbols**, add:

```
VCONTAINER_PRESENT
```

#### Step 2: Understand Architecture

UIFramework is designed to be **DI-agnostic**, VContainer integration is implemented via adapter pattern:

```
VContainer
├── IUIService (UIService) ← Main entry point, initialized via RegisterBuildCallback
│   ├── Dependency: IAssetPathBuilderFactory
│   ├── Dependency: IUnityObjectSpawner
│   ├── Dependency: IMainCameraService (optional)
│   └── Dependency: IAssetPackage (optional)
│
├── VContainerWindowBinder ← Adapter connecting VContainer with Presenter factory
│
├── UISystemInitializer ← Initializes the binder
│
└── Presenter types (optional registration)
    ├── Registered → Uses VContainer constructor injection
    └── Not registered → Auto-fallback to Activator + [UIInject]
```

#### Step 3: Complete Configuration Example

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations;
using CycloneGames.Factory.Runtime;
using CycloneGames.Service.Runtime;
using CycloneGames.AssetManagement.Runtime;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // ========================================
        // 1. UIService Dependencies
        // ========================================
        builder.Register<IAssetPathBuilderFactory, TemplateAssetPathBuilderFactory>(Lifetime.Singleton);
        builder.Register<IUnityObjectSpawner, DefaultUnityObjectSpawner>(Lifetime.Singleton);
        builder.Register<IMainCameraService, MainCameraService>(Lifetime.Singleton);

        // Hot-update projects: register IAssetPackage
        // builder.RegisterInstance(yourAssetPackage).As<IAssetPackage>();

        // ========================================
        // 2. UIService - Use RegisterBuildCallback to Initialize
        // ========================================
        // UIService maintains DI-agnostic design, initialized via callback
        builder.Register<IUIService, UIService>(Lifetime.Singleton);
        builder.RegisterBuildCallback(resolver =>
        {
            var uiService = resolver.Resolve<IUIService>();
            var factory = resolver.Resolve<IAssetPathBuilderFactory>();
            var spawner = resolver.Resolve<IUnityObjectSpawner>();
            var cameraService = resolver.Resolve<IMainCameraService>();

            // If IAssetPackage is registered, use the overload with package
            // var package = resolver.Resolve<IAssetPackage>();
            // uiService.Initialize(factory, spawner, cameraService, package);

            // Otherwise use default overload
            uiService.Initialize(factory, spawner, cameraService);
        });

        // ========================================
        // 3. UIFramework Presenter Support
        // ========================================
        builder.Register<VContainerWindowBinder>(Lifetime.Singleton);
        builder.RegisterEntryPoint<UISystemInitializer>();

        // ========================================
        // 4. Business Services (used by Presenters)
        // ========================================
        builder.Register<IInventoryService, InventoryService>(Lifetime.Singleton);
        builder.Register<IAudioService, AudioService>(Lifetime.Singleton);

        // ========================================
        // 5. Presenter Registration - OPTIONAL!
        // ========================================
        // If not registered, UIPresenterFactory auto-falls back to Activator
        // Presenters in hot-update assemblies use [UIInject] for injection

        // For constructor injection, register explicitly:
        // builder.Register<InventoryPresenter>(Lifetime.Transient);
    }
}
```

> [!NOTE]
>
> **About `[UIInject]` and VContainer Integration**
>
> `VContainerWindowBinder` automatically registers VContainer's resolver with `UIServiceLocator` on creation.
> This means `[UIInject]` can **automatically inject services registered in VContainer**:
>
> ```csharp
> // Register in VContainer
> builder.Register<IAudioService, AudioService>(Lifetime.Singleton);
>
> // Use [UIInject] in Presenter (no need to register Presenter in VContainer)
> public class HotUpdatePresenter : UIPresenter<IView>
> {
>     [UIInject] private IAudioService AudioService { get; set; } // ✅ Auto-resolved from VContainer
> }
> ```
>
> Scene-scoped services are also supported: each `VContainerWindowBinder` maintains its own resolver in the stack, auto-cleaned on dispose.

#### Step 4: Create UI System Initializer

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.UIFramework.Runtime.Integrations;

public class UISystemInitializer : IStartable
{
    private readonly VContainerWindowBinder _binder;

    [Inject]
    public UISystemInitializer(IObjectResolver resolver)
    {
        _binder = new VContainerWindowBinder(resolver);
    }

    public void Start()
    {
        CycloneGames.Logger.CLogger.Log("[UISystemInitializer] VContainer integration initialized");
    }
}
```

#### Step 5: Writing Presenters

**Approach A: Using `[UIInject]` (No registration needed, hot-update friendly)**

```csharp
using CycloneGames.UIFramework.Runtime;

// No VContainer registration needed, auto-falls back to Activator
public class InventoryPresenter : UIPresenter<IInventoryView>
{
    [UIInject] private IInventoryService InventoryService { get; set; }
    [UIInject] private IAudioService AudioService { get; set; }

    public override void OnViewOpened()
    {
        View.SetGold(InventoryService.Gold);
        AudioService.PlaySFX("ui_open");
    }
}
```

**Approach B: Using Constructor Injection (Requires VContainer registration)**

```csharp
using VContainer;
using CycloneGames.UIFramework.Runtime;

// Requires registration: builder.Register<InventoryPresenter>(Lifetime.Transient);
public class InventoryPresenter : UIPresenter<IInventoryView>
{
    private readonly IInventoryService _inventoryService;

    [Inject]
    public InventoryPresenter(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    public override void OnViewOpened()
    {
        View.SetGold(_inventoryService.Gold);
    }
}
```

#### Step 6: Scene-Scoped Services (Optional)

If your scene has exclusive services that need to be used in UI, simply register `UIServiceLocatorBridge`:

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.UIFramework.Runtime.Integrations;

public class BattleSceneLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Scene-exclusive services
        builder.Register<IBattleService, BattleService>(Lifetime.Scoped);
        builder.Register<IEnemySpawner, EnemySpawner>(Lifetime.Scoped);

        // One line: pushes scene resolver immediately on construction, auto-pops on dispose
        builder.Register<UIServiceLocatorBridge>(Lifetime.Scoped);
    }
}
```

> [!IMPORTANT]
>
> **When is `UIServiceLocatorBridge` needed?**
>
> | Scenario                                       | Required?                                      |
> | ---------------------------------------------- | ---------------------------------------------- |
> | Only using Root global services                | ❌ No (`VContainerWindowBinder` handles it)    |
> | Scene-exclusive services via `[UIInject]`      | ✅ Yes, register in that scene's LifetimeScope |
> | Using constructor injection (not `[UIInject]`) | ❌ No (VContainer handles parent-child scopes) |
>
> **If you forget to register**: `[UIInject]` will return `null` for scene services, but won't throw.

Now scene UI can access scene services via `[UIInject]`:

```csharp
public class BattleHUDPresenter : UIPresenter<IBattleHUDView>
{
    [UIInject] private IBattleService BattleService { get; set; }  // Scene service ✅
    [UIInject] private IAudioService AudioService { get; set; }    // Global service ✅

    public override void OnViewOpened()
    {
        View.SetEnemyCount(BattleService.EnemyCount);
    }
}
```

> [!TIP]
>
> **How the Resolver Stack Works**
>
> ```
> Global Root Scope starts → VContainerWindowBinder Push(rootResolver)
> Enter Battle Scene → UIServiceLocatorBridge Push(battleResolver)
>
> [UIInject] resolves IBattleService:
>   1. Check battleResolver → Found!
>
> [UIInject] resolves IAudioService:
>   1. Check battleResolver → Not found
>   2. Check rootResolver → Found!
>
> Leave Battle Scene → UIServiceLocatorBridge.Dispose() Pop(battleResolver)
> ```

#### Using UIService to Open UI

```csharp
public class GameController
{
    private readonly IUIService _uiService;

    [Inject]
    public GameController(IUIService uiService)
    {
        _uiService = uiService;
    }

    public async void OpenInventory()
    {
        var window = await _uiService.OpenUIAsync("UIWindow_Inventory");

        if (window is UIWindow<InventoryPresenter> inventoryWindow)
        {
            inventoryWindow.Presenter.RefreshData();
        }
    }

    public void CloseInventory()
    {
        _uiService.CloseUI("UIWindow_Inventory");
    }
}
```

> [!IMPORTANT]
>
> **How It Works**
>
> ```
> VContainer builds container
>     │
>     ▼
> RegisterBuildCallback executes
>     │  - Resolves UIService and dependencies
>     │  - Calls uiService.Initialize(...)
>     ▼
> UISystemInitializer.Start() called
>     │  - Creates VContainerWindowBinder
>     │  - Sets UIPresenterFactory.CustomFactory
>     ▼
> Runtime: uiService.OpenUIAsync("UIWindow_Inventory")
>     │  - UIManager loads prefab
>     │  - Instantiates UIWindow<InventoryPresenter>
>     ▼
> UIWindow.Awake()
>     │  - UIPresenterFactory.Create<InventoryPresenter>()
>     ├─ VContainer registered → Constructor injection
>     └─ VContainer not registered → Activator + [UIInject] injection
> ```

---

### Design Philosophy: View-First MVP

You might ask: _"Why does the View (UIWindow) create the Presenter, instead of the Presenter creating the View?"_

We chose the **View-First** approach specifically for the Unity engine environment:

1.  **Unity-Native Workflow**: In Unity, UI starts with Prefabs. The "Entry Point" is naturally the `UIWindow` component on a GameObject.
2.  **Lifecycle Safety**: The Presenter's lifecycle is perfectly bound to the View (`Awake` to `OnDestroy`). You never have "Zombie Presenters" running without a View, which avoids many common null reference errors.
3.  **Zero Glue Code**: `UIWindow<T>` handles the binding automatically. You don't need separate "ScreenManager" or "Router" scripts just to wire things up.
4.  **DI Compatible**: Even though the View initiates creation, the `UIPresenterFactory` serves as an indirection layer. This allows full DI frameworks (like VContainer) to intervene and inject dependencies, giving you the best of both worlds: **View-driven lifecycle + DI-driven logic**.

---

### API Reference

#### `UIPresenter<TView>`

| Method            | Description                                  |
| ----------------- | -------------------------------------------- |
| `View`            | The bound view instance (protected property) |
| `OnViewBound()`   | Called after SetView, before window opens    |
| `OnViewOpening()` | Called when window starts opening            |
| `OnViewOpened()`  | Called when window is fully open             |
| `OnViewClosing()` | Called when window starts closing            |
| `OnViewClosed()`  | Called after close animation                 |
| `Dispose()`       | Called when window is destroyed              |

#### `UIServiceLocator`

| Method                        | Description                  |
| ----------------------------- | ---------------------------- |
| `Register<T>(T instance)`     | Register a singleton service |
| `RegisterFactory<T>(Func<T>)` | Register a lazy factory      |
| `Get<T>()`                    | Get a registered service     |
| `Unregister<T>()`             | Remove a service             |
| `Clear()`                     | Clear all services           |

#### `UIPresenterFactory`

| Property/Method | Description                         |
| --------------- | ----------------------------------- |
| `CustomFactory` | Set to integrate with DI frameworks |
| `Create<T>()`   | Create a Presenter instance         |
| `ClearCache()`  | Clear reflection cache              |

---

### Performance Notes

- **Zero GC after warmup**: Reflection results are cached
- **Thread-safe**: UIServiceLocator uses locking for concurrent access
- **Memory-safe**: Presenters are disposed with their windows
- **No forced DI**: Works without any DI framework
