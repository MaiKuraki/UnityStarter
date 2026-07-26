// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Immutable ordered locale selection. Index zero is the requested voice locale and the
    /// remaining entries are explicit fallbacks in priority order.
    /// </summary>
    public readonly struct AudioVoiceLocaleSnapshot : IEquatable<AudioVoiceLocaleSnapshot>
    {
        public const int MaxLocaleCount = 8;

        private readonly VoiceLocaleId[] locales;

        private AudioVoiceLocaleSnapshot(VoiceLocaleId[] locales)
        {
            this.locales = locales;
        }

        public bool IsValid =>
            locales != null && locales.Length > 0 && locales[0].IsValid;

        public int Count => locales != null ? locales.Length : 0;
        public int FallbackCount => Count > 0 ? Count - 1 : 0;
        public VoiceLocaleId Primary => IsValid ? locales[0] : VoiceLocaleId.Invalid;

        public VoiceLocaleId this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => locales[index];
        }

        public VoiceLocaleId GetFallback(int index)
        {
            if ((uint)index >= (uint)FallbackCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return locales[index + 1];
        }

        public static bool TryCreate(
            VoiceLocaleId primary,
            IReadOnlyList<VoiceLocaleId> fallbackLocales,
            out AudioVoiceLocaleSnapshot snapshot)
        {
            snapshot = default;
            if (!primary.IsValid)
                return false;

            int fallbackCount = fallbackLocales != null ? fallbackLocales.Count : 0;
            if (fallbackCount >= MaxLocaleCount)
                return false;

            var copy = new VoiceLocaleId[1 + fallbackCount];
            copy[0] = primary;

            for (int i = 0; i < fallbackCount; i++)
            {
                VoiceLocaleId fallback = fallbackLocales[i];
                if (!fallback.IsValid)
                    return false;

                for (int existingIndex = 0; existingIndex <= i; existingIndex++)
                {
                    if (copy[existingIndex] == fallback)
                        return false;
                }

                copy[i + 1] = fallback;
            }

            snapshot = new AudioVoiceLocaleSnapshot(copy);
            return true;
        }

        public bool Equals(AudioVoiceLocaleSnapshot other)
        {
            int count = Count;
            if (count != other.Count)
                return false;

            for (int i = 0; i < count; i++)
            {
                if (locales[i] != other.locales[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj) =>
            obj is AudioVoiceLocaleSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < Count; i++)
                    hash = hash * 31 + locales[i].GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            AudioVoiceLocaleSnapshot left,
            AudioVoiceLocaleSnapshot right) => left.Equals(right);

        public static bool operator !=(
            AudioVoiceLocaleSnapshot left,
            AudioVoiceLocaleSnapshot right) => !left.Equals(right);
    }
}
