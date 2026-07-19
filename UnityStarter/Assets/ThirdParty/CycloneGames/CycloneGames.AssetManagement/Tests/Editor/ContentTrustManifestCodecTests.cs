using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

using CycloneGames.AssetManagement.Runtime.Trust;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class ContentTrustManifestCodecTests
    {
        [Test]
        public void Materialized_Canonical_Payload_Has_A_Hard_Memory_Ceiling()
        {
            Assert.Throws<InvalidOperationException>(() =>
                ContentTrustManifestCanonicalPayload.ThrowIfMaterializedPayloadTooLarge(
                    ContentTrustManifestCanonicalPayload.MAX_MATERIALIZED_PAYLOAD_BYTES + 1));
        }

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        [Test]
        public void Builder_AddFile_Computes_Hash_And_Sorts_Entries()
        {
            string root = CreateTempRoot();
            try
            {
                WriteFile(root, "b.bundle", "content-b");
                WriteFile(root, "a.bundle", "content-a");

                ContentTrustManifest manifest = new ContentTrustManifestBuilder()
                    .WithVersion("2026.07.09")
                    .AddFile(root, "b.bundle")
                    .AddFile(root, "a.bundle")
                    .Build();

                Assert.AreEqual("2026.07.09", manifest.Version);
                Assert.AreEqual("a.bundle", manifest.Entries[0].Location);
                Assert.AreEqual("b.bundle", manifest.Entries[1].Location);
                Assert.AreEqual(ContentTrustHashAlgorithm.Sha256, manifest.Entries[0].HashAlgorithm);
                Assert.AreEqual(64, manifest.Entries[0].ExpectedHashHex.Length);
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void Manifest_Defensively_Copies_Normalizes_And_Canonically_Sorts_Entries()
        {
            var sourceEntries = new[]
            {
                new ContentTrustFileEntry("z\\bundle.bin", 2L, ContentTrustHashAlgorithm.None, null),
                new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.Sha256, new string('A', 64))
            };

            var manifest = new ContentTrustManifest(
                " 2026.07.09 ",
                sourceEntries,
                contentRoot: " Content\\Root ");
            sourceEntries[0] = new ContentTrustFileEntry("mutated.bundle", 99L, ContentTrustHashAlgorithm.None, null);

            Assert.AreEqual("2026.07.09", manifest.Version);
            Assert.AreEqual("Content/Root", manifest.ContentRoot);
            Assert.AreEqual("a.bundle", manifest.Entries[0].Location);
            Assert.AreEqual(new string('a', 64), manifest.Entries[0].ExpectedHashHex);
            Assert.AreEqual("z/bundle.bin", manifest.Entries[1].Location);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ContentTrustFileEntry>)manifest.Entries)[0] = sourceEntries[0]);
        }

        [Test]
        public void Manifest_Rejects_Case_Insensitive_Duplicate_Locations()
        {
            Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("Content/A.bundle", 1L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("content/a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                }));
        }

        [Test]
        public void Manifest_Rejects_Unicode_Normalization_Duplicate_Locations()
        {
            Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("caf\u00e9.bundle", 1L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("cafe\u0301.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                }));
        }

        [TestCase("../outside.bundle")]
        [TestCase("folder/../outside.bundle")]
        [TestCase("folder/./asset.bundle")]
        [TestCase("folder//asset.bundle")]
        [TestCase("/absolute.bundle")]
        [TestCase("C:/absolute.bundle")]
        [TestCase("NUL.bundle")]
        public void Manifest_Rejects_NonPortable_Or_Unsafe_Paths(string location)
        {
            Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry(location, 1L, ContentTrustHashAlgorithm.None, null)
                }));
        }

        [Test]
        public void Manifest_Rejects_Negative_Entry_Size()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("asset.bundle", -1L, ContentTrustHashAlgorithm.None, null)
                }));
        }

        [Test]
        public void Manifest_Enforces_All_Field_Length_Limits()
        {
            var emptyEntries = Array.Empty<ContentTrustFileEntry>();

                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    new string('v', ContentTrustManifestValidation.MAX_VERSION_UTF8_BYTES + 1),
                    emptyEntries));
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    new string('\u754c', (ContentTrustManifestValidation.MAX_VERSION_UTF8_BYTES / 3) + 1),
                    emptyEntries));
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    "invalid\nversion",
                    emptyEntries));
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    "version",
                    emptyEntries,
                    contentRoot: new string('c', ContentTrustManifestValidation.MAX_CONTENT_ROOT_UTF8_BYTES + 1)));
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    "version",
                    emptyEntries,
                    signature: new string('s', ContentTrustManifestValidation.MAX_SIGNATURE_UTF8_BYTES + 1)));
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    "version",
                    new[]
                    {
                        new ContentTrustFileEntry(
                            new string('p', ContentTrustManifestValidation.MAX_ENTRY_LOCATION_UTF8_BYTES + 1),
                            1L,
                            ContentTrustHashAlgorithm.None,
                            null)
                    }));
        }

        [Test]
        public void Manifest_Enforces_Entry_Count_Limit_Before_Enumeration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContentTrustManifest(
                "2026.07.09",
                new OversizedEntryList()));
        }

        [Test]
        public void Manifest_Enforces_Total_Content_Size_Limit()
        {
            var builder = new ContentTrustManifestBuilder().WithVersion("2026.07.09");
            builder.AddEntry(new ContentTrustFileEntry(
                "maximum.bundle",
                ContentTrustManifestValidation.MAX_TOTAL_CONTENT_SIZE_BYTES,
                ContentTrustHashAlgorithm.None,
                null));

            Assert.Throws<InvalidOperationException>(() => builder.AddEntry(new ContentTrustFileEntry(
                "overflow.bundle",
                1L,
                ContentTrustHashAlgorithm.None,
                null)));
            builder.ClearEntries();
            Assert.DoesNotThrow(() => builder.AddEntry(new ContentTrustFileEntry(
                "reset.bundle",
                1L,
                ContentTrustHashAlgorithm.None,
                null)));

            var maximumManifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry(
                        "maximum.bundle",
                        ContentTrustManifestValidation.MAX_TOTAL_CONTENT_SIZE_BYTES,
                        ContentTrustHashAlgorithm.None,
                        null)
                });

            Assert.AreEqual(
                ContentTrustManifestValidation.MAX_TOTAL_CONTENT_SIZE_BYTES,
                maximumManifest.Entries[0].SizeBytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry(
                        "asset.bundle",
                        ContentTrustManifestValidation.MAX_TOTAL_CONTENT_SIZE_BYTES + 1L,
                        ContentTrustHashAlgorithm.None,
                        null)
                }));
        }

        [Test]
        public void Manifest_Rejects_Invalid_Expected_Hash_Fields()
        {
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    "2026.07.09",
                    new[]
                    {
                        new ContentTrustFileEntry("asset.bundle", 1L, ContentTrustHashAlgorithm.None, "00")
                    }));
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    "2026.07.09",
                    new[]
                    {
                        new ContentTrustFileEntry("asset.bundle", 1L, ContentTrustHashAlgorithm.Sha256, "00")
                    }));
                Assert.Throws<ArgumentException>(() => new ContentTrustManifest(
                    "2026.07.09",
                    new[]
                    {
                        new ContentTrustFileEntry(
                            "asset.bundle",
                            1L,
                            ContentTrustHashAlgorithm.Sha256,
                            new string('z', 64))
                    }));
        }

        [Test]
        public void Codec_RoundTrips_Json_Document()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("bundles/ui.bundle", 42L, ContentTrustHashAlgorithm.XxHash64, "0123456789abcdef")
                },
                contentRoot: "content",
                signature: "signed-payload");

            string json = ContentTrustManifestCodec.ToJson(in manifest);
            ContentTrustManifest parsed = ContentTrustManifestCodec.FromJson(json);

            Assert.AreEqual(manifest.Version, parsed.Version);
            Assert.AreEqual(manifest.ContentRoot, parsed.ContentRoot);
            Assert.AreEqual(manifest.Signature, parsed.Signature);
            Assert.AreEqual(1, parsed.Entries.Count);
            Assert.AreEqual(ContentTrustHashAlgorithm.XxHash64, parsed.Entries[0].HashAlgorithm);
        }

        [Test]
        public void Codec_Json_Is_Independent_Of_Entry_Input_Order()
        {
            var first = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("b.bundle", 2L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                });
            var second = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("b.bundle", 2L, ContentTrustHashAlgorithm.None, null)
                });

            Assert.AreEqual(
                ContentTrustManifestCodec.ToJson(in first),
                ContentTrustManifestCodec.ToJson(in second));
        }

        [Test]
        public void Codec_Json_Character_Limit_Is_Inclusive()
        {
            Assert.IsTrue(ContentTrustManifestValidation.IsJsonCharCountWithinLimit(
                ContentTrustManifestValidation.MAX_JSON_CHAR_COUNT));
            Assert.IsFalse(ContentTrustManifestValidation.IsJsonCharCountWithinLimit(
                ContentTrustManifestValidation.MAX_JSON_CHAR_COUNT + 1));
        }

        [Test]
        public void Codec_Json_Character_Preflight_Matches_Escaped_Output()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("content/a.bundle", 12345L, ContentTrustHashAlgorithm.None, null)
                },
                signature: "signature\"\\\nvalue");

            string withSignature = ContentTrustManifestCodec.ToJson(in manifest, includeSignature: true);
            string withoutSignature = ContentTrustManifestCodec.ToJson(in manifest, includeSignature: false);

            Assert.AreEqual(
                withSignature.Length,
                ContentTrustManifestCodec.GetJsonCharCount(in manifest, includeSignature: true));
            Assert.AreEqual(
                withoutSignature.Length,
                ContentTrustManifestCodec.GetJsonCharCount(in manifest, includeSignature: false));
        }

        [Test]
        public void Codec_Rejects_Overlong_Manifest_Field()
        {
            string version = new string('v', ContentTrustManifestValidation.MAX_VERSION_UTF8_BYTES + 1);
            string json =
                "{\"schemaVersion\":2,\"version\":\"" + version + "\",\"entries\":[]}";

            bool parsed = ContentTrustManifestCodec.TryFromJson(
                json,
                out ContentTrustManifest manifest,
                out string error);

            Assert.IsFalse(parsed);
            Assert.IsNull(manifest.Entries);
            StringAssert.Contains("limit", error.ToLowerInvariant());
        }

        [Test]
        public void Codec_Rejects_Duplicate_Locations_After_Normalization()
        {
            const string json =
                "{\"schemaVersion\":2,\"version\":\"2026.07.09\",\"entries\":[" +
                "{\"location\":\"content/a.bundle\",\"sizeBytes\":1,\"hashAlgorithm\":\"None\"}," +
                "{\"location\":\"Content\\\\A.bundle\",\"sizeBytes\":1,\"hashAlgorithm\":\"None\"}]}";

            bool parsed = ContentTrustManifestCodec.TryFromJson(
                json,
                out ContentTrustManifest manifest,
                out string error);

            Assert.IsFalse(parsed);
            Assert.IsNull(manifest.Entries);
            StringAssert.Contains("Duplicate", error);
        }

        [Test]
        public void Codec_RoundTrips_All_HashAlgorithm_Tokens()
        {
            foreach (ContentTrustHashAlgorithm algorithm in Enum.GetValues(typeof(ContentTrustHashAlgorithm)))
            {
                var manifest = new ContentTrustManifest(
                    "2026.07.09",
                    new[]
                    {
                        new ContentTrustFileEntry(
                            "bundles/ui.bundle",
                            42L,
                            algorithm,
                            GetValidExpectedHash(algorithm))
                    });

                string json = ContentTrustManifestCodec.ToJson(in manifest);
                ContentTrustManifest parsed = ContentTrustManifestCodec.FromJson(json);

                Assert.AreEqual(algorithm, parsed.Entries[0].HashAlgorithm);
            }
        }

        [Test]
        public void Codec_Rejects_Entry_With_Missing_HashAlgorithm()
        {
            const string json =
                "{\"schemaVersion\":2,\"version\":\"2026.07.09\",\"entries\":[{\"location\":\"bundles/ui.bundle\",\"sizeBytes\":42,\"expectedHashHex\":\"0123456789abcdef\"}]}";

            bool parsed = ContentTrustManifestCodec.TryFromJson(json, out ContentTrustManifest manifest, out string error);

            Assert.IsFalse(parsed);
            Assert.IsNull(manifest.Entries);
            StringAssert.Contains("Unsupported content trust hash algorithm", error);
        }

        [Test]
        public void Codec_Rejects_Entry_With_Unsupported_HashAlgorithm()
        {
            const string json =
                "{\"schemaVersion\":2,\"version\":\"2026.07.09\",\"entries\":[{\"location\":\"bundles/ui.bundle\",\"sizeBytes\":42,\"hashAlgorithm\":\"sha256\",\"expectedHashHex\":\"0123456789abcdef\"}]}";

            bool parsed = ContentTrustManifestCodec.TryFromJson(json, out ContentTrustManifest manifest, out string error);

            Assert.IsFalse(parsed);
            Assert.IsNull(manifest.Entries);
            StringAssert.Contains("Unsupported content trust hash algorithm", error);
        }

        [Test]
        public void Codec_Rejects_Overlong_HashAlgorithm_Token()
        {
            string token = new string('S', ContentTrustManifestValidation.MAX_HASH_ALGORITHM_TOKEN_LENGTH + 1);
            string json =
                "{\"schemaVersion\":2,\"version\":\"2026.07.09\",\"entries\":[{" +
                "\"location\":\"bundles/ui.bundle\",\"sizeBytes\":42,\"hashAlgorithm\":\"" + token + "\"}]}";

            bool parsed = ContentTrustManifestCodec.TryFromJson(
                json,
                out ContentTrustManifest manifest,
                out string error);

            Assert.IsFalse(parsed);
            Assert.IsNull(manifest.Entries);
            StringAssert.Contains("Unsupported content trust hash algorithm", error);
        }

        [Test]
        public void Manifest_Rejects_Unsupported_HashAlgorithm_On_Construction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry(
                        "bundles/ui.bundle",
                        42L,
                        (ContentTrustHashAlgorithm)byte.MaxValue,
                        "0123456789abcdef")
                }));
        }

        [Test]
        public void Codec_AppendJson_Matches_ToJson()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("b.bundle", 2L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                },
                signature: "signed-payload");
            var builder = new StringBuilder();

            ContentTrustManifestCodec.AppendJson(builder, in manifest);

            Assert.AreEqual(ContentTrustManifestCodec.ToJson(in manifest), builder.ToString());
        }

        [Test]
        public void CanonicalPayload_Excludes_Signature_And_Sorts_Entries()
        {
            var first = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("b.bundle", 2L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                },
                signature: "first-signature");
            var second = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("b.bundle", 2L, ContentTrustHashAlgorithm.None, null)
                },
                signature: "second-signature");

            byte[] firstPayload = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in first);
            byte[] secondPayload = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in second);

            Assert.AreEqual(
                firstPayload.Length,
                ContentTrustManifestCanonicalPayload.GetByteCount(in first));
            CollectionAssert.AreEqual(firstPayload, secondPayload);
        }

        [Test]
        public void CanonicalPayload_WriteTo_Matches_ToBytes()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("b.bundle", 2L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                });
            byte[] expected = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in manifest);

            using (var stream = new MemoryStream())
            {
                ContentTrustManifestCanonicalPayload.WriteTo(in manifest, stream);

                CollectionAssert.AreEqual(expected, stream.ToArray());
            }
        }

        [Test]
        public void SignatureUtility_Writes_Signer_Result_Without_Changing_Canonical_Payload()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("bundle.bin", 1L, ContentTrustHashAlgorithm.None, null)
                });
            byte[] before = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in manifest);

            ContentTrustManifest signed = ContentTrustManifestSignatureUtility.Sign(in manifest, new CountingSigner());
            byte[] after = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in signed);

            Assert.AreEqual("signature-bytes-" + before.Length, signed.Signature);
            Assert.AreSame(manifest.Entries, signed.Entries);
            CollectionAssert.AreEqual(before, after);
        }

        [Test]
        public void SignatureUtility_CanonicalSigner_Can_Control_Payload_Buffering()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("bundle.bin", 1L, ContentTrustHashAlgorithm.None, null)
                });

            ContentTrustManifest signed = ContentTrustManifestSignatureUtility.SignCanonical(in manifest, new CanonicalStreamSigner());
            byte[] canonicalPayload = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in manifest);

            Assert.AreEqual("canonical-stream-" + canonicalPayload.Length, signed.Signature);
        }

        private static void WriteFile(string root, string relativePath, string value)
        {
            string path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Utf8NoBom.GetBytes(value));
        }

        private static string GetValidExpectedHash(ContentTrustHashAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case ContentTrustHashAlgorithm.None:
                    return null;
                case ContentTrustHashAlgorithm.Sha256:
                    return new string('0', 64);
                case ContentTrustHashAlgorithm.XxHash64:
                    return new string('0', 16);
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null);
            }
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.ManifestCodecTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteTempRoot(string root)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private sealed class CountingSigner : IContentTrustManifestSigner
        {
            public string Sign(byte[] canonicalPayload)
            {
                return "signature-bytes-" + canonicalPayload.Length;
            }
        }

        private sealed class CanonicalStreamSigner : IContentTrustManifestCanonicalSigner
        {
            public string SignCanonicalManifest(in ContentTrustManifest manifest)
            {
                using (var stream = new MemoryStream())
                {
                    ContentTrustManifestCanonicalPayload.WriteTo(in manifest, stream);
                    return "canonical-stream-" + stream.Length;
                }
            }
        }

        private sealed class OversizedEntryList : IReadOnlyList<ContentTrustFileEntry>
        {
            public int Count => ContentTrustManifestValidation.MAX_ENTRY_COUNT + 1;

            public ContentTrustFileEntry this[int index] =>
                throw new InvalidOperationException("An oversized list must be rejected before enumeration.");

            public IEnumerator<ContentTrustFileEntry> GetEnumerator()
            {
                throw new InvalidOperationException("An oversized list must be rejected before enumeration.");
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
