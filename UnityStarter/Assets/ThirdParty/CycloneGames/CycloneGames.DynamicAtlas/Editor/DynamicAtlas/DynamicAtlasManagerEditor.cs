// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using CycloneGames.DynamicAtlas;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CycloneGames.DynamicAtlas.Editor
{
    [CustomEditor(typeof(DynamicAtlasManager), true)]
    [CanEditMultipleObjects]
    public sealed class DynamicAtlasManagerEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> KnownConfigurationFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "pageSize",
            "maxPages",
            "minRetainedPages",
            "maxEntries",
            "maxEntriesPerPage",
            "maxKeyLength",
            "maxMissesPerFrame",
            "memoryBudgetBytes",
            "padding",
            "enableBleed",
            "filterMode",
            "retentionPolicy",
            "oversizePolicy",
            "copyFallback",
            "defaultPixelsPerUnit",
        };

        private SerializedProperty _script;
        private SerializedProperty _autoInitialize;
        private SerializedProperty _configuration;
        private SerializedProperty _pageSize;
        private SerializedProperty _maxPages;
        private SerializedProperty _minRetainedPages;
        private SerializedProperty _maxEntries;
        private SerializedProperty _maxEntriesPerPage;
        private SerializedProperty _maxKeyLength;
        private SerializedProperty _memoryBudgetBytes;
        private SerializedProperty _padding;
        private SerializedProperty _enableBleed;
        private SerializedProperty _filterMode;
        private SerializedProperty _retentionPolicy;
        private SerializedProperty _oversizePolicy;
        private SerializedProperty _copyFallback;
        private SerializedProperty _defaultPixelsPerUnit;

        private bool _showOwnership = true;
        private bool _showProfiles = true;
        private bool _showCapacity = true;
        private bool _showPacking = true;
        private bool _showValidation = true;
        private bool _showRuntime = true;
        private bool _configurationLocked;
        private bool _validationDirty = true;
        private int _cachedInvalidCount;
        private int _cachedLoaderPairCount;
        private int _cachedMissingLoaderCount;
        private string _cachedFirstError;
        private double _nextRuntimeValidationRefresh;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _autoInitialize = serializedObject.FindProperty("autoInitialize");
            _configuration = serializedObject.FindProperty("configuration");
            _pageSize = FindConfigurationProperty("pageSize");
            _maxPages = FindConfigurationProperty("maxPages");
            _minRetainedPages = FindConfigurationProperty("minRetainedPages");
            _maxEntries = FindConfigurationProperty("maxEntries");
            _maxEntriesPerPage = FindConfigurationProperty("maxEntriesPerPage");
            _maxKeyLength = FindConfigurationProperty("maxKeyLength");
            _memoryBudgetBytes = FindConfigurationProperty("memoryBudgetBytes");
            _padding = FindConfigurationProperty("padding");
            _enableBleed = FindConfigurationProperty("enableBleed");
            _filterMode = FindConfigurationProperty("filterMode");
            _retentionPolicy = FindConfigurationProperty("retentionPolicy");
            _oversizePolicy = FindConfigurationProperty("oversizePolicy");
            _copyFallback = FindConfigurationProperty("copyFallback");
            _defaultPixelsPerUnit = FindConfigurationProperty("defaultPixelsPerUnit");
            Undo.undoRedoPerformed += MarkValidationDirty;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= MarkValidationDirty;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _configurationLocked = CalculateConfigurationLocked();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_script);
            }

            DynamicAtlasInspectorUiUtility.DrawInspectorTitle(
                "Dynamic Atlas Manager",
                "Bounded runtime page ownership, packing, and diagnostics",
                DynamicAtlasInspectorUiUtility.AssetColor);

            DrawOwnership();
            DrawStartingProfiles();
            DrawCapacity();
            DrawPackingAndPolicy();
            DrawUnhandledConfigurationProperties();

            if (serializedObject.ApplyModifiedProperties())
            {
                _validationDirty = true;
            }

            DrawValidation();
            DrawRuntimeStatus();
        }

        private void DrawOwnership()
        {
            string badge = _autoInitialize.hasMultipleDifferentValues
                ? "MIXED"
                : _autoInitialize.boolValue ? "OWNED" : "INJECTED";
            _showOwnership = DynamicAtlasInspectorUiUtility.DrawFoldoutHeader(
                "Initialization and Ownership",
                _showOwnership,
                DynamicAtlasInspectorUiUtility.SetupColor,
                badge,
                _autoInitialize.hasMultipleDifferentValues
                    ? DynamicAtlasInspectorUiUtility.NeutralColor
                    : DynamicAtlasInspectorUiUtility.SetupColor);
            if (!_showOwnership)
            {
                return;
            }

            DynamicAtlasInspectorUiUtility.BeginPanel();
            using (new EditorGUI.DisabledScope(IsConfigurationLocked()))
            {
                EditorGUILayout.PropertyField(_autoInitialize, new GUIContent(
                    "Auto Initialize",
                    "Creates and owns a DynamicAtlasService during Awake. Disable when a composition root injects the service."));
            }

            DrawConfigurationLockedMessage();
            EditorGUILayout.HelpBox(
                "Loader delegates are runtime dependencies and are not serialized. Configure both loadFunc and unloadFunc through DynamicAtlasConfig before initialization when TryAcquireLocation is used.",
                MessageType.None);
            DynamicAtlasInspectorUiUtility.EndPanel();
        }

        private void DrawStartingProfiles()
        {
            _showProfiles = DynamicAtlasInspectorUiUtility.DrawFoldoutHeader(
                "Starting Profiles",
                _showProfiles,
                DynamicAtlasInspectorUiUtility.AssetColor,
                "EXPLICIT",
                DynamicAtlasInspectorUiUtility.AssetColor);
            if (!_showProfiles)
            {
                return;
            }

            DynamicAtlasInspectorUiUtility.BeginPanel();
            EditorGUILayout.HelpBox(
                "Profiles set bounded authoring values. They do not identify the actual hardware tier and never enable synchronous GPU readback. Select a measured starting point, then validate content formats and budgets on target devices.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(IsConfigurationLocked()))
            {
                EditorGUILayout.BeginHorizontal();
                DrawProfileButton("Desktop High", DynamicAtlasConfig.PlatformTier.DesktopHighEnd);
                DrawProfileButton("Mobile High", DynamicAtlasConfig.PlatformTier.MobileHighEnd);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                DrawProfileButton("Mobile Low", DynamicAtlasConfig.PlatformTier.MobileLowEnd);
                DrawProfileButton("WebGL", DynamicAtlasConfig.PlatformTier.WebGL);
                EditorGUILayout.EndHorizontal();
            }

            DrawConfigurationLockedMessage();
            DynamicAtlasInspectorUiUtility.EndPanel();
        }

        private void DrawCapacity()
        {
            _showCapacity = DynamicAtlasInspectorUiUtility.DrawFoldoutHeader(
                "Capacity and Memory Budget",
                _showCapacity,
                DynamicAtlasInspectorUiUtility.AssetColor,
                _maxPages.hasMultipleDifferentValues ? "MIXED" : $"{_maxPages.intValue} PAGES",
                DynamicAtlasInspectorUiUtility.AssetColor);
            if (!_showCapacity)
            {
                return;
            }

            DynamicAtlasInspectorUiUtility.BeginPanel();
            using (new EditorGUI.DisabledScope(IsConfigurationLocked()))
            {
                EditorGUILayout.PropertyField(_pageSize);
                EditorGUILayout.PropertyField(_maxPages);
                EditorGUILayout.PropertyField(_minRetainedPages);
                EditorGUILayout.PropertyField(_maxEntries);
                EditorGUILayout.PropertyField(_maxEntriesPerPage);
                EditorGUILayout.PropertyField(_maxKeyLength);
                EditorGUILayout.PropertyField(_memoryBudgetBytes);
            }

            DrawConfigurationLockedMessage();
            DynamicAtlasInspectorUiUtility.EndPanel();
        }

        private void DrawPackingAndPolicy()
        {
            _showPacking = DynamicAtlasInspectorUiUtility.DrawFoldoutHeader(
                "Packing and Copy Policy",
                _showPacking,
                DynamicAtlasInspectorUiUtility.RuntimeColor,
                GetCopyPolicyBadge(),
                DynamicAtlasInspectorUiUtility.RuntimeColor);
            if (!_showPacking)
            {
                return;
            }

            DynamicAtlasInspectorUiUtility.BeginPanel();
            using (new EditorGUI.DisabledScope(IsConfigurationLocked()))
            {
                EditorGUILayout.PropertyField(_padding);
                EditorGUILayout.PropertyField(_enableBleed);
                EditorGUILayout.PropertyField(_filterMode);
                EditorGUILayout.PropertyField(_retentionPolicy);
                EditorGUILayout.PropertyField(_oversizePolicy);
                EditorGUILayout.PropertyField(_copyFallback);
                EditorGUILayout.PropertyField(_defaultPixelsPerUnit);
            }

            DrawConfigurationLockedMessage();
            EditorGUILayout.HelpBox(
                "Copy policy is a permission boundary. Actual GraphicsFormat compatibility and CopyTexture support are resolved when content is inserted on the running device.",
                MessageType.None);
            if (!_copyFallback.hasMultipleDifferentValues &&
                _copyFallback.intValue == (int)DynamicAtlasCopyFallback.AllowSynchronousReadback)
            {
                EditorGUILayout.HelpBox(
                    "Synchronous readback can stall the render thread and allocate temporary GPU/CPU resources. Enable it only for a measured content path with an insertion-time and peak-memory budget.",
                    MessageType.Warning);
            }
            DynamicAtlasInspectorUiUtility.EndPanel();
        }

        private void DrawUnhandledConfigurationProperties()
        {
            SerializedProperty iterator = _configuration.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            bool panelStarted = false;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (iterator.depth != _configuration.depth + 1 || KnownConfigurationFields.Contains(iterator.name))
                {
                    continue;
                }

                if (!panelStarted)
                {
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField("Additional Configuration", EditorStyles.boldLabel);
                    DynamicAtlasInspectorUiUtility.BeginPanel();
                    panelStarted = true;
                }

                using (new EditorGUI.DisabledScope(IsConfigurationLocked()))
                {
                    EditorGUILayout.PropertyField(iterator, includeChildren: true);
                }
            }

            if (panelStarted)
            {
                DynamicAtlasInspectorUiUtility.EndPanel();
            }
        }

        private void DrawValidation()
        {
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= _nextRuntimeValidationRefresh)
            {
                _validationDirty = true;
                _nextRuntimeValidationRefresh = EditorApplication.timeSinceStartup + 1d;
            }

            RefreshValidationCacheIfRequired();

            int invalidCount = _cachedInvalidCount;
            int loaderPairCount = _cachedLoaderPairCount;
            int missingLoaderCount = _cachedMissingLoaderCount;
            string firstError = _cachedFirstError;

            bool valid = invalidCount == 0;
            _showValidation = DynamicAtlasInspectorUiUtility.DrawFoldoutHeader(
                "Validation and Platform Context",
                _showValidation,
                DynamicAtlasInspectorUiUtility.RuntimeColor,
                valid ? "READY" : $"{invalidCount} INVALID",
                valid ? DynamicAtlasInspectorUiUtility.SuccessColor : DynamicAtlasInspectorUiUtility.WarningColor);
            if (!_showValidation)
            {
                return;
            }

            DynamicAtlasInspectorUiUtility.BeginPanel();
            DynamicAtlasInspectorUiUtility.DrawStatusRow(
                "Configuration",
                valid ? "Valid" : firstError ?? "Invalid",
                valid ? DynamicAtlasInspectorUiUtility.SuccessColor : DynamicAtlasInspectorUiUtility.WarningColor);

            if (loaderPairCount == targets.Length)
            {
                DynamicAtlasInspectorUiUtility.DrawStatusRow(
                    "Location loader ownership",
                    "Loader + unloader pair",
                    DynamicAtlasInspectorUiUtility.SuccessColor);
            }
            else if (missingLoaderCount == targets.Length)
            {
                DynamicAtlasInspectorUiUtility.DrawStatusRow(
                    "Location loader ownership",
                    "Not configured",
                    DynamicAtlasInspectorUiUtility.NeutralColor);
                EditorGUILayout.HelpBox(
                    "TryAcquireLocation returns LoaderUnavailable after a cache miss until an explicit loader and matching unloader are supplied. Direct Texture2D and Sprite acquisition remains available.",
                    MessageType.Info);
            }
            else
            {
                DynamicAtlasInspectorUiUtility.DrawStatusRow(
                    "Location loader ownership",
                    "Mixed or incomplete",
                    DynamicAtlasInspectorUiUtility.WarningColor);
            }

            DrawBudgetStatus();
            DynamicAtlasInspectorUiUtility.DrawStatusRow(
                "Active BuildTarget",
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                DynamicAtlasInspectorUiUtility.AssetColor);
            DynamicAtlasInspectorUiUtility.DrawStatusRow(
                "Current Editor graphics device",
                SystemInfo.graphicsDeviceType.ToString(),
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                    ? DynamicAtlasInspectorUiUtility.WarningColor
                    : DynamicAtlasInspectorUiUtility.NeutralColor);
            DynamicAtlasInspectorUiUtility.DrawStatusRow(
                "Current Editor CopyTexture",
                SystemInfo.copyTextureSupport.ToString(),
                (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0
                    ? DynamicAtlasInspectorUiUtility.SuccessColor
                    : DynamicAtlasInspectorUiUtility.WarningColor);
            EditorGUILayout.HelpBox(
                "Graphics capability rows describe the current Editor graphics device, not the selected Player target. Validate import candidates with the SpriteAtlas Compatibility Validator, then verify actual copy support, memory, batching, and stalls on each target device.",
                MessageType.None);
            DynamicAtlasInspectorUiUtility.EndPanel();
        }

        private void RefreshValidationCacheIfRequired()
        {
            if (!_validationDirty)
            {
                return;
            }

            _cachedInvalidCount = 0;
            _cachedLoaderPairCount = 0;
            _cachedMissingLoaderCount = 0;
            _cachedFirstError = null;

            for (int i = 0; i < targets.Length; i++)
            {
                DynamicAtlasManager manager = targets[i] as DynamicAtlasManager;
                if (manager == null)
                {
                    continue;
                }

                DynamicAtlasConfig config;
                try
                {
                    config = manager.Configuration;
                }
                catch (Exception exception)
                {
                    _cachedInvalidCount++;
                    if (_cachedFirstError == null)
                    {
                        _cachedFirstError = exception.Message;
                    }

                    continue;
                }

                if (!config.Validate(out string error))
                {
                    _cachedInvalidCount++;
                    if (_cachedFirstError == null)
                    {
                        _cachedFirstError = error;
                    }
                }

                if (config.loadFunc != null && config.unloadFunc != null)
                {
                    _cachedLoaderPairCount++;
                }
                else if (config.loadFunc == null && config.unloadFunc == null)
                {
                    _cachedMissingLoaderCount++;
                }
            }

            _validationDirty = false;
        }

        private void DrawBudgetStatus()
        {
            if (_pageSize.hasMultipleDifferentValues ||
                _maxPages.hasMultipleDifferentValues ||
                _memoryBudgetBytes.hasMultipleDifferentValues ||
                _copyFallback.hasMultipleDifferentValues)
            {
                DynamicAtlasInspectorUiUtility.DrawStatusRow(
                    "Texture budget",
                    "Mixed values",
                    DynamicAtlasInspectorUiUtility.NeutralColor);
                return;
            }

            long pageBytes = TextureFormatHelper.EstimatePageBytes(
                _pageSize.intValue,
                (DynamicAtlasCopyFallback)_copyFallback.intValue);
            long configuredBudget = _memoryBudgetBytes.longValue;
            long maximumConfiguredPages = SaturatingMultiply(pageBytes, _maxPages.intValue);
            bool onePageFits = pageBytes != long.MaxValue && configuredBudget >= pageBytes;

            DynamicAtlasInspectorUiUtility.DrawStatusRow(
                "Estimated bytes per page",
                pageBytes == long.MaxValue ? "Invalid" : EditorUtility.FormatBytes(pageBytes),
                onePageFits ? DynamicAtlasInspectorUiUtility.NeutralColor : DynamicAtlasInspectorUiUtility.WarningColor);
            DynamicAtlasInspectorUiUtility.DrawStatusRow(
                "Configured texture budget",
                EditorUtility.FormatBytes(Math.Max(0L, configuredBudget)),
                onePageFits ? DynamicAtlasInspectorUiUtility.SuccessColor : DynamicAtlasInspectorUiUtility.WarningColor);
            DynamicAtlasInspectorUiUtility.DrawStatusRow(
                "Max-page texture estimate",
                maximumConfiguredPages == long.MaxValue
                    ? "Overflow"
                    : EditorUtility.FormatBytes(maximumConfiguredPages),
                maximumConfiguredPages <= configuredBudget
                    ? DynamicAtlasInspectorUiUtility.SuccessColor
                    : DynamicAtlasInspectorUiUtility.NeutralColor);
            EditorGUILayout.HelpBox(
                "The texture estimate covers atlas page copies only. Source assets, Sprite objects, metadata, temporary scale/readback textures, allocator overhead, and delayed destruction must be budgeted separately.",
                MessageType.None);
        }

        private void DrawRuntimeStatus()
        {
            if (!EditorApplication.isPlaying || targets.Length != 1)
            {
                return;
            }

            DynamicAtlasManager manager = target as DynamicAtlasManager;
            if (manager == null || !manager.IsInitialized)
            {
                return;
            }

            _showRuntime = DynamicAtlasInspectorUiUtility.DrawFoldoutHeader(
                "Runtime Snapshot",
                _showRuntime,
                DynamicAtlasInspectorUiUtility.SuccessColor,
                "LIVE",
                DynamicAtlasInspectorUiUtility.SuccessColor);
            if (!_showRuntime)
            {
                return;
            }

            DynamicAtlasInspectorUiUtility.BeginPanel();
            try
            {
                DynamicAtlasStats stats = manager.GetStats();
                DynamicAtlasInspectorUiUtility.DrawStatusRow("Pages", stats.PageCount.ToString(), DynamicAtlasInspectorUiUtility.AssetColor);
                DynamicAtlasInspectorUiUtility.DrawStatusRow("Entries", stats.EntryCount.ToString(), DynamicAtlasInspectorUiUtility.AssetColor);
                DynamicAtlasInspectorUiUtility.DrawStatusRow(
                    "Active / retained",
                    $"{stats.ActiveEntryCount} / {stats.RetainedEntryCount}",
                    DynamicAtlasInspectorUiUtility.AssetColor);
                DynamicAtlasInspectorUiUtility.DrawStatusRow(
                    "Estimated texture bytes",
                    EditorUtility.FormatBytes(stats.EstimatedTextureBytes),
                    stats.EstimatedTextureBytes <= stats.MemoryBudgetBytes
                        ? DynamicAtlasInspectorUiUtility.SuccessColor
                        : DynamicAtlasInspectorUiUtility.WarningColor);
                DynamicAtlasInspectorUiUtility.DrawStatusRow(
                    "Pending destruction bytes",
                    EditorUtility.FormatBytes(stats.PendingDestructionBytes),
                    stats.PendingDestructionBytes == 0L
                        ? DynamicAtlasInspectorUiUtility.NeutralColor
                        : DynamicAtlasInspectorUiUtility.WarningColor);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Warning);
            }

            DynamicAtlasInspectorUiUtility.EndPanel();
        }

        private SerializedProperty FindConfigurationProperty(string propertyName)
        {
            return _configuration?.FindPropertyRelative(propertyName);
        }

        private void DrawProfileButton(string label, DynamicAtlasConfig.PlatformTier tier)
        {
            if (!GUILayout.Button(label, EditorStyles.miniButton))
            {
                return;
            }

            Undo.RecordObjects(targets, $"Apply Dynamic Atlas {label} Profile");
            ApplyStartingProfile(serializedObject, tier);
            _validationDirty = true;
        }

        internal static void ApplyStartingProfile(
            SerializedObject serializedTargets,
            DynamicAtlasConfig.PlatformTier tier)
        {
            if (serializedTargets == null)
            {
                throw new ArgumentNullException(nameof(serializedTargets));
            }

            DynamicAtlasConfig profile = DynamicAtlasConfig.CreateForTier(tier);
            serializedTargets.Update();
            SerializedProperty configuration = serializedTargets.FindProperty("configuration");
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    "DynamicAtlasManager serialized configuration was not found.");
            }

            configuration.FindPropertyRelative("pageSize").intValue = profile.pageSize;
            configuration.FindPropertyRelative("maxPages").intValue = profile.maxPages;
            configuration.FindPropertyRelative("minRetainedPages").intValue = profile.minRetainedPages;
            configuration.FindPropertyRelative("maxEntries").intValue = profile.maxEntries;
            configuration.FindPropertyRelative("maxEntriesPerPage").intValue = profile.maxEntriesPerPage;
            configuration.FindPropertyRelative("maxKeyLength").intValue = profile.maxKeyLength;
            configuration.FindPropertyRelative("maxMissesPerFrame").intValue = profile.maxMissesPerFrame;
            configuration.FindPropertyRelative("memoryBudgetBytes").longValue = profile.memoryBudgetBytes;
            configuration.FindPropertyRelative("padding").intValue = profile.padding;
            configuration.FindPropertyRelative("enableBleed").boolValue = profile.enableBleed;
            configuration.FindPropertyRelative("filterMode").intValue = (int)profile.filterMode;
            configuration.FindPropertyRelative("retentionPolicy").intValue = (int)profile.retentionPolicy;
            configuration.FindPropertyRelative("oversizePolicy").intValue = (int)profile.oversizePolicy;
            configuration.FindPropertyRelative("copyFallback").intValue = (int)profile.copyFallback;
            configuration.FindPropertyRelative("defaultPixelsPerUnit").floatValue = profile.defaultPixelsPerUnit;
            serializedTargets.ApplyModifiedProperties();
        }

        private string GetCopyPolicyBadge()
        {
            if (_copyFallback.hasMultipleDifferentValues)
            {
                return "MIXED";
            }

            int index = _copyFallback.enumValueIndex;
            string[] names = _copyFallback.enumDisplayNames;
            return index >= 0 && index < names.Length ? names[index] : "INVALID";
        }

        private bool IsConfigurationLocked()
        {
            return _configurationLocked;
        }

        private bool CalculateConfigurationLocked()
        {
            if (!EditorApplication.isPlaying)
            {
                return false;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is DynamicAtlasManager manager && manager.IsInitialized)
                {
                    return true;
                }
            }

            return false;
        }

        private void MarkValidationDirty()
        {
            _validationDirty = true;
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            _validationDirty = true;
            _nextRuntimeValidationRefresh = 0d;
            Repaint();
        }

        private void DrawConfigurationLockedMessage()
        {
            if (IsConfigurationLocked())
            {
                EditorGUILayout.HelpBox(
                    "Configuration is immutable after service initialization. Stop Play Mode or inject a replacement service at the composition boundary.",
                    MessageType.Info);
            }
        }

        private static long SaturatingMultiply(long value, int multiplier)
        {
            if (value < 0L || multiplier < 0 || value == long.MaxValue)
            {
                return long.MaxValue;
            }

            try
            {
                return checked(value * multiplier);
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }
    }
}
