using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The manifest is committed and reviewed, so its format is part of the team's workflow: it has to
    /// diff cleanly line by line, and it has to be byte-identical whether it was written on a Windows
    /// artist machine or a Linux CI agent.
    /// </summary>
    [TestFixture]
    public sealed class AtlasManifestTests
    {
        private const string ExpectedManifest =
            "# CycloneGames atlas manifest. Generated file - do not edit by hand.\n"
            + "schema=2\n"
            + "generator=gen/1\n"
            + "settings=00255800013571c5\n"
            + "atlas=UI\tAssets/Atlas/UI.spriteatlasv2\t3\te640bae279033453\t1\t0\n";

        private static AtlasManifest BuildSample()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/zeta.png", "UI", false);
            index.Add("Assets/UI/alpha.png", "UI", false);
            index.Add("Assets/UI/mid/beta.png", "UI", false);

            return new AtlasManifest(
                AtlasManifest.CurrentSchemaVersion,
                "gen/1",
                AtlasHash.Combine64(11L, 22L),
                new List<AtlasManifestEntry>
                {
                    new AtlasManifestEntry(
                        "UI",
                        "Assets/Atlas/UI.spriteatlasv2",
                        3,
                        index.GetBuckets()[0].GetPathHash(),
                        1,
                        0),
                },
                new Dictionary<string, long>());
        }

        [Test]
        public void Write_ProducesByteStableLineOrientedText()
        {
            // The expected text is a recorded golden value. If it changes, every committed manifest
            // becomes invalid at once, which is exactly what this test exists to prevent.
            Assert.AreEqual(ExpectedManifest, AtlasManifestSerializer.Write(BuildSample()));
        }

        [Test]
        public void Write_UsesLfOnly()
        {
            string text = AtlasManifestSerializer.Write(BuildSample());
            Assert.IsFalse(text.Contains("\r"), "CRLF would line-ending-churn every commit");
            Assert.IsTrue(text.EndsWith("\n", System.StringComparison.Ordinal));
        }

        [Test]
        public void Write_SortsEntriesByAtlasKey()
        {
            var manifest = new AtlasManifest(
                AtlasManifest.CurrentSchemaVersion,
                "gen/1",
                0L,
                new List<AtlasManifestEntry>
                {
                    new AtlasManifestEntry("Zebra", "p/z", 1, 1L, 1, 0),
                    new AtlasManifestEntry("Apple", "p/a", 1, 2L, 1, 0),
                    new AtlasManifestEntry("Mango", "p/m", 1, 3L, 1, 0),
                },
                new Dictionary<string, long>());

            string text = AtlasManifestSerializer.Write(manifest);
            int apple = text.IndexOf("atlas=Apple", System.StringComparison.Ordinal);
            int mango = text.IndexOf("atlas=Mango", System.StringComparison.Ordinal);
            int zebra = text.IndexOf("atlas=Zebra", System.StringComparison.Ordinal);

            Assert.Less(apple, mango);
            Assert.Less(mango, zebra);
        }

        [Test]
        public void Read_RoundTrips()
        {
            AtlasManifest original = BuildSample();
            var errors = new List<string>();
            AtlasManifest parsed = AtlasManifestSerializer.Read(
                AtlasManifestSerializer.Write(original),
                errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual(original.SchemaVersion, parsed.SchemaVersion);
            Assert.AreEqual(original.GeneratorVersion, parsed.GeneratorVersion);
            Assert.AreEqual(original.SettingsFingerprint, parsed.SettingsFingerprint);
            Assert.AreEqual(original.Entries.Count, parsed.Entries.Count);

            for (int i = 0; i < original.Entries.Count; i++)
            {
                Assert.AreEqual(original.Entries[i].AtlasKey, parsed.Entries[i].AtlasKey);
                Assert.AreEqual(original.Entries[i].OutputPath, parsed.Entries[i].OutputPath);
                Assert.AreEqual(original.Entries[i].SpriteCount, parsed.Entries[i].SpriteCount);
                Assert.AreEqual(original.Entries[i].ContentHash, parsed.Entries[i].ContentHash);
                Assert.AreEqual(original.Entries[i].PageCount, parsed.Entries[i].PageCount);
                Assert.AreEqual(original.Entries[i].RuleId, parsed.Entries[i].RuleId);
            }
        }

        [Test]
        public void Read_ToleratesWindowsLineEndings()
        {
            var errors = new List<string>();
            AtlasManifest parsed = AtlasManifestSerializer.Read(
                ExpectedManifest.Replace("\n", "\r\n"),
                errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual(1, parsed.Entries.Count);
            Assert.AreEqual("UI", parsed.Entries[0].AtlasKey);
        }

        /// <summary>
        /// A corrupted manifest must still let CI report what it could parse, instead of aborting and
        /// reporting nothing at all.
        /// </summary>
        [Test]
        public void Read_ReportsBadLinesInsteadOfThrowing()
        {
            var errors = new List<string>();
            AtlasManifest parsed = AtlasManifestSerializer.Read(
                "schema=1\n"
                + "atlas=Good\tAssets/Atlas/G.p\t1\tabcdef01\t1\t0\n"
                + "atlas=Short\n"
                + "atlas=BadCount\tp\tnotanumber\tabcdef01\t1\t0\n"
                + "# a comment\n"
                + "\n"
                + "unknownKey=whatever\n",
                errors);

            Assert.AreEqual(2, errors.Count);
            Assert.AreEqual(1, parsed.Entries.Count);
            Assert.AreEqual("Good", parsed.Entries[0].AtlasKey);
        }

        [Test]
        public void Read_EmptyInputYieldsAnEmptyManifest()
        {
            AtlasManifest parsed = AtlasManifestSerializer.Read(string.Empty);
            Assert.IsNotNull(parsed);
            Assert.IsEmpty(parsed.Entries);
        }

        [Test]
        public void Compare_DetectsAddedRemovedAndChanged()
        {
            var before = new AtlasManifest(1, "gen", 1L, new List<AtlasManifestEntry>
            {
                new AtlasManifestEntry("A", "a", 1, 10L, 1, 0),
                new AtlasManifestEntry("B", "b", 1, 20L, 1, 0),
            }, new Dictionary<string, long>());
            var after = new AtlasManifest(1, "gen", 1L, new List<AtlasManifestEntry>
            {
                new AtlasManifestEntry("A", "a", 1, 11L, 1, 0),
                new AtlasManifestEntry("C", "c", 1, 30L, 1, 0),
            }, new Dictionary<string, long>());

            AtlasManifestDelta delta = AtlasManifestComparer.Compare(before, after);

            Assert.IsFalse(delta.IsUpToDate);
            Assert.AreEqual(3, delta.DifferenceCount);
            Assert.AreEqual(new[] { "A" }, delta.Changed);
            Assert.AreEqual(new[] { "C" }, delta.Added);
            Assert.AreEqual(new[] { "B" }, delta.Removed);
        }

        [Test]
        public void Compare_IdenticalManifestsAreUpToDate()
        {
            AtlasManifest manifest = BuildSample();
            Assert.IsTrue(AtlasManifestComparer.Compare(manifest, manifest).IsUpToDate);
        }

        [Test]
        public void Compare_DeltasAreSortedForStableLogs()
        {
            var before = new AtlasManifest(
                1, "gen", 1L, new List<AtlasManifestEntry>(), new Dictionary<string, long>());
            var after = new AtlasManifest(1, "gen", 1L, new List<AtlasManifestEntry>
            {
                new AtlasManifestEntry("Zebra", "z", 1, 1L, 1, 0),
                new AtlasManifestEntry("Apple", "a", 1, 2L, 1, 0),
                new AtlasManifestEntry("Mango", "m", 1, 3L, 1, 0),
            }, new Dictionary<string, long>());

            Assert.AreEqual(
                new[] { "Apple", "Mango", "Zebra" },
                AtlasManifestComparer.Compare(before, after).Added);
        }

        [Test]
        public void Compare_NullOnEitherSideIsNotUpToDateButDoesNotThrow()
        {
            AtlasManifestDelta delta =
                AtlasManifestComparer.Compare(null, BuildSample());
            Assert.IsNotNull(delta);
            Assert.AreEqual(0, delta.DifferenceCount);
        }

        /// <summary>
        /// Source fingerprints are what let a cold start skip an atlas, so they have to survive a
        /// round trip. They are keyed by atlas key rather than page key: the generator needs them
        /// before it knows the page count, which itself requires loading the sprites they exist to
        /// avoid loading.
        /// </summary>
        [Test]
        public void Write_RecordsSourceFingerprintsByAtlasKey()
        {
            var manifest = new AtlasManifest(
                2,
                "gen/1",
                0L,
                new List<AtlasManifestEntry>
                {
                    new AtlasManifestEntry("UI", "p/ui", 3, 1L, 2, 0),
                    new AtlasManifestEntry("UI__p000", "p/ui__p000", 3, 1L, 2, 0),
                    new AtlasManifestEntry("UI__p001", "p/ui__p001", 3, 1L, 2, 0),
                },
                new Dictionary<string, long> { { "UI", 0x1234567890abcdefL } });

            string text = AtlasManifestSerializer.Write(manifest);

            // One line for the atlas, not one per page.
            Assert.IsTrue(text.Contains("source=UI\t1234567890abcdef\n"), text);
            Assert.AreEqual(1, CountLinesStartingWith(text, "source="));
        }

        private static int CountLinesStartingWith(string text, string prefix)
        {
            int count = 0;
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Sorted so the block diffs line-granularly: adding one atlas must not rewrite the others.
        /// </summary>
        [Test]
        public void Write_SortsSourceLinesByAtlasKey()
        {
            var manifest = new AtlasManifest(
                2,
                "gen/1",
                0L,
                new List<AtlasManifestEntry>(),
                new Dictionary<string, long>
                {
                    { "Zebra", 3L },
                    { "Apple", 1L },
                    { "Mango", 2L },
                });

            string text = AtlasManifestSerializer.Write(manifest);
            int apple = text.IndexOf("source=Apple", System.StringComparison.Ordinal);
            int mango = text.IndexOf("source=Mango", System.StringComparison.Ordinal);
            int zebra = text.IndexOf("source=Zebra", System.StringComparison.Ordinal);

            Assert.Less(apple, mango);
            Assert.Less(mango, zebra);
        }

        /// <summary>
        /// A manifest written before source fingerprints existed must still parse. Its entries carry
        /// no source hash, which the pipeline reads as "unknown" and regenerates rather than skips —
        /// so adopting the new field costs nothing and forces no full rebuild.
        /// </summary>
        [Test]
        public void Read_AcceptsManifestWithoutSourceLines()
        {
            const string legacy =
                "# CycloneGames atlas manifest. Generated file - do not edit by hand.\n"
                + "schema=1\n"
                + "generator=gen/1\n"
                + "settings=0000000000000000\n"
                + "atlas=UI\tp/ui\t3\tabcdef0123456789\t1\t0\n";

            var errors = new List<string>();
            AtlasManifest parsed = AtlasManifestSerializer.Read(legacy, errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual(1, parsed.SchemaVersion);
            Assert.AreEqual(1, parsed.Entries.Count);
            Assert.IsEmpty(parsed.SourceHashes, "no source lines means no source fingerprints");
        }

        [Test]
        public void Read_ReportsMalformedSourceLineWithoutAborting()
        {
            const string text =
                "schema=2\ngenerator=g\nsettings=0\n"
                + "source=UI\n"
                + "source=HUD\tdeadbeef\n"
                + "atlas=UI\tp/ui\t1\t1\t1\t0\n";

            var errors = new List<string>();
            AtlasManifest parsed = AtlasManifestSerializer.Read(text, errors);

            Assert.AreEqual(1, errors.Count, "the malformed line is reported");
            Assert.AreEqual(1, parsed.Entries.Count, "the atlas line still parsed");
            Assert.IsTrue(parsed.SourceHashes.ContainsKey("HUD"), "the valid line still applied");
        }

        /// <summary>
        /// Guards the file-writing convention used by the pipeline: no BOM, so a manifest produced on
        /// Windows and one produced on Linux compare equal byte for byte.
        /// </summary>
        [Test]
        public void WrittenBytesHaveNoBom()
        {
            byte[] bytes = new UTF8Encoding(false).GetPreamble();
            Assert.AreEqual(0, bytes.Length, "a BOM would make every manifest differ per platform");
        }
    }
}
