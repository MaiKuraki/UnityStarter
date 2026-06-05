using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.Localization.Core;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Defines a locale with its display metadata and fallback chain.
    /// Create as ScriptableObject assets: one per supported language.
    /// </summary>
    [CreateAssetMenu(fileName = "Locale", menuName = "CycloneGames/Localization/Locale")]
    public sealed class Locale : ScriptableObject, ILocaleFallbackNode<Locale>
    {
        [SerializeField] private string localeCode = "en";
        [SerializeField] private string displayName = "English";
        [SerializeField] private string nativeName = "English";
        [SerializeField] private List<Locale> fallbacks = new List<Locale>();

        private LocaleId _cachedId;
        private bool _idCached;

        public LocaleId Id
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!_idCached)
                {
                    _cachedId = new LocaleId(localeCode);
                    _idCached = true;
                }
                return _cachedId;
            }
        }

        public string DisplayName => displayName;
        public string NativeName => nativeName;
        public IReadOnlyList<Locale> Fallbacks => fallbacks;
        public int FallbackCount => fallbacks != null ? fallbacks.Count : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Locale GetFallback(int index)
        {
            return fallbacks[index];
        }

        private void OnValidate()
        {
            _idCached = false;
            _cachedId = default;
        }
    }
}
