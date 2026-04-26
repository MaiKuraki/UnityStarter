using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    [Flags]
    public enum GASSmokeTestFailureFlags : uint
    {
        None = 0,
        AttributeRegistrationFailed = 1u << 0,
        InstantEffectFailed = 1u << 1,
        DurationEffectFailed = 1u << 2,
        PredictionConfirmFailed = 1u << 3,
        PredictionRejectFailed = 1u << 4,
        DeltaCaptureFailed = 1u << 5,
        RuntimeDiagnosticsFailed = 1u << 6,
        RuntimeIndexValidationFailed = 1u << 7,
        StackingEffectFailed = 1u << 8,
        ActiveEffectRemovalFailed = 1u << 9,
        PeriodicEffectFailed = 1u << 10,
        DeltaClearFailed = 1u << 11,
        PredictionAttributeRollbackFailed = 1u << 12,
        PredictionAttributeConfirmFailed = 1u << 13
    }

    public struct GASSmokeTestResult
    {
        public GASSmokeTestFailureFlags FailureFlags;
        public GASRuntimeDiagnostics InitialDiagnostics;
        public GASRuntimeDiagnostics FinalDiagnostics;
        public uint InitialChecksum;
        public uint FinalChecksum;
        public float FinalHealth;
        public int ActiveEffectCount;
        public int StackCount;
        public int ActiveEffectCountAfterRemove;
        public int DeltaAttributeCount;
        public int DeltaActiveEffectCount;
        public int DeltaGrantedAbilityCount;
        public float HealthAfterPeriodicTick;
        public float HealthAfterPredictionReject;
        public float HealthAfterPredictionConfirm;
        public bool DeltaHadChangesAfterClear;
        public long RuntimeThreadViolationCount;

        public bool Passed => FailureFlags == GASSmokeTestFailureFlags.None;
    }

    public static class GASSmokeTest
    {
        public static bool RunBasicRuntimeValidation(out GASSmokeTestResult result)
        {
            result = default;
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var delta = new GASAbilitySystemStateDeltaBuffer();

            try
            {
                asc.InitAbilityActorInfo(null, null);
                asc.RuntimeThreadPolicy = GASRuntimeThreadPolicy.Throw;
                asc.ReserveRuntimeCapacity(
                    abilityCapacity: 4,
                    attributeCapacity: 4,
                    activeEffectCapacity: 4,
                    tickingAbilityCapacity: 2,
                    dirtyAttributeCapacity: 4,
                    predictedAttributeCapacity: 4,
                    predictionWindowCapacity: 4,
                    coreModifierCapacity: 8,
                    corePredictionCapacity: 4,
                    predictionTransactionRecordCapacity: 8);
                asc.PrewarmRuntimePools(grantedAbilitySpecLists: 2, abilityAppliedEffectLists: 2);

                var attributes = new SmokeTestAttributeSet();
                attributes.Health.SetBaseValue(100f);
                attributes.Health.SetCurrentValue(100f);
                asc.AddAttributeSet(attributes);

                result.InitialChecksum = asc.ComputeReplicatedStateChecksum();
                result.InitialDiagnostics = asc.GetRuntimeDiagnostics();
                if (attributes.Health.OwningSet == null || !asc.ValidateRuntimeIndexes())
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.AttributeRegistrationFailed;
                }

                var damage = CreateAttributeEffect("GAS.SmokeTest.Damage", EDurationPolicy.Instant, attributes.Health, EAttributeModifierOperation.Add, -25f);
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(damage, asc));
                if (Math.Abs(attributes.Health.BaseValue - 75f) > 0.001f)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.InstantEffectFailed;
                }

                var buff = CreateAttributeEffect("GAS.SmokeTest.HealthBuff", EDurationPolicy.HasDuration, attributes.Health, EAttributeModifierOperation.Add, 10f, duration: 5f);
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(buff, asc));
                if (asc.ActiveEffects.Count != 1)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.DurationEffectFailed;
                }

                var stacking = new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    limit: 3,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication);
                var stackEffect = CreateAttributeEffect(
                    "GAS.SmokeTest.StackingBuff",
                    EDurationPolicy.HasDuration,
                    attributes.Health,
                    EAttributeModifierOperation.Add,
                    1f,
                    duration: 3f,
                    stacking: stacking);
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(stackEffect, asc));
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(stackEffect, asc));
                result.StackCount = asc.GetCurrentStackCount(stackEffect);
                if (result.StackCount != 2)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.StackingEffectFailed;
                }

                ActiveGameplayEffect activeStackEffect = null;
                for (int i = 0; i < asc.ActiveEffects.Count; i++)
                {
                    if (ReferenceEquals(asc.ActiveEffects[i].Spec.Def, stackEffect))
                    {
                        activeStackEffect = asc.ActiveEffects[i];
                        break;
                    }
                }

                if (activeStackEffect == null || !asc.TryRemoveActiveEffect(activeStackEffect) || asc.GetCurrentStackCount(stackEffect) != 0)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.ActiveEffectRemovalFailed;
                }

                result.ActiveEffectCountAfterRemove = asc.ActiveEffects.Count;

                var periodic = CreateAttributeEffect(
                    "GAS.SmokeTest.PeriodicDamage",
                    EDurationPolicy.HasDuration,
                    attributes.Health,
                    EAttributeModifierOperation.Add,
                    -5f,
                    duration: 5f,
                    period: 1f);
                var healthBeforePeriodic = attributes.Health.BaseValue;
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(periodic, asc));
                asc.Tick(0.01f, isServer: true);
                result.HealthAfterPeriodicTick = attributes.Health.BaseValue;
                if (Math.Abs(result.HealthAfterPeriodicTick - (healthBeforePeriodic - 5f)) > 0.001f)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.PeriodicEffectFailed;
                }

                var confirmKey = asc.OpenPredictionWindow(null);
                if (!confirmKey.IsValid)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.PredictionConfirmFailed;
                }

                var rejectKey = asc.OpenPredictionWindow(null);
                var healthBeforeRejectedPrediction = attributes.Health.BaseValue;
                using (asc.BeginPredictionScope(rejectKey))
                {
                    var predictedDamage = CreateAttributeEffect("GAS.SmokeTest.PredictedRejectedDamage", EDurationPolicy.Instant, attributes.Health, EAttributeModifierOperation.Add, -7f);
                    asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(predictedDamage, asc));
                }

                if (!rejectKey.IsValid || !asc.RejectPredictionWindow(rejectKey) || asc.HasOpenPredictionWindow(rejectKey))
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.PredictionRejectFailed;
                }

                result.HealthAfterPredictionReject = attributes.Health.BaseValue;
                if (Math.Abs(result.HealthAfterPredictionReject - healthBeforeRejectedPrediction) > 0.001f)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.PredictionAttributeRollbackFailed;
                }

                var healthBeforeConfirmedPrediction = attributes.Health.BaseValue;
                using (asc.BeginPredictionScope(confirmKey))
                {
                    var confirmedDamage = CreateAttributeEffect("GAS.SmokeTest.PredictedConfirmedDamage", EDurationPolicy.Instant, attributes.Health, EAttributeModifierOperation.Add, -3f);
                    asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(confirmedDamage, asc));
                }

                if (!asc.ConfirmPredictionWindow(confirmKey) || asc.HasOpenPredictionWindow(confirmKey))
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.PredictionConfirmFailed;
                }

                result.HealthAfterPredictionConfirm = attributes.Health.BaseValue;
                if (Math.Abs(result.HealthAfterPredictionConfirm - (healthBeforeConfirmedPrediction - 3f)) > 0.001f)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.PredictionAttributeConfirmFailed;
                }

                asc.CapturePendingStateDeltaNonAlloc(delta);
                result.DeltaAttributeCount = delta.AttributeCount;
                result.DeltaActiveEffectCount = delta.ActiveEffectCount;
                result.DeltaGrantedAbilityCount = delta.GrantedAbilityCount;
                if (!delta.HasChanges || delta.StateChecksum == 0u)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.DeltaCaptureFailed;
                }

                asc.CapturePendingStateDeltaNonAlloc(delta);
                result.DeltaHadChangesAfterClear = delta.HasChanges;
                if (result.DeltaHadChangesAfterClear)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.DeltaClearFailed;
                }

                result.FinalDiagnostics = asc.GetRuntimeDiagnostics();
                result.FinalChecksum = result.FinalDiagnostics.ReplicatedStateChecksum;
                result.FinalHealth = attributes.Health.BaseValue;
                result.ActiveEffectCount = asc.ActiveEffects.Count;
                result.RuntimeThreadViolationCount = asc.RuntimeThreadViolationCount;

                if (result.FinalDiagnostics.HasCriticalIssues)
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.RuntimeDiagnosticsFailed;
                }

                if (!asc.ValidateRuntimeIndexes())
                {
                    result.FailureFlags |= GASSmokeTestFailureFlags.RuntimeIndexValidationFailed;
                }

                return result.Passed;
            }
            finally
            {
                asc.Dispose();
            }
        }

        private static GameplayEffect CreateAttributeEffect(
            string name,
            EDurationPolicy durationPolicy,
            GameplayAttribute attribute,
            EAttributeModifierOperation operation,
            float magnitude,
            float duration = 0f,
            float period = 0f,
            GameplayEffectStacking stacking = default,
            bool executePeriodicEffectOnApplication = true)
        {
            var modifiers = new List<ModifierInfo>(1)
            {
                new ModifierInfo(attribute, operation, new ScalableFloat(magnitude))
            };

            return new GameplayEffect(
                name,
                durationPolicy,
                duration,
                period,
                modifiers: modifiers,
                stacking: stacking,
                executePeriodicEffectOnApplication: executePeriodicEffectOnApplication);
        }

        private sealed class SmokeTestAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");
        }
    }
}
