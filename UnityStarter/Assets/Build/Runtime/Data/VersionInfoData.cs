using UnityEngine;

namespace Build.Data
{
    /// <summary>
    /// Build-time version metadata container for tracking resource package versions.
    /// 
    /// IMPORTANT: For displaying version information in-game, use Application.version in most cases.
    /// Application.version reflects the bundleVersion from ProjectSettings and represents the native
    /// binary version (APK/IPA/EXE version). This is what users see in app stores and should be your
    /// primary version identifier for the application itself.
    /// 
    /// This ScriptableObject stores build-specific metadata (Git commit info, build timestamps) and is
    /// primarily used for resource versioning in hot-update scenarios (e.g., YooAsset package versioning).
    /// Use this data only when you need to track or display resource/content version separately from the
    /// application version, such as in a dual-versioning system (App Version + Resource Version).
    /// </summary>
    [CreateAssetMenu(fileName = "VersionInfoData", menuName = "CycloneGames/Build/Version Info Data")]
    public class VersionInfoData : ScriptableObject
    {
        [Header("Build Information")]
        [Tooltip("The Git commit hash at the time of the build.")]
        public string commitHash;

        [Tooltip("The total number of commits at the time of the build.")]
        public string commitCount;

        [Tooltip("The date and time the build was created.")]
        public string buildDate;
    }
}