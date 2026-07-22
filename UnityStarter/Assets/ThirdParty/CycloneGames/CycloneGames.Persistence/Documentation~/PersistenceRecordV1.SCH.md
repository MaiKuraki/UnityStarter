# Persistence Record V1

本文档定义 `cgp-record` 版本 `1` 的字节级契约。实现不得依赖区域设置、平台换行符、运行时类型名或宽松文本解析，独立实现必须产生并读取完全相同的字节。

## 规范布局

```text
# cgp-record: 1\n
# content-version: <Int32 十进制>\n
# codec-id: <带版本的 ASCII token>\n
# transform-id: identity/1\n
# payload-bytes: <Int32 十进制>\n
# xxh64: <16 位大写十六进制>\n
---\n
<原样 payload bytes>
```

其中 `\n` 表示单字节 `0A`。Header 不带 BOM。Payload 末尾没有隐含换行；只有 payload 本身包含换行时，文件才以换行结束。

## 字段规则

- 所有字段和 delimiter 必须各出现一次，并严格采用上述顺序。
- 每个冒号后严格只有一个 ASCII 空格。
- Header 只接受 LF，拒绝 CRLF 和单独 CR。
- `cgp-record`、`content-version`、`payload-bytes` 是非负 `Int32`，采用规范 ASCII 十进制：零只能写作 `0`，正数不能包含符号、空白或前导零。
- `codec-id` 长度为 1–64 bytes；首尾是 `[a-z0-9]`，必须且只能包含一个 `/`，其余字符仅允许 `[a-z0-9._-]`。后缀表示 provider 格式版本，不是运行时类型名。
- Record V1 不发布 payload transform 扩展点。`transform-id` 固定为 `identity/1`；其他语法合法的 token 在完整性校验后返回 `TransformMismatch`。
- Header 最大 256 bytes。
- `payload-bytes` 必须等于 `---\n` 后的全部剩余字节。截断和 trailing bytes 都是非法记录。
- Payload bytes 由 serializer 定义，可以是 UTF-8 YAML，也可以是任意二进制。

## xxHash64 输入

Checksum 只检测偶发损坏，不提供真实性验证，也不是加密。

使用 xxHash64、seed `0` 计算以下字节序列：

```text
ASCII("CGP\0")
+ Int32LE(recordVersion)
+ Int32LE(contentVersion)
+ Int32LE(codecIdByteLength)
+ codecId ASCII bytes
+ Int32LE(transformIdByteLength)
+ transformId ASCII bytes
+ Int32LE(payloadByteLength)
+ exact payload bytes
```

数值 digest 写为 16 位大写十六进制 ASCII。Metadata 参与 checksum，因此在不重新计算 checksum 的情况下修改结构仍然合法的 version、codec ID 或 transform ID，会得到 `IntegrityCheckFailed`。若修改后的声明长度不再等于剩余 payload，则会更早以 `MalformedRecord` 拒绝。

## Parser 优先级

1. 缺少精确的 `# cgp-record: ` 前缀时返回 `RecordFormatMismatch`。
2. 第一行十进制非法时返回 `MalformedRecord`；第一行是规范十进制但 version 不是 `1` 时立即返回 `UnsupportedRecordVersion`，因为 V1 parser 不能解释未来格式的剩余 bytes。
3. 对 version `1`，语法、字段顺序、十进制形式、token grammar、delimiter、长度或十六进制形式非法时返回 `MalformedRecord`。
4. Payload 超过配置上限时返回 `PayloadTooLarge`。
5. Checksum 不一致时返回 `IntegrityCheckFailed`，且不得调用 codec。
6. Checksum 有效但 codec 或 transform 不匹配时返回 `CodecMismatch` 或 `TransformMismatch`。
7. Checksum 有效但 content version 高于调用方支持版本时返回 `FutureContentVersion`。
8. 只有通过上述检查后，codec 才会获得借用的 payload memory。

Core 不猜测或迁移旧 prototype record、raw YAML 和未知 magic。

## Golden fixture

`Tests/Core/Fixtures/PersistenceRecordV1.yaml.bytes` 是 `1.0.0` 之前的 golden fixture。其 payload 是 `value: 42\n`，content version 是 `3`，codec ID 是 `test-yaml/1`，checksum 是 `8C3CEB0DE230D196`。
