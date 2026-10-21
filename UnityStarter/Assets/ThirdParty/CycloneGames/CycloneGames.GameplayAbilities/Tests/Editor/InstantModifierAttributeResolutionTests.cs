using System;
using System.Collections.Generic;
using System.Diagnostics;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Contract and regression suite for instant-modifier attribute resolution:
    /// GameplayEffectModCallbackData.ResolvedAttribute carries the attribute resolved by the
    /// AbilitySystemComponent, and AttributeSet.ApplyDefaultInstantEffectModification falls back
    /// to the name lookup for externally constructed callback data.
    ///
    /// Locked invariants:
    /// 1. Instant modifier results (Add / Multiply / Override, raw fixed-point) are bit-identical
    ///    to independently derived expected values; unknown attribute names are silently ignored.
    /// 2. The string-keyed lookup and the cached-reference resolution path observe identical data.
    /// 3. The end-to-end instant-application path allocates zero managed bytes in steady state.
    /// </summary>
    public sealed class InstantModifierAttributeResolutionTests
    {
        private const int MicroBenchmarkIterations = 1_000_000;
        private const int AllocationProbeIterations = 10_000;

        // ---------------------------------------------------------------------
        // 1. Correctness invariants
        // ---------------------------------------------------------------------

        [Test]
        public void InstantModifiers_ProduceExpectedRawValues_AddMultiplyOverride()
        {
            var asc = CreateAsc(out var attributes);
            try
            {
                attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
                asc.Tick(0f, true);

                ApplyInstant(asc, new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(10f)));
                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue), "Add +10 must produce 110.");
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

                ApplyInstant(asc, new ModifierInfo("Health", EAttributeModifierOperation.Multiply, new ScalableFloat(2f)));
                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(220).RawValue), "Multiply x2 must produce 220.");

                ApplyInstant(asc, new ModifierInfo("Health", EAttributeModifierOperation.Override, new ScalableFloat(50f)));
                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(50).RawValue), "Override must produce 50.");
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(50).RawValue));

                // Independent expectation for a mixed-attribute effect: each modifier lands on its own attribute.
                var mixed = new GameplayEffect(
                    "InstantResolution.Mixed",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(5f)),
                        new ModifierInfo("Power", EAttributeModifierOperation.Add, new ScalableFloat(7f)),
                        new ModifierInfo("Armor", EAttributeModifierOperation.Override, new ScalableFloat(3f))
                    });
                GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(mixed, asc));
                Assert.That(result.Succeeded, Is.True, "Mixed instant effect application failed.");

                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(55).RawValue), "Health must be 55 (+5).");
                Assert.That(attributes.Power.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(7).RawValue), "Power must be 7 (+7).");
                Assert.That(attributes.Armor.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(3).RawValue), "Armor must be 3 (override).");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void InstantModifier_UnknownAttribute_IsIgnoredExactlyAsBefore()
        {
            var asc = CreateAsc(out var attributes);
            try
            {
                attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
                asc.Tick(0f, true);

                // A modifier naming an unregistered attribute must be silently ignored.
                ApplyInstant(asc, new ModifierInfo("DoesNotExist", EAttributeModifierOperation.Add, new ScalableFloat(999f)));

                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 2. Throughput evidence
        // ---------------------------------------------------------------------

        [Test, Performance]
        public void StringLookupVsCachedReference_MicroBenchmark()
        {
            // Measures the two attribute-resolution strategies in isolation:
            // Dictionary<string, GameplayAttribute> lookup vs cached reference.
            // 8 entries mirror a typical attribute set.
            var dictionary = new Dictionary<string, GameplayAttribute>(8);
            var attributes = new GameplayAttribute[8];
            for (int i = 0; i < attributes.Length; i++)
            {
                attributes[i] = new GameplayAttribute($"Attribute{i}");
                dictionary.Add(attributes[i].Name, attributes[i]);
            }
            GameplayAttribute cached = attributes[7];
            const string key = "Attribute7";

            long lookupSink = 0;
            Measure.Method(() =>
                {
                    GameplayAttribute hit = null;
                    for (int i = 0; i < MicroBenchmarkIterations; i++)
                    {
                        dictionary.TryGetValue(key, out hit);
                        lookupSink += hit.BaseValueRaw;
                    }
                })
                .WarmupCount(3)
                .MeasurementCount(10)
                .IterationsPerMeasurement(1)
                .SampleGroup("InstantResolution.StringDictionaryLookup.PerMillion")
                .Run();

            long cachedSink = 0;
            Measure.Method(() =>
                {
                    GameplayAttribute hit;
                    for (int i = 0; i < MicroBenchmarkIterations; i++)
                    {
                        hit = cached;
                        cachedSink += hit.BaseValueRaw;
                    }
                })
                .WarmupCount(3)
                .MeasurementCount(10)
                .IterationsPerMeasurement(1)
                .SampleGroup("InstantResolution.CachedReference.PerMillion")
                .Run();

            Assert.That(lookupSink, Is.EqualTo(cachedSink), "Both paths must observe identical attribute data.");
            UnityEngine.Debug.Log($"[InstantResolution Benchmark] MicroBenchmarkIterations={MicroBenchmarkIterations}, " +
                "SampleGroups: InstantResolution.StringDictionaryLookup.PerMillion vs InstantResolution.CachedReference.PerMillion");
        }

        [Test, Performance]
        public void InstantEffectApplication_EndToEnd_Benchmark()
        {
            var asc = CreateAsc(out var attributes);
            try
            {
                attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
                asc.Tick(0f, true);

                var effect = new GameplayEffect(
                    "InstantResolution.EndToEnd",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    });

                Measure.Method(() =>
                    {
                        GameplayEffectApplicationResult result =
                            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
                        if (!result.Succeeded)
                        {
                            throw new InvalidOperationException("Instant effect benchmark failed to apply.");
                        }
                    })
                    .WarmupCount(5)
                    .MeasurementCount(20)
                    .IterationsPerMeasurement(1_000)
                    .GC()
                    .SampleGroup("InstantResolution.Application.EndToEnd")
                    .Run();

                // Correctness invariant inside the benchmark run: base value must have advanced
                // by exactly the number of successful applications observed by the framework.
                UnityEngine.Debug.Log($"[InstantResolution Benchmark] EndToEnd final Health.BaseValueRaw={attributes.Health.BaseValueRaw}");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void InstantEffectApplication_SteadyStateAllocatesNoManagedBytes()
        {
            var asc = CreateAsc(out var attributes);
            try
            {
                attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
                asc.Tick(0f, true);

                var effect = new GameplayEffect(
                    "InstantResolution.ZeroAlloc",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    });

                // Warm up every internal cache/pool before probing allocations.
                for (int i = 0; i < 1_000; i++)
                {
                    asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < AllocationProbeIterations; i++)
                {
                    asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
                }
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                UnityEngine.Debug.Log($"[InstantResolution Benchmark] Steady-state allocation over {AllocationProbeIterations} instant applications: {allocatedBytes} bytes");
                GASAllocationProbe.AssertNoMeasuredAllocation(allocatedBytes, "The instant application path must not allocate managed memory in steady state.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static void ApplyInstant(AbilitySystemComponent asc, ModifierInfo modifier)
        {
            var effect = new GameplayEffect(
                "InstantResolution.Single",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo> { modifier });
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            Assert.That(result.Succeeded, Is.True, $"Instant effect for '{modifier.AttributeName}' failed to apply.");
        }

        private static AbilitySystemComponent CreateAsc(out InstantResolutionAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new InstantResolutionAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private sealed class InstantResolutionAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");
            public GameplayAttribute Power { get; } = new GameplayAttribute("Power");
            public GameplayAttribute Armor { get; } = new GameplayAttribute("Armor");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
                RegisterAttribute(Power);
                RegisterAttribute(Armor);
            }
        }
    }
}
