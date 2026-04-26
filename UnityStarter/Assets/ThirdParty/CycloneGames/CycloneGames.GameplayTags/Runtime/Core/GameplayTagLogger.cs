using System;
using System.Collections.Generic;
using System.IO;

namespace CycloneGames.GameplayTags.Core
{
    /// <summary>
    /// Provides the bridge between the core GameplayTags runtime and host-specific features.
    /// Unity projects can keep the defaults, while plain C# hosts can override delegates at startup.
    /// </summary>
    public static class GameplayTagRuntimePlatform
    {
        public static Action<string> LogWarning { get; set; } = message => Console.WriteLine(message);
        public static Action<string> LogError { get; set; } = message => Console.Error.WriteLine(message);
        public static Func<bool> IsRuntimePlaying { get; set; } = static () => false;
        public static Func<byte[]> LoadBuildTagData { get; set; } = static () => null;
        public static Func<string> GetProjectTagSettingsDirectory { get; set; } =
            static () => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ProjectSettings", "GameplayTags"));
        public static Func<IEnumerable<IGameplayTagSource>> EnumerateProjectTagSources { get; set; } =
            static () => Array.Empty<IGameplayTagSource>();
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
