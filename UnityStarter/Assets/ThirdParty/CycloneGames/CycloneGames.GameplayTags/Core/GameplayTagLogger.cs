using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
    /// <summary>
    /// Provides the bridge between the core GameplayTags runtime and host-specific features.
    /// Unity projects can keep the defaults, while plain C# hosts can override delegates at startup.
    /// </summary>
    public static class GameplayTagRuntimePlatform
    {
        private static readonly object ProjectTagSourcesLock = new();
        private static Dictionary<string, IGameplayTagSource> s_ProjectTagSources = new(StringComparer.Ordinal);

        public static Action<string> LogWarning { get; set; } = message => Console.WriteLine(message);
        public static Action<string> LogError { get; set; } = message => Console.Error.WriteLine(message);
        public static Func<bool> IsRuntimePlaying { get; set; } = static () => false;
        public static Func<byte[]> LoadBuildTagData { get; set; } = static () => null;
        public static Func<string> GetProjectTagSettingsDirectory { get; set; } =
            static () => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ProjectSettings", "GameplayTags"));
        public static Func<IEnumerable<IGameplayTagSource>> EnumerateProjectTagSources { get; set; } =
            static () => Array.Empty<IGameplayTagSource>();

        /// <summary>
        /// Registers an additional project tag source, such as a DataTable/Luban-backed tag catalog.
        /// Sources are keyed by name so repeated startup calls replace the old source.
        /// Register sources before <see cref="GameplayTagManager.InitializeIfNeeded"/> or call
        /// <see cref="GameplayTagManager.ReloadTags"/> after changing sources at runtime.
        /// </summary>
        public static void RegisterProjectTagSource(IGameplayTagSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (string.IsNullOrWhiteSpace(source.Name))
            {
                throw new ArgumentException("Gameplay tag source name cannot be empty.", nameof(source));
            }

            lock (ProjectTagSourcesLock)
            {
                Dictionary<string, IGameplayTagSource> next = new(s_ProjectTagSources, StringComparer.Ordinal)
                {
                    [source.Name] = source
                };
                Volatile.Write(ref s_ProjectTagSources, next);
            }
        }

        public static bool UnregisterProjectTagSource(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return false;
            }

            lock (ProjectTagSourcesLock)
            {
                if (!s_ProjectTagSources.ContainsKey(sourceName))
                {
                    return false;
                }

                Dictionary<string, IGameplayTagSource> next = new(s_ProjectTagSources, StringComparer.Ordinal);
                bool removed = next.Remove(sourceName);
                Volatile.Write(ref s_ProjectTagSources, next);
                return removed;
            }
        }

        public static void ClearRegisteredProjectTagSources()
        {
            lock (ProjectTagSourcesLock)
            {
                Volatile.Write(ref s_ProjectTagSources, new Dictionary<string, IGameplayTagSource>(StringComparer.Ordinal));
            }
        }

        internal static void RegisterAdditionalProjectTagSources(GameplayTagRegistrationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Dictionary<string, IGameplayTagSource> sources = Volatile.Read(ref s_ProjectTagSources);
            if (sources.Count == 0)
            {
                return;
            }

            if (sources.Count == 1)
            {
                foreach (IGameplayTagSource source in sources.Values)
                {
                    source.RegisterTags(context);
                }

                return;
            }

            string[] sourceNames = new string[sources.Count];
            int index = 0;
            foreach (string sourceName in sources.Keys)
            {
                sourceNames[index++] = sourceName;
            }

            Array.Sort(sourceNames, StringComparer.Ordinal);
            for (int i = 0; i < sourceNames.Length; i++)
            {
                sources[sourceNames[i]].RegisterTags(context);
            }
        }
    }

    /// <summary>
    /// Provides a simple, pluggable logger for the GameplayTags system.
    /// This allows the core library to remain engine-agnostic.
    /// A consumer can assign their own logging implementation, for example:
    /// GameplayTagLogger.LogWarning = MyEngine.Logger.Warning;
    /// </summary>
    public static class GameplayTagLogger
    {
        public static void LogWarning(string message)
        {
            GameplayTagRuntimePlatform.LogWarning?.Invoke(message);
        }

        public static void LogError(string message)
        {
            GameplayTagRuntimePlatform.LogError?.Invoke(message);
        }
    }
}
