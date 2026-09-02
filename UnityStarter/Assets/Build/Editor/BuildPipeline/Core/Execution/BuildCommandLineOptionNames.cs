namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Stable command-line tokens owned by this build pipeline. Unity's native
    /// <c>-buildTarget</c> token is intentionally reused; every custom token is
    /// isolated under the <c>-pipeline</c> namespace to avoid collisions with
    /// Unity Editor command-line arguments.
    /// </summary>
    public static class BuildCommandLineOptionNames
    {
        public const string Prefix = "-pipeline";
        public const string BuildTarget = "-buildTarget";
        public const string Profile = Prefix + "Profile";
        public const string ScriptingBackend = Prefix + "ScriptingBackend";
        public const string Output = Prefix + "Output";
        public const string Version = Prefix + "Version";
        public const string OutputRoot = Prefix + "OutputRoot";
        public const string VersionInfo = Prefix + "VersionInfo";
        public const string BuildNumber = Prefix + "BuildNumber";
        public const string SourceProvider = Prefix + "SourceProvider";
        public const string SourceRevision = Prefix + "SourceRevision";
        public const string SourceBranch = Prefix + "SourceBranch";
        public const string CiProvider = Prefix + "CiProvider";
        public const string CiRunId = Prefix + "CiRunId";
        public const string Recipe = Prefix + "Recipe";
        public const string Selection = Prefix + "Select";
        public const string StepConfiguration = Prefix + "StepConfig";
        public const string StepIncrementality = Prefix + "StepIncrementality";
        public const string StepDependency = Prefix + "StepDependency";
        public const string Development = Prefix + "Development";
        public const string ExportAndroidProject = Prefix + "ExportAndroidProject";
        public const string EnableCheat = Prefix + "EnableCheat";
        public const string DisableCheat = Prefix + "DisableCheat";
        public const string AllowExternalOutput = Prefix + "AllowExternalOutput";
        public const string RecoverOnly = Prefix + "RecoverOnly";
        public const string ReplaceExactVersion = Prefix + "ReplaceExactVersion";
    }
}
