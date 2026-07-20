using System;
using System.Globalization;
using System.IO;
using System.Text;
using CycloneGames.Hash.Core;

namespace CycloneGames.Services.Tests
{
    internal struct TestSettings
    {
        public int Version;
        public int Value;
    }

    internal sealed class TestSettingsCodec : ISettingsCodec<TestSettings>
    {
        public bool ThrowOnSerialize { get; set; }

        public bool ThrowOnDeserialize { get; set; }

        public byte[] SerializedPayloadOverride { get; set; }

        public int SerializeCount { get; private set; }

        public int DeserializeCount { get; private set; }

        public int LastSerializeMaxByteCount { get; private set; } = -1;

        public byte[] Serialize(in TestSettings settings, int maxByteCount)
        {
            SerializeCount++;
            LastSerializeMaxByteCount = maxByteCount;
            if (ThrowOnSerialize)
            {
                throw new InvalidOperationException("Injected serialization failure.");
            }

            byte[] payload;
            if (SerializedPayloadOverride != null)
            {
                payload = Clone(SerializedPayloadOverride);
            }
            else
            {
                string text = string.Concat(
                    settings.Version.ToString(CultureInfo.InvariantCulture),
                    "|",
                    settings.Value.ToString(CultureInfo.InvariantCulture));
                payload = Encoding.UTF8.GetBytes(text);
            }

            if (payload.Length > maxByteCount)
            {
                throw new SettingsPayloadBudgetExceededException(maxByteCount);
            }

            return payload;
        }

        public TestSettings Deserialize(ReadOnlyMemory<byte> payload)
        {
            DeserializeCount++;
            if (ThrowOnDeserialize)
            {
                throw new InvalidOperationException("Injected deserialization failure.");
            }

            string text = Encoding.UTF8.GetString(payload.ToArray());
            string[] parts = text.Split('|');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new FormatException("The test payload is malformed.");
            }

            return new TestSettings
            {
                Version = version,
                Value = value
            };
        }

        private static byte[] Clone(byte[] source)
        {
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    internal sealed class TestSettingsSchema : ISettingsSchema<TestSettings>
    {
        public TestSettingsSchema(int currentVersion = 2, int defaultValue = 7)
        {
            CurrentVersion = currentVersion;
            DefaultValue = defaultValue;
        }

        public int CurrentVersion { get; }

        public int DefaultValue { get; set; }

        public int MigrationValueDelta { get; set; } = 100;

        public bool FailMigration { get; set; }

        public bool ThrowOnMigration { get; set; }

        public bool ThrowOnValidation { get; set; }

        public bool ThrowOnGetVersion { get; set; }

        public bool ThrowOnClone { get; set; }

        public int MigrationCount { get; private set; }

        public int LastMigrationSourceVersion { get; private set; } = -1;

        public int LastMigrationTargetVersion { get; private set; } = -1;

        public TestSettings CreateDefault()
        {
            return new TestSettings
            {
                Version = CurrentVersion,
                Value = DefaultValue
            };
        }

        public TestSettings Clone(in TestSettings settings)
        {
            if (ThrowOnClone)
            {
                throw new InvalidOperationException("Injected snapshot failure.");
            }

            return settings;
        }

        public int GetVersion(in TestSettings settings)
        {
            if (ThrowOnGetVersion)
            {
                throw new InvalidOperationException("Injected version discovery failure.");
            }

            return settings.Version;
        }

        public SettingsValidationResult Validate(in TestSettings settings)
        {
            if (ThrowOnValidation)
            {
                throw new InvalidOperationException("Injected validation failure.");
            }

            return settings.Value >= 0
                ? SettingsValidationResult.Valid()
                : SettingsValidationResult.Invalid("Value must be non-negative.");
        }

        public SettingsMigrationResult Migrate(
            int sourceVersion,
            int targetVersion,
            ref TestSettings settings)
        {
            MigrationCount++;
            LastMigrationSourceVersion = sourceVersion;
            LastMigrationTargetVersion = targetVersion;

            if (ThrowOnMigration)
            {
                throw new InvalidOperationException("Injected migration exception.");
            }

            if (FailMigration)
            {
                return SettingsMigrationResult.Failure("Injected migration failure.");
            }

            settings.Version = targetVersion;
            settings.Value += MigrationValueDelta;
            return SettingsMigrationResult.Success();
        }
    }

    internal struct MutableSettings
    {
        public int Version;
        public int[] Values;
    }

    internal sealed class MutableSettingsCodec : ISettingsCodec<MutableSettings>
    {
        public byte[] Serialize(in MutableSettings settings, int maxByteCount)
        {
            string values = settings.Values == null
                ? string.Empty
                : string.Join(",", settings.Values);
            byte[] payload = Encoding.UTF8.GetBytes(string.Concat(
                settings.Version.ToString(CultureInfo.InvariantCulture),
                "|",
                values));
            if (payload.Length > maxByteCount)
            {
                throw new SettingsPayloadBudgetExceededException(maxByteCount);
            }

            return payload;
        }

        public MutableSettings Deserialize(ReadOnlyMemory<byte> payload)
        {
            string text = Encoding.UTF8.GetString(payload.ToArray());
            string[] parts = text.Split('|');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
            {
                throw new FormatException("The mutable test payload is malformed.");
            }

            string[] serializedValues = parts[1].Length == 0
                ? Array.Empty<string>()
                : parts[1].Split(',');
            var values = new int[serializedValues.Length];
            for (int index = 0; index < serializedValues.Length; index++)
            {
                if (!int.TryParse(
                        serializedValues[index],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out values[index]))
                {
                    throw new FormatException("The mutable test payload is malformed.");
                }
            }

            return new MutableSettings
            {
                Version = version,
                Values = values
            };
        }
    }

    internal sealed class MutableSettingsSchema : ISettingsSchema<MutableSettings>
    {
        private readonly int[] _defaultValues;

        public MutableSettingsSchema(params int[] defaultValues)
        {
            _defaultValues = CloneArray(defaultValues ?? Array.Empty<int>());
        }

        public int CurrentVersion => 1;

        public MutableSettings CreateDefault()
        {
            return new MutableSettings
            {
                Version = CurrentVersion,
                Values = CloneArray(_defaultValues)
            };
        }

        public MutableSettings Clone(in MutableSettings settings)
        {
            return new MutableSettings
            {
                Version = settings.Version,
                Values = settings.Values == null ? null : CloneArray(settings.Values)
            };
        }

        public int GetVersion(in MutableSettings settings)
        {
            return settings.Version;
        }

        public SettingsValidationResult Validate(in MutableSettings settings)
        {
            if (settings.Values == null)
            {
                return SettingsValidationResult.Invalid("Values cannot be null.");
            }

            for (int index = 0; index < settings.Values.Length; index++)
            {
                if (settings.Values[index] < 0)
                {
                    return SettingsValidationResult.Invalid("Values must be non-negative.");
                }
            }

            return SettingsValidationResult.Valid();
        }

        public SettingsMigrationResult Migrate(
            int sourceVersion,
            int targetVersion,
            ref MutableSettings settings)
        {
            settings.Version = targetVersion;
            return SettingsMigrationResult.Success();
        }

        private static int[] CloneArray(int[] source)
        {
            var copy = new int[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }

    internal sealed class FakeSettingsStorage :
        ISettingsStorage,
        ILegacySettingsChecksumStorage
    {
        private byte[] _content;
        private byte[] _legacyChecksum;

        public string Location { get; set; } = "memory://settings";

        public Exception LengthFailure { get; set; }

        public long? LengthOverride { get; set; }

        public Exception ReadFailure { get; set; }

        public Exception AtomicWriteFailure { get; set; }

        public Exception LegacyReadFailure { get; set; }

        public Exception LegacyDeleteFailure { get; set; }

        public int LengthCount { get; private set; }

        public int ReadCount { get; private set; }

        public int AtomicWriteCount { get; private set; }

        public int LegacyReadCount { get; private set; }

        public int LegacyDeleteCount { get; private set; }

        public bool HasContent => _content != null;

        public bool HasLegacyChecksum => _legacyChecksum != null;

        public byte[] LastAtomicWriteContent { get; private set; }

        public long GetLength()
        {
            LengthCount++;
            if (LengthFailure != null)
            {
                throw LengthFailure;
            }

            return LengthOverride ?? GetRequiredContent().LongLength;
        }

        public byte[] Read(int maxByteCount)
        {
            ReadCount++;
            if (ReadFailure != null)
            {
                throw ReadFailure;
            }

            byte[] content = GetRequiredContent();
            if (content.Length > maxByteCount)
            {
                throw new IOException("The fake settings content exceeds the requested read budget.");
            }

            return Clone(content);
        }

        public void WriteAtomically(byte[] content)
        {
            AtomicWriteCount++;
            if (AtomicWriteFailure != null)
            {
                throw AtomicWriteFailure;
            }

            byte[] copy = CloneRequired(content);
            _content = copy;
            LastAtomicWriteContent = Clone(copy);
        }

        public byte[] ReadLegacyChecksum(int maxByteCount)
        {
            LegacyReadCount++;
            if (LegacyReadFailure != null)
            {
                throw LegacyReadFailure;
            }

            if (_legacyChecksum == null)
            {
                throw new SettingsStorageEntryNotFoundException(
                    Location + ".checksum");
            }

            if (_legacyChecksum.Length > maxByteCount)
            {
                throw new IOException("The fake legacy checksum exceeds the requested read budget.");
            }

            return Clone(_legacyChecksum);
        }

        public void DeleteLegacyChecksum()
        {
            LegacyDeleteCount++;
            if (LegacyDeleteFailure != null)
            {
                throw LegacyDeleteFailure;
            }

            _legacyChecksum = null;
        }

        public void SetContent(byte[] content)
        {
            _content = CloneRequired(content);
        }

        public byte[] GetContentCopy()
        {
            return Clone(GetRequiredContent());
        }

        public void SetLegacyChecksum(byte[] content)
        {
            _legacyChecksum = CloneRequired(content);
        }

        private byte[] GetRequiredContent()
        {
            if (_content == null)
            {
                throw new SettingsStorageEntryNotFoundException(Location);
            }

            return _content;
        }

        private static byte[] CloneRequired(byte[] source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return Clone(source);
        }

        private static byte[] Clone(byte[] source)
        {
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    internal static class SettingsStoreTestData
    {
        private const string MagicLine = "# CycloneGames.Services Settings";

        public static byte[] Payload(int version, int value)
        {
            return Encoding.UTF8.GetBytes(string.Concat(
                version.ToString(CultureInfo.InvariantCulture),
                "|",
                value.ToString(CultureInfo.InvariantCulture)));
        }

        public static byte[] Envelope(
            int schemaVersion,
            byte[] payload,
            int formatVersion = 1,
            ulong? checksumOverride = null)
        {
            ulong checksum = checksumOverride ?? XxHash64.HashToUInt64(payload);
            string header = string.Concat(
                MagicLine,
                "\n# format: ",
                formatVersion.ToString(CultureInfo.InvariantCulture),
                "\n# schema: ",
                schemaVersion.ToString(CultureInfo.InvariantCulture),
                "\n# xxh64: ",
                checksum.ToString("X16", CultureInfo.InvariantCulture),
                "\n---\n");
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            var envelope = new byte[headerBytes.Length + payload.Length];
            Buffer.BlockCopy(headerBytes, 0, envelope, 0, headerBytes.Length);
            Buffer.BlockCopy(payload, 0, envelope, headerBytes.Length, payload.Length);
            return envelope;
        }

        public static byte[] EnvelopeWithCrLfHeader(int schemaVersion, byte[] payload)
        {
            ulong checksum = XxHash64.HashToUInt64(payload);
            string header = string.Concat(
                MagicLine,
                "\r\n# format: 1",
                "\r\n# schema: ",
                schemaVersion.ToString(CultureInfo.InvariantCulture),
                "\r\n# xxh64: ",
                checksum.ToString("X16", CultureInfo.InvariantCulture),
                "\r\n---\r\n");
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            var envelope = new byte[headerBytes.Length + payload.Length];
            Buffer.BlockCopy(headerBytes, 0, envelope, 0, headerBytes.Length);
            Buffer.BlockCopy(payload, 0, envelope, headerBytes.Length, payload.Length);
            return envelope;
        }

        public static byte[] LegacyChecksum(byte[] payload, bool matches)
        {
            ulong checksum = XxHash64.HashToUInt64(payload);
            if (!matches)
            {
                checksum ^= 1UL;
            }

            return Encoding.ASCII.GetBytes(
                checksum.ToString("X16", CultureInfo.InvariantCulture));
        }
    }
}
