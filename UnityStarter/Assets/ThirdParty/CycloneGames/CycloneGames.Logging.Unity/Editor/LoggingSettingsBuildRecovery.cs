#if UNITY_EDITOR
using System;

namespace CycloneGames.Logging.Unity.Editor
{
    /// <summary>
    /// Explicit recovery entry point for interrupted LoggingSettings build overrides.
    /// This facade has no dependency on a project-specific build-pipeline assembly.
    /// </summary>
    public static class LoggingSettingsBuildRecovery
    {
        public static void Recover(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            LoggingSettingsBuildOverrideTransaction.Recover(projectRoot);
        }
    }
}
#endif
