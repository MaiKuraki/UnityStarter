using System;
using System.Diagnostics.CodeAnalysis;
using Debug = UnityEngine.Debug;

namespace AdvancedSceneManager.Callbacks
{

    /// <summary>Contains utility functions for <see cref="Action"/>.</summary>
    public static class ActionUtility
    {

        /// <summary>Tries to invoke the action, then logs error to the console if an error occurred.</summary>
        public static void LogInvoke(this Action action)
        {
            if (!TryInvoke(action, out var e))
                Debug.LogException(e);
        }

        /// <summary>Tries to invoke the action, eats the exception.</summary>
        public static void TryInvoke(this Action action) =>
            TryInvoke(action, out _);

        /// <summary>Tries to invoke the action.</summary>
        /// <param name="action">The action to invoke.</param>
        /// <param name="exception">The exception that occurred when invoking action. <see langword="null"/> if <see langword="true"/> was returned.</param>
        /// <returns><see langword="true"/> if invoke succeeded with no exception.</returns>
        public static bool TryInvoke(this Action action, [NotNullWhen(false)] out Exception exception)
        {
            try
            {
                action?.Invoke();
                exception = null;
                return true;
            }
            catch (Exception e)
            {
                exception = e;
                return false;
            }
        }

    }

}
