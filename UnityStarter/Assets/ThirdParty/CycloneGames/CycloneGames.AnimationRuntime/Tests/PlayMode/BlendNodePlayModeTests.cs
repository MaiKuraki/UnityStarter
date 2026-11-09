using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests
{
    /// <summary>
    /// PlayMode tests for the blend nodes. They need a real PlayableGraph, so they cannot live in EditMode.
    /// </summary>
    public sealed class BlendNodePlayModeTests : LoggedTestBase
    {
        private GameObject _root;
        private Animator _animator;
        private AnimationRuntimeGraph _graph;
        private readonly AnimationClip[] _clips = new AnimationClip[4];

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("AnimationRuntimeBlendTest");
            _animator = _root.AddComponent<Animator>();
            _graph = new AnimationRuntimeGraph(_animator, new[] { AnimationLayerDescriptor.Default });

            for (int i = 0; i < _clips.Length; i++)
            {
                _clips[i] = new AnimationClip { name = "Clip" + i };
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_graph != null)
            {
                _graph.Dispose();
                _graph = null;
            }

            for (int i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] != null)
                {
                    Object.DestroyImmediate(_clips[i]);
                    _clips[i] = null;
                }
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        [Test]
        public void LinearBlend_ExactThreshold_IsPure()
        {
            using (LinearBlendNode node = LinearBlendNode.Create(
                _graph.PlayableGraph,
                new[] { _clips[0], _clips[1], _clips[2] },
                new[] { 0f, 1f, 2f }))
            {
                node.SetParameter(1f);

                Assert.AreEqual(0f, node.GetAppliedWeight(0), 1e-5f);
                Assert.AreEqual(1f, node.GetAppliedWeight(1), 1e-5f);
                Assert.AreEqual(0f, node.GetAppliedWeight(2), 1e-5f);
            }
        }

        [Test]
        public void LinearBlend_Midpoint_SplitsEvenly()
        {
            using (LinearBlendNode node = LinearBlendNode.Create(
                _graph.PlayableGraph,
                new[] { _clips[0], _clips[1] },
                new[] { 0f, 1f }))
            {
                node.SetParameter(0.5f);

                Assert.AreEqual(0.5f, node.GetAppliedWeight(0), 1e-5f);
                Assert.AreEqual(0.5f, node.GetAppliedWeight(1), 1e-5f);
            }
        }

        [Test]
        public void LinearBlend_IdleChildIsDisconnected()
        {
            using (LinearBlendNode node = LinearBlendNode.Create(
                _graph.PlayableGraph,
                new[] { _clips[0], _clips[1], _clips[2] },
                new[] { 0f, 1f, 2f }))
            {
                node.SetParameter(0.5f);
                Assert.IsTrue(node.IsChildConnected(1), "a contributing child must be connected");

                node.SetParameter(2f);
                for (int i = 0; i < 12; i++)
                {
                    node.Apply();
                }

                Assert.IsFalse(node.IsChildConnected(1), "a child at weight zero must eventually stop being evaluated");
                Assert.IsTrue(node.IsChildConnected(2));
            }
        }

        [Test]
        public void DirectionalBlend_BetweenChildren_SplitsEvenly()
        {
            using (DirectionalBlendNode node = DirectionalBlendNode.Create(
                _graph.PlayableGraph,
                new[] { _clips[0], _clips[1], _clips[2], _clips[3] },
                new[] { 0f, 90f, 180f, 270f }))
            {
                node.SetParameter(45f, 1f);

                Assert.AreEqual(0.5f, node.GetAppliedWeight(0), 1e-4f);
                Assert.AreEqual(0.5f, node.GetAppliedWeight(1), 1e-4f);
            }
        }

        [Test]
        public void DirectionalBlend_DeadZone_PrefersCenter()
        {
            using (DirectionalBlendNode node = DirectionalBlendNode.Create(
                _graph.PlayableGraph,
                new[] { _clips[0], _clips[1], _clips[2], _clips[3] },
                new[] { 0f, 90f, 180f, 270f },
                centerIndex: 0,
                deadZone: 0.1f))
            {
                node.SetParameter(123f, 0.05f);

                Assert.AreEqual(1f, node.GetAppliedWeight(0), 1e-5f);
            }
        }

        [Test]
        public void BlendNode_TakesOverLayer_AndRejectsClipPlaybacks()
        {
            using (LinearBlendNode node = LinearBlendNode.Create(
                _graph.PlayableGraph,
                new[] { _clips[0], _clips[1] },
                new[] { 0f, 1f }))
            {
                Assert.IsTrue(_graph.SetLayerSource(0, node.Playable));
                Assert.IsTrue(_graph.IsLayerExternallySourced(0));

                AnimationPlaybackHandle handle = _graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, _clips[0], 0d, 1f));

                Assert.IsFalse(handle.IsValid, "a layer driven by an external node must reject clip playbacks");
            }
        }

        [Test]
        public void DisposingBlendNode_ClearsTheLayerOnNextCommit()
        {
            LinearBlendNode node = LinearBlendNode.Create(
                _graph.PlayableGraph,
                new[] { _clips[0], _clips[1] },
                new[] { 0f, 1f });

            Assert.IsTrue(_graph.SetLayerSource(0, node.Playable));
            node.Dispose();

            _graph.Commit(new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, 0.016f));

            Assert.IsFalse(_graph.IsLayerExternallySourced(0), "the layer must self-heal when its source dies");
        }

        [Test]
        public void LinearBlend_RejectsMismatchedInput()
        {
            Assert.Throws<System.ArgumentException>(() =>
                LinearBlendNode.Create(_graph.PlayableGraph, new[] { _clips[0], _clips[1] }, new[] { 0f }));

            Assert.Throws<System.ArgumentException>(() =>
                LinearBlendNode.Create(_graph.PlayableGraph, new[] { _clips[0], _clips[1] }, new[] { 1f, 1f }));
        }
    }
}
