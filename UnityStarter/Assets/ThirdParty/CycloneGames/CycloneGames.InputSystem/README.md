# CycloneGames.InputSystem

> Note: The CycloneGames.InputSystem code was authored by the project's developer; this documentation was prepared with AI assistance.

A reactive wrapper around Unity Input System with context stacks, multi-player joining, device locking, YAML-based configuration, and an Editor tool.

English | [简体中文](./README.SCH.md)

## Features

- **Context Stack**: Push/pop contexts to manage input states (e.g., Gameplay, UI, Cutscene).
- **Rich Multi-Player Modes**:
  - **Single-Player**: Auto-joins and locks all required devices to one player.
  - **Lobby (Device Locking)**: The first device joins as Player 0. Subsequent devices are automatically paired to this single player, ideal for allowing one player to switch between keyboard and gamepad seamlessly.
  - **Lobby (Shared Devices)**: Each new device joins as a new player (Player 0, 1, 2...), perfect for local co-op.
- **Configurable Code Generation**:
  - Automatically generate a static `InputActions` class from your YAML config.
  - Customize the output directory and namespace to fit your project structure, keeping your `Packages` folder clean.
- **Reactive API (R3)**: Provides `Observable` streams for button presses, long presses, analog values, and more.
- **Intelligent Hot-Swapping**: Automatically pairs newly connected devices to the correct player _after_ the lobby phase.
- **Active Device Detection**: `ActiveDeviceKind` property tracks whether the last input came from Keyboard/Mouse or a Gamepad.

## Install

- Unity 2022.3+
- Enable Input System package
- Dependencies: UniTask, R3, VYaml, CycloneGames.Utility, CycloneGames.Logger

## Quick Start

### Step 1: Generate Default Config

Open the editor window: `Tools → CycloneGames → Input System Editor`, then click **Generate Default Config** to create the default YAML configuration file.

### Step 2: Configure Code Generation (Recommended)

In the editor window:

1. Set the **Output Directory** (e.g., `Assets/Scripts/Generated`) and **Namespace** (e.g., `YourGame.Input.Generated`).
2. Click **Save and Generate Constants** to save the configuration and generate the `InputActions.cs` file.

### Step 3: Initialize at Boot

Load the configuration at game startup (e.g., in `MonoBehaviour.Start()` or an initialization script):

```csharp
using CycloneGames.Utility.Runtime;
using CycloneGames.InputSystem.Runtime;
using Cysharp.Threading.Tasks;

// Call at startup
var defaultUri = FilePathUtility.GetUnityWebRequestUri("input_config.yaml", UnityPathSource.StreamingAssets);
var userUri = FilePathUtility.GetUnityWebRequestUri("user_input_settings.yaml", UnityPathSource.PersistentData);
await InputSystemLoader.InitializeAsync(defaultUri, userUri);
```

`InputSystemLoader` will prioritize loading the user configuration (`PersistentData`), falling back to the default configuration (`StreamingAssets`) if it doesn't exist. On first run, it will automatically copy the default configuration to the user configuration directory.

### Step 4: Join Player and Set Context

**Using generated constants (recommended):**

```csharp
// Make sure to import your custom namespace
using YourGame.Input.Generated;
using CycloneGames.InputSystem.Runtime;

var svc = InputManager.Instance.JoinSinglePlayer(0);
var ctx = new InputContext("Gameplay", "PlayerActions")
  .AddBinding(svc.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), new ActionCommand(() => {/*...*/}));
svc.RegisterContext(ctx);
svc.PushContext("Gameplay");
```

**Using string-based API (compatibility mode):**

```csharp
var svc = InputManager.Instance.JoinSinglePlayer(0);
var ctx = new InputContext("Gameplay", "PlayerActions")
  .AddBinding(svc.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(() => {/*...*/}));
svc.RegisterContext(ctx);
svc.PushContext("Gameplay");
```

## YAML Schema

```yaml
joinAction:
  type: Button
  action: JoinGame
  deviceBindings:
    - "<Keyboard>/enter"
    - "<Gamepad>/start"
playerSlots:
  - playerId: 0
    contexts:
      - name: Gameplay
        actionMap: PlayerActions
        bindings:
          - type: Vector2
            action: Move
            deviceBindings:
              - "<Gamepad>/leftStick"
              - "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
              - "<Mouse>/delta"
          - type: Button
            action: Confirm
            longPressMs: 500 # optional, emits long-press after 500ms
            deviceBindings:
              - "<Gamepad>/buttonSouth"
              - "<Keyboard>/space"
          - type: Float
            action: FireTrigger
            longPressMs: 600 # optional long-press for float
            longPressValueThreshold: 0.6 # threshold (0-1) considered as pressed
            deviceBindings:
              - "<Gamepad>/leftTrigger"
```

## Minimal Example (Beginner Friendly)

Create a simple player controller:

```csharp
using UnityEngine;
using CycloneGames.InputSystem.Runtime;

public class SimplePlayer : MonoBehaviour
{
  private IInputPlayer _input;

  private void Start()
  {
    // Join player 0 and create a gameplay context.
    _input = InputManager.Instance.JoinSinglePlayer(0);
    var ctx = new InputContext("Gameplay", "PlayerActions")
      .AddBinding(_input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMove))
      .AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirm))
      // Optional long-press (requires YAML: longPressMs on "Confirm")
      .AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirmLongPress));

    _input.RegisterContext(ctx);
    _input.PushContext("Gameplay");
  }

  private void OnMove(Vector2 dir)
  {
    // Move your character with dir.x, dir.y
    transform.position += new Vector3(dir.x, 0f, dir.y) * Time.deltaTime * 5f;
  }

  private void OnConfirm()
  {
    Debug.Log("Confirm pressed");
  }

  private void OnConfirmLongPress()
  {
    Debug.Log("Confirm long-pressed");
  }

  private void OnDestroy()
  {
    // Cleanup: InputPlayer will be automatically cleaned up when InputManager.Dispose() is called
    // If you need to clean up earlier when the component is destroyed, you can call:
    // (_input as IDisposable)?.Dispose();
  }
}
```

## Context-specific Short vs Long Press

If the same physical button should trigger a short press in one context and a long press in another (mutually exclusive), define two contexts and configure the action differently.

YAML example:

```yaml
playerSlots:
  - playerId: 0
    contexts:
      - name: Inspect
        actionMap: PlayerActions
        bindings:
          - type: Button
            action: Confirm
            # short press only (omit longPressMs)
            deviceBindings:
              - "<Keyboard>/space"
              - "<Gamepad>/buttonSouth"
      - name: Charge
        actionMap: PlayerActions
        bindings:
          - type: Button
            action: Confirm
            longPressMs: 600 # long press only for this context
            deviceBindings:
              - "<Keyboard>/space"
              - "<Gamepad>/buttonSouth"
```

Runtime usage:

```csharp
// In Inspect context: bind short press only
ctxInspect.AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnInspectConfirm));

// In Charge context: bind long press only
ctxCharge.AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnChargeConfirm));

// Switch contexts as needed
_input.RegisterContext(ctxInspect);
_input.RegisterContext(ctxCharge);
_input.PushContext("Inspect"); // later: _input.PushContext("Charge")
```

## Advanced Usage

### Multi-Player Modes

#### Single-Player Mode (Auto-Lock Devices)

```csharp
// Automatically join player 0 and lock all required devices to that player
var svc = InputManager.Instance.JoinSinglePlayer(0);

// If player is already joined, JoinSinglePlayer returns the existing service
// This allows you to call it multiple times safely
var svc2 = InputManager.Instance.JoinSinglePlayer(0); // Returns same service, no event triggered
```

**Note**: If the player is already joined, `JoinSinglePlayer` returns the existing service without triggering `OnPlayerInputReady` event. This is useful when you need to get the service multiple times, but if you need to rebind input contexts after the player has already joined, use `RefreshPlayerInput` instead.

#### Lobby Mode (Device Locking)

The first device joins as Player 0, and subsequent devices are automatically paired to that player. Ideal for single players switching between keyboard and gamepad:

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
InputManager.Instance.StartListeningForPlayers(true); // true = device locking mode
```

#### Lobby Mode (Shared Devices)

Each new device creates a new player, perfect for local co-op:

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
InputManager.Instance.StartListeningForPlayers(false); // false = shared devices mode
```

#### Batch Join Players

```csharp
// Synchronous batch join
var players = InputManager.Instance.JoinPlayersBatch(new List<int> { 0, 1, 2 });

// Asynchronous batch join (with timeout)
var players = await InputManager.Instance.JoinPlayersBatchAsync(
    new List<int> { 0, 1, 2 },
    timeoutPerPlayerInSeconds: 5
);
```

#### Async Join Player (Wait for Device Connection)

```csharp
// Wait for device connection, timeout 5 seconds
var svc = await InputManager.Instance.JoinSinglePlayerAsync(0, timeoutInSeconds: 5);
if (svc != null)
{
    // Player successfully joined
}
```

#### Manual Device Locking

```csharp
// Lock a specific device to a specific player
var svc = InputManager.Instance.JoinPlayerAndLockDevice(0, Keyboard.current);
```

#### Shared Device Mode

```csharp
// Multiple players share keyboard (suitable for turn-based games)
var player0 = InputManager.Instance.JoinPlayerOnSharedDevice(0);
var player1 = InputManager.Instance.JoinPlayerOnSharedDevice(1);
```

### Configuration Hot Reload

Reload configuration at runtime (e.g., when player modifies key bindings):

```csharp
bool success = await InputManager.Instance.ReloadConfigurationAsync();
if (success)
{
    Debug.Log("Configuration reloaded");
    // Newly joined players will use the new configuration
}
```

### Save User Configuration

Save the current configuration to the user configuration directory:

```csharp
await InputManager.Instance.SaveUserConfigurationAsync();
```

### Event Callbacks

```csharp
// Listen for player input ready events
InputManager.Instance.OnPlayerInputReady += (IInputPlayer playerInput) =>
{
    Debug.Log($"Player {((InputPlayer)playerInput)?.PlayerId ?? -1} input ready");
    // Set up player input context, etc.
};

// Listen for configuration reload events
InputManager.Instance.OnConfigurationReloaded += () =>
{
    Debug.Log("Configuration reloaded");
};

// Refresh player input by triggering OnPlayerInputReady event for an already joined player
// Useful when you dynamically bind input contexts after the player has already joined (e.g., in a different scene)
// Example: LaunchScene initializes input system, GameplayScene binds input contexts
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
// ... later, in GameplayScene after binding contexts ...
if (InputManager.Instance.GetInputPlayer(0) != null)
{
    InputManager.Instance.RefreshPlayerInput(0); // Triggers OnPlayerInputReady event to activate newly bound contexts
}

// Listen for context change events
inputService.OnContextChanged += (string contextName) =>
{
    Debug.Log($"Context switched to: {contextName}");
};
```

### Input Blocking

Temporarily disable all input (e.g., when showing pause menu):

```csharp
// Block input
inputService.BlockInput();

// Restore input
inputService.UnblockInput();
```

### Context Stack Management

```csharp
// Push new context (e.g., open menu)
inputService.PushContext("Menu");

// Pop context (return to previous context)
inputService.PopContext();

// View current active context
string currentContext = inputService.ActiveContextName.CurrentValue;

// Subscribe to context changes
inputService.ActiveContextName.Subscribe(ctxName =>
{
    Debug.Log($"Current context: {ctxName}");
});
```

### Active Device Detection

Track the device type the player last used in real-time:

```csharp
// Subscribe to device type changes
_input.ActiveDeviceKind.Subscribe(kind =>
{
    switch (kind)
    {
        case InputDeviceKind.KeyboardMouse:
            UpdateHUDIcons(KeyboardMouseIcons);
            break;
        case InputDeviceKind.Gamepad:
            UpdateHUDIcons(GamepadIcons);
            break;
    }
});
```

## Tutorial: Hold-to-Fill Progress (Press-and-Hold)

Goal: while the button is held, increase a progress bar; stop (or reset) on release.

### Step 1: Subscribe to Press State

```csharp
var isPressing = _input.GetPressStateObservable("PlayerActions", "Confirm");
```

### Step 2: Increment While Pressed

```csharp
float progress = 0f;
float speed = 0.4f; // 40% per second

isPressing.Subscribe(pressed =>
{
  if (pressed)
  {
    UniTask.Void(async () =>
    {
      while (pressed && progress < 1f)
      {
        await UniTask.Yield();
        progress = Mathf.Min(1f, progress + Time.deltaTime * speed);
        // Update UI
        UpdateProgressBar(progress);
      }
    });
  }
  else
  {
    // On release: stop. Optionally reset:
    // progress = 0f;
    // UpdateProgressBar(progress);
  }
});
```

### Step 3: Optional - Require Long Press Before Starting

Set long press time in YAML:

```yaml
- type: Button
  action: Confirm
  longPressMs: 500
  deviceBindings:
    - "<Keyboard>/space"
    - "<Gamepad>/buttonSouth"
```

Then use long press to start and release to stop:

```csharp
_input.GetLongPressObservable("PlayerActions", "Confirm").Subscribe(_ => StartFilling());
_input.GetPressStateObservable("PlayerActions", "Confirm").Where(p => !p).Subscribe(_ => StopFilling());
```

## Other Advanced Features

### Float/Trigger Long-Press

For analog inputs (like gamepad triggers), you can use a threshold to define "pressed" state:

YAML configuration:

```yaml
- type: Float
  action: FireTrigger
  longPressMs: 600
  longPressValueThreshold: 0.6 # Threshold (0-1) considered as pressed
  deviceBindings:
    - "<Gamepad>/leftTrigger"
```

Code usage:

```csharp
_input.GetLongPressObservable("PlayerActions", "FireTrigger").Subscribe(_ => StartCharge());
_input.GetPressStateObservable("PlayerActions", "FireTrigger").Where(p => !p).Subscribe(_ => CancelCharge());
```

### Mutual Exclusivity in the Same Context

If you must decide short vs long press within a single context (no context switch), use press-state + long-press streams to ensure only one fires:

```csharp
var press = _input.GetPressStateObservable("PlayerActions", "Confirm");
var longPress = _input.GetLongPressObservable("PlayerActions", "Confirm").Share();
float thresholdSec = 0.5f; // keep in sync with YAML

bool isPressed = false;
float startTime = 0f;
bool longFired = false;

longPress.Subscribe(_ => longFired = true);
press.Subscribe(p =>
{
  if (p)
  {
    isPressed = true; startTime = Time.realtimeSinceStartup; longFired = false;
  }
  else if (isPressed)
  {
    var dur = Time.realtimeSinceStartup - startTime;
    if (!longFired && dur < thresholdSec) OnShortClick();
    if (longFired) OnLongPress();
    isPressed = false;
  }
});
```

### Editor Tips

- **Code Generation**: The editor window provides settings to customize the output directory and namespace for the generated `InputActions.cs` file. These settings are saved per-project in `EditorPrefs`.
- **Long Press**: The "Long Press (ms)" field is only respected for `Button` and `Float` action types. For `Float` types, you can also set a "Long Press Threshold (0-1)" to define what analog value counts as "pressed".
- **Vector2 Sources**: The `InputBindingConstants.Vector2Sources` class provides convenient constants for common Vector2 bindings like `Gamepad_LeftStick` and `Composite_WASD`.
- **Reset Configuration**: The editor window provides a "Reset User to Default" button to reset user configuration to default.

## API Overview

### IInputPlayer

Input interface for a single player.

#### Properties

- `ReadOnlyReactiveProperty<string> ActiveContextName` - Name of the currently active context
- `ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind` - Current active device type (keyboard/mouse/gamepad/other)
- `event Action<string> OnContextChanged` - Context change event
- `int PlayerId` - Player ID (InputPlayer implementation only)
- `InputUser User` - Unity Input System user object (InputPlayer implementation only)

#### Constant Param based API (Recommended)

Use generated constants to completely avoid runtime string operations:

- `Observable<Vector2> GetVector2Observable(int actionId)`
- `Observable<Unit> GetButtonObservable(int actionId)`
- `Observable<Unit> GetLongPressObservable(int actionId)`
- `Observable<bool> GetPressStateObservable(int actionId)` - Press state stream (true=pressed, false=released)
- `Observable<float> GetScalarObservable(int actionId)` - Scalar value stream (for Float type actions)

#### String-Based API (Compatibility Mode)

- `Observable<Vector2> GetVector2Observable(string actionName)` - Uses current context's ActionMap
- `Observable<Vector2> GetVector2Observable(string actionMapName, string actionName)` - Specify ActionMap
- `Observable<Unit> GetButtonObservable(string actionName)`
- `Observable<Unit> GetButtonObservable(string actionMapName, string actionName)`
- `Observable<Unit> GetLongPressObservable(string actionName)`
- `Observable<Unit> GetLongPressObservable(string actionMapName, string actionName)`
- `Observable<bool> GetPressStateObservable(string actionName)`
- `Observable<bool> GetPressStateObservable(string actionMapName, string actionName)`
- `Observable<float> GetScalarObservable(string actionName)`
- `Observable<float> GetScalarObservable(string actionMapName, string actionName)`

#### Context Management

- `void RegisterContext(InputContext context)` - Register context (must be called before PushContext)
- `void PushContext(string contextName)` - Push new context to the top of the stack
- `void PopContext()` - Pop the top context, restore the previous context

#### Input Control

- `void BlockInput()` - Block all input
- `void UnblockInput()` - Restore input (restores the currently active context's ActionMap)

### InputManager

Singleton manager for the input system.

#### Static Properties

- `static InputManager Instance` - Singleton instance
- `static bool IsListeningForPlayers` - Whether currently listening for player joins

#### Events

- `event Action<IInputPlayer> OnPlayerInputReady` - Player input ready event (triggered when player joins or input is refreshed)
- `event Action OnConfigurationReloaded` - Configuration reload event

#### Initialization

- `void Initialize(string yamlContent, string userConfigUri)` - Initialize manager (usually called by `InputSystemLoader`)

#### Player Join Methods

- `IInputPlayer JoinSinglePlayer(int playerIdToJoin = 0)` - Synchronously join a single player (auto-lock devices). If player is already joined, returns existing service without triggering `OnPlayerInputReady` event.
- `UniTask<IInputPlayer> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)` - Asynchronously join a single player (wait for device connection)
- `List<IInputPlayer> JoinPlayersBatch(List<int> playerIds)` - Batch synchronously join players
- `UniTask<List<IInputPlayer>> JoinPlayersBatchAsync(List<int> playerIds, int timeoutPerPlayerInSeconds = 5)` - Batch asynchronously join players
- `IInputPlayer JoinPlayerOnSharedDevice(int playerIdToJoin)` - Join player on shared device
- `IInputPlayer JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)` - Lock specific device to player
- `IInputPlayer GetInputPlayer(int playerId)` - Get existing input player for the specified player ID, or null if not joined
- `bool RefreshPlayerInput(int playerId)` - Refreshes player input by triggering `OnPlayerInputReady` event for an already joined player. Useful when you dynamically bind input contexts after the player has already joined (e.g., in a different scene). This allows the InputSystem to recognize and manage newly bound input contexts. Returns true if player exists and event was triggered, false otherwise.

#### Lobby Mode

- `void StartListeningForPlayers(bool lockDeviceOnJoin)` - Start listening for player joins
  - `lockDeviceOnJoin = true`: Device locking mode (all devices paired to player 0)
  - `lockDeviceOnJoin = false`: Shared devices mode (each device creates a new player)
- `void StopListeningForPlayers()` - Stop listening for player joins

#### Configuration Management

- `async UniTask<bool> ReloadConfigurationAsync()` - Reload configuration
- `async UniTask SaveUserConfigurationAsync()` - Save user configuration

#### Cleanup

- `void Dispose()` - Release all resources (including all players' `InputPlayer` instances)

### InputContext

Input context containing action bindings and commands.

#### Constructor

- `InputContext(string name, string actionMapName)` - Create context

#### Methods

- `InputContext AddBinding(Observable<Unit> source, IActionCommand command)` - Add button action binding
- `InputContext AddBinding(Observable<Vector2> source, IMoveCommand command)` - Add Vector2 action binding
- `InputContext AddBinding(Observable<float> source, IScalarCommand command)` - Add scalar action binding

### Command Interfaces

- `IActionCommand` - No-parameter command interface
- `IMoveCommand` - Vector2 parameter command interface
- `IScalarCommand` - float parameter command interface

### Predefined Command Classes

- `ActionCommand(Action action)` - No-parameter command
- `MoveCommand(Action<Vector2> action)` - Vector2 command
- `ScalarCommand(Action<float> action)` - float command

### InputSystemLoader

Configuration loader that handles loading default and user configurations.

#### Static Methods

- `static async Task InitializeAsync(string defaultConfigUri, string userConfigUri)` - Initialize input system
  - Prioritizes loading user configuration, falls back to default if not found
  - Automatically copies default configuration to user configuration directory on first run

### Generated InputActions Class

After code generation, you will get:

```csharp
namespace YourGame.Input.Generated
{
    public static class InputActions
    {
        public static class ActionMaps
        {
            public static readonly int PlayerActions = ...;
            // Other ActionMap constants
        }

        public static class Actions
        {
            public static readonly int Gameplay_Move = ...;
            public static readonly int Gameplay_Confirm = ...;
            // Other action constants (format: ContextName_ActionName)
        }
    }
}
```

Usage:

```csharp
using YourGame.Input.Generated;

// Use constants to get Observable
var moveStream = inputService.GetVector2Observable(InputActions.Actions.Gameplay_Move);
```

## Dependency Injection (VContainer) Integration

### Installation

The package includes a VContainer installer. Register it in your DI container setup:

#### Option 1: URI-based Loading (StreamingAssets/PersistentData)

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Install InputSystem with URI-based config loading
        var inputSystemInstaller = new InputSystemVContainerInstaller(
            defaultConfigFileName: "input_config.yaml",
            userConfigFileName: "user_input_settings.yaml",
            postInitCallback: async resolver =>
            {
                // Optional: Setup initial player after initialization
                var inputResolver = resolver.Resolve<IInputPlayerResolver>();
                var player0Input = inputResolver.GetInputPlayer(0);
                // Setup contexts, etc.
            }
        );
        inputSystemInstaller.Install(builder);

        // Register your game systems that depend on input
        builder.Register<PlayerController>(Lifetime.Scoped);
    }
}
```

#### Option 2: AssetManagement Loading (YooAsset/Addressables)

If you're using `CycloneGames.AssetManagement` with YooAsset or Addressables:

**Important**: User config is **always** loaded from `PersistentData` path automatically. You only need to provide a loader for the default config.

> **Note on Config Loading Methods:**
>
> - **TextAsset** (recommended for Addressables/Resources): Loads YAML as a Unity TextAsset. Works with all providers (YooAsset, Addressables, Resources). Your YAML file should be imported as a TextAsset in Unity.
> - **RawFile** (YooAsset only): Loads YAML as a raw file. Only works with YooAsset provider. More efficient for YooAsset but not supported by Addressables/Resources.
>
> By default, the helper tries RawFile first (for YooAsset), then falls back to TextAsset (for Addressables/Resources). You can also explicitly specify `useTextAsset: true` to force TextAsset loading.

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using CycloneGames.AssetManagement.Runtime.Integrations.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Install AssetManagement first
        var assetManagementInstaller = new AssetManagementVContainerInstaller();
        assetManagementInstaller.Install(builder);

        // Create default config loader from AssetManagement
        // Get package directly (not from resolver) and create loader
        // The package should be obtained from your AssetManagement setup
        var package = assetModule.GetPackage("DefaultPackage"); // Get package from your setup
        var defaultLoader = InputSystemAssetManagementHelper.CreateDefaultConfigLoader(
            package: package,
            defaultConfigLocation: "input_config.yaml",
            useTextAsset: false  // false = try RawFile first, fallback to TextAsset
        );

        // Install InputSystem
        // User config will be automatically loaded from PersistentData/user_input_settings.yaml
        var inputSystemInstaller = new InputSystemVContainerInstaller(
            defaultLoader,
            userConfigFileName: "user_input_settings.yaml", // Optional: specify user config filename
            postInitCallback: async resolver =>
            {
                var inputResolver = resolver.Resolve<IInputPlayerResolver>();
                var player0Input = inputResolver.GetInputPlayer(0);
                // Setup contexts, etc.
            }
        );
        inputSystemInstaller.Install(builder);

        builder.Register<PlayerController>(Lifetime.Scoped);
    }
}
```

**How it works:**

1. First, tries to load user config from `PersistentData/user_input_settings.yaml` (or subdirectory path if specified, e.g., `ConfigFolder/user_input_settings.yaml`)
2. If not found, loads default config from AssetManagement (or StreamingAssets if using Option 1)
3. If default config was loaded, automatically saves it to `PersistentData/user_input_settings.yaml` (or subdirectory) for future use
4. All subsequent saves/loads of user config go to `PersistentData` path

**Path Support:**

- **User Config**: Supports subdirectories (e.g., `ConfigFolder/user_input_settings.yaml` will be saved to `PersistentData/ConfigFolder/user_input_settings.yaml`). Directory will be created automatically if it doesn't exist.
- **Default Config (StreamingAssets)**: Supports subdirectories (e.g., `Config/input_config.yaml` will load from `StreamingAssets/Config/input_config.yaml`).
- **Default Config (AssetManagement)**: Use the location as defined in your AssetManagement package (e.g., `Assets/Config/input_config.yaml` or just `input_config.yaml`).

#### Option 3: Custom Default Config Loader

For complete control over default config loading (e.g., from database, network, etc.):

```csharp
var inputSystemInstaller = new InputSystemVContainerInstaller(
    defaultLoader: async resolver =>
    {
        // Your custom loading logic
        // e.g., load from database, network, etc.
        return await LoadConfigFromCustomSource();
    },
    userConfigFileName: "user_input_settings.yaml" // User config always from PersistentData
);
inputSystemInstaller.Install(builder);
```

**Note**: User config is always managed in `PersistentData` path because:

- It needs to be writable for saving user customizations
- It persists across app updates
- It's separate from the default config which may be in read-only locations (AssetManagement, StreamingAssets)

#### Option 4: Delayed Initialization (Hot-Update Scenarios)

For hot-update games where AssetManagement packages may not be ready at registration time:

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Install AssetManagement first
        var assetManagementInstaller = new AssetManagementVContainerInstaller();
        assetManagementInstaller.Install(builder);

        // Install InputSystem with delayed initialization
        // Set autoInitialize: false to delay initialization until package is ready
        // Get package from your AssetManagement setup (not from resolver)
        var package = assetModule.GetPackage("DefaultPackage"); // Get from your setup
        var defaultLoader = InputSystemAssetManagementHelper.CreateDefaultConfigLoader(
            package: package,
            defaultConfigLocation: "Assets/Config/input_config.yaml"
        );

        var inputSystemInstaller = new InputSystemVContainerInstaller(
            defaultConfigLoader: defaultLoader,
            userConfigFileName: "user_input_settings.yaml",
            autoInitialize: false // Delay initialization
        );
        inputSystemInstaller.Install(builder);

        builder.Register<PlayerController>(Lifetime.Scoped);
    }

    protected override async UniTaskVoid Start()
    {
        // Wait for AssetManagement package to be ready
        var assetModule = Container.Resolve<IAssetModule>();
        await assetModule.InitializeAsync();

        var defaultPackage = assetModule.CreatePackage("DefaultPackage");
        await defaultPackage.InitializeAsync(/* ... */);

        // Now initialize InputSystem manually
        var initializer = Container.Resolve<IInputSystemInitializer>();
        await initializer.InitializeAsync(Container);

        // Setup players, etc.
    }
}
```

**Updating Configuration After Hot-Update:**

```csharp
public class HotUpdateHandler
{
    private readonly IInputSystemInitializer _inputInitializer;
    private readonly IAssetPackage _package;  // Inject package directly, not IAssetModule

    [Inject]
    public HotUpdateHandler(IInputSystemInitializer inputInitializer, IAssetPackage package)
    {
        _inputInitializer = inputInitializer;
        _package = package;  // Package should be registered in DI container
    }

    public async UniTask OnHotUpdateComplete()
    {
        // After hot-update, reload config from updated AssetManagement package
        // Option 1: Use ReinitializeFromPackageAsync (recommended)
        await _inputInitializer.ReinitializeFromPackageAsync(
            _package,
            "Assets/Config/input_config.yaml",
            saveToUserConfig: true
        );

        // Option 2: Manual load and update
        // var loader = InputSystemAssetManagementHelper.CreateConfigLoader(
        //     package,
        //     "Assets/Config/input_config.yaml"
        // );
        // string newConfig = await loader();
        // if (!string.IsNullOrEmpty(newConfig))
        // {
        //     await _inputInitializer.UpdateConfigurationAsync(newConfig, saveToUserConfig: true);
        // }
    }
}
```

**Reloading User Configuration (Player Key Rebinding):**

```csharp
public class SettingsMenu
{
    private readonly IInputSystemInitializer _inputInitializer;

    [Inject]
    public SettingsMenu(IInputSystemInitializer inputInitializer)
    {
        _inputInitializer = inputInitializer;
    }

    public async UniTask OnPlayerSavedKeyBindings()
    {
        // Player has modified and saved key bindings
        // Reload the updated user configuration
        await _inputInitializer.ReloadUserConfigurationAsync();
    }
}
```

**Cross-Scene/Cross-Resolver Usage:**

You can resolve `IInputSystemInitializer` from any resolver (parent or child scope) to reload configuration:

```csharp
// In any scene or LifetimeScope
public class SomeOtherScene : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // ... other registrations
    }

    protected override async UniTaskVoid Start()
    {
        // Resolve initializer from parent scope
        var initializer = Parent.Container.Resolve<IInputSystemInitializer>();

        // Get package from your setup (should be registered in DI or obtained from your AssetManagement setup)
        var package = Parent.Container.Resolve<IAssetPackage>(); // Or get from your AssetManagement setup
        await initializer.ReinitializeFromPackageAsync(package, "Assets/Config/input_config.yaml");
    }
}
```

### Usage Patterns

#### Pattern 1: Inject IInputPlayerResolver (Recommended)

Use the resolver to get input services when needed:

```csharp
using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using VContainer;

public class PlayerController
{
    private readonly IInputPlayerResolver _inputResolver;
    private IInputPlayer _input;

    [Inject]
    public PlayerController(IInputPlayerResolver inputResolver)
    {
        _inputResolver = inputResolver;
    }

    public void Initialize(int playerId)
    {
        _input = _inputResolver.GetInputPlayer(playerId);

        var ctx = new InputContext("Gameplay", "PlayerActions")
            .AddBinding(_input.GetVector2Observable("Move"), new MoveCommand(OnMove))
            .AddBinding(_input.GetButtonObservable("Confirm"), new ActionCommand(OnConfirm));

        _input.RegisterContext(ctx);
        _input.PushContext("Gameplay");
    }

    private void OnMove(Vector2 dir) { /* ... */ }
    private void OnConfirm() { /* ... */ }
}
```

#### Pattern 2: Inject InputManager Directly

For advanced scenarios where you need full control:

```csharp
using CycloneGames.InputSystem.Runtime;
using VContainer;

public class GameSession
{
    private readonly InputManager _inputManager;

    [Inject]
    public GameSession(InputManager inputManager)
    {
        _inputManager = inputManager;
    }

    public async UniTask StartMultiplayerLobby()
    {
        _inputManager.OnPlayerInputReady += OnPlayerInputReady;
        _inputManager.StartListeningForPlayers(false); // Shared devices mode
    }

    private void OnPlayerInputReady(IInputPlayer service)
    {
        // Setup player-specific input contexts
        var ctx = new InputContext("Gameplay", "PlayerActions")
            .AddBinding(service.GetVector2Observable("Move"), new MoveCommand(OnMove));
        service.RegisterContext(ctx);
        service.PushContext("Gameplay");
    }
}
```

#### Pattern 3: Factory Method with Player ID

Create a factory that resolves input services by player ID:

```csharp
using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using VContainer;

public class PlayerFactory
{
    private readonly IInputPlayerResolver _inputResolver;
    private readonly IObjectResolver _resolver;

    [Inject]
    public PlayerFactory(IInputPlayerResolver inputResolver, IObjectResolver resolver)
    {
        _inputResolver = inputResolver;
        _resolver = resolver;
    }

    public PlayerController CreatePlayer(int playerId)
    {
        var inputService = _inputResolver.GetInputPlayer(playerId);
        var controller = _resolver.Resolve<PlayerController>();
        controller.Initialize(inputService, playerId);
        return controller;
    }
}
```

### Complete Example: VContainer Integration

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using YourGame.Input.Generated;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Install InputSystem
        builder.Install(new InputSystemVContainerInstaller());

        // Register game systems
        builder.Register<PlayerController>(Lifetime.Scoped);
        builder.Register<GameSession>(Lifetime.Singleton);
    }

    protected override async UniTaskVoid Start()
    {
        // InputSystem is automatically initialized by the installer
        // Now you can use it
        var resolver = Container.Resolve<IInputPlayerResolver>();
        var inputService = resolver.GetInputPlayer(0);

        // Setup input context
        var ctx = new InputContext("Gameplay", "PlayerActions")
            .AddBinding(
                inputService.GetVector2Observable(InputActions.Actions.Gameplay_Move),
                new MoveCommand(OnMove)
            )
            .AddBinding(
                inputService.GetButtonObservable(InputActions.Actions.Gameplay_Confirm),
                new ActionCommand(OnConfirm)
            );

        inputService.RegisterContext(ctx);
        inputService.PushContext("Gameplay");
    }

    private void OnMove(Vector2 dir) { /* ... */ }
    private void OnConfirm() { /* ... */ }
}

// Example: PlayerController with injected input
public class PlayerController
{
    private readonly IInputPlayerResolver _inputResolver;
    private IInputPlayer _input;

    [Inject]
    public PlayerController(IInputPlayerResolver inputResolver)
    {
        _inputResolver = inputResolver;
    }

    public void Initialize(int playerId)
    {
        _input = _inputResolver.GetInputPlayer(playerId);
        // Setup contexts...
    }
}
```
