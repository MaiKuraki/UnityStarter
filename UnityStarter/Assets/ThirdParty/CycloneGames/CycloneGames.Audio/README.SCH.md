# CycloneGames.Audio

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

一个为 Unity 打造的增强型音频管理系统。其核心逻辑源自微软的 `Audio-Manager-for-Unity`，由 CycloneGames 在其基础上进行了扩展，并专注于性能与内存效率的优化。

如果您的游戏**不**打算使用 **Wwise，CriWare，FMOD** 等成熟的中间件，此插件是作者比较推荐的，插件的逻辑与 **Wwise** 对音频的管理和编辑相似，包括了 **Bank, RTPC, Parameter, Multi-Bus** 等常用类 **Wwise** 的功能，更适合熟悉 **Wwise** 的开发者以及设计师使用。

**上游源码**: https://github.com/microsoft/Audio-Manager-for-Unity

此版本为生产环境引入了关键优化，包括性能监控、异步资源加载以及降低的 GC（垃圾回收）开销。

## 特性

- **AudioGraph 重绘**: 更合理好看的 AudioGraph 编辑界面，类虚幻引擎的快捷键 (Alt+鼠标点击操作连接线)。
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

### 0) 创建 AudioEvent 资源

在播放音频之前，您需要在 Unity 中创建 AudioEvent 资源：

1. 在项目窗口中右键点击
2. 选择 **Create > CycloneGames > Audio > Audio Bank**
3. 使用 AudioFile 节点和其他音频组件配置您的 AudioEvent 内部逻辑

### 1) 播放音效 (SFX)

```csharp
using CycloneGames.Audio.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class AudioExample : MonoBehaviour
{
    [SerializeField] private AudioEvent jumpEvent; // 在检查器中分配
    [SerializeField] private AudioEvent machineGunEvent; // 在检查器中分配
    
    void Start()
    {
        // 播放一次性音效
        AudioManager.PlayEvent(jumpEvent, gameObject);
        
        // 播放一个音效并获取句柄以便后续控制
        var audioHandle = AudioManager.PlayEvent(machineGunEvent, gameObject);
        
        // 5秒后停止循环音效
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

### 2) 播放音乐

```csharp
using CycloneGames.Audio.Runtime;
using UnityEngine;

public class MusicController : MonoBehaviour
{
    [SerializeField] private AudioEvent backgroundMusic; // 在检查器中分配
    
    void Start()
    {
        // 播放背景音乐
        var musicHandle = AudioManager.PlayEvent(backgroundMusic, gameObject);
    }
    
    public void StopMusic()
    {
        // 停止所有背景音乐实例
        AudioManager.StopAll(backgroundMusic);
    }
}
```

## API 参考

### AudioManager 静态方法

- `PlayEvent(AudioEvent eventToPlay, GameObject emitterObject)` - 在 GameObject 上播放 AudioEvent
- `PlayEvent(AudioEvent eventToPlay, Vector3 position)` - 在特定位置播放 AudioEvent
- `StopAll(AudioEvent eventsToStop)` - 停止特定 AudioEvent 的所有实例
- `StopAll(int groupNum)` - 停止特定组中的所有事件
- `ValidateManager()` - 确保 AudioManager 实例存在

### ActiveEvent 方法

- `Stop()` - 带淡出效果停止事件
- `StopImmediate()` - 立即停止事件
- `SetMute(bool toggle)` - 静音/取消静音事件
- `SetSolo(bool toggle)` - 独奏/取消独奏事件

## CycloneGames 独有拓展

此实现对原始的微软音频管理器进行了显著的扩展。关键增强功能如下：

### 重绘 AudioGraph，类 UnrealEngine 的快捷键添加

重绘了 AudioGraph, 增强了 Node 连接曲线的绘制，增加了类似虚幻引擎的快捷键 Alt + 鼠标左键，删除单一曲线或删除当前选中节点上的所有曲线。

### 异步操作

所有资源密集型操作（例如加载 `AudioClip`）都使用 `UniTask` 异步执行。

### GC 优化

我对音频系统进行了细致的性能分析和优化，以减少性能关键路径上的内存分配，针对不同运行平台，修改了默认的内存池大小。这些更改带来了更低且更可预测的内存占用，从而降低了垃圾回收的频率和影响。

### 性能监控

系统内置了性能检测工具，可为 **AudioManager** 提供内存监控数据，使开发人员能够快速诊断与音频相关的性能问题。