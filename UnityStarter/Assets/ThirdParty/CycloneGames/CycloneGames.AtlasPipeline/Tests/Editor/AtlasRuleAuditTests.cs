using System.Collections.Generic;
using NUnit.Framework;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The audit is the rule lifecycle's error reporting. What matters is not the comparison — it is
    /// that a missing reference is treated as data loss rather than a cosmetic problem, and that an
    /// orphan never becomes a deletion.
    /// </summary>
    [TestFixture]
    public sealed class AtlasRuleAuditTests
    {
        private static KeyValuePair<string, string> Disk(string guid, string path)
        {
            return new KeyValuePair<string, string>(guid, path);
        }

        [Test]
        public void Audit_HealthyRegistrationProducesNoFindings()
        {
            var registered = new List<string> { "guid-ui" };
            var onDisk = new List<KeyValuePair<string, string>>
            {
                Disk("guid-ui", "Assets/Settings/AtlasRules/UI.asset"),
            };

            IReadOnlyList<AtlasRuleAuditEntry> findings = AtlasRuleAuditor.Audit(registered, onDisk);

            Assert.IsEmpty(findings);
        }

        /// <summary>
        /// The dangerous case. A slot whose asset failed to load makes the rule's folder unmanaged —
        /// and its atlases unreferenced, which the sweep would then delete. This is why it is a
        /// blocking finding and not a warning.
        /// </summary>
        [Test]
        public void Audit_MissingReferenceIsReportedPerSlot()
        {
            var registered = new List<string> { "guid-ui", string.Empty, "guid-gone" };
            var onDisk = new List<KeyValuePair<string, string>>
            {
                Disk("guid-ui", "Assets/Settings/AtlasRules/UI.asset"),
            };

            IReadOnlyList<AtlasRuleAuditEntry> findings = AtlasRuleAuditor.Audit(registered, onDisk);

            Assert.AreEqual(2, findings.Count);
            Assert.AreEqual(AtlasRuleAuditKind.MissingReference, findings[0].Kind);
            Assert.AreEqual(1, findings[0].SlotIndex, "the empty slot");
            Assert.AreEqual(AtlasRuleAuditKind.MissingReference, findings[1].Kind);
            Assert.AreEqual(2, findings[1].SlotIndex, "the slot whose guid is nowhere on disk");
            Assert.AreEqual("guid-gone", findings[1].Guid);
        }

        /// <summary>
        /// Registration order is resolution order, so the FIRST slot wins and later duplicates are
        /// the finding. Reporting the winner would point the fix at the wrong slot.
        /// </summary>
        [Test]
        public void Audit_DuplicateReportsLaterSlotsNotTheWinner()
        {
            var registered = new List<string> { "guid-ui", "guid-ui", "guid-ui" };
            var onDisk = new List<KeyValuePair<string, string>>
            {
                Disk("guid-ui", "Assets/Settings/AtlasRules/UI.asset"),
            };

            IReadOnlyList<AtlasRuleAuditEntry> findings = AtlasRuleAuditor.Audit(registered, onDisk);

            Assert.AreEqual(2, findings.Count);
            Assert.AreEqual(AtlasRuleAuditKind.DuplicateReference, findings[0].Kind);
            Assert.AreEqual(1, findings[0].SlotIndex);
            Assert.AreEqual(2, findings[1].SlotIndex);
        }

        /// <summary>
        /// An orphan is reported with its path and never with a slot: the fix is a human decision
        /// (register it again, or delete the file), not something the pipeline should do itself.
        /// </summary>
        [Test]
        public void Audit_OrphanIsReportedWithoutASlot()
        {
            var registered = new List<string> { "guid-ui" };
            var onDisk = new List<KeyValuePair<string, string>>
            {
                Disk("guid-ui", "Assets/Settings/AtlasRules/UI.asset"),
                Disk("guid-dead", "Assets/Settings/AtlasRules/Old.asset"),
            };

            IReadOnlyList<AtlasRuleAuditEntry> findings = AtlasRuleAuditor.Audit(registered, onDisk);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(AtlasRuleAuditKind.OrphanAsset, findings[0].Kind);
            Assert.AreEqual(-1, findings[0].SlotIndex);
            Assert.AreEqual("Assets/Settings/AtlasRules/Old.asset", findings[0].AssetPath);
        }

        /// <summary>
        /// Same project, same findings, same order — on every machine. Dictionary iteration must not
        /// leak into the report, or CI logs become nondeterministic noise.
        /// </summary>
        [Test]
        public void Audit_FindingsAreSortedForStableLogs()
        {
            var registered = new List<string>
            {
                "guid-gone",
                "guid-ui",
                "guid-ui",
                string.Empty,
            };
            var onDisk = new List<KeyValuePair<string, string>>
            {
                Disk("guid-orphan-b", "Assets/Rules/B.asset"),
                Disk("guid-ui", "Assets/Rules/UI.asset"),
                Disk("guid-orphan-a", "Assets/Rules/A.asset"),
            };

            IReadOnlyList<AtlasRuleAuditEntry> findings =
                AtlasRuleAuditor.Audit(registered, onDisk);

            // Two missing slots, one duplicate, two orphans.
            Assert.AreEqual(5, findings.Count);
            // Missing references first, in slot order.
            Assert.AreEqual(AtlasRuleAuditKind.MissingReference, findings[0].Kind);
            Assert.AreEqual(0, findings[0].SlotIndex);
            Assert.AreEqual(AtlasRuleAuditKind.MissingReference, findings[1].Kind);
            Assert.AreEqual(3, findings[1].SlotIndex);
            // Then duplicates, in slot order.
            Assert.AreEqual(AtlasRuleAuditKind.DuplicateReference, findings[2].Kind);
            Assert.AreEqual(2, findings[2].SlotIndex);
            // Then orphans, by path.
            Assert.AreEqual(AtlasRuleAuditKind.OrphanAsset, findings[3].Kind);
            Assert.AreEqual("Assets/Rules/A.asset", findings[3].AssetPath);
            Assert.AreEqual(AtlasRuleAuditKind.OrphanAsset, findings[4].Kind);
            Assert.AreEqual("Assets/Rules/B.asset", findings[4].AssetPath);
        }

        [Test]
        public void Describe_IsEmptyWithoutFindings()
        {
            Assert.IsEmpty(AtlasRuleAuditor.Describe(new List<AtlasRuleAuditEntry>()));
            Assert.IsEmpty(AtlasRuleAuditor.Describe(null));
        }

        /// <summary>
        /// The text is the only thing most people see, so it has to carry the fix as well as the
        /// fault — and for an orphan it has to say the file was KEPT, or the reader assumes the
        /// pipeline deleted it.
        /// </summary>
        [Test]
        public void Describe_NamesTheFaultAndTheFix()
        {
            var findings = new List<AtlasRuleAuditEntry>
            {
                new AtlasRuleAuditEntry(
                    AtlasRuleAuditKind.MissingReference, 2, string.Empty, string.Empty),
                new AtlasRuleAuditEntry(
                    AtlasRuleAuditKind.DuplicateReference, 3, "g", "Assets/R/UI.asset"),
                new AtlasRuleAuditEntry(
                    AtlasRuleAuditKind.OrphanAsset, -1, "g", "Assets/R/Old.asset"),
            };

            string text = AtlasRuleAuditor.Describe(findings);

            StringAssert.Contains("slot 2", text);
            StringAssert.Contains("slot 3", text);
            StringAssert.Contains("Assets/R/Old.asset", text);
            StringAssert.Contains("has been kept", text);
        }
    }
}
