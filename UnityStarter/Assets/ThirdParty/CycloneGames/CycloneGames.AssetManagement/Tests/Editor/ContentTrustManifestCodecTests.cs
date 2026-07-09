using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using CycloneGames.AssetManagement.Runtime.Trust;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class ContentTrustManifestCodecTests
    {
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
                    .WithRollbackVersion("2026.07.08")
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
        public void Codec_RoundTrips_Json_Document()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("bundles/ui.bundle", 42L, ContentTrustHashAlgorithm.XxHash64, "0123456789abcdef")
                },
                minimumClientVersion: "1.2.3",
                rollbackVersion: "2026.07.08",
                contentRoot: "content",
                signature: "signed-payload");

            string json = ContentTrustManifestCodec.ToJson(in manifest);
            ContentTrustManifest parsed = ContentTrustManifestCodec.FromJson(json);

            Assert.AreEqual(manifest.Version, parsed.Version);
            Assert.AreEqual(manifest.MinimumClientVersion, parsed.MinimumClientVersion);
            Assert.AreEqual(manifest.RollbackVersion, parsed.RollbackVersion);
            Assert.AreEqual(manifest.ContentRoot, parsed.ContentRoot);
            Assert.AreEqual(manifest.Signature, parsed.Signature);
            Assert.AreEqual(1, parsed.Entries.Count);
            Assert.AreEqual(ContentTrustHashAlgorithm.XxHash64, parsed.Entries[0].HashAlgorithm);
        }

        [Test]
        public void Codec_AppendJson_Matches_ToJson_And_Clears_Workspace()
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
            var workspace = new List<ContentTrustFileEntry>(4);

            ContentTrustManifestCodec.AppendJson(builder, in manifest, sortWorkspace: workspace);

            Assert.AreEqual(ContentTrustManifestCodec.ToJson(in manifest), builder.ToString());
            Assert.AreEqual(0, workspace.Count);
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

            CollectionAssert.AreEqual(firstPayload, secondPayload);
        }

        [Test]
        public void CanonicalPayload_WriteTo_Matches_ToBytes_And_Clears_Workspace()
        {
            var manifest = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("b.bundle", 2L, ContentTrustHashAlgorithm.None, null),
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                });
            byte[] expected = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in manifest);
            var workspace = new List<ContentTrustFileEntry>(4);

            using (var stream = new MemoryStream())
            {
                ContentTrustManifestCanonicalPayload.WriteTo(in manifest, stream, workspace);

                CollectionAssert.AreEqual(expected, stream.ToArray());
            }

            Assert.AreEqual(0, workspace.Count);
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
                    var workspace = new List<ContentTrustFileEntry>(4);
                    ContentTrustManifestCanonicalPayload.WriteTo(in manifest, stream, workspace);
                    return "canonical-stream-" + stream.Length;
                }
            }
        }
    }
}
