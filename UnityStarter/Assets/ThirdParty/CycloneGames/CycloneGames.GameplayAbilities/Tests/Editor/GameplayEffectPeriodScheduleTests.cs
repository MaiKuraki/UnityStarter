using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayEffectPeriodScheduleTests
    {
        [Test]
        public void Compute_WithoutPeriod_IsNotPeriodic()
        {
            GameplayEffectPeriodSchedule schedule = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 5f, 0f, false);

            Assert.That(schedule.IsPeriodic, Is.False);
            Assert.That(schedule.ExecutionCount, Is.EqualTo(0));
            Assert.That(schedule.GetExecutionTime(0), Is.EqualTo(-1f));
        }

        [Test]
        public void Compute_InstantPolicy_IsNotPeriodic()
        {
            GameplayEffectPeriodSchedule schedule = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.Instant, 0f, 1f, true);

            Assert.That(schedule.IsPeriodic, Is.False);
        }

        [Test]
        public void Compute_AlignedDuration_IncludesBoundaryExecution()
        {
            GameplayEffectPeriodSchedule schedule = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 4f, 1f, false);

            Assert.That(schedule.ExecutionCount, Is.EqualTo(4));
            Assert.That(schedule.FirstExecutionTime, Is.EqualTo(1f));
            Assert.That(schedule.LastExecutionTime, Is.EqualTo(4f));
            Assert.That(schedule.EndsOnDurationBoundary, Is.True);
            Assert.That(schedule.UntickedTail, Is.EqualTo(0f));
        }

        [Test]
        public void Compute_AlignedDuration_AddsImmediateExecution()
        {
            GameplayEffectPeriodSchedule schedule = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 5f, 1f, true);

            Assert.That(schedule.ExecutionCount, Is.EqualTo(6));
            Assert.That(schedule.FirstExecutionTime, Is.EqualTo(0f));
            Assert.That(schedule.LastExecutionTime, Is.EqualTo(5f));
            Assert.That(schedule.EndsOnDurationBoundary, Is.True);
        }

        [Test]
        public void Compute_UnalignedDuration_ReportsTail()
        {
            GameplayEffectPeriodSchedule schedule = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 5.5f, 1f, false);

            Assert.That(schedule.ExecutionCount, Is.EqualTo(5));
            Assert.That(schedule.LastExecutionTime, Is.EqualTo(5f));
            Assert.That(schedule.EndsOnDurationBoundary, Is.False);
            Assert.That(schedule.UntickedTail, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Compute_InfiniteDuration_IsUnbounded()
        {
            GameplayEffectPeriodSchedule schedule = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.Infinite, 0f, 1f, false);

            Assert.That(schedule.IsPeriodic, Is.True);
            Assert.That(schedule.IsUnbounded, Is.True);
            Assert.That(schedule.ExecutionCount, Is.EqualTo(-1));
            Assert.That(schedule.FirstExecutionTime, Is.EqualTo(1f));
            Assert.That(schedule.LastExecutionTime, Is.EqualTo(-1f));
        }

        [Test]
        public void Compute_PeriodLongerThanDuration_ReportsNeverOrSingleExecution()
        {
            GameplayEffectPeriodSchedule withoutImmediate = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 0.5f, 1f, false);
            Assert.That(withoutImmediate.ExecutionCount, Is.EqualTo(0));

            GameplayEffectPeriodSchedule withImmediate = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 0.5f, 1f, true);
            Assert.That(withImmediate.ExecutionCount, Is.EqualTo(1));
            Assert.That(withImmediate.FirstExecutionTime, Is.EqualTo(0f));
            Assert.That(withImmediate.LastExecutionTime, Is.EqualTo(0f));
        }

        [Test]
        public void GetExecutionTime_FollowsThePublishedSchedule()
        {
            GameplayEffectPeriodSchedule immediate = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 4f, 1f, true);
            Assert.That(immediate.GetExecutionTime(0), Is.EqualTo(0f));
            Assert.That(immediate.GetExecutionTime(1), Is.EqualTo(1f));
            Assert.That(immediate.GetExecutionTime(4), Is.EqualTo(4f));
            Assert.That(immediate.GetExecutionTime(5), Is.EqualTo(-1f));

            GameplayEffectPeriodSchedule deferred = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, 4f, 1f, false);
            Assert.That(deferred.GetExecutionTime(0), Is.EqualTo(1f));
            Assert.That(deferred.GetExecutionTime(3), Is.EqualTo(4f));
            Assert.That(deferred.GetExecutionTime(4), Is.EqualTo(-1f));
        }

        [TestCase(4f, 1f, false, 1f, 4, 4)]
        [TestCase(4f, 1f, true, 1f, 4, 5)]
        [TestCase(5.5f, 1f, false, 0.5f, 11, 5)]
        public void Compute_MatchesRuntimeExecutionCount(
            float duration,
            float period,
            bool executeOnApplication,
            float deltaTime,
            int ticks,
            int expectedCount)
        {
            GameplayEffectPeriodSchedule schedule = GameplayEffectPeriodSchedule.Compute(
                EDurationPolicy.HasDuration, duration, period, executeOnApplication);

            Assert.That(schedule.ExecutionCount, Is.EqualTo(expectedCount));

            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            var effect = new GameplayEffect(
                "ScheduleDriftProbe",
                EDurationPolicy.HasDuration,
                duration,
                period,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                },
                executePeriodicEffectOnApplication: executeOnApplication);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));

            for (int i = 0; i < ticks; i++)
            {
                asc.Tick(deltaTime, true);
            }

            Assert.That(attributes.Health.BaseValueRaw,
                Is.EqualTo(GASFixedValue.FromInt(100 + expectedCount).RawValue));
            Assert.That(asc.ActiveEffects, Is.Empty);

            asc.Dispose();
        }

        private sealed class HealthAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
            }
        }
    }
}
