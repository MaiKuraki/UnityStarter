using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace Build.Pipeline.Editor
{
    internal static class CheatBuildDefineUtility
    {
        public const string DefineSymbol = "ENABLE_CHEAT";

        private const string RuntimeAssemblyName = "CycloneGames.Cheat.Runtime";
        private const string RuntimeTypeFullName = "CycloneGames.Cheat.Runtime.CheatCommandRuntime";
        private const string RuntimeTypeQualifiedName = RuntimeTypeFullName + ", " + RuntimeAssemblyName;

        private static readonly char[] DefineSeparators = { ';' };

        public static bool ShouldRequestCheat(
            CheatBuildMode mode,
            bool isDevelopmentBuild,
            bool? overrideValue)
        {
            if (overrideValue.HasValue)
            {
                return overrideValue.Value;
            }

            switch (mode)
            {
                case CheatBuildMode.Disabled:
                    return false;
                case CheatBuildMode.DevelopmentBuilds:
                    return isDevelopmentBuild;
                case CheatBuildMode.Enabled:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsCheatModuleInstalled()
        {
            Assembly[] assemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            bool includedInPlayer = false;
            for (int index = 0; index < assemblies.Length; index++)
            {
                if (string.Equals(assemblies[index].name, RuntimeAssemblyName, StringComparison.Ordinal))
                {
                    includedInPlayer = true;
                    break;
                }
            }

            if (!includedInPlayer)
            {
                return false;
            }

            if (Type.GetType(RuntimeTypeQualifiedName, false) != null)
            {
                return true;
            }

            System.Reflection.Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < loadedAssemblies.Length; index++)
            {
                System.Reflection.Assembly loadedAssembly = loadedAssemblies[index];
                if (string.Equals(
                        loadedAssembly.GetName().Name,
                        RuntimeAssemblyName,
                        StringComparison.Ordinal)
                    && loadedAssembly.GetType(RuntimeTypeFullName, false) != null)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasCheatDefine(NamedBuildTarget target)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            if (!string.IsNullOrEmpty(defines)
                && ContainsCheatDefine(
                    defines.Split(DefineSeparators, StringSplitOptions.RemoveEmptyEntries)))
            {
                return true;
            }

            Assembly[] assemblies = CompilationPipeline.GetAssemblies(
                AssembliesType.PlayerWithoutTestAssemblies);
            for (int index = 0; index < assemblies.Length; index++)
            {
                if (IsCheatRuntimeAssemblyWithDefine(
                        assemblies[index].name,
                        assemblies[index].defines))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsCheatRuntimeAssemblyWithDefine(
            string assemblyName,
            IReadOnlyList<string> defines)
        {
            return string.Equals(
                    assemblyName,
                    RuntimeAssemblyName,
                    StringComparison.Ordinal)
                && ContainsCheatDefine(defines);
        }

        internal static bool ContainsCheatDefine(IReadOnlyList<string> defines)
        {
            if (defines == null)
            {
                return false;
            }

            for (int index = 0; index < defines.Count; index++)
            {
                if (string.Equals(
                        defines[index]?.Trim(),
                        DefineSymbol,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
