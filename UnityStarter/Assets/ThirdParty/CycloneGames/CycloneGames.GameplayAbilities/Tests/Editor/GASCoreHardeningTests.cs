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
                3);

            Assert.That(multiplyState.TryGetAttribute(attributeId, out var multiplied), Is.True);
            Assert.That(multiplied.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(80).RawValue));

            var divisionState = new GASAbilitySystemState(new GASEntityId(2));
            divisionState.SetAttributeBase(attributeId, GASFixedValue.FromInt(80));
            AddInfiniteEffect(
                divisionState,
                2,
                new[] { new GASModifierData(attributeId, GASModifierOp.Division, GASFixedValue.FromInt(2)) },
                3);

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

        private static GASActiveEffectHandle AddInfiniteEffect(
            GASAbilitySystemState state,
            int definitionId,
            GASModifierData[] modifiers,
            ushort stackCount)
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
                modifiers?.Length ?? 0);
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
