using System;
using CycloneGames.AIResources.Core;
using NUnit.Framework;

namespace CycloneGames.AIResources.Tests.Editor
{
    public sealed class AIResourceTableTests
    {
        [Test]
        public void Claim_IsExclusivePerResource()
        {
            using var table = new AIResourceTable(8);

            Assert.AreEqual(AIResourceClaimResult.Claimed, table.TryClaim(1, ownerId: 10, priority: 0, nowSeconds: 0d));
            Assert.AreEqual(AIResourceClaimResult.Rejected, table.TryClaim(1, ownerId: 20, priority: 0, nowSeconds: 0d));
            Assert.IsTrue(table.IsClaimedBy(1, 10));
            Assert.IsFalse(table.IsClaimedBy(1, 20));
            Assert.AreEqual(1, table.ClaimedCount);
        }

        [Test]
        public void Claim_BySameOwnerRefreshes()
        {
            using var table = new AIResourceTable(8);
            table.TryClaim(1, 10, 0, 0d, leaseSeconds: 1d);

            Assert.AreEqual(AIResourceClaimResult.Refreshed, table.TryClaim(1, 10, 0, 0.5d, leaseSeconds: 1d));
            Assert.AreEqual(0, table.SweepExpired(1.2d), "the refresh must extend the lease");
            Assert.AreEqual(0, table.SweepExpired(1.4d));
            Assert.AreEqual(1, table.SweepExpired(1.6d));
        }

        [Test]
        public void Preemption_RequiresStrictlyHigherPriority()
        {
            using var table = new AIResourceTable(8);
            table.TryClaim(1, 10, priority: 1, nowSeconds: 0d);

            Assert.AreEqual(AIResourceClaimResult.Rejected, table.TryClaim(1, 20, priority: 1, nowSeconds: 0d, preempt: true));
            Assert.AreEqual(AIResourceClaimResult.Preempted, table.TryClaim(1, 20, priority: 2, nowSeconds: 0d, preempt: true));
            Assert.IsTrue(table.IsClaimedBy(1, 20));
            Assert.IsFalse(table.IsClaimedBy(1, 10));
        }

        [Test]
        public void Release_RequiresOwner()
        {
            using var table = new AIResourceTable(8);
            table.TryClaim(1, 10, 0, 0d);

            Assert.IsFalse(table.Release(1, 20));
            Assert.IsTrue(table.IsClaimedBy(1, 10));
            Assert.IsTrue(table.Release(1, 10));
            Assert.AreEqual(0, table.ClaimedCount);
            Assert.IsFalse(table.Release(1, 10));
        }

        [Test]
        public void ReleaseOwner_ReleasesEveryClaim()
        {
            using var table = new AIResourceTable(8);
            table.TryClaim(1, 10, 0, 0d);
            table.TryClaim(2, 10, 0, 0d);
            table.TryClaim(3, 20, 0, 0d);

            Assert.AreEqual(2, table.ReleaseOwner(10));
            Assert.AreEqual(1, table.ClaimedCount);
            Assert.IsTrue(table.IsClaimedBy(3, 20));
        }

        [Test]
        public void LeaseExpiry_ReclaimsWithoutOwnerCooperation()
        {
            using var table = new AIResourceTable(8);
            table.TryClaim(1, 10, 0, nowSeconds: 0d, leaseSeconds: 2d);

            Assert.AreEqual(0, table.SweepExpired(1.9d));
            Assert.IsTrue(table.IsClaimedBy(1, 10));
            Assert.AreEqual(1, table.SweepExpired(2.1d));
            Assert.IsFalse(table.TryGetOwner(1, out _));
        }

        [Test]
        public void PermanentClaims_DoNotExpire()
        {
            using var table = new AIResourceTable(8);
            table.TryClaim(1, 10, 0, nowSeconds: 0d, leaseSeconds: 0d);

            Assert.AreEqual(0, table.SweepExpired(1_000_000d));
            Assert.IsTrue(table.IsClaimedBy(1, 10));
        }

        [Test]
        public void FullTable_ReportsExhausted()
        {
            using var table = new AIResourceTable(2);

            Assert.AreEqual(AIResourceClaimResult.Claimed, table.TryClaim(1, 10, 0, 0d));
            Assert.AreEqual(AIResourceClaimResult.Claimed, table.TryClaim(2, 10, 0, 0d));
            Assert.AreEqual(AIResourceClaimResult.Exhausted, table.TryClaim(3, 10, 0, 0d));

            Assert.IsTrue(table.Release(1, 10));
            Assert.AreEqual(AIResourceClaimResult.Claimed, table.TryClaim(4, 10, 0, 0d));
        }

        [Test]
        public void Fuzz_RandomOperationsKeepTableConsistent()
        {
            const int ResourceCount = 64;
            const int OwnerCount = 8;
            const int Operations = 40000;

            using var table = new AIResourceTable(ResourceCount);
            var random = new Random(20261108);
            var expectedOwner = new int[ResourceCount];
            for (int i = 0; i < ResourceCount; i++)
            {
                expectedOwner[i] = -1;
            }

            int expectedCount = 0;

            for (int op = 0; op < Operations; op++)
            {
                int resource = random.Next(ResourceCount);
                int owner = random.Next(OwnerCount);
                int priority = random.Next(4);
                int action = random.Next(10);

                if (action < 6)
                {
                    AIResourceClaimResult result = table.TryClaim(resource, owner, priority, op, leaseSeconds: 0d, preempt: action == 5);
                    if (result == AIResourceClaimResult.Claimed || result == AIResourceClaimResult.Preempted)
                    {
                        if (expectedOwner[resource] < 0)
                        {
                            expectedCount++;
                        }

                        expectedOwner[resource] = owner;
                    }
                    else if (result == AIResourceClaimResult.Refreshed)
                    {
                        Assert.AreEqual(owner, expectedOwner[resource], "refresh must mean the caller already owns it");
                    }
                }
                else if (action < 9)
                {
                    bool released = table.Release(resource, owner);
                    bool expected = expectedOwner[resource] == owner;
                    Assert.AreEqual(expected, released, "release result must match ownership");
                    if (expected)
                    {
                        expectedOwner[resource] = -1;
                        expectedCount--;
                    }
                }
                else
                {
                    expectedCount -= table.ReleaseOwner(owner);
                    for (int i = 0; i < ResourceCount; i++)
                    {
                        if (expectedOwner[i] == owner)
                        {
                            expectedOwner[i] = -1;
                        }
                    }
                }

                Assert.AreEqual(expectedCount, table.ClaimedCount, "claimed count drifted at op " + op);

                for (int i = 0; i < ResourceCount; i++)
                {
                    bool claimed = table.TryGetOwner(i, out int actual);
                    if (expectedOwner[i] < 0)
                    {
                        Assert.IsFalse(claimed, "resource " + i + " must be free at op " + op);
                    }
                    else
                    {
                        Assert.IsTrue(claimed, "resource " + i + " must be claimed at op " + op);
                        Assert.AreEqual(expectedOwner[i], actual, "resource " + i + " owner drifted at op " + op);
                    }
                }
            }
        }

        [Test]
        public void SteadyState_DoesNotRetainHeap()
        {
            using var table = new AIResourceTable(128);
            for (int i = 0; i < 128; i++)
            {
                table.TryClaim(i, i % 8, 0, 0d);
            }

            for (int i = 0; i < 2000; i++)
            {
                table.Release(i % 128, i % 8);
                table.TryClaim(i % 128, i % 8, 0, i);
            }

            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 200000; i++)
            {
                table.Release(i % 128, i % 8);
                table.TryClaim(i % 128, i % 8, 0, i);
                table.SweepExpired(i);
            }

            long after = GC.GetTotalMemory(true);
            Assert.Less(after - before, 1024 * 1024, "claim table must not retain heap in steady state");
        }
    }
}
