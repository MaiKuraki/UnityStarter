using NUnit.Framework;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASDeterministicCoreTests
    {
        [Test]
        public void AttributeValues_StoreRawFixedValues()
        {
            var attribute = new GASAttributeValueData(new GASAttributeId(1), 10.5f, 7.25f, 1u);

            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(10.5f).RawValue));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromFloat(7.25f).RawValue));
        }

        [Test]
        public void InstantModifier_UsesDeterministicRawMath()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);

            state.SetAttributeBase(health, 100f);
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Add, -12.5f));

            Assert.That(state.TryGetAttribute(health, out var attribute), Is.True);
            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
        }

        [Test]
        public void RawAttributeApi_AvoidsFloatRoundTrip()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            long baseRaw = GASFixedValue.FromFloat(100f).RawValue;
            long deltaRaw = GASFixedValue.FromFloat(-12.5f).RawValue;

            state.SetAttributeBaseRaw(health, baseRaw);
            state.ApplyInstantModifierRaw(health, GASModifierOp.Add, deltaRaw);

            Assert.That(state.TryGetAttribute(health, out var attribute), Is.True);
            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(attribute.BaseValueRaw));
        }

        [Test]
        public void SnapshotTypes_StoreRawFixedValues()
        {
            long baseRaw = GASFixedValue.FromFloat(10f).RawValue;
            long currentRaw = GASFixedValue.FromFloat(8.5f).RawValue;
            long durationRaw = GASFixedValue.FromFloat(3.25f).RawValue;

            var attribute = GASAttributeStateData.FromRaw("Health", baseRaw, currentRaw);
            var effect = GASActiveEffectStateData.FromRaw(
                1,
                null,
                null,
                1,
                2,
                durationRaw,
                durationRaw,
                0L,
                default,
                null,
                0);

            Assert.That(attribute.BaseValueRaw, Is.EqualTo(baseRaw));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(currentRaw));
            Assert.That(effect.DurationRaw, Is.EqualTo(durationRaw));
            Assert.That(effect.TimeRemainingRaw, Is.EqualTo(durationRaw));
        }

        [Test]
        public void PredictionReject_RestoresRawBaseValue()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            var prediction = new GASPredictionKey(1, new GASEntityId(99), 7);

            state.SetAttributeBase(health, 100f);
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Add, -20f), prediction);
            state.RejectPrediction(prediction);

            Assert.That(state.TryGetAttribute(health, out var attribute), Is.True);
            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(100f).RawValue));
        }

        [Test]
        public void Checksum_IsStableForSameRawState()
        {
            var a = BuildState();
            var b = BuildState();

            Assert.That(a.ComputeChecksum().Combined, Is.EqualTo(b.ComputeChecksum().Combined));
        }

        private static GASAbilitySystemState BuildState()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            state.SetAttributeBase(health, 100f);
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Division, 4f));
            return state;
        }
    }
}
