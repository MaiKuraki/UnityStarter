using UnityEditor;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Single source of truth for whether a build transaction may start in the
    /// current Unity Editor process.
    /// </summary>
    internal static class EditorBuildAvailabilityPolicy
    {
        internal static bool IsBusy()
        {
            return EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode
                || UnityEditor.BuildPipeline.isBuildingPlayer;
        }
    }
}
