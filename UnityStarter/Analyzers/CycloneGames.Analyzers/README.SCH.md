# CycloneGames Roslyn Analyzers

`CycloneGames.Analyzers` 是面向 UnityStarter 的 Roslyn Analyzer 模块，用于在编译期约束 Unity Runtime 性能、安全 API、异步模型和框架规范。

## 构建

```bash
cd <unity-project>/Analyzers
dotnet build CycloneGames.Analyzers.sln -c Release
dotnet test CycloneGames.Analyzers.sln -c Release
```

`<unity-project>` 表示 Unity 项目目录（包含 `ProjectSettings/ProjectVersion.txt` 的文件夹）。Analyzer 体系不依赖任何名称：源码归属门、测试路径助手与激活验证脚本都通过 `ProjectSettings/ProjectVersion.txt` marker 定位 Unity 项目，因此项目文件夹改名或移动都不需要改动 Analyzer 工程。`CycloneGames.Analyzers.Unity.csproj` 的 `UnityProjectRoot` 默认取工程文件上两级目录，Analyzer 工程自身被移动时可用 `-p:UnityProjectRoot=<unity-project-路径>` 显式覆盖。当 CycloneGames 包以 UPM 形式（`Packages/...`）被消费时，包源码有意落在仓库自有范围之外：宿主项目不会治理包代码，包侧治理由包仓库自己的 Analyzer 构建承担。**本迭代的已记录决策：** Analyzer 与其 `Default.ruleset` 仅随项目模板分发，不作为 UPM 包发布——UPM 消费者在无宿主侧 Analyzer 治理的情况下运行，这是被接受的边界而非遗漏。

解决方案会从同一套规则源码生成两份 Analyzer 工件：

```text
CycloneGames.Analyzers/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
CycloneGames.Analyzers.Unity/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
```

第一个项目继续作为开发与 package 构建。Unity 专用项目使用当前 Unity 版本支持的 Roslyn 依赖级别，从 Editor 工件中排除 CodeFix/Workspaces 依赖，并把 Release 输出安装到 `<unity-project>/Assets/Analyzers/CycloneGames.Analyzers.dll`。

## Unity 项目接入

Unity 接入需要把编译后的 Analyzer DLL 放入对应的 `Assets/` 或 package 作用域，并为资产设置区分大小写的 `RoslynAnalyzer` label。参见 [Unity 2022.3 Roslyn Analyzer 手册](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html)。

当前仓库通过 `<unity-project>/Assets/Analyzers/` 中已提交的 DLL 与 `.meta` 文件激活 Analyzer。Plugin Importer 继续禁止把该 DLL 当作普通 managed plugin 加载；`RoslynAnalyzer` label 是实际激活契约。构建 `CycloneGames.Analyzers.Unity.csproj` 的 Release 配置会刷新已提交 DLL。

修改 Analyzer 依赖、部署 metadata 或 diagnostic 激活行为后，运行真实编译器 fixture：

```bash
# 任意平台：验证器是 .NET console 工具，不依赖 PowerShell。
dotnet run --project <unity-project>/Analyzers/CycloneGames.Analyzers.Verifier/CycloneGames.Analyzers.Verifier.csproj -- \
  --unity-editor-path '<path-to-Unity>/Editor/Unity.exe'

# Windows 编辑器路径：'<path-to-Unity>/Editor/Unity.exe'
# macOS 编辑器路径：  '/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity'
# Linux 编辑器路径：  '~/Unity/Hub/Editor/<version>/Editor/Unity'
```

验证器会先构建 Unity 兼容版 Analyzer；传入 `--skip-build` 可复用现有 Release 构建。`--unity-project-root` 可覆盖基于 marker 的项目发现，`--help` 列出全部选项。

验证器会创建隔离的临时 Unity 项目，安装已提交 Analyzer 资产，并编译 `Integration/ForbiddenUnityApiViolation.cs.txt`。只有 Unity 未出现 `CS8032`/加载错误且真实输出 `CG0010` 时才通过。由于 fixture 故意触发 Error 级别的 `CG0010` 诊断，一次通过的验证通常会以 Unity 因编译错误中止 batchmode 并返回非零退出码结束——这个失败正是 Analyzer 生效的预期信号，而不是验证失败。`--timeout-seconds` 是覆盖 Analyzer 构建、Unity 编译与有界清理的单一端到端 deadline；`--keep-temporary-project` 会为诊断保留临时项目。所有外部进程都带硬 deadline 运行，超时即连同进程树一起终止；无法确认进程树终止时，验证器 fail-closed 并保留临时项目（任何失败同样保留并打印路径）。Windows 上还会 best-effort 地只停止本次运行中由编辑器新拉起的 VBCSCompiler server，既有 Editor server 不受影响。

所有 Analyzer callback 只接收仓库自有源码。对于绝对源码路径，最后一个 `Assets/` segment 用于确定候选 Unity 项目根；对于规范的相对 `Assets/...` 路径，当前 host 目录必须能解析到该项目根。只有 `ProjectSettings/ProjectVersion.txt` 是带 Unity 版本 marker、大小受限、普通且非 reparse 的文件时，候选根才受信任。验证结果先进入容量有界的进程内 cache，再应用 `Assets/ThirdParty/` allowlist。路径 segment 比较遵循 host 文件系统：Windows 不区分大小写，Linux/macOS 区分大小写。这样，`Assets/Build/`、`Assets/<project-folder>/`、`Assets/ThirdParty/CycloneGames/` 与可选的 `Assets/ThirdParty/CycloneGames.MemoryGovernance/` package family 仍在治理范围内；Unity `Library/PackageCache/`、UPM `Packages/`、package 内嵌 `Assets/`、非 CycloneGames 第三方内容、generated path 和其他所有未知非空路径都会 fail-closed。即使真实 Unity checkout 位于名为 `Packages/<reverse-DNS-name>` 的祖先目录下，只要自身候选根具有 marker，仍会接受治理。只有空路径无需 marker 也在范围内；这是未提供物理路径的 focused Roslyn host/test 专用契约。

`<unity-project>/Assets/Default.ruleset` 是已提交的 Unity 强制策略。`CG0010` 保持 Error；既有场景发现与定时器调用完成迁移前，`CG0011` 与 `CG0013` 保持可见 Warning，避免启用 Analyzer 时把已知迁移债直接转化为无关编译中断。GameplayAbilities sample 中两处按名称查找目标的代码是该 sample scene 专用、已局部记录的 `CG0010` 例外；生产替代边界是项目自有 targeting service。

激活流程只持久化已提交的 DLL 与 `.meta` 资产。每个验证项目都使用独占随机 owner marker 认领；递归清理前会重新验证该 marker。`--keep-temporary-project` 会为诊断保留项目；无法确认进程树终止时也会自动保留。`bin/`、`obj/` 下的构建中间产物、带 owner marker 的操作系统临时验证项目以及 Unity import cache 均可重建；没有 Unity 进程占用时可以安全删除。

## 已实现规则

| ID | 规则 | 描述符默认严重级别 |
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

`DiagnosticIds` 声明 28 个 ID 常量；上表列出的是 24 条已实现规则。`CG0015`（`NativeContainerLeak`）、`CG0022`（`ActorStartBaseCall`）、`CG0023`（`PoolOnDespawnOverride`）与 `CG0024`（`GameplayTagImplicitCast`）是尚未实现、刻意保留的规则 ID。

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

Unity compiler 的严重级别应通过已提交 ruleset 配置：

```xml
<Rule Id="CG0011" Action="Warning" />
```

兼容的 IDE 或独立 .NET compiler host 可以使用 `.editorconfig`：

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
