using System;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASNetworkStateChunkPlannerTests
    {
        [Test]
        public void Planner_CoversEveryRecordWithinPayloadAndRecordBudgets()
        {
            const int maxPayload = 256;
            var planner = new GASNetworkStateChunkPlanner(
                maxPayload,
                abilityCount: 17,
                attributeCount: 19,
                effectCount: 11,
                effectTagCount: 23,
                effectMagnitudeCount: 13,
                looseTagCount: 29);

            int abilities = 0;
            int attributes = 0;
            int effects = 0;
            int effectTags = 0;
            int effectMagnitudes = 0;
            int looseTags = 0;
            int chunks = 0;
            while (planner.TryGetNext(out GASNetworkStateChunkPlan plan))
            {
                Assert.That(plan.ChunkIndex, Is.EqualTo(chunks));
                Assert.That(plan.ChunkCount, Is.EqualTo(planner.ChunkCount));
                Assert.That(plan.PayloadBytes, Is.LessThanOrEqualTo(maxPayload));
                Assert.That(plan.TotalRecordCount, Is.LessThanOrEqualTo(GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk));
                abilities += plan.AbilityCount;
                attributes += plan.AttributeCount;
                effects += plan.EffectCount;
                effectTags += plan.EffectTagCount;
                effectMagnitudes += plan.EffectMagnitudeCount;
                looseTags += plan.LooseTagCount;
                chunks++;
            }

            Assert.That(chunks, Is.EqualTo(planner.ChunkCount));
            Assert.That(abilities, Is.EqualTo(17));
            Assert.That(attributes, Is.EqualTo(19));
            Assert.That(effects, Is.EqualTo(11));
            Assert.That(effectTags, Is.EqualTo(23));
            Assert.That(effectMagnitudes, Is.EqualTo(13));
            Assert.That(looseTags, Is.EqualTo(29));
        }

        [Test]
        public void EmptySnapshot_ProducesOneHeaderOnlyChunkAndCanReset()
        {
            var planner = new GASNetworkStateChunkPlanner(
                GASNetworkWireCodec.StateBatchHeaderBytes,
                0,
                0,
                0,
                0,
                0,
                0);

            Assert.That(planner.ChunkCount, Is.EqualTo(1));
            Assert.That(planner.TryGetNext(out GASNetworkStateChunkPlan plan), Is.True);
            Assert.That(plan.PayloadBytes, Is.EqualTo(GASNetworkWireCodec.StateBatchHeaderBytes));
            Assert.That(plan.TotalRecordCount, Is.Zero);
            Assert.That(planner.TryGetNext(out _), Is.False);

            planner.Reset();
            Assert.That(planner.TryGetNext(out _), Is.True);
        }

        [Test]
        public void Planner_RejectsBudgetThatCannotFitTheLargestPresentRecord()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GASNetworkStateChunkPlanner(
                GASNetworkWireCodec.StateBatchHeaderBytes + GASNetworkWireCodec.EffectStateRecordBytes - 1,
                0,
                0,
                1,
                0,
                0,
                0));
        }
    }
}
