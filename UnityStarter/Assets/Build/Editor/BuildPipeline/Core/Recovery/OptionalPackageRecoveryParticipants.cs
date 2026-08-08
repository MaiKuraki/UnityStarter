using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Build.Pipeline.Editor
{
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class GameplayTagsRecoveryParticipant :
        IBuildRecoveryParticipant,
        IBuildRecoveryAvailability
    {
        public const string ParticipantId = "GameplayTags";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/gameplay-tags"
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public bool IsRecoveryAvailable(string projectRoot, out string unavailableReason)
        {
            return OptionalRecoveryFacadeInvoker.IsAvailable(
                "CycloneGames.GameplayTags.Unity.Editor.BuildTags",
                "CycloneGames.GameplayTags.Unity.Editor",
                out unavailableReason);
        }

        public void Recover(string projectRoot)
        {
            OptionalRecoveryFacadeInvoker.Recover(
                projectRoot,
                "CycloneGames.GameplayTags.Unity.Editor.BuildTags",
                "CycloneGames.GameplayTags.Unity.Editor",
                "GameplayTags");
        }
    }

    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class LoggingSettingsRecoveryParticipant :
        IBuildRecoveryParticipant,
        IBuildRecoveryAvailability
    {
        public const string ParticipantId = "LoggingSettings";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/logging-settings"
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public bool IsRecoveryAvailable(string projectRoot, out string unavailableReason)
        {
            return OptionalRecoveryFacadeInvoker.IsAvailable(
                "CycloneGames.Logging.Unity.Editor.LoggingSettingsBuildRecovery",
                "CycloneGames.Logging.Unity.Editor",
                out unavailableReason);
        }

        public void Recover(string projectRoot)
        {
            OptionalRecoveryFacadeInvoker.Recover(
                projectRoot,
                "CycloneGames.Logging.Unity.Editor.LoggingSettingsBuildRecovery",
                "CycloneGames.Logging.Unity.Editor",
                "Logging settings");
        }
    }

    internal static class OptionalRecoveryFacadeInvoker
    {
        private const string RecoveryMethodName = "Recover";

        internal static bool IsAvailable(
            string typeName,
            string assemblyName,
            out string unavailableReason)
        {
            if (TryResolve(typeName, assemblyName, out _, out unavailableReason))
            {
                return true;
            }

            unavailableReason +=
                " Reinstall the compatible package, let Unity finish compiling, then retry explicit workspace recovery.";
            return false;
        }

        internal static void Recover(
            string projectRoot,
            string typeName,
            string assemblyName,
            string displayName)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            if (!TryResolve(typeName, assemblyName, out MethodInfo method, out string reason))
            {
                throw new InvalidOperationException(
                    $"{displayName} recovery is unavailable. {reason} " +
                    "Reinstall the compatible package and retry; retained transaction evidence must not be deleted.");
            }

            try
            {
                method.Invoke(null, new object[] { projectRoot });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static bool TryResolve(
            string typeName,
            string assemblyName,
            out MethodInfo method,
            out string unavailableReason)
        {
            method = null;
            Type facade = ReflectionCache.GetType(typeName);
            if (facade == null)
            {
                unavailableReason =
                    $"Required recovery facade '{typeName}' is not loaded.";
                return false;
            }

            string actualAssembly = facade.Assembly.GetName().Name;
            if (!string.Equals(actualAssembly, assemblyName, StringComparison.Ordinal))
            {
                unavailableReason =
                    $"Recovery facade '{typeName}' was loaded from unexpected assembly '{actualAssembly}'.";
                return false;
            }

            method = ReflectionCache.GetMethod(
                facade,
                RecoveryMethodName,
                BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(string) });
            if (method == null || method.ReturnType != typeof(void))
            {
                method = null;
                unavailableReason =
                    $"Recovery facade '{typeName}' does not expose public static void Recover(string).";
                return false;
            }

            unavailableReason = string.Empty;
            return true;
        }
    }
}
