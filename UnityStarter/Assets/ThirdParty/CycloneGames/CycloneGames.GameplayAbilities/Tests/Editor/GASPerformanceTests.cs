using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASPerformanceTests
    {
        private const int AllocationProbeIterations = 100_000;

        [Test, Performance]
        public void AbilitySystemComponent_IdleTick_SteadyStateBenchmark()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            asc.ReserveRuntimeCapacity(
                abilityCapacity: 16,
                attributeCapacity: 32,
                activeEffectCapacity: 64,
                tickingAbilityCapacity: 8,
                dirtyAttributeCapacity: 32,
                predictedAttributeCapacity: 16,
                predictionWindowCapacity: 8,
                coreModifierCapacity: 64,
                corePredictionCapacity: 8,
                predictionTransactionRecordCapacity: 16);

            Measure.Method(() => asc.Tick(1f / 60f, isServer: true))
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(10_000)
                .GC()
                .Run();

            asc.Dispose();
        }

        [Test, Performance]
        public void GameplayEffectSpec_CreateConfigureDiscard_Benchmark()
        {
            const string tagName = "Test.GAS.Performance.SetByCaller";
            GameplayTagManager.RegisterDynamicTag(tagName, "GameplayEffectSpec benchmark tag");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag dataTag = GameplayTagManager.RequestTag(tagName);
            var effect = new GameplayEffect(
                "BenchmarkEffect",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Benchmark", EAttributeModifierOperation.Add, new SetByCallerMagnitude(dataTag))
                });
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            Measure.Method(() =>
                {
                    GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);
                    spec.SetSetByCallerMagnitudeRaw(dataTag, GASFixedValue.One.RawValue);
                    spec.Discard();
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(10_000)
                .GC()
                .Run();

            asc.Dispose();
        }

        [Test, Performance]
        public void DurationEffect_ApplyRemove_Benchmark()
        {
            var effect = new GameplayEffect(
                "DurationApplyRemoveBenchmark",
                EDurationPolicy.HasDuration,
                duration: 10f);
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            Measure.Method(() =>
                {
                    GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                        GameplayEffectSpec.Create(effect, asc));
                    if (!result.Succeeded || result.ActiveEffect == null || !asc.TryRemoveActiveEffect(result.ActiveEffect))
                    {
                        throw new InvalidOperationException("Duration effect benchmark failed to apply and remove its effect.");
                    }
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(1_000)
                .GC()
                .Run();

            asc.Dispose();
        }

        [Test, Performance]
        public void InstancedPerExecutionAbility_ActivateEnd_Benchmark()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new BenchmarkExecutionAbility());

            Measure.Method(() =>
                {
                    if (!asc.TryActivateAbility(spec))
                    {
                        throw new InvalidOperationException("Instanced-per-execution benchmark ability did not activate.");
                    }
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(1_000)
                .GC()
                .Run();

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test, Performance]
        public void TargetData_AcquireRelease_Benchmark()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            Measure.Method(() =>
                {
                    TargetData data = asc.RentTargetData<GameplayAbilityTargetData_ActorArray>();
                    data.Release();
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(10_000)
                .GC()
                .Run();

            asc.Dispose();
        }

        [Test, Performance]
        public void AbilityTask_CreateActivateEnd_Benchmark()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new BenchmarkTaskOwnerAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();

            Measure.Method(() =>
                {
                    BenchmarkImmediateTask task = ability.NewAbilityTask<BenchmarkImmediateTask>();
                    task.Activate();
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(1_000)
                .GC()
                .Run();

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void AbilitySystemComponent_IdleTick_SteadyStateAllocatesNoManagedBytes()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                asc.ReserveRuntimeCapacity(
                    abilityCapacity: 16,
                    attributeCapacity: 32,
                    activeEffectCapacity: 64,
                    tickingAbilityCapacity: 8,
                    dirtyAttributeCapacity: 32,
                    predictedAttributeCapacity: 16,
                    predictionWindowCapacity: 8,
                    coreModifierCapacity: 64,
                    corePredictionCapacity: 8,
                    predictionTransactionRecordCapacity: 16);

                for (int i = 0; i < 1_000; i++)
                {
                    asc.Tick(1f / 60f, isServer: true);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < AllocationProbeIterations; i++)
                {
                    asc.Tick(1f / 60f, isServer: true);
                }
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.Zero(allocatedBytes);
            }
            finally
            {
                asc.Dispose();
            }
        }

        private sealed class BenchmarkExecutionAbility : GameplayAbility
        {
            public BenchmarkExecutionAbility() : this(initializeDefinition: true)
            {
            }

            private BenchmarkExecutionAbility(bool initializeDefinition)
            {
                if (!initializeDefinition)
                {
                    return;
                }

                Initialize(
                    "Benchmark.Execution",
                    EGameplayAbilityInstancingPolicy.InstancedPerExecution,
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
                EndAbility();
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new BenchmarkExecutionAbility(initializeDefinition: false);
            }
        }

        private sealed class BenchmarkTaskOwnerAbility : GameplayAbility
        {
            public BenchmarkTaskOwnerAbility()
            {
                Initialize(
                    "Benchmark.TaskOwner",
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
                return new BenchmarkTaskOwnerAbility();
            }
        }

        private sealed class BenchmarkImmediateTask : AbilityTask
        {
            protected override void OnActivate()
            {
                EndTask();
            }
        }
    }
}
