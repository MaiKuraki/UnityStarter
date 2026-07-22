# CycloneGames.Persistence.VYaml

[English](README.md) | 简体中文

人类可读 YAML codec，桥接 `CycloneGames.Persistence` 与 VYaml。通过显式传入的 generated resolver 将类型化持久化 DTO 转换为 UTF-8 YAML。Persistence Core 负责 record framing、content version metadata、字节上限、checksum 校验和 storage orchestration。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速开始](#快速开始)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [高级主题](#高级主题)
- [问题排查](#问题排查)

## 概述

`VYamlPersistenceCodec<T>` 以稳定标识符 `vyaml/1` 实现 `IPersistenceCodec<T>`。它通过 `IYamlFormatterResolver` 进行序列化，使用复合 resolver 链：调用方 generated resolver → VYaml standard formatter → VYaml Unity formatter（仅当前两者不拥有该类型时才使用 Unity formatter）。

本 codec 适合人工可审查性优先于二进制紧凑性的设置和诊断数据。YAML 可读性是诊断便利——不能用于绕过 record integrity、migration、validation 或 record 字节预算。

### 关键特性

- 稳定 codec ID `vyaml/1`
- 复合 resolver 链——primary resolver + `StandardResolver` + `UnityResolver` fallback
- UTF-8 without BOM，规范 LF 行尾
- 直接写入 `IBufferWriter<byte>`——无中间数组分配
- `ReadOnlyMemory<byte>` 反序列化——调用返回后不保留借用 memory
- 构造时验证 resolver——拒绝不包含 `T` formatter 的 resolver

## 架构

| Assembly | Package | 引用 |
| --- | --- | --- |
| `CycloneGames.Persistence.VYaml` | `com.cyclone-games.persistence.vyaml` | `CycloneGames.Persistence.Core`、`VYaml.Core` |

Assembly 设置 `autoReferenced: false` 和 `noEngineReferences: true`。Persistence Core 不依赖 VYaml。不引用本 provider 的应用不会把对应 codec 加入自身依赖图。

VYaml 的 Unity 安装由两部分协调组成：
1. NuGetForUnity 安装 `VYaml`、`VYaml.Annotations` 和 source-generator。
2. Unity Package Manager 安装 `jp.hadashikick.vyaml`，它提供 `VYaml.Core` 和 Unity formatter，并引用 NuGet `VYaml.dll`。

Binary、annotations、source generator 与 Unity bridge 必须保持同版。当前支持基线为 VYaml `1.4.0`。

## 快速开始

定义显式 VYaml 合约：

```csharp
using VYaml.Annotations;

[YamlObject]
public sealed partial class AudioSettings
{
    public float MasterVolume { get; set; } = 1f;
    public bool Muted { get; set; }
}
```

通过 source-generated resolver 创建 codec：

```csharp
using CycloneGames.Persistence.VYaml;
using VYaml.Serialization;

var codec = new VYamlPersistenceCodec<AudioSettings>(
    GeneratedResolver.Instance);
```

绑定到 `PersistenceProfile<AudioSettings>` 并创建 `PersistenceStore<AudioSettings>`。Composition root 持有 profile 和 store。本 package 不使用全局 resolver 或 service locator。

## 核心概念

### 复合 resolver

Codec 在构造时构建自定义 resolver 链：

```csharp
private sealed class PersistenceYamlResolver : IYamlFormatterResolver
{
    private readonly IYamlFormatterResolver _primaryResolver;

    public IYamlFormatter<TValue> GetFormatter<TValue>()
    {
        // 1. 调用方 generated resolver
        IYamlFormatter<TValue> formatter = _primaryResolver.GetFormatter<TValue>();
        if (formatter != null) return formatter;

        // 2. VYaml standard formatter（primitive、collection）
        formatter = StandardResolver.Instance.GetFormatter<TValue>();
        if (formatter != null) return formatter;

        // 3. VYaml Unity formatter（UnityEngine 类型）
        return UnityResolver.Instance.GetFormatter<TValue>();
    }
}
```

不存在 reflection discovery、resolver registry、可变全局默认值或 runtime type-name contract。

### 序列化与反序列化

```csharp
// 序列化：直接写入有界 IBufferWriter<byte>
public void Serialize(in T value, IBufferWriter<byte> destination,
    in PersistenceWriteContext context)
{
    var emitter = new Utf8YamlEmitter(destination);
    YamlSerializer.Serialize(ref emitter, value, _serializerOptions);
}

// 反序列化：读取借用 ReadOnlyMemory<byte>
public T Deserialize(ReadOnlyMemory<byte> payload,
    in PersistenceReadContext context)
{
    return YamlSerializer.Deserialize<T>(payload, _serializerOptions);
}
```

## 使用指南

### Generated Formatter Namespace 安全

当前支持的 VYaml source generator 会在 formatter 代码中生成 `VYaml.Annotations` 等未添加 `global::` 前缀的引用。如果 DTO namespace 引入项目内 `VYaml` namespace，该名称可能遮蔽依赖的根 namespace，产生易误判的 namespace 缺失编译错误。

Generated persistence DTO 应放在中性领域 namespace：

```csharp
namespace MyGame.Settings.Contracts;
```

不要把 `[YamlObject]` DTO 放入 `MyGame.Persistence.VYaml` 等 namespace。如果生成代码报告项目内 `VYaml` namespace 下不存在 `Annotations` 或 `Serialization`，应将 DTO 移入中性 namespace 并重新生成 resolver。

### 可读性与完整性

Record header 是合法 YAML comment preamble，后接 exact YAML payload。Payload 可用于诊断阅读，但 runtime integrity 仍为严格模式。手工修改会使 xxHash64 失效并被拒绝。

如果产品需要可编辑导入，应实现 Editor/import workflow：解析不可信 YAML、校验领域对象，再通过正式 save pipeline 重写。不要添加 runtime checksum bypass。

## 高级主题

### AOT 与 IL2CPP

构造函数要求 `IYamlFormatterResolver`。应传入拥有序列化 DTO 的 assembly 所生成的 resolver。Provider 在接受 resolver 前会确认其包含 `T` 的 formatter。

Editor 中 generated resolver 编译成功不代表 IL2CPP 证据。每组发布 DTO 都需要 Player/AOT smoke test。

### 恶意 YAML 安全

当前 provider 仅适用于可信本地设置。1 MiB record limit 能限制字节与分配，但不能证明 parser depth、alias/anchor expansion、CPU 时间或栈使用受控。在选定 VYaml 版本具备可复现的恶意输入 fixture 与强制 parser budget 前，不得把攻击者可控 YAML 交给本 codec。可编辑 import workflow 必须先实施这些限制，再执行领域校验。

### 平台与线程

Codec 是同步 managed code，public contract 不暴露 Unity 类型。只包含标准 managed type 的 generated DTO 不会初始化 Unity resolver。包含 Unity 类型的 DTO 会进入 `VYaml.Core` Unity formatter 路径，因此需要 Unity host。

Codec 可以在持久化 workflow owner 选择的线程执行。没有明确所有权 policy 时，不要跨线程共享可变 resolver 或 DTO。

## 问题排查

| 现象 | 原因 | 解决方案 |
| --- | --- | --- |
| 构造函数抛出 `ArgumentNullException` | 传入 null resolver | 传入 `GeneratedResolver.Instance` 或具体 `IYamlFormatterResolver`。 |
| 构造函数抛出 `ArgumentException` | Resolver 不包含 `T` 的 formatter | 为 DTO 添加 `[YamlObject]` attribute 并重新生成 resolver。 |
| 编译错误：缺少 namespace `VYaml.Annotations` | DTO namespace 遮蔽 VYaml 依赖 | 将 DTO 移入中性 namespace（如 `MyGame.Settings.Contracts`）。 |
| Malformed YAML 未被 Persistence Store 捕获 | Record-level integrity checks 通过但 YAML 本身损坏 | Persistence Core 拒绝 checksum 失败；使用 VYaml tests 验证结构性 YAML 问题。 |
| IL2CPP Player 抛出 `MissingFormatterException` | 使用 reflection-based resolver 或缺少 generated formatter | 确认所有 DTO 均有 `[YamlObject]` 且 generated resolver 已包含在 Player build 中。 |
| 编码中出现回车符 | VYaml 未输出 canonical LF | 验证 VYaml 版本为 `1.4.0`；provider tests 验证 LF-only 输出。 |
| NuGet/Unity 版本不匹配 | VYaml binary 和 Unity bridge 版本不同 | 将两者固定到同一 immutable tag 或 commit；版本不匹配属于安装错误。 |
