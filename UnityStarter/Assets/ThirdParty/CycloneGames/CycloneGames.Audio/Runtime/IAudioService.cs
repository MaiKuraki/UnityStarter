using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    public interface IAudioService
    {
        /// <summary>
        /// Start playing an AudioEvent
        /// </summary>
        ActiveEvent PlayEvent(AudioEvent eventToPlay, GameObject emitterObject);

        /// <summary>
        /// Start playing an AudioEvent at a specific position
        /// </summary>
        ActiveEvent PlayEvent(AudioEvent eventToPlay, Vector3 position);

        /// <summary>
        /// Start playing an AudioEvent at a specific DSP time
        /// </summary>
        ActiveEvent PlayEventScheduled(AudioEvent eventToPlay, GameObject emitterObject, double dspTime);

        /// <summary>
        /// Stop all active instances of an audio event
        /// </summary>
        void StopAll(AudioEvent eventsToStop);

        /// <summary>
        /// Stop all active instances of a group
        /// </summary>
        void StopAll(int groupNum);
        
        /// <summary>
        /// Set the global volume for a specific mixer group (exposed parameter)
        /// </summary>
        void SetMixerVolume(string parameterName, float volume);
        
        /// <summary>
        /// Get the current global volume for a mixer group parameter
        /// </summary>
        float GetMixerVolume(string parameterName);
    }
}