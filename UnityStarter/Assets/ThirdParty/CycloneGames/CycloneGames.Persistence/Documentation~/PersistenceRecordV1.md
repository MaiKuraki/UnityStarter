# Persistence Record V1

This document is the byte-level contract for `cgp-record` version `1`. Implementations must produce and consume the same bytes without relying on locale, platform line endings, runtime type names, or permissive text parsing.

## Canonical layout

```text
# cgp-record: 1\n
# content-version: <Int32 decimal>\n
# codec-id: <versioned ASCII token>\n
# transform-id: identity/1\n
# payload-bytes: <Int32 decimal>\n
# xxh64: <16 uppercase hexadecimal digits>\n
---\n
<exact payload bytes>
```

The displayed `\n` is one byte `0A`. The header has no BOM. There is no implicit newline after the payload; a final newline exists only when it belongs to the payload.

## Field rules

- Fields and the delimiter occur exactly once and in the displayed order.
- Every colon is followed by exactly one ASCII space.
- Header line endings are LF. CRLF and standalone CR are invalid.
- `cgp-record`, `content-version`, and `payload-bytes` are non-negative signed 32-bit integers written as canonical ASCII decimal. Zero is `0`; a positive value has no sign, whitespace, or leading zero.
- `codec-id` contains 1–64 bytes. It starts and ends with `[a-z0-9]`, contains exactly one `/`, and otherwise uses only `[a-z0-9._-]`. The suffix is the provider format version, not a runtime type name.
- Record V1 has no payload-transform extension point. `transform-id` is fixed to `identity/1`; another valid token returns `TransformMismatch` after integrity verification.
- The header is at most 256 bytes.
- `payload-bytes` must equal all bytes remaining after `---\n`. A truncated payload and trailing bytes are both invalid.
- Payload bytes are serializer-owned. They may be UTF-8 YAML or arbitrary binary.

## xxHash64 input

The checksum detects accidental corruption. It does not authenticate data and is not encryption.

Hash the following byte sequence with xxHash64 seed `0`:

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

Write the numeric digest as 16 uppercase hexadecimal ASCII digits. Metadata is part of the checksum, so changing a structurally valid version, codec ID, or transform ID without recomputing the checksum produces `IntegrityCheckFailed`. A changed declared length that no longer equals the remaining payload is rejected earlier as `MalformedRecord`.

## Parser precedence

1. A missing exact `# cgp-record: ` prefix is `RecordFormatMismatch`.
2. An invalid first-line decimal is `MalformedRecord`; a canonical first-line version other than `1` is immediately `UnsupportedRecordVersion`, because a V1 parser must not interpret the remaining bytes of a future format.
3. For version `1`, invalid syntax, field order, decimal form, token grammar, delimiter, length, or hexadecimal form is `MalformedRecord`.
4. A payload over the configured limit is `PayloadTooLarge`.
5. Checksum mismatch is `IntegrityCheckFailed` and the codec is not called.
6. A checksum-valid but unexpected codec or transform is `CodecMismatch` or `TransformMismatch`.
7. A checksum-valid content version newer than the caller supports is `FutureContentVersion`.
8. Only then may the codec receive the borrowed payload memory.

Old prototype records, raw YAML, and unknown magic are never guessed or migrated by Core.

## Golden fixture

`Tests/Core/Fixtures/PersistenceRecordV1.yaml.bytes` is the pre-`1.0.0` golden fixture. Its payload is `value: 42\n`, content version is `3`, codec ID is `test-yaml/1`, and checksum is `8C3CEB0DE230D196`.
