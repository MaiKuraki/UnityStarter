using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Project-level localization configuration asset.
    /// Single source of truth for available locales and default locale.
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationSettings", menuName = "CycloneGames/Localization/Settings")]
    public sealed class LocalizationSettings : ScriptableObject
    {
        [SerializeField] private Locale defaultLocale;
        [SerializeField] private List<Locale> availableLocales = new List<Locale>();
        [SerializeField] private bool detectSystemLanguage = true;

        [Tooltip("Pseudo-localization mode for QA testing. Set to None for release builds.")]
        [SerializeField] private PseudoLocaleMode pseudoLocaleMode = PseudoLocaleMode.None;

        public Locale DefaultLocale => defaultLocale;
        public IReadOnlyList<Locale> AvailableLocales => availableLocales;
        public bool DetectSystemLanguage => detectSystemLanguage;
        public PseudoLocaleMode PseudoLocaleMode => pseudoLocaleMode;

        public LocalizationOptions ToOptions()
        {
            return new LocalizationOptions(
                defaultLocale,
                availableLocales,
                detectSystemLanguage,
                pseudoMode: pseudoLocaleMode);
        }
    }
}
