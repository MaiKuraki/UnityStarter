using System;
using CycloneGames.Audio.Runtime;
using CycloneGames.Choreography.Core;
using CycloneGames.Logging;
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

        [Tooltip("When true, skip event playback if the authored bank/group is not loaded in CycloneGames.Audio.")]
        [SerializeField] private bool ValidateBankState = true;

        private CycloneAudioProvider _provider;
        private IAudioService _audioService;
        private ICycloneAudioBankState _bankState;
        private ILogWriter _logWriter;
        private LogChannel _log = ChoreographyCycloneAudioLog.Channel;
        private bool _warnedUninitialized;

        public void Initialize(
            IAudioService audioService,
            ICycloneAudioBankState bankState = null)
        {
            InitializeCore(audioService, null, bankState);
        }

        public void Initialize(
            IAudioService audioService,
            ILogWriter logWriter,
            ICycloneAudioBankState bankState = null)
        {
            InitializeCore(
                audioService,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)),
                bankState);
        }

        private void InitializeCore(
            IAudioService audioService,
            ILogWriter logWriter,
            ICycloneAudioBankState bankState)
        {
            _audioService = audioService;
            _logWriter = logWriter;
            _log = logWriter == null
                ? ChoreographyCycloneAudioLog.Channel
                : ChoreographyCycloneAudioLog.Create(logWriter);
            _bankState = bankState;
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
                if (_audioService == null)
                {
                    _audioService = AudioManager.Instance;
                }
                BuildProvider();
            }
        }

        private void BuildProvider()
        {
            if (_audioService == null)
            {
                return;
            }

            if (_bankState == null && ValidateBankState && AudioManager.Instance != null)
            {
                _bankState = new AudioManagerBankState();
            }

            _provider = _logWriter == null
                ? new CycloneAudioProvider(
                    _audioService,
                    Emitter != null ? Emitter : gameObject,
                    _bankState)
                : new CycloneAudioProvider(
                    _audioService,
                    Emitter != null ? Emitter : gameObject,
                    _logWriter,
                    _bankState);
        }

        private void WarnUninitialized()
        {
            if (_warnedUninitialized)
            {
                return;
            }

            _warnedUninitialized = true;
            if (_log.IsEnabled(LogSeverity.Warning))
            {
                _log.Warning(
                    "CycloneAudioProviderBehaviour has no IAudioService or AudioManager.Instance; audio event playback is disabled.");
            }
        }
    }
}
