# CycloneGames.Utility

[English | 简体中文](README.md)

一组小型 Unity 工具：常见数量单位格式化、集合操作、颜色与向量运算、安全区域适配、经 authoring 的 Transform 查找、FPS 诊断、singleton，以及少量 Editor drawer。各 helper 相互独立，添加 asmdef 引用后按需使用即可，不需要初始化任何框架。

## 依赖

| Package | 用途 |
| --- | --- |
| `com.cyclone-games.hash` | Transform 键查找 |
| `com.cyclone-games.logging` | 诊断日志；未安装 backend 时默认使用 `NullLogWriter` |
| `com.unity.ugui` | `EmptyGraphic`、安全区域与 FPS 组件 |

版本声明见 `package.json`。

| 程序集 | 平台 | `autoReferenced` | 直接引用 |
| --- | --- | ---: | --- |
| `CycloneGames.Utility.Runtime` | 所有 Unity target | `true` | `CycloneGames.Hash.Core`、`CycloneGames.Logging.Core`、`UnityEngine.UI` |
| `CycloneGames.Utility.SplashScreenModifier.Runtime` | 所有 Unity target | `true` | 无 |
| `CycloneGames.Utility.Editor` | 仅 Editor | `true` | `CycloneGames.Utility.Runtime`、`CycloneGames.Logging.Core` |

```csharp
using CycloneGames.Utility.Runtime;
```

诊断使用 `LogChannel` category `CycloneGames.Utility` 和 `CycloneGames.Utility.Editor`。每个产生诊断的 asmdef 都在 `Diagnostics/` 下持有 internal `<FeatureName>Log` facade。

## 快速上手

三个不需要场景配置的例子：

```csharp
// 按固定区域性写入调用方持有的缓冲区，不产生中间字符串。
Span<char> buffer = stackalloc char[64];
if (FormatUtil.TryFormatBytes(1_572_864, buffer, out int written, decimalPlaces: 1))
{
    ConsumeText(buffer.Slice(0, written)); // "1.5 MB"
}

// 顺序无关时的 O(1) 移除。
if (activeProjectiles.TrySwapRemoveAt(index))
{
    // index 现在是原来的最后一个元素。
}

// Hex 解析与 WCAG 对比度。
if (Colors.TryParseHex("#4A90E2FF".AsSpan(), out Color color))
{
    float contrast = color.GetContrastRatio(Color.black);
}
```

## Runtime 工具

### 常见数量单位格式化

`FormatUtil` 和 `MemoryFormatExtensions` 使用 `CultureInfo.InvariantCulture`，输出不受当前语言或地区设置影响，同一输入在不同机器上得到相同字符串。字符串 API（`FormatBytes`、`FormatNumber`、`FormatDuration`、`FormatPercent`）分配返回的字符串；`Try*` 重载写入调用方持有的 `Span<char>`，输入非法或容量不足时返回 `false`。`AppendFormatted*` 扩展避免中间字符串，但 `StringBuilder` 扩容仍会分配。

| 规则 | 取值 |
| --- | --- |
| 小数位 | `[0, 5]` |
| 字节数 | 非负，1024 进制单位 `B`、`KB`、`MB`、`GB`、`TB` |
| 数值 | 1000 进制后缀 `K`、`M`、`B`、`T` |
| 百分比比例 | 有限且位于 `[0, 1]` |
| 时长 | 有限，且能放进 `Int64` 个整秒 |
| 抛异常的字符串 API | 契约非法时抛 `ArgumentOutOfRangeException` |

每帧更新的 HUD 应写入可复用的 `Span<char>`，只在显示值变化时调用 `ToString()`。`FormatPercent` 每次调用分配一个字符串：

```csharp
float ratio = totalBytes > 0 ? bytesDownloaded / (float)totalBytes : 0f;
string percent = FormatUtil.FormatPercent(ratio, decimalPlaces: 0); // "73%"
```

### 集合操作

`CollectionUtils` 提供有边界检查的访问（`TryGetElementAtIndex`、`TryFirst`、`TryLast`、`TryPop`），针对 `List<T>`、数组、`Dictionary`、`HashSet` 的 `IsNullOrEmpty`，字典 `GetOrDefault`，以及 `LinkedList` 节点辅助方法。

swap-remove 是 O(1) 且不保持顺序。数组重载需要调用方持有逻辑计数，并清空已释放的槽位以避免保留引用：

```csharp
public void DespawnAt(int index)
{
    if (!active.TrySwapRemoveAt(index))
    {
        return; // 该 index 本帧已经失效
    }

    // index 现在是原来的最后一个元素；更新所有缓存的索引。
}
```

`Shuffle` 是原地 Fisher-Yates。默认 RNG 按线程惰性创建；需要可复现结果时传入带种子的 `System.Random`。`System.Random` 不是网络协议或跨运行时的确定性保证，replay 与 lockstep 应使用项目自带的版本化 simulation RNG。可变操作不是线程安全的。

### 颜色

`Colors` 提供命名颜色、hex 解析与格式化、稳定的 `0xRRGGBBAA`、BT.601 亮度和 WCAG 相对亮度/对比度。`GetLuminance` 使用 BT.601 加权亮度，无障碍判断应使用 `GetRelativeLuminance` 和 `GetContrastRatio`。对比度计算忽略 alpha，因此半透明颜色要先合成。`Colors.RandomColor(System.Random)` 使用调用方持有的 RNG 状态；无参重载消费 Unity 全局随机状态，仅适用于主线程、非确定性的展示路径。

### 向量辅助

`Vector3Extensions` 提供有限输入辅助方法，对非法区间有明确行为。这些是 float 辅助方法，不构成确定性模拟边界。

### 安全区域

`SafeAreaUtility.CalculatePixelRect` 是纯像素与 anchor 计算，无需场景即可测试：

```csharp
var policy = new SafeAreaPolicy(
    extendIntoBottomSafeArea: false,
    enforceVerticalSymmetry: true,
    enforceHorizontalSymmetry: true,
    paddingPixels: new Vector4(x: 8, y: 8, z: 8, w: 8));

Rect usablePixels = SafeAreaUtility.CalculatePixelRect(
    Screen.safeArea,
    Screen.width,
    Screen.height,
    in policy);
```

`Vector4` padding 顺序为左、下、右、上。非有限或负值按零处理；padding 过约束时按比例缩放，保证 anchor 不会反转。先处理底部扩展，再做顶部 inset 平衡，后者可能补回底部 inset 以平衡顶部挖孔。

把 `AdaptiveSafeAreaFitter` 挂到 `RectTransform` 上使用驱动式组件。它持有 `DrivenRectTransformTracker`，缓存上一次屏幕状态，仅在屏幕状态、配置、层级尺寸或驱动值变化时重算。竖屏手机上 `enforceVerticalSymmetry: true` 会为顶部挖孔补上等量的底部 inset，让布局保持居中；横屏时 `enforceHorizontalSymmetry: true` 会镜像侧边挖孔。

### Transform 键注册表

`TransformKeyRegistry` 把 authoring 的字符串键映射到 Transform。初始化阶段建立索引后再查询：

```csharp
registry.BuildIndex(); // 冷路径，放在初始化阶段

if (registry.TryGetTransform("Weapon.Muzzle", out Transform muzzle))
{
    SpawnAt(muzzle);
}
```

行为说明：

- 空键和缺失的 Transform 引用会被忽略。第一个有效的重复键胜出，后续重复项被计数并忽略。
- 不超过 16 条索引项时用线性查找，更多时用 hash 排序数组加二分查找。字符串查找在 hash 之后还会校验 ordinal 键；纯 hash 查找遇到不同键碰撞时返回 `false`。
- 构建时用迭代式深度优先遍历展平 active 且 enabled 的嵌套注册表；禁用或未激活的注册表不参与。
- 关闭 `Build On Awake` 时，首次查找会触发构建，产生分配并扫描层级。可选的 `Transform.Find` 回退同样是冷路径。
- 键是 authoring 字符串，不是网络 ID、存档 ID、Unity 实例 ID 或 ECS 实体标识。

Editor 的 `Collect Direct Children` 操作通过 `SerializedObject` 补充缺失的直接子条目，保留 Unity 的 Undo 与 Prefab Override 行为。

### FPS 诊断

把 `FPSCounter` 挂到项目自己持有的诊断 GameObject 上，通过直接引用调用 `SetVisible` 或设置 `IsVisible`。它不会查找或创建全局实例。移动平均使用固定容量的样本环。仅瞬时值和仅平均值模式会惰性缓存数值字符串，组合模式在显示更新时可能分配拼接字符串。IMGUI 本身有引擎开销，因此这是诊断工具而非生产 HUD。`Persist Across Scenes` 会对整个 GameObject 调用 `DontDestroyOnLoad`，请使用不挂其它组件的专用对象。

### Singleton 与 MonoSingleton

`Singleton<T>` 是 CLR 惰性构造辅助。构造和发布是线程安全的，`T` 内部的可变状态不是。实例生命周期跟随 AppDomain，没有重置、取消、`Dispose` 或关闭顺序。适用于小型、无参、且不持有 Unity 对象、订阅、线程、原生句柄或会话状态的对象。

```csharp
public sealed class TelemetryNames : Singleton<TelemetryNames>
{
    public string BuildFlavor { get; } = "production";
}

string flavor = TelemetryNames.Instance.BuildFlavor;
```

`MonoSingleton<T>` 是另一个仅限主线程的 Unity 便利类型。`Instance` 先解析场景中已存在（active 或 inactive）的组件，否则在 Play Mode 下创建专用 GameObject。`TryGetInstance` 只返回已注册的缓存，不查找也不创建。存在多个候选时直接失败，不会随机选一个。重复组件会被禁用并销毁，不会删除其 GameObject 或同级组件。静态注册在 `SubsystemRegistration` 时清空，关闭 Domain Reload 时同样有效。派生类覆写生命周期方法时必须调用基类实现。

全局的 `MonoSingleton<T>` 必须挂在根 GameObject 上，否则实现会记录错误并保持 Scene 生命周期，不会移动无关的层级。带依赖、持有资源、需要有序关闭或按场景/世界多实例的服务应使用显式构造。

## Editor 工具

### 字符串常量选择器

用常量类提供候选值，序列化的仍然是普通字符串：

```csharp
public static class ControlPaths
{
    public const string Keyboard = "<Keyboard>";
    public const string Gamepad = "<Gamepad>";
}

[StringAsConstSelector(typeof(ControlPaths), UseMenu = true, AllowCustom = true)]
[SerializeField] private string controlPath;
```

drawer 每个常量类型和 domain reload 反射一次 public `const string` 字段，按 ordinal 排序名称，忽略 null/空常量，重复值保留第一个字段。`UseMenu` 按字段名分隔符（`Separator`，默认 `_`）生成层级菜单路径。`AllowCustom` 保留来自开放外部契约的值。多对象编辑使用 `SerializedObject`/`SerializedProperty`，保留 Undo 与 Prefab Override 行为。

### 属性分组

Runtime attribute 描述一个顶层序列化字段区间：

```csharp
[PropertyGroup("Timing", groupAllFieldsUntilNextGroupAttribute: true, groupColorIndex: 24)]
[SerializeField] private float delay;
[SerializeField] private float duration;

[EndPropertyGroup]
[SerializeField] private bool playOnEnable;
```

为目标准备一个 Editor adapter 即可启用渲染：

```csharp
[CustomEditor(typeof(MyComponent), true)]
[CanEditMultipleObjects]
internal sealed class MyComponentEditor : PropertyGroupInspectorDrawer
{
}
```

渲染器遵循 Unity 顶层 `SerializedProperty` 顺序。连续分组在标记了 `EndPropertyGroup` 的字段之前、下一个 `PropertyGroup` 处或对象末尾结束；非连续分组只包含带该 attribute 的字段。同名分组彼此独立。数组、列表、嵌套可序列化对象和托管引用可作为整体顶层属性分组，但嵌套序列化类型内部的 attribute 不会递归解析。元数据非法或有歧义时按普通方式绘制该属性，不会隐藏序列化数据。

没有全局兜底 Editor，因此不会拦截无关的 Unity 和第三方 Inspector。折叠状态属于当前 Editor 实例，初始值来自 `ClosedByDefault`，不做持久化。所有编辑都通过 `SerializedObject`/`SerializedProperty` 完成。

### 自定义 Inspector 与颜色预览

`FPSCounter`、`AdaptiveSafeAreaFitter` 和 `TransformKeyRegistry` 有各自的自定义 Inspector，带缓存样式、分区、多对象支持和序列化编辑。通用成对字段渲染器只通过 `PropertyGroupInspectorDrawer` 暴露，其遍历逻辑、元数据缓存和视觉辅助保持 internal。

颜色预览在 Editor 冷路径反射 Runtime 的 public color 字段得到颜色名，Player 构建不携带重复的颜色名表。`Tools/CycloneGames/Open Persistent Data Path` 使用 `EditorUtility.RevealInFinder`，必要时创建目录，失败时给出 Editor 对话框和日志。

## 启动画面子模块

`Runtime/Scripts/SplashScreenModifier/` 是物理隔离、按存在即生效的程序集，不需要调用点。安装本包后会在 `BeforeSplashScreen` 注册 `SkipUnitySplashScreen`，Player 构建请求 `SplashScreen.Stop(StopImmediate)`；Editor 与 Dedicated Server 构建为空操作。该程序集使用 `AlwaysLinkAssembly`，确保 Unity linker 处理它的 runtime-init root。`SplashScreen.Stop` 需要许可支持。

## 性能说明

| 路径 | 分配行为 |
| --- | --- |
| `TryFormat*` 目标缓冲区 API | 缓冲区由调用方持有，不产生结果字符串。 |
| 字符串格式化 API | 分配一个作为返回值的托管字符串。 |
| 集合辅助 | 不引入隐藏锁。`ClearAndResize` 扩容时会分配；默认 shuffle 按线程惰性创建一个 RNG。 |
| 安全区域计算 | 纯 struct 计算，无全局缓存。 |
| Transform 注册表构建 | 组件持有数组、可复用键集合和迭代遍历缓冲；容量或层级变化会分配。峰值容量在组件生命周期内保留，构建后清空引用。 |
| Transform 注册表查找 | 有效显式构建之后不会为重建分配；`n <= 16` 时 O(n)，否则 O(log n + 碰撞)。 |
| FPS counter | 组件持有有界样本数组和 GUI 缓存；组合文本与 IMGUI 可能分配。 |
| `Singleton<T>.Instance` | CLR 每个闭合类型初始化一次；热访问是一次静态读取。 |
| `MonoSingleton<T>.Instance` | 已注册时的访问不分配结果。首次解析可能分配搜索结果数组、缓存名、GameObject 和组件。 |
| Editor drawer | 反射/菜单数据缓存到 domain reload；打开菜单是冷路径分配。 |
| 属性分组 | 每个 Editor domain 反射一次类型/字段元数据；每个 Inspector 只持有折叠状态。 |

把某个 helper 放进热路径前，先在目标 backend 和硬件上实测。

## 线程与平台说明

- Unity 对象、`Screen`、IMGUI、UGUI、`Transform` 和 Editor API 在本包中都限定为主线程。
- `Singleton<T>` 的构造与发布遵循 CLR 静态初始化语义，包括 WebGL 的单线程运行时；可变对象本身不会因此变成线程安全。
- `MonoSingleton<T>` 限定主线程，且只在 Play Mode 创建。其防护会阻止工作线程访问 Unity API。
- 集合辅助不声明线程安全。可变数据跨线程时，由持有者围绕所有访问、容量、取消和关闭定义一套同步策略。
- Runtime 程序集不含后台任务、原生插件、unsafe 块、运行时反射发现或动态代码生成。
- Splash 子模块在启动回调线程上同步调用 Unity，WebGL 没有依赖线程的回退路径。
- Dedicated Server 构建不应挂载 UGUI、安全区域或 IMGUI 诊断组件。Splash 回调在 `UNITY_SERVER` 下是编译期空操作。

本包不会自动创建或写入偏好、设置、缓存索引、存档、注册表键、plist 值或独立的项目配置资产，也不使用 `EditorPrefs`、`PlayerPrefs` 或 `SessionState`。Inspector 编辑通过 `SerializedObject` 写入 Unity 序列化的 authoring 变更。`TransformKeyRegistry`、`FPSCounter` 和 `AdaptiveSafeAreaFitter` 含 Unity 序列化字段，重命名字段在声明处使用 `FormerlySerializedAs`。

## 故障排查

| 现象 | 常见原因 | 处理 |
| --- | --- | --- |
| `TryFormat*` 返回 `false` | 输入非法或目标缓冲区太小 | 检查小数位在 `[0, 5]`、字节数非负、比例在 `[0, 1]`；增大缓冲区。 |
| `Singleton<T>` 跨会话保留旧状态 | AppDomain 未重新加载 | 会话生命周期的数据改用显式构造。 |
| `MonoSingleton<T>` 报根 GameObject 错误 | 全局组件挂在非根 GameObject 上 | 移到根 GameObject；按场景使用时把 `IsGlobal` 设为 `false`。 |
| `TransformKeyRegistry.TryGetTransform` 对已知键返回 `false` | 索引未构建，或发生不同键 hash 碰撞 | 初始化时调用 `BuildIndex()`，确认注册表处于 active 且 enabled。 |
| 首次查找 Transform 注册表很慢 | 关闭了 `Build On Awake`，首次查找触发构建 | 加载阶段显式构建，或接受这次一次性开销。 |
| `AdaptiveSafeAreaFitter` 旋转后不更新 | 组件被禁用，或层级尺寸未变化 | 确认组件启用；改变屏幕状态或配置以触发重算。 |
| `FPSCounter` 每帧分配 | 组合显示模式叠加 IMGUI 渲染 | 切到仅瞬时值或仅平均值模式。 |
| 属性分组不显示 | 目标 Editor 未继承 `PropertyGroupInspectorDrawer` | 添加继承它的自定义 Editor。 |
| 设备上启动画面未被跳过 | 许可不允许 `SplashScreen.Stop`，或注册类型被裁剪 | 确认许可；检查 `AlwaysLinkAssembly` 是否保留；查看目标 Player 日志。 |
| `StringAsConstSelector` 丢失自定义值 | `AllowCustom` 为 `false` | 设为 `true` 以保留开放外部契约的值。 |
| Player 构建中颜色预览为空 | 颜色预览仅用于 Editor | 直接通过 Runtime 的 `Colors` 类访问。 |

## 运行测试

| 程序集 | 平台 |
| --- | --- |
| `CycloneGames.Utility.Tests.Editor` | EditMode |
| `CycloneGames.Utility.Tests.PlayMode` | PlayMode |
| `CycloneGames.Utility.Tests.Performance` | Editor，需安装 `com.unity.test-framework.performance >= 3.0.0` |

```text
<UnityEditor> -batchmode -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -assemblyNames CycloneGames.Utility.Tests.Editor -testResults <result-path> -quit
```
