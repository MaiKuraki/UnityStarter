using System;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests
{
    /// <summary>
    /// PlayMode tests for the real PlayableGraph: deferred commit, release and handle lifetime.
    /// </summary>
    public sealed class AnimationRuntimeGraphPlayModeTests : LoggedTestBase
    {
        private GameObject _root;
        private Animator _animator;
        private AnimationRuntimeGraph _graph;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("AnimationRuntimeTestActor");
            _animator = _root.AddComponent<Animator>();
            _graph = new AnimationRuntimeGraph(
                _animator,
                new[] { AnimationLayerDescriptor.Default });
        }

        [TearDown]
        public void TearDown()
        {
            if (_graph != null)
            {
                _graph.Dispose();
                _graph = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        private static AnimationTick Tick(long step)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, 0.016f);
        }

        [Test]
        public void Constructor_NullAnimator_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new AnimationRuntimeGraph(null, new[] { AnimationLayerDescriptor.Default }));
        }

        [Test]
        public void BeginClip_NullClip_IsRejectedWithoutCounting()
        {
            AnimationPlaybackHandle handle = _graph.BeginClip(new AnimationClipRequest(1, 0, 0, null, 0d, 1f));

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, _graph.GetStats().ActivePlaybacks);
            Assert.AreEqual(0, _graph.GetStats().DroppedPlaybacks);
        }

        [Test]
        public void Commit_SameStepIndex_IsIdempotent()
        {
            var clip = new AnimationClip();
            try
            {
                _graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0.5d, 1f));

                AnimationTick tick = Tick(0);
                _graph.Commit(tick);
                long writesAfterFirst = _graph.GetStats().NativeWrites;
                long commitsAfterFirst = _graph.GetStats().Commits;

                _graph.Commit(tick);

                Assert.IsTrue(writesAfterFirst > 0L, "the first commit must touch the graph");
                Assert.AreEqual(writesAfterFirst, _graph.GetStats().NativeWrites, "a repeated step must not write again");
                Assert.AreEqual(commitsAfterFirst, _graph.GetStats().Commits);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void EndClip_ReleasesOnlyAtTheNextCommit()
        {
            var clip = new AnimationClip();
            try
            {
                var request = new AnimationClipRequest(7, 0, 0, clip, 0d, 1f);
                _graph.BeginClip(request);
                _graph.Commit(Tick(0));

                Assert.AreEqual(1, _graph.GetStats().ActivePlaybacks);

                _graph.EndClip(request.Key);
                Assert.AreEqual(1, _graph.GetStats().ActivePlaybacks, "release is deferred to the next commit");

                _graph.Commit(Tick(1));
                Assert.AreEqual(0, _graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void BeginClip_BeyondCapacity_DropsAndCounts()
        {
            var go = new GameObject("AnimationRuntimeCapacityActor");
            var animator = go.AddComponent<Animator>();
            var graph = new AnimationRuntimeGraph(animator, new[]
            {
                new AnimationLayerDescriptor(null, false, 1f, 1)
            });

            var first = new AnimationClip();
            var second = new AnimationClip();
            try
            {
                Assert.IsTrue(graph.BeginClip(new AnimationClipRequest(1, 0, 0, first, 0d, 1f)).IsValid);
                Assert.IsFalse(graph.BeginClip(new AnimationClipRequest(2, 0, 0, second, 0d, 1f)).IsValid);
                Assert.AreEqual(1L, graph.GetStats().DroppedPlaybacks);
                Assert.AreEqual(1, graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                graph.Dispose();
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SecondGraphOnTheSameAnimator_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new AnimationRuntimeGraph(_animator, new[] { AnimationLayerDescriptor.Default }));
        }

        [Test]
        public void Rebind_MovesOwnershipAndEndsPlaybacks()
        {
            var go = new GameObject("AnimationRuntimeRebindTarget");
            var animator = go.AddComponent<Animator>();
            var clip = new AnimationClip();
            try
            {
                _graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                _graph.Commit(Tick(0));

                Assert.AreEqual(1, _graph.GetStats().ActivePlaybacks);
                Assert.IsTrue(AnimationRuntimeRegistry.IsAnimatorOwned(_animator));

                Assert.IsTrue(_graph.TryRebind(animator));

                Assert.AreSame(animator, _graph.TargetAnimator);
                Assert.AreEqual(0, _graph.GetStats().ActivePlaybacks);
                Assert.IsFalse(AnimationRuntimeRegistry.IsAnimatorOwned(_animator));
                Assert.IsTrue(AnimationRuntimeRegistry.IsAnimatorOwned(animator));
            }
            finally
            {
                _graph.Dispose();
                _graph = null;
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Dispose_ReturnsRegistryCountersToTheirPreviousValue()
        {
            int graphsBefore = AnimationRuntimeRegistry.LiveGraphCount;
            int playbacksBefore = AnimationRuntimeRegistry.LivePlaybackCount;

            var go = new GameObject("AnimationRuntimeRegistryActor");
            var animator = go.AddComponent<Animator>();
            var clip = new AnimationClip();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0));

                Assert.AreEqual(graphsBefore + 1, AnimationRuntimeRegistry.LiveGraphCount);
                Assert.AreEqual(playbacksBefore + 1, AnimationRuntimeRegistry.LivePlaybackCount);

                graph.Dispose();

                Assert.AreEqual(graphsBefore, AnimationRuntimeRegistry.LiveGraphCount);
                Assert.AreEqual(playbacksBefore, AnimationRuntimeRegistry.LivePlaybackCount);
            }
            finally
            {
                graph.Dispose();
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            int graphsBefore = AnimationRuntimeRegistry.LiveGraphCount;

            _graph.Dispose();
            _graph.Dispose();

            Assert.AreEqual(graphsBefore - 1, AnimationRuntimeRegistry.LiveGraphCount);
        }

        [Test]
        public void Handle_BecomesDeadAfterTheReleaseCommit()
        {
            var clip = new AnimationClip();
            try
            {
                var request = new AnimationClipRequest(5, 0, 0, clip, 0d, 1f);
                AnimationPlaybackHandle handle = _graph.BeginClip(request);

                Assert.IsTrue(_graph.IsPlaybackAlive(handle));

                _graph.EndClip(request.Key);
                Assert.IsTrue(_graph.IsPlaybackAlive(handle), "the playback survives until the release commit");

                _graph.Commit(Tick(0));
                Assert.IsFalse(_graph.IsPlaybackAlive(handle));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void SetLayerWeight_ClampsAndIsReadable()
        {
            _graph.SetLayerWeight(0, 2f);
            Assert.AreEqual(1f, _graph.GetLayerWeight(0), 1e-6f);

            _graph.SetLayerWeight(0, -1f);
            Assert.AreEqual(0f, _graph.GetLayerWeight(0), 1e-6f);

            _graph.SetLayerWeight(99, 1f);
            Assert.AreEqual(0f, _graph.GetLayerWeight(99), 1e-6f);
        }
    }
}
