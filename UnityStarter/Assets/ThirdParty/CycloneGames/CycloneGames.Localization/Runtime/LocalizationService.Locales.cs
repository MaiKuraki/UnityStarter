using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    public sealed partial class LocalizationService
    {
        private void BuildLocaleConfiguration(
            LocalizationOptions options,
            LocalizationLimits limits,
            out Dictionary<string, Locale> localeMap,
            out Dictionary<string, LocaleId[]> localeChains,
            out ReadOnlyCollection<LocaleId> availableIds,
            out Locale selectedLocale)
        {
            localeMap = new Dictionary<string, Locale>(StringComparer.Ordinal);
            var locales = new List<Locale>();
            IReadOnlyList<Locale> configured = options.AvailableLocales;
            if (configured.Count > limits.MaxAvailableLocales)
                throw new InvalidOperationException("Available locale count exceeds the configured limit.");

            for (int i = 0; i < configured.Count; i++)
                AddConfiguredLocale(configured[i], locales, localeMap, limits.MaxAvailableLocales);

            if (options.DefaultLocale != null)
            {
                LocaleId defaultId = options.DefaultLocale.Id;
                if (!defaultId.IsValid)
                    throw new InvalidOperationException("The default locale code is invalid.");

                if (localeMap.TryGetValue(defaultId.Code, out Locale existing))
                {
                    if (existing != options.DefaultLocale)
                        throw new InvalidOperationException("The default locale duplicates another locale code.");
                }
                else
                {
                    AddConfiguredLocale(options.DefaultLocale, locales, localeMap, limits.MaxAvailableLocales);
                }
            }

            if (locales.Count == 0)
                throw new InvalidOperationException("At least one valid locale is required.");

            selectedLocale = null;
            IReadOnlyList<ILocaleSelector> selectors = options.LocaleSelectors;
            if (selectors == null)
            {
                if (options.DetectSystemLanguage)
                {
                    selectors = new ILocaleSelector[]
                    {
                        new CommandLineLocaleSelector(),
                        new SystemLocaleSelector(),
                    };
                }
                else
                {
                    selectors = new ILocaleSelector[]

                    {
                        new CommandLineLocaleSelector(),
                    };
                }
            }
            selectedLocale = EvaluateSelectors(selectors, locales, localeMap);
            if (selectedLocale == null) selectedLocale = options.DefaultLocale;
            if (selectedLocale == null) selectedLocale = locales[0];

            var ids = new LocaleId[locales.Count];
            for (int i = 0; i < locales.Count; i++) ids[i] = locales[i].Id;
            availableIds = Array.AsReadOnly(ids);

            for (int i = 0; i < locales.Count; i++)
                ValidateFallbackClosure(locales[i], localeMap, limits.MaxFallbackLocales);

            localeChains = new Dictionary<string, LocaleId[]>(locales.Count, StringComparer.Ordinal);
            for (int i = 0; i < locales.Count; i++)
            {
                Locale locale = locales[i];
                localeChains.Add(
                    locale.Id.Code,
                    LocaleFallbackChainBuilder.Build(locale, limits.MaxFallbackLocales));
            }
        }

        private static void ValidateFallbackClosure(
            Locale root,
            Dictionary<string, Locale> localeMap,
            int maxLocales)
        {
            var visited = new HashSet<Locale>();
            var queue = new Queue<Locale>();
            visited.Add(root);
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Locale locale = queue.Dequeue();
                LocaleId id = locale.Id;
                if (!id.IsValid || !localeMap.TryGetValue(id.Code, out Locale configured) || configured != locale)
                {
                    throw new InvalidOperationException(
                        "Every fallback locale must be the uniquely configured asset for its locale code.");
                }

                int count = locale.FallbackCount;
                if (count < 0 || count > maxLocales)
                    throw new InvalidOperationException("A fallback list exceeds the configured limit.");
                for (int i = 0; i < count; i++)
                {
                    Locale fallback = locale.GetFallback(i);
                    if (fallback == null)
                        throw new InvalidOperationException("Fallback locale entries must not be null.");
                    if (!visited.Add(fallback)) continue;
                    if (visited.Count > maxLocales)
                        throw new InvalidOperationException("A fallback graph exceeds the configured limit.");
                    queue.Enqueue(fallback);
                }
            }
        }

        private static void AddConfiguredLocale(
            Locale locale,
            List<Locale> locales,
            Dictionary<string, Locale> localeMap,
            int maxLocales)
        {
            if (locale == null)
                throw new InvalidOperationException("Available locale entries must not be null.");
            LocaleId id = locale.Id;
            if (!id.IsValid)
                throw new InvalidOperationException("An available locale code is invalid.");
            if (localeMap.ContainsKey(id.Code))
                throw new InvalidOperationException("Duplicate available locale code '" + id.Code + "'.");
            if (locales.Count >= maxLocales)
                throw new InvalidOperationException("Available locale count exceeds the configured limit.");
            localeMap.Add(id.Code, locale);
            locales.Add(locale);
        }

        private static Locale EvaluateSelectors(
            IReadOnlyList<ILocaleSelector> selectors,
            List<Locale> locales,
            Dictionary<string, Locale> localeMap)
        {
            if (selectors == null) return null;
            for (int i = 0; i < selectors.Count; i++)
            {
                ILocaleSelector selector = selectors[i];
                if (selector == null) continue;

                string code;
                try
                {
                    code = selector.GetPreferredLocaleCode();
                }
                catch
                {
                    continue;
                }

                if (!LocaleId.TryCreate(code, out LocaleId preferred)) continue;
                if (localeMap.TryGetValue(preferred.Code, out Locale exact)) return exact;
                if (localeMap.TryGetValue(preferred.Language.Code, out Locale language)) return language;

                for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
                {
                    if (locales[localeIndex].Id.Language == preferred.Language)
                        return locales[localeIndex];
                }
            }

            return null;
        }

    }
}
