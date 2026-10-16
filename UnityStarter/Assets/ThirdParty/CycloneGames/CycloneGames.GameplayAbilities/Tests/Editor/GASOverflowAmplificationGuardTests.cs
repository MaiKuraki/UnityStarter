using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Contract suite for overflow-chain hardening:
    /// 1. Duplicate overflow entries are rejected at definition construction (each duplicate
    ///    multiplied the fan-out, producing 2^N applications for N duplicated chain levels).
    /// 2. Single-entry chains stay linear and complete.
    /// 3. The runtime overflow application depth guard caps pathological chain depth with a
    ///    controlled refusal instead of unbounded nesting.
    /// </summary>
    public sealed class GASOverflowAmplificationGuardTests
    {
        [Test]
        public void DuplicateOverflowEntries_AreRejectedAtConstruction()
        {
            var child = new GameplayEffect("OverflowGuard.Child", EDurationPolicy.Infinite);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => new GameplayEffect(
                "OverflowGuard.Duplicate",
                EDurationPolicy.Infinite,
                overflowEffects: new List<GameplayEffect> { child, child }));

            Assert.That(exception.Message, Does.Contain("duplicates"),
                "The rejection message must identify the duplicate entry.");
            UnityEngine.Debug.Log($"[OverflowGuard][Dedupe] PASS: duplicate overflow entries rejected at construction: {exception.Message}");
        }

        [Test]
        public void SingleEntryOverflowChain_RemainsLinearAndCompletes()
        {
            const int chainLength = 16;
            GameplayEffect head = BuildChain(chainLength, duplicateEntries: false);
            var asc = CreateAsc();
            try
            {
                ArmChain(asc, head, chainLength);
                GASOverflowProbe.Reset();
                ApplyOnce(asc, head);

                UnityEngine.Debug.Log(
                    $"[OverflowGuard][Linear] single-entry chain N={chainLength}: applications={GASOverflowProbe.OverflowApplications}, " +
                    $"maxDepth={GASOverflowProbe.MaxDepth}, refusals={GASOverflowProbe.DepthLimitRefusals}");
                Assert.That(GASOverflowProbe.OverflowApplications, Is.EqualTo(chainLength - 1),
                    "A single-entry chain must stay linear (chain length - 1 applications).");
                Assert.That(GASOverflowProbe.MaxDepth, Is.EqualTo(chainLength - 1));
                Assert.That(GASOverflowProbe.DepthLimitRefusals, Is.Zero);
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void OverflowDepthGuard_CapsPathologicalDeepChain_AndSystemStaysHealthy()
        {
            const int chainLength = 300; // Beyond the 256 depth guard.
            GameplayEffect head = BuildChain(chainLength, duplicateEntries: false);
            var asc = CreateAsc();
            try
            {
                ArmChain(asc, head, chainLength);
                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(chainLength), "Arming must create every link.");

                GASOverflowProbe.Reset();
                ApplyOnce(asc, head); // Cascades through the entire armed chain.

                UnityEngine.Debug.Log(
                    $"[OverflowGuard][DepthCap] chain N={chainLength}: applications={GASOverflowProbe.OverflowApplications}, " +
                    $"maxDepth={GASOverflowProbe.MaxDepth}, refusals={GASOverflowProbe.DepthLimitRefusals}");

                Assert.That(GASOverflowProbe.MaxDepth, Is.LessThanOrEqualTo(256),
                    "Nested overflow depth must be capped at the guard limit.");
                Assert.That(GASOverflowProbe.OverflowApplications, Is.LessThanOrEqualTo(256),
                    "Application fan-out must be bounded by the depth guard.");
                Assert.That(GASOverflowProbe.DepthLimitRefusals, Is.GreaterThanOrEqualTo(1L),
                    "The guard must record at least one refusal on a pathologically deep chain.");

                // The system must remain fully usable after the controlled refusal.
                var instant = new GameplayEffect(
                    "OverflowGuard.AfterCap",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    });
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(instant, asc)).Succeeded, Is.True,
                    "The system must remain usable after the depth guard fires.");
                asc.Tick(1f / 60f, isServer: true);
            }
            finally
            {
                asc.Dispose();
            }
        }

        private static GameplayEffect BuildChain(int length, bool duplicateEntries)
        {
            GameplayEffect[] chain = new GameplayEffect[length];
            GameplayEffect next = null;
            for (int i = length - 1; i >= 0; i--)
            {
                List<GameplayEffect> overflow = null;
                if (next != null)
                {
                    overflow = duplicateEntries
                        ? new List<GameplayEffect> { next, next }
                        : new List<GameplayEffect> { next };
                }

                next = new GameplayEffect(
                    $"OverflowGuard.Link.{i}.{(duplicateEntries ? "dup" : "single")}",
                    EDurationPolicy.HasDuration,
                    duration: 60f,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    },
                    stacking: new GameplayEffectStacking(
                        EGameplayEffectStackingType.AggregateByTarget,
                        1,
                        EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication),
                    overflowEffects: overflow);
                chain[i] = next;
            }

            return chain[0];
        }

        private static void ArmChain(AbilitySystemComponent asc, GameplayEffect head, int length)
        {
            GameplayEffect link = head;
            for (int i = 0; i < length; i++)
            {
                ApplyOnce(asc, link);
                link = link.OverflowEffects.Count > 0 ? link.OverflowEffects[0] : null;
            }
        }

        private static void ApplyOnce(AbilitySystemComponent asc, GameplayEffect effect)
        {
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            Assert.That(result.Succeeded, Is.True, $"Effect '{effect.Name}' failed to apply.");
        }

        private static AbilitySystemComponent CreateAsc()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new GuardAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.ValueOf(0).SetBaseValue(100f);
            return asc;
        }

        private sealed class GuardAttributeSet : AttributeSet
        {
            private readonly GameplayAttribute _attr0 = new GameplayAttribute("Attr0");

            internal GameplayAttribute ValueOf(int index) => _attr0;

            protected override void RegisterAttributes()
            {
                RegisterAttribute(_attr0);
            }
        }
    }
}
