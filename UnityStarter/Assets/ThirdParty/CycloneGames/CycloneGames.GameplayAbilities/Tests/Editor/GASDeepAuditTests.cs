using System.Reflection;
using System.Runtime.CompilerServices;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>Focused regression coverage for the authorized GAS audit fixes.</summary>
    public sealed class GASDeepAuditTests
    {
        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.Reset();
            GameplayTagRedirector.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.Reset();
            GameplayTagRedirector.ClearAll();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RemoveActiveEffectsWithGrantedTags_IncludesDynamicOnlyTags(bool useGrantedRatherThanAsset)
        {
            GameplayTag dynamicTag = RegisterTag("DynamicOnly");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var definition = new GameplayEffect("DeepAudit.GAS.DynamicEffect", EDurationPolicy.Infinite);
                GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);
                if (useGrantedRatherThanAsset)
                    spec.DynamicGrantedTags.AddTag(dynamicTag);
                else
                    spec.DynamicAssetTags.AddTag(dynamicTag);

                asc.ApplyGameplayEffectSpecToSelf(spec);
                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
                asc.RemoveActiveEffectsWithGrantedTags(Container(dynamicTag));
                TestContext.WriteLine(
                    $"[DeepAudit][DynamicDispel] kind={(useGrantedRatherThanAsset ? "Granted" : "Asset")}, " +
                    $"remaining={asc.ActiveEffects.Count}");
                Assert.That(asc.ActiveEffects.Count, Is.Zero,
                    "A dynamic-only granted/asset tag must participate in dispel just like a definition tag.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RemoveActiveEffectsWithGrantedTags_IncludesDefinitionOnlyTags(bool useGrantedRatherThanAsset)
        {
            GameplayTag definitionTag = RegisterTag("DefinitionOnly");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayTagContainer tags = Container(definitionTag);
                var definition = new GameplayEffect(
                    "DeepAudit.GAS.DefinitionEffect",
                    EDurationPolicy.Infinite,
                    assetTags: useGrantedRatherThanAsset ? null : tags,
                    grantedTags: useGrantedRatherThanAsset ? tags : null);
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(definition, asc));

                GameplayDefinitionTagSet removalSet = useGrantedRatherThanAsset
                    ? definition.GrantedTags
                    : definition.AssetTags;
                asc.RemoveActiveEffectsWithGrantedTags(removalSet);

                Assert.That(asc.ActiveEffects.Count, Is.Zero);
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void RemoveActiveEffectsWithGrantedTags_MatchesDynamicTagWhenDefinitionTagsAlsoExist()
        {
            GameplayTag definitionTag = RegisterTag("Combined.Definition");
            GameplayTag dynamicTag = RegisterTag("Combined.Dynamic");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var definition = new GameplayEffect(
                    "DeepAudit.GAS.CombinedEffect",
                    EDurationPolicy.Infinite,
                    grantedTags: Container(definitionTag));
                GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);
                spec.DynamicAssetTags.AddTag(dynamicTag);
                asc.ApplyGameplayEffectSpecToSelf(spec);

                asc.RemoveActiveEffectsWithGrantedTags(Container(dynamicTag));

                Assert.That(asc.ActiveEffects.Count, Is.Zero);
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void RemoveGameplayEffectsWithTags_DoesNotRemoveTheNewlyAppliedEffect()
        {
            GameplayTag removalTag = RegisterTag("Excluded");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayTagContainer tags = Container(removalTag);
                var oldEffect = new GameplayEffect(
                    "DeepAudit.GAS.OldEffect",
                    EDurationPolicy.Infinite,
                    grantedTags: tags);
                var newEffect = new GameplayEffect(
                    "DeepAudit.GAS.NewEffect",
                    EDurationPolicy.Infinite,
                    grantedTags: tags,
                    removeGameplayEffectsWithTags: tags);
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(oldEffect, asc));

                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(newEffect, asc));

                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
                Assert.That(asc.ActiveEffects[0].Spec.Def, Is.SameAs(newEffect));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void RemoveActiveEffectsWithGrantedTags_LeavesNonMatchingEffect()
        {
            GameplayTag matchingTag = RegisterTag("NonMatching.Match");
            GameplayTag otherTag = RegisterTag("NonMatching.Other");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var matching = new GameplayEffect(
                    "DeepAudit.GAS.Matching",
                    EDurationPolicy.Infinite,
                    assetTags: Container(matchingTag));
                var other = new GameplayEffect(
                    "DeepAudit.GAS.Other",
                    EDurationPolicy.Infinite,
                    assetTags: Container(otherTag));
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(matching, asc));
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(other, asc));

                asc.RemoveActiveEffectsWithGrantedTags(Container(matchingTag));

                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
                Assert.That(asc.ActiveEffects[0].Spec.Def, Is.SameAs(other));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void RemoveActiveEffectsWithGrantedTags_RemovesEveryMatchAcrossMultipleEffects()
        {
            GameplayTag sharedTag = RegisterTag("Multiple.Shared");
            GameplayTag otherTag = RegisterTag("Multiple.Other");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var definitionEffect = new GameplayEffect(
                    "DeepAudit.GAS.Multiple.Definition",
                    EDurationPolicy.Infinite,
                    grantedTags: Container(sharedTag));
                var dynamicGrantedEffect = new GameplayEffect("DeepAudit.GAS.Multiple.DynamicGranted", EDurationPolicy.Infinite);
                var dynamicAssetEffect = new GameplayEffect("DeepAudit.GAS.Multiple.DynamicAsset", EDurationPolicy.Infinite);
                var combinedEffect = new GameplayEffect(
                    "DeepAudit.GAS.Multiple.Combined",
                    EDurationPolicy.Infinite,
                    assetTags: Container(sharedTag));
                var otherEffect = new GameplayEffect(
                    "DeepAudit.GAS.Multiple.Other",
                    EDurationPolicy.Infinite,
                    assetTags: Container(otherTag));

                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(definitionEffect, asc));
                GameplayEffectSpec dynamicGrantedSpec = GameplayEffectSpec.Create(dynamicGrantedEffect, asc);
                dynamicGrantedSpec.DynamicGrantedTags.AddTag(sharedTag);
                asc.ApplyGameplayEffectSpecToSelf(dynamicGrantedSpec);
                GameplayEffectSpec dynamicAssetSpec = GameplayEffectSpec.Create(dynamicAssetEffect, asc);
                dynamicAssetSpec.DynamicAssetTags.AddTag(sharedTag);
                asc.ApplyGameplayEffectSpecToSelf(dynamicAssetSpec);
                GameplayEffectSpec combinedSpec = GameplayEffectSpec.Create(combinedEffect, asc);
                combinedSpec.DynamicGrantedTags.AddTag(sharedTag);
                asc.ApplyGameplayEffectSpecToSelf(combinedSpec);
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(otherEffect, asc));
                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(5));

                asc.RemoveActiveEffectsWithGrantedTags(Container(sharedTag));

                TestContext.WriteLine($"[DeepAudit][CombinedDispel] remaining={asc.ActiveEffects.Count}");
                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
                Assert.That(asc.ActiveEffects[0].Spec.Def, Is.SameAs(otherEffect));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void CueFastPathWrappers_DoNotRequireAsyncStateMachines()
        {
            MethodInfo getCue = typeof(GameplayCueManager).GetMethod(
                "GetCueSOAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(getCue, Is.Not.Null);
            Assert.That(getCue.GetCustomAttribute<AsyncStateMachineAttribute>(), Is.Null);

            Assert.That(typeof(GameplayCueSO).GetMethod(nameof(GameplayCueSO.OnExecutedAsync))
                .GetCustomAttribute<AsyncStateMachineAttribute>(), Is.Null);
            Assert.That(typeof(GameplayCueSO).GetMethod(nameof(GameplayCueSO.OnActiveAsync))
                .GetCustomAttribute<AsyncStateMachineAttribute>(), Is.Null);
            Assert.That(typeof(GameplayCueSO).GetMethod(nameof(GameplayCueSO.OnWhileActiveAsync))
                .GetCustomAttribute<AsyncStateMachineAttribute>(), Is.Null);
            Assert.That(typeof(GameplayCueSO).GetMethod(nameof(GameplayCueSO.OnRemovedAsync))
                .GetCustomAttribute<AsyncStateMachineAttribute>(), Is.Null);
        }

        private static GameplayTag RegisterTag(string suffix)
        {
            string name = "DeepAudit.GAS." + suffix;
            GameplayTagManager.RegisterDynamicTag(name);
            return GameplayTagManager.Request(name);
        }

        private static GameplayTagContainer Container(GameplayTag tag)
        {
            var container = new GameplayTagContainer();
            container.AddTag(tag);
            return container;
        }
    }
}
