using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.AnimationRuntime.Tests
{
    /// <summary>
    /// Paths the stress suite does not reach: voice reuse under a full layer, handle ABA, external-source
    /// hand-over, multi-actor isolation and blend-node connection churn.
    /// </summary>
    public sealed class AnimationRuntimeReliabilityTests : LoggedTestBase
    {
        private readonly List<GameObject> _created = new List<GameObject>(8);
        private readonly List<AnimationClip> _clips = new List<AnimationClip>(8);

        [TearDown]
        public void Cleanup()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i] != null)
                {
                    Object.DestroyImmediate(_clips[i]);
                }
            }

            _created.Clear();
            _clips.Clear();
        }

        private AnimationRuntimeGraph CreateGraph(string name, params AnimationLayerDescriptor[] layers)
        {
            var go = new GameObject(name);
            _created.Add(go);
            var animator = go.AddComponent<Animator>();
            return new AnimationRuntimeGraph(
                animator,
                layers != null && layers.Length > 0 ? layers : new[] { AnimationLayerDescriptor.Default });
        }

        private AnimationClip CreateClip(string name)
        {
            var clip = new AnimationClip { name = name };
            _clips.Add(clip);
            return clip;
        }

        private static AnimationTick Tick(long step)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, 0d, 0.016f);
        }

        [Test]
        public void ReBegin_WhenLayerIsFull_UpdatesInsteadOfDropping()
        {
            AnimationRuntimeGraph graph = CreateGraph("RebeginActor", new AnimationLayerDescriptor(null, false, 1f, 1));
            AnimationClip first = CreateClip("First");
            AnimationClip second = CreateClip("Second");

            try
            {
                Assert.IsTrue(graph.BeginClip(new AnimationClipRequest(1, 0, 0, first, 0d, 1f)).IsValid);
                graph.Commit(Tick(0));

                AnimationVoiceHandle replaced = graph.BeginClip(new AnimationClipRequest(1, 0, 0, second, 0d, 1f));

                Assert.IsTrue(replaced.IsValid, "re-begin of the same key must reuse its slot, not be dropped");
                graph.Commit(Tick(1));

                Assert.AreEqual(1, graph.GetStats().ActiveVoices);
                Assert.AreEqual(0L, graph.GetStats().DroppedVoices);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void Handle_FromThePreviousGeneration_IsDead_AfterSlotReuse()
        {
            AnimationRuntimeGraph graph = CreateGraph("AbaActor", new AnimationLayerDescriptor(null, false, 1f, 1));
            AnimationClip clip = CreateClip("Aba");

            try
            {
                var request = new AnimationClipRequest(7, 0, 0, clip, 0d, 1f);
                AnimationVoiceHandle first = graph.BeginClip(request);
                graph.Commit(Tick(0));

                graph.EndClip(request.Key);
                graph.Commit(Tick(1));

                AnimationVoiceHandle second = graph.BeginClip(request);
                graph.Commit(Tick(2));

                Assert.IsTrue(second.IsValid);
                Assert.IsFalse(graph.IsVoiceAlive(first), "a handle from the previous generation must be dead");
                Assert.IsTrue(graph.IsVoiceAlive(second));
                Assert.AreNotEqual(first.Generation, second.Generation);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void ClearLayerSource_ReturnsTheLayerToClipVoices()
        {
            AnimationRuntimeGraph graph = CreateGraph("SourceHandoverActor");
            AnimationClip a = CreateClip("HandA");
            AnimationClip b = CreateClip("HandB");

            LinearBlendNode node = LinearBlendNode.Create(
                graph.PlayableGraph,
                new[] { a, b },
                new[] { 0f, 1f });

            try
            {
                Assert.IsTrue(graph.SetLayerSource(0, node.Playable));
                Assert.IsFalse(graph.BeginClip(new AnimationClipRequest(1, 0, 0, a, 0d, 1f)).IsValid,
                    "a layer driven by an external node must reject clip voices");

                graph.ClearLayerSource(0);

                Assert.IsTrue(graph.BeginClip(new AnimationClipRequest(1, 0, 0, a, 0d, 1f)).IsValid,
                    "after clearing the source the layer must accept clip voices again");
            }
            finally
            {
                node.Dispose();
                graph.Dispose();
            }
        }

        [Test]
        public void SetLayerSource_FromAnotherGraph_IsRejected()
        {
            AnimationRuntimeGraph first = CreateGraph("SourceOwnerA");
            AnimationRuntimeGraph second = CreateGraph("SourceOwnerB");
            AnimationClip a = CreateClip("CrossA");
            AnimationClip b = CreateClip("CrossB");

            LinearBlendNode foreign = LinearBlendNode.Create(
                second.PlayableGraph,
                new[] { a, b },
                new[] { 0f, 1f });

            try
            {
                Assert.IsFalse(first.SetLayerSource(0, foreign.Playable),
                    "a source created in another PlayableGraph must be rejected");
                Assert.IsFalse(first.IsLayerExternallySourced(0));
            }
            finally
            {
                foreign.Dispose();
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void TwoActors_AreIsolated_WhenOneIsDisposed()
        {
            AnimationRuntimeGraph first = CreateGraph("IsolatedA");
            AnimationRuntimeGraph second = CreateGraph("IsolatedB");
            AnimationClip clip = CreateClip("Isolated");

            try
            {
                AnimationVoiceHandle a = first.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                AnimationVoiceHandle b = second.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                first.Commit(Tick(0));
                second.Commit(Tick(0));

                first.Dispose();

                Assert.IsTrue(second.IsVoiceAlive(b), "disposing one actor must not touch another");
                Assert.IsFalse(first.IsVoiceAlive(a));

                second.Commit(Tick(1));
                Assert.AreEqual(1, second.GetStats().ActiveVoices);
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void BlendNode_OscillatingParameter_DoesNotThrashConnections()
        {
            AnimationRuntimeGraph graph = CreateGraph("ChurnActor");
            AnimationClip a = CreateClip("ChurnA");
            AnimationClip b = CreateClip("ChurnB");
            AnimationClip c = CreateClip("ChurnC");

            LinearBlendNode node = LinearBlendNode.Create(
                graph.PlayableGraph,
                new[] { a, b, c },
                new[] { 0f, 1f, 2f });

            try
            {
                node.SetParameter(0.5f);
                int baseline = node.ConnectCount;

                // Alternate between two disjoint pairs every frame. The idle counter is 8 updates, so a
                // one-frame excursion to zero must not disconnect anything.
                for (int i = 0; i < 1000; i++)
                {
                    node.SetParameter((i & 1) == 0 ? 0.5f : 1.5f);
                    node.Apply();
                }

                int reconnects = node.ConnectCount - baseline;

                Assert.AreEqual(0, node.DisconnectCount, "a one-frame excursion must not disconnect a child");
                Assert.LessOrEqual(reconnects, 3, "reconnects stay bounded under oscillation");
            }
            finally
            {
                node.Dispose();
                graph.Dispose();
            }
        }

        [Test]
        public void StopAll_ThenReplay_Works()
        {
            AnimationRuntimeGraph graph = CreateGraph("StopAllActor");
            AnimationClip clip = CreateClip("StopAll");

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0));
                Assert.AreEqual(1, graph.GetStats().ActiveVoices);

                graph.StopAll();
                graph.Commit(Tick(1));
                Assert.AreEqual(0, graph.GetStats().ActiveVoices);

                AnimationVoiceHandle replay = graph.BeginClip(new AnimationClipRequest(2, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(2));

                Assert.IsTrue(replay.IsValid);
                Assert.AreEqual(1, graph.GetStats().ActiveVoices);
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
