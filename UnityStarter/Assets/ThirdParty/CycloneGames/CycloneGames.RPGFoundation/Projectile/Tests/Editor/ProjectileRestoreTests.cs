using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor
{
    /// <summary>
    /// Locks in the batch restore entry point. The decisive case is
    /// <see cref="Restore_CaptureThenRestoreReproducesOrigin"/> plus
    /// <see cref="Restore_ReplayAfterRollbackMatchesTheFirstRun"/>: together they are the only
    /// evidence that rollback is actually possible, because anything the restore forgets shows up
    /// as a state mismatch.
    /// </summary>
    public sealed class ProjectileRestoreTests
    {
        private static readonly ProjectileSpaceProfile Space = ProjectileSpaceProfile.Full3D();

        [Test]
        public void Restore_MergeKeepsUnmentionedProjectiles()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);

            for (int i = 1; i <= 4; i++)
            {
                Spawn(world, definition, (ulong)i, i);
            }

            var batch = new ProjectileSnapshot[2];
            batch[0] = Snapshot(1UL, 50, 10f);
            batch[1] = Snapshot(2UL, 50, 20f);

            ProjectileRestoreResult result = world.TryRestoreStates(batch, ProjectileRestoreMode.Merge, catalog);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Applied, Is.EqualTo(2));
            Assert.That(result.Despawned, Is.EqualTo(0));
            Assert.That(world.ActiveCount, Is.EqualTo(4));
        }

        [Test]
        public void Restore_ReplaceRemovesUnmentionedProjectiles()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);

            for (int i = 1; i <= 4; i++)
            {
                Spawn(world, definition, (ulong)i, i);
            }

            var batch = new ProjectileSnapshot[1];
            batch[0] = Snapshot(2UL, 50, 20f);

            ProjectileRestoreResult result = world.TryRestoreStates(batch, ProjectileRestoreMode.Replace, catalog);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Applied, Is.EqualTo(1));
            Assert.That(result.Despawned, Is.EqualTo(3));
            Assert.That(world.ActiveCount, Is.EqualTo(1));
            Assert.That(world.TryGetStateByNetworkEntityId(2UL, out _), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out _), Is.False);
        }

        [Test]
        public void Restore_CreatesUnknownIdsAndKeepsKnownHandles()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);
            Spawn(world, definition, 1UL, 1);
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState before), Is.True);

            var batch = new ProjectileSnapshot[2];
            batch[0] = Snapshot(1UL, 60, 111f);
            batch[1] = Snapshot(2UL, 60, 222f);

            ProjectileRestoreResult result = world.TryRestoreStates(batch, ProjectileRestoreMode.Merge, catalog);

            Assert.That(result.Spawned, Is.EqualTo(1));
            Assert.That(world.ActiveCount, Is.EqualTo(2));
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState after), Is.True);
            Assert.That(after.Handle, Is.EqualTo(before.Handle), "a patch must not replace the handle");
            Assert.That(after.Position.X, Is.EqualTo(111f).Within(0.001f));
            Assert.That(world.TryGetStateByNetworkEntityId(2UL, out ProjectileState created), Is.True);
            Assert.That(created.Position.X, Is.EqualTo(222f).Within(0.001f));
        }

        [Test]
        public void Restore_RejectsWholeBatchOnInvalidEntry()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);
            for (int i = 1; i <= 3; i++)
            {
                Spawn(world, definition, (ulong)i, i);
            }

            ulong before = StateHash(world);
            int activeBefore = world.ActiveCount;

            var batch = new ProjectileSnapshot[2];
            batch[0] = Snapshot(1UL, 50, 10f);
            batch[1] = new ProjectileSnapshot(
                2UL, 1UL, 0UL, new ProjectileDefinitionId(9001), ProjectileLifecycleFlags.None,
                50, 0, 1f, 0.2f,
                new ProjectileVector3(float.NaN, 0f, 0f), ProjectileVector3.Zero, ProjectileVector3.Zero);

            ProjectileRestoreResult result = world.TryRestoreStates(batch, ProjectileRestoreMode.Replace, catalog);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(ProjectileRestoreRejection.InvalidSnapshot));
            Assert.That(world.ActiveCount, Is.EqualTo(activeBefore), "a rejected batch must not mutate the world");
            Assert.That(StateHash(world), Is.EqualTo(before), "a rejected batch must leave state untouched");
        }

        [Test]
        public void Restore_RejectsUnknownDefinitionAndCapacity()
        {
            ProjectileWorld world = CreateWorld(4);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);
            Spawn(world, definition, 1UL, 1);

            var unknown = new ProjectileSnapshot[1];
            unknown[0] = new ProjectileSnapshot(
                99UL, 1UL, 0UL, new ProjectileDefinitionId(12345), ProjectileLifecycleFlags.None,
                50, 0, 1f, 0.2f, ProjectileVector3.Zero, ProjectileVector3.Zero, ProjectileVector3.Zero);
            ProjectileRestoreResult missingDefinition = world.TryRestoreStates(
                unknown, ProjectileRestoreMode.Merge, catalog);

            Assert.That(missingDefinition.Succeeded, Is.False);
            Assert.That(missingDefinition.Rejection, Is.EqualTo(ProjectileRestoreRejection.UnknownDefinition));

            var tooMany = new ProjectileSnapshot[5];
            tooMany[0] = Snapshot(1UL, 50, 10f);
            for (int i = 1; i < 5; i++)
            {
                tooMany[i] = Snapshot((ulong)(100 + i), 50, 10f + i);
            }

            ProjectileRestoreResult capacity = world.TryRestoreStates(
                tooMany, ProjectileRestoreMode.Merge, catalog);

            Assert.That(capacity.Succeeded, Is.False);
            Assert.That(capacity.Rejection, Is.EqualTo(ProjectileRestoreRejection.Capacity));
            Assert.That(capacity.RejectedCapacity, Is.EqualTo(1));
            Assert.That(world.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void Restore_EmptyBatchSemantics()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);
            for (int i = 1; i <= 3; i++)
            {
                Spawn(world, definition, (ulong)i, i);
            }

            ProjectileRestoreResult merge = world.TryRestoreStates(
                ReadOnlySpan<ProjectileSnapshot>.Empty, ProjectileRestoreMode.Merge, catalog);
            Assert.That(merge.Succeeded, Is.True);
            Assert.That(world.ActiveCount, Is.EqualTo(3));

            ProjectileRestoreResult replace = world.TryRestoreStates(
                ReadOnlySpan<ProjectileSnapshot>.Empty, ProjectileRestoreMode.Replace, catalog);
            Assert.That(replace.Succeeded, Is.True);
            Assert.That(replace.Despawned, Is.EqualTo(3));
            Assert.That(world.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Restore_TickAnchorFollowsTheMode()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);
            Spawn(world, definition, 1UL, 1);
            world.Step(1f / 60f, 200, null, null);

            var older = new ProjectileSnapshot[1];
            older[0] = Snapshot(1UL, 50, 10f);

            world.TryRestoreStates(older, ProjectileRestoreMode.Merge, catalog);
            Assert.That(world.LatestTick, Is.EqualTo(200), "Merge must not rewind the anchor");

            world.TryRestoreStates(older, ProjectileRestoreMode.Replace, catalog);
            Assert.That(world.LatestTick, Is.EqualTo(50), "Replace is a rewind and must move the anchor back");
        }

        [Test]
        public void Restore_DuplicateIdsAreFlaggedAndLastWins()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);

            var batch = new ProjectileSnapshot[2];
            batch[0] = Snapshot(7UL, 40, 10f);
            batch[1] = Snapshot(7UL, 40, 20f);

            ProjectileRestoreResult result = world.TryRestoreStates(batch, ProjectileRestoreMode.Merge, catalog);

            Assert.That(result.DuplicateIds, Is.EqualTo(1));
            Assert.That(world.ActiveCount, Is.EqualTo(1));
            Assert.That(world.TryGetStateByNetworkEntityId(7UL, out ProjectileState state), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(20f).Within(0.001f), "later entry wins, deterministically");
        }

        [Test]
        public void Restore_CaptureThenRestoreReproducesOrigin()
        {
            ProjectileWorld world = PrepareSimulatedWorld(out ProjectileDefinition definition, out Catalog catalog);
            ulong origin = StateHash(world);

            var captured = new ProjectileSnapshot[world.ActiveCount];
            Assert.That(world.CaptureSnapshots(captured), Is.EqualTo(captured.Length));

            ProjectileRestoreResult result = world.TryRestoreStates(
                captured, ProjectileRestoreMode.Replace, catalog);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Applied, Is.EqualTo(captured.Length));
            Assert.That(StateHash(world), Is.EqualTo(origin),
                "a capture round trip must be lossless, otherwise rollback cannot work");
        }

        [Test]
        public void Restore_ReplayAfterRollbackMatchesTheFirstRun()
        {
            ProjectileWorld world = PrepareSimulatedWorld(out _, out Catalog catalog);
            const int originTick = 120;
            const int replaySteps = 60;

            var captured = new ProjectileSnapshot[world.ActiveCount];
            world.CaptureSnapshots(captured);

            for (int step = 0; step < replaySteps; step++)
            {
                world.Step(1f / 60f, originTick + 1 + step, null, null);
            }

            ulong forward = StateHash(world);
            int forwardTick = world.LatestTick;

            world.TryRestoreStates(captured, ProjectileRestoreMode.Replace, catalog);
            Assert.That(world.LatestTick, Is.EqualTo(originTick));

            for (int step = 0; step < replaySteps; step++)
            {
                world.Step(1f / 60f, originTick + 1 + step, null, null);
            }

            Assert.That(StateHash(world), Is.EqualTo(forward),
                "replaying after a rollback must reproduce the same state bit for bit");
            Assert.That(world.LatestTick, Is.EqualTo(forwardTick));
        }

        [Test]
        public void Restore_FuzzKeepsInvariants()
        {
            const int capacity = 128;
            ProjectileWorld world = CreateWorld(capacity);
            ProjectileDefinition definition = Definition();
            var catalog = new Catalog(definition);

            var prng = new Prng((ulong)4242 ^ 0x2545F4914F6CDD1DUL);
            ulong nextId = 1UL;
            int tick = 0;
            var batch = new ProjectileSnapshot[capacity];

            for (int op = 0; op < 2000; op++)
            {
                int action = prng.NextInt(100);
                if (action < 45)
                {
                    Spawn(world, definition, nextId++, prng.NextInt(200));
                }
                else if (action < 70)
                {
                    ReadOnlySpan<ProjectileState> live = world.ActiveStates;
                    if (live.Length > 0)
                    {
                        world.Despawn(live[prng.NextInt(live.Length)].Handle);
                    }
                }
                else if (action < 92)
                {
                    tick++;
                    world.Step(1f / 60f, tick, null, null);
                }
                else
                {
                    int count = world.CaptureSnapshots(batch);
                    int keep = count == 0 ? 0 : prng.NextInt(count + 1);
                    var subset = new ProjectileSnapshot[keep];
                    for (int i = 0; i < keep; i++)
                    {
                        subset[i] = batch[prng.NextInt(count)];
                    }

                    ProjectileRestoreMode mode = prng.NextInt(2) == 0
                        ? ProjectileRestoreMode.Merge
                        : ProjectileRestoreMode.Replace;
                    world.TryRestoreStates(subset, mode, catalog);
                }

                ReadOnlySpan<ProjectileState> states = world.ActiveStates;
                Assert.That(states.Length, Is.EqualTo(world.ActiveCount));
                Assert.That(world.ActiveCount, Is.LessThanOrEqualTo(world.Capacity));
                for (int i = 0; i < states.Length; i++)
                {
                    Assert.That(states[i].IsFinite, Is.True);
                    Assert.That(
                        world.TryGetStateByNetworkEntityId(states[i].NetworkEntityId, out ProjectileState resolved),
                        Is.True);
                    Assert.That(resolved.Handle, Is.EqualTo(states[i].Handle));
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static ProjectileWorld CreateWorld(int capacity)
        {
            var config = new ProjectileWorldConfig(capacity, Math.Max(16, capacity * 2), 8, in Space, 2, 0f, 1);
            return new ProjectileWorld(in config);
        }

        private static ProjectileDefinition Definition()
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(9001),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit,
                initialSpeed: 7f,
                maxSpeed: 7f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 1000f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 0,
                collisionLayerMask: 0,
                effectPayloadId: 0);
        }

        private static void Spawn(ProjectileWorld world, in ProjectileDefinition definition, ulong id, int offset)
        {
            var request = new ProjectileSpawnRequest(
                definition, 1UL, id, 0UL, 0, 0, 0u,
                new ProjectileVector3(offset % 97, 0f, offset % 31),
                new ProjectileVector3(0f, 0f, 1f),
                new ProjectileVector3(0f, 0f, 7f));
            world.TrySpawn(in request, out _);
        }

        private static ProjectileSnapshot Snapshot(ulong id, int tick, float x)
        {
            return new ProjectileSnapshot(
                id,
                ownerEntityId: 1UL,
                targetEntityId: 0UL,
                new ProjectileDefinitionId(9001),
                ProjectileLifecycleFlags.DespawnOnHit,
                tick,
                predictionKey: 0,
                age: tick * 0.05f,
                radius: 0.2f,
                new ProjectileVector3(x, 0f, 0f),
                new ProjectileVector3(x - 1f, 0f, 0f),
                new ProjectileVector3(7f, 0f, 0f));
        }

        /// <summary>128 projectiles simulated to tick 120, ready for capture and rollback.</summary>
        private static ProjectileWorld PrepareSimulatedWorld(out ProjectileDefinition definition, out Catalog catalog)
        {
            ProjectileWorld world = CreateWorld(256);
            definition = Definition();
            catalog = new Catalog(definition);

            var prng = new Prng(12345UL ^ 0x9E3779B97F4A7C15UL);
            for (int i = 0; i < 128; i++)
            {
                Spawn(world, definition, (ulong)(i + 1), prng.NextInt(500));
            }

            for (int step = 0; step < 120; step++)
            {
                world.Step(1f / 60f, step + 1, null, null);
            }

            return world;
        }

        /// <summary>Order-independent hash: a restore may reorder dense slots.</summary>
        private static ulong StateHash(ProjectileWorld world)
        {
            ReadOnlySpan<ProjectileState> states = world.ActiveStates;
            ulong sum = 0;
            for (int i = 0; i < states.Length; i++)
            {
                ulong h = 1469598103934665603UL;
                h = Mix(h, states[i].NetworkEntityId);
                h = Mix(h, (ulong)BitConverter.SingleToInt32Bits(states[i].Position.X));
                h = Mix(h, (ulong)BitConverter.SingleToInt32Bits(states[i].Position.Z));
                h = Mix(h, (ulong)BitConverter.SingleToInt32Bits(states[i].Velocity.Z));
                h = Mix(h, (ulong)BitConverter.SingleToInt32Bits(states[i].Age));
                h = Mix(h, (ulong)(uint)states[i].CurrentTick);
                h = Mix(h, (ulong)(uint)states[i].RemainingBounceCount);
                h = Mix(h, (ulong)(uint)states[i].RemainingPierceCount);
                sum += h;
            }

            return sum;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
            return hash;
        }

        private sealed class Catalog : IProjectileDefinitionCatalog
        {
            private readonly ProjectileDefinition _definition;

            public Catalog(in ProjectileDefinition definition)
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

        private struct Prng
        {
            private ulong _state;

            public Prng(ulong seed)
            {
                _state = seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL;
            }

            public int NextInt(int maxExclusive)
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return maxExclusive <= 1 ? 0 : (int)(z % (ulong)maxExclusive);
            }
        }
    }
}
