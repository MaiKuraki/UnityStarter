using CycloneGames.Audio.Runtime;
using CycloneGames.Choreography.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Choreography.CycloneAudio.Tests
{
    public sealed class CycloneAudioProviderCompatibilityTests
    {
        [Test]
        public void Provider_PreservesLegacyClrConstructor()
        {
            Assert.That(
                typeof(CycloneAudioProvider).GetConstructor(new[]
                {
                    typeof(IAudioService),
                    typeof(GameObject),
                    typeof(IChoreographyDiagnostics),
                    typeof(ICycloneAudioBankState)
                }),
                Is.Not.Null);
        }
    }
}
