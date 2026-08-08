using Build.Pipeline.Editor;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class FakePlayerExtensionConfiguration :
        PlayerBuildExtensionConfiguration
    {
        internal const string ProviderIdValue = "test-player-extension";

        [SerializeField] private int revision;

        public override string ProviderId => ProviderIdValue;
    }
}
