#if UNITY_EDITOR

using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Editor.Utility
{

    /// <summary>Provides utility functions for managing blocklists.</summary>
    public static class BlocklistUtility
    {

        /// <summary>Gets the whitelist that ASM uses to determine scenes available for import.</summary>
        /// <remarks>Whitelist is disabled if no items exist in it.</remarks>
        public static Blocklist whitelist => SceneManager.settings.project.m_whitelist;

        /// <summary>Gets the blacklist that ASM uses to determine scenes available for import.</summary>
        public static Blocklist blacklist => SceneManager.settings.project.m_blacklist;

        /// <summary>Gets whatever the path is whitelisted.</summary>
        public static bool IsWhitelisted(string path)
        {

            if (whitelist.count == 0)
                return true;

            return Prechecks(ref path) && whitelist.MatchesFilter(path);

        }

        /// <summary>Gets whatever the path is blacklisted.</summary>
        public static bool IsBlacklisted(string path) =>
            Prechecks(ref path) && !SceneImportUtility.StringExtensions.IsASMScene(path) && (blacklist.MatchesFilter(path) || !IsWhitelisted(path));

        internal static bool Prechecks(ref string path)
        {

            if (!SceneManager.isInitialized)
                return false;

            if (string.IsNullOrEmpty(path))
                return false;

            Normalize(ref path);

            return true;

        }

        internal static void Normalize(ref string path) => path = Normalize(path);
        internal static string Normalize(string path) => path.ToLower().Replace("\\", "/").Trim(' ');

        internal static void Notify() => SceneImportUtility.Notify();
        internal static void Save() => SceneManager.settings.project.Save();

    }

}

#endif