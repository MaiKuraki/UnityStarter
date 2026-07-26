// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    public partial class AudioManager
    {
        private static readonly AudioVoiceLocaleControl sharedVoiceLocaleControl =
            new AudioVoiceLocaleControl();

        /// <summary>
        /// Dedicated voice-locale capability for explicit application composition.
        /// </summary>
        public static IAudioVoiceLocaleControl VoiceLocaleControl =>
            sharedVoiceLocaleControl;

        public static AudioVoiceLocaleSnapshot CurrentVoiceLocaleSnapshot =>
            sharedVoiceLocaleControl.CurrentVoiceLocale;

        public static VoiceLocaleId CurrentVoiceLocale =>
            sharedVoiceLocaleControl.CurrentVoiceLocale.Primary;

        public static long VoiceLocaleRevision =>
            sharedVoiceLocaleControl.VoiceLocaleRevision;

        public static event Action<AudioVoiceLocaleChange> VoiceLocaleChanged
        {
            add => sharedVoiceLocaleControl.VoiceLocaleChanged += value;
            remove => sharedVoiceLocaleControl.VoiceLocaleChanged -= value;
        }

        /// <summary>
        /// Configures the voice locale and its explicit fallback order without requiring
        /// CycloneGames.Localization.
        /// </summary>
        public static bool TrySetVoiceLocale(
            string localeCode,
            params string[] fallbackLocaleCodes)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(TrySetVoiceLocale));
            if (!VoiceLocaleId.TryCreate(localeCode, out VoiceLocaleId primary))
                return false;

            int fallbackCount =
                fallbackLocaleCodes != null ? fallbackLocaleCodes.Length : 0;
            if (fallbackCount >= AudioVoiceLocaleSnapshot.MaxLocaleCount)
                return false;

            VoiceLocaleId[] fallbacks = fallbackCount > 0
                ? new VoiceLocaleId[fallbackCount]
                : Array.Empty<VoiceLocaleId>();
            for (int i = 0; i < fallbackCount; i++)
            {
                if (!VoiceLocaleId.TryCreate(
                        fallbackLocaleCodes[i],
                        out fallbacks[i]))
                {
                    return false;
                }
            }

            if (!AudioVoiceLocaleSnapshot.TryCreate(
                    primary,
                    fallbacks,
                    out AudioVoiceLocaleSnapshot snapshot))
            {
                return false;
            }

            return sharedVoiceLocaleControl.TrySetVoiceLocale(snapshot);
        }

        public static bool TrySetVoiceLocale(in AudioVoiceLocaleSnapshot locale) =>
            sharedVoiceLocaleControl.TrySetVoiceLocale(locale);

        public static bool ClearVoiceLocale() =>
            sharedVoiceLocaleControl.ClearVoiceLocale();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVoiceLocaleState()
        {
            sharedVoiceLocaleControl.Reset();
        }
    }
}
