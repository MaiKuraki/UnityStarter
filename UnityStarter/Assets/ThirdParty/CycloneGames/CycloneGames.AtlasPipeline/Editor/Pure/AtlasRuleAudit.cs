using System.Collections.Generic;
using System.Text;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>What kind of rule lifecycle problem an audit entry describes.</summary>
    public enum AtlasRuleAuditKind
    {
        /// <summary>A settings slot whose rule asset is missing or null. Blocks the build.</summary>
        MissingReference = 0,

        /// <summary>The same rule asset registered in more than one slot. Blocks the build.</summary>
        DuplicateReference = 1,

        /// <summary>
        /// A rule asset on disk that no settings slot references. Warned, never auto-deleted: the
        /// file may be work in progress, and deleting a file because a list no longer mentions it is
        /// how art gets lost.
        /// </summary>
        OrphanAsset = 2,
    }

    /// <summary>One rule lifecycle problem found by an audit.</summary>
    public sealed class AtlasRuleAuditEntry
    {
        public AtlasRuleAuditEntry(
            AtlasRuleAuditKind kind,
            int slotIndex,
            string guid,
            string assetPath)
        {
            Kind = kind;
            SlotIndex = slotIndex;
            Guid = guid ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
        }

        public AtlasRuleAuditKind Kind { get; }

        /// <summary>
        /// Settings slot the problem is in, or -1 when the problem is a disk asset rather than a
        /// registration slot.
        /// </summary>
        public int SlotIndex { get; }

        public string Guid { get; }

        public string AssetPath { get; }
    }

    /// <summary>
    /// The lifecycle contract for rule assets, expressed as a pure comparison.
    /// <para>
    /// Removing a rule from the settings list has always been an UNREGISTER, not a delete — the
    /// serialized list holds object references, so dropping one leaves the .asset on disk untouched.
    /// That is the safe default and this audit does not change it. What it adds is the visibility the
    /// safe default needs: an orphaned rule asset is indistinguishable from a live one in the Project
    /// window, and a slot whose asset failed to load is worse — the rule's folder silently stops being
    /// managed, its atlases stop being referenced, and the sweep then deletes them as orphans. A
    /// missing reference is therefore a data-loss path, not a cosmetic problem, which is why it
    /// blocks the build while an orphan only warns.
    /// </para>
    /// </summary>
    public static class AtlasRuleAuditor
    {
        /// <summary>
        /// Compares the rule assets a settings list references against every rule asset on disk.
        /// </summary>
        /// <param name="registeredGuids">
        /// One entry per settings slot, in list order. An empty or null entry means the slot's
        /// reference failed to load; a non-empty entry is the asset's GUID.
        /// </param>
        /// <param name="assetsOnDisk">Every rule asset found in the project, as guid/path pairs.</param>
        /// <returns>
        /// Findings sorted by kind and then by slot or path, so the same project produces the same
        /// audit text on every machine and CI logs stay diffable.
        /// </returns>
        public static IReadOnlyList<AtlasRuleAuditEntry> Audit(
            IReadOnlyList<string> registeredGuids,
            IReadOnlyList<KeyValuePair<string, string>> assetsOnDisk)
        {
            var results = new List<AtlasRuleAuditEntry>();

            // First slot that claims each guid. Registration order is the resolution order, so the
            // FIRST slot is the one that wins and every later one is a duplicate.
            var firstSlotByGuid = new Dictionary<string, int>(
                System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < registeredGuids.Count; i++)
            {
                string guid = registeredGuids[i];
                if (string.IsNullOrWhiteSpace(guid))
                {
                    results.Add(new AtlasRuleAuditEntry(
                        AtlasRuleAuditKind.MissingReference, i, string.Empty, string.Empty));
                    continue;
                }

                if (firstSlotByGuid.TryGetValue(guid, out _))
                {
                    results.Add(new AtlasRuleAuditEntry(
                        AtlasRuleAuditKind.DuplicateReference,
                        i,
                        guid,
                        PathOf(assetsOnDisk, guid)));
                    continue;
                }

                firstSlotByGuid[guid] = i;

                if (PathOf(assetsOnDisk, guid) == null)
                {
                    results.Add(new AtlasRuleAuditEntry(
                        AtlasRuleAuditKind.MissingReference, i, guid, string.Empty));
                }
            }

            for (int i = 0; i < assetsOnDisk.Count; i++)
            {
                KeyValuePair<string, string> asset = assetsOnDisk[i];
                if (!firstSlotByGuid.ContainsKey(asset.Key))
                {
                    results.Add(new AtlasRuleAuditEntry(
                        AtlasRuleAuditKind.OrphanAsset, -1, asset.Key, asset.Value));
                }
            }

            results.Sort(CompareEntries);
            return results;
        }

        /// <summary>
        /// Human-readable summary, one line per finding, in the same order as the entries. Written
        /// for the person reading a failed CI log: what is wrong, where, and what to do.
        /// </summary>
        public static string Describe(IReadOnlyList<AtlasRuleAuditEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                AtlasRuleAuditEntry entry = entries[i];
                if (i > 0)
                {
                    builder.Append('\n');
                }

                switch (entry.Kind)
                {
                    case AtlasRuleAuditKind.MissingReference:
                        builder.Append("Settings slot ").Append(entry.SlotIndex)
                            .Append(" references a rule asset that is missing or failed to load. ")
                            .Append("Its folder is not being managed: no atlases are generated for it, "
                                    + "and its previous atlases are treated as orphans. Remove the "
                                    + "broken slot, or restore the rule asset.");
                        break;
                    case AtlasRuleAuditKind.DuplicateReference:
                        builder.Append("Rule asset '").Append(entry.AssetPath)
                            .Append("' is registered more than once (slot ").Append(entry.SlotIndex)
                            .Append(" repeats an earlier slot). Registration order decides which "
                                    + "configuration wins, so a duplicate is undefined behaviour. "
                                    + "Keep one slot.");
                        break;
                    case AtlasRuleAuditKind.OrphanAsset:
                        builder.Append("Rule asset '").Append(entry.AssetPath)
                            .Append("' exists on disk but is not registered. Its rules have no "
                                    + "effect. The file has been kept — removing the list entry was "
                                    + "an unregister, not a delete — so register it again, or "
                                    + "delete it via 'Assets > CycloneGames Atlas Pipeline > "
                                    + "Delete Unregistered Rule Assets' (asks for confirmation) or "
                                    + "in the Project window.");
                        break;
                }
            }

            return builder.ToString();
        }

        private static int CompareEntries(AtlasRuleAuditEntry left, AtlasRuleAuditEntry right)
        {
            int byKind = ((int)left.Kind).CompareTo((int)right.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            int bySlot = left.SlotIndex.CompareTo(right.SlotIndex);
            if (bySlot != 0)
            {
                return bySlot;
            }

            return string.CompareOrdinal(left.AssetPath, right.AssetPath);
        }

        private static string PathOf(
            IReadOnlyList<KeyValuePair<string, string>> assetsOnDisk,
            string guid)
        {
            for (int i = 0; i < assetsOnDisk.Count; i++)
            {
                if (string.Equals(assetsOnDisk[i].Key, guid, System.StringComparison.OrdinalIgnoreCase))
                {
                    return assetsOnDisk[i].Value;
                }
            }

            return null;
        }
    }
}
