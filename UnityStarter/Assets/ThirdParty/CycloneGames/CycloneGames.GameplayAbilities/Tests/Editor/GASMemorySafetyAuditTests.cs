using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Memory-safety and static-state audit suite. Verifies object-lifetime contracts
    /// (use-after-dispose / use-after-release), churn stability of process-wide static state
    /// (tag registry), isolation between independent AbilitySystemComponents, idempotency of
    /// duplicate dynamic tag registration, and boundedness of the GASTrace ring buffer.
    /// </summary>
    public sealed class GASMemorySafetyAuditTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int AttributeCount = 4;

        // ---------------------------------------------------------------------
        // 1. Use-after-dispose is rejected; duplicate dispose is a no-op.
        // ---------------------------------------------------------------------

        [Test]
        public void Dispose_RejectsSubsequentUse_AndDoubleDisposeIsIdempotent()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            asc.Tick(DeltaTime, isServer: true);
            asc.Dispose();

            Assert.That(asc.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => asc.Tick(DeltaTime, isServer: true),
                "Use after dispose must throw ObjectDisposedException.");
            Assert.DoesNotThrow(() => asc.Dispose(), "Double dispose must be a safe no-op.");
            UnityEngine.Debug.Log("[MemoryAudit][Dispose] PASS: use-after-dispose rejected; double dispose idempotent.");
        }

        // ---------------------------------------------------------------------
        // 2. Released specs fail closed on any subsequent access.
        // ---------------------------------------------------------------------

        [Test]
        public void ReleasedSpec_RejectsSubsequentAccess()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new MemoryAuditAttributeSet();
            asc.AddAttributeSet(attributes);
            try
            {
                var effect = new GameplayEffect(
                    "MemoryAudit.Spec",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    });

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);
                spec.Discard();

                Assert.Throws<ObjectDisposedException>(() => { _ = spec.TargetAttributes; },
                    "A released spec must fail closed on public reads.");
                UnityEngine.Debug.Log("[MemoryAudit][Spec] PASS: released spec rejected on access.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 3. Create/dispose churn leaves process-wide static registry state stable.
        // ---------------------------------------------------------------------

        [Test]
        public void CreateDisposeChurn_LeavesStaticTagRegistryStable()
        {
            GameplayTagManager.InitializeIfNeeded();
            int tagCountBefore = GameplayTagManager.TagCount;
            int registryIdBefore = GameplayTagManager.RegistryInstanceId;

            const int cycles = 100;
            for (int i = 0; i < cycles; i++)
            {
                var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
                var attributes = new MemoryAuditAttributeSet();
                asc.AddAttributeSet(attributes);
                attributes.SetBaseValue(attributes.ValueOf(0), GASFixedValue.FromInt(10));

                var effect = new GameplayEffect(
                    $"MemoryAudit.Churn.{i}",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    });
                GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
                Assert.That(result.Succeeded, Is.True, $"Churn cycle {i} failed to apply.");
                asc.Tick(DeltaTime, isServer: true);
                asc.Dispose();
            }

            Assert.That(GameplayTagManager.TagCount, Is.EqualTo(tagCountBefore),
                "Static tag registry count must not grow from ASC churn.");
            Assert.That(GameplayTagManager.RegistryInstanceId, Is.EqualTo(registryIdBefore),
                "Static tag registry must not be rebuilt or replaced by ASC churn.");

            // A fresh instance after churn must still behave correctly.
            var fresh = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var freshAttributes = new MemoryAuditAttributeSet();
            fresh.AddAttributeSet(freshAttributes);
            freshAttributes.SetBaseValue(freshAttributes.ValueOf(0), GASFixedValue.FromInt(10));
            var freshEffect = new GameplayEffect(
                "MemoryAudit.Fresh",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(5f))
                });
            Assert.That(fresh.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(freshEffect, fresh)).Succeeded, Is.True);
            Assert.That(freshAttributes.ValueOf(0).BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(15).RawValue));
            fresh.Dispose();

            UnityEngine.Debug.Log(
                $"[MemoryAudit][Churn] PASS: {cycles} create/apply/tick/dispose cycles; TagCount={tagCountBefore} stable, RegistryInstanceId={registryIdBefore} stable.");
        }

        // ---------------------------------------------------------------------
        // 4. Independent instances do not interfere; disposing one leaves the
        //    other fully functional.
        // ---------------------------------------------------------------------

        [Test]
        public void StaticIsolation_DisposingOneSystem_LeavesOtherUnaffected()
        {
            var first = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var second = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var firstAttributes = new MemoryAuditAttributeSet();
            var secondAttributes = new MemoryAuditAttributeSet();
            first.AddAttributeSet(firstAttributes);
            second.AddAttributeSet(secondAttributes);
            try
            {
                firstAttributes.SetBaseValue(firstAttributes.ValueOf(0), GASFixedValue.FromInt(100));
                secondAttributes.SetBaseValue(secondAttributes.ValueOf(0), GASFixedValue.FromInt(100));

                var firstEffect = new GameplayEffect(
                    "MemoryAudit.First",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(3f))
                    });
                Assert.That(first.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(firstEffect, first)).Succeeded, Is.True);
                Assert.That(firstAttributes.ValueOf(0).BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(103).RawValue));

                // The second instance must be untouched by the first instance's activity.
                Assert.That(secondAttributes.ValueOf(0).BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue),
                    "Independent instances must not share per-instance state.");

                first.Dispose();

                var secondEffect = new GameplayEffect(
                    "MemoryAudit.Second",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(5f))
                    });
                Assert.That(second.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(secondEffect, second)).Succeeded, Is.True);
                second.Tick(DeltaTime, isServer: true);
                Assert.That(secondAttributes.ValueOf(0).BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue),
                    "Disposing one instance must not affect another instance.");

                UnityEngine.Debug.Log("[MemoryAudit][Isolation] PASS: per-instance isolation + cross-instance dispose safety.");
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 5. Duplicate dynamic tag registration is idempotent.
        // ---------------------------------------------------------------------

        [Test]
        public void GameplayTagRegistry_DuplicateDynamicTag_IsIdempotent()
        {
            const string tagName = "MemoryAudit.Duplicate.Tag";
            GameplayTagManager.RegisterDynamicTag(tagName, "memory audit");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag first = GameplayTagManager.Request(tagName);
            int countAfterFirst = GameplayTagManager.TagCount;
            Assert.That(first.IsValid, Is.True, "Registered tag must be resolvable.");

            GameplayTagManager.RegisterDynamicTag(tagName, "memory audit");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag second = GameplayTagManager.Request(tagName);

            Assert.That(second, Is.EqualTo(first), "Duplicate registration must resolve to the same tag.");
            Assert.That(GameplayTagManager.TagCount, Is.EqualTo(countAfterFirst),
                "Duplicate registration must not grow the registry.");
            UnityEngine.Debug.Log($"[MemoryAudit][TagRegistry] PASS: duplicate registration idempotent; TagCount={countAfterFirst} stable.");
        }

        // ---------------------------------------------------------------------
        // 6. GASTrace ring buffer stays bounded under sustained event volume.
        // ---------------------------------------------------------------------

        [Test]
        public void GASTrace_RingBuffer_StaysBounded()
        {
            const int traceCapacity = 256;
            GASTrace.SetCapacity(traceCapacity);
            try
            {
                var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
                var attributes = new MemoryAuditAttributeSet();
                asc.AddAttributeSet(attributes);
                attributes.SetBaseValue(attributes.ValueOf(0), GASFixedValue.FromInt(0));

                GASTrace.Clear();
                GASTrace.Enabled = true;

                var effect = new GameplayEffect(
                    "MemoryAudit.Trace",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    });

                const int recordings = 600; // > 2x capacity
                for (int i = 0; i < recordings; i++)
                {
                    Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);
                }

                Assert.That(GASTrace.Count, Is.GreaterThan(0), "Tracing produced no events; measurement vacuous.");
                Assert.That(GASTrace.Count, Is.LessThanOrEqualTo(traceCapacity),
                    "Ring buffer must never exceed its configured capacity.");
                Assert.That(GASTrace.LatestSequence, Is.GreaterThanOrEqualTo((ulong)GASTrace.Count),
                    "Sequence must advance monotonically with recorded events.");

                GASTrace.Clear();
                Assert.That(GASTrace.Count, Is.Zero, "Clear must reset the trace buffer.");

                asc.Dispose();
                UnityEngine.Debug.Log(
                    $"[MemoryAudit][Trace] PASS: {recordings} events recorded into capacity-{traceCapacity} ring buffer; bounded and cleared.");
            }
            finally
            {
                GASTrace.Enabled = false;
                GASTrace.SetCapacity(GASTrace.DefaultCapacity);
                GASTrace.Clear();
            }
        }

        private sealed class MemoryAuditAttributeSet : AttributeSet
        {
            private readonly GameplayAttribute[] _attributes = new GameplayAttribute[AttributeCount];

            internal MemoryAuditAttributeSet()
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
