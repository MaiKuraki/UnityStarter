using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace CycloneGames.UIFramework.Editor
{
    /// <summary>
    /// Auto-detects optional dependencies that live in Assets/ (not UPM)
    /// and sets scripting defines accordingly.
    /// This covers the case where versionDefines cannot work (non-UPM packages).
    /// Runs on every domain reload; only touches PlayerSettings when the define state actually changes.
    /// </summary>
    [InitializeOnLoad]
    internal static class OptionalDependencyDefineManager
    {
        private struct Entry
        {
            public string AsmdefName;
            public string Define;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { AsmdefName = "CycloneGames.Localization.Runtime", Define = "CYCLONE_LOCALIZATION" },
        };

        static OptionalDependencyDefineManager()
        {
            EditorApplication.delayCall += Detect;
        }

        private static void Detect()
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup);
            string raw = PlayerSettings.GetScriptingDefineSymbols(target);
            var defines = new List<string>(raw.Split(';'));
            bool changed = false;

            for (int i = 0; i < Entries.Length; i++)
            {
                bool present = AsmdefExists(Entries[i].AsmdefName);
                bool hasDef = defines.Contains(Entries[i].Define);

                if (present && !hasDef)
                {
                    defines.Add(Entries[i].Define);
                    changed = true;
                }
                else if (!present && hasDef)
                {
                    defines.Remove(Entries[i].Define);
                    changed = true;
                }
            }

            if (changed)
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        }

        private static bool AsmdefExists(string asmdefName)
        {
            string[] guids = AssetDatabase.FindAssets(asmdefName + " t:AssemblyDefinitionAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                // Exact filename match to avoid partial hits
                if (path.EndsWith("/" + asmdefName + ".asmdef") ||
                    path.EndsWith("\\" + asmdefName + ".asmdef"))
                    return true;
            }
            return false;
        }
    }
}
