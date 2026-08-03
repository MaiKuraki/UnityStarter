# CycloneGames Roslyn Analyzers

`CycloneGames.Analyzers` 是面向 UnityStarter 的 Roslyn Analyzer 模块，用于在编译期约束 Unity Runtime 性能、安全 API、异步模型和框架规范。

## 构建

```bash
cd UnityStarter/Analyzers
dotnet build CycloneGames.Analyzers.sln -c Release
dotnet test CycloneGames.Analyzers.sln -c Release
```

输出文件：

```text
CycloneGames.Analyzers/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
```

## Unity 项目接入

只构建 Analyzer 源码不会在 Unity Editor 中自动启用规则。Unity 接入需要把编译后的 Analyzer DLL 放入对应的 `Assets/` 或 package 作用域，并为资产设置区分大小写的 `RoslynAnalyzer` label。参见 [Unity 2022.3 Roslyn Analyzer 手册](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html)。

当前仓库只维护 Analyzer 源码项目，尚未发布或激活上述 Unity 资产。Analyzer 构建成功不能证明 Unity 编译正在执行这些规则。

面向 IDE 和 Unity 生成 C# 项目的命令行验证，建议通过团队提交的 `Directory.Build.props`、Analyzer package 或 Unity project-generation hook 接入。不要依赖未提交的个人本地配置作为团队和 CI 的唯一启用方式。

`UnityStarter/Directory.Build.props` 示例：

```xml
<Project>
    <ItemGroup>
        <ProjectReference Include="$(MSBuildThisFileDirectory)Analyzers\CycloneGames.Analyzers\CycloneGames.Analyzers.csproj"
                          ReferenceOutputAssembly="false"
                          OutputItemType="Analyzer" />
    </ItemGroup>
</Project>
```

## 已实现规则

| ID | 规则 | 严重级别 |
| -- | ---- | -------- |
| CG0001 | 热路径中的 `foreach` | Warning |
| CG0002 | 热路径中的 LINQ | Warning |
| CG0003 | 热路径中的字符串构造 | Warning |
| CG0004 | 热路径中的 `Camera.main` | Warning |
| CG0010 | 生产代码中的 `GameObject.Find` | Error |
| CG0011 | 场景级 `FindObjectOfType` API | Error |
| CG0012 | `SendMessage` / `BroadcastMessage` | Error |
| CG0013 | `MonoBehaviour.Invoke` API | Error |
| CG0014 | `Resources.Load` | Warning |
| CG0030 | `MonoBehaviour` 上的 public 实例字段 | Warning |
| CG0031 | Runtime 代码中的 `using static` | Warning |
| CG0032 | Runtime 代码中的 `#region` | Info，默认关闭 |
| CG0033 | CycloneGames 框架代码中的 `[Obsolete]` | Warning |
| CG0040 | Runtime 代码中的 `async void` | Error |
| CG0041 | 热路径中的链式 `component.transform` 访问 | Warning |
| CG0042 | Editor 目录外使用 `UnityEditor` | Warning |
| CG0043 | 热路径中的 `Debug.Log` | Warning |
| CG0044 | 热路径中的 `GetComponent<T>` | Warning |
| CG0045 | 热路径中的装箱转换 | Warning |
| CG0046 | 热路径中的 lambda 或匿名方法 | Warning |
| CG0047 | 引用 UniTask 时使用 `async Task` | Warning |
| CG0048 | static class 循环依赖风险 | Warning |
| CG0049 | 受治理 CycloneGames assembly 绕过统一日志 API | Error |
| CG0050 | 在 assembly 日志门面之外调用 `LogChannel.Create` | Error |

## Code Fix

| Diagnostic | Fix |
| ---------- | --- |
| CG0001 | 仅当集合具备 `Count` 或 `Length` 且具备 `int` indexer 时，将 `foreach` 转换为 `for`。 |
| CG0004 | 新增缓存 `Camera` 字段和 `Awake` 赋值，并替换 `Camera.main`。 |

## 热路径识别

默认热路径方法名：

```text
Update, LateUpdate, FixedUpdate, OnGUI,
Tick, OnTick, OnUpdate, PreUpdate, PostUpdate,
OnPreTick, OnPostTick
```

热路径规则保持保守：宁可少量漏报，也不要大范围误报导致团队禁用 Analyzer。

## 统一日志约束

`CG0049` 禁止受治理的 CycloneGames package assembly 绕过共享日志契约。治理范围包括 Runtime、Editor、Samples 与 Benchmarks，因为可复制的示例代码也是包 API 使用指引的一部分。规则使用 Roslyn 已解析符号而不是短类型名，并报告：

- `UnityEngine.Debug.Log*`、`UnityEngine.Debug.Assert*` 和 `Debug.unityLogger` 访问。
- `UnityEngine.MonoBehaviour.print`。
- `System.Console.Write*` 以及对 `Console.Out` 或 `Console.Error` 的访问。
- Logging backend assembly 之外对具体 `CycloneGames.Logging.Pipeline.LogPipeline` backend 类型的引用。

规则只作用于名称以 `CycloneGames.` 开头的 assembly。backend assembly `CycloneGames.Logging.Pipeline`、`CycloneGames.Logging.Unity` 与 `CycloneGames.Logging.Unity.Editor` 不参与检查；明确标识为 Tests、Tools 或 CodeGen 的 assembly 或源码路径也不参与检查，这些位置属于验证或宿主 I/O 边界。Runtime、Editor、Samples 与 Benchmarks 仍在治理范围内。`CycloneGames.Logging.Unity.Samples` composition sample 可以引用 `LogPipeline`，但其中直接使用 Unity 与 Console 输出 API 仍受治理。`CycloneGames.MemoryGovernance.Logging.Pipeline` 或 `CycloneGames.MemoryGovernance.Logging.Pipeline.Editor` 等相似业务名称不是标准 backend assembly，仍会接受检查。

在受约束的 package 代码中，直接日志诊断由 `CG0049` 负责，因此只检查热路径的 `CG0043` 不会对同一个 `Debug.Log*` 调用重复报告。`CG0043` 在该范围之外仍保持启用。

`CG0050` 将 channel 构造收敛到每个产生日志 assembly 的单一、可发现边界：名称唯一且以 `Log` 结尾的顶层 `internal static` 类型，存放在 `Diagnostics/<TypeName>.cs`，并提供统一的 internal 成员 `Category`、`Channel` 与 `Create(ILogWriter logWriter)`。例如，`CycloneGames.Audio.Runtime` 使用 `Diagnostics/AudioRuntimeLog.cs`；实现文件消费 `AudioRuntimeLog.Channel` 或 `AudioRuntimeLog.Create(logWriter)`，不再直接调用 `LogChannel.Create`。名称按 assembly 特征保持唯一，可避免测试或 integration assembly 通过 internals 可见性同时看到多个同名门面时发生类型歧义。

两个规则都不提供 CodeFix。安全替换依赖模块 category、exception/context 处理、延迟格式化和日志所有权，单个调用点无法推断这些信息。`CG0050` 自动验证构造边界、文件约定与标准门面成员签名；category 具体值与显式 null 语义仍由 package test 和 API 评审负责。

## 抑制规则

项目级严重性应通过 `.editorconfig` 配置：

```ini
[*.cs]
dotnet_diagnostic.CG0014.severity = none
dotnet_diagnostic.CG0001.severity = error
```

只有在相关分配或 API 成本是有意行为并且已记录原因时，才使用局部抑制：

```csharp
#pragma warning disable CG0014
var config = Resources.Load<GameConfig>("Config");
#pragma warning restore CG0014
```

## 质量门槛

默认启用一条规则前应满足：

- 语义检查优先，避免只依赖脆弱的字符串匹配。
- Runtime、Editor、Samples 与 Benchmarks 接受治理；`CycloneGames.Logging.Unity.Samples` 仅获得 pipeline composition 窄例外，原始输出 API 仍被禁止。
- 覆盖正例和反例测试。
- 对现有 UnityStarter 模块保持低误报。
- 只有在改写对常见 Unity 代码模式安全时才提供 CodeFix。
