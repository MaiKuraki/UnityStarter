# CycloneGames.InputSystem

>Note: The CycloneGames.InputSystem code was authored by the project's developer; this documentation was prepared with AI assistance.

A reactive wrapper around Unity Input System with context stacks, multi-player joining, device locking, YAML-based configuration, and an Editor tool.

English | [简体中文](./README.SCH.md)

## Features

- **Context Stack**: Push/pop contexts to manage input states (e.g., Gameplay, UI, Cutscene).
- **Rich Multi-Player Modes**:
    - **Single-Player**: Auto-joins and locks all required devices to one player.
    - **Lobby (Device Locking)**: The first device joins as Player 0. Subsequent devices are automatically paired to this single player, ideal for allowing one player to switch between keyboard and gamepad seamlessly.
    - **Lobby (Shared Devices)**: Each new device joins as a new player (Player 0, 1, 2...), perfect for local co-op.
- **Zero-GC API**: Optional, high-performance API using generated constants to eliminate runtime string operations and garbage collection.
- **Configurable Code Generation**:
    - Automatically generate a static `InputActions` class from your YAML config.
    - Customize the output directory and namespace to fit your project structure, keeping your `Packages` folder clean.
- **Reactive API (R3)**: Provides `Observable` streams for button presses, long presses, analog values, and more.
- **Intelligent Hot-Swapping**: Automatically pairs newly connected devices to the correct player *after* the lobby phase.
- **Active Device Detection**: `ActiveDeviceKind` property tracks whether the last input came from Keyboard/Mouse or a Gamepad.

## Install

- Unity 2022.3+
- Enable Input System package
- Dependencies: UniTask, R3, VYaml, CycloneGames.Utility, CycloneGames.Logger

## Quick Start

1) Create default config: `Tools → CycloneGames → Input System Editor → Generate Default Config`.
2) **(Recommended)** Configure Code Generation:
    - In the editor window, set the **Output Directory** (e.g., `Assets/Scripts/Generated`) and **Namespace** for the generated `InputActions.cs` file.
    - Click **Save and Generate Constants**.
3) Initialize at boot:

```csharp
var defaultUri = FilePathUtility.GetUnityWebRequestUri("input_config.yaml", UnityPathSource.StreamingAssets);
var userUri = FilePathUtility.GetUnityWebRequestUri("user_input_settings.yaml", UnityPathSource.PersistentData);
await InputSystemLoader.InitializeAsync(defaultUri, userUri);
```

1) Join and set context using the generated constants for performance and type safety:

```csharp
// Make sure to import the generated namespace
using YourGame.Input.Generated;

var svc = InputManager.Instance.JoinSinglePlayer(0);
var ctx = new InputContext("Gameplay", "PlayerActions")
  .AddBinding(svc.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), new ActionCommand(() => {/*...*/}));
svc.RegisterContext(ctx);
svc.PushContext("Gameplay");
```

> **Note**: The original string-based API (`GetVector2Observable("PlayerActions", "Move")`) is still available for projects that do not wish to use code generation.

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
            longPressMs: 600                 # optional long-press for float
            longPressValueThreshold: 0.6     # threshold (0-1) considered as pressed
            deviceBindings:
              - "<Gamepad>/leftTrigger"
```

## Minimal Example (Beginner Friendly)

1) Create a MonoBehaviour named `SimplePlayer`:

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
            longPressMs: 600  # long press only for this context
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

## Tutorial: Hold-to-Fill Progress (Press-and-Hold)

Goal: while the button is held, increase a progress bar; stop (or reset) on release.

1) Subscribe to press-state:

```csharp
var isPressing = _input.GetPressStateObservable("PlayerActions", "Confirm");
```

### Float/Trigger Long-Press

YAML (Float with threshold):

```yaml
- type: Float
  action: FireTrigger
  longPressMs: 600
  longPressValueThreshold: 0.6
  deviceBindings:
    - "<Gamepad>/leftTrigger"
```

Code:

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

2) Increment while pressed:

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
        // Update UI here
      }
    });
  }
  else
  {
    // On release: stop. Optionally reset
    // progress = 0f;
  }
});
```

Optional: require a minimum hold before starting. In YAML, set:

```yaml
- type: Button
  action: Confirm
  longPressMs: 500
  deviceBindings:
    - "<Keyboard>/space"
    - "<Gamepad>/buttonSouth"
```

Then start on long-press and stop on release:

```csharp
_input.GetLongPressObservable("PlayerActions", "Confirm").Subscribe(_ => StartFilling());
_input.GetPressStateObservable("PlayerActions", "Confirm").Where(p => !p).Subscribe(_ => StopFilling());
```

1) Ensure YAML has actions:

```yaml
bindings:
  - type: Vector2
    action: Move
    deviceBindings:
      - "<Gamepad>/leftStick"
      - "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
  - type: Button
    action: Confirm
    deviceBindings:
      - "<Gamepad>/buttonSouth"
      - "<Keyboard>/space"
```

Device kind usage:

```csharp
_input.ActiveDeviceKind.Subscribe(kind => UpdateHUDIcons(kind));
```

## API

- `IInputService`
  - `ReadOnlyReactiveProperty<string> ActiveContextName`
  - `ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind`
  - `event Action<string> OnContextChanged`
  - **Zero-GC API (Recommended)**
    - `GetVector2Observable(int actionId)`
    - `GetButtonObservable(int actionId)`
    - `GetLongPressObservable(int actionId)`
    - `GetPressStateObservable(int actionId)`
    - `GetScalarObservable(int actionId)`
  - **String-Based API (Legacy/Optional)**
    - `Get...Observable(string actionName)`
    - `Get...Observable(string actionMapName, string actionName)`
  - `RegisterContext(InputContext context)`
  - `PushContext(string contextName)`
  - `PopContext()`
  - `BlockInput()`, `UnblockInput()`
