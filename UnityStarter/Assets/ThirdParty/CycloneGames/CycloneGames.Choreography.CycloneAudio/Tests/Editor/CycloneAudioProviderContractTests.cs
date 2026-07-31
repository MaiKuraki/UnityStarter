using CycloneGames.Audio.Runtime;
using CycloneGames.Choreography.Core;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Choreography.CycloneAudio.Tests
{
    /// <summary>
    /// Verifies the provider's current constructor and diagnostics contracts.
    /// </summary>
    public sealed class CycloneAudioProviderContractTests
    {
        [Test]
        public void Provider_ExposesExplicitLogWriterConstructor()
        {
            Assert.That(
                typeof(CycloneAudioProvider).GetConstructor(new[]
                {
                    typeof(IAudioService),
                    typeof(GameObject),
                    typeof(ILogWriter),
                    typeof(ICycloneAudioBankState)
                }),
                Is.Not.Null);
        }
    }
}
