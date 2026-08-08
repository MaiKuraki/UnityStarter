using Build.Pipeline.Editor;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class UnregisteredPlayerExtensionConfiguration :
        PlayerBuildExtensionConfiguration
    {
        public override string ProviderId => "unregistered-player-extension";
    }
}
