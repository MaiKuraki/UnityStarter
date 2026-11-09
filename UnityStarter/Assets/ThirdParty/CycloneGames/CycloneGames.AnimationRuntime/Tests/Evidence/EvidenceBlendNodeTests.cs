using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Evidence for the blend-node idle policy. The node promises that zero-weight children are disconnected
    /// after a few updates; these cases prove the counter advances on the per-frame <c>SetTime</c> path, not only
    /// when the host re-applies weights.
    /// </summary>
    public sealed class EvidenceBlendNodeTests : EvidenceTestBase
    {
        private const int ChildCount = 8;

        private static void WalkParameterAcrossAllChildren(LinearBlendNode node)
        {
            // Each step connects one more child and drops the previous one to weight zero.
            for (int i = 0; i < ChildCount; i++)
            {
                node.SetParameter(i);
            }
        }

        private static int CountConnected(LinearBlendNode node)
        {
            int connected = 0;
            for (int i = 0; i < node.Count; i++)
            {
                if (node.IsChildConnected(i))
                {
                    connected++;
                }
            }

            return connected;
        }

        [Test]
        public void H14_SetTimeAdvancesTheIdleCounters_SoChildrenAreDisconnected()
        {
            CreateActor("H14Actor", out Animator animator);
            var clips = new AnimationClip[ChildCount];
            var thresholds = new float[ChildCount];
            for (int i = 0; i < ChildCount; i++)
            {
                clips[i] = CreateClip("H14Clip" + i, 1f);
                thresholds[i] = i;
            }

            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            LinearBlendNode perFrameSetTime = LinearBlendNode.Create(
                graph.PlayableGraph, clips, thresholds, BlendTimeMode.Normalized);

            try
            {
                WalkParameterAcrossAllChildren(perFrameSetTime);
                int connectedBefore = CountConnected(perFrameSetTime);

                // The cheap per-frame path a host uses while a character holds a direction.
                for (int i = 0; i < 1000; i++)
                {
                    perFrameSetTime.SetTime(i * 0.016d);
                }

                int connectedAfter = CountConnected(perFrameSetTime);

                NoteEvidence(
                    "H14",
                    "SetParameter walk -> " + connectedBefore + " connected; after 1000 x SetTime -> "
                    + connectedAfter + " connected, DisconnectCount=" + perFrameSetTime.DisconnectCount);

                Assert.AreEqual(ChildCount, connectedBefore,
                    "walking the parameter across every child leaves all of them connected");
                Assert.AreEqual(1, connectedAfter,
                    "only the child that still carries weight stays connected");
                Assert.AreEqual(ChildCount - 1, perFrameSetTime.DisconnectCount,
                    "the per-frame SetTime path advances the idle counter, as the node documents");
            }
            finally
            {
                perFrameSetTime.Dispose();
                graph.Dispose();
            }
        }

        [Test]
        public void H14b_ConstantDirectionAlsoConvergesToOneConnectedChild()
        {
            CreateActor("H14bActor", out Animator animator);
            var clips = new AnimationClip[ChildCount];
            var thresholds = new float[ChildCount];
            for (int i = 0; i < ChildCount; i++)
            {
                clips[i] = CreateClip("H14bClip" + i, 1f);
                thresholds[i] = i;
            }

            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            LinearBlendNode node = LinearBlendNode.Create(
                graph.PlayableGraph, clips, thresholds, BlendTimeMode.Normalized);

            try
            {
                WalkParameterAcrossAllChildren(node);
                int connectedBefore = CountConnected(node);

                // Holding one direction: SetParameter returns false and Apply is never reached.
                int applied = 0;
                for (int i = 0; i < 1000; i++)
                {
                    node.SetTime(i * 0.016d);
                    if (node.SetParameter(ChildCount - 1))
                    {
                        applied++;
                    }
                }

                NoteEvidence(
                    "H14b",
                    "1000 frames of SetTime + SetParameter(same value): SetParameter applied " + applied
                    + " times, connected now " + CountConnected(node) + " (was " + connectedBefore + ")");

                Assert.AreEqual(0, applied, "an unchanged parameter is rejected before Apply");
                Assert.AreEqual(1, CountConnected(node),
                    "holding a direction does not keep every child connected");
            }
            finally
            {
                node.Dispose();
                graph.Dispose();
            }
        }
    }
}
