using System;
using System.IO;

using CycloneGames.AssetManagement.Runtime.Trust;

using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class ContentTrustManifestBuilderTests
    {
        [Test]
        public void AddEntry_Rejects_NonSha256_Algorithms()
        {
            var builder = new ContentTrustManifestBuilder();

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddEntry(new ContentTrustFileEntry(
                "a.bundle", 1L, ContentTrustHashAlgorithm.XxHash64, "0123456789abcdef")));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddEntry(new ContentTrustFileEntry(
                "a.bundle", 1L, ContentTrustHashAlgorithm.None, null)));
        }

        [Test]
        public void AddFile_Rejects_NonSha256_Algorithms()
        {
            string root = CreateTempRoot();
            try
            {
                File.WriteAllText(Path.Combine(root, "a.bundle"), "content");

                var builder = new ContentTrustManifestBuilder();

                Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddFile(root, "a.bundle", ContentTrustHashAlgorithm.XxHash64));
                Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddFile(root, "a.bundle", ContentTrustHashAlgorithm.None));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void AddFile_WithSha256_ComputesExpectedHash()
        {
            string root = CreateTempRoot();
            try
            {
                File.WriteAllText(Path.Combine(root, "a.bundle"), "content");

                var builder = new ContentTrustManifestBuilder().WithVersion("2026.08.15");
                builder.AddFile(root, "a.bundle");
                ContentTrustManifest manifest = builder.Build();

                Assert.That(manifest.Entries.Count, Is.EqualTo(1));
                Assert.That(manifest.Entries[0].HashAlgorithm, Is.EqualTo(ContentTrustHashAlgorithm.Sha256));
                Assert.That(manifest.Entries[0].ExpectedHashHex, Has.Length.EqualTo(64));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.ManifestBuilderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
