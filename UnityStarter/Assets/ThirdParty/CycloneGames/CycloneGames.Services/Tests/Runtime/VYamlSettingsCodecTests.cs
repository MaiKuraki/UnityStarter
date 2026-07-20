using System;
using CycloneGames.Services.Unity;
using NUnit.Framework;
using UnityEngine;
using VYaml.Annotations;
using VYaml.Serialization;

namespace CycloneGames.Services.Tests.Runtime
{
    public sealed class VYamlSettingsCodecTests
    {
        [Test]
        public void Codec_RoundTripsGeneratedSettingsAndUnityValue()
        {
            var codec = new VYamlSettingsCodec<TestYamlSettings>(GeneratedResolver.Instance);
            var expected = new TestYamlSettings
            {
                Version = 3,
                Volume = 0.75f,
                Anchor = new Vector2(12.5f, -4f)
            };

            byte[] payload = codec.Serialize(in expected, 4096);
            TestYamlSettings actual = codec.Deserialize(payload);

            Assert.AreEqual(expected.Version, actual.Version);
            Assert.AreEqual(expected.Volume, actual.Volume);
            Assert.AreEqual(expected.Anchor, actual.Anchor);
        }

        [Test]
        public void Codec_SerializationBudget_FailsBeforeGrowingBeyondBudget()
        {
            var codec = new VYamlSettingsCodec<TestYamlSettings>(GeneratedResolver.Instance);
            var settings = new TestYamlSettings
            {
                Version = 3,
                Volume = 0.5f,
                Anchor = new Vector2(1f, 2f)
            };

            Assert.Throws<SettingsPayloadBudgetExceededException>(() =>
                codec.Serialize(settings, 8));
        }

        [Test]
        public void Codec_DeserializeFailure_DoesNotPoisonLaterDeserialization()
        {
            var codec = new VYamlSettingsCodec<TestYamlSettings>(GeneratedResolver.Instance);
            byte[] malformed = { (byte)'[', (byte)'1' };

            Assert.Catch<Exception>(() => codec.Deserialize(malformed));

            var expected = new TestYamlSettings
            {
                Version = 3,
                Volume = 0.25f,
                Anchor = new Vector2(-2f, 9f)
            };
            byte[] payload = codec.Serialize(in expected, 4096);
            TestYamlSettings actual = codec.Deserialize(payload);

            Assert.AreEqual(expected.Version, actual.Version);
            Assert.AreEqual(expected.Volume, actual.Volume);
            Assert.AreEqual(expected.Anchor, actual.Anchor);
        }

        [TestCase("../outside.yaml")]
        [TestCase("Settings/../../outside.yaml")]
        [TestCase("Settings//device.yaml")]
        [TestCase("CON.yaml")]
        public void UnityFactory_RejectsNonPortableRelativePath(string relativePath)
        {
            var schema = new TestYamlSchema();

            Assert.Throws<ArgumentException>(() => UnityPersistentSettings.CreateYaml(
                relativePath,
                schema,
                GeneratedResolver.Instance));
        }

        [Test]
        public void SystemFileSettingsStorage_RejectsRelativePath()
        {
            Assert.Throws<ArgumentException>(() =>
                new SystemFileSettingsStorage("Settings/device.yaml"));
        }

        [Test]
        public void Load_DamagedEnvelopeMagic_IsNotAcceptedAsLegacyYaml()
        {
            var storage = new MemorySettingsStorage();
            var schema = new TestYamlSchema();
            using (SettingsStore<TestYamlSettings> writer = UnityPersistentSettings.CreateYaml(
                       storage,
                       schema,
                       GeneratedResolver.Instance))
            {
                Assert.That(writer.Save().Succeeded, Is.True);
            }

            storage.CorruptByte(10);

            using (SettingsStore<TestYamlSettings> reader = UnityPersistentSettings.CreateYaml(
                       storage,
                       schema,
                       GeneratedResolver.Instance))
            {
                SettingsLoadResult result = reader.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.CorruptedEnvelope));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.EnvelopeV1));
            }
        }

        private sealed class TestYamlSchema : ISettingsSchema<TestYamlSettings>
        {
            public int CurrentVersion => 3;

            public TestYamlSettings CreateDefault()
            {
                return new TestYamlSettings { Version = CurrentVersion };
            }

            public TestYamlSettings Clone(in TestYamlSettings settings)
            {
                return settings;
            }

            public int GetVersion(in TestYamlSettings settings)
            {
                return settings.Version;
            }

            public SettingsValidationResult Validate(in TestYamlSettings settings)
            {
                return settings.Volume >= 0f && settings.Volume <= 1f
                    ? SettingsValidationResult.Valid()
                    : SettingsValidationResult.Invalid("Volume must be in the inclusive range [0, 1].");
            }

            public SettingsMigrationResult Migrate(
                int sourceVersion,
                int targetVersion,
                ref TestYamlSettings settings)
            {
                settings.Version = targetVersion;
                return SettingsMigrationResult.Success();
            }
        }

        private sealed class MemorySettingsStorage : ISettingsStorage
        {
            private byte[] _content;

            public string Location => "memory://vyaml-test";

            public long GetLength()
            {
                if (_content == null)
                {
                    throw new SettingsStorageEntryNotFoundException(Location);
                }

                return _content.Length;
            }

            public byte[] Read(int maxByteCount)
            {
                if (_content == null)
                {
                    throw new SettingsStorageEntryNotFoundException(Location);
                }

                if (_content.Length > maxByteCount)
                {
                    throw new InvalidOperationException("The content exceeds the test read budget.");
                }

                return (byte[])_content.Clone();
            }

            public void WriteAtomically(byte[] content)
            {
                _content = (byte[])content.Clone();
            }

            public void CorruptByte(int offset)
            {
                _content[offset] ^= 1;
            }
        }
    }

    [YamlObject]
    public partial struct TestYamlSettings
    {
        public int Version;
        public float Volume;
        public Vector2 Anchor;
    }
}
