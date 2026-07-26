using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioVoiceLocaleSelectorValidatorTests
    {
        private const string ValidatorTypeName =
            "CycloneGames.Audio.Editor.AudioBankValidator";
        private const string ReportTypeName =
            "CycloneGames.Audio.Editor.AudioBankValidationReport";

        private readonly List<Object> ownedObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                if (ownedObjects[i] != null)
                    Object.DestroyImmediate(ownedObjects[i]);
            }

            ownedObjects.Clear();
        }

        [Test]
        public void DuplicateLocalesProduceError()
        {
            AudioVoiceFile firstVoice = CreateVoice("en");
            AudioVoiceFile secondVoice = CreateVoice("en");
            AudioVoiceLocaleSelector selector = CreateSelector(
                null,
                firstVoice,
                secondVoice);

            ValidationResult result = Validate(
                CreateBank(selector, firstVoice, secondVoice));

            Assert.IsTrue(
                result.Contains("Error", "duplicate Voice Locale"),
                result.Describe());
        }

        [Test]
        public void EmptyLocaleProducesError()
        {
            AudioVoiceFile voice = CreateVoice(string.Empty);
            AudioVoiceLocaleSelector selector = CreateSelector(null, voice);

            ValidationResult result = Validate(CreateBank(selector, voice));

            Assert.IsTrue(
                result.Contains("Error", "branch 0 has no Voice Locale"),
                result.Describe());
        }

        [Test]
        public void NonCanonicalLocaleProducesError()
        {
            AudioVoiceFile voice = CreateVoice("EN-us");
            AudioVoiceLocaleSelector selector = CreateSelector(null, voice);

            ValidationResult result = Validate(CreateBank(selector, voice));

            Assert.IsTrue(
                result.Contains("Error", "must use canonical Voice Locale 'en-US'"),
                result.Describe());
        }

        [Test]
        public void NonVoiceBranchProducesError()
        {
            AudioNode nonVoice = CreateNode<AudioNode>("Non Voice Branch");
            CreateOutput(nonVoice);
            AudioVoiceLocaleSelector selector = CreateSelector(null, nonVoice);

            ValidationResult result = Validate(CreateBank(selector, nonVoice));

            Assert.IsTrue(
                result.Contains("Error", "must connect directly to a Voice File"),
                result.Describe());
        }

        [Test]
        public void UnconnectedExplicitFallbackProducesError()
        {
            AudioVoiceFile connectedVoice = CreateVoice("en");
            AudioVoiceFile unconnectedFallback = CreateVoice("ja");
            AudioVoiceLocaleSelector selector = CreateSelector(
                unconnectedFallback,
                connectedVoice);

            ValidationResult result = Validate(
                CreateBank(selector, connectedVoice));

            Assert.IsTrue(
                result.Contains("Error", "Fallback Voice that is not connected"),
                result.Describe());
        }

        [Test]
        public void SelectorWithoutBranchesProducesError()
        {
            AudioVoiceLocaleSelector selector = CreateSelector(null);

            ValidationResult result = Validate(CreateBank(selector));

            Assert.IsTrue(
                result.Contains("Error", "has no connected Voice File branches"),
                result.Describe());
        }

        [Test]
        public void SelectorWithoutFallbackReportsIntentionalNoPlay()
        {
            AudioVoiceFile voice = CreateVoice("en");
            AudioVoiceLocaleSelector selector = CreateSelector(null, voice);

            ValidationResult result = Validate(CreateBank(selector, voice));

            Assert.IsTrue(
                result.Contains("Info", "intentionally skips playback"),
                result.Describe());
        }

        private AudioBank CreateBank(
            AudioVoiceLocaleSelector selector,
            params AudioNode[] branchNodes)
        {
            AudioBank bank = CreateObject<AudioBank>("Validation Bank");
            AudioEvent audioEvent = CreateObject<AudioEvent>("Validation Event");
            AudioOutput output = CreateNode<AudioOutput>("Output");
            CreateInput(output, selector.Output);

            audioEvent.Output = output;
            audioEvent.Nodes.Add(output);
            audioEvent.Nodes.Add(selector);
            for (int i = 0; i < branchNodes.Length; i++)
                audioEvent.Nodes.Add(branchNodes[i]);

            bank.AudioEvents.Add(audioEvent);
            return bank;
        }

        private AudioVoiceLocaleSelector CreateSelector(
            AudioVoiceFile fallback,
            params AudioNode[] branches)
        {
            AudioVoiceLocaleSelector selector =
                CreateNode<AudioVoiceLocaleSelector>("Voice Locale Selector");
            SetSerializedObject(selector, "fallbackVoice", fallback);
            CreateOutput(selector);

            var connectedOutputs = new AudioNodeOutput[branches.Length];
            for (int i = 0; i < branches.Length; i++)
            {
                AudioNode branch = branches[i];
                connectedOutputs[i] = branch != null ? branch.Output : null;
            }

            CreateInput(selector, connectedOutputs);
            return selector;
        }

        private AudioVoiceFile CreateVoice(string localeCode)
        {
            AudioVoiceFile voice = CreateNode<AudioVoiceFile>("Voice");
            AudioClip clip = AudioClip.Create(
                "Validation Voice Clip",
                1,
                1,
                8000,
                false);
            ownedObjects.Add(clip);
            SetSerializedObject(voice, "file", clip);
            SetSerializedString(voice, "voiceLocaleCode", localeCode);
            CreateOutput(voice);
            return voice;
        }

        private T CreateNode<T>(string name) where T : AudioNode =>
            CreateObject<T>(name);

        private T CreateObject<T>(string name) where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            instance.name = name;
            ownedObjects.Add(instance);
            return instance;
        }

        private AudioNodeOutput CreateOutput(AudioNode parent)
        {
            AudioNodeOutput output =
                CreateObject<AudioNodeOutput>(parent.name + " Output");
            output.ParentNode = parent;
            SetSerializedObject(parent, "output", output);
            return output;
        }

        private AudioNodeInput CreateInput(
            AudioNode parent,
            params AudioNodeOutput[] connectedOutputs)
        {
            AudioNodeInput input =
                CreateObject<AudioNodeInput>(parent.name + " Input");
            input.ParentNode = parent;

            var serializedInput = new SerializedObject(input);
            SerializedProperty connectedNodes =
                serializedInput.FindProperty("connectedNodes");
            Assert.NotNull(connectedNodes);
            connectedNodes.arraySize = connectedOutputs.Length;
            for (int i = 0; i < connectedOutputs.Length; i++)
            {
                connectedNodes.GetArrayElementAtIndex(i).objectReferenceValue =
                    connectedOutputs[i];
            }

            serializedInput.ApplyModifiedPropertiesWithoutUndo();
            SetSerializedObject(parent, "input", input);
            return input;
        }

        private static void SetSerializedString(
            Object target,
            string propertyName,
            string value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property =
                serializedObject.FindProperty(propertyName);
            Assert.NotNull(property, propertyName);
            property.stringValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedObject(
            Object target,
            string propertyName,
            Object value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property =
                serializedObject.FindProperty(propertyName);
            Assert.NotNull(property, propertyName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ValidationResult Validate(AudioBank bank)
        {
            Assembly editorAssembly =
                typeof(CycloneGames.Audio.Editor.AudioBankEditor).Assembly;
            Type validatorType = editorAssembly.GetType(ValidatorTypeName, true);
            Type reportType = editorAssembly.GetType(ReportTypeName, true);
            object report = Activator.CreateInstance(reportType, true);
            MethodInfo validate = validatorType.GetMethod(
                "Validate",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(validate);

            validate.Invoke(null, new[] { bank, report });

            FieldInfo issuesField = reportType.GetField(
                "Issues",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(issuesField);
            var issues = issuesField.GetValue(report) as IEnumerable;
            Assert.NotNull(issues);

            var result = new ValidationResult();
            foreach (object issue in issues)
            {
                Type issueType = issue.GetType();
                FieldInfo severityField = issueType.GetField("Severity");
                FieldInfo messageField = issueType.GetField("Message");
                Assert.NotNull(severityField);
                Assert.NotNull(messageField);
                result.Add(
                    severityField.GetValue(issue).ToString(),
                    messageField.GetValue(issue) as string);
            }

            return result;
        }

        private sealed class ValidationResult
        {
            private readonly List<ValidationIssue> issues =
                new List<ValidationIssue>();

            public void Add(string severity, string message)
            {
                issues.Add(new ValidationIssue(severity, message));
            }

            public bool Contains(string severity, string messageFragment)
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    ValidationIssue issue = issues[i];
                    if (string.Equals(
                            issue.Severity,
                            severity,
                            StringComparison.Ordinal) &&
                        issue.Message != null &&
                        issue.Message.IndexOf(
                            messageFragment,
                            StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            public string Describe()
            {
                if (issues.Count == 0)
                    return "No validation issues were reported.";

                var lines = new string[issues.Count];
                for (int i = 0; i < issues.Count; i++)
                    lines[i] = issues[i].Severity + ": " + issues[i].Message;
                return string.Join("\n", lines);
            }
        }

        private readonly struct ValidationIssue
        {
            public ValidationIssue(string severity, string message)
            {
                Severity = severity;
                Message = message;
            }

            public string Severity { get; }
            public string Message { get; }
        }
    }
}
