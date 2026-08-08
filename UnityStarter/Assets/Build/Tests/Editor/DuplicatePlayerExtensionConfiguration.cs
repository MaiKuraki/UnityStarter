using Build.Pipeline.Editor;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class DuplicatePlayerExtensionConfiguration :
        PlayerBuildExtensionConfiguration
    {
        internal const string ProviderIdValue = "duplicate-player-extension";

        public override string ProviderId => ProviderIdValue;
    }
}
