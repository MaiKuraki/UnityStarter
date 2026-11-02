// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Long-running registry, identity, and pool churn with periodic heap-floor sampling.
    /// </summary>
    /// <remarks>
    /// Gated by the <c>CYCLONE_AUDIO_SOAK_SECONDS</c> environment variable so the default suite stays
    /// fast. Run with e.g. <c>CYCLONE_AUDIO_SOAK_SECONDS=300</c>. The heap floor is sampled with a
    /// forced collection; the editor's per-thread allocation counter is not usable here.
    /// </remarks>
    public sealed class AudioSoakTests
    {
        private const int BankCount = 3;
        private const int EventsPerBank = 6;
        private const int ParametersPerBank = 4;
        private const float SampleIntervalSeconds = 15f;
        private const long MaximumHeapDriftBytes = 2L * 1024L * 1024L;

        private readonly List<AudioBank> banks = new List<AudioBank>(BankCount);
        private readonly List<Object> owned = new List<Object>(64);
        private readonly List<Object> transient = new List<Object>(32);

        private GameObject managerObject;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(AudioManager.Instance, "A previous test left an AudioManager instance alive.");

            managerObject = new GameObject("AudioSoakTests.AudioManager");
            managerObject.SetActive(false);
            managerObject.AddComponent<AudioListener>();

            AudioManager manager = managerObject.AddComponent<AudioManager>();
            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("customPoolSize").intValue = 4;
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

            DestroyTransient();
            DestroyAll(owned);
            banks.Clear();
        }

        [Test]
        [Timeout(900000)]
        public void Soak_RegistryIdentityAndPool_StayFlat()
        {
            int seconds = ResolveSoakSeconds();
            if (seconds <= 0)
            {
                Assert.Ignore("Set CYCLONE_AUDIO_SOAK_SECONDS to run the soak.");
                return;
            }

            for (int i = 0; i < 64; i++)
            {
                RunCycle(i);
            }

            long baseline = SampleHeapFloor();
            long minimum = baseline;
            long maximum = baseline;
            long lastSample = baseline;
            var samples = new StringBuilder();
            samples.Append("audio-soak baseline=").Append(baseline).Append(" bytes");

            float start = Time.realtimeSinceStartup;
            float nextSample = start + SampleIntervalSeconds;
            int cycles = 0;

            while (Time.realtimeSinceStartup - start < seconds)
            {
                RunCycle(cycles);
                cycles++;

                if (Time.realtimeSinceStartup >= nextSample)
                {
                    nextSample += SampleIntervalSeconds;
                    long sample = SampleHeapFloor();
                    minimum = Math.Min(minimum, sample);
                    maximum = Math.Max(maximum, sample);
                    lastSample = sample;
                    samples.Append(" | ").Append(sample);
                }
            }

            long finalSample = SampleHeapFloor();
            maximum = Math.Max(maximum, finalSample);
            minimum = Math.Min(minimum, finalSample);

            AudioRuntimeStats stats = AudioManager.GetRuntimeStats();
            TestContext.WriteLine(samples.ToString());
            TestContext.WriteLine(
                $"audio-soak cycles={cycles} seconds={seconds} drift={finalSample - baseline} range={maximum - minimum} " +
                $"banks={stats.LoadedBanks} events={stats.RegisteredEvents} active={stats.ActiveEvents} " +
                $"poolInUse={stats.PoolInUse} sourcePool={AudioManager.SourcePool.Count}");

            Assert.Greater(cycles, 0, "The soak loop did not run.");
            Assert.AreEqual(0, stats.LoadedBanks, "banks after soak");
            Assert.AreEqual(0, stats.RegisteredEvents, "events after soak");
            Assert.AreEqual(0, stats.RegisteredParameters, "parameters after soak");
            Assert.AreEqual(0, stats.ActiveEvents, "active events after soak");
            Assert.AreEqual(0, stats.PoolInUse, "sources still checked out after soak");
            Assert.AreEqual(
                AudioManager.SourcePool.Count,
                AudioManager.AvailableSources.Count,
                "every pooled source must be available after soak");

            long drift = Math.Abs(finalSample - baseline);
            Assert.LessOrEqual(
                drift,
                MaximumHeapDriftBytes,
                $"Heap floor drifted {drift} bytes over {seconds}s ({cycles} cycles).");
            Assert.LessOrEqual(
                maximum - minimum,
                MaximumHeapDriftBytes,
                $"Heap floor range {maximum - minimum} bytes exceeded the drift budget.");
        }

        private void RunCycle(int cycle)
        {
            for (int i = 0; i < banks.Count; i++)
            {
                AudioManager.LoadBank(banks[i]);
            }

            AudioRuntimeStats loaded = AudioManager.GetRuntimeStats();
            if (loaded.RegisteredEvents != BankCount * EventsPerBank)
            {
                Assert.Fail($"cycle {cycle}: expected {BankCount * EventsPerBank} events, got {loaded.RegisteredEvents}.");
            }

            Assert.IsTrue(AudioManager.TryGetEventId(EventName(0, 0), out AudioEventId eventId));
            Assert.IsTrue(eventId.IsValid);
            Assert.IsFalse(AudioManager.IsEventPlaying(eventId));

            for (int i = 0; i < 8; i++)
            {
                AudioSource source = AudioManager.GetUnusedSource();
                Assert.IsNotNull(source, $"cycle {cycle}: source pool exhausted");
                AudioManager.ReturnSourceToPool(source);
            }

            for (int i = 0; i < banks.Count; i++)
            {
                AudioManager.UnloadBank(banks[i]);
            }

            if ((cycle & 63) == 0)
            {
                ChurnTransientEvents(16);
            }
        }

        private void ChurnTransientEvents(int count)
        {
            DestroyTransient();

            for (int i = 0; i < count; i++)
            {
                var audioEvent = ScriptableObject.CreateInstance<AudioEvent>();
                audioEvent.name = "SoakTransient" + i;
                transient.Add(audioEvent);
                Assert.IsTrue(audioEvent.Id.IsValid);
            }
        }

        private void DestroyTransient()
        {
            DestroyAll(transient);
        }

        private static void DestroyAll(List<Object> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null) Object.DestroyImmediate(values[i]);
            }

            values.Clear();
        }

        private static long SampleHeapFloor()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(true);
        }

        private AudioBank CreateBank(int bankIndex)
        {
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.name = "SoakBank" + bankIndex;

            for (int i = 0; i < EventsPerBank; i++)
            {
                var audioEvent = ScriptableObject.CreateInstance<AudioEvent>();
                audioEvent.name = EventName(bankIndex, i);

                var output = ScriptableObject.CreateInstance<AudioOutput>();
                output.name = audioEvent.name + ".Output";

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

                owned.Add(audioEvent);
                owned.Add(output);
                bank.EditorEvents.Add(audioEvent);
            }

            for (int i = 0; i < ParametersPerBank; i++)
            {
                var parameter = ScriptableObject.CreateInstance<AudioParameter>();
                parameter.name = $"SoakBank{bankIndex}.Parameter{i}";
                owned.Add(parameter);
                bank.EditorParameters.Add(parameter);
            }

            owned.Add(bank);
            return bank;
        }

        private static string EventName(int bankIndex, int eventIndex)
        {
            return $"SoakBank{bankIndex}.Event{eventIndex}";
        }

        private static int ResolveSoakSeconds()
        {
            string raw = Environment.GetEnvironmentVariable("CYCLONE_AUDIO_SOAK_SECONDS");
            return int.TryParse(raw, out int seconds) && seconds > 0 ? seconds : 0;
        }
    }
}
