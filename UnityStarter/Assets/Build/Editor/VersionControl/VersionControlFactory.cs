using System.IO;
using UnityEngine;

namespace Build.VersionControl.Editor
{
    public enum VersionControlType
    {
        Git,
        Perforce,
        SVN
    }

    public static class VersionControlFactory
    {
        public static IVersionControlProvider CreateProvider(VersionControlType vcType)
        {
            switch (vcType)
            {
                case VersionControlType.Git:
                    return new VersionControlProviderGit();
                case VersionControlType.Perforce:
                    return new VersionControlProviderPerforce();
                case VersionControlType.SVN:
                    Debug.LogWarning("[VC] SVN is not implemented. Falling back to a placeholder provider (versions will be '0').");
                    return new VersionControlProviderFallback();
                default:
                    Debug.LogWarning($"[VC] Unknown version control type: {vcType}. Using fallback provider.");
                    return new VersionControlProviderFallback();
            }
        }

        /// <summary>
        /// Auto-detects the version control system in use by checking for
        /// well-known marker files/directories and environment variables.
        /// </summary>
        public static VersionControlType Detect()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // Walk upward to find Git root (handles nested Unity projects)
            string dir = Path.GetFullPath(Application.dataPath);
            string root = Path.GetPathRoot(dir);
            while (dir != null && dir.Length >= root.Length)
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                {
                    Debug.Log("[VC] Detected version control: Git");
                    return VersionControlType.Git;
                }
                if (Directory.Exists(Path.Combine(dir, ".svn")))
                {
                    Debug.Log("[VC] Detected version control: SVN");
                    return VersionControlType.SVN;
                }
                dir = Path.GetDirectoryName(dir);
            }

            // Check Perforce environment variables
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("P4PORT"))
                || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("P4USER"))
                || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("P4CLIENT")))
            {
                Debug.Log("[VC] Detected version control: Perforce (via environment variables)");
                return VersionControlType.Perforce;
            }

            Debug.Log("[VC] No version control detected. Defaulting to Git.");
            return VersionControlType.Git;
        }

        private sealed class VersionControlProviderFallback : VersionControlProviderBase
        {
            public override string GetCommitHash() => "0";
            public override string GetCommitCount() => "0";
            public override string GetBranchName() => "unknown";
            public override string GetCommitDate() => System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
