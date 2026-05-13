using NUnit.Framework;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASDeterministicCoreTests
    {
        [Test]
        public void AttributeValues_StoreRawFixedValues()
        {
            var attribute = new GASAttributeValueData(
                new GASAttributeId(1),
                GASFixedValue.FromFloat(10.5f),
                GASFixedValue.FromFloat(7.25f),
                1u);

            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(10.5f).RawValue));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromFloat(7.25f).RawValue));
        }

        [Test]
        public void FixedValue_OperatorsPreserveRawDeterminism()
        {
            var a = GASFixedValue.FromFloat(10.5f);
            var b = GASFixedValue.FromFloat(2f);

            Assert.That((a + b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(12.5f).RawValue));
            Assert.That((a - b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(8.5f).RawValue));
            Assert.That((a * b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(21f).RawValue));
            Assert.That((a / b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(5.25f).RawValue));
            Assert.That(a > b, Is.True);
        }

        [Test]
        public void InstantModifier_UsesDeterministicRawMath()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);

            state.SetAttributeBase(health, GASFixedValue.FromInt(100));
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromFloat(-12.5f)));

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
        public void Facade_FixedValueApi_UsesRawCorePath()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var facade = new GASAbilitySystemFacade(state);
            var health = new GASAttributeId(100);

            facade.SetNumericAttributeBase(health, GASFixedValue.FromInt(100));
            facade.ApplyInstantModifier(health, GASModifierOp.Add, GASFixedValue.FromFloat(-12.5f));

            Assert.That(facade.GetGameplayAttributeFixedValues(health, out var baseValue, out var currentValue), Is.True);
            Assert.That(baseValue.RawValue, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
            Assert.That(currentValue.RawValue, Is.EqualTo(baseValue.RawValue));
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
        public void EffectReplicationData_StoresRawTimeAndSetByCallerValues()
        {
            long durationRaw = GASFixedValue.FromFloat(12.5f).RawValue;
            long remainingRaw = GASFixedValue.FromFloat(4.25f).RawValue;
            long setByCallerRaw = GASFixedValue.FromFloat(1.75f).RawValue;
            var setByCallerValuesRaw = new[] { setByCallerRaw };

            var data = new GASEffectReplicationData
            {
                NetworkId = 1,
                EffectDefId = 2,
                SourceAscNetId = 3,
                TargetAscNetId = 4,
                Level = 5,
                StackCount = 2,
                DurationRaw = durationRaw,
                TimeRemainingRaw = remainingRaw,
                PeriodTimeRemainingRaw = 0L,
                SetByCallerValuesRaw = setByCallerValuesRaw,
                SetByCallerCount = setByCallerValuesRaw.Length
            };

            Assert.That(data.DurationRaw, Is.EqualTo(durationRaw));
            Assert.That(data.TimeRemainingRaw, Is.EqualTo(remainingRaw));
            Assert.That(data.Duration.RawValue, Is.EqualTo(durationRaw));
            Assert.That(data.TimeRemaining.RawValue, Is.EqualTo(remainingRaw));
            Assert.That(data.SetByCallerValuesRaw[0], Is.EqualTo(setByCallerRaw));
        }

        [Test]
        public void GameplayCueEventParams_StoresRawDuration()
        {
            long durationRaw = GASFixedValue.FromFloat(2.5f).RawValue;

            var parameters = new GameplayCueEventParams(
                null,
                null,
                null,
                null,
                null,
                null,
                1,
                durationRaw);

            Assert.That(parameters.EffectDurationRaw, Is.EqualTo(durationRaw));
            Assert.That(parameters.EffectDuration.RawValue, Is.EqualTo(durationRaw));
        }

        [Test]
        public void DeterministicTimeProvider_AdvancesWithRawTicks()
        {
            var time = new DeterministicTimeProvider();
            long deltaRaw = GASFixedValue.FromFloat(0.125f).RawValue;

            time.TickRaw(deltaRaw);
            time.Tick(GASFixedValue.FromFloat(0.25f));

            Assert.That(time.DeltaTimeRaw, Is.EqualTo(GASFixedValue.FromFloat(0.25f).RawValue));
            Assert.That(time.TotalTimeRaw, Is.EqualTo(deltaRaw + GASFixedValue.FromFloat(0.25f).RawValue));
            Assert.That(time.FrameCount, Is.EqualTo(2));
        }

        [Test]
        public void DeterministicRandomProvider_UsesStableFixedSequence()
        {
            var a = new DeterministicRandomProvider(12345);
            var b = new DeterministicRandomProvider(12345);

            Assert.That(a.NextRaw(), Is.EqualTo(b.NextRaw()));
            Assert.That(a.NextFixed().RawValue, Is.EqualTo(b.NextFixed().RawValue));
            Assert.That(a.NextInt(1, 100), Is.EqualTo(b.NextInt(1, 100)));
        }

        [Test]
        public void PredictionReject_RestoresRawBaseValue()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            var prediction = new GASPredictionKey(1, new GASEntityId(99), 7);

            state.SetAttributeBase(health, GASFixedValue.FromInt(100));
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromInt(-20)), prediction);
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
            state.SetAttributeBase(health, GASFixedValue.FromInt(100));
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Division, GASFixedValue.FromInt(4)));
            return state;
        }
    }
}
