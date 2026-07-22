# CycloneGames.Persistence.SystemIO

[English](README.md) | 简体中文

`CycloneGames.Persistence` 的文件系统存储 provider。将一条完全限定文件路径绑定到异步持久化 storage 契约，有界读取和原子写入委托给 `CycloneGames.IO`，并提供独立 Unity assembly，将可移植相对路径解析到 `Application.persistentDataPath`。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速开始](#快速开始)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [高级主题](#高级主题)
- [问题排查](#问题排查)

## 概述

`SystemFilePersistenceStorage` 为桌面、移动和 dedicated-server 环境实现 `IPersistenceStorage`。它在一次操作中完成 Missing 判断和有界读取，借用调用方 buffer 进行原子写入，并视 Record 已不存在为删除成功。Provider 不拥有 worker、cache、queue、retry loop 或 timer。

`UnityPersistentStorage` 是 `CycloneGames.Persistence.Unity` assembly 中的独立静态工厂，将可移植相对路径（如 `"Settings/audio.cgp"`）映射到 `SystemFilePersistenceStorage.CreateSandboxed`，基于 `Application.persistentDataPath`。必须在 Unity 主线程调用；返回的 storage 不再访问 Unity API。

两个 assembly 都不选择 codec，也不理解 settings、save slots、migration、encryption 或 game state。

### 关键特性

- **完整的 `IPersistenceStorage` 契约**——带 Missing 判断的有界读取、借用写入、幂等删除
- **原子替换**——写入使用同目录临时文件（`CycloneGames.IO` 的 `WriteBytesAtomicallyAsync`），再替换目标文件
- **沙箱路径绑定**——`CreateSandboxed` 拒绝 traversal、rooted path、空路径段、保留设备名和词法 root escape
- **平台守卫**——WebGL 和未确认的主机目标抛出 `PlatformNotSupportedException`；桌面、移动、server 放行
- **Unity 路径 adapter**——`UnityPersistentStorage.Create` 解析到 `Application.persistentDataPath`；仅在构造时需要 Unity 主线程

## 架构

| Assembly | 引用 | `autoReferenced` |
| --- | --- | --- |
| `CycloneGames.Persistence.SystemIO` | `CycloneGames.Persistence.Core`、`CycloneGames.IO.Core`、`CycloneGames.IO.SystemIO` | `false` |
| `CycloneGames.Persistence.Unity` | `CycloneGames.Persistence.Core`、`CycloneGames.Persistence.SystemIO` | `false` |

SystemIO assembly 设置 `noEngineReferences: true`。Unity assembly 设置 `noEngineReferences: false`（引用 `UnityEngine` 以访问 `Application.persistentDataPath`）。Consumer 只引用 composition root 实际使用的 assembly。

本 package 不依赖 VYaml、MessagePack、Settings、AssetManagement、DI Container 或 PlayerPrefs。

## 快速开始

在拥有 record 的 composition root 中引用：

```csharp
using CycloneGames.Persistence;
using CycloneGames.Persistence.Unity;

IPersistenceStorage storage = UnityPersistentStorage.Create(
    "Settings/audio.cgp");
```

纯 C# Host 使用完全限定路径：

```csharp
using System.IO;
using CycloneGames.Persistence;
using CycloneGames.Persistence.SystemIO;

IPersistenceStorage storage = new SystemFilePersistenceStorage(
    Path.GetFullPath("./Data/player.cgp"));
```

将 storage 传给 `PersistenceStore<T>`。一个 storage 实例表示一条 record；每个独立 record 或存档槽位使用不同的可信路径。

## 核心概念

### Storage 契约

| 方法 | 行为 |
| --- | --- |
| `ReadAsync(int maxByteCount, CancellationToken)` | 一次完成 Missing 判断 + 有界读取。返回 `PersistenceStorageReadResult.Found(byte[])` 或 `Missing()`。 |
| `WriteAtomicallyAsync(byte[] content, CancellationToken)` | 借用调用方数组直到 task 完成。Provider 不保留或修改 buffer。 |
| `DeleteAsync(CancellationToken)` | 幂等。Record 已不存在视为成功。 |
| `Location` | 诊断 metadata。绝对路径在 telemetry 或玩家可见输出前需脱敏。 |

```csharp
await storage.WriteAtomicallyAsync(recordBytes, cancellationToken);
// Task 完成后即可安全清零数组。
```

### 路径绑定

直接构造函数只接受完全限定路径：

```csharp
var storage = new SystemFilePersistenceStorage(
    Path.GetFullPath("./Data/player.cgp"));
```

`CreateSandboxed` 接受可信 root 下的可移植相对路径：

```csharp
var storage = SystemFilePersistenceStorage.CreateSandboxed(
    "/home/app/data",
    "profiles/user.cgp");
```

沙箱会拒绝：`../..` traversal、rooted path、空路径段、保留设备名（`CON`、`NUL` 等）、词法 root escape。

## 使用指南

### 原子写入与取消边界

写入先在目标目录创建临时文件，并在替换前完成写入和 flush。当平台提供所需原子原语时，替换失败会保留旧的完整目标文件。

异步传输期间会响应 cancellation。如果另一个 writer 正占用按路径协调的 commit 区域，等待 monitor 本身不可取消；等待期间收到的取消请求会在当前 writer 进入协调区域后立即被观察。通过协调区域内最后一次 cancellation 检查后，替换成为不可取消的 commit point。此后操作返回真实提交结果——不会把已经成功的替换报告为取消。

原子替换不是备份轮换，也不能保证应对所有断电或存储控制器故障。

### 临时文件清理

进程突然终止时，可能在 commit 或清理前留下已经写完的 `.cyclone-*.tmp` 文件。本 provider 不会在启动时扫描或删除临时文件。任何清理 policy 都必须具有明确 owner，并在删除前校验目录范围、文件名模式、文件年龄与 active writer 排除条件。

### 应用所有权

应用负责：
- 可信 root 和相对 record 名称
- storage 与 `PersistenceStore<T>` 生命周期
- 序列化、迁移、校验和保存时机
- 错误报告和面向玩家的恢复 policy

Provider 不会自动删除损坏 record，并且只使用经过规范化的绑定路径。

## 高级主题

### 平台行为

System.IO provider 面向 Windows、Linux、macOS、iOS、Android 和 dedicated-server 环境，但每个目标 Player 都必须实际验证文件系统和原子替换行为。

WebGL 和未确认的主机平台通过 `EnsurePlatformSupported()` 中的 `UNITY_SERVER` 预处理器守卫 fail closed：

```csharp
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR && !UNITY_STANDALONE \
    && !UNITY_IOS && !UNITY_ANDROID && !UNITY_SERVER
    throw new PlatformNotSupportedException("...");
#endif
```

它们需要独立的 IndexedDB 或平台 save-data SDK 异步 provider。

### 路径安全

词法路径检查假定本地文件系统可信，即其他主体不能在绑定与实际操作之间把目录替换为 symlink 或 reparse point。它不是对抗恶意本地写入者的 sandbox security boundary。需要抵抗此类攻击的产品必须使用基于 directory handle 或等价 no-follow 原语的平台 provider。

### 性能

本 provider 是冷路径 buffered 实现。读取分配一份主要的有界 record array，并产生有界 adapter 开销；写入借用已经物化的 record array——不在 provider 层复制内容。工作量为 O(record bytes)。不要从每帧循环或 input callback 调用持久化。

## 问题排查

| 现象 | 原因 | 解决方案 |
| --- | --- | --- |
| 写入时抛出 `ArgumentNullException` | 传入 `null` content array | 始终传递 `PersistenceStore` 提供的 exact record array。 |
| 构造时抛出 `ArgumentException` | 路径不是完全限定路径 | 使用 `Path.GetFullPath` 或 `CreateSandboxed`。 |
| `CreateSandboxed` 抛出 `ArgumentException` | 相对路径包含 `..` 或 root escape | 使用无 traversal 的可移植相对路径。 |
| WebGL 抛出 `PlatformNotSupportedException` | SystemIO provider 未针对 WebGL 编译 | 注入基于 IndexedDB 的 `IPersistenceStorage` adapter。 |
| 写入成功但旧文件仍存在 | 平台缺少原子替换原语 | 验证目标平台的原子替换能力；在目标硬件上测试。 |
| 有效路径产生 `FileNotFoundException` | 父目录不存在 | 首次写入前先创建目录结构；provider 不负责创建目录。 |
| `.cyclone-*.tmp` 文件累积 | 写入期间进程终止 | 实现启动清理 policy，校验目录范围、pattern、年龄和 active writer 排除。 |
| `UnityPersistentStorage.Create` 在后台线程抛异常 | `Application.persistentDataPath` 需主线程 | 从 `Awake` 或 `Start` 调用；将构造好的 storage 作为依赖传入。 |
