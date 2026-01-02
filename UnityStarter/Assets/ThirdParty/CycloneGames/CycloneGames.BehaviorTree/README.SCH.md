# CycloneGames.BehaviorTree

Unity AI 的高性能、零 GC 行为树系统，采用双层架构设计。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## 特性

- ✅ **双层架构**：ScriptableObject 编辑层 + 纯 C# 运行时层，实现 0GC 执行
- ✅ **基于组件的设计**：使用 `BTRunnerComponent` 轻松设置和管理生命周期
- ✅ **ScriptableObject 树**：使用 Unity 内置工具进行可视化树设计
- ✅ **层级式 BlackBoard**：节点间类型安全的数据共享，支持父级继承
- ✅ **丰富的节点库**：组合节点、装饰节点、条件节点和动作节点
- ✅ **DI/IoC 就绪**：支持依赖注入，无缝集成
- ✅ **编辑器可视化**：Play 模式下实时树状态可视化
- ✅ **多平台支持**：Windows、Mac、Linux、Android、iOS、WebGL 及服务器端

## 架构设计

系统采用**双层架构**，同时实现易用性和极致运行时性能：

```
┌─────────────────────────────────────────────────────────────┐
│                    编辑时 (Editor Time)                      │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ScriptableObject 层 (BTNode, CompositeNode...)     │   │
│  │  • 可视化编辑器拖拽配置                               │   │
│  │  • 序列化到 .asset 文件                              │   │
│  │  • 支持 Undo/Redo                                   │   │
│  └──────────────────────────────────────────────────────┘   │
│                           ↓ Compile()                        │
├─────────────────────────────────────────────────────────────┤
│                     运行时 (Runtime)                         │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  纯 C# 层 (RuntimeNode, RuntimeBlackboard...)       │   │
│  │  • 零 GC 分配                                       │   │
│  │  • 无 Unity 依赖（可用于服务器）                      │   │
│  │  • 极致性能                                         │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 为什么要双层设计？

| 方面 | ScriptableObject 层 | Runtime 层 |
|------|---------------------|------------|
| **目的** | 编辑器创作、序列化 | 游戏执行 |
| **GC** | 可接受（仅编辑时） | **零分配** |
| **Unity 依赖** | 必需 | 可选 |
| **性能** | 标准 | **优化** |

当调用 `BTRunnerComponent.Play()` 时，系统会将 ScriptableObject 树**编译**为优化的纯 C# 运行时树。这只在启动时发生一次，之后所有执行都使用零 GC 的运行时节点。

## 安装

### 作为 Unity Package

1. 将 `CycloneGames.BehaviorTree` 文件夹复制到项目的 `Packages` 目录
2. 或通过 Package Manager → "Add package from disk" → 选择 `package.json`

### 依赖

- Unity 2022.3 或更高版本
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.0+

## 快速开始

### 1. 创建行为树资源

在 Project 窗口右键 → `Create > CycloneGames > AI > BehaviorTree`

### 2. 设计您的树

在 Inspector 中打开树资源，使用内置编辑器：
- 创建根节点
- 添加组合节点（Sequence、Selector、Parallel）
- 添加装饰节点（Repeater、Inverter、UntilFail）
- 添加动作节点（自定义逻辑）
- 添加条件节点（决策制定）

<img src="./Documents~/BehaviorTreePreview.png" alt="Behavior Tree Editor Preview" style="width: 100%; height: auto; max-width: 1000px;" />

### 3. 附加到 GameObject

将 `BTRunnerComponent` 添加到您的 AI GameObject：

**方式 A：在 Inspector 中**
1. 选择您的 AI GameObject
2. Add Component → `BTRunnerComponent`
3. 将您的 BehaviorTree 资源分配给 `Behavior Tree` 字段
4. （可选）在 `Initial Objects` 中添加初始 BlackBoard 数据
5. 勾选 `Start On Awake` 以自动启动树

**方式 B：从代码**
```csharp
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEngine;

public class AIAgent : MonoBehaviour
{
    [SerializeField] private BehaviorTree behaviorTreeAsset;
    private BTRunnerComponent runner;

    void Start()
    {
        // 添加并配置 runner 组件
        runner = gameObject.AddComponent<BTRunnerComponent>();
        runner.SetTree(behaviorTreeAsset);
        
        // 设置初始 BlackBoard 数据
        runner.BTSetData("TargetPosition", Vector3.zero);
        runner.BTSetData("Speed", 5f);
        
        // 启动树
        runner.Play();
    }
}
```

**直接访问 BlackBoard**（高级用法）：
```csharp
// 直接访问 BlackBoard 进行类型化操作（零 GC）
BTRunnerComponent runner = GetComponent<BTRunnerComponent>();
runner.BlackBoard.SetInt("Health", 100);
runner.BlackBoard.SetFloat("Speed", 5.5f);
runner.BlackBoard.SetBool("IsAlive", true);
```

## 核心概念

### BTRunnerComponent

使用行为树的**推荐方式**。此 MonoBehaviour 组件管理 BehaviorTree ScriptableObject 的生命周期。

**主要特性**：
- 自动将树编译为优化的运行时表示
- 处理 Update 循环和树执行
- 管理 BlackBoard 数据，支持 Inspector 配置
- 提供 Play/Pause/Stop/Resume 控制
- 支持运行时热切换树

**Inspector 配置**：
- `Behavior Tree`：要使用的 ScriptableObject 树资源
- `Start On Awake`：组件唤醒时自动启动树
- `Initial Objects`：从 Inspector 设置初始 BlackBoard 值

**运行时控制**：
```csharp
BTRunnerComponent runner = GetComponent<BTRunnerComponent>();

// 控制执行
runner.Play();      // 启动/重启树
runner.Pause();     // 暂停执行
runner.Resume();    // 从暂停恢复
runner.Stop();      // 停止并重置树

// 设置 BlackBoard 数据
runner.BTSetData("Key", value);
runner.BTSendMessage("EventName");
runner.BTRemoveData("Key");  // 从黑板移除数据

// 直接访问树/黑板（高级）
BehaviorTree tree = runner.Tree;
BlackBoard bb = runner.BlackBoard;
```

### 行为树（Behavior Tree - ScriptableObject）

在 Project 窗口中创建的树资源。这是一个**模板**，由 `BTRunnerComponent` 编译为优化的运行时树。

**重要提示**：虽然您*可以*在代码中直接使用 `BehaviorTree`，但对于大多数用例来说**不推荐**。请改用 `BTRunnerComponent` 以实现自动生命周期管理。

### 黑板（BlackBoard）

节点间共享的类型安全数据存储，支持层级继承。

**零 GC 类型访问**：
```csharp
// 专用字典避免装箱
blackBoard.SetInt("Health", 100);
blackBoard.SetFloat("Speed", 5.5f);
blackBoard.SetBool("IsAlive", true);
blackBoard.SetVector3("Position", transform.position);

int health = blackBoard.GetInt("Health");
float speed = blackBoard.GetFloat("Speed", defaultValue: 1f);
```

**泛型访问**（用于引用类型）：
```csharp
blackBoard.Set("Target", targetTransform);
Transform target = blackBoard.Get<Transform>("Target");
```

**移除数据**：
```csharp
blackBoard.Remove("Key");  // 移除指定键
blackBoard.Clear();        // 清空所有数据
```

### 节点类型

#### 组合节点（Composite Nodes）
控制子节点执行流程：
- **SequenceNode**：按顺序执行子节点，遇到第一个失败则失败
- **SelectorNode**：执行子节点直到某个成功
- **ParallelNode**：同时执行所有子节点
- **RandomSelectorNode**：随机选择子节点执行

#### 装饰节点（Decorator Nodes）
修改子节点行为：
- **RepeaterNode**：重复子节点 N 次或无限次
- **InverterNode**：反转子节点结果（成功 ↔ 失败）
- **UntilFailNode**：重复子节点直到失败
- **TimeoutNode**：如果子节点超时则失败
- **CooldownNode**：冷却时间未到时阻止执行

#### 动作节点（Action Nodes）
执行实际行为：
- 自定义逻辑实现
- 可返回 Success、Failure 或 Running
- 支持 UniTask 异步执行

#### 条件节点（Condition Nodes）
做出决策：
- 评估黑板数据
- 返回 Success 或 Failure（永不返回 Running）
- 用于分支逻辑

## Runtime Core（高级）

适用于需要了解内部架构或构建自定义运行器的用户。

### RuntimeNode

所有运行时节点的基类，设计目标是**零 GC 执行**。

```csharp
public abstract class RuntimeNode
{
    public RuntimeState State { get; protected set; }
    public bool IsStarted { get; private set; }
    public string GUID { get; set; }  // 映射回 BTNode 用于调试

    public RuntimeState Run(RuntimeBlackboard blackboard);
    protected virtual void OnStart(RuntimeBlackboard blackboard);
    protected abstract RuntimeState OnRun(RuntimeBlackboard blackboard);
    protected virtual void OnStop(RuntimeBlackboard blackboard);
    public void Abort(RuntimeBlackboard blackboard);
}
```

**关键设计要点**：
- 状态机：`NotEntered → Running → Success/Failure`
- 生命周期钩子：`OnStart` / `OnRun` / `OnStop`
- 执行期间无内存分配

### GUID 的作用

每个节点都有一个 GUID，用于两个目的：
1. **编辑器调试**：将 RuntimeNode 映射回 BTNode 以显示状态
2. **运行时查找**：`RuntimeBehaviorTree.GetNodeByGUID()` 用于程序化访问

### RuntimeBlackboard

使用 **int 哈希键** 而非字符串的优化黑板：

```csharp
// Int 键访问（最高性能，0GC）
int keyHash = Animator.StringToHash("Health");
blackboard.SetInt(keyHash, 100);
int health = blackboard.GetInt(keyHash);

// 字符串键便捷方法（稍慢，仍然 0GC）
blackboard.SetInt("Health", 100);
int health = blackboard.GetInt("Health");
```

**为什么使用 Int 键？**
- int 键的字典查找比字符串键更快
- 运行时无字符串分配
- `Animator.StringToHash` 内部有缓存

## 创建自定义节点

### 动作节点示例

```csharp
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using UnityEngine;

[BTInfo("Custom/Move To Target", "移动智能体到目标位置")]
public class MoveToTargetNode : ActionNode
{
    [SerializeField] private string targetKey = "TargetPosition";
    [SerializeField] private string speedKey = "Speed";
    [SerializeField] private float arrivalRadius = 0.5f;

    protected override BTState OnRun(IBlackBoard blackBoard)
    {
        Vector3 target = blackBoard.GetVector3(targetKey);
        float speed = blackBoard.GetFloat(speedKey, 1f);
        
        Transform agentTransform = Tree.Owner.transform;
        float distance = Vector3.Distance(agentTransform.position, target);
        
        if (distance <= arrivalRadius)
        {
            return BTState.SUCCESS;
        }
        
        Vector3 direction = (target - agentTransform.position).normalized;
        agentTransform.position += direction * speed * Time.deltaTime;
        
        return BTState.RUNNING;
    }
}
```

### 条件节点示例

```csharp
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using CycloneGames.BehaviorTree.Runtime.Conditions;

[BTInfo("Custom/Check Health", "检查生命值是否高于阈值")]
public class CheckHealthNode : ConditionNode
{
    [SerializeField] private string healthKey = "Health";
    [SerializeField] private int threshold = 50;

    protected override bool Evaluate(IBlackBoard blackBoard)
    {
        int health = blackBoard.GetInt(healthKey);
        return health >= threshold;
    }
}
```

## 性能优化

### 零 GC 保证

| 操作 | GC 分配 |
|------|---------|
| 树 tick（每帧） | **0 字节** |
| BlackBoard 类型访问 | **0 字节** |
| 节点状态转换 | **0 字节** |
| 树编译 | 仅一次 |

### 最佳实践

1. **对值类型使用类型化 BlackBoard 方法**：
   ```csharp
   // 好 - 零 GC
   int value = blackBoard.GetInt("Key");
   
   // 差 - 装箱分配
   int value = (int)blackBoard.Get("Key");
   ```

2. **在 OnAwake/OnStart 中缓存引用**：
   ```csharp
   private Transform cachedTransform;
   
   protected override void OnAwake()
   {
       cachedTransform = Tree.Owner.transform;
   }
   ```

3. **避免在 OnRun 中分配内存**：
   - 不要创建新对象
   - 不要使用 LINQ
   - 不要拼接字符串

## 依赖注入

使用 DI 容器向节点注入服务：

```csharp
// 在组合根中
behaviorTree.Inject(container);

// 在自定义节点中
public class ServiceDependentNode : ActionNode
{
    private IPathfindingService pathfinder;
    
    public override void Inject(object container)
    {
        // VContainer 示例
        if (container is IObjectResolver resolver)
        {
            pathfinder = resolver.Resolve<IPathfindingService>();
        }
    }
}
```

## API 参考

### BTState 枚举

```csharp
public enum BTState
{
    SUCCESS,    // 节点成功完成
    FAILURE,    // 节点失败
    RUNNING,    // 节点仍在执行
    NOT_ENTERED // 节点尚未开始
}
```

### RuntimeState 枚举（Runtime 层）

```csharp
public enum RuntimeState
{
    Success,
    Failure,
    Running,
    NotEntered
}
```

### IBlackBoard 接口

```csharp
public interface IBlackBoard
{
    // 泛型访问
    object Get(string key);
    T Get<T>(string key);
    void Set(string key, object value);
    
    // 零 GC 类型访问
    int GetInt(string key, int defaultValue = 0);
    void SetInt(string key, int value);
    float GetFloat(string key, float defaultValue = 0f);
    void SetFloat(string key, float value);
    bool GetBool(string key, bool defaultValue = false);
    void SetBool(string key, bool value);
    Vector3 GetVector3(string key, Vector3 defaultValue = default);
    void SetVector3(string key, Vector3 value);
    
    // 工具方法
    bool Contains(string key);
    void Remove(string key);
    void Clear();
}
```