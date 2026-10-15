using System;
using NUnit.Framework;
using CycloneGames.EventBus.Editor;

namespace CycloneGames.EventBus.Tests
{
    /// <summary>
    /// Unit tests for the Issues table ordering (EventBusIssueOrdering). The GUI cannot show that
    /// rows are swapping under the reader, but the order can be pinned: deterministic by default, and
    /// stable under the "Freeze order" toggle.
    /// </summary>
    public sealed class EventBusIssueOrderingTests
    {
        private static int[] Run(
            string[] keys,
            int[] severities,
            string[] subjects,
            string[] categories,
            string[] previousOrder,
            bool freeze)
        {
            return EventBusIssueOrdering.Order(keys, severities, subjects, categories, previousOrder, freeze);
        }

        private static void AssertPermutation(int[] order, int count)
        {
            Assert.AreEqual(count, order.Length, "Order length must match the row count.");
            var seen = new bool[count];
            for (int index = 0; index < order.Length; index++)
            {
                Assert.That(order[index], Is.InRange(0, count - 1), "Index out of range.");
                Assert.IsFalse(seen[order[index]], $"Index {order[index]} appears twice (row lost or duplicated).");
                seen[order[index]] = true;
            }
        }

        [Test]
        public void DeterministicOrder_BySeverityThenSubjectThenCategory_AndRepeatable()
        {
            string[] keys = { "k0", "k1", "k2", "k3" };
            int[] severities = { 1, 0, 0, 2 };
            string[] subjects = { "Zed", "Alpha", "Alpha", "Beta" };
            string[] categories = { "X", "Y", "X", "X" };

            int[] first = Run(keys, severities, subjects, categories, null, false);
            int[] second = Run(keys, severities, subjects, categories, null, false);

            // severity asc -> sev0 first: index 2 (Alpha/X) before index 1 (Alpha/Y); then index 0
            // (sev1, Zed); then index 3 (sev2, Beta).
            Assert.That(first, Is.EqualTo(new[] { 2, 1, 0, 3 }));
            Assert.That(second, Is.EqualTo(first), "The deterministic order must be repeatable (same input, same output).");
        }

        [Test]
        public void FreezeOrder_KeepsPreviousOrder_AndAppendsNewKeys()
        {
            string[] keys = { "k0", "k1", "k2", "k3" };
            // Deterministic would put k3 (sev0) first; freeze must ignore that and keep the old order.
            int[] severities = { 2, 2, 2, 0 };
            string[] subjects = { "A", "B", "C", "D" };
            string[] categories = { "x", "x", "x", "x" };

            int[] frozen = Run(keys, severities, subjects, categories, new[] { "k0", "k1", "k2" }, true);
            Assert.That(frozen, Is.EqualTo(new[] { 0, 1, 2, 3 }),
                "Frozen order must keep the previous rows' order and append the new key at the end.");

            // The previous order is authoritative, not the current index order.
            int[] reordered = Run(keys, severities, subjects, categories, new[] { "k2", "k0", "k1" }, true);
            Assert.That(reordered, Is.EqualTo(new[] { 2, 0, 1, 3 }),
                "Frozen order must follow the previous snapshot's order.");

            // A row that disappeared then reappeared is treated as new (appended).
            int[] missing = Run(keys, severities, subjects, categories, new[] { "k1", "k3" }, true);
            Assert.That(missing, Is.EqualTo(new[] { 1, 3, 0, 2 }),
                "Rows absent from the previous order are appended in deterministic order.");
        }

        [Test]
        public void Unfreeze_RestoresDeterministicOrder_PreservingTheSet()
        {
            string[] keys = { "k0", "k1", "k2", "k3" };
            int[] severities = { 2, 2, 2, 0 };
            string[] subjects = { "A", "B", "C", "D" };
            string[] categories = { "x", "x", "x", "x" };

            int[] frozen = Run(keys, severities, subjects, categories, new[] { "k0", "k1", "k2" }, true);
            int[] thawed = Run(keys, severities, subjects, categories, new[] { "k0", "k1", "k2", "k3" }, false);

            AssertPermutation(thawed, keys.Length);
            Assert.That(thawed, Is.EqualTo(new[] { 3, 0, 1, 2 }),
                "Unfreezing must fall back to the deterministic order (severity first), keeping every row.");
        }
    }
}
