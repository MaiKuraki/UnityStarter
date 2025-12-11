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
  private IInputService _input;

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
    // Cleanup: InputService will be automatically cleaned up when InputManager.Dispose() is called
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
```

#### Lobby Mode (Device Locking)

The first device joins as Player 0, and subsequent devices are automatically paired to that player. Ideal for single players switching between keyboard and gamepad:

```csharp
InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;
InputManager.Instance.StartListeningForPlayers(true); // true = device locking mode
```

#### Lobby Mode (Shared Devices)

Each new device creates a new player, perfect for local co-op:

```csharp
InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;
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
// Listen for player join events
InputManager.Instance.OnPlayerJoined += (IInputService playerInput) =>
{
    Debug.Log($"Player {((InputService)playerInput).PlayerId} joined");
    // Set up player input context, etc.
};

// Listen for configuration reload events
InputManager.Instance.OnConfigurationReloaded += () =>
{
    Debug.Log("Configuration reloaded");
};

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

### IInputService

Input service interface for a single player.

#### Properties

- `ReadOnlyReactiveProperty<string> ActiveContextName` - Name of the currently active context
- `ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind` - Current active device type (keyboard/mouse/gamepad/other)
- `event Action<string> OnContextChanged` - Context change event
- `int PlayerId` - Player ID (InputService implementation only)
- `InputUser User` - Unity Input System user object (InputService implementation only)

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

- `event Action<IInputService> OnPlayerJoined` - Player join event
- `event Action OnConfigurationReloaded` - Configuration reload event

#### Initialization

- `void Initialize(string yamlContent, string userConfigUri)` - Initialize manager (usually called by `InputSystemLoader`)

#### Player Join Methods

- `IInputService JoinSinglePlayer(int playerIdToJoin = 0)` - Synchronously join a single player (auto-lock devices)
- `UniTask<IInputService> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)` - Asynchronously join a single player (wait for device connection)
- `List<IInputService> JoinPlayersBatch(List<int> playerIds)` - Batch synchronously join players
- `UniTask<List<IInputService>> JoinPlayersBatchAsync(List<int> playerIds, int timeoutPerPlayerInSeconds = 5)` - Batch asynchronously join players
- `IInputService JoinPlayerOnSharedDevice(int playerIdToJoin)` - Join player on shared device
- `IInputService JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)` - Lock specific device to player

#### Lobby Mode

- `void StartListeningForPlayers(bool lockDeviceOnJoin)` - Start listening for player joins
  - `lockDeviceOnJoin = true`: Device locking mode (all devices paired to player 0)
  - `lockDeviceOnJoin = false`: Shared devices mode (each device creates a new player)
- `void StopListeningForPlayers()` - Stop listening for player joins

#### Configuration Management

- `async UniTask<bool> ReloadConfigurationAsync()` - Reload configuration
- `async UniTask SaveUserConfigurationAsync()` - Save user configuration

#### Cleanup

- `void Dispose()` - Release all resources (including all players' InputService)

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
