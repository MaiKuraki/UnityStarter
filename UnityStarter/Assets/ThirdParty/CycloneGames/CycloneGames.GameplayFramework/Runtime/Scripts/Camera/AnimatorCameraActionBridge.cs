using System;
using System.Threading;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Bridges Unity Animator Animation Events to CameraActionBinding.
    ///
    /// Add this component next to CameraActionBinding on the same GameObject, then
    /// add Animation Events on your animation clips that call its methods.
    ///
    /// Available Animation Event functions:
    ///   PlayCameraAction(string actionKey)
    ///       — plays the named preset using the entry / map configuration.
    ///
    ///   PlayCameraActionTimed(AnimationEvent animationEvent)
    ///       — reads the action key from stringParameter and the duration from floatParameter.
    ///
    ///   StopCameraAction(string actionKey)
    ///       — stops all active instances of the named preset.
    ///
    ///   StopAllCameraActions()
    ///       — stops every active camera preset immediately.
    /// </summary>
    [RequireComponent(typeof(CameraActionBinding))]
    public sealed class AnimatorCameraActionBridge : MonoBehaviour
    {
        [SerializeField] private CameraActionBinding actionBinding;
        private int ownerThreadId;
        private bool isInitialized;

        private void Awake()
        {
            BindOwnerThread();
            if (actionBinding == null)
            {
                actionBinding = GetComponent<CameraActionBinding>();
            }

            if (actionBinding == null)
            {
                throw new InvalidOperationException(
                    "AnimatorCameraActionBridge requires a CameraActionBinding.");
            }

            isInitialized = true;
        }

        // ── Animation Event callbacks ──────────────────────────────────────────

        /// <summary>Plays the preset registered under <paramref name="actionKey"/>.</summary>
        public void PlayCameraAction(string actionKey)
        {
            AssertReady();
            actionBinding.PlayAction(actionKey);
        }

        /// <summary>
        /// Plays with the Animation Event stringParameter as the action key and floatParameter
        /// as the duration override. A non-positive duration uses the configured duration.
        /// </summary>
        public void PlayCameraActionTimed(AnimationEvent animationEvent)
        {
            AssertReady();
            if (animationEvent == null || string.IsNullOrEmpty(animationEvent.stringParameter))
            {
                return;
            }

            actionBinding.PlayAction(
                animationEvent.stringParameter,
                animationEvent.floatParameter);
        }

        /// <summary>Stops all active instances of the preset registered under <paramref name="actionKey"/>.</summary>
        public void StopCameraAction(string actionKey)
        {
            AssertReady();
            actionBinding.StopAction(actionKey);
        }

        /// <summary>Stops every active camera preset immediately.</summary>
        public void StopAllCameraActions()
        {
            AssertReady();
            actionBinding.StopAllActions();
        }

        private void BindOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ownerThreadId != 0 && ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "AnimatorCameraActionBridge Unity lifecycle moved to a different owner thread.");
            }

            ownerThreadId = currentThreadId;
        }

        private void AssertOwnerThread()
        {
            int expectedThreadId = ownerThreadId;
            if (expectedThreadId == 0 ||
                Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "AnimatorCameraActionBridge live state must be accessed on its Awake owner thread.");
            }
        }

        private void AssertReady()
        {
            AssertOwnerThread();
            if (!isInitialized || actionBinding == null)
            {
                throw new InvalidOperationException(
                    "AnimatorCameraActionBridge live state is not available before Awake completes successfully.");
            }
        }
    }
}
