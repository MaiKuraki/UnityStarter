# InputSystem Sample

[简体中文](README.SCH.md) | [Getting started](../Documents~/GettingStarted.md) | [Configuration guide](../Documents~/Configuration.md) | [Runtime guide](../Documents~/RuntimeGuide.md)

This opt-in sample demonstrates a complete local-input session: load a YAML configuration, create players, route actions through contexts, react to device changes, and release every owned resource. Its assembly is not auto-referenced, its scene is not in Build Settings, and its fixture is not copied into `StreamingAssets` automatically.

## Learning goals

Work through the sample in this order:

1. Run one keyboard player with a validated configuration.
2. Compare locked-device and shared-device join policies.
3. Follow `OnPlayerInputReady` from input player to spawned GameObject.
4. Bind generated action identities to commands in a gameplay context.
5. Verify cancellation, subscription cleanup, context disposal, and manager shutdown.
6. Switch one player between keyboard/mouse and a gamepad without re-joining.

## Contents

| File | Responsibility |
| --- | --- |
| `SampleScene.unity` | Scene setup for the bootstrap, spawn points, and player prefab. |
| `GameInitializer_Sample.cs` | Owns initialization, join policy, subscriptions, spawned players, and shutdown. |
| `SimplePlayerController.cs` | Receives commands and owns its runtime material instance. |
| `Generated/InputActions.cs` | Deterministic constants generated from the sample configuration. |
| `Fixtures/input_config.txt` | Versioned authoring fixture used only when running this sample. |
| `Fixtures/input_config_mixed.txt` | Single-player fixture that pairs keyboard, mouse, and gamepad at once. |

## Run the sample

1. Open `Samples/SampleScene.unity`.
2. Select `InputManagerSample` and confirm that `Configuration` and `Mixed Configuration` reference the fixtures in `Samples/Fixtures`.
3. Choose a `Startup Mode`.
4. Confirm that the player prefab, spawn points, and colors are assigned.
5. Enter Play Mode with devices appropriate for the selected mode.

The scene carries its configuration as `TextAsset` references, so nothing has to be copied into `StreamingAssets`. A product may instead use a `StreamingAssets` file, Addressables, a remote source, or another bounded `IInputConfigurationSource`. User overrides still go through `FileInputConfigurationStore`, which writes to `persistentDataPath`.

## Stage 1: Read the configuration

The fixture defines two control schemes, a `PlayerActions` action map, and a `Gameplay` context. Keyboard movement uses explicit `2DVector` composite parts. The startup mode decides whether connected devices are locked to one player or shared.

The generated identities used by the sample are:

```csharp
InputActions.Contexts.Gameplay
InputActions.ActionMaps.PlayerActions
InputActions.Actions.Gameplay_Move
InputActions.Actions.Gameplay_Confirm
```

Regenerate `InputActions.cs` after changing a context name, action-map name, or action name.

## Stage 2: Load and initialize

`GameInitializer_Sample` reads the authored configuration from a `TextAsset` reference and keeps user overrides in a store on `persistentDataPath`:

```csharp
var userStore = new FileInputConfigurationStore(
    Application.persistentDataPath);

InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
    new TextAssetInputConfigurationSource(_configuration),
    _configuration.name,
    userStore,
    "user_input_settings.yaml",
    InputManager.Instance,
    false,
    cancellationToken);

if (!result.IsSuccess)
{
    throw new InvalidOperationException(result.Error);
}
```

The bootstrap continues only after a successful load. Its cancellation token belongs to the bootstrap GameObject, so scene teardown cancels pending work.

## Stage 3: Choose a join policy

| Startup mode | Result | Typical use |
| --- | --- | --- |
| `AutoJoinLockedSinglePlayer` | Creates player `0` and locks a selected scheme. | Single-player gameplay. |
| `AutoJoinSharedKeyboard` | Creates players `0` and `1` on shared devices. | Two-player keyboard test. |
| `LobbyWithDeviceLocking` | Listens for join actions and locks participating devices. | Local multiplayer lobby. |
| `LobbyWithSharedDevices` | Listens for join actions while allowing shared devices. | Shared-device party controls. |
| `AutoJoinMixedDevices` | Creates player `0` and pairs every referenced device. | One player switching between gamepad and keyboard/mouse. |

Join failure is handled as initialization failure in the sample. A product can instead show a bounded retry UI, a device-selection screen, or an explicit cancel action.

## Stage 4: Spawn on player-ready

The manager raises `OnPlayerInputReady` after a player has an action asset. The handler validates the player ID and serialized references before instantiating the prefab. The dictionary prevents duplicate ready notifications from creating duplicate player objects.

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;

private void HandlePlayerInputReady(IInputPlayer inputPlayer)
{
    int playerId = inputPlayer.PlayerId;
    // Validate references, instantiate once, then create the context.
}
```

Keep presentation spawning outside `InputPlayer`. The input player owns input state; the scene owner decides which GameObject represents it.

## Stage 5: Route actions through a context

The sample creates one gameplay context per player and maps typed observables to commands:

```csharp
var gameplayContext = new InputContext(
        InputActions.ActionMaps.PlayerActions,
        InputActions.Contexts.Gameplay)
    .AddBinding(
        inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move),
        new MoveCommand(controller.OnMove))
    .AddBinding(
        inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Confirm),
        new ActionCommand(controller.OnConfirm))
    .AddBinding(
        inputPlayer.GetLongPressObservable(InputActions.Actions.Gameplay_Confirm),
        new ActionCommand(controller.OnConfirmLongPress));

gameplayContext.AddTo(controller.destroyCancellationToken);
inputPlayer.PushContext(gameplayContext);
```

This separates three responsibilities:

- YAML describes available actions and bindings.
- `InputContext` decides which actions are active for the current state.
- Commands call gameplay or presentation code without exposing Unity Input System callbacks to it.

For a pause menu, create a higher-priority menu context, push it when the menu opens, then dispose or pop it when the menu closes.

## Stage 6: Observe active devices

`ActiveDeviceKind` allows prompts and icons to follow the player's latest device:

```csharp
inputPlayer.ActiveDeviceKind
    .Subscribe(kind => UpdatePromptSet(kind))
    .AddTo(controller.destroyCancellationToken);
```

The sample tints the player object instead of swapping prompt sets, so a takeover is visible in the scene:

```csharp
if (controller != null) controller.ApplyDeviceTint(kind);
```

A takeover only reaches the player if the device is paired. Under a locked control scheme only that scheme's devices are paired, so the other device cannot drive anything regardless of its bindings. `pairAllMatchingDevices: true` on the player slot pairs every device the configuration references and leaves the scheme unset, which is what `Fixtures/input_config_mixed.txt` uses. It claims those devices for that player, so it is a single-player setting.

Treat this as presentation state. Device ownership and join policy remain the manager's responsibility.

## Stage 7: Release owned state

Shutdown follows the reverse ownership order:

1. Unsubscribe from `OnPlayerInputReady`.
2. Stop join listening.
3. Destroy spawned player objects; their cancellation tokens dispose contexts and subscriptions.
4. Dispose the owned manager.
5. Clear static ownership markers.

`SubsystemRegistration` resets static sample state when entering a new Play session, including configurations where Domain Reload is disabled.

## Persistence

The sample owns `user_input_settings.yaml` under `Application.persistentDataPath` through `FileInputConfigurationStore`. The file is user-local and must not be committed. When the user key is missing, the validated default fixture is selected and can be saved to the user store by the selected loading policy. Invalid user content is preserved for diagnostics while the validated default remains available for the session.

Delete the sample's user file only when deliberately resetting this sample. Production applications should expose reset through their settings or save service and should define backup, retention, and recovery behavior.

## Exercises

### Beginner

1. Change `Gameplay/PlayerActions/Confirm` from `<Keyboard>/enter` to `<Keyboard>/space`.
2. Open the Input System Editor, load the copied configuration, validate it, and generate constants.
3. Enter Play Mode and verify short press and long press separately.

### Intermediate

1. Add a `Menu` context with `Navigate`, `Submit`, and `Cancel` actions.
2. Generate constants and create a menu `InputContext`.
3. Push the menu context above gameplay and release it when the menu closes.

### Advanced

1. Add a settings screen that performs interactive rebinding.
2. Export the manager binding profile through a product-owned store.
3. Restart the session, import the profile before joining players, and verify reset behavior.
4. Add bounded diagnostics for missing layouts, invalid paths, and profile rejection.

## Validation

1. Run the `CycloneGames.InputSystem.Tests.Editor` EditMode tests.
2. Run the scene once with Domain Reload enabled and once with it disabled.
3. Exercise every startup mode with its required devices.
4. Trigger repeated player-ready refreshes and confirm one scene object per player ID.
5. Destroy the bootstrap during loading and confirm that no player or subscription remains.
6. Validate keyboard, mouse, and gamepad paths in the Unity Input Debugger.

These checks validate the Editor workflow and sample ownership. Target Player, IL2CPP, console, reconnect, suspend/resume, and long-running device behavior require platform-specific verification.
