# Cheat System with VitalRouter Integration
A lightweight, type-safe command pipeline for game debugging and cheat management

## Overview
This system provides a structured way to execute cheat commands in Unity, leveraging VitalRouter for message routing and Cysharp's UniTask for async operations. Key features include:

- **Generic Command Types:** Supports struct/class arguments via CheatCommand<T>, CheatCommandClass<T>, and multi-arg variants.
- **Thread-Safe Execution:** Uses ConcurrentDictionary to track command states and cancellation tokens.
- **VitalRouter Integration:** Routes commands to handlers via attributes ([Route]), enabling decoupled logic.

## Usage Example
1. Define Cheat Commands:
``` csharp
// No-arg command 
CheatCommandUtility.PublishCheatCommand("Protocol_CheatMessage_A").Forget();

// Struct argument (e.g., Vector3)
CheatCommandUtility.PublishCheatCommand("Protocol_GameDataMessage", new GameData(...)).Forget();

// Class argument (e.g., string)
CheatCommandUtility.PublishCheatCommand("Protocol_CustomStringMessage", "Hello").Forget();
```

2. Handle Commands (with VitalRouter):
``` csharp
[Route]
void OnMsg(CheatCommand cmd) 
{
    Debug.Log($"Received: {cmd.CommandID}");
}

[Route]
void OnReceiveMessage(CheatCommand<GameData> cmd) 
{
    Debug.Log($"Position: {cmd.Arg.position}"); 
}

``` 

## Key Technologies
- **VitalRouter:**
  - Attribute-based routing ([Route]) maps commands to handler methods.
  - Decouples publishers from subscribers via Router.Default.PublishAsync().
- **UniTask:** Ensures non-blocking async execution with cancellation support.
- **Aggressive Inlining:** Optimizes performance via MethodImplOptions.AggressiveInlining.