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
    public class AnimatorCameraActionBridge : MonoBehaviour
    {
        [SerializeField] private CameraActionBinding actionBinding;

        private void Awake()
        {
            if (actionBinding == null)
                actionBinding = GetComponent<CameraActionBinding>();
        }

        // ── Animation Event callbacks ──────────────────────────────────────────

        /// <summary>Plays the preset registered under <paramref name="actionKey"/>.</summary>
        public void PlayCameraAction(string actionKey)
        {
            actionBinding?.PlayAction(actionKey);
        }

        /// <summary>
        /// Plays with the Animation Event stringParameter as the action key and floatParameter
        /// as the duration override. A non-positive duration uses the configured duration.
        /// </summary>
        public void PlayCameraActionTimed(AnimationEvent animationEvent)
        {
            if (animationEvent == null || string.IsNullOrEmpty(animationEvent.stringParameter))
            {
                return;
            }

            actionBinding?.PlayAction(
                animationEvent.stringParameter,
                animationEvent.floatParameter);
        }

        /// <summary>Stops all active instances of the preset registered under <paramref name="actionKey"/>.</summary>
        public void StopCameraAction(string actionKey)
        {
            actionBinding?.StopAction(actionKey);
        }

        /// <summary>Stops every active camera preset immediately.</summary>
        public void StopAllCameraActions()
        {
            actionBinding?.StopAllActions();
        }
    }
}
