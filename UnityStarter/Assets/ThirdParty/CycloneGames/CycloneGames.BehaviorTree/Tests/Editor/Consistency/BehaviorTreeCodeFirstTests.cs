using System;
using System.IO;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public sealed class BehaviorTreeCodeFirstTests
    {
        private const int HAS_TARGET_KEY = 101;
        private const int HIT_COUNT_KEY = 102;

        [Test]
        public void Builder_ComposesLambdaTree()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Sequence()
                    .Condition(blackboard => blackboard.GetBool(HAS_TARGET_KEY))
                    .Action(blackboard =>
                    {
                        blackboard.SetInt(HIT_COUNT_KEY, blackboard.GetInt(HIT_COUNT_KEY) + 1);
                        return RuntimeState.Success;
                    })
                .End()
                .Build();

            try
            {
                Assert.AreEqual(RuntimeState.Failure, tree.Tick());
                Assert.AreEqual(0, tree.Blackboard.GetInt(HIT_COUNT_KEY));

                tree.Blackboard.SetBool(HAS_TARGET_KEY, true);
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, tree.Blackboard.GetInt(HIT_COUNT_KEY));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Builder_AutoClosesOpenCompositeScopes()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Sequence()
                    .Action(_ => RuntimeState.Success)
                .Build();

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Builder_CannotBeReusedAfterBuild()
        {
            var builder = new RuntimeBehaviorTreeBuilder()
                .Action(_ => RuntimeState.Success);

            RuntimeBehaviorTree tree = builder.Build();

            try
            {
                Assert.Throws<InvalidOperationException>(() => builder.Build());
                Assert.Throws<InvalidOperationException>(() => builder.Action(_ => RuntimeState.Success));
                Assert.Throws<InvalidOperationException>(() => builder.WithTickInterval(2));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Builder_RejectsMultipleRootChildren()
        {
            var builder = new RuntimeBehaviorTreeBuilder()
                .Action(_ => RuntimeState.Success);

            Assert.Throws<InvalidOperationException>(() => builder.Action(_ => RuntimeState.Success));
        }

        [Test]
        public void Builder_RejectsDecoratorWithoutChild()
        {
            var builder = new RuntimeBehaviorTreeBuilder()
                .Inverter();

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Builder_SupportsCommandAndConditionStrategy()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Sequence()
                    .Condition(new BoolKeyConditionStrategy(HAS_TARGET_KEY))
                    .Command(new IncrementIntCommand(HIT_COUNT_KEY))
                .End()
                .Build();

            try
            {
                tree.Blackboard.SetBool(HAS_TARGET_KEY, true);

                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, tree.Blackboard.GetInt(HIT_COUNT_KEY));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RandomChance_WithInvalidDenominatorFails()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .RandomChance(1f, 0f, seed: 1u)
                .Build();

            try
            {
                Assert.AreEqual(RuntimeState.Failure, tree.Tick());
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SelectorRandom_UsesSelectorFallbackSemantics()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .SelectorRandom(seed: 7u)
                    .Action(_ => RuntimeState.Failure)
                    .Action(blackboard =>
                    {
                        blackboard.SetBool(HAS_TARGET_KEY, true);
                        return RuntimeState.Success;
                    })
                .End()
                .Build();

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.IsTrue(tree.Blackboard.GetBool(HAS_TARGET_KEY));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void BlackboardReadFrom_RejectsExcessiveCountsWithoutClearingExistingState()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 9);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1UL);
                writer.Write(2);
                writer.Write(HIT_COUNT_KEY);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY + 1);
                writer.Write(2);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<InvalidDataException>(() => blackboard.ReadFrom(
                reader,
                new RuntimeBlackboardSerializationLimits(maxEntriesPerType: 1, maxTotalEntries: 1)));
            Assert.AreEqual(9, blackboard.GetInt(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardReadFrom_RejectsDuplicatePrimitiveKeysWithoutClearingExistingState()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 9);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1UL);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY);
                writer.Write(1);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY);
                writer.Write(1f);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<InvalidDataException>(() => blackboard.ReadFrom(reader));
            Assert.AreEqual(9, blackboard.GetInt(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardReadFrom_RejectsStampWithoutValueWithoutClearingExistingState()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 9);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1UL);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY + 1);
                writer.Write(1UL);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<InvalidDataException>(() => blackboard.ReadFrom(reader));
            Assert.AreEqual(9, blackboard.GetInt(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardDelta_RejectsExcessiveCounts()
        {
            var blackboard = new RuntimeBlackboard();

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(2);
                writer.Write(HIT_COUNT_KEY);
                writer.Write((byte)0);
                writer.Write(1);
            }

            byte[] patch = stream.ToArray();

            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(blackboard, patch, maxPatchEntries: 1));
            Assert.IsFalse(blackboard.HasKey(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardDelta_RejectsTrailingBytesWithoutMutation()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 7);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY);
                writer.Write((byte)0);
                writer.Write(99);
                writer.Write((byte)0x7F);
            }

            byte[] patch = stream.ToArray();

            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(blackboard, patch));
            Assert.AreEqual(7, blackboard.GetInt(HIT_COUNT_KEY));
        }

        private sealed class IncrementIntCommand : IRuntimeBTCommand
        {
            private readonly int _key;

            public IncrementIntCommand(int key)
            {
                _key = key;
            }

            public RuntimeState Execute(RuntimeBlackboard blackboard)
            {
                blackboard.SetInt(_key, blackboard.GetInt(_key) + 1);
                return RuntimeState.Success;
            }
        }

        private sealed class BoolKeyConditionStrategy : IRuntimeBTConditionStrategy
        {
            private readonly int _key;

            public BoolKeyConditionStrategy(int key)
            {
                _key = key;
            }

            public bool Evaluate(RuntimeBlackboard blackboard)
            {
                return blackboard.GetBool(_key);
            }
        }
    }
}
