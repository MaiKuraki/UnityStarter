using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography.Audio
{
    /// <summary>
    /// MonoBehaviour wrapper that lets <see cref="UnityAudioProvider"/> be attached to a GameObject and discovered
    /// by <see cref="ChoreographySchedulerComponent"/> as an <see cref="IAudioProvider"/>. It owns the pooled
    /// audio-source root and forwards provider calls. A composition root must call <see cref="Initialize"/> with a
    /// resource resolver before playback; until then, begin calls are ignored with a one-time warning.
    /// </summary>
    public sealed class ChoreographyAudioProviderBehaviour : MonoBehaviour, IAudioProvider
    {
        [Tooltip("Number of AudioSource voices pre-created in the pool.")]
        [SerializeField] private int InitialPoolSize = 8;

        private UnityAudioProvider _provider;
        private IChoreographyDiagnostics _diagnostics;
        private bool _warnedUninitialized;

        /// <summary>Wires the resource resolver (typically the AssetManagement bridge) and builds the provider.</summary>
        public void Initialize(IUnityChoreographyResourceResolver resolver, IChoreographyDiagnostics diagnostics = null)
        {
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
            _provider = new UnityAudioProvider(transform, resolver, _diagnostics, InitialPoolSize);
        }

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
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

        private void Update()
        {
            _provider?.ReclaimFinishedOneShots();
        }

        private void OnDestroy()
        {
            _provider?.StopAll();
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
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.Audio",
                    "ChoreographyAudioProviderBehaviour used before Initialize; audio playback is disabled.");
            }
        }
    }
}
