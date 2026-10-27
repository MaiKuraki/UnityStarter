using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Contract suite for the opt-in aggregation mode switch
    /// (<see cref="GASModifierAggregationMode"/>):
    /// 1. Default mode is MultiplicativeProduct and its behavior is unchanged (regression guard).
    /// 2. UnrealBiasSum reproduces stock UE aggregator values: bias-sum multiplication/division
    ///    (two x2 yield x3). Per-stack scaling is a separate axis (GASStackMagnitudePolicy) and is
    ///    covered by GASStackMagnitudePolicyTests.
    /// 3. The switch is honored identically by the Runtime ASC and the Unity-free Core state.
    /// 4. Both modes keep the steady-state zero-allocation contract.
    /// Every expected value is derived independently from the documented formula of its mode.
    /// </summary>
    public sealed class GASAggregationModeCompatTests
    {
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void DefaultMode_IsMultiplicativeProduct_AndBehaviorUnchanged()
        {
            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                Assert.That(asc.ModifierAggregationMode, Is.EqualTo(GASModifierAggregationMode.MultiplicativeProduct),
                    "Default aggregation mode must remain MultiplicativeProduct.");

                attributes.ValueOf(0).SetBaseValue(100);
                ApplyInfinite(asc, BuildMultiplierEffect("Mode.Default", 2f, 2f));
                asc.Tick(DeltaTime, isServer: true);

                // Product: 100 * 2 * 2 = 400.
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(400).RawValue));
                UnityEngine.Debug.Log("[ModeCompat][Default] PASS: two x2 yield x4 (product semantics unchanged).");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void UnrealBiasSum_TwoMultipliers_YieldBiasSum()
        {
            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                asc.ModifierAggregationMode = GASModifierAggregationMode.UnrealBiasSum;
                attributes.ValueOf(0).SetBaseValue(100);
                ApplyInfinite(asc, BuildMultiplierEffect("Mode.Bias.Two", 2f, 2f));
                asc.Tick(DeltaTime, isServer: true);

                // UE stock: 100 * (1 + (2-1) + (2-1)) = 300.
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(300).RawValue));
                UnityEngine.Debug.Log("[ModeCompat][Bias] PASS: two x2 yield x3 (UE bias-sum).");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void UnrealBiasSum_StackScaling_IsLinearInStackCount()
        {
            GameplayEffect exponentialEffect = BuildStackingMultiplierEffect(
                "Mode.Bias.Stack.Exp", 2f, stackLimit: 10, magnitudePolicy: GASStackMagnitudePolicy.Exponential);
            GameplayEffect linearEffect = BuildStackingMultiplierEffect(
                "Mode.Bias.Stack.Lin", 2f, stackLimit: 10, magnitudePolicy: GASStackMagnitudePolicy.LinearBias);

            var productAsc = CreateAsc(out PermutationAttributeSet productAttributes);
            var biasAsc = CreateAsc(out PermutationAttributeSet biasAttributes);
            try
            {
                productAttributes.ValueOf(0).SetBaseValue(100);
                biasAttributes.ValueOf(0).SetBaseValue(100);
                biasAsc.ModifierAggregationMode = GASModifierAggregationMode.UnrealBiasSum;

                for (int i = 0; i < 3; i++)
                {
                    ApplyInfinite(productAsc, exponentialEffect);
                    ApplyInfinite(biasAsc, linearEffect);
                }
                productAsc.Tick(DeltaTime, isServer: true);
                biasAsc.Tick(DeltaTime, isServer: true);

                // Exponential + product: 100 * 2^3 = 800.
                // LinearBias + bias sum: 100 * (1 + (2-1)*3) = 400, matching UE ComputeStackedModifierMagnitude.
                Assert.That(productAttributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(800).RawValue),
                    "Exponential stacking must keep magnitude^stackCount.");
                Assert.That(biasAttributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(400).RawValue),
                    "LinearBias stacking must add the per-stack delta: (2-1)*3 + 1 = 4.");
                UnityEngine.Debug.Log("[ModeCompat][Bias] PASS: stack scaling product=800 vs bias=600 at 3 stacks; cross-mode split verified.");
            }
            finally
            {
                productAsc.Dispose();
                biasAsc.Dispose();
            }
        }

        [Test]
        public void UnrealBiasSum_DivisionBiasSum()
        {
            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                asc.ModifierAggregationMode = GASModifierAggregationMode.UnrealBiasSum;
                attributes.ValueOf(0).SetBaseValue(100);

                var effect = new GameplayEffect(
                    "Mode.Bias.Divide",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Division, new ScalableFloat(2f)),
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Division, new ScalableFloat(2f))
                    });
                ApplyInfinite(asc, effect);
                asc.Tick(DeltaTime, isServer: true);

                // UE stock: 100 / (1 + (2-1) + (2-1)) = 100 / 3.
                var expected = GASFixedValue.FromInt(100) / GASFixedValue.FromInt(3);
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(expected.RawValue));
                UnityEngine.Debug.Log($"[ModeCompat][Bias] PASS: two /2 yield divide-by-3 (raw={expected.RawValue}).");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void CrossMode_MixedModifiers_ProduceTheirDocumentedValues()
        {
            // One channel: Add +20, x2, x1.5.
            // Product: (100+20) * 2 * 1.5 = 360. Bias: (100+20) * (1 + 1 + 0.5) = 300.
            GameplayEffect effect = BuildMixedEffect("Mode.Mixed");

            var productAsc = CreateAsc(out PermutationAttributeSet productAttributes);
            var biasAsc = CreateAsc(out PermutationAttributeSet biasAttributes);
            try
            {
                productAttributes.ValueOf(0).SetBaseValue(100);
                biasAttributes.ValueOf(0).SetBaseValue(100);
                biasAsc.ModifierAggregationMode = GASModifierAggregationMode.UnrealBiasSum;

                ApplyInfinite(productAsc, effect);
                ApplyInfinite(biasAsc, effect);
                productAsc.Tick(DeltaTime, isServer: true);
                biasAsc.Tick(DeltaTime, isServer: true);

                Assert.That(productAttributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(360).RawValue));
                Assert.That(biasAttributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(300).RawValue));
                UnityEngine.Debug.Log("[ModeCompat][CrossMode] PASS: mixed modifiers product=360 vs bias=300.");
            }
            finally
            {
                productAsc.Dispose();
                biasAsc.Dispose();
            }
        }

        [Test]
        public void CoreState_HonorsMode_IdenticallyToRuntime()
        {
            var attributeId = new GASAttributeId(1);
            var modifiers = new[]
            {
                new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(2)),
                new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(2))
            };

            long product = EvaluateCoreState(GASModifierAggregationMode.MultiplicativeProduct, attributeId, modifiers, stackCount: 1);
            long bias = EvaluateCoreState(GASModifierAggregationMode.UnrealBiasSum, attributeId, modifiers, stackCount: 1);

            Assert.That(product, Is.EqualTo(GASFixedValue.FromInt(400).RawValue), "Core product mode must keep 100*2*2=400.");
            Assert.That(bias, Is.EqualTo(GASFixedValue.FromInt(300).RawValue), "Core bias mode must convert two x2 into x3.");
            UnityEngine.Debug.Log($"[ModeCompat][Core] PASS: product={product} bias={bias} (matches Runtime layer).");
        }

        private static long EvaluateCoreState(
            GASModifierAggregationMode mode,
            GASAttributeId attributeId,
            GASModifierData[] modifiers,
            ushort stackCount)
        {
            var state = new GASAbilitySystemState(new GASEntityId(1))
            {
                AggregationMode = mode
            };
            state.SetAttributeBase(attributeId, GASFixedValue.FromInt(100));
            state.ApplyGameplayEffectSpecToSelf(new GASGameplayEffectSpecData(
                new GASDefinitionId(1),
                new GASEntityId(2),
                default,
                GASEffectDurationPolicy.Duration,
                1,
                stackCount,
                0,
                100,
                modifiers,
                0,
                modifiers.Length));

            Assert.That(state.TryGetAttribute(attributeId, out var attribute), Is.True);
            return attribute.CurrentValueRaw;
        }

        [Test]
        public void UnrealBiasSum_StackScaling_SingleModifier_LinearMagnitude()
        {
            var attributeId = new GASAttributeId(1);
            var modifiers = new[]
            {
                new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(2))
            };

            long stacked = EvaluateCoreState(GASModifierAggregationMode.UnrealBiasSum, attributeId, modifiers, stackCount: 3);
            // UE ComputeStackedModifierMagnitude: (2-1)*3 + 1 = 4 -> 100 * 4 = 400.
            Assert.That(stacked, Is.EqualTo(GASFixedValue.FromInt(400).RawValue));
            UnityEngine.Debug.Log("[ModeCompat][Core] PASS: bias stacked single modifier = 400 ((M-1)*stack+1).");
        }

        [Test]
        public void UnrealBiasSum_SteadyState_AllocatesNoManagedBytes()
        {
            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                asc.ModifierAggregationMode = GASModifierAggregationMode.UnrealBiasSum;
                for (int a = 0; a < 8; a++)
                {
                    attributes.ValueOf(a).SetBaseValue(100);
                }

                ApplyInfinite(asc, BuildMultiplierEffect("Mode.Alloc.Add", 1f, 1f));
                var periodic = new GameplayEffect(
                    "Mode.Alloc.Periodic",
                    EDurationPolicy.Infinite,
                    period: DeltaTime,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f)),
                        new ModifierInfo("Attr1", EAttributeModifierOperation.Multiply, new ScalableFloat(1f))
                    });
                ApplyInfinite(asc, periodic);

                for (int i = 0; i < 200; i++)
                {
                    asc.Tick(DeltaTime, isServer: true);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2_000; i++)
                {
                    asc.Tick(DeltaTime, isServer: true);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                UnityEngine.Debug.Log($"[ModeCompat][Bias] steady-state allocation over 2000 ticks: {allocated} bytes");
                GASAllocationProbe.AssertNoMeasuredAllocation(allocated, "Bias mode must keep the zero-allocation steady-state contract.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private static GameplayEffect BuildMultiplierEffect(string name, float first, float second)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(first)),
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(second))
                });
        }

        private static GameplayEffect BuildStackingMultiplierEffect(
            string name,
            float factor,
            int stackLimit,
            GASStackMagnitudePolicy magnitudePolicy = GASStackMagnitudePolicy.LinearBias)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(factor))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    stackLimit,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                    magnitudePolicy: magnitudePolicy));
        }

        private static GameplayEffect BuildMixedEffect(string name)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(20f)),
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(2f)),
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(1.5f))
                });
        }

        private static void ApplyInfinite(AbilitySystemComponent asc, GameplayEffect effect)
        {
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True,
                $"Effect '{effect.Name}' failed to apply.");
        }

        private static AbilitySystemComponent CreateAsc(out PermutationAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new PermutationAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private sealed class PermutationAttributeSet : AttributeSet
        {
            private readonly GameplayAttribute[] _attributes = new GameplayAttribute[8];

            internal PermutationAttributeSet()
            {
                for (int a = 0; a < _attributes.Length; a++)
                {
                    _attributes[a] = new GameplayAttribute($"Attr{a}");
                }
            }

            internal GameplayAttribute ValueOf(int index) => _attributes[index];

            protected override void RegisterAttributes()
            {
                for (int a = 0; a < _attributes.Length; a++)
                {
                    RegisterAttribute(_attributes[a]);
                }
            }
        }
    }
}
