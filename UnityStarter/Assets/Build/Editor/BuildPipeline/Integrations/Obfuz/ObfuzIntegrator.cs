using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Narrow reflection boundary for optional Obfuz integrations used by the build pipeline.
    /// </summary>
    internal static class ObfuzIntegrator
    {
        private const string DebugFlag = "<color=cyan>[Obfuz]</color>";
        private const string ObfuzSettingsTypeName = "Obfuz.Settings.ObfuzSettings";
        private const string BuildPipelineSettingsTypeName = "Obfuz.Settings.BuildPipelineSettings";
        private const string EncryptionVirtualMachineTypeName = "Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine";
        private const string ObfuscateUtilTypeName = "Obfuz4HybridCLR.ObfuscateUtil";
        private const string PrebuildCommandExtTypeName = "Obfuz4HybridCLR.PrebuildCommandExt";

        internal static bool IsBaseObfuzAvailable()
        {
            return ReflectionCache.GetType(ObfuzSettingsTypeName) != null
                && ReflectionCache.GetType(BuildPipelineSettingsTypeName) != null;
        }

        internal static bool IsHybridCLRObfuzAvailable()
        {
            Type obfuscateUtilType = ReflectionCache.GetType(ObfuscateUtilTypeName);
            Type prebuildCommandExtType = ReflectionCache.GetType(PrebuildCommandExtTypeName);
            if (obfuscateUtilType == null || prebuildCommandExtType == null)
            {
                return false;
            }

            return FindStaticMethod(
                    obfuscateUtilType,
                    "ObfuscateHotUpdateAssemblies",
                    typeof(BuildTarget),
                    typeof(string)) != null
                && FindStaticMethod(
                    prebuildCommandExtType,
                    "GenerateMethodBridgeAndReversePInvokeWrapper",
                    typeof(BuildTarget),
                    typeof(string)) != null
                && FindStaticMethod(
                    prebuildCommandExtType,
                    "GenerateAOTGenericReference",
                    typeof(BuildTarget),
                    typeof(string)) != null
                && FindStaticMethod(
                    prebuildCommandExtType,
                    "GetObfuscatedHotUpdateAssemblyOutputPath",
                    typeof(BuildTarget)) != null;
        }

        internal static bool VerifyEncryptionVMCompiled()
        {
            return IsBaseObfuzAvailable()
                && ReflectionCache.GetType(EncryptionVirtualMachineTypeName) != null;
        }

        internal static bool TryGetObfuzBuildPipelineEnabled(out bool enabled)
        {
            enabled = false;
            if (!IsBaseObfuzAvailable())
            {
                return false;
            }

            try
            {
                if (!TryGetPipelineState(out object pipelineSettings, out FieldInfo enableField))
                {
                    return false;
                }

                object value = enableField.GetValue(pipelineSettings);
                if (!(value is bool currentValue))
                {
                    return false;
                }

                enabled = currentValue;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{DebugFlag} Failed to read the Obfuz build-pipeline state: {exception.Message}");
                return false;
            }
        }

        internal static void ObfuscateHotUpdateAssemblies(BuildTarget target, string outputDirectory)
        {
            EnsureOutputPath(outputDirectory, nameof(outputDirectory));
            Type type = RequireType(ObfuscateUtilTypeName);
            MethodInfo method = RequireStaticMethod(
                type,
                "ObfuscateHotUpdateAssemblies",
                new[] { typeof(BuildTarget), typeof(string) });
            InvokeStatic(method, new object[] { target, outputDirectory });
        }

        internal static void GenerateMethodBridgeAndReversePInvokeWrapper(
            BuildTarget target,
            string obfuscatedHotUpdateDllPath)
        {
            EnsureOutputPath(obfuscatedHotUpdateDllPath, nameof(obfuscatedHotUpdateDllPath));
            Type type = RequireType(PrebuildCommandExtTypeName);
            MethodInfo method = RequireStaticMethod(
                type,
                "GenerateMethodBridgeAndReversePInvokeWrapper",
                new[] { typeof(BuildTarget), typeof(string) });
            InvokeStatic(method, new object[] { target, obfuscatedHotUpdateDllPath });
        }

        internal static void GenerateAOTGenericReference(
            BuildTarget target,
            string obfuscatedHotUpdateDllPath)
        {
            EnsureOutputPath(obfuscatedHotUpdateDllPath, nameof(obfuscatedHotUpdateDllPath));
            Type type = RequireType(PrebuildCommandExtTypeName);
            MethodInfo method = RequireStaticMethod(
                type,
                "GenerateAOTGenericReference",
                new[] { typeof(BuildTarget), typeof(string) });
            InvokeStatic(method, new object[] { target, obfuscatedHotUpdateDllPath });
        }

        internal static string GetObfuscatedHotUpdateAssemblyOutputPath(BuildTarget target)
        {
            Type type = RequireType(PrebuildCommandExtTypeName);
            MethodInfo method = RequireStaticMethod(
                type,
                "GetObfuscatedHotUpdateAssemblyOutputPath",
                new[] { typeof(BuildTarget) });
            string path = InvokeStatic(method, new object[] { target }) as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    $"Obfuz4HybridCLR returned an empty output path for build target '{target}'.");
            }

            return path;
        }

        private static bool TryGetPipelineState(out object pipelineSettings, out FieldInfo enableField)
        {
            pipelineSettings = null;
            enableField = null;

            Type settingsType = ReflectionCache.GetType(ObfuzSettingsTypeName);
            Type pipelineType = ReflectionCache.GetType(BuildPipelineSettingsTypeName);
            if (settingsType == null || pipelineType == null)
            {
                return false;
            }

            PropertyInfo instanceProperty = ReflectionCache.GetProperty(
                settingsType,
                "Instance",
                BindingFlags.Public | BindingFlags.Static);
            FieldInfo pipelineField = ReflectionCache.GetField(
                settingsType,
                "buildPipelineSettings",
                BindingFlags.Public | BindingFlags.Instance);
            enableField = ReflectionCache.GetField(
                pipelineType,
                "enable",
                BindingFlags.Public | BindingFlags.Instance);
            if (instanceProperty == null || pipelineField == null || enableField == null)
            {
                return false;
            }

            object settings = instanceProperty.GetValue(null);
            if (settings == null)
            {
                return false;
            }

            pipelineSettings = pipelineField.GetValue(settings);
            return pipelineSettings != null;
        }

        private static Type RequireType(string typeName)
        {
            Type type = ReflectionCache.GetType(typeName);
            if (type == null)
            {
                throw new InvalidOperationException(
                    $"Required optional integration API is unavailable: '{typeName}'. Install a compatible package version.");
            }

            return type;
        }

        private static MethodInfo FindStaticMethod(Type type, string methodName, params Type[] parameterTypes)
        {
            return ReflectionCache.GetMethod(
                type,
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                parameterTypes);
        }

        private static MethodInfo RequireStaticMethod(Type type, string methodName, Type[] parameterTypes)
        {
            MethodInfo method = FindStaticMethod(type, methodName, parameterTypes);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            return method;
        }

        private static object InvokeStatic(MethodInfo method, object[] arguments)
        {
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Optional integration call '{method.DeclaringType?.FullName}.{method.Name}' failed.",
                    exception.InnerException);
            }
        }

        private static void EnsureOutputPath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An Obfuz output path is required.", parameterName);
            }
        }
    }
}
