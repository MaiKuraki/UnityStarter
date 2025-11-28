using UnityEngine;

namespace Build.Data
{
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