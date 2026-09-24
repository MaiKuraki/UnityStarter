# TextMeshPro 兼容性

[English | 简体中文](TextMeshProCompatibility.md)

TextMeshPro 是本包的可选 capability，不是包依赖。本文记录 Unity 各分支如何分发 TextMeshPro、capability 符号如何派生，以及 TextMeshPro 缺失时会发生什么。

## 为什么需要 capability 符号

| Unity 分支 | TextMeshPro 的分发方式 | `com.unity.textmeshpro` | 程序集名 |
| --- | --- | --- | --- |
| Unity 2022 LTS 及更早 | 独立 UPM 包 | 存在于 `Packages/manifest.json`；Package Manager 可增删 | `Unity.TextMeshPro` |
| Unity 2023.2 与 Unity 6（6000.0+） | 合并进 uGUI，随 `com.unity.ugui` 2.0.0 分发 | 不存在；Package Manager 无法安装或移除 | `Unity.TextMeshPro` |

关键的不对称出现在 Unity 6 分支。由于 `com.unity.textmeshpro` 在那里不再是可解析的包，针对该 id 的 `versionDefines` 规则会静默停止命中。只依赖该规则的集成会在 Unity 2022 上编译，而在 Unity 6 上静默消失——尽管 Unity 6 上 TextMeshPro 必然存在。

两条分支上的托管程序集名保持不变，因此 asmdef 中对 `Unity.TextMeshPro` 的 `references` 条目仍然可以解析。另外 Unity 把无法解析的程序集引用当作软引用：缺少该程序集不会导致导入失败，只会缩小可解析的类型范围。

## capability 符号

`CYCLONEGAMES_HAS_TEXTMESHPRO` 是正向 capability 符号。它由每个程序集按两条 `versionDefines` 规则派生，一条对应一个 Unity 分支：

```json
"versionDefines": [
    {
        "name": "com.unity.textmeshpro",
        "expression": "",
        "define": "CYCLONEGAMES_HAS_TEXTMESHPRO"
    },
    {
        "name": "com.unity.ugui",
        "expression": "2.0.0",
        "define": "CYCLONEGAMES_HAS_TEXTMESHPRO"
    }
]
```

| 规则 | 命中条件 | 含义 |
| --- | --- | --- |
| `com.unity.textmeshpro`，空表达式 | 独立包的任意版本 | Unity 2022 LTS 及更早 |
| `com.unity.ugui` `2.0.0` | uGUI 2.0.0 或更高 | Unity 6 及以后；TextMeshPro 已内建于 uGUI |

空表达式表示"任意版本"。单独的裸版本号表示"该版本或更高"。

两条规则是**或（OR）关系，不是与（AND）**。Unity 独立求值每一条 `versionDefines` 规则，任意一条命中即定义该符号。命中一条就够；两条都命中也没问题；两条都不命中则符号未定义、程序集被排除。在 Unity 2022.3 + uGUI 1.0.0 + TextMeshPro 3.0.9 上实测：只有第一条规则命中，符号仍然定义，TextMeshPro 切片正常编译。

由此得到的真值表：

| uGUI | `com.unity.textmeshpro` | 符号 | 结果 |
| --- | --- | --- | --- |
| 1.0.0 | 存在（任意版本） | 已定义 | TextMeshPro 切片编译。这是 Unity 2022 LTS 的正常情况 |
| 1.0.0 | 不存在 | 未定义 | TextMeshPro 切片被排除；binder 继续工作 |
| 2.0.0+ | 存在 | 已定义 | 两条规则都命中。正常编译 |
| 2.0.0+ | 不存在 | 已定义 | Unity 6 及以后。正常编译，TextMeshPro 来自 uGUI |

Unity 2022 上配 uGUI 2.0.0 不是受支持的配置：`com.unity.ugui` 是随编辑器版本锁定的内置包，2.0.0 声明的 Unity 最低版本是 2023.2。强行写入 manifest 会得到无法满足的依赖。万一这种配置真的解析成功，第二条规则会命中，模块依旧编译——因为规则之间是或关系。

Owner：声明这些规则的 asmdef。作用域：仅限该程序集。不要把 `CYCLONEGAMES_HAS_TEXTMESHPRO` 加进 PlayerSettings 的 scripting define symbols；残留的手工符号会在缺少 TextMeshPro 时强制编译并导致构建失败。

## 消费该符号的程序集

| 程序集 | `defineConstraints` | 内容 |
| --- | --- | --- |
| `CycloneGames.UIFramework.Runtime.Integrations.Localization.TextMeshPro` | 是 | `UILocaleLayout`、`TrackedElement`、`ElementSnapshot`、`LocaleSnapshot` |
| `CycloneGames.UIFramework.Editor.Integrations.Localization.TextMeshPro` | 是 | `UILocaleLayoutEditor`、`LocalizeContextMenu` |
| `CycloneGames.UIFramework.Tests.Editor.Integrations.Localization` | 是 | 语言布局测试 |
| `CycloneGames.UIFramework.Tests.Editor` | 否（文件级 `#if`） | `TemplateProcessor_RemovesPlaceholderWindowAndUpdatesPreferredTmpTitle` |
| `CycloneGames.Localization.Components.TextMeshPro` | 是 | `LocalizeTMPText` |

`CycloneGames.UIFramework.Editor.Integrations.Localization` 中的 `UIFrameworkLocalizationEditorLog` facade 保持 internal，与本仓库所有 log facade 一致。由于 TextMeshPro Editor 切片是独立程序集，该程序集在自己的 `AssemblyInfo.cs` 中声明 `InternalsVisibleTo("CycloneGames.UIFramework.Editor.Integrations.Localization.TextMeshPro")`。缺少这条声明时切片会以 `CS0122` 失败。

必须在没有 TextMeshPro 时继续存活的程序集不持有编译期 TextMeshPro 依赖：

- `CycloneGames.UIFramework.Runtime.Integrations.Localization` —— `LocalizationWindowBinder`。
- `CycloneGames.UIFramework.Editor.Integrations.Localization` —— 共享的 `UIFrameworkLocalizationEditorLog` facade。
- `CycloneGames.UIFramework.Editor` —— `UIWindowCreatorWindow` 与 `UIWindowTemplateProcessor` 通过类型名字符串匹配 TextMeshPro，不做类型引用，因此不需要该程序集。
- `CycloneGames.Localization.Components` —— `LocalizeImage`。

## TextMeshPro 缺失时的行为

1. 表中标注 `defineConstraints` 的每个程序集都被排除编译。不报错，也不会出现缺包警告。
2. Localization 窗口 binder 仍然编译，仍然绑定窗口层级中的每一个 `ILocalizationBindingTarget`。
3. `UILocaleLayout` 不再作为组件存在。任何挂过它的 prefab 或场景会报 missing script，其序列化快照数据不会被加载。这是一条数据丢失路径：先安装 TextMeshPro 再打开这类资产，或者先备份。
4. `LocalizeTMPText` 不可用；字符串本地化仍然通过图片、音频与自定义绑定目标工作。

## 验证步骤

1. Unity 2022 LTS：确认 `Packages/manifest.json` 中存在 `com.unity.textmeshpro`。
2. Unity 6：确认 `com.unity.ugui` 解析到 2.0.0 或更高。`com.unity.textmeshpro` 必须不存在；它的缺失是预期行为，不是故障。
3. 两条分支上都在 Inspector 中打开对应 asmdef，读取两条规则的 *Expression outcome* 字段。应当至少有一条规则显示命中；两条都命中也是正常的。
4. 确认 `Library/ScriptAssemblies/` 中存在 `CycloneGames.UIFramework.Runtime.Integrations.Localization.TextMeshPro.dll`。

## 故障排查

| 现象 | 原因 | 修复 |
| --- | --- | --- |
| Unity 2022 上有语言布局脚本，Unity 6 上没有 | 只写了 `com.unity.textmeshpro` 规则 | 补上 `com.unity.ugui` `2.0.0` 规则 |
| `error CS0246: The type or namespace name 'TMPro' could not be found` | 在 TextMeshPro 缺失时手工设置了 `CYCLONEGAMES_HAS_TEXTMESHPRO` | 删除手工符号，交给 `versionDefines` 派生 |
| `UILocaleLayout` 报 missing script | TextMeshPro 切片被排除 | 安装 TextMeshPro；Unity 6 上确认 uGUI 为 2.0.0 或更高 |
| 某个 asmdef 在控制台提示 `Unity.TextMeshPro` 警告 | 对缺失程序集的软引用 | 预期行为。该引用无害，因为 `defineConstraints` 会排除该程序集 |
