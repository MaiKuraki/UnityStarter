using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Contract suite for the stack-magnitude axis. Stacking is independent of the ASC-wide
    /// aggregation mode: <see cref="GASStackMagnitudePolicy"/> decides how one effect's stack count
    /// scales its Multiply/Division magnitudes, while <see cref="GASModifierAggregationMode"/>
    /// decides how several modifiers of the same channel combine.
    ///
    /// Every expected value is derived from the documented formula, not from a recorded run:
    ///   LinearBias  : (magnitude - 1) * stackCount + 1   (identical to UE ComputeStackedModifierMagnitude)
    ///   Exponential : magnitude ^ stackCount
    /// </summary>
    public sealed class GASStackMagnitudePolicyTests
    {
        private const float DeltaTime = 1f / 60f;

        [TestCase(2f, 1, 200f)]
        [TestCase(2f, 2, 300f)]
        [TestCase(2f, 3, 400f)]
        [TestCase(2f, 5, 600f)]
        [TestCase(1.5f, 2, 200f)]
        [TestCase(1.5f, 4, 300f)]
        public void LinearBias_Multiply_AddsPerStackDelta(float magnitude, int stacks, float expected)
        {
            var asc = CreateAsc(out PolicyAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(100f);
                ApplyStacks(asc, BuildMultiplier("Policy.LinearBias", magnitude, stacks, GASStackMagnitudePolicy.LinearBias), stacks);

                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromFloat(expected).RawValue),
                    $"{magnitude}x at {stacks} stacks must be {expected} under LinearBias.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [TestCase(2f, 2, 400f)]
        [TestCase(2f, 3, 800f)]
        [TestCase(1.5f, 2, 225f)]
        public void Exponential_CompoundsPerStack(float magnitude, int stacks, float expected)
        {
            var asc = CreateAsc(out PolicyAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(100f);
                ApplyStacks(asc, BuildMultiplier("Policy.Exponential", magnitude, stacks, GASStackMagnitudePolicy.Exponential), stacks);

                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromFloat(expected).RawValue),
                    $"{magnitude}x at {stacks} stacks must be {expected} under Exponential.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [TestCase(GASStackMagnitudePolicy.LinearBias)]
        [TestCase(GASStackMagnitudePolicy.Exponential)]
        public void SingleStack_IsUnchangedByPolicy(GASStackMagnitudePolicy policy)
        {
            var asc = CreateAsc(out PolicyAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(100f);
                ApplyStacks(asc, BuildMultiplier("Policy.Single", 2f, 4, policy), 1);

                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(200).RawValue),
                    "A single stack must scale by the raw magnitude for every policy.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void LinearBias_Division_ScalesTheDivisor()
        {
            var asc = CreateAsc(out PolicyAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(100f);
                var effect = new GameplayEffect(
                    "Policy.Division",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Division, new ScalableFloat(2f))
                    },
                    stacking: new GameplayEffectStacking(
                        EGameplayEffectStackingType.AggregateByTarget,
                        10,
                        EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));
                ApplyStacks(asc, effect, 3);

                // Divisor: (2-1)*3 + 1 = 4 -> 100 / 4 = 25, matching UE's bias-sum division.
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(25).RawValue));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void LinearBias_TwoIndependentSources_StillComposeAsProduct()
        {
            var asc = CreateAsc(out PolicyAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(100f);
                ApplyStacks(asc, BuildMultiplier("Policy.Source.A", 2f, 2, GASStackMagnitudePolicy.LinearBias), 1);
                ApplyStacks(asc, BuildMultiplier("Policy.Source.B", 2f, 2, GASStackMagnitudePolicy.LinearBias), 1);

                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(400).RawValue),
                    "Aggregation mode still governs how independent sources combine: two x2 yield x4.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void Policy_SurvivesModifierPoolCompaction()
        {
            // Core-only path: the runtime reads the policy from the definition on every evaluation,
            // but the Core state stores it per active effect and rebuilds that record when a
            // neighbouring modifier range is removed.
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var attributeId = new GASAttributeId(1);
            state.SetAttributeBase(attributeId, GASFixedValue.FromInt(100));

            var modifier = new[] { new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(2)) };
            GASActiveEffectHandle lower = state.AddActiveEffect(new GASDefinitionId(1), new GASEntityId(2), default,
                GASEffectDurationPolicy.Infinite, 1, 1, 0, 0, modifier, 0, 1, GASStackMagnitudePolicy.LinearBias);
            state.AddActiveEffect(new GASDefinitionId(2), new GASEntityId(2), default,
                GASEffectDurationPolicy.Infinite, 1, 3, 0, 0, modifier, 0, 1, GASStackMagnitudePolicy.Exponential);

            Assert.That(state.TryGetAttribute(attributeId, out var before), Is.True);
            Assert.That(before.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(1600).RawValue),
                "Baseline: x2 (linear, 1 stack) and 2^3 (exponential, 3 stacks) compose to 1600.");

            // Removing the lower slice compacts the modifier pool and rewrites the surviving effect.
            Assert.That(state.RemoveActiveEffect(lower), Is.True);

            Assert.That(state.TryGetAttribute(attributeId, out var after), Is.True);
            Assert.That(after.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(800).RawValue),
                "Compaction must not reset the surviving effect's stack magnitude policy to LinearBias (would yield 400).");
        }

        [Test]
        public void ZeroScaledDivisor_IsTreatedAsNoOp()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var attributeId = new GASAttributeId(1);
            state.SetAttributeBase(attributeId, GASFixedValue.FromInt(100));

            // LinearBias divisor: (0.5 - 1) * 2 + 1 == 0. Dividing by it would be undefined.
            var modifiers = new[]
            {
                new GASModifierData(attributeId, GASModifierOp.Division, GASFixedValue.FromFloat(0.5f))
            };
            state.AddActiveEffect(new GASDefinitionId(1), new GASEntityId(2), default,
                GASEffectDurationPolicy.Infinite, 1, 2, 0, 0, modifiers, 0, 1,
                GASStackMagnitudePolicy.LinearBias);

            Assert.That(state.TryGetAttribute(attributeId, out var attribute), Is.True);
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue),
                "A zero scaled divisor must be skipped instead of dividing by zero.");
        }

        [Test]
        public void Exponential_BeyondFixedPointRange_IsRejectedAtAuthoring()
        {
            // 10^10 exceeds the Q32.32 integer range (2.1e9); the asset must not reach the runtime.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => BuildMultiplier(
                "Policy.Overflow", 10f, 10, GASStackMagnitudePolicy.Exponential));
        }

        [Test]
        public void LinearBias_LargeStackLimit_IsAccepted()
        {
            // (10-1)*100 + 1 = 901 stays well inside range, so a large limit is not rejected.
            Assert.DoesNotThrow(() => BuildMultiplier("Policy.WideLimit", 10f, 100, GASStackMagnitudePolicy.LinearBias));
        }

        [Test]
        public void StackingDrawer_GreysOutPolicyWhenEffectHasNoMultiplicativeModifier()
        {
            Assert.That(PolicyAppliesFor(EAttributeModifierOperation.Add), Is.False,
                "An Add-only effect never consults the stack magnitude policy.");
            Assert.That(PolicyAppliesFor(EAttributeModifierOperation.Override), Is.False,
                "Override ignores the stack count entirely.");
            Assert.That(PolicyAppliesFor(EAttributeModifierOperation.Multiply), Is.True);
            Assert.That(PolicyAppliesFor(EAttributeModifierOperation.Division), Is.True);
        }

        private static bool PolicyAppliesFor(EAttributeModifierOperation operation)
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                so.SerializableModifiers = new List<ModifierInfoSerializable>
                {
                    new ModifierInfoSerializable
                    {
                        AttributeName = "Attr0",
                        Operation = operation,
                        Magnitude = new ScalableFloat(1f)
                    }
                };

                var serialized = new UnityEditor.SerializedObject(so);
                UnityEditor.SerializedProperty stacking = serialized.FindProperty("Stacking");
                return CycloneGames.GameplayAbilities.Editor.GameplayEffectStackingDrawer
                    .MagnitudePolicyApplies(stacking);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void UnlimitedStackLimit_ResolvesToUshortMaxAndSkipsTheLimitedRange()
        {
            var unlimited = new GameplayEffectStacking(
                EGameplayEffectStackingType.AggregateByTarget,
                0,
                EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                limitMode: EGameplayEffectStackLimitMode.Unlimited);

            Assert.That(unlimited.ResolveStackLimit(), Is.EqualTo((int)ushort.MaxValue),
                "Unlimited must saturate at the Core mirror's ushort capacity instead of wrapping.");

            // Limit = 0 is meaningless in Unlimited mode and must not be rejected.
            Assert.DoesNotThrow(() => new GameplayEffect(
                "Policy.Unlimited",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                },
                stacking: unlimited));
        }

        [Test]
        public void UnlimitedWithExponential_IsRejectedAtAuthoring()
        {
            // 65535 stacks of any compounding factor leaves the Q32.32 range immediately.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GameplayEffect(
                "Policy.Unlimited.Exponential",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(1.01f))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    1,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                    magnitudePolicy: GASStackMagnitudePolicy.Exponential,
                    limitMode: EGameplayEffectStackLimitMode.Unlimited)));
        }

        [Test]
        public void Unlimited_StacksPastTheLimitedCap()
        {
            var asc = CreateAsc(out PolicyAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(10f);
                var effect = new GameplayEffect(
                    "Policy.Unlimited.Stacks",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    },
                    stacking: new GameplayEffectStacking(
                        EGameplayEffectStackingType.AggregateByTarget,
                        1,
                        EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                        limitMode: EGameplayEffectStackLimitMode.Unlimited));

                ApplyStacks(asc, effect, 5);

                Assert.That(asc.ActiveEffects[0].StackCount, Is.EqualTo(5),
                    "Unlimited must ignore the Limit field entirely.");
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(15).RawValue),
                    "Additive magnitude must scale with all five stacks: 10 + 5 x 1.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [TestCase("2", true, 2)]
        [TestCase("4096", true, 4096)]
        [TestCase("999999", true, 4096)]
        [TestCase("999999999999", true, 4096)]
        [TestCase("0", false, 0)]
        [TestCase("-3", false, 0)]
        [TestCase("12.5", false, 0)]
        [TestCase("12a", false, 0)]
        [TestCase("", false, 0)]
        public void TryResolveStackLimit_RejectsNonNumbersAndClampsToRange(
            string text, bool expectedOk, int expectedValue)
        {
            bool ok = CycloneGames.GameplayAbilities.Editor.GameplayEffectStackingDrawer
                .TryResolveStackLimit(text, 4096, out int value);

            Assert.That(ok, Is.EqualTo(expectedOk));
            Assert.That(value, Is.EqualTo(expectedValue));
        }

        [Test]
        public void LimitedCap_StopsAdditiveValueFromGrowing()
        {
            var asc = CreateAsc(out PolicyAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(10f);
                var effect = new GameplayEffect(
                    "Policy.Add.Cap",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(5f))
                    },
                    stacking: new GameplayEffectStacking(
                        EGameplayEffectStackingType.AggregateByTarget,
                        2,
                        EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));

                ApplyStacks(asc, effect, 5);

                // Add always scales as magnitude * stackCount, so the cap is what bounds the total.
                Assert.That(asc.ActiveEffects[0].StackCount, Is.EqualTo(2),
                    "A limited stack must stop at the configured cap.");
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(20).RawValue),
                    "10 + 5 x 2 stacks; the three further applications must not add damage.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void StackLimitZero_IsRejectedForStackingEffects()
        {
            // The inspector lets a designer pick a stacking type and leave Limit at 0. The runtime
            // definition treats that as invalid rather than "unlimited", so the asset must be fixed
            // before it can be built.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GameplayEffect(
                "Policy.ZeroLimit",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    0,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication)));

            // StackingNone ignores the limit entirely, so the default (0) stays valid.
            Assert.DoesNotThrow(() => new GameplayEffect(
                "Policy.ZeroLimit.None",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                }));
        }

        [Test]
        public void ScriptableObject_Serializer_ExposesMagnitudePolicy()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                var serialized = new UnityEditor.SerializedObject(so);
                UnityEditor.SerializedProperty stacking = serialized.FindProperty("Stacking");
                Assert.That(stacking, Is.Not.Null, "GameplayEffectSO.Stacking must stay a serialized field.");

                UnityEditor.SerializedProperty policy = stacking.FindPropertyRelative("MagnitudePolicy");
                Assert.That(policy, Is.Not.Null,
                    "MagnitudePolicy must be reachable by name so the SO asset persists and the drawer can draw it.");
                Assert.That(policy.enumValueIndex, Is.EqualTo((int)GASStackMagnitudePolicy.LinearBias),
                    "Assets serialized before the field existed must fall back to LinearBias.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void StackingDrawer_HidesDefaultsAndLocksDormantValues()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                // Stacking disabled: only the type row.
                Assert.That(VisibleRowCount(so), Is.EqualTo(1));

                // Limited, no multiplicative modifier, not periodic: the rows that matter.
                so.Stacking = new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    2,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication);
                Assert.That(VisibleRowCount(so), Is.EqualTo(5),
                    "Type, LimitMode, Limit, Duration Refresh and Expiration Policy.");

                // Unlimited with a default-ish limit: the value carries no information, hide the row.
                so.Stacking = new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    1,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                    limitMode: EGameplayEffectStackLimitMode.Unlimited);
                Assert.That(VisibleRowCount(so), Is.EqualTo(4),
                    "Unlimited hides a limit that carries no information.");

                // Unlimited with a real configured limit: keep it visible but locked, otherwise
                // switching back to Limited would silently reapply a number nobody can see.
                so.Stacking = new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    2,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                    limitMode: EGameplayEffectStackLimitMode.Unlimited);
                Assert.That(VisibleRowCount(so), Is.EqualTo(5),
                    "A dormant non-default limit stays visible.");

                so.SerializableModifiers = new List<ModifierInfoSerializable>
                {
                    new ModifierInfoSerializable
                    {
                        AttributeName = "Attr0",
                        Operation = EAttributeModifierOperation.Multiply,
                        Magnitude = new ScalableFloat(1.1f)
                    }
                };
                Assert.That(VisibleRowCount(so), Is.EqualTo(6),
                    "A multiplicative modifier brings the Magnitude Policy row back.");

                so.Stacking = new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    2,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                    limitMode: EGameplayEffectStackLimitMode.Unlimited,
                    periodResetPolicy: EGameplayEffectStackingPeriodPolicy.NeverReset);
                Assert.That(VisibleRowCount(so), Is.EqualTo(7),
                    "A non-default Period Reset is shown locked even when the effect is not periodic.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        private static int VisibleRowCount(GameplayEffectSO so)
        {
            var serialized = new UnityEditor.SerializedObject(so);
            UnityEditor.SerializedProperty stacking = serialized.FindProperty("Stacking");
            var drawer = new CycloneGames.GameplayAbilities.Editor.GameplayEffectStackingDrawer();
            float height = drawer.GetPropertyHeight(stacking, UnityEngine.GUIContent.none);
            float line = UnityEditor.EditorGUIUtility.singleLineHeight;
            float spacing = UnityEditor.EditorGUIUtility.standardVerticalSpacing;
            return UnityEngine.Mathf.RoundToInt((height + spacing) / (line + spacing));
        }

        [TestCase(GASStackMagnitudePolicy.LinearBias, 3, 400)]
        [TestCase(GASStackMagnitudePolicy.Exponential, 3, 800)]
        public void Core_HonorsPolicyIdenticallyToRuntime(GASStackMagnitudePolicy policy, int stacks, int expected)
        {
            var attributeId = new GASAttributeId(1);
            var modifiers = new[]
            {
                new GASModifierData(attributeId, GASModifierOp.Multiply, GASFixedValue.FromInt(2))
            };

            var state = new GASAbilitySystemState(new GASEntityId(1));
            state.SetAttributeBase(attributeId, GASFixedValue.FromInt(100));
            state.ApplyGameplayEffectSpecToSelf(new GASGameplayEffectSpecData(
                new GASDefinitionId(1),
                new GASEntityId(2),
                default,
                GASEffectDurationPolicy.Infinite,
                1,
                (ushort)stacks,
                0,
                0,
                modifiers,
                0,
                modifiers.Length,
                policy));

            Assert.That(state.TryGetAttribute(attributeId, out var attribute), Is.True);
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(expected).RawValue),
                $"Core must apply {policy} the same way the runtime does.");
        }

        private static void ApplyStacks(AbilitySystemComponent asc, GameplayEffect effect, int stacks)
        {
            for (int i = 0; i < stacks; i++)
            {
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True,
                    $"Stack application {i} failed for {effect.Name}.");
            }

            asc.Tick(DeltaTime, isServer: true);
        }

        private static GameplayEffect BuildMultiplier(
            string name,
            float magnitude,
            int stackLimit,
            GASStackMagnitudePolicy policy)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(magnitude))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    stackLimit,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                    magnitudePolicy: policy));
        }

        private static AbilitySystemComponent CreateAsc(out PolicyAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new PolicyAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private sealed class PolicyAttributeSet : AttributeSet
        {
            private readonly GameplayAttribute[] _attributes = new GameplayAttribute[4];

            internal PolicyAttributeSet()
            {
                for (int i = 0; i < _attributes.Length; i++)
                {
                    _attributes[i] = new GameplayAttribute($"Attr{i}");
                }
            }

            internal GameplayAttribute ValueOf(int index) => _attributes[index];

            protected override void RegisterAttributes()
            {
                for (int i = 0; i < _attributes.Length; i++)
                {
                    RegisterAttribute(_attributes[i]);
                }
            }
        }
    }
}
