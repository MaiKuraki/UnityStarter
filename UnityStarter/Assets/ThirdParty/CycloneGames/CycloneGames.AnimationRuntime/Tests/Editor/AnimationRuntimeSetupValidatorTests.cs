using CycloneGames.AnimationRuntime.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests
{
    public sealed class AnimationRuntimeSetupValidatorTests
    {
        private readonly AnimationSetupFinding[] _findings =
            new AnimationSetupFinding[AnimationRuntimeSetupValidator.MaxFindings];

        private GameObject _root;
        private GameObject _child;

        [TearDown]
        public void TearDown()
        {
            if (_child != null)
            {
                Object.DestroyImmediate(_child);
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private Animator CreateAnimator(bool onChild, AnimatorUpdateMode mode = AnimatorUpdateMode.Normal)
        {
            _root = new GameObject("SetupRoot");
            Animator animator;

            if (onChild)
            {
                _child = new GameObject("SetupChild");
                _child.transform.SetParent(_root.transform);
                animator = _child.AddComponent<Animator>();
            }
            else
            {
                animator = _root.AddComponent<Animator>();
            }

            animator.updateMode = mode;
            return animator;
        }

        private int Validate(
            Animator animator,
            bool onThisGameObject,
            AnimationCommitPhase phase,
            RootMotionOwnership ownership,
            bool dispatch = true,
            bool animatorFireEvents = false,
            bool hasIKPass = false,
            bool pause = false,
            bool hasRenderer = true,
            bool hasTimeline = false)
        {
            return AnimationRuntimeSetupValidator.Validate(
                animator,
                onThisGameObject,
                phase,
                ownership,
                dispatch,
                animatorFireEvents,
                hasIKPass,
                pause,
                hasRenderer,
                hasTimeline,
                _findings);
        }

        [Test]
        public void MissingAnimator_IsABindingError()
        {
            int count = Validate(null, false, AnimationCommitPhase.LateUpdate, RootMotionOwnership.External);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Binding, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Error, _findings[0].Severity);
        }

        [Test]
        public void FixedUpdateWithoutAnimatePhysics_IsAFrameWarning()
        {
            Animator animator = CreateAnimator(false);

            int count = Validate(animator, true, AnimationCommitPhase.FixedUpdate, RootMotionOwnership.External);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Frame, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Warning, _findings[0].Severity);
        }

        [Test]
        public void AnimatePhysicsOutsideFixedUpdate_IsAFrameWarning()
        {
            Animator animator = CreateAnimator(false, AnimatorUpdateMode.AnimatePhysics);

            int count = Validate(animator, true, AnimationCommitPhase.LateUpdate, RootMotionOwnership.External);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Frame, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Warning, _findings[0].Severity);
        }

        [Test]
        public void BackendRootMotionOnAChildAnimator_IsAFrameError()
        {
            Animator animator = CreateAnimator(true);

            int count = Validate(animator, false, AnimationCommitPhase.LateUpdate, RootMotionOwnership.Backend);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Frame, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Error, _findings[0].Severity);
        }

        [Test]
        public void FireEventsWithClipEvents_IsASetupWarning()
        {
            Animator animator = CreateAnimator(false);

            int count = Validate(
                animator, true, AnimationCommitPhase.LateUpdate, RootMotionOwnership.External,
                dispatch: true, animatorFireEvents: true);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Setup, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Warning, _findings[0].Severity);
        }

        [Test]
        public void LateUpdateWithIKPass_IsAFrameWarning()
        {
            Animator animator = CreateAnimator(false);

            int count = Validate(
                animator, true, AnimationCommitPhase.LateUpdate, RootMotionOwnership.External,
                hasIKPass: true);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Frame, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Warning, _findings[0].Severity);
        }

        [Test]
        public void TimelineProviderWithClipEvents_IsASetupWarning()
        {
            Animator animator = CreateAnimator(false);

            int count = Validate(
                animator, true, AnimationCommitPhase.LateUpdate, RootMotionOwnership.External,
                dispatch: true, hasTimeline: true);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Setup, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Warning, _findings[0].Severity);
        }

        [Test]
        public void PauseWhenInvisibleWithoutRenderer_IsAFrameWarning()
        {
            Animator animator = CreateAnimator(false);

            int count = Validate(
                animator, true, AnimationCommitPhase.LateUpdate, RootMotionOwnership.External,
                pause: true, hasRenderer: false);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AnimationSetupField.Frame, _findings[0].Field);
            Assert.AreEqual(AnimationSetupSeverity.Warning, _findings[0].Severity);
        }

        [Test]
        public void ValidSetup_ProducesNoFindings()
        {
            Animator animator = CreateAnimator(false, AnimatorUpdateMode.AnimatePhysics);

            int count = Validate(
                animator, true, AnimationCommitPhase.FixedUpdate, RootMotionOwnership.Backend,
                dispatch: false, pause: false, hasRenderer: true, hasTimeline: false);

            Assert.AreEqual(0, count);
        }

        [Test]
        public void FindingsStopAtTheBufferCapacity()
        {
            Animator animator = CreateAnimator(true, AnimatorUpdateMode.AnimatePhysics);
            var small = new AnimationSetupFinding[1];

            int count = AnimationRuntimeSetupValidator.Validate(
                animator, false, AnimationCommitPhase.LateUpdate, RootMotionOwnership.Backend,
                true, false, false, true, false, true, small);

            Assert.AreEqual(1, count);
        }
    }
}
