using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public sealed class RuntimeBlackboardSafetyTests
    {
        [Test]
        public void ConcurrentHashAndSerializationUseStableExclusiveScratch()
        {
            using var blackboard = new RuntimeBlackboard(initialCapacity: 128);
            for (int i = 0; i < 128; i++)
            {
                blackboard.SetInt(i, i * 17);
                blackboard.SetFloat(1000 + i, i + 0.25f);
            }

            blackboard.EnableConcurrentStorageAccess();
            ulong expectedHash = blackboard.ComputeHash();
            byte[] expectedPayload = Serialize(blackboard);

            Assert.DoesNotThrow(() =>
                Parallel.For(0, 128, _ =>
                {
                    if (blackboard.ComputeHash() != expectedHash)
                    {
                        throw new InvalidOperationException("Concurrent hash changed without a value mutation.");
                    }

                    byte[] payload = Serialize(blackboard);
                    if (!BytesEqual(expectedPayload, payload))
                    {
                        throw new InvalidOperationException("Concurrent serialization changed without a value mutation.");
                    }
                }));
        }

        [Test]
        public void FloatChangesUseTheSameBitwiseContractAsHashing()
        {
            using var blackboard = new RuntimeBlackboard();
            const int Key = 41;

            blackboard.SetFloat(Key, -0f);
            ulong negativeZeroStamp = blackboard.GetStamp(Key);
            ulong negativeZeroHash = blackboard.ComputeHash();

            blackboard.SetFloat(Key, +0f);

            Assert.Greater(blackboard.GetStamp(Key), negativeZeroStamp);
            Assert.AreNotEqual(negativeZeroHash, blackboard.ComputeHash());

            float firstNaN = IntBitsToFloat(unchecked((int)0x7FC00001u));
            float secondNaN = IntBitsToFloat(unchecked((int)0x7FC00002u));
            blackboard.SetFloat(Key, firstNaN);
            ulong firstNaNStamp = blackboard.GetStamp(Key);
            ulong firstNaNHash = blackboard.ComputeHash();

            blackboard.SetFloat(Key, secondNaN);

            Assert.Greater(blackboard.GetStamp(Key), firstNaNStamp);
            Assert.AreNotEqual(firstNaNHash, blackboard.ComputeHash());
        }

        [Test]
        public void AttachedDeltaAcceptsProducerThreadSignalButKeepsControlOnOwnerThread()
        {
            const int Key = 73;
            using var source = new RuntimeBlackboard();
            using var target = new RuntimeBlackboard();
            using var delta = new BTBlackboardDelta(1);
            source.EnableConcurrentStorageAccess();
            delta.TrackKey(Key);
            delta.Attach(source, flushExistingValues: false);

            Task.Run(() => source.SetInt(Key, 99)).GetAwaiter().GetResult();

            Assert.IsTrue(delta.TryFlush(source, out ArraySegment<byte> patch));
            BTBlackboardDelta.Apply(target, patch);
            Assert.AreEqual(99, target.GetInt(Key));

            Exception wrongThreadFailure = null;
            Task.Run(() =>
            {
                try
                {
                    delta.TryFlush(source, out _);
                }
                catch (Exception exception)
                {
                    wrongThreadFailure = exception;
                }
            }).GetAwaiter().GetResult();

            Assert.That(wrongThreadFailure, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void SubTreeOutputSchemaFailureDoesNotPartiallyCommitEarlierPorts()
        {
            int firstParentKey = RuntimeBlackboard.DefaultStringHashFunc("FirstParent");
            int secondParentKey = RuntimeBlackboard.DefaultStringHashFunc("SecondParent");
            const int FirstLocalKey = 101;
            const int SecondLocalKey = 102;
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddInt("FirstParent", RuntimeBlackboardSyncFlags.LocalOnly)
                .AddBool("SecondParent", RuntimeBlackboardSyncFlags.LocalOnly)
                .Build();
            var blackboard = new RuntimeBlackboard(schema: schema);
            blackboard.SetInt(firstParentKey, 7);
            blackboard.SetBool(secondParentKey, false);

            var child = new TwoIntWriteNode(FirstLocalKey, 42, SecondLocalKey, 99);
            var subTree = new RuntimeSubTreeNode { Child = child };
            subTree.SetPortRemapping(
                new[] { FirstLocalKey, SecondLocalKey },
                new[] { firstParentKey, secondParentKey });

            using var tree = new RuntimeBehaviorTree(new RuntimeRootNode { Child = subTree }, blackboard);
            Assert.Throws<InvalidOperationException>(() => tree.Tick());
            Assert.AreEqual(7, blackboard.GetInt(firstParentKey));
            Assert.IsFalse(blackboard.GetBool(secondParentKey));
        }

        [Test]
        public void SubTreeObserverFailureOccursAfterAllOutputsCommit()
        {
            const int FirstParentKey = 201;
            const int SecondParentKey = 202;
            const int FirstLocalKey = 301;
            const int SecondLocalKey = 302;
            var blackboard = new RuntimeBlackboard();
            var child = new TwoIntWriteNode(FirstLocalKey, 11, SecondLocalKey, 22);
            var subTree = new RuntimeSubTreeNode { Child = child };
            subTree.SetPortRemapping(
                new[] { FirstLocalKey, SecondLocalKey },
                new[] { FirstParentKey, SecondParentKey });
            blackboard.AddObserver(FirstParentKey, (_, __) => throw new InvalidOperationException("Observer failure."));

            using var tree = new RuntimeBehaviorTree(new RuntimeRootNode { Child = subTree }, blackboard);
            Assert.Throws<AggregateException>(() => tree.Tick());
            Assert.AreEqual(11, blackboard.GetInt(FirstParentKey));
            Assert.AreEqual(22, blackboard.GetInt(SecondParentKey));
        }

        [Test]
        public void ResetToSchemaDefaults_CommitsFinalStateBeforeStableOrderedNotifications()
        {
            const int FirstDefaultKey = 10;
            const int RemovedKey = 15;
            const int SecondDefaultKey = 20;
            var schema = new RuntimeBlackboardSchema(
                new[]
                {
                    new RuntimeBlackboardKeyDefinition(
                        FirstDefaultKey,
                        "FirstDefault",
                        RuntimeBlackboardValueType.Int,
                        RuntimeBlackboardSyncFlags.LocalOnly,
                        hasDefaultValue: true,
                        defaultValue: RuntimeBlackboardValue.Int(1)),
                    new RuntimeBlackboardKeyDefinition(
                        RemovedKey,
                        "Removed",
                        RuntimeBlackboardValueType.Int,
                        RuntimeBlackboardSyncFlags.LocalOnly,
                        hasDefaultValue: false,
                        defaultValue: default),
                    new RuntimeBlackboardKeyDefinition(
                        SecondDefaultKey,
                        "SecondDefault",
                        RuntimeBlackboardValueType.Bool,
                        RuntimeBlackboardSyncFlags.LocalOnly,
                        hasDefaultValue: true,
                        defaultValue: RuntimeBlackboardValue.Bool(true))
                });
            using var blackboard = new RuntimeBlackboard(schema: schema);
            blackboard.SetInt(FirstDefaultKey, 99);
            blackboard.SetInt(RemovedKey, 5);
            blackboard.SetBool(SecondDefaultKey, false);
            var observedKeys = new List<int>();
            blackboard.AddGlobalObserver((key, committed) =>
            {
                observedKeys.Add(key);
                Assert.That(committed.GetInt(FirstDefaultKey), Is.EqualTo(1));
                Assert.That(committed.HasKey(RemovedKey), Is.False);
                Assert.That(committed.GetBool(SecondDefaultKey), Is.True);
            });

            blackboard.ResetToSchemaDefaults();

            Assert.That(observedKeys, Is.EqualTo(new[] { FirstDefaultKey, RemovedKey, SecondDefaultKey }));
            Assert.That(blackboard.GetInt(FirstDefaultKey), Is.EqualTo(1));
            Assert.That(blackboard.HasKey(RemovedKey), Is.False);
            Assert.That(blackboard.GetBool(SecondDefaultKey), Is.True);

            ulong firstDefaultStamp = blackboard.GetStamp(FirstDefaultKey);
            ulong secondDefaultStamp = blackboard.GetStamp(SecondDefaultKey);
            observedKeys.Clear();
            blackboard.ResetToSchemaDefaults();
            Assert.That(observedKeys, Is.Empty);
            Assert.That(blackboard.GetStamp(FirstDefaultKey), Is.EqualTo(firstDefaultStamp));
            Assert.That(blackboard.GetStamp(SecondDefaultKey), Is.EqualTo(secondDefaultStamp));
        }

#if UNITY_EDITOR
        [Test]
        public void DebugEntriesAreCopiedWithoutExposingMutableStorage()
        {
            using var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(1, 10);
            var entries = new List<RuntimeBlackboardDebugEntry>();

            blackboard.CopyDebugEntries(entries);
            entries.Clear();

            Assert.AreEqual(10, blackboard.GetInt(1));
            blackboard.CopyDebugEntries(entries);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(RuntimeBlackboardValueType.Int, entries[0].ValueType);
        }
#endif

        private static byte[] Serialize(RuntimeBlackboard blackboard)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                blackboard.WriteTo(writer);
            }

            return stream.ToArray();
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static float IntBitsToFloat(int bits)
        {
            byte[] bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }

        private sealed class TwoIntWriteNode : RuntimeNode
        {
            private readonly int _firstKey;
            private readonly int _firstValue;
            private readonly int _secondKey;
            private readonly int _secondValue;

            public TwoIntWriteNode(int firstKey, int firstValue, int secondKey, int secondValue)
            {
                _firstKey = firstKey;
                _firstValue = firstValue;
                _secondKey = secondKey;
                _secondValue = secondValue;
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                blackboard.SetInt(_firstKey, _firstValue);
                blackboard.SetInt(_secondKey, _secondValue);
                return RuntimeState.Success;
            }
        }
    }
}
