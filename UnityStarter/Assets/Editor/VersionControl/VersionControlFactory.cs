namespace CycloneGames.Editor.VersionControl
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
                    throw new System.NotImplementedException("SVN support is not implemented.");
                default: return null;
            }
        }
    }
}