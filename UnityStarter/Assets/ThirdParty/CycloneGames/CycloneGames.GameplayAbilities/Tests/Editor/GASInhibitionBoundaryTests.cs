using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Boundary and authoring coverage for inhibition-driven state: blocked ability tags, granted
    /// ability removal policies, cue suppression and tag-count integrity across repeated cycles.
    /// </summary>
    public sealed class GASInhibitionBoundaryTests
    {
        [Test]
        public void BlockedAbilityTags_AreCountedAcrossEffects()
        {
            GameplayTag first = RegisterTag("Test.GAS.Boundary.Blocked.First");
            GameplayTag second = RegisterTag("Test.GAS.Boundary.Blocked.Second");
            GameplayTag blocked = RegisterTag("Test.GAS.Boundary.Blocked.Tag");

            var asc = CreateAbilitySystem(out GASRuntimeContext context);
            var blockedTags = Container(blocked);
            var firstEffect = new GameplayEffect(
                "Boundary.Blocked.First",
                EDurationPolicy.Infinite,
                ongoingTagRequirements: Requirements(first),
                blockedAbilityTags: blockedTags);
            var secondEffect = new GameplayEffect(
                "Boundary.Blocked.Second",
                EDurationPolicy.Infinite,
                ongoingTagRequirements: Requirements(second),
                blockedAbilityTags: blockedTags);

            asc.AddLooseGameplayTag(first);
            asc.AddLooseGameplayTag(second);
            Assert.That(Apply(asc, firstEffect).Succeeded, Is.True);
            Assert.That(Apply(asc, secondEffect).Succeeded, Is.True);
            asc.Tick(0f, true);
            Assert.That(asc.AreAbilitiesBlockedByTag(blockedTags), Is.True);

            asc.RemoveLooseGameplayTag(first);
            asc.Tick(0f, true);
            Assert.That(
                asc.AreAbilitiesBlockedByTag(blockedTags),
                Is.True,
                "The second effect still blocks the tag.");

            asc.RemoveLooseGameplayTag(second);
            asc.Tick(0f, true);
            Assert.That(asc.AreAbilitiesBlockedByTag(blockedTags), Is.False);

            asc.AddLooseGameplayTag(first);
            asc.Tick(0f, true);
            Assert.That(asc.AreAbilitiesBlockedByTag(blockedTags), Is.True);

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void BlockedAbilityTags_DoNotLeakAfterEffectRemoval()
        {
            GameplayTag required = RegisterTag("Test.GAS.Boundary.Leak.Required");
            GameplayTag blocked = RegisterTag("Test.GAS.Boundary.Leak.Tag");

            var asc = CreateAbilitySystem(out GASRuntimeContext context);
            var blockedTags = Container(blocked);
            var effect = new GameplayEffect(
                "Boundary.Leak",
                EDurationPolicy.Infinite,
                ongoingTagRequirements: Requirements(required),
                blockedAbilityTags: blockedTags);

            asc.AddLooseGameplayTag(required);
            GameplayEffectApplicationResult application = Apply(asc, effect);
            Assert.That(application.Succeeded, Is.True);
            asc.Tick(0f, true);
            Assert.That(asc.AreAbilitiesBlockedByTag(blockedTags), Is.True);

            Assert.That(asc.TryRemoveActiveEffect(application.ActiveEffect), Is.True);
            asc.Tick(0f, true);

            Assert.That(
                asc.AreAbilitiesBlockedByTag(blockedTags),
                Is.False,
                "Removing the effect must release its blocked tags.");

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void GrantedAbilities_MultipleEffectsGrantIndependently()
        {
            GameplayTag first = RegisterTag("Test.GAS.Boundary.Grant.First");
            GameplayTag second = RegisterTag("Test.GAS.Boundary.Grant.Second");
            var sharedAbility = new BoundaryAbility("Boundary.SharedGrant");

            var asc = CreateAbilitySystem(out GASRuntimeContext context);
            var firstEffect = new GameplayEffect(
                "Boundary.Grant.First",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { sharedAbility },
                ongoingTagRequirements: Requirements(first));
            var secondEffect = new GameplayEffect(
                "Boundary.Grant.Second",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { sharedAbility },
                ongoingTagRequirements: Requirements(second));

            asc.AddLooseGameplayTag(first);
            asc.AddLooseGameplayTag(second);
            Assert.That(Apply(asc, firstEffect).Succeeded, Is.True);
            Assert.That(Apply(asc, secondEffect).Succeeded, Is.True);
            asc.Tick(0f, true);
            Assert.That(asc.GetActivatableAbilities().Count, Is.EqualTo(2));

            asc.RemoveLooseGameplayTag(first);
            asc.Tick(0f, true);
            Assert.That(
                asc.GetActivatableAbilities().Count,
                Is.EqualTo(1),
                "Only the inhibited effect's own grant is removed.");

            asc.RemoveLooseGameplayTag(second);
            asc.Tick(0f, true);
            Assert.That(asc.GetActivatableAbilities().Count, Is.Zero);

            asc.AddLooseGameplayTag(first);
            asc.Tick(0f, true);
            Assert.That(asc.GetActivatableAbilities().Count, Is.EqualTo(1));

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void GrantedAbilities_PendingRemoveOnEnd_SurvivesEffectRemoval()
        {
            GameplayTag required = RegisterTag("Test.GAS.Boundary.Pending.Required");
            var ability = new BoundaryAbility("Boundary.PendingGrant");

            var asc = CreateAbilitySystem(out GASRuntimeContext context);
            var effect = new GameplayEffect(
                "Boundary.Pending",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { ability },
                ongoingTagRequirements: Requirements(required),
                grantedAbilityRemovalPolicies: new List<EGameplayEffectGrantedAbilityRemovePolicy>
                {
                    EGameplayEffectGrantedAbilityRemovePolicy.RemoveAbilityOnEnd
                });

            asc.AddLooseGameplayTag(required);
            GameplayEffectApplicationResult application = Apply(asc, effect);
            Assert.That(application.Succeeded, Is.True);
            asc.Tick(0f, true);

            GameplayAbilitySpec spec = asc.GetActivatableAbilities()[0];
            Assert.That(asc.TryActivateAbility(spec), Is.True);

            asc.RemoveLooseGameplayTag(required);
            asc.Tick(0f, true);
            Assert.That(spec.RemoveOnEnd, Is.True);

            Assert.That(asc.TryRemoveActiveEffect(application.ActiveEffect), Is.True);
            Assert.DoesNotThrow(() => asc.Tick(0f, true));
            Assert.That(asc.GetActivatableAbilities().Count, Is.Zero);

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void InhibitionCycles_DoNotAccumulateTagCounts()
        {
            GameplayTag required = RegisterTag("Test.GAS.Boundary.Cycles.Required");
            GameplayTag granted = RegisterTag("Test.GAS.Boundary.Cycles.Granted");
            GameplayTag blocked = RegisterTag("Test.GAS.Boundary.Cycles.Blocked");

            var asc = CreateAbilitySystem(out GASRuntimeContext context);
            var effect = new GameplayEffect(
                "Boundary.Cycles",
                EDurationPolicy.Infinite,
                grantedTags: Container(granted),
                ongoingTagRequirements: Requirements(required),
                blockedAbilityTags: Container(blocked));

            asc.AddLooseGameplayTag(required);
            Assert.That(Apply(asc, effect).Succeeded, Is.True);
            asc.Tick(0f, true);
            Assert.That(asc.GetTagCount(granted), Is.EqualTo(1));

            for (int i = 0; i < 20; i++)
            {
                asc.RemoveLooseGameplayTag(required);
                asc.Tick(0f, true);
                Assert.That(asc.GetTagCount(granted), Is.Zero, $"cycle {i} withdrawal");

                asc.AddLooseGameplayTag(required);
                asc.Tick(0f, true);
                Assert.That(asc.GetTagCount(granted), Is.EqualTo(1), $"cycle {i} restore");
            }

            Assert.That(asc.AreAbilitiesBlockedByTag(Container(blocked)), Is.True);
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void InhibitionCues_AreSuppressedWhenConfigured()
        {
            GameplayTag required = RegisterTag("Test.GAS.Boundary.Cue.Required");
            GameplayTag cue = RegisterTag("Test.GAS.Boundary.Cue.Tag");

            var asc = CreateAbilitySystem(out GASRuntimeContext context);
            var effect = new GameplayEffect(
                "Boundary.Cue",
                EDurationPolicy.Infinite,
                gameplayCues: Container(cue),
                ongoingTagRequirements: Requirements(required),
                suppressGameplayCues: true);

            var events = new List<EGameplayCueEvent>();
            asc.OnGameplayCueCommitted += (in GameplayCueCommitted committed) => events.Add(committed.Event);

            asc.AddLooseGameplayTag(required);
            Assert.That(Apply(asc, effect).Succeeded, Is.True);
            asc.Tick(0f, true);
            asc.RemoveLooseGameplayTag(required);
            asc.Tick(0f, true);
            asc.AddLooseGameplayTag(required);
            asc.Tick(0f, true);

            Assert.That(events, Is.Empty);

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void GrantedAbilityRemovalPolicies_RejectInvalidConfiguration()
        {
            var ability = new BoundaryAbility("Boundary.InvalidPolicy");

            Assert.Throws<ArgumentException>(() => new GameplayEffect(
                "Boundary.InvalidPolicy.Count",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { ability },
                grantedAbilityRemovalPolicies: new List<EGameplayEffectGrantedAbilityRemovePolicy>()));

            Assert.Throws<ArgumentException>(() => new GameplayEffect(
                "Boundary.InvalidPolicy.Value",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { ability },
                grantedAbilityRemovalPolicies: new List<EGameplayEffectGrantedAbilityRemovePolicy>
                {
                    (EGameplayEffectGrantedAbilityRemovePolicy)99
                }));
        }

        [Test]
        public void ConcreteEffectSO_MapsBlockedTagsAndRemovalPolicies()
        {
            GameplayTag blocked = RegisterTag("Test.GAS.Boundary.Authoring.Blocked");
            var abilitySO = ScriptableObject.CreateInstance<BoundaryAbilitySO>();
            var effectSO = ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                abilitySO.AbilityName = "Boundary.AuthoredAbility";
                effectSO.EffectName = "Boundary.AuthoredEffect";
                effectSO.DurationPolicy = EDurationPolicy.Infinite;
                effectSO.GrantedAbilities = new List<GameplayAbilitySO> { abilitySO };
                effectSO.GrantedAbilityRemovalPolicies =
                    new List<EGameplayEffectGrantedAbilityRemovePolicy>
                    {
                        EGameplayEffectGrantedAbilityRemovePolicy.RemoveAbilityOnEnd
                    };
                effectSO.BlockedAbilityTags = new SerializableGameplayTagContainer();
                effectSO.BlockedAbilityTags.Container.AddTag(blocked);

                GameplayEffect definition = effectSO.GetGameplayEffect();

                Assert.That(definition.GrantedAbilities.Count, Is.EqualTo(1));
                Assert.That(
                    definition.GrantedAbilityRemovalPolicies[0],
                    Is.EqualTo(EGameplayEffectGrantedAbilityRemovePolicy.RemoveAbilityOnEnd));
                Assert.That(definition.BlockedAbilityTags.IsEmpty, Is.False);
                Assert.That(definition.BlockedAbilityTagsSnapshot.HasTag(blocked), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(effectSO);
                UnityEngine.Object.DestroyImmediate(abilitySO);
            }
        }

        [Test]
        public void InhibitionParity_TwoInstancesIdenticalScript()
        {
            GameplayTag required = RegisterTag("Test.GAS.Boundary.Parity.Required");
            GameplayTag granted = RegisterTag("Test.GAS.Boundary.Parity.Granted");
            GameplayTag blocked = RegisterTag("Test.GAS.Boundary.Parity.Blocked");
            GameplayTag cue = RegisterTag("Test.GAS.Boundary.Parity.Cue");

            int[] first = RunParityScript(required, granted, blocked, cue, out bool[] firstBlocks);
            int[] second = RunParityScript(required, granted, blocked, cue, out bool[] secondBlocks);

            Assert.That(second, Is.EqualTo(first), "Two independent instances must produce identical counts.");
            Assert.That(secondBlocks, Is.EqualTo(firstBlocks));
        }

        private static int[] RunParityScript(
            GameplayTag required,
            GameplayTag granted,
            GameplayTag blocked,
            GameplayTag cue,
            out bool[] blockStates)
        {
            var asc = CreateAbilitySystem(out GASRuntimeContext context);
            var events = new List<EGameplayCueEvent>();
            asc.OnGameplayCueCommitted += (in GameplayCueCommitted committed) => events.Add(committed.Event);

            var effect = new GameplayEffect(
                "Boundary.Parity",
                EDurationPolicy.Infinite,
                grantedTags: Container(granted),
                gameplayCues: Container(cue),
                ongoingTagRequirements: Requirements(required),
                blockedAbilityTags: Container(blocked));

            asc.AddLooseGameplayTag(required);
            Assert.That(Apply(asc, effect).Succeeded, Is.True);
            asc.Tick(0f, true);
            int grantedWhileActive = asc.GetTagCount(granted);
            bool blockedWhileActive = asc.AreAbilitiesBlockedByTag(Container(blocked));

            asc.RemoveLooseGameplayTag(required);
            asc.Tick(0f, true);
            int grantedWhileInhibited = asc.GetTagCount(granted);
            bool blockedWhileInhibited = asc.AreAbilitiesBlockedByTag(Container(blocked));

            asc.AddLooseGameplayTag(required);
            asc.Tick(0f, true);
            int grantedAfterRestore = asc.GetTagCount(granted);

            blockStates = new[] { blockedWhileActive, blockedWhileInhibited };
            int[] result =
            {
                grantedWhileActive,
                grantedWhileInhibited,
                grantedAfterRestore,
                events.Count
            };

            asc.Dispose();
            context.Dispose();
            return result;
        }

        private static AbilitySystemComponent CreateAbilitySystem(out GASRuntimeContext context)
        {
            context = new GASRuntimeContext();
            return new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
        }

        private static GameplayEffectApplicationResult Apply(
            AbilitySystemComponent asc,
            GameplayEffect effect)
        {
            return asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
        }

        private static GameplayTagRequirements Requirements(GameplayTag tag)
        {
            return new GameplayTagRequirements(new GameplayTagContainer(), Container(tag));
        }

        private static GameplayTagContainer Container(GameplayTag tag)
        {
            var container = new GameplayTagContainer();
            container.AddTag(tag);
            return container;
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GAS inhibition boundary test tag.");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.Request(name);
        }

        private sealed class BoundaryAbility : GameplayAbility
        {
            public BoundaryAbility(string name)
            {
                Initialize(
                    name,
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
            }

            public override GameplayAbility CreateRuntimeInstance() => new BoundaryAbility(Name);
        }

        private sealed class BoundaryAbilitySO : GameplayAbilitySO
        {
            protected override GameplayAbility CreateGameplayAbility()
            {
                string name = string.IsNullOrEmpty(AbilityName) ? "Boundary.AuthoredAbility" : AbilityName;
                return new BoundaryAbility(name);
            }
        }
    }
}
