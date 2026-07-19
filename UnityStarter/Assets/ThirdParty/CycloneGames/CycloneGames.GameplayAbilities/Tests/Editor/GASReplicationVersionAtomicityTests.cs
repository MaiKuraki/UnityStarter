using System;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASReplicationVersionAtomicityTests
    {
        [Test]
        public void MutationScope_MaxMinusOne_MultipleMarksConsumeOneStateVersion()
        {
            var builder = new ReplicationStateBuilder
            {
                StateVersion = ulong.MaxValue - 1UL
            };
            var attribute = new GameplayAttribute("VersionGuard.ScopeAttribute");

            using (builder.BeginMutationScope())
            {
                Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
                Assert.That(builder.MutationScopeDepth, Is.EqualTo(1));

                builder.MarkGrantedAbilitiesDirty();
                builder.MarkActiveEffectsDirty();
                builder.MarkAttributeValueDirty(attribute);
                builder.MarkAttributeValueDirty(attribute);

                Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
            }

            Assert.That(builder.MutationScopeDepth, Is.Zero);
            Assert.That(builder.GrantedAbilitiesDirty, Is.True);
            Assert.That(builder.ActiveEffectsDirty, Is.True);
            Assert.That(builder.DirtyAttributeNames, Does.Contain(attribute.Name));
            Assert.Throws<InvalidOperationException>(() => builder.BeginMutationScope());
            Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void MutationScope_NestedScopesShareOneStateReservation()
        {
            var builder = new ReplicationStateBuilder
            {
                StateVersion = 41UL
            };

            using (builder.BeginMutationScope())
            {
                Assert.That(builder.StateVersion, Is.EqualTo(42UL));
                using (builder.BeginMutationScope())
                {
                    Assert.That(builder.MutationScopeDepth, Is.EqualTo(2));
                    builder.MarkGrantedAbilitiesDirty();
                    builder.MarkActiveEffectsDirty();
                    Assert.That(builder.StateVersion, Is.EqualTo(42UL));
                }

                Assert.That(builder.MutationScopeDepth, Is.EqualTo(1));
            }

            Assert.That(builder.MutationScopeDepth, Is.Zero);
            Assert.That(builder.StateVersion, Is.EqualTo(42UL));
        }

        [Test]
        public void MutationScope_NestedAttributeStructureUsesExistingStateReservation()
        {
            var builder = new ReplicationStateBuilder
            {
                StateVersion = ulong.MaxValue - 1UL,
                AttributeRegistryVersion = uint.MaxValue - 1U
            };

            using (builder.BeginMutationScope())
            {
                Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
                using (builder.BeginMutationScope(attributeStructure: true))
                {
                    builder.MarkAttributeStructureDirty();
                    builder.MarkAttributeStructureDirty();
                    Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
                    Assert.That(builder.AttributeRegistryVersion, Is.EqualTo(uint.MaxValue));
                }
            }

            Assert.That(builder.AttributeStructureDirty, Is.True);
            Assert.Throws<InvalidOperationException>(() => builder.BeginMutationScope(attributeStructure: true));
            Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
            Assert.That(builder.AttributeRegistryVersion, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void MutationScope_AttributeStructurePreflightIsAtomic()
        {
            var builder = new ReplicationStateBuilder
            {
                StateVersion = 17UL,
                AttributeRegistryVersion = uint.MaxValue
            };

            Assert.Throws<InvalidOperationException>(() => builder.BeginMutationScope(attributeStructure: true));

            Assert.That(builder.StateVersion, Is.EqualTo(17UL));
            Assert.That(builder.AttributeRegistryVersion, Is.EqualTo(uint.MaxValue));
            Assert.That(builder.MutationScopeDepth, Is.Zero);
            Assert.That(builder.AttributeStructureDirty, Is.False);
        }

        [Test]
        public void MutationScope_AttributeMarkRequiresStructureReservation()
        {
            var builder = new ReplicationStateBuilder();

            using (builder.BeginMutationScope())
            {
                Assert.Throws<InvalidOperationException>(builder.MarkAttributeStructureDirty);
                Assert.That(builder.AttributeRegistryVersion, Is.Zero);
                Assert.That(builder.AttributeStructureDirty, Is.False);
            }

            Assert.That(builder.StateVersion, Is.EqualTo(1UL));
            Assert.That(builder.MutationScopeDepth, Is.Zero);
        }

        [Test]
        public void AddAttributeSet_StateVersionExhaustionLeavesRegistrationUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var set = new VersionGuardAttributeSet();
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.AddAttributeSet(set));

                Assert.That(asc.AttributeSets.Count, Is.Zero);
                Assert.That(asc.GetAttribute(VersionGuardAttributeSet.AttributeName), Is.Null);
                Assert.That(set.OwningAbilitySystemComponent, Is.Null);
                Assert.That(asc.ReplicationStateBuilder.AttributeRegistryVersion, Is.Zero);
                Assert.That(asc.ReplicationStateBuilder.AttributeStructureDirty, Is.False);
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.Dispose();
            }
        }

        [Test]
        public void AddAttributeSet_AttributeRegistryVersionExhaustionLeavesRegistrationUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var set = new VersionGuardAttributeSet();
            asc.ReplicationStateBuilder.AttributeRegistryVersion = uint.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.AddAttributeSet(set));

                Assert.That(asc.AttributeSets.Count, Is.Zero);
                Assert.That(asc.GetAttribute(VersionGuardAttributeSet.AttributeName), Is.Null);
                Assert.That(set.OwningAbilitySystemComponent, Is.Null);
                Assert.That(asc.ReplicationStateBuilder.StateVersion, Is.Zero);
                Assert.That(asc.ReplicationStateBuilder.AttributeStructureDirty, Is.False);
            }
            finally
            {
                asc.ReplicationStateBuilder.AttributeRegistryVersion = 0U;
                asc.Dispose();
            }
        }

        [Test]
        public void RemoveAttributeSet_AttributeRegistryVersionExhaustionLeavesRegistrationUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var set = new VersionGuardAttributeSet();
            asc.AddAttributeSet(set);
            asc.ReplicationStateBuilder.AttributeRegistryVersion = uint.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.RemoveAttributeSet(set));

                Assert.That(asc.AttributeSets.Count, Is.EqualTo(1));
                Assert.That(asc.AttributeSets[0], Is.SameAs(set));
                Assert.That(asc.GetAttribute(VersionGuardAttributeSet.AttributeName), Is.SameAs(set.Health));
                Assert.That(set.OwningAbilitySystemComponent, Is.SameAs(asc));
            }
            finally
            {
                asc.ReplicationStateBuilder.AttributeRegistryVersion = 0U;
                if (ReferenceEquals(set.OwningAbilitySystemComponent, asc))
                {
                    asc.RemoveAttributeSet(set);
                }
                asc.Dispose();
            }
        }

        [Test]
        public void AttributeSetValueMutation_StateVersionExhaustionLeavesValuesUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var set = new VersionGuardAttributeSet();
            asc.AddAttributeSet(set);
            set.SetBaseValueRaw(set.Health, 17L);
            set.SetCurrentValueRaw(set.Health, 23L);
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => set.SetBaseValueRaw(set.Health, 31L));
                Assert.Throws<InvalidOperationException>(() => set.SetCurrentValueRaw(set.Health, 37L));

                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(17L));
                Assert.That(set.Health.CurrentValueRaw, Is.EqualTo(23L));
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.RemoveAttributeSet(set);
                asc.Dispose();
            }
        }

        [Test]
        public void GrantAbility_StateVersionExhaustionLeavesAbilityContainersUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.GrantAbility(new VersionGuardAbility()));

                Assert.That(asc.AbilitySpecs.Count, Is.Zero);
                Assert.That(asc.RuntimeContext.GetMemoryStatistics().AbilitySpecs.Active, Is.Zero);
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.Dispose();
            }
        }

        [Test]
        public void ClearAbility_StateVersionExhaustionLeavesGrantedSpecRegistered()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new VersionGuardAbility());
            int handle = spec.Handle;
            int grantedCount = asc.AbilitySpecs.Count;
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.ClearAbility(spec));

                Assert.That(asc.AbilitySpecs.Count, Is.EqualTo(grantedCount));
                Assert.That(asc.AbilitySpecs.TryGetSpecByHandle(handle, out GameplayAbilitySpec registered), Is.True);
                Assert.That(registered, Is.SameAs(spec));
                Assert.That(spec.Owner, Is.SameAs(asc));
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                if (asc.AbilitySpecs.TryGetSpecByHandle(handle, out GameplayAbilitySpec registered))
                {
                    asc.ClearAbility(registered);
                }
                asc.Dispose();
            }
        }

        [Test]
        public void ActivateAbility_StateVersionExhaustionLeavesSpecInactive()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new VersionGuardAbility());
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.TryActivateAbility(spec));

                Assert.That(spec.IsActive, Is.False);
                Assert.That(asc.AbilitySpecs.TickingCount, Is.Zero);
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.ClearAbility(spec);
                asc.Dispose();
            }
        }

        [Test]
        public void EndAbility_StateVersionExhaustionLeavesSpecActive()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new VersionGuardAbility());
            Assert.That(asc.TryActivateAbility(spec), Is.True);
            GameplayAbility ability = spec.GetPrimaryInstance();
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(ability.EndAbility);

                Assert.That(spec.IsActive, Is.True);
                Assert.That(asc.AbilitySpecs.TickingCount, Is.EqualTo(1));
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                if (spec.IsActive)
                {
                    ability.EndAbility();
                }
                asc.ClearAbility(spec);
                asc.Dispose();
            }
        }

        [Test]
        public void ApplyGameplayEffect_StateVersionExhaustionLeavesActiveEffectsUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(
                        new GameplayEffect("VersionGuardApply", EDurationPolicy.Infinite),
                        asc));

                Assert.That(result.Succeeded, Is.False);
                Assert.That(asc.ActiveEffects.Count, Is.Zero);
                Assert.That(asc.RuntimeContext.GetMemoryStatistics().ActiveEffects.Active, Is.Zero);
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.Dispose();
            }
        }

        [Test]
        public void TryRemoveActiveEffect_StateVersionExhaustionLeavesEffectActive()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    new GameplayEffect("VersionGuardEffect", EDurationPolicy.Infinite),
                    asc));
            Assert.That(result.Succeeded, Is.True);
            ActiveGameplayEffect effect = result.ActiveEffect;
            int activeCount = asc.ActiveEffects.Count;
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.TryRemoveActiveEffect(effect));

                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(activeCount));
                Assert.That(asc.ActiveEffects[0], Is.SameAs(effect));
                Assert.That(effect.IsExpired, Is.False);
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                if (asc.ActiveEffects.Count > 0 && ReferenceEquals(asc.ActiveEffects[0], effect))
                {
                    asc.TryRemoveActiveEffect(effect);
                }
                asc.Dispose();
            }
        }

        [Test]
        public void TryApplyActiveEffectStackChange_StateVersionExhaustionLeavesStackUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var stacking = new GameplayEffect(
                "VersionGuardStack",
                EDurationPolicy.Infinite,
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(stacking, asc));
            Assert.That(result.Succeeded, Is.True);
            ActiveGameplayEffect effect = result.ActiveEffect;
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.TryApplyActiveEffectStackChange(effect, 2));

                Assert.That(effect.StackCount, Is.EqualTo(1));
                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.TryRemoveActiveEffect(effect);
                asc.Dispose();
            }
        }

        [Test]
        public void AddLooseGameplayTag_StateVersionExhaustionLeavesTagCountsUntouched()
        {
            GameplayTag tag = RegisterTag("Test.GAS.VersionGuard.LooseTag");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.AddLooseGameplayTag(tag));

                Assert.That(asc.GetTagCount(tag), Is.Zero);
                Assert.That(asc.PendingAddedTags.Count, Is.Zero);
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.Dispose();
            }
        }

        [Test]
        public void AddLooseGameplayTag_MaxMinusOneHierarchyConsumesSingleStateVersion()
        {
            GameplayTag tag = RegisterTag("Test.GAS.VersionGuard.Hierarchy.Child");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue - 1UL;

            try
            {
                asc.AddLooseGameplayTag(tag);

                Assert.That(asc.ReplicationStateBuilder.StateVersion, Is.EqualTo(ulong.MaxValue));
                Assert.That(asc.GetTagCount(tag), Is.EqualTo(1));
                Assert.That(asc.PendingAddedTags.Count, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                if (asc.GetTagCount(tag) > 0)
                {
                    asc.RemoveLooseGameplayTag(tag);
                }
                asc.Dispose();
            }
        }

        [Test]
        public void Tick_StateVersionExhaustionLeavesSimulationFrameUntouched()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    new GameplayEffect("VersionGuardTick", EDurationPolicy.HasDuration, duration: 5f),
                    asc));
            Assert.That(result.Succeeded, Is.True);
            ActiveGameplayEffect effect = result.ActiveEffect;
            long simulationFrame = asc.SimulationFrame;
            long timeRemainingRaw = effect.TimeRemainingRaw;
            asc.ReplicationStateBuilder.StateVersion = ulong.MaxValue;

            try
            {
                Assert.Throws<InvalidOperationException>(() => asc.Tick(1f, isServer: true));

                Assert.That(asc.SimulationFrame, Is.EqualTo(simulationFrame));
                Assert.That(effect.TimeRemainingRaw, Is.EqualTo(timeRemainingRaw));
            }
            finally
            {
                asc.ReplicationStateBuilder.StateVersion = 0UL;
                asc.TryRemoveActiveEffect(effect);
                asc.Dispose();
            }
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayAbilities replication version atomicity test tag.");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }

        private sealed class VersionGuardAttributeSet : AttributeSet
        {
            public const string AttributeName = "VersionGuard.Health";
            public GameplayAttribute Health { get; } = new GameplayAttribute(AttributeName);

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
            }
        }

        private sealed class VersionGuardAbility : GameplayAbility
        {
            public VersionGuardAbility()
            {
                Initialize(
                    "VersionGuardAbility",
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

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new VersionGuardAbility();
            }
        }
    }
}
