// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    public readonly struct AudioVoiceLocaleChange
    {
        public AudioVoiceLocaleChange(
            AudioVoiceLocaleSnapshot previous,
            AudioVoiceLocaleSnapshot current,
            long revision)
        {
            Previous = previous;
            Current = current;
            Revision = revision;
        }

        public AudioVoiceLocaleSnapshot Previous { get; }
        public AudioVoiceLocaleSnapshot Current { get; }
        public long Revision { get; }
    }

    /// <summary>
    /// Dedicated voice-locale state capability for explicit application composition.
    /// All access is Unity-main-thread-affine.
    /// </summary>
    public interface IAudioVoiceLocaleControl
    {
        /// <remarks>All members, including reads and event subscription, are main-thread-affine.</remarks>
        AudioVoiceLocaleSnapshot CurrentVoiceLocale { get; }
        long VoiceLocaleRevision { get; }
        event Action<AudioVoiceLocaleChange> VoiceLocaleChanged;

        /// <summary>
        /// Accepts a valid locale snapshot. A non-reentrant accepted change must be visible through
        /// <see cref="CurrentVoiceLocale"/> before this method returns. Reentrant changes may be
        /// queued, but must be drained before the outermost mutation returns.
        /// </summary>
        bool TrySetVoiceLocale(in AudioVoiceLocaleSnapshot locale);

        /// <summary>
        /// Clears the current locale under the same synchronous dispatch contract as
        /// <see cref="TrySetVoiceLocale(in AudioVoiceLocaleSnapshot)"/>.
        /// </summary>
        bool ClearVoiceLocale();
    }

    /// <summary>
    /// Explicitly constructible state owner with bounded reentrant dispatch and isolated
    /// subscriber failures.
    /// </summary>
    public sealed class AudioVoiceLocaleControl : IAudioVoiceLocaleControl
    {
        private const int MaxChangesPerDispatch = 64;

        private readonly Action<Exception> subscriberExceptionSink;
        private readonly Queue<PendingMutation> pendingMutations =
            new Queue<PendingMutation>(4);
        private AudioVoiceLocaleSnapshot currentVoiceLocale;
        private long revision;
        private Action<AudioVoiceLocaleChange> voiceLocaleChanged;
        private int acceptedMutationCount;
        private bool isDispatching;
        private bool isReportingException;

        private readonly struct PendingMutation
        {
            public PendingMutation(AudioVoiceLocaleSnapshot locale, bool clear)
            {
                Locale = locale;
                Clear = clear;
            }

            public AudioVoiceLocaleSnapshot Locale { get; }
            public bool Clear { get; }
        }

        public AudioVoiceLocaleControl(Action<Exception> subscriberExceptionSink = null)
        {
            this.subscriberExceptionSink = subscriberExceptionSink;
        }

        public AudioVoiceLocaleSnapshot CurrentVoiceLocale
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(
                    nameof(AudioVoiceLocaleControl) + ".CurrentVoiceLocale");
                return currentVoiceLocale;
            }
        }

        public long VoiceLocaleRevision
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(
                    nameof(AudioVoiceLocaleControl) + ".VoiceLocaleRevision");
                return revision;
            }
        }

        public event Action<AudioVoiceLocaleChange> VoiceLocaleChanged
        {
            add
            {
                AudioRuntimeThreadGuard.EnsureMainThread(
                    nameof(AudioVoiceLocaleControl) + ".VoiceLocaleChanged.add");
                voiceLocaleChanged += value;
            }
            remove
            {
                AudioRuntimeThreadGuard.EnsureMainThread(
                    nameof(AudioVoiceLocaleControl) + ".VoiceLocaleChanged.remove");
                voiceLocaleChanged -= value;
            }
        }

        public bool TrySetVoiceLocale(in AudioVoiceLocaleSnapshot locale)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(
                nameof(AudioVoiceLocaleControl) + ".TrySetVoiceLocale");
            if (!locale.IsValid)
                return false;

            if (isDispatching)
                return TryEnqueueMutation(new PendingMutation(locale, clear: false));

            if (currentVoiceLocale == locale)
                return true;

            DispatchMutations(new PendingMutation(locale, clear: false));
            return true;
        }

        public bool ClearVoiceLocale()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(
                nameof(AudioVoiceLocaleControl) + ".ClearVoiceLocale");
            if (isDispatching)
                return TryEnqueueMutation(new PendingMutation(default, clear: true));

            if (!currentVoiceLocale.IsValid)
                return false;

            DispatchMutations(new PendingMutation(default, clear: true));
            return true;
        }

        internal void Reset()
        {
            currentVoiceLocale = default;
            revision = 0;
            pendingMutations.Clear();
            acceptedMutationCount = 0;
            isDispatching = false;
            isReportingException = false;
            voiceLocaleChanged = null;
        }

        private bool TryEnqueueMutation(PendingMutation mutation)
        {
            if (acceptedMutationCount >= MaxChangesPerDispatch)
            {
                ReportSubscriberException(new InvalidOperationException(
                    $"Audio voice-locale dispatch reached the bounded limit of {MaxChangesPerDispatch}. The reentrant change was rejected."));
                return false;
            }

            acceptedMutationCount++;
            pendingMutations.Enqueue(mutation);
            return true;
        }

        private void DispatchMutations(PendingMutation initialMutation)
        {
            isDispatching = true;
            acceptedMutationCount = 1;
            PendingMutation nextMutation = initialMutation;
            int processedCount = 0;
            try
            {
                while (true)
                {
                    processedCount++;
                    if (TryApplyMutation(nextMutation, out AudioVoiceLocaleChange change))
                        InvokeSubscribers(change);

                    if (pendingMutations.Count == 0)
                        break;

                    if (processedCount >= MaxChangesPerDispatch)
                    {
                        pendingMutations.Clear();
                        ReportSubscriberException(new InvalidOperationException(
                            $"Audio voice-locale dispatch exceeded its accepted mutation budget of {MaxChangesPerDispatch}. Remaining changes were dropped."));
                        break;
                    }

                    nextMutation = pendingMutations.Dequeue();
                }
            }
            finally
            {
                acceptedMutationCount = 0;
                isDispatching = false;
            }
        }

        private bool TryApplyMutation(
            PendingMutation mutation,
            out AudioVoiceLocaleChange change)
        {
            AudioVoiceLocaleSnapshot previous = currentVoiceLocale;
            if (mutation.Clear)
            {
                if (!previous.IsValid)
                {
                    change = default;
                    return false;
                }

                currentVoiceLocale = default;
            }
            else
            {
                if (previous == mutation.Locale)
                {
                    change = default;
                    return false;
                }

                currentVoiceLocale = mutation.Locale;
            }

            revision = NextRevision(revision);
            change = new AudioVoiceLocaleChange(previous, currentVoiceLocale, revision);
            return true;
        }

        private void InvokeSubscribers(AudioVoiceLocaleChange change)
        {
            Action<AudioVoiceLocaleChange> handlers = voiceLocaleChanged;
            if (handlers == null)
                return;

            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<AudioVoiceLocaleChange>)invocationList[i])(change);
                }
                catch (Exception exception)
                {
                    ReportSubscriberException(exception);
                }
            }
        }

        private void ReportSubscriberException(Exception exception)
        {
            if (isReportingException)
            {
                Debug.LogException(exception);
                return;
            }

            isReportingException = true;
            try
            {
                if (subscriberExceptionSink != null)
                {
                    try
                    {
                        subscriberExceptionSink(exception);
                        return;
                    }
                    catch (Exception sinkException)
                    {
                        Debug.LogException(sinkException);
                    }
                }

                Debug.LogException(exception);
            }
            finally
            {
                isReportingException = false;
            }
        }

        private static long NextRevision(long current)
        {
            unchecked
            {
                current++;
                return current > 0 ? current : 1;
            }
        }
    }
}
