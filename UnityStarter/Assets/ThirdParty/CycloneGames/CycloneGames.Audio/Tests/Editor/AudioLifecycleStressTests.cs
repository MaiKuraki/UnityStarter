// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Repeated load, unload, resolve, and pool cycles. Every case asserts that registries and pool
    /// counters return to their idle state, so a leak or double-release fails the run instead of
    /// accumulating silently.
    /// </summary>
    public sealed class AudioLifecycleStressTests
    {
        private const int BankCount = 3;
        private const int EventsPerBank = 6;
        private const int ParametersPerBank = 4;
        private const int StateGroupsPerBank = 2;

        private readonly List<AudioBank> banks = new List<AudioBank>(BankCount);
        private readonly List<Object> created = new List<Object>(64);

        private GameObject managerObject;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(AudioManager.Instance, "A previous test left an AudioManager instance alive.");

            managerObject = new GameObject("AudioLifecycleStressTests.AudioManager");
            managerObject.SetActive(false);
            managerObject.AddComponent<AudioListener>();

            AudioManager manager = managerObject.AddComponent<AudioManager>();
            var serializedManager = new SerializedObject(manager);
            SerializedProperty customPoolSize = serializedManager.FindProperty("customPoolSize");
            Assert.IsNotNull(customPoolSize, "AudioManager.customPoolSize could not be found.");
            customPoolSize.intValue = 4;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            managerObject.SetActive(true);
            AudioManager.SetInstance(manager);

            for (int bankIndex = 0; bankIndex < BankCount; bankIndex++)
            {
                banks.Add(CreateBank(bankIndex));
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AudioManager.Instance != null)
            {
                for (int i = 0; i < banks.Count; i++)
                {
                    if (banks[i] != null) AudioManager.UnloadBank(banks[i]);
                }
            }

            if (managerObject != null)
            {
                AudioManager.ReleaseInstance(managerObject.GetComponent<AudioManager>());
                Object.DestroyImmediate(managerObject);
                managerObject = null;
            }

            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            banks.Clear();
        }

        [Test]
        public void BankChurn_LeavesNoRegistrationResidue([Values(1, 50, 200)] int cycles)
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                for (int i = 0; i < banks.Count; i++)
                {
                    AudioManager.LoadBank(banks[i]);
                }

                AudioRuntimeStats loaded = AudioManager.GetRuntimeStats();
                Assert.AreEqual(BankCount, loaded.LoadedBanks, $"cycle {cycle}: loaded banks");
                Assert.AreEqual(BankCount * EventsPerBank, loaded.RegisteredEvents, $"cycle {cycle}: events");
                Assert.AreEqual(BankCount * ParametersPerBank, loaded.RegisteredParameters, $"cycle {cycle}: parameters");
                Assert.AreEqual(BankCount * StateGroupsPerBank, loaded.RegisteredStateGroups, $"cycle {cycle}: state groups");

                for (int i = 0; i < banks.Count; i++)
                {
                    AudioManager.UnloadBank(banks[i]);
                }

                AudioRuntimeStats unloaded = AudioManager.GetRuntimeStats();
                Assert.AreEqual(0, unloaded.LoadedBanks, $"cycle {cycle}: banks after unload");
                Assert.AreEqual(0, unloaded.RegisteredEvents, $"cycle {cycle}: events after unload");
                Assert.AreEqual(0, unloaded.RegisteredParameters, $"cycle {cycle}: parameters after unload");
                Assert.AreEqual(0, unloaded.RegisteredStateGroups, $"cycle {cycle}: state groups after unload");
            }
        }

        [Test]
        public void EventIdChurn_ResolvesThenInvalidates([Values(1, 64)] int cycles)
        {
            var ids = new List<AudioEventId>(EventsPerBank);

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                AudioManager.LoadBank(banks[0]);

                ids.Clear();
                var seen = new HashSet<int>();
                for (int i = 0; i < EventsPerBank; i++)
                {
                    string eventName = EventName(0, i);
                    Assert.IsTrue(AudioManager.TryGetEventId(eventName, out AudioEventId id), $"cycle {cycle}: resolve {eventName}");
                    Assert.IsTrue(id.IsValid);
                    Assert.IsTrue(seen.Add(id.Value), $"cycle {cycle}: duplicate id for {eventName}");
                    ids.Add(id);
                }

                Assert.AreEqual(
                    EventsPerBank,
                    AudioManager.GetRuntimeStats().RegisteredEvents,
                    $"cycle {cycle}: registered events");

                AudioManager.UnloadBank(banks[0]);

                for (int i = 0; i < ids.Count; i++)
                {
                    Assert.IsNull(AudioManager.PlayEvent(ids[i], (GameObject)null), $"cycle {cycle}: stale id must not play");
                    Assert.IsFalse(AudioManager.IsEventPlaying(ids[i]), $"cycle {cycle}: stale id must not report playing");
                }

                Assert.IsFalse(AudioManager.TryGetEventId(EventName(0, 0), out AudioEventId resolvedAfterUnload));
                Assert.IsFalse(resolvedAfterUnload.IsValid);
            }
        }

        [Test]
        public void SourcePoolChurn_KeepsPoolBalanced([Values(64, 512)] int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                AudioSource source = AudioManager.GetUnusedSource();
                Assert.IsNotNull(source, $"iteration {i}: pool returned no source");
                AudioManager.ReturnSourceToPool(source);
            }

            Assert.AreEqual(
                0,
                AudioManager.GetRuntimeStats().PoolInUse,
                "renting and returning must leave no source in use");

            Assert.AreEqual(
                AudioManager.SourcePool.Count,
                AudioManager.AvailableSources.Count,
                "every pooled source must be available again");
        }

        [Test]
        public void NoneHandle_IsRejectedByEveryEntryPoint()
        {
            Assert.IsNull(AudioManager.PlayEvent(AudioEventId.None, (GameObject)null));
            Assert.IsNull(AudioManager.PlayEvent(AudioEventId.None, Vector3.zero));
            Assert.IsFalse(AudioManager.IsEventPlaying(AudioEventId.None));

            AudioManager.StopAll(AudioEventId.None);
            Assert.AreEqual(0, AudioManager.GetRuntimeStats().ActiveEvents);
        }

        private AudioBank CreateBank(int bankIndex)
        {
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.name = $"StressBank{bankIndex}";
            created.Add(bank);

            for (int i = 0; i < EventsPerBank; i++)
            {
                AudioEvent audioEvent = CreateEvent(EventName(bankIndex, i));
                bank.EditorEvents.Add(audioEvent);
            }

            for (int i = 0; i < ParametersPerBank; i++)
            {
                var parameter = ScriptableObject.CreateInstance<AudioParameter>();
                parameter.name = $"StressBank{bankIndex}.Parameter{i}";
                created.Add(parameter);
                bank.EditorParameters.Add(parameter);
            }

            for (int i = 0; i < StateGroupsPerBank; i++)
            {
                var stateGroup = ScriptableObject.CreateInstance<AudioStateGroup>();
                stateGroup.name = $"StressBank{bankIndex}.StateGroup{i}";
                created.Add(stateGroup);
                bank.EditorStateGroups.Add(stateGroup);
            }

            return bank;
        }

        private AudioEvent CreateEvent(string eventName)
        {
            var audioEvent = ScriptableObject.CreateInstance<AudioEvent>();
            audioEvent.name = eventName;

            var output = ScriptableObject.CreateInstance<AudioOutput>();
            output.name = eventName + ".Output";

            // An output-only graph is valid and keeps the registry tests free of playback logs.
            FieldInfo outputField = typeof(AudioEvent).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(outputField, "AudioEvent.output layout changed.");
            outputField.SetValue(audioEvent, output);

            FieldInfo nodesField = typeof(AudioEvent).GetField("nodes", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(nodesField, "AudioEvent.nodes layout changed.");
            (nodesField.GetValue(audioEvent) as List<AudioNode>)?.Add(output);

            created.Add(audioEvent);
            created.Add(output);
            return audioEvent;
        }

        private static string EventName(int bankIndex, int eventIndex)
        {
            return $"StressBank{bankIndex}.Event{eventIndex}";
        }
    }
}
