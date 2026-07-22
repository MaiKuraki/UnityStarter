using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Tests.Editor.Framework;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public class BehaviorTreeConsistencyTests
    {
        [Test]
        public void RuntimeBehaviorTree_StopPreventsFurtherTicksUntilPlay()
        {
            var leaf = new FixedStateNode(RuntimeState.Success);
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(leaf);
            int terminationCount = 0;
            tree.Terminated += _ => terminationCount++;

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, leaf.RunCount);
                Assert.IsTrue(tree.IsStopped);
                Assert.AreEqual(RuntimeState.Success, tree.State);
                Assert.AreEqual(1, terminationCount);

                tree.Stop();

                Assert.IsTrue(tree.IsStopped);
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, leaf.RunCount);
                Assert.AreEqual(1, terminationCount);

                tree.Play();

                Assert.IsFalse(tree.IsStopped);
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(2, leaf.RunCount);
                Assert.AreEqual(2, terminationCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_StopAbortsRunningStatefulActions()
        {
            var leaf = new RecordingStatefulActionNode(RuntimeState.Running, RuntimeState.Running);
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(leaf);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(1, leaf.StartCount);

                tree.Stop();

                Assert.IsTrue(tree.IsStopped);
                Assert.AreEqual(1, leaf.HaltCount);
                Assert.AreEqual(RuntimeState.NotEntered, tree.State);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_WakeUpUsesAtomicMaxBudget()
        {
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Success));
            tree.TickInterval = 1000;

            try
            {
                Parallel.For(0, 128, i => tree.WakeUp((i % 8) + 1));

                Assert.IsTrue(tree.HasWakeUpRequest);
                Assert.AreEqual(8, tree.WakeUpTickBudget);

                int immediateTicks = 0;
                while (tree.ShouldTick())
                {
                    immediateTicks++;
                    if (immediateTicks > 16)
                    {
                        Assert.Fail("Wake-up budget did not drain.");
                    }
                }

                Assert.AreEqual(8, immediateTicks);
                Assert.IsFalse(tree.HasWakeUpRequest);
                Assert.AreEqual(0, tree.WakeUpTickBudget);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SelectorLowerPriorityAbortInterruptsRunningChild()
        {
            int interruptKey = Animator.StringToHash("Interrupt");
            var highPriority = new ConditionalRunningNode(interruptKey, true);
            var lowPriority = new RecordingStatefulActionNode(RuntimeState.Running, RuntimeState.Running);
            var selector = BehaviorTreeTestFactory.CreateSelector(highPriority, lowPriority);
            selector.AbortType = RuntimeAbortType.LowerPriority;

            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);

            try
            {
                tree.Blackboard.SetBool(interruptKey, false);
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(1, lowPriority.StartCount);
                Assert.AreEqual(1, highPriority.RunCount);

                tree.Blackboard.SetBool(interruptKey, true);
                Assert.AreEqual(RuntimeState.Running, tree.Tick());

                Assert.AreEqual(1, lowPriority.HaltCount);
                Assert.GreaterOrEqual(highPriority.RunCount, 2);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeSelector_EmptyChildrenAlwaysFails()
        {
            foreach (RuntimeAbortType abortType in Enum.GetValues(typeof(RuntimeAbortType)))
            {
                var selector = new RuntimeSelector
                {
                    AbortType = abortType
                };
                selector.Seal();
                var tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);

                try
                {
                    Assert.AreEqual(RuntimeState.Failure, tree.Tick(), abortType.ToString());
                }
                finally
                {
                    tree.Dispose();
                }
            }
        }

        [Test]
        public void RuntimeBlackboard_SettingDifferentTypeReplacesPreviousSlot()
        {
            const int key = 909;
            var blackboard = new RuntimeBlackboard();

            try
            {
                blackboard.SetInt(key, 12);
                Assert.IsTrue(blackboard.TryGetInt(key, out _));

                blackboard.SetFloat(key, 1.5f);

                Assert.IsFalse(blackboard.TryGetInt(key, out _));
                Assert.IsTrue(blackboard.TryGetFloat(key, out float value));
                Assert.AreEqual(1.5f, value);
                Assert.IsTrue(blackboard.HasKey(key));
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_ObserverRemovalUsesStableNotificationSnapshot()
        {
            const int key = 910;
            var blackboard = new RuntimeBlackboard();
            int firstCalls = 0;
            int secondCalls = 0;

            BlackboardObserverCallback second = (_, _) => secondCalls++;
            BlackboardObserverCallback first = (_, bb) =>
            {
                firstCalls++;
                bb.RemoveObserver(key, second);
            };

            try
            {
                blackboard.AddObserver(key, first);
                blackboard.AddObserver(key, second);

                blackboard.SetInt(key, 1);
                blackboard.SetInt(key, 2);

                Assert.AreEqual(2, firstCalls);
                Assert.AreEqual(1, secondCalls);
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_DuplicateObserverRegistrationIsIgnored()
        {
            const int key = 913;
            var blackboard = new RuntimeBlackboard();
            int calls = 0;
            BlackboardObserverCallback observer = (_, _) => calls++;

            try
            {
                blackboard.AddObserver(key, observer);
                blackboard.AddObserver(key, observer);

                blackboard.SetInt(key, 1);

                Assert.AreEqual(1, calls);
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_ObserverCanMutateConcurrentStorageAfterWriteLockIsReleased()
        {
            const int sourceKey = 911;
            const int targetKey = 912;
            var blackboard = new RuntimeBlackboard();

            try
            {
                blackboard.EnableConcurrentStorageAccess();
                blackboard.AddObserver(sourceKey, (_, bb) => bb.SetInt(targetKey, bb.GetInt(sourceKey) + 1));

                blackboard.SetInt(sourceKey, 4);

                Assert.AreEqual(5, blackboard.GetInt(targetKey));
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_DisposeIsIdempotentAndRejectsFurtherAccess()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(1, 2);

            blackboard.Dispose();

            Assert.IsTrue(blackboard.IsDisposed);
            Assert.DoesNotThrow(blackboard.Dispose);
            Assert.Throws<ObjectDisposedException>(() => blackboard.GetInt(1));
            Assert.Throws<ObjectDisposedException>(() => blackboard.SetInt(1, 3));
        }

        [Test]
        public void RuntimeBlackboard_RejectsParentCyclesAndEmptyStringKeys()
        {
            using var parent = new RuntimeBlackboard();
            using var child = new RuntimeBlackboard(parent);

            Assert.Throws<InvalidOperationException>(() => parent.Parent = child);
            Assert.Throws<ArgumentException>(() => child.SetInt(string.Empty, 1));
        }

        [Test]
        public void RuntimeBlackboard_SameValueWriteDoesNotChangeStampOrNotify()
        {
            const int key = 913;
            var blackboard = new RuntimeBlackboard();
            int observerCalls = 0;

            try
            {
                blackboard.AddObserver(key, (_, _) => observerCalls++);

                blackboard.SetInt(key, 7);
                ulong firstStamp = blackboard.GetStamp(key);

                blackboard.SetInt(key, 7);

                Assert.AreEqual(firstStamp, blackboard.GetStamp(key));
                Assert.AreEqual(1, observerCalls);
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_TypeReplacementKeepsSingleValueSlot()
        {
            const int key = 914;
            var blackboard = new RuntimeBlackboard();

            try
            {
                blackboard.SetInt(key, 7);
                blackboard.SetFloat(key, 1.5f);

                Assert.IsFalse(blackboard.TryGetInt(key, out _));
                Assert.IsTrue(blackboard.TryGetFloat(key, out float value));
                Assert.AreEqual(1.5f, value);
                Assert.IsTrue(blackboard.HasKey(key));
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboardSchema_AppliesDefaultsAndRejectsUnknownOrWrongTypeWrites()
        {
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddInt("Health", 100)
                .AddBool("Alerted", false, RuntimeBlackboardSyncFlags.LocalOnly)
                .Build();

            int healthKey = RuntimeBlackboard.DefaultStringHashFunc("Health");
            int alertedKey = RuntimeBlackboard.DefaultStringHashFunc("Alerted");
            int unknownKey = RuntimeBlackboard.DefaultStringHashFunc("Unknown");
            var blackboard = new RuntimeBlackboard(schema: schema);

            try
            {
                Assert.AreSame(schema, blackboard.Schema);
                Assert.AreEqual(100, blackboard.GetInt(healthKey));
                Assert.IsFalse(blackboard.GetBool(alertedKey));

                Assert.Throws<KeyNotFoundException>(() => blackboard.SetInt(unknownKey, 1));
                Assert.Throws<InvalidOperationException>(() => blackboard.SetFloat(healthKey, 1f));
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboardSchema_RejectsNetworkedObjectKeys()
        {
            Assert.Throws<ArgumentException>(
                () => new RuntimeBlackboardKeyDefinition(
                    1,
                    "Target",
                    RuntimeBlackboardValueType.Object,
                    RuntimeBlackboardSyncFlags.Networked,
                    false,
                    default));
        }

        [Test]
        public void RuntimeBlackboard_HashAndSerializationRemainDeterministic()
        {
            const int intKey = 101;
            const int floatKey = 202;
            const int boolKey = 303;
            const int vectorKey = 404;
            const int longKey = 505;
            const int long2Key = 606;
            const int long3Key = 707;

            var a = new RuntimeBlackboard();
            var b = new RuntimeBlackboard();
            var restored = new RuntimeBlackboard();

            try
            {
                a.SetInt(intKey, 7);
                a.SetFloat(floatKey, 1.25f);
                a.SetBool(boolKey, true);
                a.SetVector3(vectorKey, new Vector3(2f, 3f, 4f));
                a.SetLong(longKey, 1234567890123L);
                a.SetLong2(long2Key, new RuntimeBlackboardLong2(-1L, 2L));
                a.SetLong3(long3Key, new RuntimeBlackboardLong3(3L, -4L, 5L));

                b.SetLong3(long3Key, new RuntimeBlackboardLong3(3L, -4L, 5L));
                b.SetLong2(long2Key, new RuntimeBlackboardLong2(-1L, 2L));
                b.SetLong(longKey, 1234567890123L);
                b.SetVector3(vectorKey, new Vector3(2f, 3f, 4f));
                b.SetBool(boolKey, true);
                b.SetFloat(floatKey, 1.25f);
                b.SetInt(intKey, 7);

                Assert.AreEqual(a.ComputeHash(), b.ComputeHash());

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                a.WriteTo(writer);
                writer.Flush();
                stream.Position = 0;

                using var reader = new BinaryReader(stream);
                restored.ReadFrom(reader);

                Assert.AreEqual(a.ComputeHash(), restored.ComputeHash());
                Assert.AreEqual(7, restored.GetInt(intKey));
                Assert.AreEqual(1.25f, restored.GetFloat(floatKey));
                Assert.IsTrue(restored.GetBool(boolKey));
                Assert.AreEqual(new Vector3(2f, 3f, 4f), restored.GetVector3(vectorKey));
                Assert.AreEqual(1234567890123L, restored.GetLong(longKey));
                Assert.AreEqual(new RuntimeBlackboardLong2(-1L, 2L), restored.GetLong2(long2Key));
                Assert.AreEqual(new RuntimeBlackboardLong3(3L, -4L, 5L), restored.GetLong3(long3Key));
            }
            finally
            {
                a.Dispose();
                b.Dispose();
                restored.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_SerializationSkipsLocalObjectValuesAndStamps()
        {
            const int intKey = 105;
            const int objectKey = 106;
            var source = new RuntimeBlackboard();
            var restored = new RuntimeBlackboard();

            try
            {
                source.SetInt(intKey, 42);
                source.SetObject(objectKey, new object());
                var localObject = new object();
                restored.SetObject(objectKey, localObject);

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                source.WriteTo(writer);
                writer.Flush();
                stream.Position = 0;

                using var reader = new BinaryReader(stream);
                restored.ReadFrom(reader);

                Assert.AreEqual(42, restored.GetInt(intKey));
                Assert.AreSame(localObject, restored.GetObject<object>(objectKey));
                Assert.AreEqual(source.ComputeHash(), restored.ComputeHash());
            }
            finally
            {
                source.Dispose();
                restored.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_ReadFromKeepsSequenceMonotonic()
        {
            const int sourceKey = 110;
            const int existingKey = 111;
            const int newKey = 112;
            using var source = new RuntimeBlackboard();
            using var target = new RuntimeBlackboard();

            source.SetInt(sourceKey, 1);
            target.SetInt(existingKey, 1);
            target.SetInt(existingKey, 2);
            target.SetInt(existingKey, 3);
            ulong priorStamp = target.GetStamp(existingKey);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                source.WriteTo(writer);
            }

            stream.Position = 0;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                target.ReadFrom(reader);
            }

            target.SetInt(newKey, 1);
            Assert.That(target.GetStamp(newKey), Is.GreaterThan(priorStamp));
        }

        [Test]
        public void RuntimeBlackboard_SerializationLimitsCountValuesNotStampMetadata()
        {
            const int firstKey = 107;
            const int secondKey = 108;
            var source = new RuntimeBlackboard();
            var restored = new RuntimeBlackboard();

            try
            {
                source.SetInt(firstKey, 1);
                source.SetInt(secondKey, 2);

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                source.WriteTo(writer);
                writer.Flush();
                stream.Position = 0;

                using var reader = new BinaryReader(stream);
                restored.ReadFrom(reader, new RuntimeBlackboardSerializationLimits(maxEntriesPerType: 2, maxTotalEntries: 2));

                Assert.AreEqual(1, restored.GetInt(firstKey));
                Assert.AreEqual(2, restored.GetInt(secondKey));
            }
            finally
            {
                source.Dispose();
                restored.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_SchemaSnapshotOnlySerializesSnapshotKeysAndPreservesLocalState()
        {
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddInt("Health", RuntimeBlackboardSyncFlags.Networked)
                .AddInt("DeltaOnly", RuntimeBlackboardSyncFlags.Delta)
                .AddBool("LocalFlag", RuntimeBlackboardSyncFlags.LocalOnly)
                .Build();

            int healthKey = RuntimeBlackboard.DefaultStringHashFunc("Health");
            int deltaOnlyKey = RuntimeBlackboard.DefaultStringHashFunc("DeltaOnly");
            int localFlagKey = RuntimeBlackboard.DefaultStringHashFunc("LocalFlag");
            var source = new RuntimeBlackboard(schema: schema);
            var target = new RuntimeBlackboard(schema: schema);

            try
            {
                source.SetInt(healthKey, 50);
                source.SetInt(deltaOnlyKey, 10);
                source.SetBool(localFlagKey, true);

                target.SetInt(healthKey, 1);
                target.SetInt(deltaOnlyKey, 99);
                target.SetBool(localFlagKey, false);

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                source.WriteTo(writer);
                writer.Flush();
                stream.Position = 0;

                using var reader = new BinaryReader(stream);
                target.ReadFrom(reader);

                Assert.AreEqual(50, target.GetInt(healthKey));
                Assert.AreEqual(99, target.GetInt(deltaOnlyKey));
                Assert.IsFalse(target.GetBool(localFlagKey));
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_DisposePermanentlyClosesWakeUpState()
        {
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));
            Task producer = Task.Run(() =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    tree.WakeUp(8);
                }
            });

            tree.Dispose();
            Assert.DoesNotThrow(() => producer.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => tree.WakeUp(8));
            Assert.IsTrue(tree.IsDisposed);
            Assert.IsFalse(tree.HasWakeUpRequest);
            Assert.AreEqual(0, tree.WakeUpTickBudget);
        }

        [Test]
        public void RuntimeBehaviorTree_RejectsDisposedBlackboardBeforeTakingNodeOwnership()
        {
            var child = new FixedStateNode(RuntimeState.Success);
            var root = new RuntimeRootNode { Child = child };
            var disposedBlackboard = new RuntimeBlackboard();
            disposedBlackboard.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                new RuntimeBehaviorTree(root, disposedBlackboard, new RuntimeBTContext()));

            using var replacementBlackboard = new RuntimeBlackboard();
            using var tree = new RuntimeBehaviorTree(root, replacementBlackboard, new RuntimeBTContext());
            Assert.AreEqual(RuntimeState.Success, tree.Tick());
        }

        [Test]
        public void RuntimeBehaviorTree_AwakeFailureRestoresCallerBlackboardContextAndOwnership()
        {
            var previousContext = new RuntimeBTContext();
            var blackboard = new RuntimeBlackboard { Context = previousContext };
            var node = new ThrowingAwakeNode();
            var root = new RuntimeRootNode { Child = node };

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new RuntimeBehaviorTree(root, blackboard, new RuntimeBTContext()));

                Assert.IsFalse(blackboard.IsDisposed);
                Assert.AreSame(previousContext, blackboard.Context);
                Assert.AreEqual(1, node.DisposeCount);
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_AwakeFailureDisposesInternallyCreatedBlackboard()
        {
            var node = new ThrowingAwakeNode();
            var root = new RuntimeRootNode { Child = node };

            Assert.Throws<InvalidOperationException>(() =>
                new RuntimeBehaviorTree(root, null, new RuntimeBTContext()));

            Assert.That(node.BlackboardSeenDuringDispose, Is.Not.Null);
            Assert.IsTrue(node.BlackboardSeenDuringDispose.IsDisposed);
            Assert.AreEqual(1, node.DisposeCount);
        }

        [Test]
        public void RuntimeUtilitySelector_CopiesScoreKeysAndFreezesConfigurationWhenOwned()
        {
            const int firstScoreKey = 7101;
            const int secondScoreKey = 7102;
            var first = new FixedStateNode(RuntimeState.Success);
            var second = new FixedStateNode(RuntimeState.Success);
            var selector = new RuntimeUtilitySelector();
            selector.AddChild(first);
            selector.AddChild(second);

            int[] scoreKeys = { firstScoreKey, secondScoreKey };
            selector.SetScoreKeys(scoreKeys);
            scoreKeys[0] = secondScoreKey;
            scoreKeys[1] = firstScoreKey;

            using RuntimeBehaviorTree tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);
            tree.Blackboard.SetFloat(firstScoreKey, 10f);
            tree.Blackboard.SetFloat(secondScoreKey, 1f);

            Assert.Throws<InvalidOperationException>(() => selector.SetScoreKeys(scoreKeys));
            Assert.AreEqual(RuntimeState.Success, tree.Tick());
            Assert.AreEqual(1, first.RunCount);
            Assert.AreEqual(0, second.RunCount);
        }

        [Test]
        public void RuntimeBlackboard_HashScopesExcludeUnrelatedKeysAndSeparateValueTypes()
        {
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddInt("SnapshotOnly", RuntimeBlackboardSyncFlags.Snapshot)
                .AddInt("DeltaOnly", RuntimeBlackboardSyncFlags.Delta)
                .Build();
            int snapshotKey = RuntimeBlackboard.DefaultStringHashFunc("SnapshotOnly");
            int deltaKey = RuntimeBlackboard.DefaultStringHashFunc("DeltaOnly");
            using var scoped = new RuntimeBlackboard(schema: schema);
            using var intValue = new RuntimeBlackboard();
            using var floatValue = new RuntimeBlackboard();

            scoped.SetInt(snapshotKey, 1);
            scoped.SetInt(deltaKey, 2);
            ulong snapshotHash = scoped.ComputeHash(RuntimeBlackboardNetworkScope.Snapshot);
            ulong networkedHash = scoped.ComputeHash(RuntimeBlackboardNetworkScope.Networked);
            scoped.SetInt(deltaKey, 3);

            Assert.AreEqual(snapshotHash, scoped.ComputeHash(RuntimeBlackboardNetworkScope.Snapshot));
            Assert.AreNotEqual(networkedHash, scoped.ComputeHash(RuntimeBlackboardNetworkScope.Networked));

            intValue.SetInt(7, 1065353216);
            floatValue.SetFloat(7, 1f);
            Assert.AreNotEqual(
                intValue.ComputeHash(RuntimeBlackboardNetworkScope.Networked),
                floatValue.ComputeHash(RuntimeBlackboardNetworkScope.Networked));
        }

        [Test]
        public void RuntimeBlackboard_ReadFromRejectsPrimitiveObjectKeyCollisionBeforeMutation()
        {
            const int key = 211;
            using var source = new RuntimeBlackboard();
            using var target = new RuntimeBlackboard();
            var localObject = new object();
            source.SetInt(key, 9);
            target.SetObject(key, localObject);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                source.WriteTo(writer);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            Assert.Throws<InvalidDataException>(() => target.ReadFrom(reader));
            Assert.AreSame(localObject, target.GetObject<object>(key));
        }

        [Test]
        public void RuntimeBlackboard_ReadFromRejectsRemoteStampBeyondDeclaredSequence()
        {
            const int key = 215;
            using var source = new RuntimeBlackboard();
            using var target = new RuntimeBlackboard();
            source.SetInt(key, 9);
            target.SetInt(key, 3);
            byte[] payload;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    source.WriteTo(writer);
                }

                payload = stream.ToArray();
            }

            Buffer.BlockCopy(BitConverter.GetBytes(2UL), 0, payload, payload.Length - sizeof(ulong), sizeof(ulong));
            using var malformed = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(malformed);
            Assert.Throws<InvalidDataException>(() => target.ReadFrom(reader));
            Assert.AreEqual(3, target.GetInt(key));
        }

        [Test]
        public void RuntimeBlackboard_ClearRetainsMonotonicRevisionAndEmitsTrackedRemovals()
        {
            const int key = 212;
            using var source = new RuntimeBlackboard();
            using var target = new RuntimeBlackboard();
            using var delta = new BTBlackboardDelta(1);
            delta.TrackKey(key);
            delta.Attach(source);
            source.SetInt(key, 4);
            Assert.IsTrue(delta.TryFlush(source, out ArraySegment<byte> initial));
            BTBlackboardDelta.Apply(target, initial);
            ulong revisionBeforeClear = source.Revision;

            source.Clear();

            Assert.Greater(source.Revision, revisionBeforeClear);
            Assert.IsTrue(delta.TryFlush(source, out ArraySegment<byte> removal));
            BTBlackboardDelta.Apply(target, removal);
            Assert.IsFalse(target.HasKey(key));

            source.SetInt(key, 5);
            Assert.Greater(source.GetStamp(key), revisionBeforeClear);
        }

        [Test]
        public void BlackboardDelta_OverBudgetFlushCanBeRetriedWithoutLosingMutation()
        {
            const int key = 213;
            using var source = new RuntimeBlackboard();
            using var target = new RuntimeBlackboard();
            using var delta = new BTBlackboardDelta(1);
            delta.TrackKey(key);
            delta.Attach(source);
            source.SetLong3(key, new RuntimeBlackboardLong3(1, 2, 3));

            Assert.Throws<InvalidOperationException>(
                () => delta.TryFlush(source, maxPatchBytes: 16, out _));
            Assert.IsTrue(delta.TryFlush(source, maxPatchBytes: 64, out ArraySegment<byte> patch));
            BTBlackboardDelta.Apply(target, patch);
            Assert.AreEqual(new RuntimeBlackboardLong3(1, 2, 3), target.GetLong3(key));
        }

        [Test]
        public void BlackboardDelta_RevisionMismatchAbortsBatchBeforeMutation()
        {
            const int key = 214;
            using var source = new RuntimeBlackboard();
            using var target = new RuntimeBlackboard();
            using var delta = new BTBlackboardDelta(1);
            delta.TrackKey(key);
            source.SetInt(key, 10);
            Assert.IsTrue(delta.TryFlush(source, out ArraySegment<byte> patch));
            ulong expectedRevision = target.Revision;
            target.SetInt(999, 1);

            Assert.Throws<InvalidOperationException>(
                () => BTBlackboardDelta.Apply(target, patch, 1, expectedRevision));
            Assert.IsFalse(target.HasKey(key));
        }

        [Test]
        public void BlackboardDelta_RoundTripsMutationsAndRemovals()
        {
            const int intKey = 111;
            const int boolKey = 222;
            const int long3Key = 333;
            var source = new RuntimeBlackboard();
            var target = new RuntimeBlackboard();
            var delta = new BTBlackboardDelta();

            try
            {
                delta.TrackKey(intKey);
                delta.TrackKey(boolKey);
                delta.TrackKey(long3Key);

                source.SetInt(intKey, 5);
                source.SetBool(boolKey, true);
                source.SetLong3(long3Key, new RuntimeBlackboardLong3(10L, 20L, 30L));

                Assert.IsTrue(delta.TryFlush(source, out var patch1));
                BTBlackboardDelta.Apply(target, patch1);

                Assert.AreEqual(5, target.GetInt(intKey));
                Assert.IsTrue(target.GetBool(boolKey));
                Assert.AreEqual(new RuntimeBlackboardLong3(10L, 20L, 30L), target.GetLong3(long3Key));

                source.SetInt(intKey, 9);
                source.Remove(boolKey);
                source.SetLong3(long3Key, new RuntimeBlackboardLong3(30L, 20L, 10L));

                Assert.IsTrue(delta.TryFlush(source, out var patch2));
                BTBlackboardDelta.Apply(target, patch2);

                Assert.AreEqual(9, target.GetInt(intKey));
                Assert.IsFalse(target.HasKey(boolKey));
                Assert.AreEqual(new RuntimeBlackboardLong3(30L, 20L, 10L), target.GetLong3(long3Key));
            }
            finally
            {
                delta.Dispose();
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_SameValueWriteDoesNotEmitPatchWhenAttached()
        {
            const int key = 112;
            var source = new RuntimeBlackboard();
            var delta = new BTBlackboardDelta(1);

            try
            {
                delta.TrackKey(key);
                delta.Attach(source);

                source.SetInt(key, 5);
                Assert.IsTrue(delta.TryFlush(source, out _));
                Assert.IsFalse(delta.TryFlush(source, out _));

                source.SetInt(key, 5);
                Assert.IsFalse(delta.TryFlush(source, out _));

                source.SetInt(key, 6);
                Assert.IsTrue(delta.TryFlush(source, out _));
            }
            finally
            {
                delta.Dispose();
                source.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_TrackKeyExceedingCapacityFailsFast()
        {
            var delta = new BTBlackboardDelta(1);

            try
            {
                delta.TrackKey(1);
                delta.TrackKey(1);

                Assert.Throws<InvalidOperationException>(() => delta.TrackKey(2));
            }
            finally
            {
                delta.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_CreateForSchemaTracksOnlyDeltaKeys()
        {
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddInt("SnapshotOnly", RuntimeBlackboardSyncFlags.Snapshot)
                .AddInt("DeltaOnly", RuntimeBlackboardSyncFlags.Delta)
                .AddInt("Networked", RuntimeBlackboardSyncFlags.Networked)
                .Build();

            int snapshotOnlyKey = RuntimeBlackboard.DefaultStringHashFunc("SnapshotOnly");
            int deltaOnlyKey = RuntimeBlackboard.DefaultStringHashFunc("DeltaOnly");
            int networkedKey = RuntimeBlackboard.DefaultStringHashFunc("Networked");
            var source = new RuntimeBlackboard(schema: schema);
            var target = new RuntimeBlackboard(schema: schema);
            BTBlackboardDelta delta = BTBlackboardDelta.CreateForSchema(schema);

            try
            {
                delta.Attach(source);
                source.SetInt(snapshotOnlyKey, 1);
                source.SetInt(deltaOnlyKey, 2);
                source.SetInt(networkedKey, 3);

                Assert.IsTrue(delta.TryFlush(source, out var patch));
                BTBlackboardDelta.Apply(target, patch);

                Assert.AreEqual(0, target.GetInt(snapshotOnlyKey));
                Assert.AreEqual(2, target.GetInt(deltaOnlyKey));
                Assert.AreEqual(3, target.GetInt(networkedKey));
            }
            finally
            {
                delta.Dispose();
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTreeBuilder_BindsBlackboardSchema()
        {
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddInt("Counter", 3)
                .Build();

            int counterKey = RuntimeBlackboard.DefaultStringHashFunc("Counter");
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .WithBlackboardSchema(schema)
                .Action(blackboard =>
                {
                    blackboard.SetInt(counterKey, blackboard.GetInt(counterKey) + 1);
                    return RuntimeState.Success;
                })
                .Build();

            try
            {
                Assert.AreSame(schema, tree.Blackboard.Schema);
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(4, tree.Blackboard.GetInt(counterKey));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void NetworkSnapshot_RoundTripRestoresBlackboardAndHash()
        {
            const int intKey = 333;
            const int vectorKey = 444;

            var sourceTree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));
            var targetTree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));

            try
            {
                sourceTree.Blackboard.SetInt(intKey, 42);
                sourceTree.Blackboard.SetVector3(vectorKey, new Vector3(9f, 8f, 7f));

                var snapshot = BTNetworkSync.CaptureSnapshot(sourceTree);
                var bytes = BTNetworkSync.SerializeSnapshot(snapshot);
                var restoredSnapshot = BTNetworkSync.DeserializeSnapshot(bytes);

                BTNetworkSync.ApplyBlackboardSnapshot(targetTree, restoredSnapshot);

                Assert.AreEqual(sourceTree.Blackboard.ComputeHash(), targetTree.Blackboard.ComputeHash());
                Assert.AreEqual(42, targetTree.Blackboard.GetInt(intKey));
                Assert.AreEqual(new Vector3(9f, 8f, 7f), targetTree.Blackboard.GetVector3(vectorKey));
                Assert.IsFalse(BTNetworkSync.CheckDesync(
                    targetTree,
                    snapshot.TreeStateHash,
                    RuntimeBlackboardNetworkScope.Snapshot));
            }
            finally
            {
                sourceTree.Dispose();
                targetTree.Dispose();
            }
        }

        [Test]
        public void NetworkSnapshot_BufferRoundTripUsesValidCounts()
        {
            const int intKey = 707;
            var sourceTree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));

            try
            {
                sourceTree.Blackboard.SetInt(intKey, 91);
                using var buffer = new BTStateSnapshotBuffer();

                BTStateSnapshot snapshot = BTNetworkSync.CaptureSnapshot(sourceTree, buffer);
                ArraySegment<byte> payload = BTNetworkSync.SerializeSnapshot(snapshot, buffer);
                var bytes = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array, payload.Offset, bytes, 0, payload.Count);

                BTStateSnapshot restored = BTNetworkSync.DeserializeSnapshot(bytes);

                Assert.AreEqual(snapshot.NodeCount, restored.NodeCount);
                Assert.AreEqual(snapshot.BlackboardDataLength, restored.BlackboardDataLength);
                Assert.AreEqual(snapshot.BlackboardHash, restored.BlackboardHash);
            }
            finally
            {
                sourceTree.Dispose();
            }
        }

        [Test]
        public void NetworkSnapshot_RejectsLegacyV1FormatMarker()
        {
            var sourceTree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));

            try
            {
                BTStateSnapshot snapshot = BTNetworkSync.CaptureSnapshot(sourceTree);
                byte[] bytes = BTNetworkSync.SerializeSnapshot(snapshot);
                bytes[0] = (byte)'B';
                bytes[1] = (byte)'T';
                bytes[2] = (byte)'S';
                bytes[3] = (byte)'1';

                Assert.Throws<InvalidDataException>(() => BTNetworkSync.DeserializeSnapshot(bytes));
            }
            finally
            {
                sourceTree.Dispose();
            }
        }

        [Test]
        public void NetworkSnapshot_RejectsTrailingBytes()
        {
            var sourceTree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));

            try
            {
                var snapshot = BTNetworkSync.CaptureSnapshot(sourceTree);
                byte[] bytes = BTNetworkSync.SerializeSnapshot(snapshot);
                var malformed = new byte[bytes.Length + 1];
                Buffer.BlockCopy(bytes, 0, malformed, 0, bytes.Length);
                malformed[malformed.Length - 1] = 0x7F;

                Assert.Throws<InvalidDataException>(() => BTNetworkSync.DeserializeSnapshot(malformed));
            }
            finally
            {
                sourceTree.Dispose();
            }
        }

        private sealed class ThrowingAwakeNode : RuntimeNode
        {
            public int DisposeCount { get; private set; }
            public RuntimeBlackboard BlackboardSeenDuringDispose { get; private set; }

            public override void OnAwake()
            {
                throw new InvalidOperationException("Expected awake failure.");
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                return RuntimeState.Success;
            }

            protected override void OnDispose(RuntimeBlackboard blackboard)
            {
                DisposeCount++;
                BlackboardSeenDuringDispose = blackboard;
            }
        }

    }
}
