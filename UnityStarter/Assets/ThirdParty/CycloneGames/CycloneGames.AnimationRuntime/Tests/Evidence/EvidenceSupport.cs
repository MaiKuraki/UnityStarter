using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using CycloneGames.AnimationRuntime.Core;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Diagnostics seam that counts instead of logging. <see cref="Enabled"/> decides whether callers are
    /// allowed to format a message, which is exactly the contract of <see cref="IAnimationDiagnostics"/>.
    /// </summary>
    internal sealed class CountingDiagnostics : IAnimationDiagnostics
    {
        private readonly bool _enabled;

        internal int WarningCount;
        internal int ErrorCount;

        internal CountingDiagnostics(bool enabled)
        {
            _enabled = enabled;
        }

        public bool IsEnabled(AnimationDiagnosticLevel level)
        {
            return _enabled;
        }

        public void Report(AnimationDiagnosticLevel level, string message)
        {
            if (level == AnimationDiagnosticLevel.Error)
            {
                ErrorCount++;
            }
            else
            {
                WarningCount++;
            }
        }
    }

    /// <summary>
    /// Fault injection for host-supplied code that the graph calls during construction. Used only to prove how
    /// the constructor behaves when a dependency throws; it is not a behaviour any healthy host should have.
    /// </summary>
    internal sealed class ThrowingDiagnostics : IAnimationDiagnostics
    {
        public bool IsEnabled(AnimationDiagnosticLevel level)
        {
            throw new InvalidOperationException("Injected diagnostics failure.");
        }

        public void Report(AnimationDiagnosticLevel level, string message)
        {
            throw new InvalidOperationException("Injected diagnostics failure.");
        }
    }

    /// <summary>
    /// Shared helpers for the evidence suite. Every fixture destroys what it created so the order of tests in a
    /// run cannot influence the registry counters or the Animator ownership table.
    /// </summary>
    public abstract class EvidenceTestBase : LoggedTestBase
    {
        /// <summary>Project log channel for evidence lines. Tests must not call UnityEngine.Debug directly.</summary>
        private static readonly LogChannel EvidenceChannel =
            LogChannel.Create("CycloneGames.AnimationRuntime.Tests.Evidence");

        private readonly List<Object> _created = new List<Object>(32);

        [TearDown]
        public void DestroyCreatedObjects()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        protected GameObject CreateActor(string name, out Animator animator)
        {
            var go = new GameObject(name);
            _created.Add(go);
            animator = go.AddComponent<Animator>();
            // AnimationRuntime dispatches clip events itself; native Animator events would double-fire and log.
            animator.fireEvents = false;
            return go;
        }

        /// <summary>
        /// Runtime clip with a real curve, so <see cref="AnimationClip.length"/> is non-zero. An empty
        /// <c>new AnimationClip()</c> has length 0, which collapses every time assertion to zero.
        /// </summary>
        protected AnimationClip CreateClip(string name, float length)
        {
            var clip = new AnimationClip { name = name };
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(length, 1f));
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", curve);
            _created.Add(clip);
            return clip;
        }

        protected static AnimationTick Tick(long step, double time)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, false, 0.016f);
        }

        /// <summary>
        /// Publishes one measured value to three sinks: the module's <see cref="TestRunLog"/>, the project log
        /// channel (CycloneGames.Logging), and a persistent file. The channel alone is not enough here: its
        /// process writer is not installed in a batch-mode test run, so the line would be dropped, and
        /// <see cref="TestRunLog"/> writes into the project Temp folder, which a batch-mode run deletes on exit.
        /// </summary>
        protected static void NoteEvidence(string label, string message)
        {
            TestRunLog.Note(label, message);
            EvidenceChannel.Info("[EVIDENCE] " + label + " | " + message);
            EvidenceFileSink.Write(label, message, EvidenceChannel, LogRuntime.HasWriter);
        }

        protected static double Milliseconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>
        /// Heap trend measurement. <c>GC.GetAllocatedBytesForCurrentThread()</c> always returns 0 inside the
        /// editor's Mono host, so allocation evidence is collected as a trend of <see cref="GC.GetTotalMemory"/>
        /// around a warm-up plus a measurement loop.
        /// </summary>
        protected static long MeasureAllocationDelta(int warmupIterations, int measuredIterations, Action body)
        {
            for (int i = 0; i < warmupIterations; i++)
            {
                body();
            }

            long before = GC.GetTotalMemory(false);
            for (int i = 0; i < measuredIterations; i++)
            {
                body();
            }

            long after = GC.GetTotalMemory(false);
            return after - before;
        }

        protected static string FormatBytes(long bytes)
        {
            return bytes + " B (" + (bytes / 1024.0).ToString("0.0") + " KiB)";
        }

        protected static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "field '" + fieldName + "' not found on " + target.GetType().Name);
            field.SetValue(target, value);
        }

        protected static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "field '" + fieldName + "' not found on " + target.GetType().Name);
            return field.GetValue(target);
        }

        /// <summary>
        /// Removes an Animator from the process-wide ownership table. Only used to clean up state left behind by
        /// a failed construction; the module exposes no public API for this, which is itself part of
        /// the finding.
        /// </summary>
        protected static void RemoveAnimatorOwnership(Animator animator)
        {
            FieldInfo field = typeof(AnimationRuntimeRegistry).GetField(
                "_owners",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "the ownership table moved; update this cleanup helper");

            var table = (ConditionalWeakTable<Animator, AnimationRuntimeGraph>)field.GetValue(null);
            table.Remove(animator);
        }
    }
}
