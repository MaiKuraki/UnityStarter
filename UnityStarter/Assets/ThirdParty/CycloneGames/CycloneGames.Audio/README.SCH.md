# CycloneGames.Audio

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

一个为 Unity 打造的增强型音频管理系统。其核心逻辑源自微软的 `Audio-Manager-for-Unity`，由 CycloneGames 在其基础上进行了扩展，并专注于性能与内存效率的优化。

如果您的游戏**不**打算使用 **Wwise，CriWare，FMOD** 等成熟的中间件，此插件是作者比较推荐的，插件的逻辑与 **Wwise** 对音频的管理和编辑相似，包括了 **Bank, RTPC, Parameter, Multi-Bus** 等常用类 **Wwise** 的功能，更适合熟悉 **Wwise** 的开发者以及设计师使用。

**上游源码**: https://github.com/microsoft/Audio-Manager-for-Unity

此版本为生产环境引入了关键优化，包括性能监控、异步资源加载以及降低的 GC（垃圾回收）开销。

## 特性

- **集中式音频控制**: 通过统一的 API 管理音效和音乐。
- **性能监控**: 内置的钩子和工具，用于实时监控音频系统的性能。
- **异步加载**: 集成 `UniTask` 实现音频资源的非阻塞异步加载，确保流畅的游戏体验，避免卡顿。
- **GC 优化**: 减少运行时内存分配，以最小化垃圾回收峰值，这对于性能敏感的应用至关重要。

## 安装与依赖

- Unity: `2022.3`+
- 依赖:
  - `com.cysharp.unitask` ≥ `2.0.0`

通过 UPM 安装或将包放置在 `Packages`/`Assets` 目录下。详情请参阅此文件夹中的 `package.json`。

## 编辑器预览
-   <img src="./Documents~/Preview_01.png" alt="Preview 1" style="width: 100%; height: auto; max-width: 800px;" />
-   <img src="./Documents~/Preview_02.png" alt="Preview 2" style="width: 100%; height: auto; max-width: 800px;" />
-   <img src="./Documents~/Preview_03.png" alt="Preview 3" style="width: 100%; height: auto; max-width: 800px;" />

## 快速上手

### 1) 播放音效 (SFX)

```csharp
using CycloneGames.Audio;
using Cysharp.Threading.Tasks;

// 播放一次性音效
AudioSystem.PlayOneShot("SFX_Player_Jump");

// 播放一个音效并获取句柄以便后续控制
var audioHandle = AudioSystem.Play("SFX_Machine_Gun_Loop");

// 停止循环音效
if (audioHandle.IsValid())
{
    audioHandle.Stop();
}
```

### 2) 播放音乐

```csharp
using CycloneGames.Audio;

// 播放背景音乐，将自动循环
AudioSystem.PlayMusic("Music_Level_1");

// 在 2 秒内淡出音乐
AudioSystem.StopMusic(2.0f);
```

## CycloneGames 独有拓展

此实现对原始的微软音频管理器进行了显著的扩展。关键增强功能如下：

### 异步操作

所有资源密集型操作（例如加载 `AudioClip`）都使用 `UniTask` 异步执行。这可以防止主线程阻塞，对于消除游戏过程中因加载新声音而导致的帧率下降至关重要。

### GC 优化

我对音频系统进行了细致的性能分析和优化，以减少性能关键路径上的内存分配。这些更改带来了更低且更可预测的内存占用，从而降低了垃圾回收的频率和影响。

### 性能监控

系统内置了性能检测工具，可为 **AudioManager** 提供内存监控数据，使开发人员能够快速诊断与音频相关的性能问题。