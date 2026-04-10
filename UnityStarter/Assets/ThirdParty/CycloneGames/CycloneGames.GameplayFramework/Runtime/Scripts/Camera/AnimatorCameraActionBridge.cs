using System.Globalization;
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
    ///   PlayCameraActionTimed(string "actionKey@duration")
    ///       — plays with a runtime duration override (use '@' separator, e.g. "dodge@0.6").
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
        /// Plays with a duration override encoded as "actionKey@seconds".
        /// If the '@' separator is absent the key is used as-is with the configured duration.
        /// </summary>
        public void PlayCameraActionTimed(string actionKeyAndDuration)
        {
            if (string.IsNullOrEmpty(actionKeyAndDuration)) return;

            int sep = actionKeyAndDuration.LastIndexOf('@');
            if (sep < 0)
            {
                actionBinding?.PlayAction(actionKeyAndDuration);
                return;
            }

            string key = actionKeyAndDuration.Substring(0, sep);
            string durStr = actionKeyAndDuration.Substring(sep + 1);

            if (float.TryParse(durStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float dur))
                actionBinding?.PlayAction(key, dur);
            else
                actionBinding?.PlayAction(key);
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
