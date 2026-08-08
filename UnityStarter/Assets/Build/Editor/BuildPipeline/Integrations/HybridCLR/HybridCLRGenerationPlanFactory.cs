using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal static class HybridCLRGenerationPlanFactory
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        internal static HybridCLRGenerationPlan Create(
            BuildTarget target,
            bool fullGeneration,
            bool includeObfuz)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var plan = new HybridCLRGenerationPlan(projectRoot);

            string hotUpdateDirectory = GetHybridCLRTargetDirectory(
                target,
                "GetHotUpdateDllsOutputDirByTarget");
            plan.AddMirrorDirectory(ResolveProjectPath(projectRoot, hotUpdateDirectory));

            string generatedCppDirectory = ResolveProjectPath(
                projectRoot,
                GetSettingsUtilStringProperty("GeneratedCppDir"));
            if (fullGeneration)
            {
                string strippedAotDirectory = GetHybridCLRTargetDirectory(
                    target,
                    "GetAssembliesPostIl2CppStripDir");
                plan.AddMirrorDirectory(ResolveProjectPath(projectRoot, strippedAotDirectory));

                string hybridClrDataDirectory = ResolveProjectPath(
                    projectRoot,
                    GetSettingsUtilStringProperty("HybridCLRDataDir"));
                plan.AddReplaceDirectory(Path.Combine(
                    hybridClrDataDirectory,
                    "StrippedAOTDllsTempProj",
                    target.ToString()));

                plan.AddGeneratedAssetFile(GetConfiguredGeneratedAssetFile(
                    projectRoot,
                    "outputLinkFile"));
                plan.AddGeneratedAssetFile(GetConfiguredGeneratedAssetFile(
                    projectRoot,
                    "outputAOTGenericReferenceFile"));
                plan.AddSnapshotFile(Path.Combine(generatedCppDirectory, "UnityVersion.h"));
                plan.AddSnapshotFile(Path.Combine(generatedCppDirectory, "AssemblyManifest.cpp"));
                plan.AddSnapshotFile(Path.Combine(generatedCppDirectory, "MethodBridge.cpp"));
            }
            else if (includeObfuz)
            {
                // Obfuz fast mode regenerates these two shared outputs even though it reuses
                // the existing stripped-AOT directory.
                plan.AddGeneratedAssetFile(GetConfiguredGeneratedAssetFile(
                    projectRoot,
                    "outputAOTGenericReferenceFile"));
                plan.AddSnapshotFile(Path.Combine(generatedCppDirectory, "MethodBridge.cpp"));
            }

            if (includeObfuz)
            {
                AddObfuzPaths(plan, projectRoot, target, fullGeneration);
            }

            return plan;
        }

        private static void AddObfuzPaths(
            HybridCLRGenerationPlan plan,
            string projectRoot,
            BuildTarget target,
            bool fullGeneration)
        {
            plan.AddReplaceDirectory(ResolveProjectPath(
                projectRoot,
                ObfuzIntegrator.GetObfuscatedHotUpdateAssemblyOutputPath(target)));

            Type settingsType = RequireType("Obfuz.Settings.ObfuzSettings");
            PropertyInfo instanceProperty = RequireProperty(
                settingsType,
                "Instance",
                PublicStatic);
            object settings = instanceProperty.GetValue(null)
                ?? throw new InvalidOperationException("ObfuzSettings.Instance returned null.");
            MethodInfo getTempOutput = ReflectionCache.GetMethod(
                settingsType,
                "GetObfuscatedAssemblyTempOutputPath",
                PublicInstance,
                new[] { typeof(BuildTarget) });
            if (getTempOutput == null || getTempOutput.ReturnType != typeof(string))
            {
                throw new MissingMethodException(
                    settingsType.FullName,
                    "GetObfuscatedAssemblyTempOutputPath(BuildTarget)");
            }

            string tempOutput = InvokeString(
                getTempOutput,
                settings,
                new object[] { target },
                "Obfuz temporary output path");
            plan.AddReplaceDirectory(ResolveProjectPath(projectRoot, tempOutput));

            if (!fullGeneration)
            {
                return;
            }

            string localIl2CppDirectory = ResolveProjectPath(
                projectRoot,
                GetSettingsUtilStringProperty("LocalIl2CppDir"));
            string metadataDirectory = Path.Combine(
                localIl2CppDirectory,
                "libil2cpp",
                "hybridclr",
                "metadata");
            string[] polymorphicOverlayFiles =
            {
                "MetadataReader.h",
                "PolymorphicDefs.h",
                "PolymorphicDatas.h",
                "PolymorphicRawImage.h",
                "PolymorphicRawImage.cpp",
                "Image.cpp"
            };
            for (int index = 0; index < polymorphicOverlayFiles.Length; index++)
            {
                plan.AddSnapshotFile(Path.Combine(
                    metadataDirectory,
                    polymorphicOverlayFiles[index]));
            }
        }

        private static string GetHybridCLRTargetDirectory(
            BuildTarget target,
            string methodName)
        {
            Type settingsUtilType = RequireType("HybridCLR.Editor.SettingsUtil");
            MethodInfo method = ReflectionCache.GetMethod(
                settingsUtilType,
                methodName,
                PublicStatic,
                new[] { typeof(BuildTarget) });
            if (method == null || method.ReturnType != typeof(string))
            {
                throw new MissingMethodException(settingsUtilType.FullName, methodName);
            }

            return InvokeString(
                method,
                instance: null,
                new object[] { target },
                $"HybridCLR {methodName} result");
        }

        private static string GetSettingsUtilStringProperty(string propertyName)
        {
            Type settingsUtilType = RequireType("HybridCLR.Editor.SettingsUtil");
            PropertyInfo property = RequireProperty(
                settingsUtilType,
                propertyName,
                PublicStatic);
            if (property.PropertyType != typeof(string))
            {
                throw new InvalidOperationException(
                    $"HybridCLR SettingsUtil.{propertyName} must return string.");
            }

            string value = property.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"HybridCLR SettingsUtil.{propertyName} returned an empty path.");
            }

            return value;
        }

        private static string GetConfiguredGeneratedAssetFile(
            string projectRoot,
            string fieldName)
        {
            Type settingsUtilType = RequireType("HybridCLR.Editor.SettingsUtil");
            PropertyInfo settingsProperty = RequireProperty(
                settingsUtilType,
                "HybridCLRSettings",
                PublicStatic);
            object settings = settingsProperty.GetValue(null)
                ?? throw new InvalidOperationException(
                    "HybridCLR SettingsUtil.HybridCLRSettings returned null.");
            FieldInfo field = ReflectionCache.GetField(
                settings.GetType(),
                fieldName,
                PublicInstance);
            if (field == null || field.FieldType != typeof(string))
            {
                throw new MissingFieldException(settings.GetType().FullName, fieldName);
            }

            string relative = field.GetValue(settings) as string;
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generated Asset setting '{fieldName}' must be a non-empty Assets-relative path.");
            }

            string assetsRoot = Path.Combine(projectRoot, "Assets");
            string output = Path.GetFullPath(Path.Combine(assetsRoot, relative));
            if (!BuildPathPolicy.IsStrictDescendant(assetsRoot, output))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generated Asset setting '{fieldName}' escaped Assets: '{relative}'.");
            }

            return output;
        }

        private static Type RequireType(string typeName)
        {
            Type type = ReflectionCache.GetType(typeName);
            if (type == null)
            {
                throw new InvalidOperationException(
                    $"Required optional integration API is unavailable: '{typeName}'.");
            }

            return type;
        }

        private static PropertyInfo RequireProperty(
            Type type,
            string propertyName,
            BindingFlags bindingFlags)
        {
            PropertyInfo property = ReflectionCache.GetProperty(
                type,
                propertyName,
                bindingFlags);
            if (property == null || !property.CanRead)
            {
                throw new MissingMemberException(type.FullName, propertyName);
            }

            return property;
        }

        private static string InvokeString(
            MethodInfo method,
            object instance,
            object[] arguments,
            string description)
        {
            try
            {
                string value = method.Invoke(instance, arguments) as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"{description} was empty.");
                }

                return value;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Failed to resolve {description}.",
                    exception.InnerException);
            }
        }

        private static string ResolveProjectPath(string projectRoot, string path)
        {
            string resolved = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(projectRoot, path));
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, resolved))
            {
                throw new InvalidOperationException(
                    $"HybridCLR/Obfuz generation path must remain inside the Unity project: '{resolved}'.");
            }

            return resolved;
        }
    }
}
