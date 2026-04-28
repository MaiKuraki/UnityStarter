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

        private sealed class VersionControlProviderFallback : VersionControlProviderBase
        {
            public override string GetCommitHash() => "0";
            public override string GetCommitCount() => "0";
            public override string GetBranchName() => "unknown";
            public override string GetCommitDate() => System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
