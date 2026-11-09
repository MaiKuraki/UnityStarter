using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Play-mode evidence: execution order, Animator binding when the Animator is not on the component's own
    /// GameObject, and the state a failed rebind leaves behind. These need the player loop, so they are skipped
    /// outside play mode.
    /// </summary>
    public sealed class EvidenceBehaviourPlayModeTests : EvidenceTestBase
    {
        // ---------------------------------------------------------------- H03

        [UnityTest]
        public IEnumerator H03_ExecutionOrderMinus100_CommitsBeforeDefaultOrderScripts()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Execution order evidence needs the player loop; run under PlayMode.");
            }

            GameObject lateRoot = null;
            GameObject updateRoot = null;

            try
            {
                lateRoot = CreateBehaviourActor(
                    "H03LateRoot",
                    AnimationCommitPhase.LateUpdate,
                    out AnimationRuntimeBehaviour lateBehaviour,
                    out CommitOrderProbe lateProbe);

                updateRoot = CreateBehaviourActor(
                    "H03UpdateRoot",
                    AnimationCommitPhase.Update,
                    out AnimationRuntimeBehaviour updateBehaviour,
                    out CommitOrderProbe updateProbe);

                // Two frames to let Awake and the first commit settle.
                yield return null;
                yield return null;

                var latePhaseSamples = new List<Vector2Int>(3);
                var updatePhaseSamples = new List<Vector2Int>(3);

                for (int i = 0; i < 3; i++)
                {
                    yield return null;
                    latePhaseSamples.Add(new Vector2Int(lateProbe.CommitsInUpdate, lateProbe.CommitsInLateUpdate));
                    updatePhaseSamples.Add(new Vector2Int(updateProbe.CommitsInUpdate, updateProbe.CommitsInLateUpdate));
                }

                // The probe keeps the default execution order, so each sample is a pair of readings from the
                // same frame: commits seen in Update, and commits seen in LateUpdate of the frame before (the
                // coroutine resumes between the two phases). One commit per frame makes the difference between
                // the two readings identify the phase the commit landed in.
                int latePhaseGap = latePhaseSamples[0].y - latePhaseSamples[0].x;
                int updatePhaseGap = updatePhaseSamples[0].y - updatePhaseSamples[0].x;

                NoteEvidence(
                    "H03",
                    "samples as (commits in Update, commits in LateUpdate): LateUpdate phase "
                    + Format(latePhaseSamples) + " gap " + latePhaseGap + "; Update phase "
                    + Format(updatePhaseSamples) + " gap " + updatePhaseGap);

                bool oneCommitPerFrame = true;
                for (int i = 1; i < latePhaseSamples.Count; i++)
                {
                    if (latePhaseSamples[i].x != latePhaseSamples[i - 1].x + 1)
                    {
                        oneCommitPerFrame = false;
                    }

                    if (updatePhaseSamples[i].x != updatePhaseSamples[i - 1].x + 1)
                    {
                        oneCommitPerFrame = false;
                    }

                    if (latePhaseSamples[i].y - latePhaseSamples[i].x != latePhaseGap)
                    {
                        oneCommitPerFrame = false;
                    }

                    if (updatePhaseSamples[i].y - updatePhaseSamples[i].x != updatePhaseGap)
                    {
                        oneCommitPerFrame = false;
                    }
                }

                Assert.IsTrue(oneCommitPerFrame, "every sample must advance by exactly one commit");

                // A coroutine resumes between the Update and the LateUpdate phase, so a sample is
                // (commits this frame's Update saw, commits the previous frame's LateUpdate saw).
                //   LateUpdate phase: Update sees N-1, LateUpdate sees N  -> gap 0, the commit sits in the
                //                     LateUpdate gap and lands before the default-order probe.
                //   Update phase:     Update sees N,   LateUpdate sees N -> gap -1, the commit already ran
                //                     before the default-order probe's Update in the same frame.
                Assert.AreEqual(0, latePhaseGap,
                    "in the LateUpdate phase the commit is already counted when a default-order LateUpdate "
                    + "runs, because DefaultExecutionOrder(-100) sorts the behaviour first");
                Assert.AreEqual(-1, updatePhaseGap,
                    "in the Update phase the commit has already run when a default-order Update runs, so "
                    + "gameplay state written in Update is only sampled one frame later");

                Assert.IsTrue(lateBehaviour.IsInitialized);
                Assert.IsTrue(updateBehaviour.IsInitialized);
            }
            finally
            {
                if (lateRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(lateRoot);
                }

                if (updateRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(updateRoot);
                }
            }
        }

        private GameObject CreateBehaviourActor(
            string name,
            AnimationCommitPhase phase,
            out AnimationRuntimeBehaviour behaviour,
            out CommitOrderProbe probe)
        {
            var go = new GameObject(name);
            go.SetActive(false);

            // The behaviour binds an Animator in Awake; without one it disables itself and never commits.
            go.AddComponent<Animator>();

            behaviour = go.AddComponent<AnimationRuntimeBehaviour>();
            SetPrivateField(behaviour, "_commitPhase", phase);

            probe = go.AddComponent<CommitOrderProbe>();
            probe.Behaviour = behaviour;

            go.SetActive(true);
            return go;
        }

        private static string Format(List<Vector2Int> samples)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < samples.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" / ");
                }

                builder.Append('(').Append(samples[i].x).Append(", ").Append(samples[i].y).Append(')');
            }

            return builder.ToString();
        }

        // ---------------------------------------------------------------- H04

        [UnityTest]
        public IEnumerator H04_ChildAnimator_PutsTheAnimatorAndTheRootMotionHandlerOnDifferentObjects()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Component lifecycle evidence needs play mode; run under PlayMode.");
            }

            var root = new GameObject("H04Root");
            root.SetActive(false);

            var child = new GameObject("H04Model");
            child.transform.SetParent(root.transform, false);
            var animator = child.AddComponent<Animator>();

            var behaviour = root.AddComponent<AnimationRuntimeBehaviour>();
            SetPrivateField(behaviour, "_rootMotionOwnership", RootMotionOwnership.Backend);

            var parentProbe = root.AddComponent<RootMotionProbe>();
            var childProbe = child.AddComponent<RootMotionProbe>();

            root.SetActive(true);

            AnimationClip clip = null;
            try
            {
                Assert.IsTrue(behaviour.IsInitialized,
                    "the behaviour must bind the child Animator when no Animator is assigned explicitly");
                Assert.AreSame(animator, behaviour.TargetAnimator);

                NoteEvidence(
                    "H04",
                    "behaviour object = " + behaviour.gameObject.name
                    + ", Animator object = " + behaviour.TargetAnimator.gameObject.name
                    + ", same object = " + ReferenceEquals(behaviour.gameObject, animator.gameObject));

                Assert.IsFalse(ReferenceEquals(behaviour.gameObject, animator.gameObject),
                    "OnAnimatorMove is a message on the Animator's GameObject, while the handler lives on the "
                    + "behaviour's GameObject, so this binding can never deliver the root-motion callback");
                Assert.IsFalse(behaviour.IsRootMotionActive,
                    "Backend ownership must be rejected (not silently inert) when the Animator is on a child");

                // Observation only: does the engine deliver OnAnimatorMove at all in this configuration?
                LogAssert.ignoreFailingMessages = true;
                clip = new AnimationClip { name = "H04RootMotion" };
                var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
                clip.SetCurve(string.Empty, typeof(Animator), "RootT.x", curve);

                behaviour.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                }

                NoteEvidence(
                    "H04",
                    "OnAnimatorMove deliveries after 5 frames: parent " + parentProbe.Calls
                    + ", child that owns the Animator " + childProbe.Calls
                    + ", frames seen by parent " + parentProbe.Frames
                    + ", IsRootMotionActive=" + behaviour.IsRootMotionActive);

                Assert.AreEqual(0, parentProbe.Calls,
                    "the behaviour's GameObject never receives the message, which is why Backend is rejected");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                UnityEngine.Object.DestroyImmediate(root);
                if (clip != null)
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                }
            }
        }

        [UnityTest]
        public IEnumerator H04b_BackendOnTheSameObject_IsActive()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Component lifecycle evidence needs play mode; run under PlayMode.");
            }

            var root = new GameObject("H04bRoot");
            root.SetActive(false);
            var animator = root.AddComponent<Animator>();
            var behaviour = root.AddComponent<AnimationRuntimeBehaviour>();
            SetPrivateField(behaviour, "_rootMotionOwnership", RootMotionOwnership.Backend);
            root.SetActive(true);

            try
            {
                Assert.IsTrue(behaviour.IsInitialized);
                Assert.IsTrue(behaviour.IsRootMotionActive,
                    "the supported same-object layout keeps Backend root motion working");
                Assert.IsTrue(animator.applyRootMotion);
                NoteEvidence("H04b", "same-object Backend: IsRootMotionActive=" + behaviour.IsRootMotionActive);
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- H18

        [UnityTest]
        public IEnumerator H18b_FailedRebind_LeavesTheComponentAndGraphInAgreement()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Component lifecycle evidence needs play mode; run under PlayMode.");
            }

            var root = new GameObject("H18bRoot");
            root.SetActive(false);
            var animatorA = root.AddComponent<Animator>();
            var behaviour = root.AddComponent<AnimationRuntimeBehaviour>();
            root.SetActive(true);

            CreateActor("H18bOther", out Animator animatorB);
            var otherOwner = new AnimationRuntimeGraph(animatorB, new[] { AnimationLayerDescriptor.Default });

            try
            {
                Assert.IsTrue(behaviour.IsInitialized);
                Assert.AreSame(animatorA, behaviour.TargetAnimator);

                bool moved = behaviour.TryRebind(animatorB);
                Assert.IsFalse(moved, "rebinding onto an Animator that another graph owns must be refused");

                NoteEvidence(
                    "H18b",
                    "after the refused rebind: TryRebind=" + moved
                    + ", behaviour.TargetAnimator=" + behaviour.TargetAnimator.name
                    + ", behaviour.IsInitialized=" + behaviour.IsInitialized
                    + ", graph.TargetAnimator=" + behaviour.Graph.TargetAnimator.name
                    + ", registry still owns the first Animator="
                    + AnimationRuntimeRegistry.IsAnimatorOwned(animatorA));

                Assert.AreSame(animatorA, behaviour.TargetAnimator,
                    "the component keeps the Animator it still drives");
                Assert.IsTrue(behaviour.IsInitialized, "the live graph was not torn down");
                Assert.AreSame(animatorA, behaviour.Graph.TargetAnimator,
                    "the graph and the component point at the same Animator");
                Assert.IsTrue(AnimationRuntimeRegistry.IsAnimatorOwned(animatorA));

                yield return null;
                yield return null;

                Assert.IsTrue(behaviour.IsInitialized, "a refused rebind is a no-op, not a permanent dead state");
                Assert.IsTrue(behaviour.TryRebind(animatorA), "rebinding to the current Animator succeeds");
            }
            finally
            {
                otherOwner.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator H26_TickDrivenBehaviour_PlaysWithoutUpdateClip()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Component lifecycle evidence needs play mode; run under PlayMode.");
            }

            var root = new GameObject("H26Root");
            root.SetActive(false);
            var animator = root.AddComponent<Animator>();
            var behaviour = root.AddComponent<AnimationRuntimeBehaviour>();
            SetPrivateField(behaviour, "_timePolicy", AnimationTimePolicy.TickDriven);
            root.SetActive(true);

            AnimationClip clip = new AnimationClip { name = "H26Clip" };
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(2f, 1f));
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", curve);

            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                Assert.AreEqual(AnimationTimePolicy.TickDriven, behaviour.TimePolicy);
                Assert.IsTrue(behaviour.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f)).IsValid);

                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                }

                int count = behaviour.Graph.CopyPlaybackSnapshots(0, buffer);
                Assert.AreEqual(1, count);
                NoteEvidence(
                    "H26",
                    "TickDriven behaviour after 5 frames without UpdateClip: playback time = "
                    + buffer[0].Time.ToString("0.000") + " s");

                Assert.Greater(buffer[0].Time, 0d,
                    "the component advances the clip from its own clock, no host time update required");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [UnityTest]
        public IEnumerator H28_CommitNow_AtLongMaxStep_WrapsToZero()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Component lifecycle evidence needs play mode; run under PlayMode.");
            }

            var root = new GameObject("H28Root");
            root.SetActive(false);
            root.AddComponent<Animator>();
            var behaviour = root.AddComponent<AnimationRuntimeBehaviour>();
            root.SetActive(true);

            try
            {
                yield return null;
                Assert.IsTrue(behaviour.IsInitialized);
                SetPrivateField(behaviour, "_stepIndex", long.MaxValue);
                behaviour.CommitNow();

                long step = (long)GetPrivateField(behaviour, "_stepIndex");
                NoteEvidence("H28", "step index after the long boundary = " + step);
                Assert.AreEqual(0L, step, "the step index wraps to 0 instead of becoming a negative, invalid step");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private sealed class RecordingLogWriter : ILogWriter
        {
            public readonly List<string> Messages = new List<string>();

            public bool IsEnabled(LogSeverity severity, string category)
            {
                return true;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                Messages.Add(message);
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                var builder = new StringBuilder();
                messageBuilder(builder);
                Messages.Add(builder.ToString());
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
                var builder = new StringBuilder();
                messageBuilder(state, builder);
                Messages.Add(builder.ToString());
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
                Messages.Add(message ?? exception.Message);
            }
        }

        [UnityTest]
        public IEnumerator H29_MultipleChildAnimators_ReportWhichOneIsUsed()
        {
            var root = new GameObject("H29Root");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform);
            var bodyAnimator = body.AddComponent<Animator>();

            var weapon = new GameObject("Weapon");
            weapon.transform.SetParent(root.transform);
            weapon.AddComponent<Animator>();

            ILogWriter previous = LogRuntime.Writer;
            var writer = new RecordingLogWriter();
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previous, writer));

            try
            {
                var behaviour = root.AddComponent<AnimationRuntimeBehaviour>();
                yield return null;

                Assert.IsTrue(behaviour.IsInitialized);
                Assert.AreEqual(bodyAnimator, behaviour.TargetAnimator,
                    "the fallback uses the first Animator in hierarchy order");
                Assert.IsTrue(
                    writer.Messages.Exists(message => message.Contains("Multiple Animators") && message.Contains("Body")),
                    "the fallback reports which Animator it chose");
            }
            finally
            {
                LogRuntime.TryReplaceWriter(writer, previous);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator H25_MissingAnimator_DisablesAndRecoversWhenOneAppears()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Component lifecycle evidence needs play mode; run under PlayMode.");
            }

            var root = new GameObject("H25Root");
            root.SetActive(false);
            var behaviour = root.AddComponent<AnimationRuntimeBehaviour>();
            root.SetActive(true);

            try
            {
                yield return null;

                Assert.IsFalse(behaviour.enabled, "a missing Animator disables the component instead of throwing");
                Assert.IsFalse(behaviour.IsInitialized);

                root.AddComponent<Animator>();
                behaviour.Initialize();

                NoteEvidence(
                    "H25",
                    "after adding an Animator and re-initializing: enabled=" + behaviour.enabled
                    + ", initialized=" + behaviour.IsInitialized);

                Assert.IsTrue(behaviour.IsInitialized);
                Assert.IsTrue(behaviour.enabled, "the component re-enables itself once initialization succeeds");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
