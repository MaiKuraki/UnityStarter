using System;
using System.Globalization;
using CycloneGames.Localization.Core;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Selects locale from command-line arguments.
    /// Supports: <c>--locale zh-CN</c> or <c>-locale zh-CN</c>.
    /// <para>
    /// Highest priority in the default selector chain.
    /// Useful for QA: launch the game with <c>--locale ja</c> to force Japanese.
    /// Result is cached after first call; subsequent queries allocate no managed objects.
    /// </para>
    /// </summary>
    public sealed class CommandLineLocaleSelector : ILocaleSelector
    {
        private string _cached;
        private bool _resolved;

        public string GetPreferredLocaleCode()
        {
            if (_resolved) return _cached;
            _resolved = true;

            var args = Environment.GetCommandLineArgs();
            if (args == null) return null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--locale", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[i], "-locale", StringComparison.OrdinalIgnoreCase))
                {
                    _cached = args[i + 1];
                    return _cached;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Selects locale from <see cref="PlayerPrefs"/> using the user's saved language preference.
    /// Key: <c>"CycloneGames.Locale"</c>.
    /// <para>
    /// Second priority in the default chain; allows players to override system language.
    /// </para>
    /// </summary>
    public sealed class PlayerPrefsLocaleSelector : ILocaleSelector
    {
        /// <summary>
        /// The PlayerPrefs key used to store the player's locale preference.
        /// </summary>
        public const string PrefsKey = "CycloneGames.Locale";

        public string GetPreferredLocaleCode()
        {
            string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
            return saved.Length > 0 ? saved : null;
        }

        /// <summary>
        /// Saves the player's locale preference. Call after <see cref="ILocalizationService.SetLocaleAsync"/>.
        /// </summary>
        public static void Save(LocaleId locale)
        {
            if (locale.IsValid)
            {
                PlayerPrefs.SetString(PrefsKey, locale.Code);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Clears the saved preference, reverting to system language detection on next launch.
        /// </summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Selects locale by detecting the OS/system language.
    /// Uses <see cref="CultureInfo.CurrentUICulture"/> with
    /// <see cref="Application.systemLanguage"/> as fallback for platforms that
    /// do not support CultureInfo.
    /// <para>
    /// Third priority in the default chain.
    /// </para>
    /// </summary>
    public sealed class SystemLocaleSelector : ILocaleSelector
    {
        private string _cached;
        private bool _resolved;

        public string GetPreferredLocaleCode()
        {
            if (_resolved) return _cached;
            _resolved = true;
            _cached = DetectSystemCode();
            return _cached;
        }

        private static string DetectSystemCode()
        {
            try
            {
                var culture = CultureInfo.CurrentUICulture;
                if (culture != null && !culture.Equals(CultureInfo.InvariantCulture))
                    return culture.Name;
            }
            catch
            {
                // CultureInfo can be unavailable on some Unity platforms.
            }

            return Application.systemLanguage switch
            {
                SystemLanguage.English => "en",
                SystemLanguage.Chinese => "zh-CN",
                SystemLanguage.ChineseSimplified => "zh-CN",
                SystemLanguage.ChineseTraditional => "zh-TW",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.French => "fr",
                SystemLanguage.German => "de",
                SystemLanguage.Spanish => "es",
                SystemLanguage.Portuguese => "pt",
                SystemLanguage.Russian => "ru",
                SystemLanguage.Italian => "it",
                SystemLanguage.Arabic => "ar",
                SystemLanguage.Thai => "th",
                SystemLanguage.Turkish => "tr",
                SystemLanguage.Vietnamese => "vi",
                SystemLanguage.Polish => "pl",
                SystemLanguage.Dutch => "nl",
                SystemLanguage.Indonesian => "id",
                _ => null
            };
        }
    }
}
