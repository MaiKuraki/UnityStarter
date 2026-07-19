using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayEffectDefinitionImmutabilityTests
    {
        [Test]
        public void Constructor_CapturesImmutableTagAndCollectionSnapshots()
        {
            GameplayTag originalTag = RegisterAndRequestTag("Test.GAS.Definition.Original");
            GameplayTag replacementTag = RegisterAndRequestTag("Test.GAS.Definition.Replacement");
            GameplayTag requiredTag = RegisterAndRequestTag("Test.GAS.Definition.Required");

            var grantedTags = new GameplayTagContainer();
            grantedTags.AddTag(originalTag);
            var requiredTags = new GameplayTagContainer();
            requiredTags.AddTag(requiredTag);
            var sourceModifier = new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(5f));
            var modifierInput = new List<ModifierInfo> { sourceModifier };
            var requirementInput = new List<ICustomApplicationRequirement> { AlwaysAllowRequirement.Instance };
            var overflowDefinition = new GameplayEffect("Overflow", EDurationPolicy.Instant);
            var overflowInput = new List<GameplayEffect> { overflowDefinition };

            var definition = new GameplayEffect(
                "ImmutableDefinition",
                EDurationPolicy.HasDuration,
                duration: 5f,
                modifiers: modifierInput,
                grantedTags: grantedTags,
                applicationTagRequirements: new GameplayTagRequirements(new GameplayTagContainer(), requiredTags),
                customApplicationRequirements: requirementInput,
                overflowEffects: overflowInput);

            grantedTags.Clear();
            grantedTags.AddTag(replacementTag);
            requiredTags.Clear();
            modifierInput.Clear();
            requirementInput.Clear();
            overflowInput.Clear();

            Assert.That(definition.GrantedTags.HasTag(originalTag), Is.True);
            Assert.That(definition.GrantedTags.HasTag(replacementTag), Is.False);
            Assert.That(definition.ApplicationTagRequirements.RequiredTags.HasTag(requiredTag), Is.True);
            Assert.That(definition.Modifiers.Count, Is.EqualTo(1));
            Assert.That(definition.CustomApplicationRequirements.Count, Is.EqualTo(1));
            Assert.That(definition.OverflowEffects.Count, Is.EqualTo(1));
            Assert.That(definition.Modifiers[0], Is.Not.SameAs(sourceModifier));

            Assert.That(definition.GrantedTags, Is.InstanceOf<IReadOnlyGameplayTagContainer>());
            Assert.That(definition.GrantedTags, Is.Not.InstanceOf<IGameplayTagContainer>());
            Assert.That(definition.ApplicationTagRequirements.RequiredTags, Is.Not.InstanceOf<IGameplayTagContainer>());
            Assert.That(definition.Modifiers, Is.Not.InstanceOf<ModifierInfo[]>());
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ModifierInfo>)definition.Modifiers)[0] =
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(10f)));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<GameplayEffect>)definition.OverflowEffects).Clear());

            GameplayTagContainer isolatedCopy = definition.GrantedTags.ToMutableContainer();
            isolatedCopy.AddTag(replacementTag);
            Assert.That(isolatedCopy.HasTag(replacementTag), Is.True);
            Assert.That(definition.GrantedTags.HasTag(replacementTag), Is.False);
        }

        [Test]
        public void ActiveEffectApplyAndRemove_UseTheCapturedDefinitionSnapshot()
        {
            GameplayTag capturedTag = RegisterAndRequestTag("Test.GAS.Definition.Active.Captured");
            GameplayTag replacementTag = RegisterAndRequestTag("Test.GAS.Definition.Active.Replacement");
            var sourceTags = new GameplayTagContainer();
            sourceTags.AddTag(capturedTag);
            var definition = new GameplayEffect(
                "CapturedActiveEffect",
                EDurationPolicy.HasDuration,
                duration: 5f,
                grantedTags: sourceTags);

            sourceTags.Clear();
            sourceTags.AddTag(replacementTag);

            var asc = new AbilitySystemComponent();
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(definition, asc));

            Assert.That(result.Succeeded, Is.True);
            Assert.That(asc.HasMatchingGameplayTag(capturedTag), Is.True);
            Assert.That(asc.HasMatchingGameplayTag(replacementTag), Is.False);

            Assert.That(asc.TryRemoveActiveEffect(result.ActiveEffect), Is.True);
            Assert.That(asc.HasMatchingGameplayTag(capturedTag), Is.False);
            asc.Dispose();
        }

        [Test]
        public void Constructor_RejectsOversizedStringsAndCollections()
        {
            Assert.Throws<ArgumentException>(() =>
                new GameplayEffect(new string('N', GameplayEffect.MaxNameLength + 1), EDurationPolicy.Instant));

            var modifier = new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f));
            var modifiers = Repeat(modifier, GameplayEffect.MaxModifierCount + 1);
            Assert.Throws<ArgumentException>(() =>
                new GameplayEffect("TooManyModifiers", EDurationPolicy.Instant, modifiers: modifiers));

            var overflow = new GameplayEffect("OverflowBoundEntry", EDurationPolicy.Instant);
            var overflowEffects = Repeat(overflow, GameplayEffect.MaxOverflowEffectCount + 1);
            Assert.Throws<ArgumentException>(() =>
                new GameplayEffect("TooManyOverflowEffects", EDurationPolicy.Instant, overflowEffects: overflowEffects));

            var requirements = Repeat<ICustomApplicationRequirement>(
                AlwaysAllowRequirement.Instance,
                GameplayEffect.MaxCustomApplicationRequirementCount + 1);
            Assert.Throws<ArgumentException>(() =>
                new GameplayEffect("TooManyRequirements", EDurationPolicy.Instant, customApplicationRequirements: requirements));
        }

        [Test]
        public void Constructor_RejectsAggregateTagDataAboveTheDefinitionBudget()
        {
            var tags = new GameplayTagContainer();
            int index = 0;
            while (tags.TagCount <= GameplayEffect.MaxAggregateTagCount)
            {
                tags.AddTag(RegisterAndRequestTag($"Test.GAS.Definition.Bounds.Tag{index:D4}"));
                index++;
            }

            Assert.Throws<ArgumentException>(() =>
                new GameplayEffect("TooManyTags", EDurationPolicy.Instant, assetTags: tags));
        }

        private static GameplayTag RegisterAndRequestTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayEffect immutability test tag.");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }

        private static List<T> Repeat<T>(T value, int count)
        {
            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(value);
            }

            return result;
        }

        private sealed class AlwaysAllowRequirement : ICustomApplicationRequirement
        {
            public static AlwaysAllowRequirement Instance { get; } = new AlwaysAllowRequirement();

            public bool CanApplyGameplayEffect(GameplayEffectSpec spec, AbilitySystemComponent target) => true;
        }
    }
}
