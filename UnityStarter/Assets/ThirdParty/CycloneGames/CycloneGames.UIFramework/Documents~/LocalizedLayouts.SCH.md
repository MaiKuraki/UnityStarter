# 本地化 UI 布局

[English](LocalizedLayouts.md) | 简体中文

本地化 UI 布局在已提交的语言发生变化时，对 UI Prefab 应用按语言区分的几何与排版覆盖。集成将这些视觉差异保存在 Prefab 内并按确定性方式应用，不会替代本地化字符串、Sprite、字体或资源解析。

## 目录

- [概述](#概述)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [进阶主题](#进阶主题)
- [故障排查](#故障排查)

## 概述

本地化内容可能改变 UI 所需空间和阅读方向。`UILocaleLayout` 将这些视觉差异保存为按语言区分的 snapshot，并只在语言变化时应用，面向由 `UIService` 管理的 `UIWindow` Prefab 和由产品层显式组合的独立 Canvas。

### 主要特性

- **按语言区分的 snapshot**：`RectTransform` 的 anchor、pivot、position、size、scale；TMP font metric、alignment、RTL 状态；以及 `LayoutGroup.childAlignment`。
- **确定性回退**：精确语言、language-only、Prefab base；每次切换都会先恢复 base 再应用 override。
- **窗口作用域绑定**：`LocalizationWindowBinder` 将 binding target 限定在窗口会话内并按逆序释放。
- **Authoring 支持**：通过 `SerializedProperty` 与 Undo-aware 编辑提供 capture、validation、自动恢复的临时 Preview 和 schema normalize。
- **事件驱动更新**：语言变化由事件驱动，不存在逐帧层级扫描。

### 快速上手

**1. 配置 Localization**

通过 Localization authoring 流程创建产品的 `LocalizationSettings` 与 `Locale` 资产。在 application 或 session scope 中初始化唯一的 `ILocalizationService`。必须在创建任何 UI binding 前完成初始化。

```csharp
var localization = new LocalizationService();
localization.Initialize(localizationSettings.ToOptions());
```

**2. 注册 Window Binder**

创建一个 `LocalizationWindowBinder`，并与其他 window binder 一起传给 `UIService`。受支持的组合顺序为：创建并初始化 `ILocalizationService`、在 Unity 主线程构造 `LocalizationWindowBinder`、将该 binder 传入并构造 `UIService`、打开 Window 或绑定独立 target。

```csharp
IUIWindowBinder[] binders =
{
    new LocalizationWindowBinder(localization, assetPackage),
};

var uiService = new UIService(
    uiRoot,
    assetProvider,
    options: uiOptions,
    binders: binders);
```

**3. 添加布局 Component 并跟踪元素**

将 `UILocaleLayout` 添加到 `UIWindow` Prefab 根节点，或添加到持有本地化区域的稳定子节点。可使用以下 component context menu 跟踪元素：

- `TMP_Text > Track Locale Layout`
- `Image > Track Locale Layout`
- `RectTransform > Track Locale Layout`
- `LayoutGroup > Track Locale Layout`

如果没有 `UILocaleLayout`，该 action 会询问是否添加到最近的 `UIWindow` 根节点或 Canvas 根节点。它会记录 Undo、安全更新平行 snapshot 数据、保留 Prefab Override，并选中布局 component。

## 核心概念

### 数据模型

一个 `UILocaleLayout` 拥有三组序列化数据：

| 数据 | 含义 |
| --- | --- |
| Base locale | Prefab 当前 authoring 层级所代表的语言，不需要单独保存 override snapshot。 |
| Tracked elements | 按稳定顺序保存 `RectTransform` 引用，以及可选的 TMP 文本和 `LayoutGroup` 引用。 |
| Locale snapshots | 按 tracked elements 相同索引顺序保存的各语言布局值。 |

Tracked-element 顺序属于序列化契约。添加、移除、清理或重排元素时，应使用 Inspector action，使所有 snapshot 与该顺序保持一致。

### 捕获的属性

| Component | 捕获属性 |
| --- | --- |
| `RectTransform` | `anchorMin`、`anchorMax`、`pivot`、`anchoredPosition`、`sizeDelta`、`localScale` |
| `TMP_Text` | `fontSize`、`lineSpacing`、`characterSpacing`、`alignment`、`isRightToLeftText` |
| `LayoutGroup` | `childAlignment` |

除 `RectTransform` 外，其他引用均为可选。没有 TMP 文本的 tracked entry 不修改文本设置；没有 `LayoutGroup` 的 entry 不修改 child alignment。

### Locale Fallback

布局查找使用小型确定性 fallback chain：

1. 精确语言 override；
2. language-only override；
3. Prefab base layout。

Base locale 为 `en` 时的示例：

| 请求语言 | 可用 override | 结果 |
| --- | --- | --- |
| `ja-JP` | `ja-JP`、`ja` | `ja-JP` |
| `ja-JP` | `ja` | `ja` |
| `en-US` | 无 | Prefab base layout |
| `de-DE` | 无 | Prefab base layout |

比较采用 ordinal、忽略大小写的方式，提高 authoring 容错性。如果 snapshot 的 value 数量少于 tracked elements，或者当前 schema entry 没有捕获值，未匹配元素会使用其 base layout。语言切换不会让未匹配元素残留前一个语言的几何数据。

## 使用指南

### 制作语言 Override

Prefab 层级就是 base-locale 布局。先完成该布局，再为确实不同的语言创建 override。

1. 将 `Base Locale` 设置为 Prefab 当前 authoring 语言。
2. 通过 `Add from Localization Settings` 添加语言，或手动输入 BCP 47 风格代码。
3. 在 `Editing Locale` 中选择 override。
4. 点击 `Apply for Editing`。
5. 在常规 Inspector 中调整 tracked `RectTransform`、TMP 文本和 `LayoutGroup` 属性。
6. 检查差异状态。
7. 点击 `Capture Current Hierarchy`。
8. 对其他语言重复以上步骤。

`Capture Current Hierarchy` 会为选定语言写入完整的当前 schema snapshot。它是显式 authoring commit point；普通 Scene 变化不会被静默复制到语言数据。

### Preview

`Preview Snapshot` 使用 Unity `AnimationMode`，并在应用临时值前注册每个受影响的序列化属性。Preview 不会创建 Undo 记录、Scene dirty 状态或 Prefab Override。`Exit Preview` 会停止 animation preview 并恢复序列化层级值。

Unity `AnimationMode` 同一时间只能有一个 owner。当 Timeline、Animation Window 或其他工具已持有它时，Locale Preview 会拒绝启动。以下操作之前也会自动恢复 Preview：保存 Prefab、进入 Play mode、Domain reload、Undo 或层级替换、关闭或替换 Inspector。Preview 不是持久化路径；需要保存的 authoring 修改应使用 `Apply for Editing` 和 `Capture Current Hierarchy`。

### 独立 UI 组合

不属于 `UIWindow` 的 locale-aware Canvas 可以显式绑定。`Bind` 与 `Unbind` 只允许主线程调用；对于同一个 service relationship，它们具有幂等行为。已显式绑定的 component 在 disabled 时取消订阅，并在重新 enabled 后应用当前语言。除非明确理解重复投递，否则不要同时使用显式 binding 与 window binder。

```csharp
public sealed class MenuComposition : MonoBehaviour
{
    [SerializeField] private UILocaleLayout localeLayout;

    private ILocalizationService _localization;

    public void Initialize(ILocalizationService localization)
    {
        _localization = localization;
        var context = new LocalizationBindingContext(localization);
        localeLayout.Bind(in context);
    }

    private void OnDestroy()
    {
        if (localeLayout != null)
        {
            localeLayout.Unbind();
        }
        _localization = null;
    }
}
```

### Undo、Prefab Override 与多对象编辑

持久化 Inspector action 会通过 Unity Undo 记录受影响的 component 或 UI object，调用 `SerializedObject.ApplyModifiedProperties()`，并记录 Prefab Instance property modification。工具只会为持久化 action 显式将 Scene 标记为 dirty；Preview 会恢复临时层级值。

同时选中多个 `UILocaleLayout` 时，Inspector 允许安全修改 base locale，但会禁用依赖索引的 element 与 snapshot action。

## 进阶主题

### 自定义 Localization Binding Target

当语言变化需要 snapshot 以外的行为时，Window 子节点可以实现 `ILocalizationBindingTarget`。Target 持有自己的 service subscription，并在 `Unbind` 中释放。`LocalizationWindowBinder` 在 window binding 时发现 binding target 一次，并在该 window lifetime 内保存结果列表。它按 hierarchy 顺序 Bind；任意 target 失败时，会从失败 target 开始逆序 `Unbind`。每个 target 必须让 `Unbind` 保持 idempotent，并确保 Unity mutation 位于 main thread。

```csharp
public sealed class LocaleSpecificIconPolicy : MonoBehaviour, ILocalizationBindingTarget
{
    private ILocalizationService _localization;

    public void Bind(in LocalizationBindingContext context)
    {
        Unbind();
        _localization = context.Localization;
        _localization.Changed += HandleLocalizationChanged;
        Apply(_localization.CurrentLocale);
    }

    public void Unbind()
    {
        if (_localization != null)
        {
            _localization.Changed -= HandleLocalizationChanged;
            _localization = null;
        }
    }

    private void HandleLocalizationChanged(LocalizationChange change)
    {
        Apply(change.CurrentLocale);
    }

    private void Apply(LocaleId locale)
    {
        // 应用功能专属、仅主线程的 presentation policy。
    }
}
```

加载本地化资产的 target 通过 `LocalizationBindingContext` 接收可选 `IAssetPackage`；它拥有 binding lifetime 内取得的 cancellation 与 handle。Locale commit ordering 与 reentrancy 由 `LocalizationService` 负责。Presentation target 只观察 committed、带 revision 的 `LocalizationChange`。

### RTL 与对齐行为

对于 tracked TMP 文本，snapshot 同时保存 `alignment` 和 `isRightToLeftText`。对于 tracked `LayoutGroup`，snapshot 保存 `childAlignment`。这些属性能在不使用运行时反射的前提下覆盖常见阿拉伯语、希伯来语及混合方向 authoring 流程。

本集成不会反转任意 hierarchy 顺序、导航顺序、动画方向或产品特定语义布局。如果某个区域需要结构镜像，应由该功能持有专用可选 component 或独立 Prefab variant。每个布局属性必须只有一个 authority，避免 Unity layout component、Animation 与 locale snapshot 持续互相覆盖。

### 序列化与 Schema 兼容

Component 会在 Prefab 或 Scene 中序列化 base locale、tracked elements 和 locale snapshots。它不会写入 `EditorPrefs`、`PlayerPrefs`、`SessionState`、registry 或独立 cache file。

| Schema 状态 | Runtime 行为 |
| --- | --- |
| 当前 schema | 应用全部已捕获的几何、TMP、RTL 和 layout-group 值；没有捕获值的 entry 使用 base layout。 |
| Schema `0` | 先恢复当前 schema 所含属性的 base 值，再应用 font size、line spacing、character spacing、anchored position 与 size delta。 |
| 尚未支持的未来 schema | Editor validation 报告错误；在模块理解该 schema 前不得发布。 |

Runtime 会把尚未支持的未来 schema 视为不可用，并恢复已捕获的 base layout。Editor 对整个 `UILocaleLayout` 采用 fail-closed：所有编辑 action 均被禁用。

`Migrate and Normalize Snapshots` 是显式 Editor migration action。它会让每个 snapshot 与 tracked-element 数量对齐、保留 schema `0` 的值、从当前层级捕获新增的 anchor/pivot/scale/alignment/RTL 值，并将迁移后的 snapshot 标记为当前 schema。迁移后必须检查每个语言，因为 schema `0` 中不存在的属性来自当前层级。

### Runtime 成本与内存 Owner

`UILocaleLayout` 不包含 `Update`、`LateUpdate`、Coroutine、worker thread、lock 或 polling loop。

| 操作 | 成本与 ownership |
| --- | --- |
| Layout 初始化 | 每个 layout instance 分配一个按 tracked element 数量确定的 base `ElementSnapshot[]`，由该 component 持有。 |
| Window binding | 扫描 `MonoBehaviour` child 一次，分配一个 binding-target list，并按 hierarchy 顺序 Bind。 |
| Locale change | 每个 subscribed target 处理 committed event；每个 layout 只扫描自己的 tracked element，不执行 hierarchy discovery。 |
| Window close/rollback | 按逆序调用 target `Unbind` 并清除 binding 引用。 |

成功的 locale-application loop 不包含有意的 managed allocation。大型 UI Prefab 应只跟踪确实对语言敏感的元素，不要捕获从不变化的装饰。Canvas rebuild 和 Unity layout system 成本取决于 hierarchy 形态，不能只通过 snapshot 数量推断。

### 线程与平台边界

构造、`Bind`、`Unbind`、window binding disposal、Localization mutation，以及所有 Unity UI mutation 都限定在 Unity 主线程。`LocalizationService` 在初始化时捕获 mutation owner，并拒绝 off-owner mutation。它的 immutable lookup snapshot 可并发服务 pure managed query，但本 UI integration 不从 worker thread 访问 Unity object，也不维护第二套 dispatcher 或 lock。

实现仅使用 managed C#、Unity UI、TMP 与显式 asmdef reference，不包含 native plugin、file I/O、dynamic code generation、runtime reflection、unsafe code 或 worker-thread 要求。

## 故障排查

| 现象 | 可能原因 | 解决方法 |
| --- | --- | --- |
| 语言变化更新文本但没有更新几何 | 未注册 `LocalizationWindowBinder`，或独立 UI 未绑定 | 注册 binder；独立 UI 则调用 `Bind` |
| 某语言使用 base geometry | 未捕获 override | 添加 exact 或 language-only override 并捕获 |
| Reorder 按钮被禁用 | schema `0` 或长度不匹配数据，或未来 schema | 运行 `Migrate and Normalize Snapshots` 并逐语言检查；对未来 schema 使用兼容模块版本 |
| 同一元素出现两次 | 重复的 tracked element | 运行 `Remove Missing and Duplicate` |
| Preview 看起来仍然生效 | 未退出 Preview | 选中布局并点击 `Exit Preview`；Prefab 保存、Play mode、reload 与 Inspector 关闭也会恢复 |
| RTL 文本方向正确但 child order 不正确 | 不支持结构镜像 | 制作功能专属结构镜像 |
| Preview 被拒绝 | 其他工具持有 `AnimationMode` | 先停止 Timeline 或 Animation Window preview |
| 未来 schema component 被锁定 | Editor 不理解该 schema | 使用兼容模块版本打开资产，或恢复兼容的 Prefab/Scene revision |

### 验证

运行 `CycloneGames.UIFramework.Tests.Editor.LocalizationIntegrationTests` 与 `CycloneGames.UIFramework.Tests.Editor.LocalizationEditorTests` 的 EditMode test。聚焦套件覆盖 snapshot 应用、language fallback、base restoration、schema `0` 与未来 schema 行为、缺失 value、binding ownership、hierarchy-order bind、reverse-order disposal、off-owner mutation 拒绝、reentrant locale commit ordering、`AnimationMode` preview 恢复与 future-schema authoring guard。

最小 Editor 手动检查：在 Prefab Mode 中打开 UI Prefab、跟踪 TMP 元素、添加两个 locale override、分别应用/编辑/捕获、Preview 各语言并确认自动恢复、对 capture/apply/add/remove/clean/migration 验证 Undo/Redo、实例化 Prefab 并确认 Prefab Override 正确记录、同时选择多个 layout component 确认依赖索引的 action 保持禁用、进入 Play mode 确认布局仅随 locale event 变化一次、并确认 Animation Window 或 Timeline preview 持有 `AnimationMode` 时 Locale Preview 保持禁用。
