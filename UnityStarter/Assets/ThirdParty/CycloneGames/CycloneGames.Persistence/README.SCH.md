# CycloneGames.Persistence

[English](README.md) | 简体中文

CycloneGames.Persistence 是与 serializer 无关、无 Unity 依赖的单条有界 versioned record 底座。它组合 codec 与 storage adapter，强制执行严格字节格式，在反序列化前检测偶发损坏，并以结构化结果返回运行时失败。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速开始](#快速开始)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [高级主题](#高级主题)
- [问题排查](#问题排查)

## 概述

Persistence 将两个外部 adapter——serializer codec 与文件/数据库 storage provider——协调为安全的单条 Record pipeline。它把每条 Payload 用 versioned header、稳定 codec identifier 和 xxHash64 checksum 包装起来，并在反序列化前拒绝 header、长度或 integrity probe 不匹配的 Record。

运行时失败通过带显式 `PersistenceErrorCode` 值的结果类型返回。无效参数和并发 overlap 会立即抛异常。致命运行时异常（`OutOfMemoryException`）不会转换为结果。

Core assembly 仅引用 `CycloneGames.Hash.Core`。它不依赖 Unity、System.IO、Settings、VYaml、MessagePack、DI、reflection discovery 或后台 worker。

### 关键特性

- **Record V1 格式**——规范 LF-only ASCII header、codec identity、content version、固定 `identity/1`、xxHash64 覆盖 dispatch metadata 与 payload
- **结构化结果**——`PersistenceLoadResult<T>` 区分 Loaded、Missing、Failed；`PersistenceOperationResult` 以 error code 报告 Succeeded 或 Failed
- **每次一个操作**——overlap 或 callback reentrancy 抛出 `InvalidOperationException`
- **取消支持**——每个边界都会观察 cancellation token；commit 结果决定最终状态
- **Payload 限制**——硬上限 1 MiB；profile 级别可降至 256 KiB
- **无分配泄漏**——Buffer 用完即清；codec 写入借用 `IBufferWriter<byte>`

## 架构

| Assembly | Package | 引用 |
| --- | --- | --- |
| `CycloneGames.Persistence.Core` | `com.cyclone-games.persistence` | `CycloneGames.Hash.Core` |

Package 声明依赖 `com.cyclone-games.hash`（1.0.0）。不引用 `UnityEngine`（`noEngineReferences: true`），仅需 Unity 2022.3。

Storage 和 codec provider 为独立 package。Persistence Core 可以脱离它们编译和测试。应用按需添加 composition root 所需的 provider：

- `com.cyclone-games.persistence.systemio`——文件系统存储
- `com.cyclone-games.persistence.vyaml`——人类可读 YAML codec
- `com.cyclone-games.persistence.messagepack`——紧凑二进制 codec（可选，默认 inactive）

## 快速开始

Composition root 安装实际需要的 package：

- `com.cyclone-games.persistence`
- 一个 storage provider：`com.cyclone-games.persistence.systemio`
- 一个 codec provider：如 `com.cyclone-games.persistence.vyaml`

应用拥有 DTO 和 schema version。VYaml 需要 generated resolver；IL2CPP build 不接受 reflection fallback。

```csharp
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence;
using CycloneGames.Persistence.Unity;
using CycloneGames.Persistence.VYaml;

public static class LocalProfileComposition
{
    public static async Task SaveAsync(
        LocalProfile profile,
        CancellationToken cancellationToken)
    {
        IPersistenceStorage storage = UnityPersistentStorage.Create("profiles/local.yaml");
        var codec = new VYamlPersistenceCodec<LocalProfile>(GeneratedResolver.Instance);
        var limits = new PersistenceLimits(maximumPayloadBytes: 256 * 1024);
        var store = new PersistenceStore<LocalProfile>(
            storage,
            new PersistenceProfile<LocalProfile>(codec, limits));

        PersistenceOperationResult result = await store.SaveAsync(
            in profile,
            contentVersion: 2,
            cancellationToken);
        if (!result.IsSuccess)
        {
            // 根据 ErrorCode 执行产品恢复策略；Exception 进入遥测前必须脱敏。
        }
    }
}
```

Load 需要声明调用方能理解的最新 content version：

```csharp
PersistenceLoadResult<LocalProfile> result = await store.LoadAsync(
    maximumSupportedContentVersion: 2,
    cancellationToken);

switch (result.Status)
{
    case PersistenceLoadStatus.Loaded:
        LocalProfile profile = result.Value;
        break;
    case PersistenceLoadStatus.Missing:
        // 创建产品默认值；Missing 不是错误。
        break;
    case PersistenceLoadStatus.Failed:
        // 保留原有权威状态，并执行恢复策略。
        break;
    default:
        throw new InvalidOperationException("The load result was not initialized.");
}
```

## 核心概念

| 类型 | 职责 |
| --- | --- |
| `IPersistenceCodec<T>` | 使用稳定 `PersistenceCodecId` 同步编码/解码一份借用 payload。 |
| `IPersistenceStorage` | 拥有一个 location，执行有界读取、原子写入和幂等删除。 |
| `PersistenceProfile<T>` | 绑定一个 codec 与不可变 allocation limits。 |
| `PersistenceStore<T>` | Serialize、编码 Record V1、verify、deserialize、分类失败，并限制一个 active operation。 |
| `PersistenceLimits` | 限制 plaintext payload、record read 和 pooled writer 增长。 |
| `PersistenceLoadResult<T>` | 明确区分 `Loaded`、`Missing`、`Failed` 和默认 struct 的未初始化状态。 |
| `PersistenceOperationResult` | 报告 `Succeeded` 或 `Failed`；不能只用 `ErrorCode.None` 判断成功。 |

### Codec 契约

Codec 写入 Store 提供的有界 `IBufferWriter<byte>`。它不能分配 result array、保留借用输入、通过 runtime reflection 发现类型，或写出 `context.Limits`。Context 会暴露调用方 token，让同步 codec 可以在有界工作阶段检查取消。`T` 必须是纯 DTO，不能拥有 `IDisposable` 资源、unmanaged handle、Unity object 生命周期或线程亲和状态——失败和取消的候选值会被丢弃而不执行 disposal。

```csharp
public sealed class ExampleCodec<T> : IPersistenceCodec<T>
{
    public PersistenceCodecId CodecId { get; } = new PersistenceCodecId("example/1");

    public void Serialize(
        in T value,
        IBufferWriter<byte> destination,
        in PersistenceWriteContext context)
    {
        // 向 destination 写入有界、确定的表示。
    }

    public T Deserialize(
        ReadOnlyMemory<byte> payload,
        in PersistenceReadContext context)
    {
        // 不得保留 payload。旧 wire shape 仍可读取时可使用 context.ContentVersion。
        throw new NotImplementedException();
    }
}
```

`ReadOnlyMemory<byte>` 是有意设计：当前 VYaml 和 MessagePack API 可以直接读取 memory，避免 adapter 复制整段 payload。所有权契约仍禁止在调用结束后保留 memory。

## 使用指南

### Save 流程

1. Codec 写入 clear-on-return pooled buffer。
2. Store 以 Record V1 header 和 xxHash64 包装 payload。
3. 创建 exact record array 并交给 storage adapter。
4. write task 完成后清零数组。

### Load 流程

1. Storage 移交 exact record array。
2. Store 原地解析，校验 header、codec ID、content version 和 checksum。
3. 将 `ReadOnlyMemory<byte>` slice 借给 codec。
4. 反序列化后清零数组。

### Error Code

| ErrorCode | 说明 |
| --- | --- |
| `None` | 默认值；不代表成功。 |
| `ReadFailed` | Storage 读取异常。 |
| `PayloadTooLarge` | Codec 或 Record 超出配置上限。 |
| `RecordFormatMismatch` | 未知 magic 或非 Record V1 格式。 |
| `UnsupportedRecordVersion` | Record version 超出 Store 理解范围。 |
| `MalformedRecord` | Header 结构错误。 |
| `IntegrityCheckFailed` | xxHash64 不匹配。 |
| `CodecMismatch` | Record 中 codec ID 与 profile codec 不一致。 |
| `TransformMismatch` | Transform identity 不匹配（保留）。 |
| `FutureContentVersion` | Content version 超出最大支持值。 |
| `DeserializeFailed` | Codec 反序列化异常。 |
| `SerializationFailed` | Codec 序列化异常。 |
| `WriteFailed` | Storage 写入异常。 |
| `DeleteFailed` | Storage 删除异常。 |
| `Cancelled` | 调用方 token 已取消。 |

### 并发与内存

- Payload hard limit 为 1 MiB；profile 可降低上限；settings 通常使用 256 KiB。
- 时间复杂度 O(n)，temporary memory 为有界 O(n)。
- 一个 Store 同时只接受一个 operation。Overlap 抛 `InvalidOperationException`。
- Guard 只覆盖当前 Store 实例。多个 Store 共享 codec、resolver 或 storage instance 时不会自动串行化。
- Store 不创建 worker thread、不使用 `Task.Run`，内部使用 `ConfigureAwait(false)`。
- 只有调用方 token 已取消时，关联的 `OperationCanceledException` 才分类为 `Cancelled`。Codec 或 provider 主动抛出的非请求取消属于对应阶段失败。
- Storage commit boundary 决定 cancellation 语义。Commit 成功后，即使 token 随后取消，也应报告成功。

Persistence 是冷路径能力。不要每帧调用，也不要用作逐实体更新机制。

## 高级主题

### Record 格式

Record V1 使用规范 LF-only ASCII header、exact payload、稳定 codec ID、固定 `identity/1`，并对 dispatch metadata 与 payload 计算 xxHash64。字节契约和 parser 优先级见 [PersistenceRecordV1.SCH.md](Documentation~/PersistenceRecordV1.SCH.md)。

xxHash64 只检测偶发损坏，不能验证攻击者控制的文件。不要把本地值作为支付、entitlement 或服务器权威，也不要直接保存 secret。

### 平台矩阵

| 层 | Editor/桌面/移动/Server | WebGL | 主机 |
| --- | --- | --- | --- |
| Persistence Core | 静态兼容；纯 managed code | 静态兼容 | 静态兼容 |
| SystemIO provider | 面向已验证文件系统目标 | Fail closed | 通过资格验证前 fail closed |
| Unity SystemIO path adapter | 组合 `Application.persistentDataPath` | Fail closed | 通过资格验证前 fail closed |
| 自定义 provider | 可选 | 需要 IndexedDB/JavaScript async provider | 需要平台 SDK save-data provider |

本矩阵描述静态边界。IL2CPP、stripping、移动端、WebGL、主机、文件系统持久性和长期运行稳定性都需要独立、可复现的 Player 证据。

### Storage 行为

Core 自身不写文件，也不知道 path。`IPersistenceStorage` 实现拥有 location、lifecycle、atomicity、delete、quota 和 recovery。SystemIO provider 在消费方指定 root 下写一份显式文件，使用同目录 temporary file 和 atomic replace。该文件是产品 runtime data；是否删除由产品恢复策略决定。

### 迁移

Store 不猜测旧格式。Prototype record、raw YAML 和未知 magic 返回 `RecordFormatMismatch`。产品如需导入，必须在采用 Record V1 前执行显式迁移。Package 边界、所有权和从已退役设置/持久化组合包进行破坏性迁移的说明见 [ADR-001](Documentation~/ADR-001-Settings-Persistence-Boundaries.SCH.md)。

## 问题排查

| 现象 | 原因 | 解决方案 |
| --- | --- | --- |
| Load 返回 `RecordFormatMismatch` | 文件未经 Record V1 包装 | 在采用 Store 前运行产品迁移。 |
| Load 返回 `CodecMismatch` | Record 使用不同 codec 写入 | 验证 codec ID 匹配；孤儿 record 需迁移。 |
| Load 返回 `FutureContentVersion` | Record content version 超出 `maximumSupportedContentVersion` | 提高最大值，或为新版本增加迁移步骤。 |
| Load 返回 `IntegrityCheckFailed` | 文件被手动修改或损坏 | 从已知良好备份恢复；永远不要绕过 checksum。 |
| Save 返回 `PayloadTooLarge` | DTO 超出配置上限 | 提高 `PersistenceLimits.MaximumPayloadBytes`，或拆分 record。 |
| 并发调用抛 `InvalidOperationException` | 同一 Store 的重叠操作 | 串行化调用；一个 Store = 一个 active operation。 |
| 未捕获到 `OperationCanceledException` | Composition root 中仅 catch `Exception` | Catch 或检查 `result.ErrorCode == PersistenceErrorCode.Cancelled`。 |
| IL2CPP Player 测试失败 | 使用 reflection fallback 而非 generated resolver | 将所有 reflection-based formatter 替换为 source-generated 或显式注册。 |
