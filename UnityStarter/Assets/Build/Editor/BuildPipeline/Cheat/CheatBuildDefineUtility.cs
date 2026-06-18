using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal readonly struct CheatBuildDefineScope : IDisposable
    {
        private readonly NamedBuildTarget _target;
        private readonly string _originalDefines;
        private readonly bool _changed;

        public CheatBuildDefineScope(NamedBuildTarget target, string originalDefines, bool changed)
        {
            _target = target;
            _originalDefines = originalDefines;
            _changed = changed;
        }

        public void Dispose()
        {
            if (!_changed)
            {
                return;
            }

            PlayerSettings.SetScriptingDefineSymbols(_target, _originalDefines);
        }
    }

    internal static class CheatBuildDefineUtility
    {
        public const string DefineSymbol = "ENABLE_CHEAT";

        private const string RuntimeAssemblyName = "CycloneGames.Cheat.Runtime";
        private const string RuntimeTypeFullName = "CycloneGames.Cheat.Runtime.CheatCommandRuntime";
        private const string RuntimeTypeQualifiedName = RuntimeTypeFullName + ", " + RuntimeAssemblyName;

        private static readonly char[] DefineSeparators = { ';' };

        public static bool ShouldRequestCheat(BuildData buildData, bool isDevelopmentBuild, bool? overrideValue)
        {
            if (overrideValue.HasValue)
            {
                return overrideValue.Value;
            }

            if (buildData == null)
            {
                return false;
            }

            switch (buildData.CheatBuildMode)
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

        public static bool ShouldEnableCheat(BuildData buildData, bool isDevelopmentBuild, bool? overrideValue)
        {
            return ShouldRequestCheat(buildData, isDevelopmentBuild, overrideValue) && IsCheatModuleInstalled();
        }

        public static bool IsCheatModuleInstalled()
        {
            if (Type.GetType(RuntimeTypeQualifiedName, false) != null)
            {
                return true;
            }

            System.Reflection.Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                System.Reflection.Assembly assembly = loadedAssemblies[i];
                if (string.Equals(assembly.GetName().Name, RuntimeAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            Assembly[] assemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            for (int i = 0; i < assemblies.Length; i++)
            {
                string assemblyName = assemblies[i].name;
                if (string.Equals(assemblyName, RuntimeAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasCheatDefine(NamedBuildTarget target)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            return HasDefine(defines, DefineSymbol);
        }

        public static CheatBuildDefineScope Apply(NamedBuildTarget target, bool enableCheat)
        {
            string originalDefines = PlayerSettings.GetScriptingDefineSymbols(target);
            string updatedDefines = SetDefine(originalDefines, DefineSymbol, enableCheat);
            bool changed = !string.Equals(originalDefines, updatedDefines, StringComparison.Ordinal);

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, updatedDefines);
                Debug.Log($"[CheatBuildDefineUtility] {(enableCheat ? "Enabled" : "Disabled")} {DefineSymbol} for {target}.");
            }
            else
            {
                Debug.Log($"[CheatBuildDefineUtility] {DefineSymbol} already {(enableCheat ? "enabled" : "disabled")} for {target}.");
            }

            return new CheatBuildDefineScope(target, originalDefines, changed);
        }

        private static bool HasDefine(string defines, string define)
        {
            if (string.IsNullOrEmpty(defines))
            {
                return false;
            }

            string[] parts = defines.Split(DefineSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), define, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SetDefine(string defines, string define, bool enabled)
        {
            var result = new List<string>(16);

            if (!string.IsNullOrEmpty(defines))
            {
                string[] parts = defines.Split(DefineSeparators, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string item = parts[i].Trim();
                    if (item.Length == 0 || string.Equals(item, define, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!result.Contains(item))
                    {
                        result.Add(item);
                    }
                }
            }

            if (enabled)
            {
                result.Add(define);
            }

            return string.Join(";", result);
        }
    }
}
