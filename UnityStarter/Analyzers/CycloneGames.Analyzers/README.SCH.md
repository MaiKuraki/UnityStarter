# CycloneGames Roslyn Analyzers

`CycloneGames.Analyzers` 是面向 UnityStarter 的 Roslyn Analyzer 模块，用于在编译期约束 Unity Runtime 性能、安全 API、异步模型和框架规范。

## 构建

```bash
cd UnityStarter/Analyzers
dotnet build CycloneGames.Analyzers.sln -c Release
```

输出文件：

```text
CycloneGames.Analyzers/bin/Release/netstandard2.0/CycloneGames.Analyzers.dll
```

## Unity 项目接入

建议通过团队提交的 `Directory.Build.props`、NuGet analyzer package 或 Unity project-generation hook 接入。不要依赖未提交的个人本地配置作为团队和 CI 的唯一启用方式。

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

热路径规则应保持保守。对团队级 Analyzer 来说，少量漏报通常优于大量误报。

## 质量门槛

默认启用一条规则前应满足：

- 语义检查优先，避免只依赖脆弱的字符串匹配。
- 明确 Runtime、Editor、Samples、Tests 的路径行为。
- 覆盖正例和反例测试。
- 对现有 UnityStarter 模块保持低误报。
- 只有在改写对常见 Unity 代码模式安全时才提供 CodeFix。
