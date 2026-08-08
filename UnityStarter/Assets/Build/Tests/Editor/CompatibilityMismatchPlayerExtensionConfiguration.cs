using Build.Pipeline.Editor;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class CompatibilityMismatchPlayerExtensionConfiguration :
        PlayerBuildExtensionConfiguration
    {
        internal const string ProviderIdValue = "compatibility-mismatch-extension";

        public override string ProviderId => ProviderIdValue;
    }
}
