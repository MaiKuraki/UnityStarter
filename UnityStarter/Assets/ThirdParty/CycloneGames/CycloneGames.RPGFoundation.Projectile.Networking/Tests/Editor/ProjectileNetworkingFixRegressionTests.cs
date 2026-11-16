using System;
using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
{
    /// <summary>
    /// Locks in the networking fixes from 2026-11-16: the snapshot history used to scan bucket
    /// heads (which made filling N entities O(N^2)), reconciliation reported NaN as "no
    /// divergence", and the snapshot sink had no implementation at all.
    /// </summary>
    public sealed class ProjectileNetworkingFixRegressionTests
    {
        private static readonly ProjectileSpaceProfile Space = ProjectileSpaceProfile.Full3D();

        private static ProjectileSnapshotMessage Message(ulong id, int tick, ushort sequence, float x)
        {
            return new ProjectileSnapshotMessage(
                id,
                ownerEntityId: 1UL,
                targetEntityId: 0UL,
                definitionId: 7001,
                serverTick: tick,
                sequence: sequence,
                predictionKey: 0,
                lifecycleFlags: 0u,
                age: tick * 0.1f,
                radius: 0.2f,
                position: new NetworkVector3(x, 0f, 0f),
                previousPosition: new NetworkVector3(x - 1f, 0f, 0f),
                velocity: new NetworkVector3(10f, 0f, 0f));
        }

        [Test]
        public void History_TracksTheNewestFramePerEntity()
        {
            var history = new ProjectileNetworkSnapshotHistory(capacity: 64, depthPerEntity: 4);

            for (int tick = 1; tick <= 12; tick++)
            {
                ProjectileSnapshotMessage message = Message(1UL, tick, (ushort)tick, tick);
                history.Record(in message);
            }

            Assert.That(history.Count, Is.EqualTo(4), "depth bounds the retained frames");
            Assert.That(history.TryGetLatest(1UL, out ProjectileSnapshotMessage latest), Is.True);
            Assert.That(latest.ServerTick, Is.EqualTo(12));

            Assert.That(history.TryGet(1UL, 12, 12, out ProjectileSnapshotMessage exact), Is.True);
            Assert.That(exact.Position.X, Is.EqualTo(12f).Within(0.001f));

            // The oldest frames were overwritten by the ring.
            Assert.That(history.TryGet(1UL, 1, 1, out _), Is.False);

            // AtOrBefore must never return a future tick.
            Assert.That(history.TryGetAtOrBefore(1UL, 10, out ProjectileSnapshotMessage older), Is.True);
            Assert.That(older.ServerTick, Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void History_RemoveProjectileFreesTheSlotForReuse()
        {
            var history = new ProjectileNetworkSnapshotHistory(capacity: 8, depthPerEntity: 2);

            ProjectileSnapshotMessage first = Message(1UL, 1, 1, 1f);
            history.Record(in first);
            Assert.That(history.Count, Is.EqualTo(1));

            Assert.That(history.RemoveProjectile(1UL), Is.EqualTo(1));
            Assert.That(history.Count, Is.EqualTo(0));
            Assert.That(history.TryGetLatest(1UL, out _), Is.False);

            // The freed bucket must be reusable, otherwise the hash and the bucket pool drift.
            ProjectileSnapshotMessage second = Message(2UL, 1, 1, 2f);
            history.Record(in second);
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history.TryGetLatest(2UL, out ProjectileSnapshotMessage latest), Is.True);
            Assert.That(latest.ProjectileEntityId, Is.EqualTo(2UL));
        }

        [Test]
        public void History_ScalesToTenThousandEntities()
        {
            const int entities = 10000;
            var history = new ProjectileNetworkSnapshotHistory(entities * 8, 8);

            var messages = new ProjectileSnapshotMessage[entities];
            for (int i = 0; i < entities; i++)
            {
                messages[i] = Message((ulong)(i + 1), 1, 1, i);
            }

            for (int i = 0; i < messages.Length; i++)
            {
                history.Record(in messages[i]);
            }

            Assert.That(history.Count, Is.EqualTo(entities * 1));

            for (int i = 0; i < entities; i += 97)
            {
                Assert.That(history.TryGetLatest((ulong)(i + 1), out ProjectileSnapshotMessage latest), Is.True,
                    "entity " + (i + 1) + " must resolve");
                Assert.That(latest.ProjectileEntityId, Is.EqualTo((ulong)(i + 1)));
            }

            Assert.That(history.RemoveProjectile(5000UL), Is.EqualTo(1));
            Assert.That(history.TryGetLatest(5000UL, out _), Is.False);
            Assert.That(history.TryGetLatest(5001UL, out _), Is.True, "removing one entity must not disturb its neighbour");
        }

        [Test]
        public void Reconciliation_TreatsNonFinitePredictionAsHardSnap()
        {
            ProjectileSnapshotMessage authoritative = Message(7UL, 10, 3, 10f);

            ProjectileSnapshotMessage corrupt = new ProjectileSnapshotMessage(
                7UL, 1UL, 0UL, 7001, 10, 3, 0, 0u, 1f, 0.2f,
                new NetworkVector3(float.NaN, 0f, 0f),
                new NetworkVector3(0f, 0f, 0f),
                new NetworkVector3(10f, 0f, 0f));

            bool detected = ProjectileNetworkReconciliation.RequiresCorrection(
                in corrupt,
                in authoritative,
                ProjectileNetworkCorrectionPolicy.Default,
                out ProjectileNetworkCorrectionFlags flags,
                out _);

            Assert.That(detected, Is.True, "NaN must not be reported as 'no divergence'");
            Assert.That((flags & ProjectileNetworkCorrectionFlags.HardSnap) != 0, Is.True);
            Assert.That(
                ProjectileNetworkReconciliation.TryCreateCorrection(
                    in corrupt, in authoritative, ProjectileNetworkCorrectionPolicy.Default, out _),
                Is.True);
        }

        [Test]
        public void Reconciliation_StillDetectsOrdinaryDrift()
        {
            ProjectileSnapshotMessage authoritative = Message(7UL, 10, 3, 10f);
            ProjectileSnapshotMessage drifted = Message(7UL, 10, 3, 910f);

            Assert.That(
                ProjectileNetworkReconciliation.RequiresCorrection(
                    in drifted, in authoritative, ProjectileNetworkCorrectionPolicy.Default, out _, out _),
                Is.True);
        }

        [Test]
        public void Sink_CreatesUnknownAndPatchesKnown()
        {
            var world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var sink = new ProjectileWorldSnapshotSink(world, new FixedDefinitionResolver(definition));

            ProjectileSnapshot snapshot = Snapshot(1UL, tick: 10, age: 2f, x: 100f);
            Assert.That(sink.TryApplySnapshot(in snapshot), Is.True);
            Assert.That(sink.SpawnedCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(1));

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState created), Is.True);
            Assert.That(created.Age, Is.EqualTo(2f).Within(0.001f));
            Assert.That(created.CurrentTick, Is.EqualTo(10));

            int bounceBefore = created.RemainingBounceCount;
            ProjectileSnapshot patch = Snapshot(1UL, tick: 12, age: 2.5f, x: 120f);
            Assert.That(sink.TryApplySnapshot(in patch), Is.True);
            Assert.That(sink.AppliedCount, Is.EqualTo(1L));

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState patched), Is.True);
            Assert.That(patched.Position.X, Is.EqualTo(120f).Within(0.001f));
            Assert.That(patched.RemainingBounceCount, Is.EqualTo(bounceBefore));
        }

        [Test]
        public void Sink_RejectsStaleInvalidAndUnknownDefinition()
        {
            var world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var sink = new ProjectileWorldSnapshotSink(world, new FixedDefinitionResolver(definition));

            ProjectileSnapshot initial = Snapshot(1UL, tick: 20, age: 2f, x: 200f);
            Assert.That(sink.TryApplySnapshot(in initial), Is.True);

            ProjectileSnapshot stale = Snapshot(1UL, tick: 15, age: 1f, x: 100f);
            Assert.That(sink.TryApplySnapshot(in stale), Is.False);
            Assert.That(sink.StaleCount, Is.EqualTo(1L));

            ProjectileSnapshot malformed = new ProjectileSnapshot(
                2UL, 1UL, 0UL, new ProjectileDefinitionId(7001), ProjectileLifecycleFlags.None,
                20, 0, 2f, 0.2f,
                new ProjectileVector3(float.NaN, 0f, 0f), ProjectileVector3.Zero, ProjectileVector3.Zero);
            Assert.That(sink.TryApplySnapshot(in malformed), Is.False);
            Assert.That(sink.InvalidCount, Is.EqualTo(1L));

            ProjectileSnapshot unknown = new ProjectileSnapshot(
                3UL, 1UL, 0UL, new ProjectileDefinitionId(9999), ProjectileLifecycleFlags.None,
                20, 0, 2f, 0.2f, ProjectileVector3.Zero, ProjectileVector3.Zero, ProjectileVector3.Zero);
            Assert.That(sink.TryApplySnapshot(in unknown), Is.False);
            Assert.That(sink.MissingDefinitionCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(1), "rejected snapshots must not create projectiles");
        }

        [Test]
        public void Sink_HardResetReplacesTheHandle()
        {
            var world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var sink = new ProjectileWorldSnapshotSink(world, new FixedDefinitionResolver(definition));

            ProjectileSnapshot initial = Snapshot(1UL, tick: 10, age: 1f, x: 100f);
            Assert.That(sink.TryApplySnapshot(in initial), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState before), Is.True);

            ProjectileSnapshot reset = Snapshot(1UL, tick: 30, age: 3f, x: 300f);
            Assert.That(sink.TryResetFromSnapshot(in reset), Is.True);
            Assert.That(sink.ResetCount, Is.EqualTo(1L));

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState after), Is.True);
            Assert.That(after.Handle, Is.Not.EqualTo(before.Handle));
            Assert.That(after.Position.X, Is.EqualTo(300f).Within(0.001f));
            Assert.That(world.ActiveCount, Is.EqualTo(1));
        }

        private static ProjectileWorld CreateWorld(int capacity)
        {
            var config = new ProjectileWorldConfig(capacity, 64, 8, in Space, 2, 0f, 1);
            return new ProjectileWorld(in config);
        }

        private static ProjectileDefinition Definition()
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(7001),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit | ProjectileLifecycleFlags.IgnoreOwner,
                initialSpeed: 10f,
                maxSpeed: 10f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 100f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 4,
                collisionLayerMask: 0,
                effectPayloadId: 0);
        }

        private static ProjectileSnapshot Snapshot(ulong id, int tick, float age, float x)
        {
            return new ProjectileSnapshot(
                id,
                ownerEntityId: 1UL,
                targetEntityId: 0UL,
                new ProjectileDefinitionId(7001),
                ProjectileLifecycleFlags.DespawnOnHit,
                tick,
                predictionKey: 0,
                age,
                radius: 0.2f,
                new ProjectileVector3(x, 0f, 0f),
                new ProjectileVector3(x - 1f, 0f, 0f),
                new ProjectileVector3(10f, 0f, 0f));
        }

        private sealed class FixedDefinitionResolver : IProjectileDefinitionResolver
        {
            private readonly ProjectileDefinition _definition;

            public FixedDefinitionResolver(in ProjectileDefinition definition)
            {
                _definition = definition;
            }

            public bool TryGetDefinition(int definitionId, out ProjectileDefinition definition)
            {
                if (definitionId == _definition.DefinitionId.Value)
                {
                    definition = _definition;
                    return true;
                }

                definition = default;
                return false;
            }
        }
    }
}
