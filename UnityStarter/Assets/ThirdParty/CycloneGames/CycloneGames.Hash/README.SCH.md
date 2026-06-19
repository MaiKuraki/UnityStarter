# CycloneGames.Hash

CycloneGames.Hash 为 CycloneGames 底层模块提供纯 C#、确定性的哈希基础能力。它适用于 stable identifier、protocol manifest、DataTable schema hash、构建缓存校验和热更新兼容性校验。

这个包有意独立于 `CycloneGames.Utility`：它不依赖 Unity API、不依赖 logger、不依赖 Burst，也不需要 unsafe code。

## 设计目标

- 在 Unity Editor、player build、headless server、CI 和离线工具中输出一致。
- 对序列化 hash 值明确端序。
- 基于 `ReadOnlySpan<byte>` 和栈友好的 struct API，支持热路径 zero-GC 使用。
- 依赖面足够小，适合 GameplayTags、Networking、GameplayAbilities、DataTable 等底层模块依赖。
- 明确区分非密码学一致性检查和防篡改安全校验。

## 程序集

| Assembly | 路径 | 用途 |
| --- | --- | --- |
| `CycloneGames.Hash.Core` | `Core/` | 纯 C# 确定性哈希算法和端序工具。 |
| `CycloneGames.Hash.Tests.Editor` | `Tests/Editor/` | known vector 和 streaming consistency 的 EditMode tests。 |

`CycloneGames.Hash.Core` 使用 `noEngineReferences=true`，并且没有 assembly references。

## 核心类型

| 类型 | 使用场景 |
| --- | --- |
| `Fnv1a64` | Stable ID、有序 manifest、小型协议 fingerprint。 |
| `StableHash64` | 模块级 identifier 和 manifest 累积使用的非零 stable hash helper。 |
| `XxHash64` | 构建缓存、payload check、大块 byte buffer 的快速内容哈希。 |
| `HashByteOrder` | 显式 little-endian 和 big-endian `ulong` 读写工具。 |

## 用法

Stable ID 和 manifest：

```csharp
ulong tagId = StableHash64.ComputeUtf16Ordinal("Ability.Damage.Fire");

ulong manifest = Fnv1a64.OffsetBasis;
manifest = StableHash64.CombineUInt64LittleEndian(manifest, tagId);
```

快速内容检查：

```csharp
ulong payloadHash = XxHash64.Compute(payloadBytes);

XxHash64 streaming = XxHash64.Create();
streaming.Append(chunkA);
streaming.Append(chunkB);
ulong finalHash = streaming.GetDigest();
```

序列化 hash 值：

```csharp
Span<byte> buffer = stackalloc byte[8];
HashByteOrder.WriteUInt64LittleEndian(buffer, payloadHash);
```

## 算法策略

`Fnv1a64` 和 `XxHash64` 都是非密码学算法。它们适合检测意外差异、schema 不匹配、manifest 不兼容、缓存失效和协议握手差异。

不要把这些算法作为抵御恶意篡改的唯一手段。热更新包、CDN 下载、付费内容、账号数据和反作弊敏感 payload 应在安全边界使用 signed manifest 或 SHA-256 等密码学哈希。

`ComputeUtf16Ordinal` 会按 .NET UTF-16 code unit 计算哈希。这是为了保留已经采用 ordinal string code-unit hashing 的系统的 stable ID 语义。当序列化 byte 表示才是真实契约时，应使用 byte-based API。

## 持久化

本包不写入文件、资产、registry、`EditorPrefs`、`PlayerPrefs` 或隐藏全局状态。Manifest、cache file、protocol packet 和 schema hash record 都由消费模块自己拥有。

## 集成建议

- GameplayTags 使用 `StableHash64` 生成 tag stable ID 和 manifest hash。
- Networking 可以使用 `HashByteOrder` 和 `StableHash64` 进行 protocol version manifest 和 compatibility handshake。
- DataTable 可以使用 `XxHash64` 校验生成 payload，使用 `StableHash64` 生成 schema fingerprint。
- GameplayAbilities 可以使用 stable hash 生成 ability-set manifest，但 ability 领域逻辑仍应以显式 ID 或 GameplayTags 作为契约。

## 验证

CLI 构建检查：

```bash
dotnet build CycloneGames.Hash.Core.csproj -v:minimal
dotnet build CycloneGames.Hash.Tests.Editor.csproj -v:minimal
```

Unity Editor 检查：

1. 打开 `<repo-root>/UnityStarter`。
2. 运行 `CycloneGames.Hash.Tests.Editor` 下的 EditMode tests。
3. 确认所有参与玩法模拟或构建工具链的目标平台都通过 known-vector tests。
