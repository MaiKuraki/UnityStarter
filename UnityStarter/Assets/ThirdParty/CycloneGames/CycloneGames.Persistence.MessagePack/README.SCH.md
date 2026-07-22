# CycloneGames.Persistence.MessagePack

[English](README.md) | 简体中文

可选紧凑二进制 codec，桥接 `CycloneGames.Persistence` 与 MessagePack-CSharp。通过显式传入的 source-generated resolver 和强制的 untrusted-data security policy，转换类型化持久化 DTO。

Provider source 随 package 提供，但仅当 `com.github.messagepack-csharp` `3.1.8` 已安装时才编译。当前 UnityStarter checkout 未激活 MessagePack。Assembly 由 `versionDefines` 生成的 `CYCLONEGAMES_HAS_MESSAGEPACK` 条件编译。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速开始](#快速开始)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [高级主题](#高级主题)
- [问题排查](#问题排查)

## 概述

`MessagePackPersistenceCodec<T>` 以稳定标识符 `messagepack/1` 实现 `IPersistenceCodec<T>`。它通过显式 `IFormatterResolver` 和基于 `UntrustedData` 的 `MessagePackSecurity` 策略进行序列化。Persistence Core 负责 record framing、content version metadata、字节上限、checksum 校验和 storage orchestration。

Codec 强制：无压缩（压缩会改变 wire profile，需要新标识符）、不使用 runtime type-name 解析、不依赖 reflection formatter 发现、一个解码值后不允许多余字节、不放宽 assembly-version 约束（`OmitAssemblyVersion` 和 `AllowAssemblyVersionMismatch` 已关闭）。构造函数会拒绝不包含 `T` 的显式 formatter 的 resolver，以及缺少 collision-resistant hashing、正数 graph depth 和正数 decompressed-size limit 的 security policy。

### 关键特性

- 稳定 codec ID `messagepack/1`——无压缩 current-spec option profile + 显式 generated formatter schema
- 安全优先的构造函数——强制 collision-resistant hashing、graph-depth limit、decompressed-size 上限
- Trailing-byte 拒绝——解码值必须消耗整个 payload
- 仅 source-generated formatter——不支持 reflection 或 typeless resolver
- 直接写入 `IBufferWriter<byte>`——无中间 array 分配

## 架构

| Assembly | Package | 引用 |
| --- | --- | --- |
| `CycloneGames.Persistence.MessagePack` | `com.cyclone-games.persistence.messagepack` | `CycloneGames.Persistence.Core`、`MessagePack.dll` |

Assembly 使用 `defineConstraints: ["CYCLONEGAMES_HAS_MESSAGEPACK"]` 和 `com.github.messagepack-csharp` `[3.1.8,4.0.0)` 的 `versionDefines`。Unity package 未安装时 assembly 不编译。不要手动添加 `CYCLONEGAMES_HAS_MESSAGEPACK`。

### 激活状态

| 项目 | 当前 checkout |
| --- | --- |
| Provider source | Present |
| `CycloneGames.Persistence.MessagePack` assembly | Inactive |
| Provider tests | Inactive |
| `com.github.messagepack-csharp` | 未安装 |
| NuGet `MessagePack` binary、annotations 与 analyzer | 未安装 |

## 快速开始

使用本 provider 前，安装完整依赖集：

1. 安装精确版本的 `MessagePack` NuGet package，包括匹配的 annotations 和 analyzer/source generator。
2. 安装相同版本的官方 `com.github.messagepack-csharp` Unity package，固定到 immutable tag 或 commit。
3. 让 Unity 解析 `CYCLONEGAMES_HAS_MESSAGEPACK`。
4. Consumer asmdef 显式引用 `CycloneGames.Persistence.MessagePack`。

定义稳定二进制合约：

```csharp
using MessagePack;

[MessagePackObject]
public sealed class PlayerPreferences
{
    [Key(0)] public float LookSensitivity { get; set; }
    [Key(1)] public bool InvertY { get; set; }
}
```

构造 codec：

```csharp
MessagePackSecurity security = MessagePackSecurity.UntrustedData
    .WithMaximumObjectGraphDepth(64)
    .WithMaximumDecompressedSize(1024 * 1024);

var codec = new MessagePackPersistenceCodec<PlayerPreferences>(
    GeneratedMessagePackResolver.Instance,
    security);
```

绑定到 `PersistenceProfile<PlayerPreferences>` 并照常创建 Store。Persistence Core 负责 record framing、limits、versioning 和 checksums。

## 核心概念

### Codec 构造

```csharp
public sealed class MessagePackPersistenceCodec<T> : IPersistenceCodec<T>
{
    public MessagePackPersistenceCodec(
        IFormatterResolver resolver,
        MessagePackSecurity security)
    {
        // 验证 resolver 为 T 提供 formatter
        // 验证 security：collision-resistant hash、
        //   正数 graph depth、正数 decompressed-size limit
        // 构建锁定 options：No compression、current spec、
        //   OmitAssemblyVersion=false、AllowAssemblyVersionMismatch=false
    }

    public PersistenceCodecId CodecId => new PersistenceCodecId("messagepack/1");
}
```

Option 在构造时锁定。调用方不能在同一 codec identifier 下替换 option profile。改变 option-level wire behavior 需要新标识符和显式格式决策。

### 序列化与反序列化

- **Serialize**——通过 `MessagePackSerializer.Serialize` 直接写入 `IBufferWriter<byte>`，传递 `PersistenceWriteContext` 的 cancellation token
- **Deserialize**——读取借用 `ReadOnlyMemory<byte>`，验证 `bytesRead == payload.Length`，拒绝多余字节

## 使用指南

### Schema Policy

- 使用 `[MessagePackObject]` 与显式整数 `[Key]`。
- 新字段使用新的 Optional Key。
- 删除字段的 Key 永久保留——不得复用。
- Contractless、typeless 和 reflection-generated resolver 不属于 `messagepack/1` 契约。
- 不持久化 Runtime Type Name。
- Schema Migration 由所属领域负责，不属于本 provider。

### 安全约束

构造函数要求以 `MessagePackSecurity.UntrustedData` 为基础 policy，并拒绝：
- 缺少 collision-resistant hashing 的 policy
- 零或负 `MaximumObjectGraphDepth`
- 零或负 `MaximumDecompressedSize`
- 超过 `PersistenceLimits.HardMaximumPayloadBytes` 的 decompressed size

Persistence Core 在调用 codec 前校验 record length、codec identity、transform identity 和 xxHash64。xxHash64 只能检测意外修改，不提供 authentication。

## 高级主题

### Compression 与 Wire Profile

`messagepack/1` 禁用 MessagePack compression。启用 compression 会形成不同 wire profile，必须使用新 codec identifier。Compression 不是 encryption。Authenticated encryption 属于独立审查的 Persistence Security Provider。

### AOT 与 IL2CPP

Unity IL2CPP 要求每个持久化 DTO 都具有 source-generated 或显式注册的 formatter。Editor/Mono reflection fallback 不是发布证据。MessagePack binary、annotations、analyzer、generated resolver 和 Unity package 必须保持同版。

在发布产品中启用前，必须使用准确 DTO assembly 和 stripping 配置执行 IL2CPP round trip。

### 内存与性能

序列化直接写入 Persistence Core 的有界 `IBufferWriter<byte>`。反序列化读取借用的 `ReadOnlyMemory<byte>`。两者都是同步 CPU 工作。Allocation 取决于 DTO 和 MessagePack formatter 行为。Persistence 是有界冷路径——不要逐帧调用。

## 问题排查

| 现象 | 原因 | 解决方案 |
| --- | --- | --- |
| Assembly 不编译 | `com.github.messagepack-csharp` 未安装或版本不匹配 | 安装固定到 immutable tag 的 `3.1.8`；验证 `versionDefines` 解析。 |
| 构造函数抛出 `ArgumentException` | Resolver 缺少 `T` 的显式 formatter | 添加 `[MessagePackObject]` 和 `[Key]` attribute；重新生成 resolver。 |
| 构造函数抛出 `ArgumentException` | Security policy 的 depth 或 size limit 为零 | 使用带正数 limits 的 `MessagePackSecurity.UntrustedData`。 |
| `InvalidDataException` 含 trailing bytes | Payload 在一个值后包含额外数据 | 验证序列化输出；检查是否存在重复编码。 |
| 反序列化返回错误数据 | Key 值变更或复用 | 保留已删除 key；以新 optional key 添加新字段。 |
| IL2CPP Player 抛出 `MissingFormatterException` | 使用 reflection-based 或 typeless resolver | 切换为 source-generated resolver + 显式 formatter。 |
| 设置文件不可读 | 在活跃 profile 上切换了 compression | Compression 改变 wire format；旧 record 需迁移或使用新 codec ID。 |
