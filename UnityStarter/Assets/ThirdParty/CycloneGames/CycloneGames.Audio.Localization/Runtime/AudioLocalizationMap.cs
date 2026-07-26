// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using CycloneGames.Localization.Core;
using UnityEngine;

namespace CycloneGames.Audio.Runtime.Integrations.Localization
{
    /// <summary>
    /// Maps one committed localization locale to an Audio voice locale and its ordered fallbacks.
    /// Implementations must not mutate either service.
    /// </summary>
    public interface IAudioLocalizationMapper
    {
        bool TryMap(LocaleId localizationLocale, out AudioVoiceLocaleSnapshot voiceLocale);
    }

    /// <summary>
    /// Default mapper that uses the canonical localization locale code as the voice locale code.
    /// </summary>
    public sealed class IdentityAudioLocalizationMapper : IAudioLocalizationMapper
    {
        public static readonly IdentityAudioLocalizationMapper Instance =
            new IdentityAudioLocalizationMapper();

        private static readonly VoiceLocaleId[] NoFallbacks = Array.Empty<VoiceLocaleId>();

        private IdentityAudioLocalizationMapper()
        {
        }

        public bool TryMap(LocaleId localizationLocale, out AudioVoiceLocaleSnapshot voiceLocale)
        {
            voiceLocale = default;
            if (!localizationLocale.IsValid ||
                !VoiceLocaleId.TryCreate(localizationLocale.Code, out VoiceLocaleId primary))
            {
                return false;
            }

            return AudioVoiceLocaleSnapshot.TryCreate(primary, NoFallbacks, out voiceLocale);
        }
    }

    [Serializable]
    public sealed class AudioLocalizationMapEntry
    {
        [SerializeField] private string localizationLocaleCode;
        [SerializeField] private string voiceLocaleCode;
        [SerializeField] private string[] voiceFallbackLocaleCodes = Array.Empty<string>();

        public string LocalizationLocaleCode => localizationLocaleCode;
        public string VoiceLocaleCode => voiceLocaleCode;
        public IReadOnlyList<string> VoiceFallbackLocaleCodes => voiceFallbackLocaleCodes;
    }

    /// <summary>
    /// Explicit, exact locale mapping. Missing localization locales do not implicitly fall back to
    /// their primary language; add that relationship to this asset when the product permits it.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AudioLocalizationMap",
        menuName = "CycloneGames/Audio/Localization Map")]
    public sealed class AudioLocalizationMap : ScriptableObject, IAudioLocalizationMapper
    {
        public const int MaxMappingCount = 256;

        [SerializeField] private List<AudioLocalizationMapEntry> entries =
            new List<AudioLocalizationMapEntry>();

        [NonSerialized] private Dictionary<LocaleId, AudioVoiceLocaleSnapshot> compiledMappings;
        [NonSerialized] private bool compilationAttempted;
        [NonSerialized] private string validationError;

        public int Count => entries != null ? entries.Count : 0;

        public bool TryMap(LocaleId localizationLocale, out AudioVoiceLocaleSnapshot voiceLocale)
        {
            voiceLocale = default;
            if (!localizationLocale.IsValid || !EnsureCompiled())
                return false;

            return compiledMappings.TryGetValue(localizationLocale, out voiceLocale);
        }

        /// <summary>
        /// Validates and compiles the complete map as one unit. A single invalid entry rejects the
        /// complete map so runtime behavior cannot depend on serialized entry order.
        /// </summary>
        public bool TryValidate(out string error)
        {
            bool valid = EnsureCompiled();
            error = validationError;
            return valid;
        }

        private bool EnsureCompiled()
        {
            if (compilationAttempted)
                return compiledMappings != null;

            compilationAttempted = true;
            validationError = null;

            int count = entries != null ? entries.Count : 0;
            if (count > MaxMappingCount)
            {
                validationError =
                    $"Audio localization map contains {count} entries; the limit is {MaxMappingCount}.";
                return false;
            }

            var candidate = new Dictionary<LocaleId, AudioVoiceLocaleSnapshot>(count);
            for (int i = 0; i < count; i++)
            {
                AudioLocalizationMapEntry entry = entries[i];
                if (entry == null)
                {
                    validationError = $"Audio localization map entry {i} is null.";
                    return false;
                }

                if (!LocaleId.TryCreate(entry.LocalizationLocaleCode, out LocaleId localizationLocale))
                {
                    validationError =
                        $"Audio localization map entry {i} has an invalid localization locale code.";
                    return false;
                }

                if (!string.Equals(
                        entry.LocalizationLocaleCode,
                        localizationLocale.Code,
                        StringComparison.Ordinal))
                {
                    validationError =
                        $"Audio localization map entry {i} must use canonical localization locale '{localizationLocale.Code}'.";
                    return false;
                }

                if (candidate.ContainsKey(localizationLocale))
                {
                    validationError =
                        $"Audio localization map contains duplicate locale '{localizationLocale.Code}'.";
                    return false;
                }

                if (!VoiceLocaleId.TryCreate(entry.VoiceLocaleCode, out VoiceLocaleId primary))
                {
                    validationError =
                        $"Audio localization map entry '{localizationLocale.Code}' has an invalid voice locale code.";
                    return false;
                }

                if (!string.Equals(
                        entry.VoiceLocaleCode,
                        primary.Code,
                        StringComparison.Ordinal))
                {
                    validationError =
                        $"Audio localization map entry '{localizationLocale.Code}' must use canonical voice locale '{primary.Code}'.";
                    return false;
                }

                IReadOnlyList<string> fallbackCodes = entry.VoiceFallbackLocaleCodes;
                int fallbackCount = fallbackCodes != null ? fallbackCodes.Count : 0;
                if (fallbackCount >= AudioVoiceLocaleSnapshot.MaxLocaleCount)
                {
                    validationError =
                        $"Audio localization map entry '{localizationLocale.Code}' exceeds the voice fallback limit.";
                    return false;
                }

                VoiceLocaleId[] fallbacks = fallbackCount == 0
                    ? Array.Empty<VoiceLocaleId>()
                    : new VoiceLocaleId[fallbackCount];
                for (int fallbackIndex = 0; fallbackIndex < fallbackCount; fallbackIndex++)
                {
                    if (!VoiceLocaleId.TryCreate(fallbackCodes[fallbackIndex], out fallbacks[fallbackIndex]))
                    {
                        validationError =
                            $"Audio localization map entry '{localizationLocale.Code}' has an invalid voice fallback at index {fallbackIndex}.";
                        return false;
                    }

                    if (!string.Equals(
                            fallbackCodes[fallbackIndex],
                            fallbacks[fallbackIndex].Code,
                            StringComparison.Ordinal))
                    {
                        validationError =
                            $"Audio localization map entry '{localizationLocale.Code}' voice fallback {fallbackIndex} must use canonical locale '{fallbacks[fallbackIndex].Code}'.";
                        return false;
                    }

                    if (fallbacks[fallbackIndex] == primary)
                    {
                        validationError =
                            $"Audio localization map entry '{localizationLocale.Code}' voice fallback {fallbackIndex} duplicates the primary voice locale.";
                        return false;
                    }

                    for (int previousFallbackIndex = 0;
                         previousFallbackIndex < fallbackIndex;
                         previousFallbackIndex++)
                    {
                        if (fallbacks[previousFallbackIndex] != fallbacks[fallbackIndex])
                            continue;

                        validationError =
                            $"Audio localization map entry '{localizationLocale.Code}' contains duplicate voice fallback '{fallbacks[fallbackIndex].Code}'.";
                        return false;
                    }
                }

                if (!AudioVoiceLocaleSnapshot.TryCreate(primary, fallbacks, out AudioVoiceLocaleSnapshot snapshot))
                {
                    validationError =
                        $"Audio localization map entry '{localizationLocale.Code}' could not create a voice locale snapshot.";
                    return false;
                }

                candidate.Add(localizationLocale, snapshot);
            }

            compiledMappings = candidate;
            return true;
        }

        private void OnEnable()
        {
            InvalidateCompiledMap();
        }

        private void OnValidate()
        {
            InvalidateCompiledMap();
        }

        private void InvalidateCompiledMap()
        {
            compiledMappings = null;
            compilationAttempted = false;
            validationError = null;
        }
    }
}
