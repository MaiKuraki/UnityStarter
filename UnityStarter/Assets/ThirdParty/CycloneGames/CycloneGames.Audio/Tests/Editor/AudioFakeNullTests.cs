// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System.Collections.Generic;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Unity objects report <c>null</c> after destruction while the CLR reference survives. Registry
    /// teardown must keep working in that state, or the registration can never be released.
    /// </summary>
    public sealed class AudioFakeNullTests
    {
        private GameObject managerObject;
        private AudioBank bank;
        private AudioEvent audioEvent;
        private AudioOutput output;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(AudioManager.Instance, "A previous test left an AudioManager instance alive.");

            managerObject = new GameObject("AudioFakeNullTests.AudioManager");
            managerObject.SetActive(false);
            managerObject.AddComponent<AudioListener>();

            AudioManager manager = managerObject.AddComponent<AudioManager>();
            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("customPoolSize").intValue = 2;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            managerObject.SetActive(true);
            AudioManager.SetInstance(manager);

            bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.name = "FakeNullBank";

            audioEvent = ScriptableObject.CreateInstance<AudioEvent>();
            audioEvent.name = "FakeNull.Event";

            output = ScriptableObject.CreateInstance<AudioOutput>();
            output.name = "FakeNull.Event.Output";

            var outputField = typeof(AudioEvent).GetField(
                "output",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(outputField);
            outputField.SetValue(audioEvent, output);

            var nodesField = typeof(AudioEvent).GetField(
                "nodes",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(nodesField);
            (nodesField.GetValue(audioEvent) as List<AudioNode>)?.Add(output);

            bank.EditorEvents.Add(audioEvent);
        }

        [TearDown]
        public void TearDown()
        {
            if (AudioManager.Instance != null && bank != null)
            {
                AudioManager.UnloadBank(bank);
            }

            if (managerObject != null)
            {
                AudioManager.ReleaseInstance(managerObject.GetComponent<AudioManager>());
                Object.DestroyImmediate(managerObject);
                managerObject = null;
            }

            DestroyIfAlive(audioEvent);
            DestroyIfAlive(output);
            DestroyIfAlive(bank);
        }

        [Test]
        public void UnloadBank_OnDestroyedBank_ReleasesRegistration()
        {
            AudioManager.LoadBank(bank);
            Assert.AreEqual(1, AudioManager.GetRuntimeStats().LoadedBanks);
            Assert.AreEqual(1, AudioManager.GetRuntimeStats().RegisteredEvents);

            Object.DestroyImmediate(bank);
            Assert.IsTrue(bank == null, "DestroyImmediate must produce a Unity fake-null bank.");
            Assert.IsFalse(ReferenceEquals(bank, null), "The CLR reference must survive destruction.");

            // The runtime id is a managed field, so a destroyed bank can still be identified.
            AudioManager.UnloadBank(bank);

            AudioRuntimeStats stats = AudioManager.GetRuntimeStats();
            Assert.AreEqual(0, stats.LoadedBanks, "A destroyed bank must release its registration.");
            Assert.AreEqual(0, stats.RegisteredEvents, "Owned events must be unregistered with the bank.");
            Assert.IsFalse(AudioManager.TryGetEventId("FakeNull.Event", out _));
        }

        [Test]
        public void UnloadBank_OnDestroyedBank_DoesNotRaiseCallback()
        {
            bool raised = false;
            System.Action<AudioBank> handler = _ => raised = true;
            AudioManager.OnBankUnloaded += handler;

            try
            {
                AudioManager.LoadBank(bank);
                Object.DestroyImmediate(bank);
                AudioManager.UnloadBank(bank);

                Assert.IsFalse(raised, "Listeners must not receive a destroyed bank reference.");
            }
            finally
            {
                AudioManager.OnBankUnloaded -= handler;
            }
        }

        [Test]
        public void PlayEvent_OnDestroyedEvent_ReturnsNull()
        {
            AudioManager.LoadBank(bank);
            Object.DestroyImmediate(audioEvent);

            Assert.IsNull(AudioManager.PlayEvent(audioEvent, (GameObject)null));
            Assert.IsNull(AudioManager.PlayEvent(audioEvent, Vector3.zero));
        }

        [Test]
        public void LoadBank_OnDestroyedBank_IsRejectedWithoutThrowing()
        {
            Object.DestroyImmediate(bank);

            Assert.DoesNotThrow(() => AudioManager.LoadBank(bank));
            Assert.AreEqual(0, AudioManager.GetRuntimeStats().LoadedBanks);
        }

        private static void DestroyIfAlive(Object value)
        {
            if (value != null) Object.DestroyImmediate(value);
        }
    }
}
