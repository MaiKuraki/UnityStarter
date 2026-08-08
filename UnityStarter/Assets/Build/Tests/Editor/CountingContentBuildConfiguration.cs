using Build.Pipeline.Editor;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class CountingContentBuildConfiguration : AssetContentBuildConfiguration
    {
        public override string ProviderId => CountingContentBuildAdapter.Provider;
    }
}
