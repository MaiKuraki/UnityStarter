# RPG 交互模块 (Interaction Module)

专为 Unity RPG 开发的高性能、响应式交互系统。基于 **R3** (Reactive Extensions) 和 **VitalRouter** 构建解耦的消息传递，并内置了自动 LOD (Level of Detail) 检测优化系统。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## ✨ 特性

- ⚡ **响应式架构** - 基于 R3 构建，支持事件驱动更新和属性绑定。
- 📡 **VitalRouter 集成** - 解耦的命令处理，便于本地和网络交互逻辑扩展。
- 👁️ **LOD 检测系统** - 根据目标距离自动调整检测频率（近处高频，远处低频），显著节省 CPU 资源。
- 🎯 **加权评分** - 结合“距离”和“角度”权重的智能目标选择算法。
- 📝 **本地化支持** - 通过 `InteractionPromptData` 内置支持多语言提示文本。
- 🔌 **编辑器友好** - 提供定制的 Inspector 和调试 Gizmos 用于调整检测区域。

## 📦 依赖项

- **R3**: 用于响应式属性和事件。
- **VitalRouter**: 用于命令路由及拦截。
- **UniTask**: 用于异步/等待操作。

## 🚀 快速开始

### 步骤 1：创建可交互对象 (Interactable)

将功能添加到任意 GameObject（例如宝箱或 NPC）：

1. 在 GameObject 上添加 `Interactable` 脚本。
2. 配置 **Interaction Settings** (交互设置)：
   - **Interaction Prompt**：显示的提示文本 (例如 "打开")。
   - **Interaction Distance**：允许交互的最大距离 (例如 `2.0`)。
   - **Events**：将 `OnInteract` 关联到你的逻辑 (例如播放动画)。

### 步骤 2：设置玩家检测器 (Detector)

将检测器添加到你的角色或摄像机上：

1. 添加 `InteractionDetector` 脚本。
2. 赋值 **Detection Origin** (通常是摄像机或角色头部 Transform)。
3. 设置 **Interactable Layer** 为你的物品所在的 Layer。

### 步骤 3：初始化系统

确保场景中或启动逻辑中存在 `InteractionSystem`。它负责处理命令路由。

```csharp
// InteractionSystem 通常会自行初始化，但如果使用 DI 也可以手动管理
var system = new GameObject("InteractionSystem").AddComponent<InteractionSystem>();
system.Initialize();
```

### 步骤 4：处理输入并触发交互

在你的玩家控制器 (Player Controller) 中，监听输入并通过 VitalRouter 发布命令：

```csharp
using VitalRouter;
using R3;
using CycloneGames.RPGFoundation.Runtime.Interaction;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private InteractionDetector _detector;

    private void Start()
    {
        // 监听当前最佳交互目标 (ReactiveProperty)
        _detector.CurrentInteractable
            .Subscribe(target => {
                if (target != null) Debug.Log($"看向了: {target.InteractionPrompt}");
                // 在此处更新 UI 显示
            });
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            var target = _detector.CurrentInteractable.CurrentValue;
            if (target != null)
            {
                // 通过 VitalRouter 分发交互命令
                Router.Default.PublishAsync(new InteractionCommand(target));
            }
        }
    }
}
```

## ⚙️ 配置详细说明

### Interactable 组件

| 参数                     | 说明                                                              | 默认值     |
| ------------------------ | ----------------------------------------------------------------- | ---------- |
| **Interaction Prompt**   | 显示给玩家的提示文本（若无本地化配置则作为后备文本）。            | "Interact" |
| **Is Interactable**      | 启用/禁用交互的总开关。                                           | true       |
| **Priority**             | 优先级。高优先级的物体会覆盖低优先级物体（例如：关键道具 > 门）。 | 0          |
| **Interaction Distance** | 该物体允许被交互的最大距离。                                      | 2.0        |
| **Cooldown**             | 交互完成后进入冷却的时间（秒）。                                  | 0          |
| **Prompt Data**          | 包含本地化 TableName 和 Key 的结构体。                            | -          |

### Interaction Detector (交互检测器)

检测器使用 射线/锥体 (Raycast/Cone) 检查系统，并配合智能评分机制。

#### Detection Settings (检测设置)

| 参数                  | 说明                                         |
| --------------------- | -------------------------------------------- |
| **Detection Origin**  | 作为检测起点的 Transform 组件 (例如摄像机)。 |
| **Detection Offset**  | 相对于起点的局部偏移量 (用于微调视线高度)。  |
| **Detection Radius**  | 检测球体的半径，用于粗略筛选候选目标。       |
| **Layer Mask**        | 哪些层 (Layer) 包含可交互物体。              |
| **Obstruction Layer** | 哪些层会阻挡视线 (例如墙壁)，用于遮挡剔除。  |
| **Max Interactables** | NonAlloc 物理检测缓冲区的最大大小。          |

#### Scoring Weights (评分权重)

系统通过计算分数来选择“最佳”候选目标：`分数 = (距离 * 距离权重) + (角度 * 角度权重)`。分数越低越好。

- **Distance Weight**: 距离的重要性。权重越高，系统越倾向于选择离玩家最近的物体。
- **Angle Weight**: 角度的重要性。权重越高，系统越倾向于选择位于屏幕/视野正中心的物体。

#### LOD Settings (性能优化)

检测器根据目标距离动态降低刷新频率，以优化性能。

- **Near Interval**: 目标在 `Near Distance` 范围内时的检测间隔 (例如 33ms ≈ 30帧/秒)。
- **Far Interval**: 目标较远时的检测间隔 (例如 150ms)。
- **Sleep Mode**: 如果在 `Sleep Enter Ms` 毫秒内未检测到任何目标，检测器进入睡眠模式，检测间隔降至最低 (`Sleep Interval Ms`)。

## 🛠 编辑器工具

### Interaction Scene Debugger (场景调试器)

_(如果包含在 Editor 文件夹中)_  
使用 `Window > CycloneGames > Interaction Debugger` (位置可能变动) 可以在运行时可视化查看当前活跃的交互物体和检测器状态。

### Gizmos 可视化

- **黄色线框球**: 显示原始的检测半径范围。
- **红/绿线**: 显示视线检查 (Line-of-Sight) 的射线。
- **蓝线**: 指向当前被选中的“最佳”候选交互目标。

## 🧩 高级用法

### 自定义交互逻辑

继承 `Interactable` 类或实现 `IInteractable` 接口以创建复杂行为（例如需要钥匙才能打开的门）。

```csharp
public class DoorInteractable : Interactable
{
    public override async UniTask TryInteractAsync(CancellationToken ct)
    {
        if (HasKey())
        {
            await OpenDoorAnimation();
            base.TryInteractAsync(ct); // 触发标准事件
        }
        else
        {
            ShowLockedMessage(); // 提示需要钥匙
        }
    }
}
```

### VitalRouter 集成

该系统构建在 [VitalRouter](https://github.com/hadashiA/VitalRouter) 之上。这意味着你可以全局拦截交互指令：

```csharp
// 全局拦截器示例
public class InteractionLogger : ICommandInterceptor
{
    public async UniTask InvokeAsync<T>(T command, CancellationToken cancellation, Next<T> next) where T : ICommand
    {
        if (command is InteractionCommand ic)
        {
            Debug.Log($"玩家与 {ic.Target}进行了交互");
        }
        await next(command, cancellation);
    }
}
```
