# CycloneGames.AIPerception

[English](./README.md) | 简体中文

产品级 AI 感知系统，集成 Burst/Jobs 优化、零 GC 设计、3D 空间分区、刺激记忆、LOD 和跨平台支持。

---

## 目录

1. [特性](#特性)
2. [安装](#安装)
3. [快速开始](#快速开始)
4. [核心概念](#核心概念)
5. [组件参考](#组件参考)
6. [传感器参考](#传感器参考)
    - [视觉传感器 (Sight)](#视觉传感器-sight)
    - [听觉传感器 (Hearing)](#听觉传感器-hearing)
    - [邻近传感器 (Proximity)](#邻近传感器-proximity)
7. [刺激记忆 (Stimulus Memory)](#刺激记忆-stimulus-memory)
8. [LOD 系统](#lod-系统)
9. [空间分区](#空间分区)
10. [类型系统](#类型系统)
11. [Job 调度模式](#job-调度模式)
12. [编辑器工具](#编辑器工具)
13. [运行时调试工具](#运行时调试工具)
14. [扩展系统](#扩展系统)
15. [性能与规模](#性能与规模)
16. [平台支持](#平台支持)
17. [API 参考](#api-参考)
18. [故障排除](#故障排除)

---

## 特性

| 分类 | 能力 |
|------|------|
| **传感器** | Sight（锥体 + LOS）、Hearing（球体 + 遮挡）、Proximity（半径触发） |
| **零 GC 运行时** | `NativeList`/`NativeArray` + 代际句柄 — 运行时零堆分配 |
| **Burst/Jobs** | `IJobParallelFor` 预过滤 + SIMD 加速 |
| **刺激记忆** | 目标离开传感器范围后仍被记住，可见度线性衰减 |
| **LOD** | 基于距离的更新频率缩放，3 级预设 |
| **空间索引** | 3D 网格空间分区，O(k) 范围查询 |
| **延迟模式** | `LateUpdate` 批量 Job 完成，适用于 100+ 传感器场景 |
| **编辑器工具** | 自定义 Inspector、全局 Gizmo 开关、LOD 预览、运行时统计 |
| **调试覆盖层** | 游戏内 GUI 窗口，显示实时检测和记忆条目 |
| **类型系统** | 基于整数的可扩展分类系统 |
| **跨平台** | WebGL 降级、移动端优化、主机就绪 |
| **容量控制** | 可配置注册表容量，自动扩容 + 阈值告警 |

---

## 安装

1. 将 `CycloneGames.AIPerception` 复制到项目的 `Assets` 目录。
2. 确保已安装以下 Unity 包：
   - `com.unity.collections` (2.1+)
   - `com.unity.burst` (1.8+)
   - `com.unity.mathematics` (1.3+)

---

## 快速开始

### 第一步：标记可检测对象

为任何需要被 AI 检测的 GameObject 添加 `PerceptibleComponent`：

```
Component > CycloneGames > AI > Perceptible
```

```csharp
var perceptible = gameObject.AddComponent<PerceptibleComponent>();
perceptible.SetTypeId(PerceptibleTypes.Enemy);
```

### 第二步：为 AI 代理添加感知

为 AI 角色添加 `AIPerceptionComponent`：

```
Component > CycloneGames > AI > AI Perception
```

在 Inspector 中配置：
- **Sight Sensor**：锥体 FOV、最大距离、LOS
- **Hearing Sensor**：半径、遮挡衰减
- **Proximity Sensor**：触发半径
- 各传感器可独立设置 **Memory Duration（记忆时长）**

### 第三步：查询结果

```csharp
using CycloneGames.AIPerception;

public class AIBrain : MonoBehaviour
{
    private AIPerceptionComponent _perception;

    void Start() => _perception = GetComponent<AIPerceptionComponent>();

    void Update()
    {
        // 视觉 — 实时检测
        if (_perception.HasSightDetection)
        {
            var target = _perception.GetClosestSightTarget();
            Debug.Log($"看到: {((PerceptibleComponent)target).name}");
        }

        // 邻近 — 近距离警戒
        if (_perception.HasProximityDetection)
        {
            var nearest = _perception.GetClosestProximityTarget();
            Debug.Log($"附近有人，位置: {nearest.Position}");
        }
    }
}
```

---

## 核心概念

### 架构

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
|------|------|
| `PerceptibleHandle` | 值类型句柄（Id + Generation）— 无 GC、无悬垂引用 |
| `PerceptibleData` | Blittable 结构体，Burst Job 输入 |
| `DetectionResult` | 传感器输出：目标、距离、位置、可见度、`IsFromMemory` |
| `StimulusMemoryEntry` | 目标离开范围后持久化；可见度线性衰减 |
| `SensorLODLevel` | 距离阈值 + 频率倍率 |

---

## 组件参考

### PerceptibleComponent

使 GameObject 可被 AI 检测。`[DisallowMultipleComponent]`，实现 `IPerceptible`。

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Type ID | `int` | 0 | 可感知类型分类 |
| Tag | `string` | "" | 可选过滤标签 |
| Detection Radius | `float` | 1 | 邻近触发大小 |
| Is Detectable | `bool` | true | 开关检测 |
| LOS Point | `Transform` | null | 视线检测原点（为空时使用 transform） |
| Is Sound Source | `bool` | false | 标记为音频发射器 |
| Loudness | `float` | 1 | 音量（0–10） |

**运行时 API：**

```csharp
bool detected = perceptible.IsDetectable;          // 已启用且激活
var handle    = perceptible.Handle;                 // 代际句柄
var pos       = perceptible.Position;               // float3 世界位置
var detectors = perceptible.GetDetectors();         // 谁在检测我们
perceptible.SetLoudness(0.5f);                      // 动态音量
perceptible.ShowDebugOverlay = true;                // 开关调试窗口
```

### AIPerceptionComponent

AI 代理的传感器宿主。`[DisallowMultipleComponent]`。

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Enable Sight | `bool` | true | 视觉传感器 |
| Sight Config | `SightSensorConfig` | 默认 | 锥体、范围、LOS、记忆 |
| Enable Hearing | `bool` | false | 听觉传感器 |
| Hearing Config | `HearingSensorConfig` | 默认 | 半径、遮挡、记忆 |
| Enable Proximity | `bool` | false | 邻近传感器 |
| Proximity Config | `ProximitySensorConfig` | 默认 | 半径、记忆 |
| Show Debug Overlay | `bool` | false | 运行时 GUI 窗口 |

**运行时 API：**

```csharp
// 检测查询
bool hasSight     = perception.HasSightDetection;
bool hasProximity = perception.HasProximityDetection;
int sightCount    = perception.SightDetectedCount;
int memCount      = perception.SightSensor.MemoryCount;

// 获取最近目标
IPerceptible target  = perception.GetClosestSightTarget();
IPerceptible nearest = perception.GetClosestProximityTarget();

// 直接访问传感器
SightSensor sight = perception.SightSensor;
var result = sight.GetResult(0);  // 第 0 个 DetectionResult
```

### PerceptionManagerComponent

全局系统驱动。自动创建为 `[PerceptionManager]` 并置于 `DontDestroyOnLoad`。

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Deferred Job Completion | `bool` | false | LateUpdate 批量完成 Job |
| LOD Reference | `Transform` | null | 距离参考点（摄像机/玩家） |
| LOD Levels | `SensorLODLevel[]` | 3 级 | 距离 → 频率倍率 |

```csharp
// 访问单例
var mgr = PerceptionManagerComponent.Instance;
mgr.UseDeferredJobCompletion = true;
```

---

## 传感器参考

### 视觉传感器 (Sight)

锥形视觉检测，Burst 预过滤 + 主线程 LOS 射线。

| 属性 | 范围 | 默认值 | 说明 |
|------|------|--------|------|
| Half Angle | 0–180 度 | 60 | 视野半角 |
| Max Distance | 0–200 m | 30 | 检测距离 |
| Update Interval | 0–5 s | 0.1 | 更新间隔 |
| Obstacle Layer | LayerMask | 默认 | 阻挡视线的层 |
| Use Line of Sight | bool | true | 射线可见性检查 |
| Filter by Type | bool | false | 仅检测特定类型 |
| Target Type ID | int | 0 | 过滤类型 ID |
| Memory Duration | 0–60 s | 3 | 离开视野后的记忆时长 |

> [!TIP]
> 远处敌人可将 Update Interval 设为 0.2s。不需要穿墙时禁用 LOS 可显著提升性能。

### 听觉传感器 (Hearing)

球形声音检测，带墙壁遮挡衰减。

| 属性 | 范围 | 默认值 | 说明 |
|------|------|--------|------|
| Radius | 0–100 m | 15 | 检测球半径 |
| Update Interval | 0–5 s | 0.2 | 更新间隔 |
| Use Occlusion | bool | true | 墙壁衰减 |
| Occlusion Layer | LayerMask | 默认 | 阻隔声音的层 |
| Occlusion Attenuation | 0–1 | 0.5 | 穿墙音量衰减 |
| Filter by Type | bool | false | 仅检测特定类型 |
| Target Type ID | int | 6 | 过滤类型 ID |
| Memory Duration | 0–60 s | 5 | 声音消失后的记忆时长 |

### 邻近传感器 (Proximity)

简单球体触发检测 — 无 LOS、无遮挡。适用于近战范围、危险区域、个人空间。

| 属性 | 范围 | 默认值 | 说明 |
|------|------|--------|------|
| Radius | 0–50 m | 5 | 触发球半径 |
| Update Interval | 0–5 s | 0.15 | 更新间隔 |
| Filter by Type | bool | false | 仅检测特定类型 |
| Target Type ID | int | 0 | 过滤类型 ID |
| Memory Duration | 0–60 s | 2 | 离开范围后的记忆时长 |

---

## 刺激记忆 (Stimulus Memory)

目标离开传感器范围后不会立即被遗忘。记忆系统会保留条目，可见度随时间线性衰减。

```
检测 → MemoryEntry（PeakVisibility, LastDetectedTime）
         │
         ├── 重新检测到 → 刷新 LastDetectedTime，更新 PeakVisibility
         │
         └── 未检测到 → age 增加
                 │
                 ├── age < MemoryDuration → 作为 IsFromMemory 结果输出
                 └── age >= MemoryDuration → RemoveAtSwapBack
```

**按传感器配置：**

```csharp
sightConfig.MemoryDuration = 3f;     // 记住看到的 3 秒
hearingConfig.MemoryDuration = 5f;   // 记住听到的 5 秒
proximityConfig.MemoryDuration = 0f; // 邻近不需要记忆
```

**查询记忆：**

```csharp
int remembered = perception.SightSensor.MemoryCount;
for (int i = 0; i < perception.SightSensor.DetectedCount; i++)
{
    var r = perception.SightSensor.GetResult(i);
    if (r.IsFromMemory)
        Debug.Log($"记忆中的目标，位置: {r.LastKnownPosition}，可见度: {r.Visibility:F2}");
}
```

> [!TIP]
> 长记忆时长（5–10s）创造"追猎"型 AI，会搜索最后已知位置。短时长（1–2s）创造"反应"型 AI，只追可见目标。

---

## LOD 系统

基于距离的更新频率缩放，降低远处 AI 的 CPU 开销。

```
传感器位置 → 到 LOD Reference 的距离 → LOD 倍率 → 实际更新间隔

默认等级：
  0–30m:   1.00x（全频率）
  30–80m:  0.50x（半频率）
  80–200m: 0.10x（最低频率）
```

**配置：**

```csharp
// Inspector 中：PerceptionManagerComponent > LOD
// 将 Reference 设为 Camera.main 或 Player Transform
// 按需配置等级
```

或通过代码：

```csharp
SensorManager.Instance.ConfigureLOD(
    Camera.main.transform,
    new[] {
        new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1.0f },
        new SensorLODLevel { Distance = 100f, FrequencyMultiplier = 0.25f },
    }
);
```

**编辑器预览：** PerceptionManager Inspector 显示彩色距离带横条，一目了然地展示 LOD 等级。

**SceneView Gizmo：** 选中 PerceptionManager 可看到同心 LOD 距离环 + 倍率标签。

---

## 空间分区

所有传感器使用 3D 均匀网格空间索引，避免 O(n) 全量扫描。

```
世界 → 网格单元（默认 20m）
        │
   RebuildData: 按单元键排序 PerceptibleData[]
   查询:        遍历重叠单元 → 连续切片拷贝
```

| 数据规模 | 无网格 | 有网格（20m 单元，50m 范围） |
|----------|--------|---------------------------|
| 1,000 个 | 1,000/job | ~80/job（12x） |
| 10,000 个 | 10,000/job | ~200/job（50x） |
| 100,000 个 | 100,000/job | ~500/job（200x） |

网格为全 3D（X/Y/Z），正确处理多层建筑和飞行单位。

---

## 类型系统

基于整数的可扩展分类系统，支持运行时注册。

| ID | 常量 | 说明 |
|----|------|------|
| 0 | `PerceptibleTypes.Default` | 未指定 |
| 1 | `PerceptibleTypes.Player` | 玩家角色 |
| 2 | `PerceptibleTypes.Enemy` | 敌方 NPC |
| 3 | `PerceptibleTypes.Ally` | 友方 NPC |
| 4 | `PerceptibleTypes.Neutral` | 中立实体 |
| 5 | `PerceptibleTypes.Interactable` | 可交互对象 |
| 6 | `PerceptibleTypes.SoundSource` | 音频发射器 |

**自定义类型：**

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
```

**类型过滤：**

```csharp
sightConfig.FilterByType = true;
sightConfig.TargetTypeId = PerceptibleTypes.Enemy; // 只检测敌人
```

---

## Job 调度模式

### 立即模式（默认）

Job 在 `Update()` 内完成。结果立即可用。适合开发阶段。

### 延迟模式

Job 在 `LateUpdate()` 中批量完成。适合 100+ 传感器的大规模场景。

```
Update():     调度 Job → 显示上一帧结果
LateUpdate(): 完成全部 → 原子交换 → 显示新结果
```

```csharp
PerceptionManagerComponent.Instance.UseDeferredJobCompletion = true;
```

---

## 编辑器工具

### 自定义 Inspector

| 组件 | Inspector 功能 |
|------|---------------|
| AIPerceptionComponent | 彩色折叠面板、传感器开关、运行时统计（S/H/P 计数）、调试覆盖层按钮、Memory Duration 滑块 |
| PerceptibleComponent | 类型/检测/声音分区、LOS Point 提示、运行时被检测者列表 |
| PerceptionManagerComponent | 性能开关、LOD Reference 选择器、LOD 预览横条、运行时传感器计数 |

### 菜单命令

```
Tools > CycloneGames > AI Perception
  ├── Show All Debug Overlays   — 打开所有运行时调试窗口
  ├── Hide All Debug Overlays   — 关闭所有
  └── Always Show Gizmos        — 在 SceneView 中显示全部 AI 的传感器范围
```

启用 "Always Show Gizmos" 后，无需逐个选中即可看到场景中所有 AI 的传感器线框。

---

## 运行时调试工具

### 调试覆盖层

每个 AI 和 Perceptible 可显示游戏内 GUI 窗口。通过 Inspector 按钮或全局菜单切换。

```
+---------------------------+
| AI Perception - Enemy     |
| SIGHT                     |
|   已启用: True            |
|   检测到: 2               |
|   > Player (Player)       |    <- 实时检测
|     距离: 5.2m 可见: 87%  |
|   < Enemy_02 (Enemy)[mem] |    <- 刺激记忆
|     距离: 12.1m 可见: 45% |
| HEARING                   |
|   ~ (无声音)              |
| PROXIMITY                 |
|   * Player (Player)       |
|     距离: 2.1m 邻近: 95%  |
+---------------------------+
```

实时条目：`>`、`~`、`*`。记忆条目：`<`、`~M`、`.M`并带 `[mem]` 后缀。

### Gizmo 可视化

| 模式 | 显示内容 |
|------|---------|
| **选中时** | 全细节：锥体弧、球体盘、检测连线、记忆虚线 |
| **Always Show Gizmos** | 全部 AI 的简化线框 |

### LOD Gizmo

选中 PerceptionManager 可看到同心距离环 + 倍率标签（x1.00、x0.50、x0.10）。

---

## 扩展系统

### 自定义 Perceptible

```csharp
public class WeaponPerceptible : PerceptibleComponent
{
    [SerializeField] private int _dangerLevel;
    public int DangerLevel => _dangerLevel;
}
```

### 自定义 AI Perception

```csharp
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

### 自定义传感器

实现 `ISensor` 和 `IDisposable` 接口，通过 `SensorManager.Instance.Register()` 注册。

---

## 性能与规模

### 优化清单

1. **更新间隔**：0.1–0.2s 对大多数情况足够
2. **类型过滤**：只扫描相关类型
3. **禁用 LOS**：无墙体场景可跳过射线
4. **启用 LOD**：远处传感器频率降低 2–10x
5. **延迟模式**：100+ 并发传感器时启用
6. **Memory Duration = 0**：不需要记忆的传感器关闭记忆

### 规模限制

| 场景 | 容量 | 建议 |
|------|------|------|
| < 100 传感器, < 1K 目标 | 默认 | 无需调优 |
| 100–500 传感器, 1K–10K 目标 | 默认 | 启用 Deferred + LOD |
| 500+ 传感器, 10K+ 目标 | `SetMaxCapacity(32768)` | 启用所有优化 |
| 无限制 | `SetMaxCapacity(0)` | 监控内存使用 |

```csharp
// 为大型场景增加注册表容量
PerceptibleRegistry.Instance.SetMaxCapacity(32768);
```

---

## 平台支持

| 平台 | 策略 | 性能 |
|------|------|------|
| Windows / Mac / Linux | 完整 Burst SIMD | 最优 |
| Android / iOS | ARM NEON | 优秀 |
| WebGL | 主线程降级 | 良好 |
| 主机平台 | 平台 Burst | 优秀 |

---

## API 参考

### PerceptibleRegistry

```csharp
var r = PerceptibleRegistry.Instance;

PerceptibleHandle h = r.Register(perceptible);  // O(1)
r.Unregister(h);                                  // O(1)
IPerceptible p = r.Get(h);                       // O(1)
bool valid = r.IsValid(h);                       // O(1)
r.MarkDirty();                                    // 强制重建
r.SetMaxCapacity(16384);                          // 可配置上限（0 = 无限）
int count = r.Count;
int dataCount = r.GetDataCount();
```

### SensorManager

```csharp
var m = SensorManager.Instance;

m.Register(sensor);
m.Unregister(sensor);
m.ConfigureLOD(referenceTransform, lodLevels);
m.UseDeferredJobCompletion = true;
```

### AIPerceptionComponent

```csharp
// 检测状态
bool hasAny = perception.HasAnyDetection;
int sightCount = perception.SightDetectedCount;
int hearingCount = perception.HearingDetectedCount;
int proximityCount = perception.ProximityDetectedCount;

// 最近目标
IPerceptible t = perception.GetClosestSightTarget();
IPerceptible t = perception.GetClosestHearingTarget();
IPerceptible t = perception.GetClosestProximityTarget();

// 传感器
SightSensor s = perception.SightSensor;
int memCount = s.MemoryCount;
DetectionResult r = s.GetResult(index);

// 调试
perception.ShowDebugOverlay = true;
```

---

## 故障排除

### 检测不工作

1. PerceptibleComponent：已启用且 `Is Detectable` 已勾选
2. AIPerceptionComponent：传感器开关已打开
3. 目标在范围内（检查 MaxDistance / Radius）
4. 目标在视野内（仅 Sight）
5. 无障碍物阻挡 LOS（或禁用 LOS）
6. 检查 FilterByType — 确保 TypeId 匹配

### 目标可见但 "LOS Blocked"

Obstacle Layer 可能包含目标所在层。只将环境层（墙壁、地板）加入 Obstacle Layer。

### 记忆条目未出现

确保传感器配置中 `MemoryDuration > 0`。通过 `MemoryCount` 检查。

### Inspector 标签显示为空白

本模块使用纯文本标签，兼容 Unity Editor 默认字体。如果出现空白，请检查 Editor 字体设置。

### 注册表容量耗尽

```
[AIPerception] Registry capacity exhausted (16384). Increase via SetMaxCapacity().
```

在启动时调用 `PerceptibleRegistry.Instance.SetMaxCapacity(32768)`，或设为 0 实现无限增长。
