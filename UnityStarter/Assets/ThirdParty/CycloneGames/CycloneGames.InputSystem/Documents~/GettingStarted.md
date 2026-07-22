# Getting Started

[English | 简体中文](GettingStarted.SCH.md)

Related: [Configuration guide](Configuration.md) | [Runtime guide](RuntimeGuide.md) | [Module reference](../README.md)

## Overview

This guide takes a project from an authored configuration to one active player and one gameplay context. It uses a serialized `TextAsset` to keep the first integration visible before choosing a storage policy.

### Prerequisites

- The project installs Unity Input System and includes this module.
- The consumer assembly references `CycloneGames.InputSystem.Runtime`.
- The scene contains the devices required by the selected control scheme.
- The configuration stays within module validation budgets.

## Core Concepts

A usable configuration needs four identities:

1. A player slot, such as player `0`.
2. A context, such as `Gameplay`.
3. An action map, such as `PlayerActions`.
4. An action, such as `Confirm` or `Move`.

The full runtime identity is `context/actionMap/action`. Generated action IDs are deterministic FNV-1a hashes of this identity.

## Usage Guide

### Create a configuration

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

### Initialize one manager

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

### Bind an action through a context

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

### Add generated identities

In the Input System Editor:

1. Select a code output folder under `Assets`.
2. Enter the generated namespace.
3. Choose `Save User + Generate Code`, or generate after saving the intended configuration owner.

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

### Choose the configuration owner

| Pattern | Source | Appropriate use |
| --- | --- | --- |
| Scene-owned | Serialized `TextAsset` | Small projects, explicit scene setup, tests |
| Packaged file | `UriInputConfigurationSource` with StreamingAssets | Product-authored files included in a build |
| User settings | `IInputConfigurationStore` | Local bindings and product-managed input settings |
| Asset package | Product adapter | Downloadable or package-owned configuration |
| In-memory | Product source implementation | Tests, server tools, generated configuration |

The foundation project does not require a project-level StreamingAssets file.

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Initialization fails | Empty or malformed YAML, schema error, identity uniqueness violation, or budget exceeded | Inspect `InputManagerInitializationResult.Status` and `Message`. |
| `JoinSinglePlayer` returns `null` | No control scheme matches available devices, or devices are already claimed | Verify `deviceRequirements` and check Input Debugger for paired/reserved devices. |
| No action fires | Context not pushed, name mismatch (case-sensitive), context blocked/captured, or input globally blocked | Use context-qualified getters and inspect `ActiveContextName`. |
| Generated code won't compile | Duplicate identities or action-ID collision | Check YAML for unique context, map, and action names; regenerate. |

Continue with the [Configuration guide](Configuration.md) for field semantics and the [Runtime guide](RuntimeGuide.md) for contexts, multiplayer, persistence, and production ownership.
