using System;
using System.Collections.Generic;
using System.Text;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASSetByCallerWarningTests
    {
        private ILogWriter previousWriter;
        private RecordingWarningWriter warningWriter;

        [SetUp]
        public void SetUp()
        {
            warningWriter = new RecordingWarningWriter();
            previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, warningWriter));
        }

        [TearDown]
        public void TearDown()
        {
            if (warningWriter == null)
            {
                return;
            }

            LogRuntime.TryReplaceWriter(warningWriter, previousWriter ?? NullLogWriter.Instance);

            warningWriter = null;
            previousWriter = null;
        }

        [Test]
        public void CreateAndDiscard_MissingSetByCallerDoesNotWarn()
        {
            GameplayTag dataTag = RegisterTag("Test.GAS.SetByCallerWarning.CreateDiscard");
            var asc = CreateAbilitySystem(out _);
            GameplayEffect effect = CreateEffect("CreateDiscardNoWarning", dataTag, defaultValue: 4f);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);

            spec.Discard();

            Assert.That(FlushWarnings(), Is.Empty);
            asc.Dispose();
        }

        [Test]
        public void SetThenApply_SetByCallerDoesNotEmitMissingWarning()
        {
            GameplayTag dataTag = RegisterTag("Test.GAS.SetByCallerWarning.SetThenApply");
            var asc = CreateAbilitySystem(out WarningAttributeSet attributes);
            GameplayEffect effect = CreateEffect("SetThenApplyNoWarning", dataTag, defaultValue: 4f);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);
            GASFixedValue supplied = GASFixedValue.FromFloat(9.5f);
            spec.SetSetByCallerMagnitude(dataTag, supplied);

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(attributes.Value.BaseValueRaw, Is.EqualTo(supplied.RawValue));
            Assert.That(attributes.Value.CurrentValueRaw, Is.EqualTo(supplied.RawValue));
            Assert.That(FlushWarnings(), Is.Empty);
            asc.Dispose();
        }

        [Test]
        public void MissingSetByCaller_ApplyWarnsOnceAndUsesDefault()
        {
            GameplayTag dataTag = RegisterTag("Test.GAS.SetByCallerWarning.MissingApply");
            var asc = CreateAbilitySystem(out WarningAttributeSet attributes);
            const string EffectName = "MissingApplyWarning";
            GASFixedValue fallback = GASFixedValue.FromFloat(7.25f);
            GameplayEffect effect = CreateEffect(EffectName, dataTag, fallback.ToFloat());
            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);
            string[] warnings = FlushWarnings();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(attributes.Value.BaseValueRaw, Is.EqualTo(fallback.RawValue));
            Assert.That(attributes.Value.CurrentValueRaw, Is.EqualTo(fallback.RawValue));
            AssertSingleMissingWarning(warnings, dataTag, EffectName);
            asc.Dispose();
        }

        [Test]
        public void ExplicitGetWithWarning_MissingSetByCallerStillWarns()
        {
            GameplayTag dataTag = RegisterTag("Test.GAS.SetByCallerWarning.ExplicitGet");
            var asc = CreateAbilitySystem(out _);
            const string EffectName = "ExplicitGetWarning";
            GameplayEffect effect = CreateEffect(EffectName, dataTag, defaultValue: 3f);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);
            long fallbackRaw = GASFixedValue.FromFloat(11f).RawValue;

            long valueRaw = spec.GetSetByCallerMagnitudeRaw(
                dataTag,
                warnIfNotFound: true,
                defaultValueRaw: fallbackRaw);
            string[] warnings = FlushWarnings();

            Assert.That(valueRaw, Is.EqualTo(fallbackRaw));
            AssertSingleMissingWarning(warnings, dataTag, EffectName);
            spec.Discard();
            asc.Dispose();
        }

        private string[] FlushWarnings()
        {
            return warningWriter.Snapshot();
        }

        private static void AssertSingleMissingWarning(
            string[] warnings,
            GameplayTag dataTag,
            string effectName)
        {
            Assert.That(warnings, Has.Length.EqualTo(1));
            StringAssert.Contains("GetSetByCallerMagnitude: Tag '" + dataTag.Name + "'", warnings[0]);
            StringAssert.Contains("effect '" + effectName + "'", warnings[0]);
        }

        private static AbilitySystemComponent CreateAbilitySystem(out WarningAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new WarningAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private static GameplayEffect CreateEffect(
            string name,
            GameplayTag dataTag,
            float defaultValue)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(
                        WarningAttributeSet.ValueName,
                        EAttributeModifierOperation.Add,
                        new SetByCallerMagnitude(dataTag, defaultValue, warnIfNotFound: true))
                });
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayAbilities SetByCaller warning test tag");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.Request(name);
        }

        private sealed class WarningAttributeSet : AttributeSet
        {
            internal const string ValueName = "SetByCallerWarningValue";
            public GameplayAttribute Value { get; } = new GameplayAttribute(ValueName);

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Value);
            }
        }

        private sealed class RecordingWarningWriter : ILogWriter
        {
            private readonly object gate = new object();
            private readonly List<string> warnings = new List<string>(2);

            public bool IsEnabled(LogSeverity severity, string category)
            {
                return severity == LogSeverity.Warning &&
                       string.Equals(category, "CycloneGames.GameplayAbilities", StringComparison.Ordinal);
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (!IsEnabled(severity, category))
                {
                    return;
                }

                lock (gate)
                {
                    warnings.Add(message ?? string.Empty);
                }
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (!IsEnabled(severity, category))
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder(builder);
                Write(severity, category, builder.ToString(), filePath, lineNumber, memberName);
            }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (!IsEnabled(severity, category))
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder(state, builder);
                Write(severity, category, builder.ToString(), filePath, lineNumber, memberName);
            }

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (!IsEnabled(severity, category))
                {
                    return;
                }

                Write(
                    severity,
                    category,
                    string.IsNullOrEmpty(message) ? exception?.ToString() : message + " " + exception,
                    filePath,
                    lineNumber,
                    memberName);
            }

            public string[] Snapshot()
            {
                lock (gate)
                {
                    return warnings.ToArray();
                }
            }
        }
    }
}
