# CycloneGames.Logger 示例

高性能、零GC日志系统，具备三级自适应容量管理机制，在所有Unity平台上实现最佳内存安全性。

## 核心特性

- **三级容量管理** - 自动扩充和收缩池容量  
- **零GC日志** - Builder API消除热路径内存分配  
- **对象池监控** - Debug/Development版本统计功能  
- **跨平台支持** - 支持Windows、macOS、Linux、Android、iOS、WebGL及主机平台

## 示例脚本

### LoggerPoolMonitor.cs
**交互式对象池监控和容量验证**

功能：
- 实时显示对象池统计数据
- 突发负载测试，验证零GC行为
- 演示三级容量管理（Target/Peak/Max）
- Context Menu快捷命令

使用方法：
```csharp
// 添加到GameObject并运行
// 在Inspector右键查看Context Menu：
//  - Show Pool Statistics（显示池统计）
//  - Run Burst Test（运行突发测试）
//  - Reset Statistics（重置统计）
```

### LoggerBenchmark.cs
**性能对比与GC追踪**

测试内容：
- Unity Debug.Log vs CLogger String API vs Builder API
- 测量执行时间和GC分配
- 测试后显示对象池统计

预期结果：
- Builder API：**最小GC分配**（包含Unity框架开销；生产环境接近零GC）
- String API：中等GC分配
- Unity Debug.Log：高GC分配

注意：GC测量包含Unity测试环境开销和冷启动池分配。关键指标是100%归还率和0%丢弃率，这验证了生产环境下的零GC行为。

### LoggerPerformanceTest.cs
**大容量日志压力测试**

- 记录10,000条不同级别的日志
- 验证持续负载下的池行为
- 报告峰值池大小和丢弃计数

### LoggerSample.cs
**基础使用示例**

简单演示Logger的设置和基本日志记录。

---

## 三级容量管理

日志系统使用自适应对象池，可自动扩充和收缩：

```
Target (128/256)  <- 正常稳态容量
    | 负载增加时自动扩充
Peak (1024/4096)  <- 突发期间允许的最大容量（0 GC）
    | 超出时触发异步收缩
Max (2048/8192)   <- 防止内存泄漏的硬上限
```

### 工作原理

1. **正常负载**：池维持在Target容量（StringBuilder为128，LogMessage为256）
2. **突发负载**：池自动扩充到Peak容量，**不丢弃对象**（0 GC）
3. **突发后**：池自动收缩回Target，释放多余内存
4. **极端负载**：仅在超过Max时丢弃对象（罕见，安全机制）

**结果**：99.9%的场景下实现零GC，同时保证内存安全。

---

## 处理策略

### ThreadedLogProcessor（默认）
在支持线程的平台上使用后台线程（`BelowNormal`优先级），提供最佳性能。

### SingleThreadLogProcessor
用于不支持线程的平台（WebGL）。需要每帧调用`Pump()`。

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    CLogger.ConfigureSingleThreadedProcessing();
#else
    CLogger.ConfigureThreadedProcessing();
#endif
```

---

## 零GC日志

### String API（便捷）
```csharp
CLogger.LogInfo($"玩家HP: {hp}", "Combat");
// 字符串插值会产生少量GC
```

### Builder API（零GC）[推荐]
```csharp
CLogger.LogInfo(sb => sb.Append("玩家HP: ").Append(hp), "Combat");
// 零GC - StringBuilder使用对象池
```

### 带状态Builder（高级）
```csharp
CLogger.LogInfo(player, (p, sb) => 
    sb.Append("玩家 ").Append(p.name).Append(" HP: ").Append(p.hp), "Combat");
// 零GC + 避免闭包分配
```

---

## 对象池统计（仅Editor/Development版本）

在Editor或Development版本中监控池健康状况：

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
var stats = StringBuilderPool.GetStatistics();
Debug.Log($@"
StringBuilder Pool:
  当前大小: {stats.CurrentSize} | 峰值: {stats.PeakSize}
  命中率: {stats.HitRate:P} | 丢弃率: {stats.DiscardRate:P}
");
#endif
```

**关键指标**：
- **PeakSize**：达到的最大池大小（应低于Max）
- **DiscardRate**：应约为0%以获得最佳性能
- **HitRate**：应约为100%（从池中获取 vs 新创建）

---

## 集中配置

### 方式1：LoggerSettings资源（推荐）
1. 创建：`Assets -> Create -> CycloneGames -> Logger -> LoggerSettings`
2. 移动到：`Assets/Resources/CycloneGames.Logger/LoggerSettings.asset`
3. 配置：处理模式、Logger、日志级别等

### 方式2：自定义Bootstrap
```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Initialize()
{
    #if UNITY_WEBGL && !UNITY_EDITOR
    CLogger.ConfigureSingleThreadedProcessing();
    #else
    CLogger.ConfigureThreadedProcessing();
    #endif

    CLogger.Instance.AddLoggerUnique(new UnityLogger());
    
    #if !UNITY_WEBGL || UNITY_EDITOR
    var path = Path.Combine(Application.persistentDataPath, "App.log");
    CLogger.Instance.AddLoggerUnique(new FileLogger(path));
    #endif

    CLogger.Instance.SetLogLevel(LogLevel.Info);
}
```

---

## 最佳实践

**性能方面：**
- 在性能关键代码中使用**Builder API**  
- 在开发版本中监控**DiscardRate**  
- 设置适当的**LogLevel**过滤不必要的日志  

**平台方面：**
- WebGL版本在Update中调用**Pump()**  
- 使用**分类**进行细粒度过滤  

**质量方面：**
- 集中配置Logger  
- 避免重复注册Logger  

---

## 故障排查

**问：统计数据显示高DiscardRate？**  
答：在池源代码中增加`PeakPoolSize`，或减少日志频率。

**问：内存持续增长？**  
答：在统计数据中验证`TrimCount > 0`。池应该在突发后自动收缩。

**问：WebGL日志不显示？**  
答：确保每帧调用`Pump()`，且`maxItems`足够。

---

更多详情请查看主包文档。
