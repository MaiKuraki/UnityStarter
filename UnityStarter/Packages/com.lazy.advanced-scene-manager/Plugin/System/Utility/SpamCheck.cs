using System;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Provides an easy way to check for spamming.</summary>
    public class SpamCheck
    {

        internal static SpamCheck EventMethods { get; } = new SpamCheck() { isEventMethods = true };

        /// <summary>Gets the global spam check.</summary>
        /// <remarks>Don't worry about conflicts with ASM stuff, we use a separate one.</remarks>
        public static SpamCheck Global { get; } = new SpamCheck();

        internal bool isEventMethods { get; set; } = false;

        bool m_isEnabled = true;
        /// <summary>Gets or sets if this <see cref="SpamCheck"/> is enabled.</summary>
        /// <remarks>When disabled actions will run without checking whatever it is a spam call.</remarks>
        public bool isEnabled
        {
            get => isEventMethods ? SceneManager.settings.project.preventSpammingEventMethods : m_isEnabled;
            set => m_isEnabled = value;
        }

        /// <summary>Gets if an action was executed not long enough ago.</summary>
        public bool IsSpam() =>
            isEnabled && (timeSinceLastExecute > 0 && timeSinceLastExecute <= executeCooldown);

        float cooldown = 0.5f;
        /// <summary>Gets or sets the cooldown.</summary>
        public float executeCooldown
        {
            get => isEventMethods ? Mathf.Abs(SceneManager.settings.project.spamCheckCooldown) : cooldown;
            set => Mathf.Abs(cooldown = value);
        }

        /// <summary>Gets the time an action was executed last.</summary>
        public float lastExecute { get; private set; }

        /// <summary>Gets the time an action was executed last.</summary>
        public float timeSinceLastExecute =>
          Mathf.Abs(GetTime() - lastExecute);

        /// <summary>Marks this spam check as executed, disallowing any actions until cooldown has run out.</summary>
        public void MarkAsExecuted() =>
            lastExecute = GetTime();

        float GetTime()
        {
#if UNITY_EDITOR
            return (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            return UnityEngine.Time.time;
#endif
        }

        /// <summary>Runs action if allowed.</summary>
        public void Execute(Action action)
        {
            if (!IsSpam())
            {
                action?.Invoke();
                MarkAsExecuted();
            }
            else if (isEventMethods)
                Debug.LogWarning("Spam check has blocked an event method.");
        }

    }

}
