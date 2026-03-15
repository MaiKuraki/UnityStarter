# Device Feedback — 触觉反馈与灯光控制

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

Unity 多平台硬件反馈库。  
为 **手机触觉振动**（Android / iOS / WebGL）、**手柄马达震动** 和 **设备灯光控制** 提供统一 API，同时兼容依赖注入（DI）和静态调用两种模式。

无论你需要简单的一行振动调用，还是精确到采样级别的 Core Haptics 编著加实时参数调制，此库均通过相同接口跨平台提供，并自动回退到设备可用的最优硬件 API。

---

## 特性

- **iOS Core Haptics（iOS 13+）** — 完整 CHHapticEngine 集成：瞬态/持续事件、双参数曲线（强度 + 锐度）原生 OS 级插值、复合模式、实时参数调制
- **锐度维度** — 所有触觉 API 均接受 sharpness 参数（0.0 = 深沉/宽广 → 1.0 = 锋利/清脆）；iOS 13+ 原生支持，其他平台近似处理
- **HapticClip ScriptableObject** — 设计师友好的触觉模式资产：通过 Inspector 编辑双 AnimationCurve 或离散 `HapticEvent` 数组
- **跨平台手机振动** — Android（API 1+）、iOS（Taptic Engine / AudioToolbox / Core Haptics）、WebGL（`navigator.vibrate`）
- **Android API 30+ 硬件触觉基元** — 通过 `VibrationEffect.startComposition()` 使用设备调校的触觉基元（CLICK、TICK、LOW_TICK、THUD），由 OEM 针对每台设备精确校准，在任意强度下均能提供最优手感
- **Android 振幅控制** — API 26+ 支持 0–255 精确振幅（`VibrationEffect`）；旧设备自动降级
- **双 AnimationCurve 曲线振动** — 强度和锐度均可沿时间轴变化；iOS 13+ 使用原生 `CHHapticParameterCurve` 实现 OS 级平滑插值
- **iOS 原生触觉** — Impact（Light / Medium / Heavy / Rigid / Soft）、Notification（Success / Warning / Error）、Selection — 当 Core Haptics 可用时自动升级
- **通用触觉预设** — `HapticPreset` 枚举，按设备映射到原生 API 并附带合适的锐度值
- **零 GC 热路径** — 所有波形采样使用预分配静态缓冲区；缓存 JNI 数组；预缓存枚举名称字符串用于原生交互 — 无逐次调用堆分配
- **低延迟优化 iOS** — 预分配并预热的 `UIFeedbackGenerator` 实例；即发即忘的瞬态事件播放器，不阻塞持续触觉
- **手柄震动接口** — 双马达控制（占位接口，可扩展）
- **设备灯光控制** — 灯条颜色、渐变、亮度曲线，适用于 DualSense / DualShock（占位接口，可扩展）
- **DI 友好架构** — 面向接口编程（`IHapticFeedbackService`、`IMobileVibrationService` 等）

---

## 系统要求

- Unity **2019.3** 或更高版本
- **Android**：API 1+（振幅功能需要 API 26+）
- **iOS**：Taptic Engine（iPhone 7+）；Core Haptics（iPhone 8+ 且 iOS 13+）
- **WebGL**：支持 Vibration API 的浏览器

---

## 快速上手

### 静态门面（无 DI）

```csharp
using CycloneGames.DeviceFeedback.Runtime;

MobileVibration.Init();

// 预设触觉
MobileVibration.PlayPreset(HapticPreset.Success);

// 自定义振动：80% 强度，0.3 秒，锋利手感
MobileVibration.Play(0.8f, 0.3f, sharpness: 0.9f);

// 双曲线振动（强度 + 锐度随时间变化）
MobileVibration.PlayCurve(intensityCurve, 2.0f, sharpnessCurve);

// 播放 HapticClip 资产
MobileVibration.PlayClip(myHapticClip);

// 实时调制（iOS 13+ Core Haptics）
MobileVibration.Play(0.5f, 5.0f, 0.5f); // 开始持续触觉
// 在 Update() 中：
MobileVibration.UpdateContinuousParameters(newIntensity, newSharpness);

// 取消振动
MobileVibration.Cancel();
```

### 依赖注入

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

// 注册示例（VContainer / Zenject）：
// container.Register<IMobileVibrationService, MobileVibrationService>(Lifetime.Singleton);
```

---

## HapticClip — 设计师触觉模式资产

通过 **Assets > Create > CycloneGames > Device Feedback > Haptic Clip** 创建。

`HapticClip` 是一个 `ScriptableObject`，支持两种模式：

### 曲线模式（默认）

定义 `intensityCurve` 和 `sharpnessCurve`（X 轴：归一化时间 0–1，Y 轴：值 0–1）以及 `duration`。

### 事件模式

在 `events` 数组中填入离散的 `HapticEvent` 条目：

| 字段        | 说明                                           |
| ----------- | ---------------------------------------------- |
| `type`      | `Transient`（瞬态敲击）或 `Continuous`（持续） |
| `time`      | 相对于片段起始的时间（秒）                     |
| `duration`  | 持续时长（秒），仅 Continuous 使用             |
| `intensity` | 0.0–1.0                                        |
| `sharpness` | 0.0–1.0                                        |

当 `events` 非空时，曲线被忽略。事件在 iOS 13+ 上直接映射为 `CHHapticEvent`，实现采样精确的触觉播放。

---

## API 参考

### `IHapticFeedbackService` — 通用接口

| 成员                                                                                            | 说明                                                                              |
| ----------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| `bool IsAvailable`                                                                              | 硬件是否存在且已初始化                                                            |
| `bool IsActive { get; set; }`                                                                   | 主开关。设为 `false` 时所有调用变为空操作                                         |
| `Initialize()`                                                                                  | 检测硬件并获取原生引用                                                            |
| `PlayPreset(HapticPreset)`                                                                      | 播放标准预设（Light / Medium / Heavy / Success / Warning / Error / Selection）    |
| `Play(float intensity, float duration, float sharpness)`                                        | 自定义触觉：强度 0–1，时长（秒），锐度 0–1（默认 0.5）                            |
| `PlayCurve(AnimationCurve intensity, float duration, AnimationCurve sharpness, int intervalMs)` | 双曲线触觉。iOS 13+：原生 `CHHapticParameterCurve`；Android 26+：`createWaveform` |
| `PlayClip(HapticClip)`                                                                          | 播放 `HapticClip` ScriptableObject（事件或曲线）                                  |
| `Cancel()`                                                                                      | 立即停止当前振动                                                                  |

### `IMobileVibrationService` — 手机扩展

继承 `IHapticFeedbackService` + `IDisposable`。

| 成员                                                           | 说明                                                    |
| -------------------------------------------------------------- | ------------------------------------------------------- |
| `bool HasVibrator`                                             | 设备是否有振动马达                                      |
| `Vibrate()`                                                    | 默认振动                                                |
| `Vibrate(long ms)`                                             | 振动指定毫秒                                            |
| `Vibrate(long[] pattern, int repeat)`                          | 振动序列。`repeat = -1` 表示只播放一次                  |
| `VibratePop()`                                                 | 短促弹性反馈（iOS 13+ 使用 Core Haptics 瞬态事件）      |
| `VibratePeek()`                                                | 中等窥视反馈                                            |
| `VibrateNope()`                                                | 错误/拒绝反馈序列（Core Haptics 上为 3 个快速瞬态事件） |
| `VibrateIOS(IOSImpactStyle)`                                   | iOS Impact — Core Haptics 可用时映射为精确瞬态事件      |
| `VibrateIOS(IOSNotificationStyle)`                             | iOS Notification                                        |
| `VibrateIOSSelection()`                                        | iOS Selection 点击感                                    |
| `UpdateContinuousParameters(float intensity, float sharpness)` | 实时调制当前活跃的持续触觉（仅 iOS 13+ Core Haptics）   |

### `MobileVibration` — 静态门面

将 `IMobileVibrationService` 的所有方法以 `static` 形式暴露。

### `IGamepadRumbleService` — 手柄震动（占位）

| 成员                                            | 说明                          |
| ----------------------------------------------- | ----------------------------- |
| `SetMotorSpeeds(float low, float high)`         | 直接设置双马达转速（0.0–1.0） |
| `Rumble(float low, float high, float duration)` | 定时震动，到期自动停止        |

### `IDeviceLightService` — 设备灯光（占位）

| 成员                                                    | 说明             |
| ------------------------------------------------------- | ---------------- |
| `SetColor(Color)`                                       | 设置灯光为纯色   |
| `Flash(Color, Color, float, float)`                     | 在两种颜色间闪烁 |
| `PlayGradient(Gradient, float, int)`                    | 平滑颜色过渡     |
| `PlayIntensityCurve(Color, AnimationCurve, float, int)` | 脉动亮度         |
| `CancelAnimation()`                                     | 停止灯光动画     |
| `Reset()`                                               | 恢复默认         |

---

## 平台支持一览

| 功能                | Android              | iOS (< 13)   | iOS (13+)           | WebGL        | Editor |
| ------------------- | -------------------- | ------------ | ------------------- | ------------ | ------ |
| 基础振动            | ✅                   | ✅           | ✅                  | ✅           | ⬜     |
| 时长控制            | ✅                   | ⬜           | ✅（持续事件）      | ✅           | ⬜     |
| 振幅/强度           | ✅（API 26+）        | ⬜           | ✅（原生）          | ⬜           | ⬜     |
| 锐度                | ⬜                   | ⬜           | ✅（原生）          | ⬜           | ⬜     |
| 振动序列            | ✅                   | ⬜           | ✅（复合模式）      | ⬜           | ⬜     |
| AnimationCurve 波形 | ✅（API 26+）        | ⚠️（仅峰值） | ✅（原生曲线）      | ⚠️（总时长） | ⬜     |
| HapticClip 事件     | ✅（波形）           | ⬜           | ✅（CHHapticEvent） | ⚠️（总时长） | ⬜     |
| 触觉预设            | ✅                   | ✅（原生）   | ✅（Core Haptics）  | ⚠️（降级）   | ⬜     |
| API 30+ 触觉基元    | ✅ (CLICK/TICK/THUD) | —            | —                   | —            | ⬜     |
| 实时调制            | ⬜                   | ⬜           | ✅                  | ⬜           | ⬜     |
| 取消振动            | ✅                   | ⬜           | ✅                  | ✅           | ⬜     |

---

## iOS Core Haptics 架构

在 iOS 13+ 设备上，库自动检测 Core Haptics 支持并升级所有触觉调用：

- **瞬态事件** — `VibratePop`、`VibratePeek`、`VibrateIOS(Impact)`、`PlayPreset` 均使用 `CHHapticEventTypeHapticTransient` 配合精确的强度/锐度值
- **持续事件** — `Play(intensity, duration, sharpness)` 使用 `CHHapticEventTypeHapticContinuous` 实现精确控制的持续触觉
- **复合模式** — `VibrateNope`、`PlayClip(events)` 构建多事件 `CHHapticPattern` 一次性发送给引擎
- **参数曲线** — `PlayCurve(intensity, duration, sharpness)` 将采样点转换为 `CHHapticParameterCurve`，由 OS 处理控制点间的平滑插值
- **实时调制** — `UpdateContinuousParameters` 每帧向活跃的模式播放器发送 `CHHapticDynamicParameter` 更新

当 Core Haptics 不可用时，自动回退至 `UIFeedbackGenerator`（iOS 10+）。

---

## 低延迟与精确度架构

触觉反馈对延迟极其敏感 — 用户可感知低至 20ms 的延迟。本库采用了多种策略将延迟降到最低：

### iOS：预热 UIFeedbackGenerator

原生插件（`HapticFeedback.mm`）在初始化时预分配 **7 个静态生成器实例**（Light、Medium、Heavy、Rigid、Soft 冲击 + Notification + Selection）。每个生成器在触发前调用 `[prepare]` 以提前启动 Taptic Engine 硬件。每次触发后立即重新 prepare，确保下次调用同样低延迟。

这消除了原来“创建 + 准备 + 触发”在同一次调用中完成时产生的 **~30–50ms 冷启动延迟**。

### iOS：即发即忘瞬态播放器

Core Haptics 插件（`CoreHaptics.mm`）采用两种不同的播放器策略：

- **瞬态触觉**（`PlayTransient`）— 为每次点击创建**临时的即发即忘播放器**。播放器通过 `CHHapticTimeImmediate` 立即启动，完成后由引擎自动释放。关键是，它**不会停止**正在运行的持续播放器，避免了 **~10–30ms 阻塞延迟**。
- **持续/模式/曲线效果** — 使用**持久化的 `s_continuousPlayer`**，支持 `UpdateParameters` 实时调制。新的持续效果启动前会停止上一个。

这意味着快速连续点击和持续触觉可以共存，互不干扰。

### Android：API 30+ Composition 触觉基元

在 Android API 30+ 上，`PlayPreset()` 自动使用 `VibrationEffect.startComposition()` 配合**硬件调校触觉基元**（CLICK=1, TICK=7, LOW_TICK=8, THUD=3）。这些基元由 **OEM 针对每台设备校准**，提供比原始振幅波形明显更好的手感。如果设备不支持特定基元，会透明地回退到传统振幅路径。

### 平台回退链

```
iOS:     Core Haptics (13+) → UIFeedbackGenerator (10+) → AudioToolbox
Android: Composition API (30+) → VibrationEffect (26+) → legacy vibrate()
WebGL:   navigator.vibrate()
```

---

## 零 GC 设计

所有波形采样和事件编组使用**预分配静态缓冲区**，按需增长但不收缩：

```
s_timingBuf, s_amplitudeBuf, s_floatTimeBuf,
s_intensityBuf, s_sharpnessBuf, s_typeBuf, s_durationBuf
```

`EnsureBuffers(count)` 每次调用仅检查一次；后续等量或更小量的调用不产生任何分配。JNI 对象（`AndroidJavaClass`、`AndroidJavaObject`）在服务生命周期内缓存，通过 `IDisposable` 释放。原生交互所用的枚举名称字符串预缓存在静态 readonly 数组中，避免 `ToString()` 堆分配。

---

## 示例

通过 **Package Manager > Device Feedback > Samples > Mobile Vibration Example** 导入。

示例场景演示了：

- 所有 `HapticPreset` 预设值
- 自定义时长和振动序列
- **强度 + 锐度滑块** 配合 `Play()` 实时调节手感
- **双 AnimationCurve 曲线振动**（强度 + 锐度）
- **HapticClip 播放** — 从 ScriptableObject 资产播放
- **实时调制** — 启动持续触觉后通过滑块调节参数
- iOS 专用 Impact / Notification / Selection 反馈
- 启用/禁用开关

---
