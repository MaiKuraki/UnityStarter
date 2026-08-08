using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal enum HybridCLRBuildVariant
    {
        Standard,
        Obfuz
    }

    [HotUpdateProviderAuthoring(
        HybridCLRHotUpdateProviderIds.Standard,
        "HybridCLR",
        Description = "Compile and publish HybridCLR hot-update and AOT metadata assemblies.",
        RequiredEditorTypeNames = new[]
        {
            "HybridCLR.Editor.Commands.PrebuildCommand"
        },
        Order = 100)]
    [CreateAssetMenu(menuName = "CycloneGames/Build/Hot Update/HybridCLR")]
    public class HybridCLRBuildConfig : HotUpdateBuildConfiguration
    {
        [Tooltip("Assembly Definition Assets compiled and published as HybridCLR hot-update assemblies.")]
        [SerializeField] private List<AssemblyDefinitionAsset> hotUpdateAssemblies =
            new List<AssemblyDefinitionAsset>();

        [Tooltip("Build-exclusive Assets directory for transactionally published hot-update DLLs.")]
        [SerializeField] private DefaultAsset hotUpdateDllOutputDirectory;

        [Tooltip("Build-exclusive Assets directory for transactionally published AOT metadata DLLs.")]
        [SerializeField] private DefaultAsset aotDllOutputDirectory;

        public override string ProviderId => HybridCLRHotUpdateProviderIds.Standard;

        internal virtual HybridCLRBuildVariant Variant =>
            HybridCLRBuildVariant.Standard;

        public List<string> GetHotUpdateAssemblyNames()
        {
            var names = new List<string>();
            if (hotUpdateAssemblies == null)
            {
                return names;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssemblyDefinitionAsset assembly in hotUpdateAssemblies)
            {
                if (assembly == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(assembly);
                if (string.IsNullOrEmpty(assetPath)
                    || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    var data = JsonUtility.FromJson<AsmDefJson>(assembly.text);
                    if (!string.IsNullOrWhiteSpace(data?.name) && seen.Add(data.name))
                    {
                        names.Add(data.name);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[HybridCLRBuildConfig] Failed to parse asmdef '{assetPath}': {exception.Message}");
                }
            }

            return names;
        }

        internal IReadOnlyList<string> GetHotUpdateAssemblyAssetPaths()
        {
            var paths = new List<string>();
            if (hotUpdateAssemblies == null)
            {
                return paths;
            }

            foreach (AssemblyDefinitionAsset assembly in hotUpdateAssemblies)
            {
                if (assembly == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(assembly);
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    paths.Add(assetPath.Replace('\\', '/'));
                }
            }

            return paths;
        }

        public string GetHotUpdateDllOutputDirectoryPath()
        {
            return hotUpdateDllOutputDirectory == null
                ? null
                : AssetDatabase.GetAssetPath(hotUpdateDllOutputDirectory);
        }

        public string GetAOTDllOutputDirectoryPath()
        {
            return aotDllOutputDirectory == null
                ? null
                : AssetDatabase.GetAssetPath(aotDllOutputDirectory);
        }

        [Serializable]
        private sealed class AsmDefJson
        {
            public string name = string.Empty;
        }
    }
}
