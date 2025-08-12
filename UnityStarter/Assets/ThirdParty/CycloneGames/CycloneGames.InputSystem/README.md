# CycloneGames.InputSystem

>Note: The CycloneGames.InputSystem code was authored by the project's developer; this documentation was prepared with AI assistance.

A reactive wrapper around Unity Input System with context stacks, multi-player joining, device locking, YAML-based configuration, and an Editor tool.

English | [简体中文](./README.SCH.md)

## Features

- Context stack with push/pop and per-context action maps
- Multi-player join modes: single-player lock, shared devices, lobby join with optional device locking
- YAML config with explicit action types (Button, Vector2, Float)
- Editor window: generate/load/save configs; constant picker for bindings
- Reactive API (R3) with Observables per action
- Hot-swap required devices per player, safe pairing

## Install

- Unity 2022.3+
- Enable Input System package
- Dependencies: UniTask, R3, VYaml, CycloneGames.Utility, CycloneGames.Logger

## Quick Start

1) Create default config: Tools → CycloneGames → Input System Editor → Generate Default Config
2) Initialize at boot:

```csharp
var defaultUri = FilePathUtility.GetUnityWebRequestUri("input_config.yaml", UnityPathSource.StreamingAssets);
var userUri = FilePathUtility.GetUnityWebRequestUri("user_input_settings.yaml", UnityPathSource.PersistentData);
await InputSystemLoader.InitializeAsync(defaultUri, userUri);
```

1) Join and set context:

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
          - type: Button
            action: Confirm
            deviceBindings:
              - "<Gamepad>/buttonSouth"
              - "<Keyboard>/space"
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
      .AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirm));

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
}
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

## API

- IInputService
  - `ReadOnlyReactiveProperty<string>` ActiveContextName; `event OnContextChanged`
  - GetVector2Observable(map, action) | GetVector2Observable(action)
  - GetButtonObservable(map, action) | GetButtonObservable(action)
  - GetScalarObservable(map, action) | GetScalarObservable(action)
  - RegisterContext, PushContext, PopContext, BlockInput, UnblockInput
