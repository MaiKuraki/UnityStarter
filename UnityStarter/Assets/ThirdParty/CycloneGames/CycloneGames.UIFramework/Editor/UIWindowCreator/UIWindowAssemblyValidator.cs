using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIWindowAssemblyValidator
    {
        private const string RuntimeAssemblyName = "CycloneGames.UIFramework.Runtime";
        private const string PredefinedAssemblyName = "Assembly-CSharp";
        private const int MaxAssemblyDefinitionBytes = 256 * 1024;

        [Serializable]
        private sealed class AssemblyDefinitionData
        {
            public string name = string.Empty;
            public string[] references = Array.Empty<string>();
            public string[] includePlatforms = Array.Empty<string>();
            public string[] excludePlatforms = Array.Empty<string>();
            public string[] defineConstraints = Array.Empty<string>();
            public VersionDefineData[] versionDefines = Array.Empty<VersionDefineData>();
            public bool autoReferenced = true;
            public bool noEngineReferences = false;
        }

        [Serializable]
        private sealed class VersionDefineData
        {
            public string name = string.Empty;
            public string expression = string.Empty;
            public string define = string.Empty;
        }

        [Serializable]
        private sealed class AssemblyReferenceData
        {
            public string reference = string.Empty;
        }

        private readonly struct AssemblyTarget
        {
            public readonly string Name;
            public readonly string Guid;
            public readonly string BoundaryPath;
            public readonly string DefinitionPath;
            public readonly string[] References;
            public readonly string[] IncludePlatforms;
            public readonly string[] ExcludePlatforms;
            public readonly bool HasConditionalActivation;
            public readonly bool AutoReferenced;
            public readonly bool NoEngineReferences;
            public readonly bool IsPredefined;

            public AssemblyTarget(
                string name,
                string guid,
                string boundaryPath,
                string definitionPath,
                string[] references,
                string[] includePlatforms,
                string[] excludePlatforms,
                bool hasConditionalActivation,
                bool autoReferenced,
                bool noEngineReferences,
                bool isPredefined)
            {
                Name = name ?? string.Empty;
                Guid = guid ?? string.Empty;
                BoundaryPath = boundaryPath ?? string.Empty;
                DefinitionPath = definitionPath ?? string.Empty;
                References = references ?? Array.Empty<string>();
                IncludePlatforms = includePlatforms ?? Array.Empty<string>();
                ExcludePlatforms = excludePlatforms ?? Array.Empty<string>();
                HasConditionalActivation = hasConditionalActivation;
                AutoReferenced = autoReferenced;
                NoEngineReferences = noEngineReferences;
                IsPredefined = isPredefined;
            }

            public static AssemblyTarget Predefined => new AssemblyTarget(
                PredefinedAssemblyName,
                string.Empty,
                "Assets",
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                false,
                true,
                false,
                true);
        }

        private static readonly Dictionary<string, List<AssemblyTarget>> s_definitionsByName =
            new Dictionary<string, List<AssemblyTarget>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, AssemblyTarget> s_definitionsByGuid =
            new Dictionary<string, AssemblyTarget>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_activeEditorAssemblies =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool s_indexBuilt;
        private static bool s_activeEditorAssembliesBuilt;

        internal static void InvalidateCache()
        {
            s_indexBuilt = false;
            s_activeEditorAssembliesBuilt = false;
            s_definitionsByName.Clear();
            s_definitionsByGuid.Clear();
            s_activeEditorAssemblies.Clear();
        }

        internal static void Validate(
            in UIWindowCreationPaths paths,
            bool useMvp,
            List<string> errors)
        {
            if (!TryResolveOutputAssembly(paths.ScriptFilePath, out AssemblyTarget viewAssembly, out string viewError))
            {
                errors.Add("- Window/View output: " + viewError);
                return;
            }

            if (!TryResolveRuntimeAssembly(out AssemblyTarget runtimeAssembly, out string runtimeError))
            {
                errors.Add("- Runtime assembly: " + runtimeError);
                return;
            }

            ValidateGeneratedAssembly("Window/View", viewAssembly, runtimeAssembly, errors);
            if (!useMvp)
            {
                return;
            }

            if (!TryResolveOutputAssembly(paths.PresenterFilePath, out AssemblyTarget presenterAssembly, out string presenterError))
            {
                errors.Add("- Presenter output: " + presenterError);
                return;
            }

            ValidateGeneratedAssembly("Presenter", presenterAssembly, runtimeAssembly, errors);
            if (!CanReference(presenterAssembly, viewAssembly))
            {
                errors.Add(
                    $"- Presenter assembly '{presenterAssembly.Name}' cannot reference generated view assembly " +
                    $"'{viewAssembly.Name}'. Add an explicit asmdef reference or choose compatible output folders.");
            }
        }

        internal static bool TryResolveOutputAssemblyName(
            string outputFilePath,
            out string assemblyName,
            out string error)
        {
            if (TryResolveOutputAssembly(outputFilePath, out AssemblyTarget target, out error))
            {
                assemblyName = target.Name;
                return true;
            }

            assemblyName = string.Empty;
            return false;
        }

        private static void ValidateGeneratedAssembly(
            string label,
            in AssemblyTarget assembly,
            in AssemblyTarget runtimeAssembly,
            List<string> errors)
        {
            if (assembly.NoEngineReferences)
            {
                errors.Add(
                    $"- {label} assembly '{assembly.Name}' sets noEngineReferences=true and cannot compile UIWindow code.");
            }

            if (IsEditorOnly(assembly.IncludePlatforms))
            {
                errors.Add(
                    $"- {label} assembly '{assembly.Name}' is Editor-only. Generated window code must be available to Players.");
            }
            else if (!CompilesInEditor(assembly.IncludePlatforms, assembly.ExcludePlatforms))
            {
                errors.Add(
                    $"- {label} assembly '{assembly.Name}' does not compile in the Editor, so post-compile prefab binding cannot complete.");
            }

            bool isActive = false;
            bool activeQueryFailed = false;
            try
            {
                isActive = IsActiveInCurrentEditorCompilation(assembly.Name);
            }
            catch (Exception exception)
            {
                activeQueryFailed = true;
                errors.Add(
                    $"- {label} assembly '{assembly.Name}' could not be checked against Unity's current Editor compilation graph: {exception.Message}");
            }

            if (!isActive && !activeQueryFailed)
            {
                string conditionHint = assembly.HasConditionalActivation
                    ? " Its defineConstraints/versionDefines are not satisfied in the current Editor compilation."
                    : string.Empty;
                errors.Add(
                    $"- {label} assembly '{assembly.Name}' is not active in Unity's current Editor compilation graph." +
                    conditionHint);
            }

            if (!CanReference(assembly, runtimeAssembly))
            {
                errors.Add(
                    $"- {label} assembly '{assembly.Name}' cannot reference '{RuntimeAssemblyName}'. " +
                    "Add an explicit asmdef reference or choose a compatible output folder.");
            }
        }

        private static bool CanReference(in AssemblyTarget source, in AssemblyTarget target)
        {
            if (string.Equals(source.Name, target.Name, StringComparison.Ordinal))
            {
                return true;
            }

            if (source.IsPredefined)
            {
                return target.IsPredefined || target.AutoReferenced;
            }

            if (target.IsPredefined)
            {
                return false;
            }

            for (int i = 0; i < source.References.Length; i++)
            {
                string reference = source.References[i];
                if (string.Equals(reference, target.Name, StringComparison.Ordinal) ||
                    (!string.IsNullOrEmpty(target.Guid) &&
                     string.Equals(reference, "GUID:" + target.Guid, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveOutputAssembly(
            string outputFilePath,
            out AssemblyTarget target,
            out string error)
        {
            target = default;
            if (!UIWindowCreationValidator.TryValidateAssetFilePath(
                    outputFilePath,
                    ".cs",
                    out string canonicalPath,
                    out error))
            {
                return false;
            }

            string folder = Path.GetDirectoryName(canonicalPath)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder) &&
                   (string.Equals(folder, "Assets", StringComparison.Ordinal) ||
                    folder.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                if (!TryFindBoundaryFile(folder, out string boundaryPath, out error))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(boundaryPath))
                {
                    if (boundaryPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                    {
                        return TryReadAssemblyDefinition(boundaryPath, boundaryPath, out target, out error);
                    }

                    return TryResolveAssemblyReference(boundaryPath, out target, out error);
                }

                if (string.Equals(folder, "Assets", StringComparison.Ordinal))
                {
                    target = AssemblyTarget.Predefined;
                    error = string.Empty;
                    return true;
                }

                int separator = folder.LastIndexOf('/');
                folder = separator > 0 ? folder.Substring(0, separator) : "Assets";
            }

            error = $"Could not resolve an assembly boundary for '{canonicalPath}'.";
            return false;
        }

        private static bool TryFindBoundaryFile(
            string assetFolder,
            out string boundaryPath,
            out string error)
        {
            boundaryPath = string.Empty;
            error = string.Empty;
            string absoluteFolder = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                assetFolder));
            if (!Directory.Exists(absoluteFolder))
            {
                error = $"Output folder '{assetFolder}' does not exist.";
                return false;
            }

            string[] definitions = Directory.GetFiles(absoluteFolder, "*.asmdef", SearchOption.TopDirectoryOnly);
            string[] references = Directory.GetFiles(absoluteFolder, "*.asmref", SearchOption.TopDirectoryOnly);
            if (definitions.Length + references.Length > 1)
            {
                error = $"Folder '{assetFolder}' contains multiple asmdef/asmref boundaries.";
                return false;
            }

            if (definitions.Length == 1)
            {
                boundaryPath = assetFolder + "/" + Path.GetFileName(definitions[0]);
            }
            else if (references.Length == 1)
            {
                boundaryPath = assetFolder + "/" + Path.GetFileName(references[0]);
            }

            return true;
        }

        private static bool TryResolveAssemblyReference(
            string asmrefPath,
            out AssemblyTarget target,
            out string error)
        {
            target = default;
            if (!TryReadBoundedText(asmrefPath, out string json, out error))
            {
                return false;
            }

            AssemblyReferenceData referenceData = new AssemblyReferenceData();
            JsonUtility.FromJsonOverwrite(json, referenceData);
            string reference = referenceData.reference?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(reference))
            {
                error = $"Assembly reference '{asmrefPath}' has no target.";
                return false;
            }

            EnsureDefinitionIndex();
            if (reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
            {
                string guid = reference.Substring("GUID:".Length);
                if (!s_definitionsByGuid.TryGetValue(guid, out target))
                {
                    error = $"Assembly reference '{asmrefPath}' targets missing GUID '{guid}'.";
                    return false;
                }
            }
            else
            {
                if (!s_definitionsByName.TryGetValue(reference, out List<AssemblyTarget> matches) ||
                    matches.Count == 0)
                {
                    error = $"Assembly reference '{asmrefPath}' targets missing assembly '{reference}'.";
                    return false;
                }
                if (matches.Count != 1)
                {
                    error = $"Assembly reference '{asmrefPath}' targets ambiguous assembly name '{reference}'.";
                    return false;
                }

                target = matches[0];
            }

            target = new AssemblyTarget(
                target.Name,
                target.Guid,
                asmrefPath,
                target.DefinitionPath,
                target.References,
                target.IncludePlatforms,
                target.ExcludePlatforms,
                target.HasConditionalActivation,
                target.AutoReferenced,
                target.NoEngineReferences,
                false);
            error = string.Empty;
            return true;
        }

        private static bool TryResolveRuntimeAssembly(
            out AssemblyTarget runtimeAssembly,
            out string error)
        {
            EnsureDefinitionIndex();
            if (!s_definitionsByName.TryGetValue(RuntimeAssemblyName, out List<AssemblyTarget> matches) ||
                matches.Count == 0)
            {
                runtimeAssembly = default;
                error = $"Assembly '{RuntimeAssemblyName}' was not found in the current checkout.";
                return false;
            }
            if (matches.Count != 1)
            {
                runtimeAssembly = default;
                error = $"Assembly name '{RuntimeAssemblyName}' is duplicated in the current checkout.";
                return false;
            }

            runtimeAssembly = matches[0];
            error = string.Empty;
            return true;
        }

        private static void EnsureDefinitionIndex()
        {
            if (s_indexBuilt)
            {
                return;
            }

            s_definitionsByName.Clear();
            s_definitionsByGuid.Clear();
            string[] guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!TryReadAssemblyDefinition(assetPath, assetPath, out AssemblyTarget target, out _))
                {
                    continue;
                }

                if (!s_definitionsByName.TryGetValue(target.Name, out List<AssemblyTarget> matches))
                {
                    matches = new List<AssemblyTarget>(1);
                    s_definitionsByName.Add(target.Name, matches);
                }
                matches.Add(target);
                if (!string.IsNullOrEmpty(target.Guid))
                {
                    s_definitionsByGuid[target.Guid] = target;
                }
            }

            s_indexBuilt = true;
        }

        private static bool TryReadAssemblyDefinition(
            string asmdefPath,
            string boundaryPath,
            out AssemblyTarget target,
            out string error)
        {
            target = default;
            if (!TryReadBoundedText(asmdefPath, out string json, out error))
            {
                return false;
            }

            AssemblyDefinitionData data = new AssemblyDefinitionData();
            JsonUtility.FromJsonOverwrite(json, data);
            if (string.IsNullOrWhiteSpace(data.name))
            {
                error = $"Assembly definition '{asmdefPath}' has no name.";
                return false;
            }

            target = new AssemblyTarget(
                data.name.Trim(),
                AssetDatabase.AssetPathToGUID(asmdefPath),
                boundaryPath,
                asmdefPath,
                data.references,
                data.includePlatforms,
                data.excludePlatforms,
                data.defineConstraints != null && data.defineConstraints.Length > 0,
                data.autoReferenced,
                data.noEngineReferences,
                false);
            error = string.Empty;
            return true;
        }

        private static bool TryReadBoundedText(
            string assetPath,
            out string text,
            out string error)
        {
            text = string.Empty;
            error = string.Empty;
            if (!UIWindowCreationValidator.TryGetAbsoluteAssetPath(assetPath, out string absolutePath, out error))
            {
                return false;
            }

            FileInfo file = new FileInfo(absolutePath);
            if (!file.Exists)
            {
                error = $"Assembly boundary file '{assetPath}' does not exist.";
                return false;
            }
            if (file.Length > MaxAssemblyDefinitionBytes)
            {
                error = $"Assembly boundary file '{assetPath}' exceeds {MaxAssemblyDefinitionBytes} bytes.";
                return false;
            }

            try
            {
                text = File.ReadAllText(absolutePath);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not read assembly boundary '{assetPath}': {exception.Message}";
                return false;
            }
        }

        private static bool IsEditorOnly(string[] includePlatforms)
        {
            return includePlatforms != null &&
                   includePlatforms.Length == 1 &&
                   string.Equals(includePlatforms[0], "Editor", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CompilesInEditor(string[] includePlatforms, string[] excludePlatforms)
        {
            if (ContainsPlatform(excludePlatforms, "Editor"))
            {
                return false;
            }

            return includePlatforms == null ||
                   includePlatforms.Length == 0 ||
                   ContainsPlatform(includePlatforms, "Editor");
        }

        private static bool ContainsPlatform(string[] platforms, string value)
        {
            if (platforms == null)
            {
                return false;
            }

            for (int i = 0; i < platforms.Length; i++)
            {
                if (string.Equals(platforms[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsActiveInCurrentEditorCompilation(string assemblyName)
        {
            if (!s_activeEditorAssembliesBuilt)
            {
                s_activeEditorAssemblies.Clear();
                UnityEditor.Compilation.Assembly[] assemblies =
                    CompilationPipeline.GetAssemblies(AssembliesType.Editor);
                for (int i = 0; i < assemblies.Length; i++)
                {
                    if (!string.IsNullOrEmpty(assemblies[i].name))
                    {
                        s_activeEditorAssemblies.Add(assemblies[i].name);
                    }
                }

                s_activeEditorAssembliesBuilt = true;
            }

            return s_activeEditorAssemblies.Contains(assemblyName);
        }
    }
}
