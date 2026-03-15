# Device Feedback — Haptics & Lighting

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

Multi-platform hardware feedback library for Unity.  
Provides a unified API for **mobile haptics** (Android / iOS / WebGL), **gamepad rumble**, and **device light control** — compatible with both Dependency Injection and static access patterns.

Whether you need a simple one-line vibration call or pixel-perfect Core Haptics authoring with real-time modulation, this library provides the same interface across all platforms with automatic fallback to the best available hardware API.

---

## Features

- **iOS Core Haptics (iOS 13+)** — Full CHHapticEngine integration: transient/continuous events, dual-parameter curves (intensity + sharpness) with native OS-level interpolation, composite patterns, real-time parameter modulation
- **Sharpness dimension** — All haptic APIs accept a sharpness parameter (0.0 = deep/broad → 1.0 = sharp/crisp); native on iOS 13+, approximated on other platforms
- **HapticClip ScriptableObject** — Designer-friendly asset for authoring haptic patterns in the Inspector: dual AnimationCurves or discrete `HapticEvent` arrays
- **Cross-platform mobile vibration** — Android (API 1+), iOS (Taptic Engine / AudioToolbox / Core Haptics), WebGL (`navigator.vibrate`)
- **Android API 30+ Haptic Primitives** — Device-tuned haptic primitives (CLICK, TICK, LOW_TICK, THUD) via `VibrationEffect.startComposition()`, calibrated per-device by the OEM for optimal feel at any intensity scale
- **Android amplitude control** — Full 0–255 amplitude on API 26+ via `VibrationEffect`; graceful fallback on older devices
- **Dual AnimationCurve vibration** — Intensity and sharpness both vary over time via Unity `AnimationCurve`; iOS 13+ uses native `CHHapticParameterCurve` for smooth OS-level interpolation
- **iOS Haptic Feedback** — Impact (Light / Medium / Heavy / Rigid / Soft), Notification (Success / Warning / Error), Selection — all upgraded to Core Haptics when available
- **Universal haptic presets** — `HapticPreset` enum mapped to native APIs per device with appropriate sharpness values
- **Zero-GC hot path** — Pre-allocated static buffers for all waveform sampling; cached JNI arrays; pre-cached enum name strings for native interop — no per-call heap allocations
- **Latency-optimized iOS** — Pre-allocated and pre-warmed `UIFeedbackGenerator` instances; fire-and-forget ephemeral transient players that never block the continuous haptic
- **Gamepad rumble interface** — Dual-motor control for controllers (placeholder, extensible)
- **Device light control** — Light bar color, gradient, and intensity curve for DualSense / DualShock (placeholder, extensible)
- **DI-friendly architecture** — Program against interfaces (`IHapticFeedbackService`, `IMobileVibrationService`, etc.)

---

## Requirements

- Unity **2019.3** or later
- **Android**: API level 1+ (amplitude features require API 26+)
- **iOS**: Taptic Engine (iPhone 7+); Core Haptics (iPhone 8+ with iOS 13+)
- **WebGL**: Browser with Vibration API support

---

## Quick Start

### Static facade (no DI)

```csharp
using CycloneGames.DeviceFeedback.Runtime;

MobileVibration.Init();

// Preset haptics
MobileVibration.PlayPreset(HapticPreset.Success);

// Custom vibration: 80% intensity, 0.3s, sharp feel
MobileVibration.Play(0.8f, 0.3f, sharpness: 0.9f);

// Dual-curve vibration (intensity + sharpness over time)
MobileVibration.PlayCurve(intensityCurve, 2.0f, sharpnessCurve);

// Play a HapticClip asset
MobileVibration.PlayClip(myHapticClip);

// Real-time modulation (iOS 13+ Core Haptics)
MobileVibration.Play(0.5f, 5.0f, 0.5f); // start continuous
// In Update():
MobileVibration.UpdateContinuousParameters(newIntensity, newSharpness);

// Cancel
MobileVibration.Cancel();
```

### Dependency Injection

```csharp
using CycloneGames.DeviceFeedback.Runtime;

public class GameManager
{
    private readonly IHapticFeedbackService _haptics;

    public GameManager(IHapticFeedbackService haptics)
    {
        _haptics = haptics;
        _haptics.Initialize();
    }

    public void OnPlayerHit()  => _haptics.PlayPreset(HapticPreset.Heavy);
    public void OnCollect()    => _haptics.Play(0.3f, 0.1f, sharpness: 0.8f);
    public void OnExplosion()  => _haptics.PlayClip(explosionClip);
}

// Registration (VContainer / Zenject):
// container.Register<IMobileVibrationService, MobileVibrationService>(Lifetime.Singleton);
```

---

## HapticClip — Designer-Authored Haptic Patterns

Create via **Assets > Create > CycloneGames > Device Feedback > Haptic Clip**.

A `HapticClip` is a `ScriptableObject` with two modes:

### Curve Mode (default)

Define `intensityCurve` and `sharpnessCurve` (X: normalized time 0–1, Y: value 0–1) plus a `duration`.

### Event Mode

Populate the `events` array with discrete `HapticEvent` entries:

| Field       | Description                                         |
| ----------- | --------------------------------------------------- |
| `type`      | `Transient` (sharp tap) or `Continuous` (sustained) |
| `time`      | Start time in seconds from clip beginning           |
| `duration`  | Duration in seconds (only for Continuous)           |
| `intensity` | 0.0–1.0                                             |
| `sharpness` | 0.0–1.0                                             |

When `events` is non-empty, curves are ignored. Events map directly to `CHHapticEvent` on iOS 13+ for sample-accurate playback.

---

## API Reference

### `IHapticFeedbackService` — Common Interface

| Member                                                                                          | Description                                                                                          |
| ----------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `bool IsAvailable`                                                                              | Whether hardware is present and initialized                                                          |
| `bool IsActive { get; set; }`                                                                   | Master switch. `false` = all calls become no-ops                                                     |
| `Initialize()`                                                                                  | Detect hardware and acquire native references                                                        |
| `PlayPreset(HapticPreset)`                                                                      | Play a standardized preset (Light / Medium / Heavy / Success / Warning / Error / Selection)          |
| `Play(float intensity, float duration, float sharpness)`                                        | Custom haptic: intensity 0–1, duration in seconds, sharpness 0–1 (default 0.5)                       |
| `PlayCurve(AnimationCurve intensity, float duration, AnimationCurve sharpness, int intervalMs)` | Dual-curve haptic over time. iOS 13+: native `CHHapticParameterCurve`; Android 26+: `createWaveform` |
| `PlayClip(HapticClip)`                                                                          | Play a `HapticClip` ScriptableObject (events or curves)                                              |
| `Cancel()`                                                                                      | Stop active vibration immediately                                                                    |

### `IMobileVibrationService` — Mobile Extensions

Extends `IHapticFeedbackService` + `IDisposable`.

| Member                                                         | Description                                                                      |
| -------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| `bool HasVibrator`                                             | Device has a vibration motor                                                     |
| `Vibrate()`                                                    | Default vibration                                                                |
| `Vibrate(long ms)`                                             | Vibrate for specified milliseconds                                               |
| `Vibrate(long[] pattern, int repeat)`                          | Vibration pattern. `repeat = -1` for one-shot                                    |
| `VibratePop()`                                                 | Short pop feedback (Core Haptics transient on iOS 13+)                           |
| `VibratePeek()`                                                | Medium peek feedback                                                             |
| `VibrateNope()`                                                | Error/denial pattern (3 rapid transients on Core Haptics)                        |
| `VibrateIOS(IOSImpactStyle)`                                   | iOS impact — mapped to Core Haptics transients when available                    |
| `VibrateIOS(IOSNotificationStyle)`                             | iOS notification                                                                 |
| `VibrateIOSSelection()`                                        | iOS selection tick                                                               |
| `UpdateContinuousParameters(float intensity, float sharpness)` | Real-time modulation on the active continuous haptic (iOS 13+ Core Haptics only) |

### `MobileVibration` — Static Facade

All methods from `IMobileVibrationService` exposed as `static` methods.

### `IGamepadRumbleService` — Gamepad (Placeholder)

| Member                                          | Description                              |
| ----------------------------------------------- | ---------------------------------------- |
| `SetMotorSpeeds(float low, float high)`         | Set dual-motor speeds directly (0.0–1.0) |
| `Rumble(float low, float high, float duration)` | Timed rumble with auto-stop              |

### `IDeviceLightService` — Device Light (Placeholder)

| Member                                                  | Description                |
| ------------------------------------------------------- | -------------------------- |
| `SetColor(Color)`                                       | Set light to a solid color |
| `Flash(Color, Color, float, float)`                     | Flash between two colors   |
| `PlayGradient(Gradient, float, int)`                    | Smooth color transition    |
| `PlayIntensityCurve(Color, AnimationCurve, float, int)` | Pulsing brightness         |
| `CancelAnimation()`                                     | Stop light animation       |
| `Reset()`                                               | Restore default            |

---

## Platform Support Matrix

| Feature                 | Android              | iOS (< 13)     | iOS (13+)          | WebGL         | Editor |
| ----------------------- | -------------------- | -------------- | ------------------ | ------------- | ------ |
| Basic vibration         | ✅                   | ✅             | ✅                 | ✅            | ⬜     |
| Duration control        | ✅                   | ⬜             | ✅ (continuous)    | ✅            | ⬜     |
| Amplitude/Intensity     | ✅ (API 26+)         | ⬜             | ✅ (native)        | ⬜            | ⬜     |
| Sharpness               | ⬜                   | ⬜             | ✅ (native)        | ⬜            | ⬜     |
| Vibration pattern       | ✅                   | ⬜             | ✅ (composite)     | ⬜            | ⬜     |
| AnimationCurve waveform | ✅ (API 26+)         | ⚠️ (peak only) | ✅ (native curves) | ⚠️ (total ms) | ⬜     |
| HapticClip events       | ✅ (waveform)        | ⬜             | ✅ (CHHapticEvent) | ⚠️ (total ms) | ⬜     |
| Haptic presets          | ✅                   | ✅ (native)    | ✅ (Core Haptics)  | ⚠️ (fallback) | ⬜     |
| API 30+ primitives      | ✅ (CLICK/TICK/THUD) | —              | —                  | —             | ⬜     |
| Real-time modulation    | ⬜                   | ⬜             | ✅                 | ⬜            | ⬜     |
| Cancel                  | ✅                   | ⬜             | ✅                 | ✅            | ⬜     |

---

## iOS Core Haptics Architecture

On iOS 13+ devices, the library automatically detects Core Haptics support and upgrades all haptic calls:

- **Transient events** — `VibratePop`, `VibratePeek`, `VibrateIOS(Impact)`, `PlayPreset` all use `CHHapticEventTypeHapticTransient` with calibrated intensity/sharpness pairs
- **Continuous events** — `Play(intensity, duration, sharpness)` uses `CHHapticEventTypeHapticContinuous` for sustained haptics with precise control
- **Composite patterns** — `VibrateNope`, `PlayClip(events)` build multi-event `CHHapticPattern` sent as a single batch to the engine
- **Parameter curves** — `PlayCurve(intensity, duration, sharpness)` converts sampled points to `CHHapticParameterCurve`, letting the OS handle smooth interpolation between control points
- **Real-time modulation** — `UpdateContinuousParameters` sends `CHHapticDynamicParameter` updates to the active pattern player each frame

Fallback to `UIFeedbackGenerator` (iOS 10+) is automatic when Core Haptics is unavailable.

---

## Latency & Precision Architecture

Every millisecond matters for haptic feedback — users perceive delays as low as 20ms. This library employs several strategies to minimize latency:

### iOS: Pre-Warmed UIFeedbackGenerators

The native plugin (`HapticFeedback.mm`) pre-allocates **7 static generator instances** (Light, Medium, Heavy, Rigid, Soft impact + Notification + Selection) at initialization time. Each generator calls `[prepare]` to spin up the Taptic Engine hardware **before** any feedback is triggered. After each trigger, the generator immediately re-prepares for the next call.

This eliminates the **~30–50ms cold-start latency** that occurs when a generator is created and triggered in the same call.

### iOS: Fire-and-Forget Transient Players

The Core Haptics plugin (`CoreHaptics.mm`) uses two distinct player strategies:

- **Transient haptics** (`PlayTransient`) — Creates an **ephemeral fire-and-forget player** for each tap. The player starts immediately via `CHHapticTimeImmediate` and is auto-released by the engine after completion. Crucially, it does **not** stop the continuous player, avoiding **~10–30ms blocking delay**.
- **Continuous/Pattern/Curve effects** — Uses a **persistent `s_continuousPlayer`** that supports `UpdateParameters` for real-time modulation. Previous continuous player is stopped before starting a new one.

This means rapid taps and ongoing sustained haptics can coexist without latency interference.

### Android: API 30+ Composition Primitives

On Android API 30+, `PlayPreset()` automatically uses `VibrationEffect.startComposition()` with **hardware-tuned primitives** (CLICK=1, TICK=7, LOW_TICK=8, THUD=3). These primitives are calibrated **per-device by the OEM** and provide significantly better feel than raw amplitude waveforms. If the device doesn't support a specific primitive, the library falls back to the legacy amplitude path transparently.

### Platform Fallback Chain

```
iOS:     Core Haptics (13+) → UIFeedbackGenerator (10+) → AudioToolbox
Android: Composition API (30+) → VibrationEffect (26+) → legacy vibrate()
WebGL:   navigator.vibrate()
```

---

## Zero-GC Design

All waveform sampling and event marshaling uses **pre-allocated static buffers** that grow on demand but never shrink:

```
s_timingBuf, s_amplitudeBuf, s_floatTimeBuf,
s_intensityBuf, s_sharpnessBuf, s_typeBuf, s_durationBuf
```

`EnsureBuffers(count)` checks once per call; no allocation occurs on subsequent calls with equal or smaller counts. JNI objects (`AndroidJavaClass`, `AndroidJavaObject`) are cached for the service lifetime and disposed via `IDisposable`. Enum name strings used for native interop marshaling are pre-cached in static readonly arrays, avoiding `ToString()` heap allocations.

---

## Samples

Import via **Package Manager > Device Feedback > Samples > Mobile Vibration Example**.

The sample demonstrates:

- All `HapticPreset` values
- Custom duration and pattern vibration
- **Intensity + Sharpness sliders** with `Play()` for real-time feel adjustment
- **Dual AnimationCurve vibration** (intensity + sharpness)
- **HapticClip playback** from a ScriptableObject asset
- **Real-time modulation** — start a continuous haptic and modulate via sliders
- iOS-specific Impact / Notification / Selection feedback
- Enable/disable toggle

---
