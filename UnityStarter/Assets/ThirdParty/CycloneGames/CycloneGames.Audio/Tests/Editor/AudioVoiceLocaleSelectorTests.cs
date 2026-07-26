using System.Collections.Generic;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioVoiceLocaleSelectorTests
    {
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
        public void ExactPrimaryLocaleWinsBeforeFallback()
        {
            AudioVoiceFile english = CreateVoice("en");
            AudioVoiceFile japanese = CreateVoice("ja-JP");
            AudioNodeOutput[] connectedNodes = CreateOutputs(english, japanese);

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                connectedNodes,
                Snapshot("ja-JP", "en"),
                null);

            Assert.AreEqual(1, selectedIndex);
        }

        [Test]
        public void FallbackLocalesUseDeclaredOrder()
        {
            AudioVoiceFile english = CreateVoice("en");
            AudioVoiceFile french = CreateVoice("fr");
            AudioNodeOutput[] connectedNodes = CreateOutputs(english, french);

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                connectedNodes,
                Snapshot("de-DE", "fr", "en"),
                null);

            Assert.AreEqual(1, selectedIndex);
        }

        [Test]
        public void ExplicitFallbackRunsAfterLocaleChainMisses()
        {
            AudioVoiceFile english = CreateVoice("en");
            AudioVoiceFile japanese = CreateVoice("ja");
            AudioNodeOutput[] connectedNodes = CreateOutputs(english, japanese);

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                connectedNodes,
                Snapshot("ko-KR", "ko"),
                japanese);

            Assert.AreEqual(1, selectedIndex);
        }

        [Test]
        public void UnsetLocaleUsesExplicitFallback()
        {
            AudioVoiceFile english = CreateVoice("en");
            AudioNodeOutput[] connectedNodes = CreateOutputs(english);

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                connectedNodes,
                default,
                english);

            Assert.AreEqual(0, selectedIndex);
        }

        [Test]
        public void LocaleMissWithoutExplicitFallbackReturnsNoMatch()
        {
            AudioVoiceFile english = CreateVoice("en");
            AudioNodeOutput[] connectedNodes = CreateOutputs(english);

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                connectedNodes,
                Snapshot("ko-KR", "ko"),
                null);

            Assert.AreEqual(-1, selectedIndex);
        }

        [Test]
        public void NonVoiceBranchFailsClosed()
        {
            AudioNode invalidBranch = ScriptableObject.CreateInstance<AudioNode>();
            ownedObjects.Add(invalidBranch);
            AudioNodeOutput output = CreateOutput(invalidBranch);

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                new[] { output },
                Snapshot("en"),
                null);

            Assert.AreEqual(-1, selectedIndex);
        }

        [Test]
        public void DuplicateBranchLocaleFailsClosed()
        {
            AudioVoiceFile first = CreateVoice("en");
            AudioVoiceFile second = CreateVoice("en");

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                CreateOutputs(first, second),
                Snapshot("en"),
                null);

            Assert.AreEqual(-1, selectedIndex);
        }

        [Test]
        public void DisconnectedFallbackFailsClosed()
        {
            AudioVoiceFile connected = CreateVoice("en");
            AudioVoiceFile disconnectedFallback = CreateVoice("ja");

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                CreateOutputs(connected),
                Snapshot("en"),
                disconnectedFallback);

            Assert.AreEqual(-1, selectedIndex);
        }

        [Test]
        public void NonCanonicalBranchLocaleFailsClosed()
        {
            AudioVoiceFile voice = CreateVoice("EN-us");

            int selectedIndex = AudioVoiceLocaleSelector.SelectConnectedNodeIndex(
                CreateOutputs(voice),
                Snapshot("en-US"),
                null);

            Assert.AreEqual(-1, selectedIndex);
        }

        private AudioVoiceFile CreateVoice(string localeCode)
        {
            AudioVoiceFile voice = ScriptableObject.CreateInstance<AudioVoiceFile>();
            ownedObjects.Add(voice);

            var serializedVoice = new SerializedObject(voice);
            SerializedProperty locale = serializedVoice.FindProperty("voiceLocaleCode");
            Assert.NotNull(locale);
            locale.stringValue = localeCode;
            serializedVoice.ApplyModifiedPropertiesWithoutUndo();
            return voice;
        }

        private AudioNodeOutput[] CreateOutputs(params AudioVoiceFile[] voices)
        {
            var outputs = new AudioNodeOutput[voices.Length];
            for (int i = 0; i < voices.Length; i++)
                outputs[i] = CreateOutput(voices[i]);
            return outputs;
        }

        private AudioNodeOutput CreateOutput(AudioNode parent)
        {
            AudioNodeOutput output = ScriptableObject.CreateInstance<AudioNodeOutput>();
            output.ParentNode = parent;
            ownedObjects.Add(output);
            return output;
        }

        private static AudioVoiceLocaleSnapshot Snapshot(
            string primary,
            params string[] fallbacks)
        {
            Assert.IsTrue(VoiceLocaleId.TryCreate(primary, out VoiceLocaleId primaryLocale));
            var fallbackLocales = new VoiceLocaleId[fallbacks.Length];
            for (int i = 0; i < fallbacks.Length; i++)
            {
                Assert.IsTrue(
                    VoiceLocaleId.TryCreate(fallbacks[i], out fallbackLocales[i]));
            }

            Assert.IsTrue(AudioVoiceLocaleSnapshot.TryCreate(
                primaryLocale,
                fallbackLocales,
                out AudioVoiceLocaleSnapshot snapshot));
            return snapshot;
        }
    }
}
