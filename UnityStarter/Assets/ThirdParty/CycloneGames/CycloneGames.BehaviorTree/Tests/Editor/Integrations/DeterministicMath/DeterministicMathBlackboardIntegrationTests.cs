using System;
using CycloneGames.BehaviorTree.Integrations.DeterministicMath;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.DeterministicMath;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Integrations.DeterministicMath.Tests.Editor
{
    public sealed class DeterministicMathBlackboardIntegrationTests
    {
        [Test]
        public void FixedPointValuesRoundTripThroughRawLongSlots()
        {
            const int healthKey = 101;
            const int positionKey = 202;
            var blackboard = new RuntimeBlackboard();

            try
            {
                var health = FPInt64.FromString("12.5");
                var position = new FPVector3(FPInt64.FromInt(3), FPInt64.FromInt(-4), FPInt64.FromString("5.25"));

                blackboard.SetFPInt64(healthKey, health);
                blackboard.SetFPVector3(positionKey, position);

                Assert.AreEqual(health.RawValue, blackboard.GetLong(healthKey));
                Assert.AreEqual(health.RawValue, blackboard.GetFPInt64(healthKey).RawValue);
                Assert.AreEqual(position.X.RawValue, blackboard.GetFPVector3(positionKey).X.RawValue);
                Assert.AreEqual(position.Y.RawValue, blackboard.GetFPVector3(positionKey).Y.RawValue);
                Assert.AreEqual(position.Z.RawValue, blackboard.GetFPVector3(positionKey).Z.RawValue);
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void SchemaDefaultsAndDeltaSupportFixedPointKeys()
        {
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddFPInt64("Cooldown", FPInt64.FromString("1.25"), RuntimeBlackboardSyncFlags.Networked)
                .AddFPVector2("Velocity", new FPVector2(FPInt64.FromInt(1), FPInt64.FromInt(2)), RuntimeBlackboardSyncFlags.Delta)
                .Build();

            int cooldownKey = RuntimeBlackboard.DefaultStringHashFunc("Cooldown");
            int velocityKey = RuntimeBlackboard.DefaultStringHashFunc("Velocity");
            var source = new RuntimeBlackboard(schema: schema);
            var target = new RuntimeBlackboard(schema: schema);
            BTBlackboardDelta delta = BTBlackboardDelta.CreateForSchema(schema);

            try
            {
                delta.Attach(source);

                Assert.AreEqual(FPInt64.FromString("1.25").RawValue, source.GetFPInt64(cooldownKey).RawValue);
                Assert.AreEqual(FPInt64.FromInt(1).RawValue, source.GetFPVector2(velocityKey).X.RawValue);

                source.SetFPVector2(velocityKey, new FPVector2(FPInt64.FromInt(7), FPInt64.FromInt(8)));

                Assert.IsTrue(delta.TryFlush(source, out ArraySegment<byte> patch));
                BTBlackboardDelta.Apply(target, patch);

                FPVector2 restored = target.GetFPVector2(velocityKey);
                Assert.AreEqual(FPInt64.FromInt(7).RawValue, restored.X.RawValue);
                Assert.AreEqual(FPInt64.FromInt(8).RawValue, restored.Y.RawValue);
            }
            finally
            {
                delta.Dispose();
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void DeterministicRandomProviderCanSaveAndRestoreState()
        {
            var provider = new DeterministicMathRandomProvider(1234UL);
            float first = provider.Range(0f, 10f);
            DeterministicRandomState state = provider.SaveState();
            float second = provider.Range(0f, 10f);

            provider.RestoreState(state);

            Assert.AreNotEqual(first, second);
            Assert.AreEqual(second, provider.Range(0f, 10f));
            Assert.AreEqual(3, provider.RangeInt(3, 3));
        }
    }
}
