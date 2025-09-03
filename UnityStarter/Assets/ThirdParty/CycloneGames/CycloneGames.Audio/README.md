# CycloneGames.Audio

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

An enhanced audio management system for Unity. The core logic is sourced from Microsoft's `Audio-Manager-for-Unity`, extended by CycloneGames with a strong focus on performance and memory efficiency. If you do not plan to use mature middleware such as **Wwise**, **CriWare**, or **FMOD**, this plugin is highly recommended. Its logic for managing and editing audio is similar to **Wwise**, including common Wwise-like features such as **Bank, RTPC, Parameter, and Multi-Bus**, making it more suitable for developers and designers familiar with **Wwise**.

**Upstream Source**: https://github.com/microsoft/Audio-Manager-for-Unity

This version introduces critical optimizations for production environments, including performance monitoring, asynchronous resource loading, and reduced GC (Garbage Collection) overhead.

## Features

- **Centralized Audio Control**: Manage sound effects and music from a unified API.
- **Performance Monitoring**: In-built hooks and utilities to monitor audio system performance in real-time.
- **Asynchronous Loading**: Integrates `UniTask` for non-blocking, asynchronous loading of audio assets, ensuring smooth gameplay without hitches.
- **GC Optimization**: Reduces runtime memory allocations to minimize garbage collection spikes, crucial for performance-sensitive applications.

## Installation & Dependencies

- Unity: `2022.3`+
- Dependencies:
  - `com.cysharp.unitask` ≥ `2.0.0`

Install via UPM or place the package under `Packages`/`Assets`. See `package.json` in this folder for details.

## Editor Preview
-   <img src="./Documents~/Preview_01.png" alt="Preview 1" style="width: 100%; height: auto; max-width: 800px;" />
-   <img src="./Documents~/Preview_02.png" alt="Preview 2" style="width: 100%; height: auto; max-width: 800px;" />
-   <img src="./Documents~/Preview_03.png" alt="Preview 3" style="width: 100%; height: auto; max-width: 800px;" />

## Quick Start

### 0) Creating AudioEvent Assets

Before you can play audio, you need to create AudioEvent assets in Unity:

1. Right-click in your Project window
2. Select **Create > Audio > Audio Event**
3. Configure your AudioEvent with AudioFile nodes and other audio components
4. Save the asset in your project

### 1) Playing a Sound Effect (SFX)

```csharp
using CycloneGames.Audio.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class AudioExample : MonoBehaviour
{
    [SerializeField] private AudioEvent jumpEvent; // Assign in Inspector
    [SerializeField] private AudioEvent machineGunEvent; // Assign in Inspector
    
    void Start()
    {
        // Play a one-shot sound effect
        AudioManager.PlayEvent(jumpEvent, gameObject);
        
        // Play a sound and get a handle to control it later
        var audioHandle = AudioManager.PlayEvent(machineGunEvent, gameObject);
        
        // Stop the looping sound after 5 seconds
        StartCoroutine(StopAfterDelay(audioHandle, 5f));
    }
    
    private System.Collections.IEnumerator StopAfterDelay(ActiveEvent audioHandle, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (audioHandle != null)
        {
            audioHandle.Stop();
        }
    }
}
```

### 2) Playing Music

```csharp
using CycloneGames.Audio.Runtime;
using UnityEngine;

public class MusicController : MonoBehaviour
{
    [SerializeField] private AudioEvent backgroundMusic; // Assign in Inspector
    
    void Start()
    {
        // Play background music
        var musicHandle = AudioManager.PlayEvent(backgroundMusic, gameObject);
    }
    
    public void StopMusic()
    {
        // Stop all instances of the background music
        AudioManager.StopAll(backgroundMusic);
    }
}
```

## API Reference

### AudioManager Static Methods

- `PlayEvent(AudioEvent eventToPlay, GameObject emitterObject)` - Play an AudioEvent on a GameObject
- `PlayEvent(AudioEvent eventToPlay, Vector3 position)` - Play an AudioEvent at a specific position
- `StopAll(AudioEvent eventsToStop)` - Stop all instances of a specific AudioEvent
- `StopAll(int groupNum)` - Stop all events in a specific group
- `ValidateManager()` - Ensure the AudioManager instance exists

### ActiveEvent Methods

- `Stop()` - Stop the event with fade out
- `StopImmediate()` - Stop the event immediately
- `SetMute(bool toggle)` - Mute/unmute the event
- `SetSolo(bool toggle)` - Solo/unsolo the event

## CycloneGames Extensions

This implementation significantly builds upon the original Microsoft audio manager. The key enhancements are:

### Asynchronous Operations

All resource-intensive operations, such as loading `AudioClip`s, are performed asynchronously using `UniTask`. This prevents the main thread from blocking, which is essential for eliminating frame rate drops when new sounds are introduced during gameplay.

### GC Optimizations

I have meticulously profiled and optimized the audio system to reduce memory allocations in performance-critical paths. These changes result in a much lower and more predictable memory footprint, reducing the frequency and impact of garbage collection.

### Performance Monitoring

The system is instrumented to provide memory monitoring data for the **AudioManager**, allowing developers to quickly diagnose audio-related performance issues.
