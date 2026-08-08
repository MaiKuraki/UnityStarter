using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomPropertyDrawer(typeof(YooAssetPackageProfile))]
    internal sealed class YooAssetPackageProfileDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        private static readonly string[] OrderedFields =
        {
            "enabled",
            "packageName",
            "buildPipeline",
            "packageNote",
            "compression",
            "fileNameStyle",
            "cryptography",
            "bundledCopyOption",
            "bundledCopyTags",
            "useAssetDependencyDatabase",
            "enableSharePackRule",
            "verifyBuildingResult",
            "versionCollisionPolicy"
        };

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            foreach (string fieldName in OrderedFields)
            {
                SerializedProperty child = property.FindPropertyRelative(fieldName);
                if (child == null || !ShouldDraw(property, fieldName))
                {
                    continue;
                }

                height += Spacing + EditorGUI.GetPropertyHeight(child, includeChildren: true);
                if (fieldName == "cryptography")
                {
                    height += Spacing + GetCryptographyDiagnosticHeight(child);
                }
            }

            return height + Spacing;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty packageName = property.FindPropertyRelative("packageName");
            string summary = string.IsNullOrWhiteSpace(packageName?.stringValue)
                ? label.text
                : packageName.stringValue;
            Rect line = TakeLine(ref position, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(
                line,
                property.isExpanded,
                summary,
                toggleOnLabelClick: true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (string fieldName in OrderedFields)
                {
                    SerializedProperty child = property.FindPropertyRelative(fieldName);
                    if (child == null || !ShouldDraw(property, fieldName))
                    {
                        continue;
                    }

                    float childHeight = EditorGUI.GetPropertyHeight(child, includeChildren: true);
                    Rect childRect = TakeLine(ref position, childHeight);
                    if (fieldName == "packageName")
                    {
                        DrawPackageName(childRect, child);
                    }
                    else if (fieldName == "cryptography")
                    {
                        DrawCryptography(childRect, child);
                        Rect diagnosticRect = TakeLine(
                            ref position,
                            GetCryptographyDiagnosticHeight(child));
                        DrawCryptographyDiagnostic(diagnosticRect, child);
                    }
                    else
                    {
                        EditorGUI.PropertyField(childRect, child, includeChildren: true);
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        private static void DrawPackageName(Rect position, SerializedProperty property)
        {
            IReadOnlyList<string> packageNames =
                YooAssetPackageAuthoringCatalog.GetSnapshot().PackageNames;
            if (packageNames.Count == 0)
            {
                EditorGUI.PropertyField(position, property);
                return;
            }

            string current = property.stringValue ?? string.Empty;
            var values = new List<string>(packageNames);
            int selectedIndex = values.FindIndex(
                value => string.Equals(value, current, StringComparison.Ordinal));
            if (selectedIndex < 0)
            {
                values.Add(current);
                selectedIndex = values.Count - 1;
            }

            GUIContent[] labels = values
                .Select(value => new GUIContent(
                    string.IsNullOrEmpty(value)
                        ? "<Select a YooAsset package>"
                        : value == current && !packageNames.Contains(value)
                            ? $"Missing Package [{value}]"
                            : value))
                .ToArray();
            int newIndex = EditorGUI.Popup(
                position,
                new GUIContent(
                    "Package Name",
                    "Exact package from YooAsset Bundle Collector settings. CI uses this stable name."),
                selectedIndex,
                labels);
            if (newIndex >= 0 && newIndex < values.Count)
            {
                property.stringValue = values[newIndex];
            }
        }

        private static bool ShouldDraw(SerializedProperty profile, string fieldName)
        {
            if (fieldName == "compression")
            {
                return (YooAssetBuildPipelineKind)profile
                    .FindPropertyRelative("buildPipeline")
                    .intValue == YooAssetBuildPipelineKind.Scriptable;
            }

            if (fieldName != "bundledCopyTags")
            {
                return true;
            }

            var option = (YooAssetBundledCopyOption)profile
                .FindPropertyRelative("bundledCopyOption")
                .enumValueIndex;
            return option == YooAssetBundledCopyOption.ClearAndCopyByTags
                || option == YooAssetBundledCopyOption.OnlyCopyByTags;
        }

        private static void DrawCryptography(
            Rect position,
            SerializedProperty property)
        {
            EditorGUI.PropertyField(
                position,
                property,
                new GUIContent(
                    "Cryptography",
                    "Optional typed configuration asset. No class names, keys, or EditorPrefs values are entered here."));
        }

        private static float GetCryptographyDiagnosticHeight(SerializedProperty property)
        {
            YooAssetCryptographyAvailability availability =
                YooAssetCryptographyAuthoringCatalog.Inspect(
                    property.objectReferenceValue as YooAssetCryptographyConfiguration);
            return EditorStyles.helpBox.CalcHeight(
                new GUIContent(availability.Diagnostic),
                Math.Max(100f, EditorGUIUtility.currentViewWidth - 80f));
        }

        private static void DrawCryptographyDiagnostic(
            Rect position,
            SerializedProperty property)
        {
            YooAssetCryptographyAvailability availability =
                YooAssetCryptographyAuthoringCatalog.Inspect(
                    property.objectReferenceValue as YooAssetCryptographyConfiguration);
            MessageType type = availability.Status == YooAssetCryptographyAvailabilityStatus.Available
                || availability.Status == YooAssetCryptographyAvailabilityStatus.None
                    ? MessageType.Info
                    : MessageType.Error;
            EditorGUI.HelpBox(position, availability.Diagnostic, type);
        }

        private static Rect TakeLine(ref Rect remaining, float height)
        {
            Rect line = new Rect(remaining.x, remaining.y, remaining.width, height);
            remaining.y += height + Spacing;
            remaining.height = Math.Max(0f, remaining.height - height - Spacing);
            return line;
        }
    }

    internal enum YooAssetPackageCatalogStatus
    {
        PackageUnavailable,
        Ready,
        SettingsMissing,
        Invalid
    }

    internal sealed class YooAssetPackageCatalogSnapshot
    {
        internal YooAssetPackageCatalogSnapshot(
            YooAssetPackageCatalogStatus status,
            IReadOnlyList<string> packageNames,
            string diagnostic)
        {
            Status = status;
            PackageNames = packageNames ?? Array.Empty<string>();
            Diagnostic = diagnostic ?? string.Empty;
        }

        internal YooAssetPackageCatalogStatus Status { get; }
        internal IReadOnlyList<string> PackageNames { get; }
        internal string Diagnostic { get; }
    }

    internal static class YooAssetPackageAuthoringCatalog
    {
        private const int MaximumPackageCount = 1024;
        private static readonly object Gate = new object();

        private static double nextRefreshTime;
        private static YooAssetPackageCatalogSnapshot cachedSnapshot =
            new YooAssetPackageCatalogSnapshot(
                YooAssetPackageCatalogStatus.PackageUnavailable,
                Array.Empty<string>(),
                string.Empty);

        public static YooAssetPackageCatalogSnapshot GetSnapshot()
        {
            lock (Gate)
            {
                double now = EditorApplication.timeSinceStartup;
                if (now < nextRefreshTime)
                {
                    return cachedSnapshot;
                }

                nextRefreshTime = now + 1d;
                cachedSnapshot = ReadSnapshot();
                return cachedSnapshot;
            }
        }

        private static YooAssetPackageCatalogSnapshot ReadSnapshot()
        {
            try
            {
                Type dataType = ReflectionCache.GetType(
                    "YooAsset.Editor.BundleCollectorSettingData");
                if (dataType == null)
                {
                    return CreateSnapshot(YooAssetPackageCatalogStatus.PackageUnavailable);
                }

                MethodInfo hasSetting = dataType.GetMethod(
                    "HasSettingAsset",
                    BindingFlags.Public | BindingFlags.Static);
                if (hasSetting == null)
                {
                    return Invalid(
                        "Installed YooAsset does not expose BundleCollectorSettingData.HasSettingAsset. " +
                        "Install a supported YooAsset 3.x version or update the authoring integration.");
                }

                PropertyInfo settingProperty = dataType.GetProperty(
                    "Setting",
                    BindingFlags.Public | BindingFlags.Static);
                if (settingProperty == null)
                {
                    return Invalid(
                        "Installed YooAsset does not expose BundleCollectorSettingData.Setting. " +
                        "Install a supported YooAsset 3.x version or update the authoring integration.");
                }

                string[] settingAssetPaths = FindSettingAssetPaths(
                    settingProperty.PropertyType);
                YooAssetPackageCatalogStatus settingStatus =
                    ValidateSettingAssetCatalog(
                        settingAssetPaths,
                        out string settingDiagnostic);
                if (settingStatus != YooAssetPackageCatalogStatus.Ready)
                {
                    return CreateSnapshot(
                        settingStatus,
                        settingDiagnostic);
                }

                object hasSettingResult = hasSetting.Invoke(null, null);
                if (!(hasSettingResult is bool hasSettingAsset))
                {
                    return Invalid(
                        "YooAsset BundleCollectorSettingData.HasSettingAsset returned an incompatible value.");
                }

                if (!hasSettingAsset)
                {
                    return Invalid(
                        "AssetDatabase found one YooAsset Bundle Collector settings asset, " +
                        "but BundleCollectorSettingData.HasSettingAsset reported that none exists.");
                }

                object setting = settingProperty.GetValue(null);
                if (setting == null)
                {
                    return Invalid("YooAsset Bundle Collector settings could not be loaded.");
                }

                if (!(setting is UnityEngine.Object settingAsset))
                {
                    return Invalid(
                        "YooAsset Bundle Collector settings are not backed by a Unity asset.");
                }

                UnityEngine.Object discoveredSetting = AssetDatabase.LoadAssetAtPath(
                    settingAssetPaths[0],
                    settingProperty.PropertyType);
                if (discoveredSetting == null)
                {
                    return Invalid(
                        $"YooAsset Bundle Collector settings could not be loaded from '{settingAssetPaths[0]}'.");
                }

                string reflectedSettingPath = AssetDatabase.GetAssetPath(settingAsset)
                    ?.Replace('\\', '/');
                if (settingAsset != discoveredSetting
                    || !string.Equals(
                        settingAssetPaths[0],
                        reflectedSettingPath,
                        StringComparison.Ordinal))
                {
                    return Invalid(
                        "YooAsset BundleCollectorSettingData.Setting does not reference the unique " +
                        $"Bundle Collector settings asset at '{settingAssetPaths[0]}'. " +
                        "Reload the Editor after removing stale or duplicate settings assets.");
                }

                FieldInfo packagesField = setting.GetType().GetField(
                    "Packages",
                    BindingFlags.Public | BindingFlags.Instance);
                if (!(packagesField?.GetValue(setting) is IEnumerable packages))
                {
                    return Invalid(
                        "Installed YooAsset exposes an incompatible Bundle Collector package collection.");
                }

                var result = new List<string>();
                foreach (object package in packages)
                {
                    if (result.Count >= MaximumPackageCount)
                    {
                        return Invalid(
                            $"YooAsset Bundle Collector settings exceed the authoring limit of {MaximumPackageCount} packages.");
                    }

                    if (package == null)
                    {
                        return Invalid("YooAsset Bundle Collector settings contain a null package entry.");
                    }

                    PropertyInfo nameProperty = package.GetType().GetProperty(
                        "PackageName",
                        BindingFlags.Public | BindingFlags.Instance);
                    FieldInfo nameField = package.GetType().GetField(
                        "PackageName",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (nameProperty == null && nameField == null)
                    {
                        return Invalid(
                            "Installed YooAsset exposes an incompatible Bundle Collector package entry.");
                    }

                    string name = nameProperty?.GetValue(package) as string
                        ?? nameField?.GetValue(package) as string;
                    result.Add(name);
                }

                if (!TryValidatePackageNames(
                        result,
                        out string[] names,
                        out string diagnostic))
                {
                    return Invalid(diagnostic);
                }

                return CreateSnapshot(YooAssetPackageCatalogStatus.Ready, packageNames: names);
            }
            catch (Exception exception)
            {
                Exception cause = exception is TargetInvocationException invocation
                    && invocation.InnerException != null
                        ? invocation.InnerException
                        : exception;
                return Invalid(
                    "Failed to read YooAsset Bundle Collector settings: " + cause.Message);
            }
        }

        internal static YooAssetPackageCatalogStatus ValidateSettingAssetCatalog(
            IReadOnlyList<string> settingAssetPaths,
            out string diagnostic)
        {
            if (settingAssetPaths == null)
            {
                diagnostic = "YooAsset Bundle Collector settings discovery returned no result.";
                return YooAssetPackageCatalogStatus.Invalid;
            }

            if (settingAssetPaths.Count == 0)
            {
                diagnostic =
                    "YooAsset is installed, but no Bundle Collector settings asset exists. " +
                    "Create and configure it before selecting package names.";
                return YooAssetPackageCatalogStatus.SettingsMissing;
            }

            if (settingAssetPaths.Count != 1)
            {
                string paths = string.Join(", ", settingAssetPaths.Take(8));
                if (settingAssetPaths.Count > 8)
                {
                    paths += ", ...";
                }

                diagnostic =
                    "Exactly one YooAsset Bundle Collector settings asset is required. " +
                    $"Found {settingAssetPaths.Count}: {paths}";
                return YooAssetPackageCatalogStatus.Invalid;
            }

            diagnostic = string.Empty;
            return YooAssetPackageCatalogStatus.Ready;
        }

        internal static bool TryValidatePackageNames(
            IReadOnlyList<string> packageNames,
            out string[] validatedNames,
            out string diagnostic)
        {
            validatedNames = Array.Empty<string>();
            diagnostic = string.Empty;
            if (packageNames == null)
            {
                diagnostic = "YooAsset Bundle Collector package collection is missing.";
                return false;
            }

            if (packageNames.Count > MaximumPackageCount)
            {
                diagnostic =
                    $"YooAsset Bundle Collector settings exceed the authoring limit of {MaximumPackageCount} packages.";
                return false;
            }

            var exactNames = new HashSet<string>(StringComparer.Ordinal);
            var portableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(packageNames.Count);
            for (int index = 0; index < packageNames.Count; index++)
            {
                string packageName = packageNames[index];
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    diagnostic =
                        $"YooAsset Bundle Collector settings contain an empty package name at index {index}.";
                    return false;
                }

                if (!YooAssetBuildTokenPolicy.IsValidPackageName(packageName))
                {
                    diagnostic =
                        $"YooAsset Bundle Collector settings contain an invalid stable package name at index {index}.";
                    return false;
                }

                if (!exactNames.Add(packageName))
                {
                    diagnostic =
                        $"YooAsset Bundle Collector settings contain duplicate package name '{packageName}'.";
                    return false;
                }

                if (!portableNames.Add(packageName))
                {
                    diagnostic =
                        "YooAsset Bundle Collector settings contain package names that collide " +
                        $"when compared case-insensitively: '{packageName}'.";
                    return false;
                }

                result.Add(packageName);
            }

            result.Sort(StringComparer.Ordinal);
            validatedNames = result.ToArray();
            return true;
        }

        private static string[] FindSettingAssetPaths(Type settingType)
        {
            return AssetDatabase.FindAssets($"t:{settingType.Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static YooAssetPackageCatalogSnapshot Invalid(string diagnostic)
        {
            return CreateSnapshot(YooAssetPackageCatalogStatus.Invalid, diagnostic);
        }

        private static YooAssetPackageCatalogSnapshot CreateSnapshot(
            YooAssetPackageCatalogStatus status,
            string diagnostic = null,
            IReadOnlyList<string> packageNames = null)
        {
            return new YooAssetPackageCatalogSnapshot(
                status,
                packageNames ?? Array.Empty<string>(),
                diagnostic);
        }
    }
}
