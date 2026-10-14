using System;
using System.Text;

using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor
{
    public sealed class DataTableConfigFingerprintTests
    {
        private const int GoldenSchemaVersion = 7;
        private static readonly string ShaA = new string('a', 64);
        private static readonly string ShaF = new string('f', 64);
        private const string ShaSeq = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        // Expected digests below were produced by an independent implementation of the
        // documented "DTF1" byte layout, computed with Python 3.13 hashlib outside this
        // repository:
        //   h = sha256(); h.update(b'DTF1'); h.update(le32(schema)); h.update(le32(count))
        //   for (name, length, sha) in sorted(entries):  # ordinal == code-point order (ASCII)
        //       h.update(le32(len(name_utf8))); h.update(name_utf8)
        //       h.update(bytes([flags]))  # bit0 = has length, bit1 = has sha
        //       if length is not None: h.update(le64(length))
        //       if sha is not None: h.update(sha.encode('utf-8'))
        private const string GoldenFingerprintHex =
            "308bf0b176748cd7acd1291c21c07fe52cfb10335401393651fb9c3984c2809e";
        private const string EmptyManifestSchemaOneHex =
            "b841d851875806576a8410593f04ace344ea1ee0d6711e50f3c7274d2448bee6";
        private const string EmptyManifestSchemaSevenHex =
            "29e2b62dbcfd63d493ac226bd36f3417988a5fc43b13e2def470baf9ed949257";

        [Test]
        public void Compute_IsIndependentOfEntryOrder()
        {
            DataTableConfigFingerprint first = DataTableConfigFingerprint.Compute(
                CreateGoldenManifest(reverseOrder: false));
            DataTableConfigFingerprint second = DataTableConfigFingerprint.Compute(
                CreateGoldenManifest(reverseOrder: true));

            Assert.IsTrue(first.Equals(second));
            Assert.IsTrue(first == second);
            Assert.AreEqual(first.ToString(), second.ToString());
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void Compute_ExcludesLocationAndRequiredFromContentIdentity()
        {
            var baseline = new DataTableManifest(
                GoldenSchemaVersion,
                new[]
                {
                    new DataTableManifestEntry(
                        "items/basic", "config/items_basic", 4096, ShaA, required: true),
                });

            var relocated = new DataTableManifest(
                GoldenSchemaVersion,
                new[]
                {
                    new DataTableManifestEntry(
                        "items/basic", "dlc/patch42/items_basic", 4096, ShaA, required: false),
                });

            Assert.AreEqual(
                DataTableConfigFingerprint.Compute(baseline),
                DataTableConfigFingerprint.Compute(relocated));
        }

        [Test]
        public void Compute_DetectsEveryDeclaredValueChange()
        {
            DataTableConfigFingerprint baseline =
                DataTableConfigFingerprint.Compute(CreateGoldenManifest(reverseOrder: false));

            AssertFingerprintDiffers(baseline, CreateVariant(
                schemaVersion: GoldenSchemaVersion + 1));
            AssertFingerprintDiffers(baseline, CreateVariant(
                renameBasicTo: "items/renamed"));
            AssertFingerprintDiffers(baseline, CreateVariant(
                basicLengthOverride: 4097));
            AssertFingerprintDiffers(baseline, CreateVariant(
                basicShaOverride: ShaF));
            AssertFingerprintDiffers(baseline, CreateVariant(
                dropBasicLength: true));
            AssertFingerprintDiffers(baseline, CreateVariant(
                dropAdvancedSha: true));
            AssertFingerprintDiffers(baseline, CreateVariant(
                dropZonesEntry: true));
        }

        [Test]
        public void Compute_MatchesIndependentGoldenVector()
        {
            DataTableConfigFingerprint fingerprint =
                DataTableConfigFingerprint.Compute(CreateGoldenManifest(reverseOrder: false));

            Assert.AreEqual(GoldenFingerprintHex, fingerprint.ToString());
        }

        [Test]
        public void Compute_EmptyManifest_HashesHeaderOnlyAndNeverMatchesDefault()
        {
            DataTableConfigFingerprint schemaOne = DataTableConfigFingerprint.Compute(
                new DataTableManifest(1, new DataTableManifestEntry[0]));
            DataTableConfigFingerprint schemaSeven = DataTableConfigFingerprint.Compute(
                new DataTableManifest(GoldenSchemaVersion, new DataTableManifestEntry[0]));

            Assert.AreEqual(EmptyManifestSchemaOneHex, schemaOne.ToString());
            Assert.AreEqual(EmptyManifestSchemaSevenHex, schemaSeven.ToString());
            Assert.AreNotEqual(schemaOne, schemaSeven);
            Assert.IsFalse(schemaOne.IsEmpty);
            Assert.AreNotEqual(default(DataTableConfigFingerprint), schemaOne);
        }

        [Test]
        public void Equals_IsExactAcrossDistinctStringInstances()
        {
            DataTableConfigFingerprint first = DataTableConfigFingerprint.Compute(
                CreateGoldenManifest(reverseOrder: false));
            DataTableConfigFingerprint second = DataTableConfigFingerprint.Compute(
                CreateGoldenManifest(reverseOrder: false));

            Assert.AreNotSame(first.ToString(), second.ToString());
            Assert.IsTrue(first.Equals(second));

            DataTableConfigFingerprint tampered = DataTableConfigFingerprint.Compute(
                CreateVariant(basicLengthOverride: 4097));
            Assert.IsFalse(first.Equals(tampered));
            Assert.IsTrue(first != tampered);
        }

        [Test]
        public void Equals_TreatsTwoDefaultsAsEqualAndDefaultAsUnequalToComputed()
        {
            var first = default(DataTableConfigFingerprint);
            var second = default(DataTableConfigFingerprint);
            DataTableConfigFingerprint computed = DataTableConfigFingerprint.Compute(
                new DataTableManifest(1, new DataTableManifestEntry[0]));

            Assert.IsTrue(first.Equals(second));
            Assert.IsTrue(first == second);
            Assert.IsTrue(first.IsEmpty);
            Assert.AreEqual(string.Empty, first.ToString());
            Assert.IsFalse(first.Equals(computed));
            Assert.IsFalse(computed.Equals(first));
            Assert.IsTrue(first != computed);
            Assert.IsTrue(first.Equals((object)second));
            Assert.IsFalse(first.Equals(null));
            Assert.IsFalse(first.Equals("not a fingerprint"));
        }

        [Test]
        public void Sha256Matches_PreservesBooleanSemanticsWithFixedTimeComparison()
        {
            byte[] payload = Encoding.UTF8.GetBytes("payload-bytes");
            string sha256 = DataTableHashUtility.ComputeSha256Hex(payload);

            Assert.IsTrue(DataTableHashUtility.Sha256Matches(payload, sha256));
            Assert.IsTrue(DataTableHashUtility.Sha256Matches(payload, sha256.ToUpperInvariant()));
            Assert.IsTrue(DataTableHashUtility.Sha256Matches(payload, "  " + sha256 + "  "));

            string flipped = sha256.Substring(0, 10) +
                             (sha256[10] == 'a' ? 'b' : 'a') +
                             sha256.Substring(11);
            Assert.AreEqual(64, flipped.Length);
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(payload, flipped));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(payload, sha256.Substring(0, 63)));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(payload, sha256 + "0"));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(payload, null));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(payload, string.Empty));
        }

        [Test]
        public void ManifestValidatePayload_StillRejectsTamperedContent()
        {
            byte[] payload = Encoding.UTF8.GetBytes("declared-content");
            string sha256 = DataTableHashUtility.ComputeSha256Hex(payload);
            var entry = new DataTableManifestEntry(
                "items/basic", "config/items_basic", payload.Length, sha256);

            DataTableManifest.ValidatePayload(entry, payload);

            byte[] tampered = Encoding.UTF8.GetBytes("declared-contenX");
            Assert.Throws<InvalidOperationException>(
                () => DataTableManifest.ValidatePayload(entry, tampered));
        }

        [Test]
        public void Classify_ReturnsSameForEqualFingerprintsRegardlessOfSequence()
        {
            DataTableConfigFingerprint fingerprint = DataTableConfigFingerprint.Compute(
                CreateGoldenManifest(reverseOrder: false));

            Assert.AreEqual(
                DataTableConfigReconciliationStatus.Same,
                DataTableConfigReconciliation.Classify(
                    fingerprint, new DataTableRevision(5, "rev-5"),
                    fingerprint, new DataTableRevision(5, "rev-5")));
            Assert.AreEqual(
                DataTableConfigReconciliationStatus.Same,
                DataTableConfigReconciliation.Classify(
                    fingerprint, new DataTableRevision(9, "rev-9"),
                    fingerprint, new DataTableRevision(3, "rev-3")));
        }

        [Test]
        public void Classify_ReturnsMismatchForEqualSequenceWithDifferentContent()
        {
            DataTableConfigFingerprint honest = DataTableConfigFingerprint.Compute(
                CreateGoldenManifest(reverseOrder: false));
            DataTableConfigFingerprint tampered = DataTableConfigFingerprint.Compute(
                CreateVariant(basicShaOverride: ShaF));
            var revision = new DataTableRevision(5, "rev-5");

            Assert.AreEqual(
                DataTableConfigReconciliationStatus.Mismatch,
                DataTableConfigReconciliation.Classify(honest, revision, tampered, revision));
            Assert.AreEqual(
                DataTableConfigReconciliationStatus.Mismatch,
                DataTableConfigReconciliation.Classify(tampered, revision, honest, revision));
        }

        [Test]
        public void Classify_ReturnsStaleOrAheadFromSequenceOrderWhenContentDiffers()
        {
            DataTableConfigFingerprint older = DataTableConfigFingerprint.Compute(
                CreateGoldenManifest(reverseOrder: false));
            DataTableConfigFingerprint newer = DataTableConfigFingerprint.Compute(
                CreateVariant(schemaVersion: GoldenSchemaVersion + 1));

            Assert.AreEqual(
                DataTableConfigReconciliationStatus.PeerStale,
                DataTableConfigReconciliation.Classify(
                    newer, new DataTableRevision(6, "rev-6"),
                    older, new DataTableRevision(5, "rev-5")));
            Assert.AreEqual(
                DataTableConfigReconciliationStatus.PeerAhead,
                DataTableConfigReconciliation.Classify(
                    older, new DataTableRevision(5, "rev-5"),
                    newer, new DataTableRevision(6, "rev-6")));
        }

        [Test]
        public void Classify_MultiEndpointAgreement_DeterministicallyRejectsOneTamperedPeer()
        {
            const int endpointCount = 5;
            var revision = new DataTableRevision(11, "rev-11");
            var honestFingerprints = new DataTableConfigFingerprint[endpointCount];
            for (int i = 0; i < endpointCount; i++)
            {
                honestFingerprints[i] = DataTableConfigFingerprint.Compute(
                    CreateGoldenManifest(reverseOrder: (i & 1) == 1));
            }

            for (int a = 0; a < endpointCount; a++)
            {
                for (int b = 0; b < endpointCount; b++)
                {
                    Assert.AreEqual(
                        DataTableConfigReconciliationStatus.Same,
                        DataTableConfigReconciliation.Classify(
                            honestFingerprints[a], revision, honestFingerprints[b], revision),
                        $"Honest endpoints {a} and {b} must agree.");
                }
            }

            DataTableConfigFingerprint tampered = DataTableConfigFingerprint.Compute(
                CreateVariant(basicLengthOverride: 4097));
            for (int i = 0; i < endpointCount; i++)
            {
                Assert.AreEqual(
                    DataTableConfigReconciliationStatus.Mismatch,
                    DataTableConfigReconciliation.Classify(
                        honestFingerprints[i], revision, tampered, revision),
                    $"Honest endpoint {i} must deterministically reject the tampered peer.");
            }
        }

        private static DataTableManifest CreateGoldenManifest(bool reverseOrder)
        {
            DataTableManifestEntry[] entries =
            {
                new DataTableManifestEntry("items/basic", "config/items_basic", 4096, ShaA),
                new DataTableManifestEntry("items/advanced", "config/items_advanced",
                    DataTableManifestEntry.UNKNOWN_BYTE_LENGTH, ShaSeq),
                new DataTableManifestEntry("zones", "config/zones", 33, null),
                new DataTableManifestEntry("npcs.dialog", "config/npcs_dialog",
                    DataTableManifestEntry.UNKNOWN_BYTE_LENGTH, ShaF),
            };

            if (reverseOrder)
            {
                Array.Reverse(entries);
            }

            return new DataTableManifest(GoldenSchemaVersion, entries);
        }

        private static DataTableManifest CreateVariant(
            int schemaVersion = GoldenSchemaVersion,
            string renameBasicTo = null,
            int? basicLengthOverride = null,
            string basicShaOverride = null,
            bool dropBasicLength = false,
            bool dropAdvancedSha = false,
            bool dropZonesEntry = false)
        {
            int basicLength = basicLengthOverride ??
                              (dropBasicLength
                                  ? DataTableManifestEntry.UNKNOWN_BYTE_LENGTH
                                  : 4096);

            var entries = new System.Collections.Generic.List<DataTableManifestEntry>
            {
                new DataTableManifestEntry(
                    renameBasicTo ?? "items/basic", "config/items_basic",
                    basicLength, basicShaOverride ?? ShaA),
                new DataTableManifestEntry(
                    "items/advanced", "config/items_advanced",
                    DataTableManifestEntry.UNKNOWN_BYTE_LENGTH,
                    dropAdvancedSha ? null : ShaSeq),
                new DataTableManifestEntry(
                    "npcs.dialog", "config/npcs_dialog",
                    DataTableManifestEntry.UNKNOWN_BYTE_LENGTH, ShaF),
            };

            if (!dropZonesEntry)
            {
                entries.Add(new DataTableManifestEntry("zones", "config/zones", 33, null));
            }

            return new DataTableManifest(schemaVersion, entries);
        }

        private static void AssertFingerprintDiffers(
            DataTableConfigFingerprint baseline,
            DataTableManifest variant)
        {
            DataTableConfigFingerprint variantFingerprint =
                DataTableConfigFingerprint.Compute(variant);
            Assert.AreNotEqual(baseline, variantFingerprint);
            Assert.IsFalse(baseline.Equals(variantFingerprint));
        }
    }
}
