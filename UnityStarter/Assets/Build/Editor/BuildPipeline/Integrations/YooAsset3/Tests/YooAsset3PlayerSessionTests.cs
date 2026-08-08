using NUnit.Framework;

namespace Build.Pipeline.Editor.Integrations.YooAsset3.Tests
{
    public sealed class YooAsset3PlayerSessionTests
    {
        [Test]
        public void PlayerSession_DoesNotClaimProcessGlobalExclusivity()
        {
            var adapter = new YooAsset3BuildAdapter();

            Assert.That(adapter.ExclusivePlayerSessionKey, Is.Empty);
        }
    }
}
