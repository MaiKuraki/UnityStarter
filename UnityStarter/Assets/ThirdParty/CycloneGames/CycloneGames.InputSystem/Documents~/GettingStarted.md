# Getting Started

[English | 简体中文](GettingStarted.SCH.md)

Related: [Configuration guide](Configuration.md) | [Runtime guide](RuntimeGuide.md) | [Module reference](../README.md)

This guide takes a project from an authored configuration to one active player and one gameplay context. It starts with a serialized `TextAsset`, which keeps the first integration visible and avoids selecting a storage policy before the input model is understood.

## Prerequisites

- The project installs the Unity Input System package and includes this module.
- The consumer assembly references `CycloneGames.InputSystem.Runtime`.
- The scene contains the devices required by the selected control scheme.
- The configuration stays within the module validation budgets.

## Step 1: Understand the minimum model

A usable configuration needs four identities:

1. A player slot, such as player `0`.
2. A context, such as `Gameplay`.
3. An action map, such as `PlayerActions`.
4. An action, such as `Confirm` or `Move`.

The full runtime identity is `context/actionMap/action`. Generated action IDs are deterministic hashes of this identity.

## Step 2: Create a configuration

Open `Tools > CycloneGames > Input System Editor` and choose `Generate Default`. The generated working copy contains two player slots, keyboard/mouse and gamepad schemes, a `Gameplay` context, `Move`, and `Confirm`.

For a smaller first configuration:

```yaml
schemaVersion: 1
schemaFingerprint: ""
playerSlots:
  - playerId: 0
    controlSchemes:
      - name: KeyboardMouse
        bindingGroup: KeyboardMouse
        deviceRequirements:
          - controlPath: "<Keyboard>"
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: PlayerActions
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: KeyboardMouse
            deviceBindings:
              - "<Keyboard>/enter"
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
```

Save this as a project `TextAsset`, for example `Assets/Game/Input/input_config.yaml`. A `TextAsset` is suitable for scene-owned setup and tests. StreamingAssets is an optional source selected by the product composition root.

## Step 3: Initialize one manager

The owner constructs, initializes, and disposes the manager:

```csharp
using CycloneGames.InputSystem.Runtime;
using UnityEngine;

public sealed class InputBootstrap : MonoBehaviour
{
    [SerializeField] private TextAsset _configuration;

    private InputManager _manager;
    private IInputPlayer _player;
    private InputContext _gameplay;

    private void Awake()
    {
        _manager = new InputManager();
        InputManagerInitializationResult result =
            _manager.InitializeWithResult(_configuration.text);

        if (!result.IsSuccess)
        {
            Debug.LogError($"Input initialization failed: {result.Status}: {result.Message}", this);
            enabled = false;
            return;
        }

        _player = _manager.JoinSinglePlayer(0);
        if (_player == null)
        {
            Debug.LogError("Player 0 could not acquire a declared control scheme.", this);
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        _gameplay?.Dispose();
        _manager?.Dispose();
    }
}
```

One object must own the manager lifetime. Do not create a second manager for the same input session.

## Step 4: Bind an action through a context

Contexts own the active command subscriptions. Add this after player creation:

```csharp
_gameplay = new InputContext("PlayerActions", "Gameplay")
    .AddBinding(
        _player.GetButtonObservable("Gameplay", "PlayerActions", "Confirm"),
        new ActionCommand(OnConfirm));

_player.PushContext(_gameplay);
```

The command target remains ordinary product code:

```csharp
private void OnConfirm()
{
    Debug.Log("Confirm received.");
}
```

Disposing the context removes it from every player that currently uses it and releases its subscriptions.

## Step 5: Add generated identities

In the Input System Editor:

1. Select a code output folder under `Assets`.
2. Enter the generated namespace.
3. Choose `Save User + Generate Code`, or generate after saving the intended configuration owner.
4. Review and compile the generated `InputActions.cs`.

Call sites can then use stable generated IDs:

```csharp
using CycloneGames.InputSystem.Runtime.Generated;

_gameplay = new InputContext(
        InputActions.ActionMaps.PlayerActions,
        InputActions.Contexts.Gameplay)
    .AddBinding(
        _player.GetButtonObservable(InputActions.Actions.Gameplay_Confirm),
        new ActionCommand(OnConfirm));
```

Regenerate after changing a context, action-map, or action name.

## Step 6: Choose the configuration owner

| Pattern | Source | Appropriate use |
| --- | --- | --- |
| Scene-owned | Serialized `TextAsset` | Small projects, explicit scene setup, tests |
| Packaged file | `UriInputConfigurationSource` with StreamingAssets | Product-authored files included in a build |
| User settings | `IInputConfigurationStore` | Local bindings and product-managed input settings |
| Asset package | Product adapter | Downloadable or package-owned configuration |
| In-memory | Product source implementation | Tests, server tools, generated configuration |

The foundation project does not require a project-level StreamingAssets file.

## Step 7: Verify the first integration

1. Load the YAML in the Input System Editor and confirm the `VALID` state.
2. Enter Play Mode with the required device connected.
3. Confirm manager initialization succeeds.
4. Confirm player `0` joins exactly once.
5. Trigger the configured control and observe the command.
6. Destroy the bootstrap object and confirm its contexts and manager are disposed.

Continue with the [Configuration guide](Configuration.md) for field semantics and the [Runtime guide](RuntimeGuide.md) for contexts, multiplayer, persistence, and production ownership.
