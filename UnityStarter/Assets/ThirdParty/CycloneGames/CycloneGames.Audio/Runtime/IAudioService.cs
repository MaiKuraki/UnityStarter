using System;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Primary service interface for the CycloneGames audio system.
    /// <para>
    /// Supports both static access (<c>AudioManager.PlayEvent</c>) and constructor injection
    /// (<c>IAudioService</c>) for full compatibility with DI containers (VContainer, Zenject, etc.).
    /// </para>
    /// <para>
    /// All playback methods are thread-safe. Calls originating from worker threads are
    /// automatically deferred to the main thread via a lock-free command queue.
    /// </para>
    /// </summary>
    public interface IAudioService
    {
        /// <summary>
        /// Invoked after a bank has been fully unloaded and all <see cref="AudioSource.clip"/>
        /// references have been cleared. External asset management systems (e.g. CycloneGames.AssetManagement,
        /// Addressables) should subscribe to this event to release the underlying asset handles
        /// when the audio system no longer holds any references to the bank's clips.
        /// </summary>
        event Action<AudioBank> OnBankUnloaded;

        /// <summary>
        /// Begin playback of an <see cref="AudioEvent"/> attached to a GameObject for 3D spatial tracking.
        /// The AudioSource follows the emitter's transform each frame.
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent asset to play.</param>
        /// <param name="emitterObject">The GameObject whose transform the AudioSource will track.</param>
        /// <returns>A live <see cref="ActiveEvent"/> handle for runtime control (volume, stop, etc.), or <c>null</c> if playback failed.</returns>
        ActiveEvent PlayEvent(AudioEvent eventToPlay, GameObject emitterObject);

        /// <summary>
        /// Begin playback of an <see cref="AudioEvent"/> at a fixed world position (fire-and-forget spatial).
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent asset to play.</param>
        /// <param name="position">World-space position for the AudioSource.</param>
        /// <returns>A live <see cref="ActiveEvent"/> handle, or <c>null</c> if playback failed.</returns>
        ActiveEvent PlayEvent(AudioEvent eventToPlay, Vector3 position);

        /// <summary>
        /// Play a named event previously registered via <see cref="LoadBank"/>.
        /// </summary>
        /// <param name="eventName">Case-sensitive event name as registered in the bank.</param>
        /// <param name="emitterObject">The GameObject whose transform the AudioSource will track.</param>
        /// <returns>A live <see cref="ActiveEvent"/> handle, or <c>null</c> if the name is not registered.</returns>
        ActiveEvent PlayEvent(string eventName, GameObject emitterObject);

        /// <summary>
        /// Play a named event at a fixed world position.
        /// </summary>
        /// <param name="eventName">Case-sensitive event name as registered in the bank.</param>
        /// <param name="position">World-space position for the AudioSource.</param>
        /// <returns>A live <see cref="ActiveEvent"/> handle, or <c>null</c> if the name is not registered.</returns>
        ActiveEvent PlayEvent(string eventName, Vector3 position);

        /// <summary>
        /// Schedule playback at a precise DSP time for sample-accurate synchronization
        /// (e.g. beat-matched music transitions, rhythmic sequences).
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent asset to play.</param>
        /// <param name="emitterObject">The GameObject whose transform the AudioSource will track.</param>
        /// <param name="dspTime">Absolute DSP time (see <c>AudioSettings.dspTime</c>) at which playback begins.</param>
        /// <returns>A live <see cref="ActiveEvent"/> handle, or <c>null</c> if playback failed.</returns>
        ActiveEvent PlayEventScheduled(AudioEvent eventToPlay, GameObject emitterObject, double dspTime);

        /// <inheritdoc cref="PlayEventScheduled(AudioEvent, GameObject, double)"/>
        ActiveEvent PlayEventScheduled(string eventName, GameObject emitterObject, double dspTime);

        /// <summary>
        /// Stop all playing instances of a specific <see cref="AudioEvent"/>, applying the
        /// event's configured fade-out duration if one is defined.
        /// </summary>
        /// <param name="eventsToStop">The AudioEvent asset whose instances should be stopped.</param>
        void StopAll(AudioEvent eventsToStop);

        /// <summary>
        /// Stop all playing instances whose registered name matches <paramref name="eventName"/>.
        /// </summary>
        /// <param name="eventName">Case-sensitive event name as registered via <see cref="LoadBank"/>.</param>
        void StopAll(string eventName);

        /// <summary>
        /// Stop all playing instances belonging to the specified group number.
        /// Groups enable mutually exclusive playback channels (e.g. BGM group, dialog group).
        /// </summary>
        /// <param name="groupNum">The group ID assigned in the AudioEvent asset.</param>
        void StopAll(int groupNum);

        /// <summary>
        /// Pause all currently playing events. Paused AudioSources retain their playback position
        /// and can be resumed with <see cref="ResumeAll"/>.
        /// <para>
        /// Whether the system auto-calls this on app pause / focus loss is controlled by
        /// <see cref="AudioManager.FocusMode"/>. Pause and resume can be configured independently
        /// (e.g. auto-pause but manual resume). Set to <see cref="AudioFocusMode.None"/> to
        /// fully manage audio pause/resume from game code.
        /// </para>
        /// </summary>
        void PauseAll();

        /// <summary>
        /// Resume all events that were previously paused via <see cref="PauseAll"/>.
        /// </summary>
        void ResumeAll();

        /// <summary>
        /// Pause a single <see cref="ActiveEvent"/> while preserving its playback position.
        /// </summary>
        /// <param name="activeEvent">The event handle returned by a prior <c>PlayEvent</c> call.</param>
        void PauseEvent(ActiveEvent activeEvent);

        /// <summary>
        /// Resume a single <see cref="ActiveEvent"/> that was previously paused via <see cref="PauseEvent"/>.
        /// </summary>
        /// <param name="activeEvent">The event handle to resume.</param>
        void ResumeEvent(ActiveEvent activeEvent);

        /// <summary>
        /// Query whether any instance of a named event is currently in the <see cref="EventStatus.Played"/> state.
        /// </summary>
        /// <param name="eventName">Case-sensitive event name as registered via <see cref="LoadBank"/>.</param>
        /// <returns><c>true</c> if at least one active instance exists and is playing.</returns>
        bool IsEventPlaying(string eventName);

        /// <summary>
        /// Set the master output volume via <c>AudioListener.volume</c> (range 0–1).
        /// <para>
        /// This multiplier is applied <b>after</b> the entire AudioMixer pipeline, so it
        /// stacks correctly with per-bus mixer group volumes without altering their ratios.
        /// For per-bus control, use <see cref="SetMixerVolume"/> instead.
        /// </para>
        /// </summary>
        /// <param name="volume">Master volume in the range [0, 1].</param>
        void SetGlobalVolume(float volume);

        /// <summary>
        /// Get the current master output volume (0–1).
        /// </summary>
        /// <returns>The value last set by <see cref="SetGlobalVolume"/>, or the default (1.0).</returns>
        float GetGlobalVolume();

        /// <summary>
        /// Set volume on an AudioMixer exposed parameter (e.g. "MusicVolume", "SFXVolume").
        /// <para>
        /// Values are in decibels: <c>0 dB</c> = unity gain, <c>-80 dB</c> = silence.
        /// The parameter must be exposed in the AudioMixer asset assigned to the AudioManager.
        /// </para>
        /// </summary>
        /// <param name="exposedParameterName">The exposed parameter name as defined in the AudioMixer.</param>
        /// <param name="volumeDb">Volume in decibels.</param>
        void SetMixerVolume(string exposedParameterName, float volumeDb);

        /// <summary>
        /// Get the current value of an AudioMixer exposed parameter in decibels.
        /// </summary>
        /// <param name="exposedParameterName">The exposed parameter name as defined in the AudioMixer.</param>
        /// <returns>The current decibel value, or <c>0</c> if the mixer is not assigned or the parameter does not exist.</returns>
        float GetMixerVolume(string exposedParameterName);

        /// <summary>
        /// Load an <see cref="AudioBank"/> and register its events in the name-based lookup table.
        /// </summary>
        /// <param name="bank">The bank asset containing events to register.</param>
        /// <param name="overwriteExisting">
        /// When <c>true</c>, events whose names collide with previously registered entries will
        /// overwrite them. When <c>false</c> (default), colliding names are silently skipped.
        /// </param>
        void LoadBank(AudioBank bank, bool overwriteExisting = false);

        /// <summary>
        /// Unload an <see cref="AudioBank"/>: stops all active events originating from this bank,
        /// clears every <see cref="AudioSource.clip"/> reference held by the pooled sources, and
        /// removes the bank's events from the name registry.
        /// <para>
        /// After this method returns, the audio system holds <b>zero references</b> to the bank's
        /// <see cref="AudioClip"/> assets. It is therefore safe for external asset management systems
        /// (CycloneGames.AssetManagement, Addressables, Resources, etc.) to release or unload the
        /// underlying assets without risking dangling references or use-after-free.
        /// </para>
        /// <para>
        /// The <see cref="OnBankUnloaded"/> event is raised after all cleanup is complete, providing
        /// a hook for automated asset handle release in integrated pipelines.
        /// </para>
        /// </summary>
        /// <param name="bank">The bank asset to unload.</param>
        void UnloadBank(AudioBank bank);
    }
}