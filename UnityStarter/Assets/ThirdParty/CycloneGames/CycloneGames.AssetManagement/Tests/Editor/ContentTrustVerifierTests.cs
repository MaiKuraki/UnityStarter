using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CycloneGames.AssetManagement.Runtime.Trust;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class ContentTrustVerifierTests
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        [Test]
        public void VerifyBytes_Accepts_Matching_Sha256_And_Size()
        {
            byte[] bytes = Utf8NoBom.GetBytes("trusted content");
            var entry = new ContentTrustFileEntry(
                "bundles/ui.bundle",
                bytes.LongLength,
                ContentTrustHashAlgorithm.Sha256,
                ComputeSha256Hex(bytes));

            ContentTrustVerificationResult result = ContentTrustVerifier.Shared.VerifyBytes(bytes, entry);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(ContentTrustFailure.None, result.Failure);
        }

        [Test]
        public void VerifyBytes_Accepts_Buffer_Slice_Without_Copy()
        {
            byte[] payload = Utf8NoBom.GetBytes("trusted content");
            byte[] buffer = Utf8NoBom.GetBytes("prefix--trusted content--suffix");
            var entry = new ContentTrustFileEntry(
                "bundles/ui.bundle",
                payload.LongLength,
                ContentTrustHashAlgorithm.Sha256,
                ComputeSha256Hex(payload));

            ContentTrustVerificationResult result = ContentTrustVerifier.Shared.VerifyBytes(
                buffer.AsSpan(8, payload.Length),
                entry);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(ContentTrustFailure.None, result.Failure);
        }

        [Test]
        public void VerifyBytes_Accepts_Uppercase_Expected_Hash()
        {
            byte[] bytes = Utf8NoBom.GetBytes("trusted content");
            var entry = new ContentTrustFileEntry(
                "bundles/ui.bundle",
                bytes.LongLength,
                ContentTrustHashAlgorithm.Sha256,
                ComputeSha256Hex(bytes).ToUpperInvariant());

            ContentTrustVerificationResult result = ContentTrustVerifier.Shared.VerifyBytes(bytes, entry);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(ContentTrustFailure.None, result.Failure);
        }

        [Test]
        public void VerifyBytes_Accepts_Matching_XxHash64()
        {
            byte[] bytes = Utf8NoBom.GetBytes("Hello");
            var entry = new ContentTrustFileEntry(
                "bundles/ui.bundle",
                bytes.LongLength,
                ContentTrustHashAlgorithm.XxHash64,
                "0a75a91375b27d44");

            ContentTrustVerificationResult result = ContentTrustVerifier.Shared.VerifyBytes(bytes, entry);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(ContentTrustFailure.None, result.Failure);
        }

        [Test]
        public void VerifyBytes_Rejects_Hash_Mismatch()
        {
            byte[] bytes = Utf8NoBom.GetBytes("trusted content");
            byte[] otherBytes = Utf8NoBom.GetBytes("tampered content");
            var entry = new ContentTrustFileEntry(
                "bundles/ui.bundle",
                bytes.LongLength,
                ContentTrustHashAlgorithm.Sha256,
                ComputeSha256Hex(otherBytes));

            ContentTrustVerificationResult result = ContentTrustVerifier.Shared.VerifyBytes(bytes, entry);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ContentTrustFailure.HashMismatch, result.Failure);
        }

        [Test]
        public void VerifyFile_Accepts_Matching_File()
        {
            string root = CreateTempRoot();
            try
            {
                byte[] bytes = Utf8NoBom.GetBytes("trusted file content");
                string filePath = Path.Combine(root, "bundles", "ui.bundle");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllBytes(filePath, bytes);

                var entry = new ContentTrustFileEntry(
                    "bundles/ui.bundle",
                    bytes.LongLength,
                    ContentTrustHashAlgorithm.Sha256,
                    ComputeSha256Hex(bytes));

                ContentTrustVerificationResult result = ContentTrustVerifier.Shared.VerifyFile(root, entry);

                Assert.IsTrue(result.Succeeded);
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void VerifyFile_Rejects_Path_Escaping_Root()
        {
            string root = CreateTempRoot();
            try
            {
                var entry = new ContentTrustFileEntry(
                    "../outside.bundle",
                    1L,
                    ContentTrustHashAlgorithm.None,
                    null);

                ContentTrustVerificationResult result = ContentTrustVerifier.Shared.VerifyFile(root, entry);

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(ContentTrustFailure.PathEscapesRoot, result.Failure);
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void VerifyManifestFiles_Rejects_Bad_Signature_Before_File_Check()
        {
            string root = CreateTempRoot();
            try
            {
                var manifest = new ContentTrustManifest(
                    "2026.07.09",
                    new[]
                    {
                        new ContentTrustFileEntry("missing.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                    });
                var failures = new List<ContentTrustVerificationResult>(1);
                var signatureVerifier = new RejectingSignatureVerifier();

                int failureCount = ContentTrustVerifier.Shared.VerifyManifestFiles(root, manifest, failures, signatureVerifier);

                Assert.AreEqual(1, failureCount);
                Assert.AreEqual(1, failures.Count);
                Assert.AreEqual(ContentTrustFailure.SignatureRejected, failures[0].Failure);
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void ComputeFingerprint_Changes_When_Entry_Changes()
        {
            var first = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                });
            var second = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("a.bundle", 2L, ContentTrustHashAlgorithm.None, null)
                });

            Assert.AreNotEqual(first.ComputeFingerprint(), second.ComputeFingerprint());
        }

        [Test]
        public void ComputeFingerprint_Ignores_Signature()
        {
            var first = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                },
                signature: "first-signature");
            var second = new ContentTrustManifest(
                "2026.07.09",
                new[]
                {
                    new ContentTrustFileEntry("a.bundle", 1L, ContentTrustHashAlgorithm.None, null)
                },
                signature: "second-signature");

            Assert.AreEqual(first.ComputeFingerprint(), second.ComputeFingerprint());
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.Tests", Guid.NewGuid().ToString("N"));
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

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHexLower(sha256.ComputeHash(bytes));
            }
        }

        private static string ToHexLower(byte[] bytes)
        {
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = bytes[i];
                chars[i * 2] = GetHexChar(value >> 4);
                chars[(i * 2) + 1] = GetHexChar(value & 0xF);
            }

            return new string(chars);
        }

        private static char GetHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }

        private sealed class RejectingSignatureVerifier : IContentTrustSignatureVerifier
        {
            public bool Verify(in ContentTrustManifest manifest, out string error)
            {
                error = "Rejected by test verifier.";
                return false;
            }
        }
    }
}
