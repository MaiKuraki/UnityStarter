// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Threading;
using CycloneGames.Logging;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Audio.Runtime.Integrations.Localization
{
    public enum AudioLocalizationDiagnosticCode : byte
    {
        InvalidLocalizationLocale = 0,
        MappingUnavailable = 1,
        MapperException = 2,
        VoiceLocaleRejected = 3,
        VoiceLocaleTargetException = 4,
        LastKnownGoodRestoreFailed = 5,
    }

    public enum AudioLocalizationDiagnosticSeverity : byte
    {
        Warning = 0,
        Error = 1,
    }

    public readonly struct AudioLocalizationDiagnostic
    {
        public AudioLocalizationDiagnostic(
            AudioLocalizationDiagnosticCode code,
            AudioLocalizationDiagnosticSeverity severity,
            string message,
            LocaleId localizationLocale,
            AudioVoiceLocaleSnapshot voiceLocale,
            long localizationRevision,
            Exception exception = null)
        {
            Code = code;
            Severity = severity;
            Message = message;
            LocalizationLocale = localizationLocale;
            VoiceLocale = voiceLocale;
            LocalizationRevision = localizationRevision;
            Exception = exception;
        }

        public AudioLocalizationDiagnosticCode Code { get; }
        public AudioLocalizationDiagnosticSeverity Severity { get; }
        public string Message { get; }
        public LocaleId LocalizationLocale { get; }
        public AudioVoiceLocaleSnapshot VoiceLocale { get; }
        public long LocalizationRevision { get; }
        public Exception Exception { get; }
    }

    /// <summary>
    /// One-way synchronization from committed Localization locale changes to Audio voice-locale
    /// state. This bridge owns neither service and does not load catalogs, banks, or clips.
    /// </summary>
    public sealed class AudioLocalizationBridge : IDisposable
    {
        private static readonly LogChannel Log = AudioLocalizationRuntimeLog.Channel;

        private readonly ILocalizationService localization;
        private readonly IAudioVoiceLocaleControl audio;
        private readonly IAudioLocalizationMapper mapper;
        private readonly Action<AudioLocalizationDiagnostic> diagnosticSink;
        private readonly int ownerThreadId;

        private AudioVoiceLocaleSnapshot lastKnownGoodVoiceLocale;
        private PendingLocale pendingLocale;
        private long lastProcessedLocalizationRevision = long.MinValue;
        private bool hasPendingLocale;
        private bool isApplying;
        private bool isBound;
        private bool isDisposed;

        private readonly struct PendingLocale
        {
            public PendingLocale(LocaleId locale, long revision)
            {
                Locale = locale;
                Revision = revision;
            }

            public LocaleId Locale { get; }
            public long Revision { get; }
        }

        public AudioLocalizationBridge(
            ILocalizationService localization,
            IAudioVoiceLocaleControl audio,
            IAudioLocalizationMapper mapper = null,
            Action<AudioLocalizationDiagnostic> diagnosticSink = null)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "AudioLocalizationBridge must be created on the Unity main thread.");
            }

            this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
            this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
            this.mapper = mapper ?? IdentityAudioLocalizationMapper.Instance;
            this.diagnosticSink = diagnosticSink;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool IsBound
        {
            get
            {
                EnsureOwnerThread();
                return isBound;
            }
        }

        public AudioVoiceLocaleSnapshot LastKnownGoodVoiceLocale
        {
            get
            {
                EnsureOwnerThread();
                return lastKnownGoodVoiceLocale;
            }
        }

        public long LastProcessedLocalizationRevision
        {
            get
            {
                EnsureOwnerThread();
                return lastProcessedLocalizationRevision;
            }
        }

        /// <summary>
        /// Subscribes to future locale changes and immediately synchronizes the currently committed
        /// localization locale. The localization service must already be initialized.
        /// </summary>
        public void Bind()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            if (isBound)
                return;
            if (!localization.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the localization service before binding audio localization.");
            }

            lastProcessedLocalizationRevision = long.MinValue;
            hasPendingLocale = false;
            TryCaptureCurrentVoiceLocale();

            localization.Changed += HandleLocalizationChanged;
            isBound = true;
            try
            {
                QueueLocale(localization.CurrentLocale, localization.Revision);
            }
            catch
            {
                UnbindCore();
                throw;
            }
        }

        public void Unbind()
        {
            EnsureOwnerThread();
            UnbindCore();
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            if (isDisposed)
                return;

            UnbindCore();
            isDisposed = true;
        }

        private void HandleLocalizationChanged(LocalizationChange change)
        {
            EnsureOwnerThread();
            if (!isBound)
                return;

            if (change.Reason == LocalizationChangeReason.Shutdown)
            {
                UnbindCore();
                return;
            }

            if (change.Reason == LocalizationChangeReason.LocaleChanged)
                QueueLocale(change.CurrentLocale, change.Revision);
        }

        private void QueueLocale(LocaleId locale, long revision)
        {
            if (!isBound || revision <= lastProcessedLocalizationRevision)
                return;

            if (!locale.IsValid)
            {
                lastProcessedLocalizationRevision = revision;
                Report(new AudioLocalizationDiagnostic(
                    AudioLocalizationDiagnosticCode.InvalidLocalizationLocale,
                    AudioLocalizationDiagnosticSeverity.Warning,
                    "The committed localization locale is invalid; the current voice locale was preserved.",
                    locale,
                    default,
                    revision));
                return;
            }

            if (hasPendingLocale && revision <= pendingLocale.Revision)
                return;

            pendingLocale = new PendingLocale(locale, revision);
            hasPendingLocale = true;
            if (isApplying)
                return;

            isApplying = true;
            try
            {
                while (isBound && hasPendingLocale)
                {
                    PendingLocale next = pendingLocale;
                    hasPendingLocale = false;
                    if (next.Revision <= lastProcessedLocalizationRevision)
                        continue;

                    // Mark the revision before invoking external code so a reentrant duplicate
                    // cannot be applied twice. A newer reentrant revision is retained in pendingLocale.
                    lastProcessedLocalizationRevision = next.Revision;
                    ApplyLocale(next);
                }
            }
            finally
            {
                isApplying = false;
                if (!isBound)
                    hasPendingLocale = false;
            }
        }

        private void ApplyLocale(PendingLocale requested)
        {
            AudioVoiceLocaleSnapshot candidate;
            try
            {
                if (!mapper.TryMap(requested.Locale, out candidate) || !candidate.IsValid)
                {
                    Report(new AudioLocalizationDiagnostic(
                        AudioLocalizationDiagnosticCode.MappingUnavailable,
                        AudioLocalizationDiagnosticSeverity.Warning,
                        "No valid Audio voice-locale mapping exists for the committed localization locale; the current voice locale was preserved.",
                        requested.Locale,
                        default,
                        requested.Revision));
                    return;
                }
            }
            catch (Exception exception)
            {
                Report(new AudioLocalizationDiagnostic(
                    AudioLocalizationDiagnosticCode.MapperException,
                    AudioLocalizationDiagnosticSeverity.Error,
                    "The Audio localization mapper threw an exception; the current voice locale was preserved.",
                    requested.Locale,
                    default,
                    requested.Revision,
                    exception));
                return;
            }

            AudioVoiceLocaleSnapshot previous = TryCaptureCurrentVoiceLocale();
            if (previous == candidate)
            {
                lastKnownGoodVoiceLocale = candidate;
                return;
            }

            bool accepted;
            try
            {
                accepted = audio.TrySetVoiceLocale(candidate);
            }
            catch (Exception exception)
            {
                RestoreLastKnownGood(previous, requested, candidate);
                Report(new AudioLocalizationDiagnostic(
                    AudioLocalizationDiagnosticCode.VoiceLocaleTargetException,
                    AudioLocalizationDiagnosticSeverity.Error,
                    "The Audio voice-locale target threw an exception; the last-known-good voice locale was restored when possible.",
                    requested.Locale,
                    candidate,
                    requested.Revision,
                    exception));
                return;
            }

            if (accepted)
            {
                lastKnownGoodVoiceLocale = candidate;
                return;
            }

            RestoreLastKnownGood(previous, requested, candidate);
            Report(new AudioLocalizationDiagnostic(
                AudioLocalizationDiagnosticCode.VoiceLocaleRejected,
                AudioLocalizationDiagnosticSeverity.Warning,
                "The Audio voice-locale target rejected the mapped locale; the last-known-good voice locale was preserved.",
                requested.Locale,
                candidate,
                requested.Revision));
        }

        private AudioVoiceLocaleSnapshot TryCaptureCurrentVoiceLocale()
        {
            try
            {
                AudioVoiceLocaleSnapshot current = audio.CurrentVoiceLocale;
                if (current.IsValid)
                    lastKnownGoodVoiceLocale = current;
                return current;
            }
            catch (Exception exception)
            {
                Report(new AudioLocalizationDiagnostic(
                    AudioLocalizationDiagnosticCode.VoiceLocaleTargetException,
                    AudioLocalizationDiagnosticSeverity.Error,
                    "The Audio voice-locale target failed to report its current state.",
                    localization.CurrentLocale,
                    default,
                    localization.Revision,
                    exception));
                return default;
            }
        }

        private void RestoreLastKnownGood(
            AudioVoiceLocaleSnapshot previous,
            PendingLocale requested,
            AudioVoiceLocaleSnapshot rejected)
        {
            AudioVoiceLocaleSnapshot restore = previous.IsValid
                ? previous
                : lastKnownGoodVoiceLocale;
            if (!restore.IsValid)
                return;

            try
            {
                if (audio.CurrentVoiceLocale == restore)
                    return;
                if (audio.TrySetVoiceLocale(restore))
                    return;
            }
            catch (Exception exception)
            {
                Report(new AudioLocalizationDiagnostic(
                    AudioLocalizationDiagnosticCode.LastKnownGoodRestoreFailed,
                    AudioLocalizationDiagnosticSeverity.Error,
                    "Restoring the last-known-good Audio voice locale threw an exception.",
                    requested.Locale,
                    rejected,
                    requested.Revision,
                    exception));
                return;
            }

            Report(new AudioLocalizationDiagnostic(
                AudioLocalizationDiagnosticCode.LastKnownGoodRestoreFailed,
                AudioLocalizationDiagnosticSeverity.Error,
                "The Audio voice-locale target rejected the last-known-good locale restoration.",
                requested.Locale,
                rejected,
                requested.Revision));
        }

        private void UnbindCore()
        {
            if (!isBound)
                return;

            localization.Changed -= HandleLocalizationChanged;
            isBound = false;
            hasPendingLocale = false;
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "AudioLocalizationBridge is confined to its Unity main-thread owner.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(AudioLocalizationBridge));
        }

        private void Report(AudioLocalizationDiagnostic diagnostic)
        {
            if (diagnosticSink != null)
            {
                try
                {
                    diagnosticSink(diagnostic);
                    return;
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "The Audio localization diagnostic sink threw an exception.");
                }
            }

            if (diagnostic.Severity == AudioLocalizationDiagnosticSeverity.Error)
            {
                if (diagnostic.Exception != null)
                    Log.Error(diagnostic.Exception, diagnostic.Message);
                else
                    Log.Error(diagnostic.Message);
            }
            else
            {
                Log.Warning(diagnostic.Message);
            }
        }
    }
}
