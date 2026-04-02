using System;
using System.Collections.Generic;

namespace CycloneGames.Localization.Runtime
{
    public readonly struct LocalizationOptions
    {
        public readonly Locale DefaultLocale;
        public readonly IReadOnlyList<Locale> AvailableLocales;

        /// <summary>
        /// When true, the system detects the OS language on first launch and selects the closest match.
        /// </summary>
        public readonly bool DetectSystemLanguage;

        /// <summary>
        /// Ordered locale selectors evaluated from first to last.
        /// The first selector that returns a non-null, available locale wins.
        /// If null or empty, the system uses the default chain:
        /// CommandLine → PlayerPrefs → System → Default.
        /// </summary>
        public readonly IReadOnlyList<ILocaleSelector> LocaleSelectors;

        /// <summary>
        /// When not <see cref="PseudoLocaleMode.None"/>, all resolved strings are
        /// transformed through <see cref="PseudoLocalizer"/> for QA testing.
        /// </summary>
        public readonly PseudoLocaleMode PseudoMode;

        public LocalizationOptions(
            Locale defaultLocale,
            IReadOnlyList<Locale> availableLocales,
            bool detectSystemLanguage = true,
            IReadOnlyList<ILocaleSelector> localeSelectors = null,
            PseudoLocaleMode pseudoMode = PseudoLocaleMode.None)
        {
            DefaultLocale = defaultLocale;
            AvailableLocales = availableLocales ?? Array.Empty<Locale>();
            DetectSystemLanguage = detectSystemLanguage;
            LocaleSelectors = localeSelectors;
            PseudoMode = pseudoMode;
        }
    }
}
