using CycloneGames.Audio.Runtime;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography.CycloneAudio
{
    /// <summary>
    /// Scene component wrapper for <see cref="CycloneAudioProvider"/>. It can be auto-discovered by
    /// Choreography scheduler/player components as an <see cref="IAudioProvider"/>.
    /// </summary>
    public sealed class CycloneAudioProviderBehaviour : MonoBehaviour, IAudioProvider
    {
        [Tooltip("Emitter object passed to CycloneGames.Audio when playing event cues. Leave empty to use this GameObject.")]
        [SerializeField] private GameObject Emitter;

        [Tooltip("Stop tracked duration events when this provider is destroyed.")]
        [SerializeField] private bool StopTrackedEventsOnDestroy = true;

        private CycloneAudioProvider _provider;
        private IAudioService _audioService;
        private IChoreographyDiagnostics _diagnostics;
        private bool _warnedUninitialized;

        public void Initialize(IAudioService audioService, IChoreographyDiagnostics diagnostics = null)
        {
            _audioService = audioService;
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
            BuildProvider();
        }

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
            EnsureProvider();
            if (_provider == null)
            {
                WarnUninitialized();
                return;
            }

            _provider.BeginClip(in sample);
        }

        public void UpdateClip(in ChoreographyPlaybackSample sample)
        {
            _provider?.UpdateClip(in sample);
        }

        public void EndClip(in ChoreographyClipStop stop)
        {
            _provider?.EndClip(in stop);
        }

        private void OnDestroy()
        {
            if (StopTrackedEventsOnDestroy)
            {
                _provider?.StopAll();
            }
        }

        private void EnsureProvider()
        {
            if (_provider == null)
            {
                _diagnostics ??= NullChoreographyDiagnostics.Instance;
                _audioService ??= AudioManager.Instance;
                BuildProvider();
            }
        }

        private void BuildProvider()
        {
            if (_audioService == null)
            {
                return;
            }

            _provider = new CycloneAudioProvider(_audioService, Emitter != null ? Emitter : gameObject, _diagnostics);
        }

        private void WarnUninitialized()
        {
            if (_warnedUninitialized)
            {
                return;
            }

            _warnedUninitialized = true;
            if (_diagnostics != null && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.CycloneAudio",
                    "CycloneAudioProviderBehaviour has no IAudioService or AudioManager.Instance; audio event playback is disabled.");
            }
        }
    }
}
