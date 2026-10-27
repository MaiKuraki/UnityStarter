using System;
using System.Reflection;

using CycloneGames.GameplayAbilities.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASCoreHardeningTests
    {
        [Test]
        public void Limits_RejectOverflowWithoutGrowingOrMutatingState()
        {
            var limits = new GASAbilitySystemLimits(1, 1, 1, 1, 1);
            var state = new GASAbilitySystemState(new GASEntityId(1), limits);
            int abilityCapacityBefore = GetArrayLength(state, "abilitySpecs");

            Assert.That(state.Reserve(2, 1, 1, 1, 1), Is.False);
            Assert.That(GetArrayLength(state, "abilitySpecs"), Is.EqualTo(abilityCapacityBefore));

            var firstAbility = state.GrantAbility(
                new GASDefinitionId(1),
                1,
                GASInstancingPolicy.InstancedPerActor);
            var rejectedAbility = state.GrantAbility(
                new GASDefinitionId(2),
                1,
                GASInstancingPolicy.InstancedPerActor);

            Assert.That(firstAbility.IsValid, Is.True);
            Assert.That(rejectedAbility.IsValid, Is.False);
            Assert.That(state.AbilitySpecCount, Is.EqualTo(1));
            Assert.That(state.SetAttributeBaseRaw(new GASAttributeId(1), 10L), Is.True);
            Assert.That(state.SetAttributeBaseRaw(new GASAttributeId(2), 20L), Is.False);
            Assert.That(state.AttributeCount, Is.EqualTo(1));
        }

        [Test]
        public void ModifierLimit_RejectsWholeEffectAndReleasesBudgetOnRemoval()
        {
            var limits = new GASAbilitySystemLimits(1, 1, 2, 1, 1);
            var state = new GASAbilitySystemState(new GASEntityId(1), limits);
            var attributeId = new GASAttributeId(1);
            var modifiers = new[]
            {
                new GASModifierData(attributeId, GASModifierOp.Add, GASFixedValue.FromInt(1))
            };

            Assert.That(state.SetAttributeBase(attributeId, GASFixedValue.FromInt(10)), Is.True);
            var first = AddInfiniteEffect(state, 1, modifiers, 1);
            var rejected = AddInfiniteEffect(state, 2, modifiers, 1);

            Assert.That(first.IsValid, Is.True);
            Assert.That(rejected.IsValid, Is.False);
            Assert.That(state.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(state.ModifierCount, Is.EqualTo(1));

            Assert.That(state.RemoveActiveEffect(first), Is.True);
            Assert.That(AddInfiniteEffect(state, 2, modifiers, 1).IsValid, Is.True);
        }

        [Test]
        public void PredictionLimit_RejectsMutationBeforeAttributeChanges()
        {
            var limits = new GASAbilitySystemLimits(1, 2, 1, 1, 1);
            var state = new GASAbilitySystemState(new GASEntityId(1), limits);
            var health = new GASAttributeId(1);
            var mana = new GASAttributeId(2);
            var firstPrediction = new GASPredictionKey(1, new GASEntityId(1), 1);
            var secondPrediction = new GASPredictionKey(2, new GASEntityId(1), 2);

            state.SetAttributeBase(health, GASFixedValue.FromInt(100));
            state.SetAttributeBase(mana, GASFixedValue.FromInt(50));

            Assert.That(state.ApplyInstantModifier(
                new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromInt(-10)),
                firstPrediction), Is.True);
            Assert.That(state.ApplyInstantModifier(
                new GASModifierData(mana, GASModifierOp.Add, GASFixedValue.FromInt(-10)),
                secondPrediction), Is.False);
            Assert.That(state.TryGetAttribute(mana, out var manaValue), Is.True);
            Assert.That(manaValue.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(50).RawValue));
            Assert.That(state.PredictedAttributeChangeCount, Is.EqualTo(1));
        }

        [Test]
        public void StackedMultiplyAndDivision_ComposeOncePerStack()
        {
            var multiplyState = new GASAbilitySystemState(new GASEntityId(1));
            var attributeId = new GASAttributeId(1);
            multiplyState.SetAttributeBase(attributeId, GASFixedValue.FromInt(10));
            AddInfiniteEffect(
                multiplyState,
                1,
                new[] { new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(2)) },
                3,
                GASStackMagnitudePolicy.Exponential);

            Assert.That(multiplyState.TryGetAttribute(attributeId, out var multiplied), Is.True);
            Assert.That(multiplied.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(80).RawValue));

            var divisionState = new GASAbilitySystemState(new GASEntityId(2));
            divisionState.SetAttributeBase(attributeId, GASFixedValue.FromInt(80));
            AddInfiniteEffect(
                divisionState,
                2,
                new[] { new GASModifierData(attributeId, GASModifierOp.Division, GASFixedValue.FromInt(2)) },
                3,
                GASStackMagnitudePolicy.Exponential);

            Assert.That(divisionState.TryGetAttribute(attributeId, out var divided), Is.True);
            Assert.That(divided.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(10).RawValue));
        }

        [Test]
        public void InvalidIdentifiersAndEffectSlices_FailWithoutMutation()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var validAttribute = new GASAttributeId(1);
            var validModifier = new GASModifierData(validAttribute, GASModifierOp.Add, GASFixedValue.One);
            var invalidModifier = new GASModifierData(validAttribute, (GASModifierOp)byte.MaxValue, GASFixedValue.One);

            Assert.That(state.SetAttributeBaseRaw(default, 1L), Is.False);
            Assert.That(state.SetAttributeBaseRaw(new GASAttributeId(-1), 1L), Is.False);
            Assert.That(state.GrantAbility(
                default,
                1,
                GASInstancingPolicy.InstancedPerActor).IsValid, Is.False);
            Assert.That(state.AddActiveEffect(
                new GASDefinitionId(1),
                default,
                default,
                GASEffectDurationPolicy.Infinite,
                1,
                0,
                0,
                0,
                new[] { validModifier },
                0,
                1).IsValid, Is.False);
            Assert.That(state.AddActiveEffect(
                new GASDefinitionId(1),
                default,
                default,
                GASEffectDurationPolicy.Infinite,
                1,
                1,
                0,
                0,
                new[] { validModifier },
                int.MaxValue,
                1).IsValid, Is.False);
            Assert.That(state.AddActiveEffect(
                new GASDefinitionId(1),
                default,
                default,
                GASEffectDurationPolicy.Infinite,
                1,
                1,
                0,
                0,
                new[] { invalidModifier },
                0,
                1).IsValid, Is.False);

            var invalidInstant = new GASGameplayEffectSpecData(
                new GASDefinitionId(-1),
                default,
                default,
                GASEffectDurationPolicy.Instant,
                1,
                1,
                0,
                0,
                new[] { validModifier },
                0,
                1);
            Assert.That(state.TryApplyGameplayEffectSpecToSelf(in invalidInstant, out _), Is.False);
            Assert.That(state.AttributeCount, Is.EqualTo(0));
            Assert.That(state.ActiveEffectCount, Is.EqualTo(0));
            Assert.That(state.ModifierCount, Is.EqualTo(0));
        }

        [Test]
        public void HandleAllocation_WrapsAndSkipsLiveCollisions()
        {
            var limits = new GASAbilitySystemLimits(4, 1, 4, 1, 1);
            var state = new GASAbilitySystemState(new GASEntityId(1), limits);
            var first = state.GrantAbility(
                new GASDefinitionId(1), 1, GASInstancingPolicy.NonInstanced);

            SetPrivateInt(state, "nextSpecHandle", int.MaxValue);
            var maximum = state.GrantAbility(
                new GASDefinitionId(2), 1, GASInstancingPolicy.NonInstanced);
            var wrapped = state.GrantAbility(
                new GASDefinitionId(3), 1, GASInstancingPolicy.NonInstanced);

            Assert.That(first.Value, Is.EqualTo(1));
            Assert.That(maximum.Value, Is.EqualTo(int.MaxValue));
            Assert.That(wrapped.Value, Is.EqualTo(2));

            var firstEffect = AddInfiniteEffect(state, 10, null, 1);
            SetPrivateInt(state, "nextEffectHandle", int.MaxValue);
            var maximumEffect = AddInfiniteEffect(state, 11, null, 1);
            var wrappedEffect = AddInfiniteEffect(state, 12, null, 1);

            Assert.That(firstEffect.Value, Is.EqualTo(1));
            Assert.That(maximumEffect.Value, Is.EqualTo(int.MaxValue));
            Assert.That(wrappedEffect.Value, Is.EqualTo(2));

            state.Reset(new GASEntityId(2));
            var afterReset = state.GrantAbility(
                new GASDefinitionId(4), 1, GASInstancingPolicy.NonInstanced);
            Assert.That(afterReset.Value, Is.EqualTo(3));
        }

        [Test]
        public void DefaultRegistries_AreIsolatedExplicitInstances()
        {
            var firstDefinitionRegistry = new GASDefaultDefinitionRegistry();
            var secondDefinitionRegistry = new GASDefaultDefinitionRegistry();
            var firstDefinition = new object();
            var secondDefinition = new object();

            var firstDefinitionId = firstDefinitionRegistry.RegisterAbilityDefinition(firstDefinition, "Ability.First");
            var secondDefinitionId = secondDefinitionRegistry.RegisterAbilityDefinition(secondDefinition, "Ability.Second");

            Assert.That(firstDefinitionId.Value, Is.EqualTo(1));
            Assert.That(secondDefinitionId.Value, Is.EqualTo(1));
            Assert.That(firstDefinitionRegistry.TryGetAbilityDefinitionId(secondDefinition, out _), Is.False);

            var firstAttributeRegistry = new GASDefaultAttributeRegistry();
            var secondAttributeRegistry = new GASDefaultAttributeRegistry();
            Assert.That(firstAttributeRegistry.RegisterAttribute("Health").Value, Is.EqualTo(1));
            Assert.That(secondAttributeRegistry.RegisterAttribute("Mana").Value, Is.EqualTo(1));
            Assert.That(firstAttributeRegistry.TryGetAttributeId("Mana", out _), Is.False);
        }

        [Test]
        public void ChannelComposition_FoldsInAscendingChannelOrder()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var attributeId = new GASAttributeId(1);
            state.SetAttributeBase(attributeId, GASFixedValue.FromInt(100));

            var modifiers = new[]
            {
                new GASModifierData(attributeId, GASModifierOp.Add, GASFixedValue.FromInt(10),
                    GASModifierEvaluationChannel.Channel0),
                new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(2),
                    GASModifierEvaluationChannel.Channel1),
                new GASModifierData(attributeId, GASModifierOp.Override, GASFixedValue.FromInt(7),
                    GASModifierEvaluationChannel.Channel2),
                new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(3),
                    GASModifierEvaluationChannel.Channel3)
            };
            AddInfiniteEffect(state, 1, modifiers, 1);

            // 100 --Add 10--> 110 --Multiply 2--> 220 --Override 7--> 7 --Multiply 3--> 21
            state.TryGetAttributeByIndex(0, out GASAttributeValueData attribute);
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(21).RawValue));
        }

        [Test]
        public void ChannelComposition_LastOverrideInChannelWins()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var attributeId = new GASAttributeId(1);
            state.SetAttributeBase(attributeId, GASFixedValue.FromInt(100));

            var modifiers = new[]
            {
                new GASModifierData(attributeId, GASModifierOp.Override, GASFixedValue.FromInt(5),
                    GASModifierEvaluationChannel.Channel1),
                new GASModifierData(attributeId, GASModifierOp.Override, GASFixedValue.FromInt(9),
                    GASModifierEvaluationChannel.Channel1)
            };
            AddInfiniteEffect(state, 1, modifiers, 1);

            state.TryGetAttributeByIndex(0, out GASAttributeValueData attribute);
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(9).RawValue));
        }

        [Test]
        public void Accumulator_UninitializedUseFailsInEditor()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                var accumulator = default(GASModifierAggregation.Accumulator);
                GASModifierAggregation.Accumulate(
                    ref accumulator,
                    GASModifierOp.Add,
                    GASFixedValue.FromInt(1).RawValue,
                    1,
                    GASStackMagnitudePolicy.LinearBias,
                    unrealBiasSum: false);
            });
        }

        [Test]
        public void Accumulator_CreateProducesUsableState()
        {
            var accumulator = GASModifierAggregation.Accumulator.Create();
            GASModifierAggregation.Accumulate(
                ref accumulator,
                GASModifierOp.Multiply,
                GASFixedValue.FromInt(2).RawValue,
                1,
                GASStackMagnitudePolicy.LinearBias,
                unrealBiasSum: false);
            GASModifierAggregation.Accumulate(
                ref accumulator,
                GASModifierOp.Add,
                GASFixedValue.FromInt(5).RawValue,
                1,
                GASStackMagnitudePolicy.LinearBias,
                unrealBiasSum: false);

            long folded = GASModifierAggregation.Fold(
                in accumulator,
                GASFixedValue.FromInt(10).RawValue,
                unrealBiasSum: false);

            // (10 + 5) * 2
            Assert.That(folded, Is.EqualTo(GASFixedValue.FromInt(30).RawValue));
        }

        [Test]
        public void DefaultLimits_AreStable()
        {
            // The defaults are part of the contract: every consumer that does not pass explicit
            // limits inherits them, and MaxAttributes also bounds dependency propagation depth.
            GASAbilitySystemLimits limits = GASAbilitySystemLimits.Default;
            Assert.That(limits.MaxAbilities, Is.EqualTo(256));
            Assert.That(limits.MaxAttributes, Is.EqualTo(512));
            Assert.That(limits.MaxActiveEffects, Is.EqualTo(1024));
            Assert.That(limits.MaxModifiers, Is.EqualTo(16384));
            Assert.That(limits.MaxPredictedAttributeChanges, Is.EqualTo(4096));
        }

        [Test]
        public void RemoveActiveEffect_RecomputesOnlyTouchedAttributes()
        {
            const int attributeCount = 32;
            var state = CreateAttributeState(attributeCount, out GASAttributeId[] attributeIds);

            var residentModifiers = new[]
            {
                new GASModifierData(attributeIds[0], GASModifierOp.Multiply, GASFixedValue.FromFloat(1.1f)),
                new GASModifierData(attributeIds[1], GASModifierOp.Multiply, GASFixedValue.FromFloat(1.1f))
            };
            for (int e = 0; e < 8; e++)
            {
                AddInfiniteEffect(state, e + 1, residentModifiers, 1);
            }

            var singleModifiers = new[]
            {
                new GASModifierData(attributeIds[0], GASModifierOp.Multiply, GASFixedValue.FromFloat(1.5f))
            };
            GASActiveEffectHandle handle = AddInfiniteEffect(state, 100, singleModifiers, 1);
            var versions = SnapshotAggregatorVersions(state, attributeCount);

            Assert.That(state.RemoveActiveEffect(handle), Is.True);

            Assert.That(
                SumAggregatorVersionDelta(state, attributeCount, versions),
                Is.EqualTo(1L),
                "Removal must recompute only the attributes the removed effect targeted.");

            var reference = CreateAttributeState(attributeCount, out _);
            for (int e = 0; e < 8; e++)
            {
                AddInfiniteEffect(reference, e + 1, residentModifiers, 1);
            }

            AssertAttributeValuesEqual(state, reference, attributeCount);
        }

        [Test]
        public void RemoveExpiredEffects_RecomputesEachTouchedAttributeOnce()
        {
            const int attributeCount = 32;
            const int expiredCount = 64;

            var batch = CreateAttributeState(attributeCount, out GASAttributeId[] attributeIds);
            var reference = CreateAttributeState(attributeCount, out _);

            var expiredModifiers = new[]
            {
                new GASModifierData(attributeIds[0], GASModifierOp.Multiply, GASFixedValue.FromFloat(1.25f)),
                new GASModifierData(attributeIds[1], GASModifierOp.Add, GASFixedValue.FromInt(3))
            };
            var survivorModifiers = new[]
            {
                new GASModifierData(attributeIds[1], GASModifierOp.Multiply, GASFixedValue.FromFloat(1.05f))
            };

            // Interleave a survivor so the batch has to compact around effects it keeps.
            for (int e = 0; e < expiredCount; e++)
            {
                if (e == 8)
                {
                    AddInfiniteEffect(batch, 1000, survivorModifiers, 1);
                    AddInfiniteEffect(reference, 1000, survivorModifiers, 1);
                }

                AddDurationEffect(batch, e + 1, expiredModifiers, startTick: 0, durationTicks: 1);
            }

            AddInfiniteEffect(batch, 1001, survivorModifiers, 1);
            AddInfiniteEffect(reference, 1001, survivorModifiers, 1);

            var versions = SnapshotAggregatorVersions(batch, attributeCount);

            Assert.That(batch.RemoveExpiredEffects(1L), Is.EqualTo(expiredCount));

            Assert.That(
                SumAggregatorVersionDelta(batch, attributeCount, versions),
                Is.EqualTo(2L),
                "Batch expiry must recompute each touched attribute exactly once for the whole batch.");

            AssertAttributeValuesEqual(batch, reference, attributeCount);

            Assert.That(batch.ActiveEffectCount, Is.EqualTo(2));
            Assert.That(batch.TryGetActiveEffectByIndex(0, out GASActiveEffectData first), Is.True);
            Assert.That(batch.TryGetActiveEffectByIndex(1, out GASActiveEffectData second), Is.True);
            Assert.That(first.EffectDefinitionId, Is.EqualTo(new GASDefinitionId(1000)));
            Assert.That(second.EffectDefinitionId, Is.EqualTo(new GASDefinitionId(1001)));
        }

        private static GASAbilitySystemState CreateAttributeState(
            int attributeCount,
            out GASAttributeId[] attributeIds)
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            attributeIds = new GASAttributeId[attributeCount];
            for (int i = 0; i < attributeCount; i++)
            {
                attributeIds[i] = new GASAttributeId(i + 1);
                state.SetAttributeBase(attributeIds[i], GASFixedValue.FromInt(100));
            }

            return state;
        }

        private static GASActiveEffectHandle AddDurationEffect(
            GASAbilitySystemState state,
            int definitionId,
            GASModifierData[] modifiers,
            long startTick,
            int durationTicks)
        {
            return state.AddActiveEffect(
                new GASDefinitionId(definitionId),
                default,
                default,
                GASEffectDurationPolicy.Duration,
                1,
                1,
                startTick,
                durationTicks,
                modifiers,
                0,
                modifiers.Length);
        }

        private static uint[] SnapshotAggregatorVersions(GASAbilitySystemState state, int attributeCount)
        {
            var versions = new uint[attributeCount];
            for (int i = 0; i < attributeCount; i++)
            {
                state.TryGetAttributeByIndex(i, out GASAttributeValueData attribute);
                versions[i] = attribute.AggregatorVersion;
            }

            return versions;
        }

        private static long SumAggregatorVersionDelta(
            GASAbilitySystemState state,
            int attributeCount,
            uint[] versions)
        {
            long total = 0L;
            for (int i = 0; i < attributeCount; i++)
            {
                state.TryGetAttributeByIndex(i, out GASAttributeValueData attribute);
                total += attribute.AggregatorVersion - versions[i];
            }

            return total;
        }

        private static void AssertAttributeValuesEqual(
            GASAbilitySystemState actual,
            GASAbilitySystemState expected,
            int attributeCount)
        {
            for (int i = 0; i < attributeCount; i++)
            {
                actual.TryGetAttributeByIndex(i, out GASAttributeValueData actualAttribute);
                expected.TryGetAttributeByIndex(i, out GASAttributeValueData expectedAttribute);
                Assert.That(
                    actualAttribute.CurrentValueRaw,
                    Is.EqualTo(expectedAttribute.CurrentValueRaw),
                    $"Attribute {i} diverged from the state built without the removed effects.");
            }
        }

        private static GASActiveEffectHandle AddInfiniteEffect(
            GASAbilitySystemState state,
            int definitionId,
            GASModifierData[] modifiers,
            ushort stackCount,
            GASStackMagnitudePolicy stackMagnitudePolicy = GASStackMagnitudePolicy.LinearBias)
        {
            return state.AddActiveEffect(
                new GASDefinitionId(definitionId),
                default,
                default,
                GASEffectDurationPolicy.Infinite,
                1,
                stackCount,
                0,
                0,
                modifiers,
                0,
                modifiers?.Length ?? 0,
                stackMagnitudePolicy);
        }

        private static int GetArrayLength(GASAbilitySystemState state, string fieldName)
        {
            var field = typeof(GASAbilitySystemState).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return ((Array)field.GetValue(state)).Length;
        }

        private static void SetPrivateInt(GASAbilitySystemState state, string fieldName, int value)
        {
            var field = typeof(GASAbilitySystemState).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(state, value);
        }
    }
}
