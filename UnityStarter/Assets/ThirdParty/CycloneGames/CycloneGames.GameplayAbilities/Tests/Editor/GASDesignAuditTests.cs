using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Multi-directional design audit suite covering complexity scaling, cross-instance determinism,
    /// steady-state allocation contracts, overflow-effect depth behavior, and the thread-affinity
    /// contract. Editor/Mono measurements are indicative; Player-build confirmation is separate.
    /// </summary>
    public sealed class GASDesignAuditTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int AttributeCount = 8;

        // ---------------------------------------------------------------------
        // 1. Algorithmic complexity: aggregation cost per character must not grow
        //    superlinearly across populations.
        // ---------------------------------------------------------------------

        [Test]
        public void AggregationComplexity_ScalesLinearlyWithCharacterCount()
        {
            int[] populations = { 125, 250, 500, 1000 };
            const int warmupTicks = 30;
            const int measuredTicks = 120;
            var perCharacterMs = new double[populations.Length];

            for (int v = 0; v < populations.Length; v++)
            {
                List<AuditCharacter> characters = BuildPopulation(populations[v]);
                for (int t = 0; t < warmupTicks; t++)
                {
                    TickAll(characters);
                }

                GASAggregationProbe.Reset();
                for (int t = 0; t < measuredTicks; t++)
                {
                    TickAll(characters);
                }

                double totalMs = GASAggregationProbe.AggregationTimestampTicks * 1000.0 / Stopwatch.Frequency;
                perCharacterMs[v] = totalMs / populations[v];
                Assert.That(GASAggregationProbe.ModifierHits, Is.GreaterThan(0L), "Aggregation must perform real work.");
                Assert.That(GASAggregationProbe.ModifierVisits, Is.EqualTo(GASAggregationProbe.ModifierHits),
                    "Aggregation must not visit modifiers that do not target the dirty attribute.");

                UnityEngine.Debug.Log(
                    $"[DesignAudit][Scaling] characters={populations[v]} ticks={measuredTicks} " +
                    $"aggregation={totalMs:F1}ms perChar={perCharacterMs[v]:F4}ms");
                DisposeAll(characters);
            }

            double growth = perCharacterMs[^1] / perCharacterMs[0];
            UnityEngine.Debug.Log($"[DesignAudit][Scaling] per-character cost growth 125->1000 chars: {growth:F2}x (linear = 1.0x)");
            Assert.That(growth, Is.LessThan(2.0), "Per-character aggregation cost grew superlinearly.");
            Assert.That(growth, Is.GreaterThan(0.3), "Per-character cost collapsed unexpectedly; measurement invalid.");
        }

        // ---------------------------------------------------------------------
        // 2. Determinism: two independent instances driven by the same scripted
        //    sequence must end bit-identical, including the state checksum.
        // ---------------------------------------------------------------------

        [Test]
        public void Determinism_TwoIndependentInstances_BitIdenticalAfterScriptedTicks()
        {
            const int scriptedTicks = 120;
            var first = new AuditCharacter(0);
            var second = new AuditCharacter(0); // Identical definition names: checksum hashes def names.
            try
            {
                ulong seedA = 0x9E3779B97F4A7C15UL;
                ulong seedB = 0x9E3779B97F4A7C15UL;
                var extraEffect = BuildAddEffect("Audit.Random", 7);

                for (int t = 0; t < scriptedTicks; t++)
                {
                    // Identical deterministic LCG on both instances decides apply/remove actions.
                    StepLcg(ref seedA, out uint actionA);
                    StepLcg(ref seedB, out uint actionB);
                    Assert.That(actionA, Is.EqualTo(actionB), "LCG streams diverged; test harness broken.");

                    if (actionA % 5 == 0)
                    {
                        GameplayEffectApplicationResult ra = first.Asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(extraEffect, first.Asc));
                        GameplayEffectApplicationResult rb = second.Asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(extraEffect, second.Asc));
                        Assert.That(ra.Succeeded, Is.EqualTo(rb.Succeeded), "Apply outcomes diverged.");
                        if (ra.ActiveEffect != null && rb.ActiveEffect != null)
                        {
                            first.PushRemoval(ra.ActiveEffect);
                            second.PushRemoval(rb.ActiveEffect);
                        }
                    }
                    else if (actionA % 5 == 1)
                    {
                        bool removedA = first.RemoveLastOwned();
                        bool removedB = second.RemoveLastOwned();
                        Assert.That(removedB, Is.EqualTo(removedA), "Removal availability diverged.");
                    }

                    first.Asc.Tick(DeltaTime, isServer: true);
                    second.Asc.Tick(DeltaTime, isServer: true);

                    if (t % 30 == 0)
                    {
                        Assert.That(first.Asc.ComputeReplicatedStateChecksum(),
                            Is.EqualTo(second.Asc.ComputeReplicatedStateChecksum()),
                            $"Replicated state checksum diverged at tick {t}.");
                    }
                }

                Assert.That(first.Asc.ActiveEffects.Count, Is.EqualTo(second.Asc.ActiveEffects.Count));
                for (int a = 0; a < AttributeCount; a++)
                {
                    GameplayAttribute attributeA = first.Attributes.ValueOf(a);
                    GameplayAttribute attributeB = second.Attributes.ValueOf(a);
                    Assert.That(attributeB.BaseValueRaw, Is.EqualTo(attributeA.BaseValueRaw), $"Attr{a} BaseValueRaw diverged.");
                    Assert.That(attributeB.CurrentValueRaw, Is.EqualTo(attributeA.CurrentValueRaw), $"Attr{a} CurrentValueRaw diverged.");
                }

                Assert.That(first.Asc.ComputeReplicatedStateChecksum(),
                    Is.EqualTo(second.Asc.ComputeReplicatedStateChecksum()),
                    "Final replicated state checksums diverged.");
                UnityEngine.Debug.Log("[DesignAudit][Determinism] PASS: 120 scripted ticks, bit-identical attributes and checksum.");
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 3. Allocation contracts: steady-state tick and checksum computation.
        // ---------------------------------------------------------------------

        [Test]
        public void SteadyState_TickAndChecksum_AllocateNoManagedBytes()
        {
            const int probeIterations = 2_000;
            List<AuditCharacter> characters = BuildPopulation(50);
            try
            {
                // (a) Tick with active duration effects + periodic executions.
                for (int t = 0; t < 200; t++)
                {
                    TickAll(characters);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 0; t < probeIterations; t++)
                {
                    TickAll(characters);
                }
                long tickBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                // (b) Replicated state checksum, after scratch capacity warmup.
                AbilitySystemComponent asc = characters[0].Asc;
                for (int i = 0; i < 200; i++)
                {
                    asc.ComputeReplicatedStateChecksum();
                }

                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < probeIterations; i++)
                {
                    asc.ComputeReplicatedStateChecksum();
                }
                long checksumBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                UnityEngine.Debug.Log(
                    $"[DesignAudit][Allocation] tick x{probeIterations} on 50 characters: {tickBytes} bytes; " +
                    $"checksum x{probeIterations}: {checksumBytes} bytes");
                Assert.Zero(tickBytes, "Steady-state tick must not allocate managed memory.");
                Assert.Zero(checksumBytes, "Steady-state checksum computation must not allocate managed memory.");
            }
            finally
            {
                DisposeAll(characters);
            }
        }

        // ---------------------------------------------------------------------
        // 4. Overflow effects: a finite non-cyclic overflow chain resolves fully
        //    through nested application without recursion faults.
        // ---------------------------------------------------------------------

        [Test]
        public void OverflowEffects_FiniteChain_AppliesCompletely()
        {
            const int chainLength = 50;
            var chain = new GameplayEffect[chainLength];
            GameplayEffect next = null;
            for (int i = chainLength - 1; i >= 0; i--)
            {
                next = new GameplayEffect(
                    $"Audit.Overflow.{i}",
                    EDurationPolicy.HasDuration,
                    duration: 60f,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo($"Attr{i % AttributeCount}", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    },
                    stacking: new GameplayEffectStacking(
                        EGameplayEffectStackingType.AggregateByTarget,
                        1,
                        EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication),
                    overflowEffects: next != null ? new List<GameplayEffect> { next } : null);
                chain[i] = next;
            }

            GameplayEffect head = chain[0];
            var character = new AuditCharacter(0, withBaseEffects: false);
            try
            {
                // Create every link first; none overflows on its first application.
                for (int i = 0; i < chainLength; i++)
                {
                    ApplyOnce(character.Asc, chain[i]);
                }
                Assert.That(character.Asc.ActiveEffects.Count, Is.EqualTo(chainLength),
                    "All chain links must be active before the cascade probe.");

                // Re-applying the head at its stack limit cascades through the entire chain,
                // resolving nested overflow application one level per link.
                ApplyOnce(character.Asc, head);

                UnityEngine.Debug.Log(
                    $"[DesignAudit][Overflow] finite chain of {chainLength}: cascade resolved, active effects = {character.Asc.ActiveEffects.Count}");
                Assert.That(character.Asc.ActiveEffects.Count, Is.EqualTo(chainLength),
                    "The finite overflow chain must resolve without duplicating or dropping links.");
            }
            finally
            {
                character.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 5. Thread affinity: with the Throw policy enabled, cross-thread access
        //    is rejected and counted; the owning thread remains usable.
        // ---------------------------------------------------------------------

        [Test]
        public void ThreadAffinity_CrossThreadTick_IsRejectedAndCounted()
        {
            // Phase 1: default configuration. The runtime context defaults to the Throw policy,
            // so cross-thread access is rejected out of the box.
            var defaultAsc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                defaultAsc.Tick(DeltaTime, isServer: true);
                Exception defaultCaptured = RunCrossThreadTick(defaultAsc);
                Assert.That(defaultCaptured, Is.InstanceOf<InvalidOperationException>(),
                    "Default configuration must reject cross-thread access.");
                UnityEngine.Debug.Log($"[DesignAudit][Thread] default policy rejection: {defaultCaptured.Message}");
            }
            finally
            {
                defaultAsc.Dispose();
            }

            // Phase 2: context checks disabled, ASC-level Throw policy must count the violation.
            var context = new GASRuntimeContext(threadPolicy: GASRuntimeThreadPolicy.Disabled);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                asc.RuntimeThreadPolicy = GASRuntimeThreadPolicy.Throw;
                asc.Tick(DeltaTime, isServer: true); // Register the owning thread.

                Exception captured = RunCrossThreadTick(asc);
                Assert.That(captured, Is.InstanceOf<InvalidOperationException>(),
                    "Cross-thread access must be rejected with InvalidOperationException under the Throw policy.");
                Assert.That(asc.RuntimeThreadViolationCount, Is.EqualTo(1L), "The violation must be counted exactly once.");

                // The owning thread must remain fully usable.
                asc.Tick(DeltaTime, isServer: true);
                UnityEngine.Debug.Log("[DesignAudit][Thread] PASS: default rejection + ASC-level violation counting; owner thread unaffected.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        private static Exception RunCrossThreadTick(AbilitySystemComponent asc)
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    asc.Tick(DeltaTime, isServer: true);
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });
            thread.Start();
            Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True, "Cross-thread tick probe timed out.");
            return captured;
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private static void StepLcg(ref ulong seed, out uint action)
        {
            seed = unchecked(seed * 6364136223846793005UL + 1442695040888963407UL);
            action = (uint)(seed >> 33);
        }

        private static void ApplyOnce(AbilitySystemComponent asc, GameplayEffect effect)
        {
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            Assert.That(result.Succeeded, Is.True, $"Effect '{effect.Name}' failed to apply.");
        }

        private static List<ModifierInfo> BuildModifiers(int magnitude)
        {
            var modifiers = new List<ModifierInfo>(AttributeCount);
            for (int a = 0; a < AttributeCount; a++)
            {
                modifiers.Add(new ModifierInfo($"Attr{a}", EAttributeModifierOperation.Add, new ScalableFloat(magnitude)));
            }
            return modifiers;
        }

        private static GameplayEffect BuildAddEffect(string name, int magnitude)
        {
            return new GameplayEffect(name, EDurationPolicy.Infinite, modifiers: BuildModifiers(magnitude));
        }

        private static List<AuditCharacter> BuildPopulation(int count)
        {
            var characters = new List<AuditCharacter>(count);
            for (int i = 0; i < count; i++)
            {
                characters.Add(new AuditCharacter(i));
            }
            return characters;
        }

        private static void TickAll(List<AuditCharacter> characters)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                characters[i].Asc.Tick(DeltaTime, isServer: true);
            }
        }

        private static void DisposeAll(List<AuditCharacter> characters)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                characters[i].Dispose();
            }
        }

        private sealed class AuditCharacter : IDisposable
        {
            internal readonly AbilitySystemComponent Asc;
            internal readonly AuditAttributeSet Attributes;
            private readonly List<ActiveGameplayEffect> pendingRemovals = new List<ActiveGameplayEffect>();

            internal AuditCharacter(int index, bool withBaseEffects = true)
            {
                Asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
                Attributes = new AuditAttributeSet();
                Asc.AddAttributeSet(Attributes);
                for (int a = 0; a < AttributeCount; a++)
                {
                    Attributes.SetBaseValue(Attributes.ValueOf(a), GASFixedValue.FromInt(100));
                }

                if (!withBaseEffects)
                {
                    return;
                }

                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.a", 1));
                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.b", 1));
                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.c", 1));
                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.d", 1));
                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.e", 1));
                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.f", 1));
                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.g", 1));
                ApplyOnce(Asc, BuildAddEffect($"Audit.Add.{index}.h", 1));

                var periodic = new GameplayEffect(
                    $"Audit.Periodic.{index}",
                    EDurationPolicy.Infinite,
                    period: DeltaTime,
                    modifiers: BuildModifiers(1));
                ApplyOnce(Asc, periodic);
            }

            internal void PushRemoval(ActiveGameplayEffect effect)
            {
                pendingRemovals.Add(effect);
            }

            internal bool RemoveLastOwned()
            {
                if (pendingRemovals.Count == 0)
                {
                    return false;
                }

                ActiveGameplayEffect effect = pendingRemovals[^1];
                pendingRemovals.RemoveAt(pendingRemovals.Count - 1);
                return Asc.TryRemoveActiveEffect(effect);
            }

            public void Dispose()
            {
                Asc.Dispose();
            }
        }

        private sealed class AuditAttributeSet : AttributeSet
        {
            private readonly GameplayAttribute[] _attributes = new GameplayAttribute[AttributeCount];

            internal AuditAttributeSet()
            {
                for (int a = 0; a < AttributeCount; a++)
                {
                    _attributes[a] = new GameplayAttribute($"Attr{a}");
                }
            }

            internal GameplayAttribute ValueOf(int index) => _attributes[index];

            protected override void RegisterAttributes()
            {
                for (int a = 0; a < AttributeCount; a++)
                {
                    RegisterAttribute(_attributes[a]);
                }
            }
        }
    }
}
