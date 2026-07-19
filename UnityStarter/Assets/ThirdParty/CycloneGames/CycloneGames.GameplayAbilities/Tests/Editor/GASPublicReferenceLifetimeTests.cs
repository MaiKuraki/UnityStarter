using System;

using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASPublicReferenceLifetimeTests
    {
        [Test]
        public void GameplayEffectSpec_DiscardedReferenceCannotAliasReplacement()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var effect = new GameplayEffect("SpecLifetime", EDurationPolicy.Instant);
            GameplayEffectSpec released = GameplayEffectSpec.Create(effect, asc);

            released.Discard();
            GameplayEffectSpec replacement = GameplayEffectSpec.Create(effect, asc);

            Assert.That(replacement, Is.Not.SameAs(released));
            Assert.Throws<ObjectDisposedException>(released.Discard);
            Assert.That(replacement.Def, Is.SameAs(effect));
            Assert.That(replacement.Level, Is.EqualTo(1));

            replacement.Discard();
            asc.Dispose();
        }

        [Test]
        public void GameplayEffectContext_DisposedReferenceCannotAliasReplacement()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffectContext released = asc.MakeEffectContext();

            released.Dispose();
            GameplayEffectContext replacement = asc.MakeEffectContext();

            Assert.That(replacement, Is.Not.SameAs(released));
            Assert.Throws<InvalidOperationException>(() => released.AddInstigator(asc, null));
            replacement.AddInstigator(asc, null);
            Assert.That(replacement.Instigator, Is.SameAs(asc));

            replacement.Dispose();
            asc.Dispose();
        }

        [Test]
        public void ActiveGameplayEffect_RemovedReferenceCannotRemoveReplacement()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var effect = new GameplayEffect("ActiveEffectLifetime", EDurationPolicy.Infinite);
            GameplayEffectApplicationResult first = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effect, asc));

            Assert.That(first.Succeeded, Is.True);
            Assert.That(asc.TryRemoveActiveEffect(first.ActiveEffect), Is.True);

            GameplayEffectApplicationResult replacement = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effect, asc));

            Assert.That(replacement.Succeeded, Is.True);
            Assert.That(replacement.ActiveEffect, Is.Not.SameAs(first.ActiveEffect));
            Assert.That(asc.TryRemoveActiveEffect(first.ActiveEffect), Is.False);
            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(asc.ActiveEffects[0], Is.SameAs(replacement.ActiveEffect));

            Assert.That(asc.TryRemoveActiveEffect(replacement.ActiveEffect), Is.True);
            asc.Dispose();
        }

        [Test]
        public void GameplayAbilitySpec_ClearedReferenceCannotActivateReplacement()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec released = asc.GrantAbility(new LifetimeAbility("ReleasedSpec"));

            asc.ClearAbility(released);
            GameplayAbilitySpec replacement = asc.GrantAbility(new LifetimeAbility("ReplacementSpec"));

            Assert.That(replacement, Is.Not.SameAs(released));
            Assert.That(asc.TryActivateAbility(released), Is.False);
            Assert.That(replacement.IsActive, Is.False);
            Assert.That(asc.TryActivateAbility(replacement), Is.True);

            asc.ClearAbility(replacement);
            asc.Dispose();
        }

        [Test]
        public void RuntimeGameplayAbility_ReleasedReferenceCannotEndReplacement()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec firstSpec = asc.GrantAbility(new LifetimeAbility("RuntimeAbility"));
            GameplayAbility released = firstSpec.GetPrimaryInstance();

            asc.ClearAbility(firstSpec);
            GameplayAbilitySpec replacementSpec = asc.GrantAbility(new LifetimeAbility("RuntimeAbility"));
            GameplayAbility replacement = replacementSpec.GetPrimaryInstance();

            Assert.That(replacement, Is.Not.SameAs(released));
            Assert.That(asc.TryActivateAbility(replacementSpec), Is.True);
            Assert.That(replacementSpec.IsActive, Is.True);
            Assert.DoesNotThrow(released.EndAbility);
            Assert.That(replacementSpec.IsActive, Is.True);

            asc.ClearAbility(replacementSpec);
            asc.Dispose();
        }

        [Test]
        public void AbilityTask_EndedReferenceCannotEndReplacement()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new LifetimeAbility("TaskOwner"));
            GameplayAbility ability = spec.GetPrimaryInstance();
            LifetimeTask released = ability.NewAbilityTask<LifetimeTask>();
            released.Activate();

            released.EndTask();
            LifetimeTask replacement = ability.NewAbilityTask<LifetimeTask>();
            replacement.Activate();

            Assert.That(replacement, Is.Not.SameAs(released));
            Assert.DoesNotThrow(released.EndTask);
            Assert.That(replacement.IsActive, Is.True);

            replacement.EndTask();
            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void TargetData_ReleasedReferenceCannotReleaseReplacement()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilityTargetData_ActorArray released =
                asc.RentTargetData<GameplayAbilityTargetData_ActorArray>();

            released.Release();
            GameplayAbilityTargetData_ActorArray replacement =
                asc.RentTargetData<GameplayAbilityTargetData_ActorArray>();

            Assert.That(replacement, Is.Not.SameAs(released));
            Assert.Throws<ObjectDisposedException>(() => _ = released.ActorCount);
            released.Release();
            Assert.That(replacement.ActorCount, Is.Zero);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().TargetData.Active, Is.EqualTo(1));

            replacement.Release();
            asc.Dispose();
        }

        [Test]
        public void StandaloneTargetData_ReleaseFailsClosedForSubsequentAccess()
        {
            var data = new GameplayAbilityTargetData_ActorArray();

            Assert.That(data.ActorCount, Is.Zero);
            data.Release();

            Assert.Throws<ObjectDisposedException>(() => _ = data.ActorCount);
            Assert.Throws<ObjectDisposedException>(() => _ = data.PredictionKey);
            Assert.DoesNotThrow(data.Release);
        }

        [Test]
        public void RuntimeLeaseObjects_ReleasedInstancesRejectInternalReacquisition()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GASRuntimeMemory memory = asc.RuntimeContext.Memory;
                GameplayEffectSpec effectSpec = memory.AcquireEffectSpec();
                ActiveGameplayEffect activeEffect = memory.AcquireActiveEffect();
                GameplayEffectContext effectContext = memory.AcquireEffectContext();
                GameplayAbilitySpec abilitySpec = memory.AcquireAbilitySpec();
                var abilityDefinition = new LifetimeAbility("InternalLeaseInvariant");
                GameplayAbility ability = memory.AcquireAbility(abilityDefinition);
                LifetimeTask task = memory.AcquireTask<LifetimeTask>();
                GameplayAbilityTargetData_ActorArray targetData =
                    memory.AcquireTargetData<GameplayAbilityTargetData_ActorArray>(1);

                Assert.That(memory.ReleaseEffectSpec(effectSpec), Is.True);
                Assert.That(memory.ReleaseActiveEffect(activeEffect), Is.True);
                Assert.That(memory.ReleaseEffectContext(effectContext), Is.True);
                Assert.That(memory.ReleaseAbilitySpec(abilitySpec), Is.True);
                memory.ReleaseAbility(ability);
                memory.ReleaseTask(task);
                memory.ReleaseTargetData(targetData);

                Assert.That(memory.GetStatistics().OutstandingLeases, Is.Zero);
                Assert.That(((IGASLeasedObject)effectSpec).TryAcquireLease(), Is.False);
                Assert.That(((IGASLeasedObject)activeEffect).TryAcquireLease(), Is.False);
                Assert.That(((IGASLeasedObject)effectContext).TryAcquireLease(), Is.False);
                Assert.That(((IGASLeasedObject)abilitySpec).TryAcquireLease(), Is.False);
                Assert.Throws<InvalidOperationException>(
                    () => ability.MarkLeaseAcquired(memory, abilityDefinition));
                Assert.Throws<InvalidOperationException>(() => task.MarkLeaseAcquired(memory));
                Assert.Throws<InvalidOperationException>(() => targetData.MarkLeaseAcquired(memory, 1));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [TestCase(InvalidRuntimeInstanceResult.Null)]
        [TestCase(InvalidRuntimeInstanceResult.Self)]
        [TestCase(InvalidRuntimeInstanceResult.WrongRuntimeType)]
        public void GrantAbility_InvalidRuntimeInstanceFactoryFailsWithoutOutstandingLease(
            InvalidRuntimeInstanceResult result)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var ability = new InvalidRuntimeInstanceAbility(result);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => asc.GrantAbility(ability));

                StringAssert.Contains("CreateRuntimeInstance()", exception.Message);
                Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            }
            finally
            {
                asc.Dispose();
            }
        }

        public enum InvalidRuntimeInstanceResult
        {
            Null,
            Self,
            WrongRuntimeType
        }

        private sealed class InvalidRuntimeInstanceAbility : GameplayAbility
        {
            private readonly InvalidRuntimeInstanceResult result;

            public InvalidRuntimeInstanceAbility(InvalidRuntimeInstanceResult result)
            {
                this.result = result;
                Initialize(
                    $"InvalidRuntimeInstance_{result}",
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
                switch (result)
                {
                    case InvalidRuntimeInstanceResult.Null:
                        return null;
                    case InvalidRuntimeInstanceResult.Self:
                        return this;
                    case InvalidRuntimeInstanceResult.WrongRuntimeType:
                        return new LifetimeAbility("WrongRuntimeType");
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private sealed class LifetimeAbility : GameplayAbility
        {
            private readonly string abilityName;

            public LifetimeAbility(string abilityName)
            {
                this.abilityName = abilityName;
                Initialize(
                    abilityName,
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
                return new LifetimeAbility(abilityName);
            }
        }

        private sealed class LifetimeTask : AbilityTask
        {
            protected override void OnActivate()
            {
            }
        }
    }
}
