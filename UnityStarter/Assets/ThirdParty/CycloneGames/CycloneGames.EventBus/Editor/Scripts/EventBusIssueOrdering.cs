using System;
using System.Collections.Generic;

namespace CycloneGames.EventBus.Editor
{
    /// <summary>
    /// Deterministic / freezable ordering for the Issues table. Pure and UnityEngine-free so it can be
    /// unit-tested: the GUI cannot show that rows are swapping under the reader, but the order can.
    /// </summary>
    public static class EventBusIssueOrdering
    {
        /// <summary>
        /// Returns the display order as indices into the input rows.
        ///
        /// Without <paramref name="freeze"/>, the order is deterministic: severity ascending, then
        /// subject (ordinal), then category (ordinal). Delta is deliberately NOT a key — it changes on
        /// every snapshot (0.5 s) and made rows swap under the reader.
        ///
        /// With <paramref name="freeze"/>, rows whose <paramref name="stableKeys"/> appeared in
        /// <paramref name="previousOrder"/> keep that relative order; rows that did not appear before
        /// (new issues) are appended at the end, in the deterministic order. Values are not involved,
        /// so a row's numbers may change while its position holds.
        /// </summary>
        /// <param name="stableKeys">Per-row identity (Category + Subject + Where).</param>
        /// <param name="severities">Per-row severity (0 = ERR, 1 = WARN, 2 = INFO).</param>
        /// <param name="subjects">Per-row subject, for the deterministic tie-break.</param>
        /// <param name="categories">Per-row category, for the deterministic tie-break.</param>
        /// <param name="previousOrder">The stable keys in last snapshot's display order, or null.</param>
        /// <param name="freeze">True to preserve the previous order.</param>
        public static int[] Order(
            IReadOnlyList<string> stableKeys,
            IReadOnlyList<int> severities,
            IReadOnlyList<string> subjects,
            IReadOnlyList<string> categories,
            string[] previousOrder,
            bool freeze)
        {
            if (stableKeys == null)
            {
                throw new ArgumentNullException(nameof(stableKeys));
            }

            if (severities == null)
            {
                throw new ArgumentNullException(nameof(severities));
            }

            if (subjects == null)
            {
                throw new ArgumentNullException(nameof(subjects));
            }

            if (categories == null)
            {
                throw new ArgumentNullException(nameof(categories));
            }

            int count = severities.Count;
            if (stableKeys.Count != count || subjects.Count != count || categories.Count != count)
            {
                throw new ArgumentException("All per-row lists must have equal length.");
            }

            int[] result = new int[count];
            bool[] placed = new bool[count];
            int written = 0;

            if (freeze && previousOrder != null && previousOrder.Length > 0)
            {
                // Bucket the current rows by their stable key so a previous key maps back to its row.
                var byKey = new Dictionary<string, List<int>>(count);
                for (int index = 0; index < count; index++)
                {
                    string key = stableKeys[index] ?? string.Empty;
                    if (!byKey.TryGetValue(key, out List<int> bucket))
                    {
                        bucket = new List<int>();
                        byKey[key] = bucket;
                    }

                    bucket.Add(index);
                }

                for (int position = 0; position < previousOrder.Length; position++)
                {
                    string key = previousOrder[position] ?? string.Empty;
                    if (!byKey.TryGetValue(key, out List<int> bucket) || bucket.Count == 0)
                    {
                        continue;
                    }

                    int index = bucket[0];
                    bucket.RemoveAt(0);
                    result[written++] = index;
                    placed[index] = true;
                }
            }

            // Everything not placed by the freeze keeps (or takes) the deterministic order.
            int[] deterministic = DeterministicOrder(severities, subjects, categories, count);
            for (int position = 0; position < deterministic.Length; position++)
            {
                int index = deterministic[position];
                if (!placed[index])
                {
                    result[written++] = index;
                    placed[index] = true;
                }
            }

            return result;
        }

        /// <summary>Severity ascending, then subject, then category (ordinal), with an index tie-break.</summary>
        public static int[] DeterministicOrder(
            IReadOnlyList<int> severities,
            IReadOnlyList<string> subjects,
            IReadOnlyList<string> categories,
            int count)
        {
            int[] order = new int[count];
            for (int index = 0; index < count; index++)
            {
                order[index] = index;
            }

            Array.Sort(order, (a, b) =>
            {
                int compare = severities[a].CompareTo(severities[b]);
                if (compare != 0)
                {
                    return compare;
                }

                compare = string.CompareOrdinal(subjects[a], subjects[b]);
                if (compare != 0)
                {
                    return compare;
                }

                compare = string.CompareOrdinal(categories[a], categories[b]);
                if (compare != 0)
                {
                    return compare;
                }

                return a.CompareTo(b);
            });

            return order;
        }
    }
}
