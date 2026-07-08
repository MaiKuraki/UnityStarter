using System;
using System.Collections.Generic;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Choreography.CycloneAudio
{
    /// <summary>
    /// Narrow bank-state contract used by Choreography audio playback before triggering event cues.
    /// Project integrations can implement this for Wwise, FMOD, or custom bank registries without changing Core.
    /// </summary>
    public interface ICycloneAudioBankState
    {
        bool IsBankLoaded(string bankId);
    }

    /// <summary>
    /// Default CycloneGames.Audio bank-state adapter backed by <see cref="AudioManager.GetLoadedBanks"/>.
    /// </summary>
    public sealed class AudioManagerBankState : ICycloneAudioBankState
    {
        public bool IsBankLoaded(string bankId)
        {
            if (string.IsNullOrEmpty(bankId))
            {
                return true;
            }

            IReadOnlyCollection<AudioBank> banks = AudioManager.GetLoadedBanks();
            if (banks == null || banks.Count == 0)
            {
                return false;
            }

            foreach (AudioBank bank in banks)
            {
                if (bank != null && string.Equals(bank.name, bankId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
