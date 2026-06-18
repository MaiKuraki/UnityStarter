# CycloneGames.Cheat

CycloneGames.Cheat 是面向内部调试、QA、GM、自动化和 live-ops 工具的命令层。模块使用 VitalRouter 进行强类型命令路由，并通过 `Core` 与 `Runtime` assembly 分离包级契约和 Unity-facing 运行时服务。

本模块不是反作弊系统。商业多人项目仍然必须在服务端验证权威操作，通过环境和身份限制高权限命令，审计使用记录，并避免把客户端调试命令当作玩法事实来源。

## 目录结构

```text
CycloneGames.Cheat/
  Core/
    CycloneGames.Cheat.Core.asmdef
    CheatCommand.cs
    CheatDuplicatePolicy.cs
    CheatRuntimeMetrics.cs
    ICheatLogger.cs
  Runtime/
    CycloneGames.Cheat.Runtime.asmdef
    CheatCommandRuntime.cs
    CheatCommandExecutionOptions.cs
    ICheatCommandRuntime.cs
    UnityDebugCheatLogger.cs
    Integrations/DI/VContainer/
  Samples/
  Tests/Editor/
```

命名空间与目录和 assembly 边界保持一致：

| 层 | 命名空间 | 职责 |
| --- | --- | --- |
| Core | `CycloneGames.Cheat.Core` | 命令 payload、重复策略、指标、logger 契约。 |
| Runtime | `CycloneGames.Cheat.Runtime` | VitalRouter 发布、取消、生命周期所有权、Unity logger。 |
| Tests | `CycloneGames.Cheat.Tests.Editor` | Core 和 Runtime 契约测试。 |

## Runtime 模型

`CheatCommandRuntime` 由调用方显式持有。非 DI 项目可以直接创建；DI 项目可以注册 `ICheatCommandRuntime`、`ICheatCommandPublisher` 和 `ICheatCommandControl`。释放 runtime 会停止新的发布请求，对运行中的命令请求取消，并由对应 publish 操作在 handler 退出时释放命令状态。

```csharp
using CycloneGames.Cheat.Runtime;
using Cysharp.Threading.Tasks;

var runtime = new CheatCommandRuntime(new UnityDebugCheatLogger());
await runtime.PublishAsync("World_ReloadConfig");
runtime.Dispose();
```

模块故意不再提供全局 static facade。长期项目应在 scene root、工具 owner、service composition root 或 DI lifetime scope 中显式表达所有权。

VContainer installer 位于受 `VCONTAINER_PRESENT` 约束的可选 integration assembly 中。没有安装 VContainer 的项目可以移除或忽略该目录，不会影响核心 runtime。

## 命令类型

Core 命令类型同时实现 `ICheatCommand` 和 `VitalRouter.ICommand`：

| 类型 | 用途 |
| --- | --- |
| `CheatCommand` | 只有 command ID。 |
| `CheatCommand<T>` | 一个 struct payload。 |
| `CheatCommand<T1, T2>` | 两个 struct payload。 |
| `CheatCommand<T1, T2, T3>` | 三个 struct payload。 |
| `CheatCommandClass<T>` | 一个引用类型 payload。热路径优先使用 struct payload。 |

对于稳定生产工作流，建议定义专用 command struct 并实现 `ICheatCommand`，而不是把所有操作塞进字符串分支。专用类型能让 VitalRouter 提供更强的路由，并减少 handler 内部分支。

## VitalRouter

Handler 可以使用 VitalRouter source-generated route：

```csharp
using System.Threading;
using CycloneGames.Cheat.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VitalRouter;

[Routes]
public partial class DebugWorldCheatHandler : MonoBehaviour
{
    private void Awake()
    {
        MapTo(Router.Default);
    }

    private void OnDestroy()
    {
        UnmapRoutes();
    }

    [Route]
    private async UniTask OnReload(CheatCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId != "World_ReloadConfig")
        {
            return;
        }

        await UniTask.Yield(cancellationToken);
    }
}
```

当命令不应跨系统泄漏时，为不同工具、场景、world 或权限边界使用专用 `Router`。

## Build 与 CI

`BuildData` 包含 `Cheat Build Mode`：

| 模式 | 行为 |
| --- | --- |
| `Disabled` | `BuildScript` player 构建期间移除 `ENABLE_CHEAT`。 |
| `DevelopmentBuilds` | 只在 debug/development 构建中启用 `ENABLE_CHEAT`。 |
| `Enabled` | 所有 `BuildScript` 构建都启用 `ENABLE_CHEAT`。仅用于受保护的内部构建。 |

CI 可以用 `-enableCheat` 或 `-disableCheat` 覆盖资产设置。Build 支持与本包刻意解耦：Build 模块只使用字符串 symbol、反射和 Unity 编译元数据。它检测的是 `CycloneGames.Cheat.Runtime` assembly 契约，而不是任何 package 路径，因此模块可以位于 `Assets`、嵌入式 UPM package、package cache 或其他 Unity 支持的源码位置。如果 player 编译域中不存在 runtime assembly，Build 不会应用 `ENABLE_CHEAT`，普通打包会继续执行。

## 持久化

Cheat 模块不会写 runtime 文件、存档、偏好、缓存或资产。它只持有内存中的命令状态和 logger 引用。GM 控制台历史、审计记录、远程授权或跨设备同步应由具体产品或 live-ops 层实现，并具备显式存储、schema version、访问控制和迁移策略。

## 验证

最小验证步骤：

1. 运行 `CycloneGames.Cheat.Tests.Editor`。
2. 在未定义 `ENABLE_CHEAT` 时编译一次，确认 runtime 测试覆盖 no-op 路径。
3. 在定义 `ENABLE_CHEAT` 时编译一次，确认命令能通过 VitalRouter 路由。
4. 使用 `Cheat Build Mode = Disabled` 执行一次 BuildScript player 构建，确认原始 scripting defines 被恢复。
5. 分别使用 `-enableCheat` 和 `-disableCheat` 做 CI dry-run；两个参数同时传入时应在 player build 前失败。
