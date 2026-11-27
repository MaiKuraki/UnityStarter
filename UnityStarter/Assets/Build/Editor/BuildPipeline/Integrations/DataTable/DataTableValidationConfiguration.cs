using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Authoring for the <c>data-table-validation</c> step. The step runs the
    /// DataTable pipeline's own <c>check</c> command so a manual verification and a
    /// build gate share one definition of "consistent".
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Build/Data Table Validation Config")]
    public sealed class DataTableValidationConfiguration : ScriptableObject
    {
        public const string DefaultLauncherRelativePath =
            "../DataTable/Luban/gen_code_bin_to_project_lazyload";

        [Tooltip("Pipeline profile passed to the check command.")]
        public string profile = "client";

        [Tooltip("Launcher path relative to the Unity project root, without the platform extension. Empty resolves DefaultLauncherRelativePath. The step appends .bat on Windows and .sh elsewhere, so one asset works on every platform.")]
        public string launcherRelativePath = string.Empty;

        [Tooltip("Maximum time the check command may run before the step reports failure.")]
        public int timeoutSeconds = 600;
    }
}
