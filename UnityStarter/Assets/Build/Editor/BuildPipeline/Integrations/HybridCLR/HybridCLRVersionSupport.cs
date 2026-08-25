using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    internal static class HybridCLRVersionSupport
    {
        private const string PrebuildCommandTypeName =
            "HybridCLR.Editor.Commands.PrebuildCommand";
        private const string CompileDllCommandTypeName =
            "HybridCLR.Editor.Commands.CompileDllCommand";
        private const string InstallerControllerTypeName =
            "HybridCLR.Editor.Installer.InstallerController";
        private const string SettingsUtilTypeName = "HybridCLR.Editor.SettingsUtil";

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>
        /// Verifies every reflected HybridCLR editor member this integration depends on.
        /// Returns null when the installed package supports the pinned shape, otherwise a
        /// semicolon-separated string of every missing or mismatched item.
        /// </summary>
        public static string ValidateSupport(BuildIncrementality incrementality)
        {
            var failures = new List<string>();

            // hybridclr_unity-8.14.1/Editor/Commands/PrebuildCommand.cs:17
            // Clean generation calls PrebuildCommand.GenerateAll(), a public static
            // parameterless method.
            if (incrementality == BuildIncrementality.Clean)
            {
                RequireStaticMethod(
                    PrebuildCommandTypeName,
                    "GenerateAll",
                    Type.EmptyTypes,
                    requiredReturnType: null,
                    failures);
            }

            // hybridclr_unity-8.14.1/Editor/Commands/CompileDllCommand.cs:31
            // Incremental generation calls CompileDll(BuildTarget). The single-argument
            // shape disambiguates it from the (string, BuildTarget, bool) and
            // (BuildTarget, bool) overloads.
            if (incrementality == BuildIncrementality.Incremental)
            {
                RequireStaticMethod(
                    CompileDllCommandTypeName,
                    "CompileDll",
                    new[] { typeof(BuildTarget) },
                    requiredReturnType: null,
                    failures);
            }

            ValidateInstallerController(failures);
            ValidateSettingsUtil(failures);

            return failures.Count == 0 ? null : string.Join("; ", failures);
        }

        private static void ValidateInstallerController(List<string> failures)
        {
            if (!TryResolveType(InstallerControllerTypeName, failures, out Type type))
            {
                return;
            }

            // hybridclr_unity-8.14.1/Editor/Installer/InstallerController.cs:36
            // The builder constructs InstallerController via Activator.CreateInstance, so a
            // public parameterless constructor must remain available.
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                failures.Add(
                    $"{InstallerControllerTypeName} no longer exposes a public parameterless constructor.");
            }

            // hybridclr_unity-8.14.1/Editor/Installer/InstallerController.cs:232
            // The builder probes HasInstalledHybridCLR(), a public instance bool method.
            MethodInfo hasInstalled = ReflectionCache.GetMethod(
                type,
                "HasInstalledHybridCLR",
                PublicInstance);
            if (hasInstalled == null || hasInstalled.ReturnType != typeof(bool))
            {
                failures.Add(
                    $"{InstallerControllerTypeName}.HasInstalledHybridCLR() must be a public instance bool method.");
            }
        }

        private static void ValidateSettingsUtil(List<string> failures)
        {
            // hybridclr_unity-8.14.1/Editor/SettingsUtil.cs:53,58
            // Both target-directory resolvers are public static string(BuildTarget) methods.
            RequireStaticMethod(
                SettingsUtilTypeName,
                "GetHotUpdateDllsOutputDirByTarget",
                new[] { typeof(BuildTarget) },
                typeof(string),
                failures);
            RequireStaticMethod(
                SettingsUtilTypeName,
                "GetAssembliesPostIl2CppStripDir",
                new[] { typeof(BuildTarget) },
                typeof(string),
                failures);

            // hybridclr_unity-8.14.1/Editor/SettingsUtil.cs:45,39,43
            // The generation-plan layout reads these public static string properties.
            RequireStringProperty(SettingsUtilTypeName, "GeneratedCppDir", failures);
            RequireStringProperty(SettingsUtilTypeName, "HybridCLRDataDir", failures);
            RequireStringProperty(SettingsUtilTypeName, "LocalIl2CppDir", failures);

            // hybridclr_unity-8.14.1/Editor/SettingsUtil.cs:68
            // Builder validation consumes this as IEnumerable<string>.
            RequireEnumerableStringProperty(
                SettingsUtilTypeName,
                "HotUpdateAssemblyNamesExcludePreserved",
                failures);

            // hybridclr_unity-8.14.1/Editor/SettingsUtil.cs:114
            // The generation plan reads HybridCLRSettings to resolve generated-asset fields.
            RequireReadableProperty(SettingsUtilTypeName, "HybridCLRSettings", failures);
        }

        private static bool TryResolveType(
            string typeName,
            List<string> failures,
            out Type type)
        {
            type = ReflectionCache.GetType(typeName);
            if (type == null)
            {
                failures.Add($"Type '{typeName}' is unavailable.");
                return false;
            }

            return true;
        }

        private static void RequireStaticMethod(
            string typeName,
            string methodName,
            Type[] parameterTypes,
            Type requiredReturnType,
            List<string> failures)
        {
            if (!TryResolveType(typeName, failures, out Type type))
            {
                return;
            }

            MethodInfo method = ReflectionCache.GetMethod(
                type,
                methodName,
                PublicStatic,
                parameterTypes);
            string signature = $"{methodName}({FormatParameters(parameterTypes)})";
            if (method == null)
            {
                failures.Add($"{typeName}.{signature} is unavailable.");
                return;
            }

            if (requiredReturnType != null && method.ReturnType != requiredReturnType)
            {
                failures.Add(
                    $"{typeName}.{signature} must return {requiredReturnType.Name} but returns {method.ReturnType.Name}.");
            }
        }

        private static void RequireStringProperty(
            string typeName,
            string propertyName,
            List<string> failures)
        {
            PropertyInfo property = ResolveStaticProperty(typeName, propertyName, failures);
            if (property != null && property.PropertyType != typeof(string))
            {
                failures.Add(
                    $"{typeName}.{propertyName} must return string but returns {property.PropertyType.Name}.");
            }
        }

        private static void RequireEnumerableStringProperty(
            string typeName,
            string propertyName,
            List<string> failures)
        {
            PropertyInfo property = ResolveStaticProperty(typeName, propertyName, failures);
            if (property != null
                && !typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType))
            {
                failures.Add(
                    $"{typeName}.{propertyName} must return IEnumerable<string> but returns {property.PropertyType.Name}.");
            }
        }

        private static void RequireReadableProperty(
            string typeName,
            string propertyName,
            List<string> failures)
        {
            ResolveStaticProperty(typeName, propertyName, failures);
        }

        private static PropertyInfo ResolveStaticProperty(
            string typeName,
            string propertyName,
            List<string> failures)
        {
            if (!TryResolveType(typeName, failures, out Type type))
            {
                return null;
            }

            PropertyInfo property = ReflectionCache.GetProperty(type, propertyName, PublicStatic);
            if (property == null || !property.CanRead)
            {
                failures.Add($"{typeName}.{propertyName} is unavailable.");
                return null;
            }

            return property;
        }

        private static string FormatParameters(Type[] parameterTypes)
        {
            if (parameterTypes == null || parameterTypes.Length == 0)
            {
                return string.Empty;
            }

            var names = new string[parameterTypes.Length];
            for (int index = 0; index < parameterTypes.Length; index++)
            {
                names[index] = parameterTypes[index].Name;
            }

            return string.Join(", ", names);
        }
    }
}
