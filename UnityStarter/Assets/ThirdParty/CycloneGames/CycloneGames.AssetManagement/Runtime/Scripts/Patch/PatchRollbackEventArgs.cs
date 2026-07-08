namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct PatchRollbackEventArgs
    {
        public readonly string PackageVersion;
        public readonly string RollbackVersion;
        public readonly bool Succeeded;

        public PatchRollbackEventArgs(string packageVersion, string rollbackVersion, bool succeeded)
        {
            PackageVersion = packageVersion;
            RollbackVersion = rollbackVersion;
            Succeeded = succeeded;
        }
    }
}
