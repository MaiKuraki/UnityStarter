# CycloneGames.AIPerception

[English](./README.md) | 简体中文

AI 感知组件，提供视觉、听觉、近距离检测、刺激记忆、基于距离的更新频率和 Burst/Jobs 查询处理。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速上手](#快速上手)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [进阶主题](#进阶主题)
- [常见场景](#常见场景)
- [性能与内存](#性能与内存)
- [故障排查](#故障排查)

## 概述

AIPerception 回答一个问题：给定一组传感器和世界中已注册的可感知对象，当前 AI 智能体检测到了什么？三种传感器类型——Sight（锥体+LOS）、Hearing（球体+遮挡）和 Proximity（半径触发）——通过 Burst 编译的 Job 查询 3D 空间网格。结果保存在刺激记忆中并按配置衰减；基于距离的 LOD 降低远处智能体的更新频率。

适用场景：AI 智能体需要通过可配置感知能力感知周围环境，以及成百上千个智能体需要批量 Job 调度。

### 主要特性

- **三种传感器类型**：Sight（锥体 FOV+LOS raycast）、Hearing（球体+墙壁遮挡衰减）、Proximity（简单半径触发）。
- **Burst/Jobs** `IJobParallelFor` 预过滤 + SIMD 加速。
- **刺激记忆**，可配置持续时间与线性可见度衰减。
- **LOD 系统**，基于距离的更新频率缩放。
- **3D 空间网格**，O(k) 范围查询而非 O(n) 全量扫描。
- **可扩展类型系统**，基于整数的分类。
- **延迟模式**，`LateUpdate` 中批量完成 Job。

### 依赖

- `com.unity.collections` (2.1+)
- `com.unity.burst` (1.8+)
- `com.unity.mathematics` (1.3+)

## 架构

```
PerceptibleComponent ──注册──> PerceptibleRegistry（代际句柄）
                                      │
                                 RebuildData（每帧1次）
                                      │
                                SpatialGrid（3D 单元排序）
                                      │
AIPerceptionComponent ──创建──> SightSensor ──> SightConeQueryJob [Burst]
                         │     HearingSensor ──> SphereQueryJob   [Burst]
                         │     ProximitySensor ──> ProximityQueryJob [Burst]
                         │           │
                         │     ProcessJobResults
                         │           │
                         │     MergeMemory（刺激持久化）
                         │           │
                         └── 查询 API <── DetectionResult[]（实时 + 记忆）
```

### 关键类型

| 类型 | 作用 |
| --- | --- |
| `PerceptibleHandle` | 值类型句柄（Id + Generation）— 无 GC、无悬垂引用 |
| `PerceptibleData` | Blittable 结构体，Burst Job 输入 |
| `DetectionResult` | 传感器输出：目标、距离、位置、可见度、`IsFromMemory` |
| `StimulusMemoryEntry` | 目标离开范围后持久化；可见度线性衰减 |
| `SensorLODLevel` | 距离阈值 + 频率倍率 |

## 快速上手

**第 1 步：** 为可检测对象添加 `PerceptibleComponent`：

```csharp
var perceptible = gameObject.AddComponent<PerceptibleComponent>();
perceptible.SetTypeId(PerceptibleTypes.Enemy);
```

**第 2 步：** 为 AI 智能体添加 `AIPerceptionComponent`。在 Inspector 中配置：视觉传感器（锥体 FOV、最大距离、LOS）、听觉传感器（半径、遮挡）、邻近传感器（触发半径）。各传感器独立设置记忆时长。

**第 3 步：** 查询结果：

```csharp
using CycloneGames.AIPerception.Runtime;

public class AIBrain : MonoBehaviour
{
    private AIPerceptionComponent _perception;

    void Start() => _perception = GetComponent<AIPerceptionComponent>();

    void Update()
    {
        if (_perception.HasSightDetection)
        {
            var target = _perception.GetClosestSightTarget();
            Debug.Log($"看到: {((PerceptibleComponent)target).name}");
        }

        if (_perception.HasProximityDetection)
        {
            var nearest = _perception.GetClosestProximityTarget();
            Debug.Log($"附近有人，位置: {nearest.Position}");
        }
    }
}
```

## 核心概念

### PerceptibleComponent

使 GameObject 可被 AI 检测。`[DisallowMultipleComponent]`，实现 `IPerceptible`。

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| Type ID | `int` | 0 | 可感知类型分类 |
| Tag | `string` | "" | 可选过滤标签 |
| Detection Radius | `float` | 1 | 邻近触发大小 |
| Is Detectable | `bool` | true | 开关检测 |
| LOS Point | `Transform` | null | 视线检测原点（为空时使用 transform） |
| Is Sound Source | `bool` | false | 标记为音频发射器 |
| Loudness | `float` | 1 | 音量（0–10） |

```csharp
var handle = perceptible.Handle;    // 代际句柄
var pos = perceptible.Position;     // float3 世界位置
perceptible.SetLoudness(0.5f);      // 动态音量
```

### AIPerceptionComponent

AI 智能体的传感器宿主。`[DisallowMultipleComponent]`。

| 字段 | 类型 | 默认值 |
| --- | --- | --- |
| Enable Sight | `bool` | true |
| Sight Config | `SightSensorConfig` | 默认 |
| Enable Hearing | `bool` | false |
| Hearing Config | `HearingSensorConfig` | 默认 |
| Enable Proximity | `bool` | false |
| Proximity Config | `ProximitySensorConfig` | 默认 |

```csharp
bool hasSight = perception.HasSightDetection;
int sightCount = perception.SightDetectedCount;
IPerceptible target = perception.GetClosestSightTarget();
SightSensor sight = perception.SightSensor;
var result = sight.GetResult(0);
```

### PerceptionManagerComponent

全局系统驱动。自动创建为 `[PerceptionManager]`，位于 `DontDestroyOnLoad`。

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| Deferred Job Completion | `bool` | false | LateUpdate 批量完成 Job |
| LOD Reference | `Transform` | null | 距离参考点（摄像机/玩家） |
| LOD Levels | `SensorLODLevel[]` | 3 级 | 距离 → 频率倍率 |

```csharp
PerceptionManagerComponent.Instance.UseDeferredJobCompletion = true;
```

## 使用指南

### 视觉传感器 (Sight)

锥形视觉检测，Burst 预过滤 + 主线程 LOS 射线。

| 属性 | 范围 | 默认值 | 说明 |
| --- | --- | --- | --- |
| Half Angle | 0–180 度 | 60 | 视野半角 |
| Max Distance | 0–200 m | 30 | 检测距离 |
| Update Interval | 0–5 s | 0.1 | 更新间隔 |
| Obstacle Layer | LayerMask | 默认 | 阻挡视线的层 |
| Use Line of Sight | bool | true | 射线可见性检查 |
| Filter by Type | bool | false | 仅检测特定类型 |
| Memory Duration | 0–60 s | 3 | 离开视野后的记忆时长 |

### 听觉传感器 (Hearing)

球形声音检测，带墙壁遮挡衰减。

| 属性 | 范围 | 默认值 | 说明 |
| --- | --- | --- | --- |
| Radius | 0–100 m | 15 | 检测球半径 |
| Update Interval | 0–5 s | 0.2 | 更新间隔 |
| Use Occlusion | bool | true | 墙壁衰减 |
| Occlusion Layer | LayerMask | 默认 | 阻隔声音的层 |
| Occlusion Attenuation | 0–1 | 0.5 | 穿墙音量衰减 |
| Memory Duration | 0–60 s | 5 | 声音消失后的记忆时长 |

### 邻近传感器 (Proximity)

简单球体触发检测 — 无 LOS、无遮挡。

| 属性 | 范围 | 默认值 | 说明 |
| --- | --- | --- | --- |
| Radius | 0–50 m | 5 | 触发球半径 |
| Update Interval | 0–5 s | 0.15 | 更新间隔 |
| Memory Duration | 0–60 s | 2 | 离开范围后的记忆时长 |

### 刺激记忆

目标离开传感器范围后不会立即被遗忘。条目保留且可见度线性衰减。

```
检测 → MemoryEntry（PeakVisibility, LastDetectedTime）
         │
         ├── 重新检测到 → 刷新时间，更新峰值
         │
         └── 未检测到 → age 增加
                 │
                 ├── age < MemoryDuration → 作为 IsFromMemory 输出
                 └── age >= MemoryDuration → RemoveAtSwapBack
```

```csharp
sightConfig.MemoryDuration = 3f;    // 记住看到的 3 秒
hearingConfig.MemoryDuration = 5f;  // 记住听到的 5 秒

for (int i = 0; i < perception.SightSensor.DetectedCount; i++)
{
    var r = perception.SightSensor.GetResult(i);
    if (r.IsFromMemory)
        Debug.Log($"记忆中的目标: {r.LastKnownPosition}，可见度: {r.Visibility:F2}");
}
```

长记忆时长（5–10s）创造追猎型 AI；短时长（1–2s）创造反应型 AI。

### 类型系统

基于整数的可扩展分类：

| ID | 常量 | 说明 |
| --- | --- | --- |
| 0 | `PerceptibleTypes.Default` | 未指定 |
| 1 | `PerceptibleTypes.Player` | 玩家角色 |
| 2 | `PerceptibleTypes.Enemy` | 敌方 NPC |
| 3 | `PerceptibleTypes.Ally` | 友方 NPC |
| 4 | `PerceptibleTypes.Neutral` | 中立实体 |
| 5 | `PerceptibleTypes.Interactable` | 可交互对象 |
| 6 | `PerceptibleTypes.SoundSource` | 音频发射器 |

```csharp
public static class MyTypes
{
    public static int Treasure;
    public static int Trap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Treasure = PerceptibleTypes.RegisterType("Treasure");
        Trap = PerceptibleTypes.RegisterType("Trap");
    }
}

sightConfig.FilterByType = true;
sightConfig.TargetTypeId = PerceptibleTypes.Enemy;
```

## 进阶主题

### 空间分区

所有传感器使用 3D 均匀网格（默认 20m 单元）。`RebuildData` 按单元键排序 `PerceptibleData[]`；查询遍历重叠单元并拷贝连续片段。网格为全 3D（X/Y/Z），正确处理多层建筑和飞行单位。

### Job 调度模式

**立即模式**（默认）：Job 在 `Update()` 内完成，结果立即可用。

**延迟模式**：Job 在 `LateUpdate()` 中批量完成。

```
Update():     调度 Job → 显示上一帧结果
LateUpdate(): 完成全部 → 原子交换 → 显示新结果
```

```csharp
PerceptionManagerComponent.Instance.UseDeferredJobCompletion = true;
```

### LOD 系统

```
传感器位置 → 到 LOD Reference 的距离 → LOD 倍率 → 实际更新间隔

默认等级：
  0–30m:   1.00x（全频率）
  30–80m:  0.50x（半频率）
  80–200m: 0.10x（最低频率）
```

```csharp
SensorManager.Instance.ConfigureLOD(
    Camera.main.transform,
    new[] {
        new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1.0f },
        new SensorLODLevel { Distance = 100f, FrequencyMultiplier = 0.25f },
    }
);
```

### 扩展系统

```csharp
// 自定义 perceptible
public class WeaponPerceptible : PerceptibleComponent
{
    [SerializeField] private int _dangerLevel;
    public int DangerLevel => _dangerLevel;
}

// 自定义 AI perception
public class AdvancedPerception : AIPerceptionComponent
{
    [SerializeField] private float _alertLevel;

    protected override void Update()
    {
        base.Update();
        _alertLevel = HasSightDetection
            ? Mathf.Min(_alertLevel + Time.deltaTime, 1f)
            : Mathf.Max(_alertLevel - Time.deltaTime * 0.5f, 0f);
    }
}
```

自定义传感器：实现 `ISensor` 和 `IDisposable`，通过 `SensorManager.Instance.Register()` 注册。

### 编辑器工具

- `Tools > CycloneGames > AI Perception` — 全局调试覆盖层开关和 Gizmo 命令。
- `AIPerceptionComponent`、`PerceptibleComponent` 和 `PerceptionManagerComponent` 的自定义 Inspector。
- 运行时调试覆盖层：游戏内 GUI 显示实时检测和记忆条目，实时标记（`>`、`~`、`*`）和记忆标记（`<`、`~M`、`.M` 带 `[mem]` 后缀）。
- "Always Show Gizmos" — 无需逐个选中即可显示所有 AI 的传感器线框。

## 常见场景

### 带视觉和听觉的守卫

```csharp
perception.EnableSight = true;
perception.EnableHearing = true;
perception.SightConfig.MaxDistance = 20f;
perception.SightConfig.MemoryDuration = 3f;
perception.HearingConfig.Radius = 10f;
perception.HearingConfig.MemoryDuration = 5f;

void Update()
{
    if (HasSightDetection)
        Chase(GetClosestSightTarget());
    else if (perception.SightSensor.MemoryCount > 0)
        Investigate(perception.SightSensor.GetResult(0).LastKnownPosition);
}
```

### 近距离警戒系统

```csharp
perception.EnableProximity = true;
perception.ProximityConfig.Radius = 3f;
perception.ProximityConfig.MemoryDuration = 0f; // 不需要记忆

void Update()
{
    if (HasProximityDetection)
        OnNearbyThreat(GetClosestProximityTarget());
}
```

### 类型过滤敌人检测

```csharp
perception.SightConfig.FilterByType = true;
perception.SightConfig.TargetTypeId = PerceptibleTypes.Enemy;
perception.SightConfig.UseLineOfSight = true;
```

## 性能与内存

### 调优清单

1. **更新间隔**：先依据 Gameplay 响应要求选择，再测量。
2. **类型过滤**：只扫描相关类型。
3. **禁用 LOS**：无墙体场景跳过射线。
4. **启用 LOD**：允许结果短暂过期时降低远处传感器频率。
5. **延迟模式**：比较批量完成与立即结果可见两种时序。
6. **Memory Duration = 0**：不需要记忆的传感器关闭记忆。

### 容量

```csharp
PerceptibleRegistry.Instance.SetMaxCapacity(32768); // 0 = 无限增长
```

Registry 容量是内存与失败策略配置。有限上限耗尽后拒绝注册；0 允许继续增长但需产品监控内存。

### PerceptibleRegistry API

```csharp
var r = PerceptibleRegistry.Instance;
PerceptibleHandle h = r.Register(perceptible);  // O(1)
r.Unregister(h);                                  // O(1)
IPerceptible p = r.Get(h);                       // O(1)
bool valid = r.IsValid(h);                       // O(1)
r.SetMaxCapacity(16384);
```

### SensorManager API

```csharp
var m = SensorManager.Instance;
m.Register(sensor);
m.Unregister(sensor);
m.ConfigureLOD(referenceTransform, lodLevels);
```

### 平台

Runtime assembly 使用 Unity Burst、Collections、Mathematics 和 Jobs。每个目标平台都必须通过 Player build 验证包支持、worker 可用性、Burst 编译和内存限制。不能依 Editor 执行推断 WebGL worker 行为或主机平台 Burst 支持。

## 故障排查

| 现象 | 可能原因 | 解决方法 |
| --- | --- | --- |
| 检测不工作 | Perceptible 未启用或传感器关闭 | 检查 `Is Detectable` 和传感器开关；验证范围、FOV、LOS |
| 目标可见但 "LOS Blocked" | Obstacle layer 包含目标所在层 | 将 LayerMask 只设置为环境层（墙壁、地板） |
| 记忆条目未出现 | `MemoryDuration` 为 0 | 设置传感器配置中 `MemoryDuration > 0` |
| Inspector 标签显示空白 | Editor 字体问题 | 使用默认 Editor 字体 |
| 注册表容量耗尽 | 达到最大容量 | 启动时调用 `SetMaxCapacity(32768)` 或设为 0 |
| 大量智能体导致高 CPU | 未配置 LOD | 在 PerceptionManager 上配置距离阈值 LOD |
| Burst Job 编译失败 | 缺少 Burst/Collections/Mathematics | 安装所需包并确认 Burst 已启用 |

## 验证

```text
CycloneGames.AIPerception.Tests.Editor            (EditMode)
CycloneGames.AIPerception.Networking.Tests.Editor (EditMode)
```

确认无编译错误，测试 Play Mode，并在每个目标平台上运行 Player build。验证 Burst 编译、内存限制和 IL2CPP/AOT 兼容性。
