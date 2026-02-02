# RPG Interaction Module

A high-performance, reactive interaction system for Unity RPGs. Built on **R3** (Reactive Extensions) and **VitalRouter** for decoupled messaging, and optimized with an automatic LOD (Level of Detail) detection system.

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

## ✨ Features

- ⚡ **Reactive Architecture** - Built on R3 for event-driven updates and property binding.
- 📡 **VitalRouter Integration** - Decoupled command handling for local and networked interactions.
- 👁️ **LOD Detection System** - Automatic frequency scaling based on target distance (High update rate near, low far) to save CPU.
- 🎯 **Weighted Scoring** - Smart target selection combining Distance and Angle weights.
- 📝 **Localization Ready** - Built-in support for localized prompt text via `InteractionPromptData`.
- 🔌 **Editor Friendly** - Custom Inspectors and Debug Gizmos for tuning detection areas.

## 📦 Dependencies

- **R3**: For reactive properties and events.
- **VitalRouter**: For command routing.
- **UniTask**: For async/await operations.

## 🚀 Quick Start

### Step 1: Create an Interactable Object

Add functionality to any GameObject (e.g., a chest or NPC):

1. Add the `Interactable` script to the GameObject.
2. Configure the **Interaction Settings**:
   - **Interaction Prompt**: Text to display (e.g., "Open").
   - **Interaction Distance**: How close the player needs to be (e.g., `2.0`).
   - **Events**: Hook up `OnInteract` to your logic (e.g., play animation).

### Step 2: Setup the Player Detector

Add the detector to your Player Character or Camera:

1. Add the `InteractionDetector` script.
2. Assign the **Detection Origin** (usually the Camera or Player Head).
3. Set **Interactable Layer** to the layer your objects are on.

### Step 3: Initialize the System

Ensure you have a `InteractionSystem` in your scene or bootstrap logic. This handles the routing of commands.

```csharp
// The InteractionSystem usually initializes itself, but you can manually manage it if using DI
var system = new GameObject("InteractionSystem").AddComponent<InteractionSystem>();
system.Initialize();
```

### Step 4: Handle Input & Trigger Interaction

In your Player Controller, listen for input and publish a command via VitalRouter:

```csharp
using VitalRouter;
using R3;
using CycloneGames.RPGFoundation.Runtime.Interaction;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private InteractionDetector _detector;

    private void Start()
    {
        // Listen for the current best target
        _detector.CurrentInteractable
            .Subscribe(target => {
                if (target != null) Debug.Log($"Looking at: {target.InteractionPrompt}");
                // Update UI here
            });
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            var target = _detector.CurrentInteractable.CurrentValue;
            if (target != null)
            {
                // Dispatch command via VitalRouter
                Router.Default.PublishAsync(new InteractionCommand(target));
            }
        }
    }
}
```

## ⚙️ Configuration

### Interactable Component

| Parameter                | Description                                                    | Default    |
| ------------------------ | -------------------------------------------------------------- | ---------- |
| **Interaction Prompt**   | Text shown to player (fallback if no localization).            | "Interact" |
| **Is Interactable**      | Master switch to enable/disable interaction.                   | true       |
| **Priority**             | High priority overrides other objects (e.g., key item > door). | 0          |
| **Interaction Distance** | Max distance this object accepts interaction.                  | 2.0        |
| **Cooldown**             | Time after interaction before it can be used again.            | 0          |
| **Prompt Data**          | Struct for Localization Key/Table.                             | -          |

### Interaction Detector

The detector uses a Raycast/Cone check system with intelligent scoring.

#### Detection Settings

| Parameter             | Description                                                    |
| --------------------- | -------------------------------------------------------------- |
| **Detection Origin**  | Transform component used as the start point (e.g., Camera).    |
| **Detection Offset**  | Local offset from the Origin (useful for adjusting eye level). |
| **Detection Radius**  | Radius of the sphere check for candidates.                     |
| **Layer Mask**        | Layers to check for potential interactables.                   |
| **Obstruction Layer** | Layers that block line-of-sight (e.g., Walls).                 |
| **Max Interactables** | Buffer size for NonAlloc physics checks.                       |

#### Scoring Weights

The system picks the "Best" candidate by calculating a score: `Score = (Distance * DistanceWeight) + (Angle * AngleWeight)`. Lower score is better.

- **Distance Weight**: Importance of being close.
- **Angle Weight**: Importance of looking directly at the object.

#### LOD (performance) Settings

To optimize performance, the detector runs less frequently when valid targets are far away.

- **Near Interval**: Update rate when target is within `Near Distance` (e.g., 33ms = ~30fps).
- **Far Interval**: Update rate when target is further away (e.g., 150ms).
- **Sleep Mode**: If no targets are found for `Sleep Enter Ms`, the detector slows down significantly (`Sleep Interval Ms`).

## 🛠 Editor Tools

### Interaction Scene Debugger

_(If included in Editor folder)_  
Use `Window > CycloneGames > Interaction Debugger` (location may vary) to visualize active interactables and the detector's current logic state at runtime.

### Gizmos

- **Yellow Wire Sphere**: Shows the raw detection radius.
- **Red/Green Lines**: Show Raycast checks for Line-of-Sight.
- **Blue Line**: Points to the currently selected "Best" candidate.

## 🧩 Advanced Usage

### Custom Interaction Logic

Inherit from `Interactable` or implement `IInteractable` to create complex behaviors (e.g., a door that requires a key).

```csharp
public class DoorInteractable : Interactable
{
    public override async UniTask TryInteractAsync(CancellationToken ct)
    {
        if (HasKey())
        {
            await OpenDoorAnimation();
            base.TryInteractAsync(ct); // Fire standard events
        }
        else
        {
            ShowLockedMessage();
        }
    }
}
```

### VitalRouter Integration

The system is built on [VitalRouter](https://github.com/hadashiA/VitalRouter). This means you can intercept interactions globally:

```csharp
// Global Interceptor Example
public class InteractionLogger : ICommandInterceptor
{
    public async UniTask InvokeAsync<T>(T command, CancellationToken cancellation, Next<T> next) where T : ICommand
    {
        if (command is InteractionCommand ic)
        {
            Debug.Log($"Player interacted with {ic.Target}");
        }
        await next(command, cancellation);
    }
}
```
