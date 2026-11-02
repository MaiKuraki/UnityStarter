// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Virtualisation behaviour for looping 3D events: release on distance, resume with the correct
    /// playhead, budget enforcement, and a gated churn soak.
    /// </summary>
    public sealed class AudioLoopVirtualizationTests
    {
        private const float AudibleRadius = 10f;
        private const float FarDistance = 30f;

        private readonly List<Object> created = new List<Object>(64);
        private GameObject managerObject;
        private AudioClip clip;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(AudioManager.Instance, "A previous test left an AudioManager instance alive.");

            managerObject = new GameObject("AudioLoopVirtualizationTests.AudioManager");
            managerObject.SetActive(false);
            AudioListener listener = managerObject.AddComponent<AudioListener>();

            AudioManager manager = managerObject.AddComponent<AudioManager>();
            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("customPoolSize").intValue = 32;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            managerObject.SetActive(true);
            AudioManager.SetInstance(manager);

            // The scene ships a Main Camera listener at (0,1,-10); pin the harness listener at the
            // origin so distance assertions are deterministic.
            AudioManager.SetAudioListener(listener);

            AudioManager.LoopVirtualizationEnabled = true;
            AudioManager.LoopAudibleRadius = AudibleRadius;
            AudioManager.LoopResumeHysteresis = 0.85f;
            AudioManager.LoopVirtualizationFadeSeconds = 0.02f;
            AudioManager.MaxAudibleLoops = 32;
            AudioManager.MaxLoopResumesPerFrame = 8;

            clip = AudioClip.Create("VirtualizationTone", 44100, 1, 44100, false);
            created.Add(clip);
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.LoopVirtualizationEnabled = true;
            AudioManager.LoopAudibleRadius = 0f;
            AudioManager.MaxAudibleLoops = 32;

            if (AudioManager.Instance != null && managerObject != null)
            {
                AudioManager.ReleaseInstance(managerObject.GetComponent<AudioManager>());
            }

            if (managerObject != null)
            {
                Object.DestroyImmediate(managerObject);
                managerObject = null;
            }

            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
        }

        [Test]
        public void FarLoop_ReleasesSource()
        {
            AudioEvent audioEvent = CreateLoopingEvent("Virtual.Far");
            AudioManager.LoadBank(RegisterBank(audioEvent));

            ActiveEvent activeEvent = AudioManager.PlayEvent(audioEvent, new Vector3(0f, 0f, FarDistance));
            Assert.IsNotNull(activeEvent, "looping event failed to start");
            Assert.AreEqual(1, activeEvent.SourceCount);

            DriveTicks(activeEvent, 60);

            Assert.IsTrue(activeEvent.IsVirtualized, "a loop beyond the audible radius must virtualise");
            Assert.AreEqual(0, activeEvent.SourceCount, "the virtualised loop must release its source");
            Assert.AreEqual(0, AudioManager.GetRuntimeStats().PoolInUse, "the source must return to the pool");
        }

        [Test]
        public void NearLoop_ResumesWithAdvancedPlayhead()
        {
            AudioEvent audioEvent = CreateLoopingEvent("Virtual.Near");
            AudioManager.LoadBank(RegisterBank(audioEvent));

            ActiveEvent activeEvent = AudioManager.PlayEvent(audioEvent, new Vector3(0f, 0f, FarDistance));
            Assert.IsNotNull(activeEvent);

            DriveTicks(activeEvent, 60);
            Assert.IsTrue(activeEvent.IsVirtualized);

            // Advance a fixed simulated duration; wall-clock waiting would vary with machine speed.
            DriveTicks(activeEvent, 30);

            activeEvent.SetEmitterPosition(Vector3.zero);
            AudioManager.ResetLoopResumeBudget();
            DriveTicks(activeEvent, 120);

            AudioRuntimeStats diag = AudioManager.GetRuntimeStats();
            Assert.IsFalse(activeEvent.IsVirtualized, "a loop inside the radius must resume");
            Assert.AreEqual(1, diag.PoolInUse, "the resumed loop must hold exactly one source");
            Assert.AreEqual(1, activeEvent.SourceCount);

            AudioSource source = activeEvent.GetSource(0).source;
            Assert.IsNotNull(source);
            Assert.Greater(
                activeEvent.LastResumePlayhead,
                0.2f,
                "the resumed loop must continue from the advanced playhead, not restart");
        }

        [Test]
        public void BudgetCap_VirtualisesFarthestLoopsFirst()
        {
            AudioManager.MaxAudibleLoops = 2;

            var events = new List<ActiveEvent>(6);
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.name = "Virtual.BudgetBank";
            created.Add(bank);

            for (int i = 0; i < 6; i++)
            {
                AudioEvent audioEvent = CreateLoopingEvent("Virtual.Budget" + i);
                bank.EditorEvents.Add(audioEvent);

                ActiveEvent activeEvent = AudioManager.PlayEvent(audioEvent, new Vector3(0f, 0f, 2f + i * 2f));
                Assert.IsNotNull(activeEvent, $"event {i} failed to start");
                events.Add(activeEvent);
            }

            AudioManager.LoadBank(bank);

            // Populate spatial distances before the budget pass runs.
            for (int i = 0; i < events.Count; i++) DriveTicks(events[i], 1);

            InvokeLoopBudget();
            for (int i = 0; i < events.Count; i++) DriveTicks(events[i], 60);

            int audible = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (!events[i].IsVirtualized) audible++;
                TestContext.WriteLine(
                    $"budget diag[{i}]: virtualized={events[i].IsVirtualized} " +
                    $"sqrDist={events[i].VirtualizationSqrDistance:F0} sources={events[i].SourceCount} " +
                    $"state={events[i].VirtualizationStateCode}");
            }

            TestContext.WriteLine(
                $"budget diag: audible={audible} maxAudible={AudioManager.MaxAudibleLoops} " +
                $"candidates={AudioManager.LastBudgetCandidates} overflow={AudioManager.LastBudgetOverflow} " +
                $"started={AudioManager.LastBudgetStarted}");

            Assert.LessOrEqual(audible, 2, "the budget pass must leave at most MaxAudibleLoops audible");
            Assert.IsTrue(events[0].IsVirtualized == false, "the nearest loop must stay audible");
        }

        [Test]
        public void StopWhileVirtualized_ReleasesStateAndStaysFlat()
        {
            AudioEvent audioEvent = CreateLoopingEvent("Virtual.StopLeak");
            AudioManager.LoadBank(RegisterBank(audioEvent));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long baseline = GC.GetTotalMemory(true);

            const int cycles = 300;
            for (int i = 0; i < cycles; i++)
            {
                ActiveEvent activeEvent = AudioManager.PlayEvent(
                    audioEvent,
                    new Vector3(i * 2f, 0f, FarDistance));
                Assert.IsNotNull(activeEvent, $"cycle {i}: play failed ({DescribeState()})");

                DriveTicks(activeEvent, 60);
                Assert.IsTrue(activeEvent.IsVirtualized, $"cycle {i}: loop did not virtualise");

                AudioManager.StopAll(audioEvent);
                DriveTicks(activeEvent, 2);
                Assert.AreEqual(0, AudioManager.GetRuntimeStats().ActiveEvents, $"cycle {i}: event not stopped ({DescribeState()})");
            }

            long final = GC.GetTotalMemory(true);
            AudioRuntimeStats stats = AudioManager.GetRuntimeStats();

            TestContext.WriteLine(
                $"stop-while-virtualized: cycles={cycles} managedDrift={final - baseline} " +
                $"active={stats.ActiveEvents} poolInUse={stats.PoolInUse}");

            Assert.AreEqual(0, stats.ActiveEvents);
            Assert.AreEqual(0, stats.PoolInUse, "virtualised sources must be returned before the next cycle");
            Assert.LessOrEqual(Math.Abs(final - baseline), 4L * 1024L * 1024L, "managed heap drifted across stop cycles");
        }

        [Test]
        public void ResumeSpike_RespectsPerFrameBudget()
        {
            const int loopCount = 12;
            AudioManager.MaxLoopResumesPerFrame = 3;

            var bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.name = "Virtual.SpikeBank";
            created.Add(bank);

            var events = new List<ActiveEvent>(loopCount);
            for (int i = 0; i < loopCount; i++)
            {
                AudioEvent audioEvent = CreateLoopingEvent("Virtual.Spike" + i);
                bank.EditorEvents.Add(audioEvent);

                ActiveEvent activeEvent = AudioManager.PlayEvent(audioEvent, new Vector3(0f, 0f, FarDistance));
                Assert.IsNotNull(activeEvent, $"spike event {i} failed to start");
                events.Add(activeEvent);
            }

            AudioManager.LoadBank(bank);

            for (int i = 0; i < events.Count; i++)
            {
                DriveTicks(events[i], 60);
                Assert.IsTrue(events[i].IsVirtualized, $"spike event {i} did not virtualise");
            }

            int before = (int)AudioManager.TotalLoopResumes;
            for (int i = 0; i < events.Count; i++)
            {
                events[i].SetEmitterPosition(Vector3.zero);
            }

            AudioManager.ResetLoopResumeBudget();
            for (int i = 0; i < events.Count; i++) DriveTicks(events[i], 1);
            int resumedInOneTick = (int)AudioManager.TotalLoopResumes - before;

            TestContext.WriteLine($"resume spike: resumedInOneTick={resumedInOneTick} budget={AudioManager.MaxLoopResumesPerFrame}");
            Assert.LessOrEqual(
                resumedInOneTick,
                AudioManager.MaxLoopResumesPerFrame,
                "a single tick must not exceed the resume budget");

            // Once the budget is lifted the remaining loops must not be stuck virtualised.
            AudioManager.MaxLoopResumesPerFrame = 32;
            for (int tick = 0; tick < 30; tick++)
            {
                AudioManager.ResetLoopResumeBudget();
                for (int i = 0; i < events.Count; i++) DriveTicks(events[i], 1);
            }

            int stillVirtualized = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].IsVirtualized) stillVirtualized++;
            }

            Assert.AreEqual(0, stillVirtualized, "loops stayed virtualised after the budget was lifted");
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Soak_VirtualizationChurn_StaysFlat()
        {
            int seconds = ResolveSoakSeconds();
            if (seconds <= 0)
            {
                Assert.Ignore("Set CYCLONE_AUDIO_VIRT_SOAK_SECONDS to run the virtualisation soak.");
                yield break;
            }

            const int loopCount = 24;
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.name = "Virtual.SoakBank";
            created.Add(bank);

            var events = new List<ActiveEvent>(loopCount);
            for (int i = 0; i < loopCount; i++)
            {
                AudioEvent audioEvent = CreateLoopingEvent("Virtual.Soak" + i);
                bank.EditorEvents.Add(audioEvent);

                ActiveEvent activeEvent = AudioManager.PlayEvent(audioEvent, new Vector3(0f, 0f, 4f + i));
                Assert.IsNotNull(activeEvent, $"soak event {i} failed to start");
                events.Add(activeEvent);
            }

            AudioManager.LoadBank(bank);
            AudioManager.MaxAudibleLoops = 8;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long baseline = GC.GetTotalMemory(true);

            long min = baseline;
            long max = baseline;
            long virtualizationsAtStart = AudioManager.TotalLoopVirtualizations;
            long resumesAtStart = AudioManager.TotalLoopResumes;

            float start = Time.realtimeSinceStartup;
            float nextSample = start + 15f;
            int iterations = 0;
            double worstIterationMs = 0d;

            while (Time.realtimeSinceStartup - start < seconds)
            {
                // Alternate between a nearby and a distant placement so the soak exercises both
                // virtualise and resume paths instead of parking every loop in one state.
                bool near = (iterations / 120) % 2 == 0;
                for (int i = 0; i < events.Count; i++)
                {
                    events[i].SetEmitterPosition(new Vector3(0f, 0f, near ? 2f + i * 0.2f : 20f + i));
                }

                InvokeLoopBudget();
                AudioManager.ResetLoopResumeBudget();

                long iterationStart = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < events.Count; i++) DriveTicks(events[i], 1);
                double iterationMs = (System.Diagnostics.Stopwatch.GetTimestamp() - iterationStart)
                                     * (1000d / System.Diagnostics.Stopwatch.Frequency);
                if (iterationMs > worstIterationMs) worstIterationMs = iterationMs;
                iterations++;

                if (Time.realtimeSinceStartup >= nextSample)
                {
                    nextSample += 15f;
                    GC.Collect();
                    long sample = GC.GetTotalMemory(true);
                    min = Math.Min(min, sample);
                    max = Math.Max(max, sample);
                    yield return null;
                }
            }

            long final = GC.GetTotalMemory(true);
            min = Math.Min(min, final);
            max = Math.Max(max, final);

            long virtualizations = AudioManager.TotalLoopVirtualizations - virtualizationsAtStart;
            long resumes = AudioManager.TotalLoopResumes - resumesAtStart;

            TestContext.WriteLine(
                $"virtualisation-soak seconds={Time.realtimeSinceStartup - start:F1} iterations={iterations} " +
                $"virtualizations={virtualizations} resumes={resumes} " +
                $"managedRange={max - min} managedDrift={final - baseline} worstIterationMs={worstIterationMs:F3}");

            Assert.Greater(iterations, 0);
            Assert.Greater(virtualizations, 0, "the soak did not exercise virtualisation");
            Assert.Greater(resumes, 0, "the soak did not exercise resume");
            Assert.LessOrEqual(Math.Abs(final - baseline), 4L * 1024L * 1024L, "managed heap drifted during the soak");
        }

        private static string DescribeState()
        {
            AudioRuntimeStats stats = AudioManager.GetRuntimeStats();
            return $"active={stats.ActiveEvents} poolSize={stats.PoolCurrentSize} inUse={stats.PoolInUse} " +
                   $"available={AudioManager.AvailableSources.Count} instances={stats.ActiveEvents} " +
                   $"virtualizations={AudioManager.TotalLoopVirtualizations} resumes={AudioManager.TotalLoopResumes}";
        }

        private void DriveTicks(ActiveEvent activeEvent, int ticks)
        {
            const float step = 1f / 60f;
            for (int i = 0; i < ticks; i++)
            {
                activeEvent.UpdateInternal(step);
            }
        }

        private static int ResolveSoakSeconds()
        {
            string raw = Environment.GetEnvironmentVariable("CYCLONE_AUDIO_VIRT_SOAK_SECONDS");
            return int.TryParse(raw, out int seconds) && seconds > 0 ? seconds : 0;
        }

        private static void InvokeLoopBudget()
        {
            FieldInfo throttle = typeof(AudioManager).GetField(
                "nextLoopBudgetTime",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(throttle, "AudioManager.nextLoopBudgetTime layout changed.");
            throttle.SetValue(null, 0f);

            MethodInfo method = typeof(AudioManager).GetMethod(
                "EnforceAudibleLoopBudget",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "AudioManager.EnforceAudibleLoopBudget layout changed.");
            method.Invoke(null, null);
        }

        private AudioBank RegisterBank(AudioEvent audioEvent)
        {
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.name = "Virtual.Bank";
            created.Add(bank);
            bank.EditorEvents.Add(audioEvent);
            return bank;
        }

        private AudioEvent CreateLoopingEvent(string name)
        {
            var file = ScriptableObject.CreateInstance<AudioFile>();
            file.name = name + ".File";
            file.File = clip;
            created.Add(file);

            var output = ScriptableObject.CreateInstance<AudioOutput>();
            output.name = name + ".Output";
            created.Add(output);

            output.loop = true;
            output.spatialBlend = 1f;
            output.MinDistance = 1f;
            output.MaxDistance = 200f;
            output.attenuationCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

            var fileOutput = ScriptableObject.CreateInstance<AudioNodeOutput>();
            fileOutput.name = name + ".FileOutput";
            fileOutput.ParentNode = file;
            created.Add(fileOutput);

            var outputInput = ScriptableObject.CreateInstance<AudioNodeInput>();
            outputInput.name = name + ".OutputInput";
            outputInput.ParentNode = output;
            created.Add(outputInput);

            SetField(outputInput, "connectedNodes", new AudioNodeOutput[] { fileOutput });

            SetNodeField(output, "input", outputInput);
            SetNodeField(file, "output", fileOutput);

            var audioEvent = ScriptableObject.CreateInstance<AudioEvent>();
            audioEvent.name = name;
            created.Add(audioEvent);

            SetField(audioEvent, "nodes", new List<AudioNode> { file, output });
            SetField(audioEvent, "output", output);

            Assert.IsTrue(audioEvent.ValidateAudioFiles(), $"{name} graph did not validate");
            return audioEvent;
        }

        private static void SetNodeField(AudioNode node, string fieldName, Object value)
        {
            FieldInfo field = typeof(AudioNode).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"AudioNode.{fieldName} layout changed.");
            field.SetValue(node, value);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName} layout changed.");
            field.SetValue(target, value);
        }
    }
}
