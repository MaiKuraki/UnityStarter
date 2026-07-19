using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CycloneGames.IO;
using CycloneGames.Logger;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Editor
{
    internal sealed class UIWindowCreatorAssetCommitException : IOException
    {
        public string[] ResidualPaths { get; }

        public UIWindowCreatorAssetCommitException(
            string message,
            Exception innerException,
            params string[] residualPaths)
            : base(message, innerException)
        {
            ResidualPaths = residualPaths ?? Array.Empty<string>();
        }
    }

    [Serializable]
    internal sealed class UIWindowCreatorSettings
    {
        public int schemaVersion = 1;
        public string scriptFolderPath = string.Empty;
        public string prefabFolderPath = string.Empty;
        public string configFolderPath = string.Empty;
        public string presenterFolderPath = string.Empty;
        public string namespaceName = string.Empty;
        public bool useMvp;
        public int configSourceMode;
        public bool autoFillLocationFromPrefabPath = true;
        public string runtimeLocation = string.Empty;
        public bool hasTemplateSelection;
        public string templatePrefabPath = string.Empty;
    }

    public sealed class UIWindowCreatorWindow : EditorWindow
    {
        private enum PipelineStatus
        {
            Ready,
            NeedsInput,
            Invalid,
            Conflict
        }

        private enum FeedbackKind
        {
            None,
            Success,
            Pending,
            Failure
        }

        private sealed class CreatorSnapshot
        {
            public UIWindowCreationRequest Request;
            public UIWindowCreationPaths Paths;
            public bool HasPaths;
            public bool CanCreate;
            public bool TemplateValid;
            public PipelineStatus Status;
            public string ValidationMessage = string.Empty;
            public string[] ExistingFiles = Array.Empty<string>();
        }

        internal struct TemplateInspection
        {
            public bool IsPrefab;
            public bool HasRootRectTransform;
            public bool HasCanvasGroup;
            public bool HasWindowComponent;
            public bool HasRootWindowComponent;
            public int WindowComponentCount;
            public int MissingScriptCount;
            public int ObjectCount;
            public int GraphicCount;
            public int SelectableCount;
            public int LayoutGroupCount;
            public int ContentSizeFitterCount;
            public int MaskCount;
            public int CanvasCount;
            public int TmpTextCount;

            public bool IsValid =>
                IsPrefab &&
                HasRootRectTransform &&
                MissingScriptCount == 0 &&
                WindowComponentCount <= 1 &&
                (WindowComponentCount == 0 || HasRootWindowComponent);
        }

        internal readonly struct RollbackResult
        {
            public readonly int AttemptedCount;
            public readonly string[] Failures;
            public readonly string[] ResidualPaths;

            public RollbackResult(
                int attemptedCount,
                string[] failures,
                string[] residualPaths)
            {
                AttemptedCount = attemptedCount;
                Failures = failures ?? Array.Empty<string>();
                ResidualPaths = residualPaths ?? Array.Empty<string>();
            }

            public bool IsComplete =>
                (Failures == null || Failures.Length == 0) &&
                (ResidualPaths == null || ResidualPaths.Length == 0);
        }

        internal sealed class CreatedAssetRecord
        {
            public string AssetPath { get; }
            public string ExpectedGuid { get; }

            internal CreatedAssetRecord(
                string assetPath,
                string expectedGuid)
            {
                AssetPath = assetPath ?? string.Empty;
                ExpectedGuid = expectedGuid ?? string.Empty;
            }

            internal bool TryVerifyOwnership(
                out string absolutePath,
                out bool alreadyAbsent,
                out string error)
            {
                alreadyAbsent = false;
                if (!UIWindowCreationValidator.TryGetAbsoluteAssetPath(
                        AssetPath,
                        out absolutePath,
                        out error))
                {
                    return false;
                }

                bool assetExists = File.Exists(absolutePath);
                bool metaExists = File.Exists(absolutePath + ".meta");
                if (!assetExists && !metaExists)
                {
                    if (string.IsNullOrEmpty(ExpectedGuid) ||
                        string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(ExpectedGuid)))
                    {
                        alreadyAbsent = true;
                        error = string.Empty;
                        return true;
                    }
                }

                if (string.IsNullOrEmpty(ExpectedGuid))
                {
                    error = $"Rollback record for '{AssetPath}' has no GUID identity.";
                    return false;
                }

                string currentGuid = AssetDatabase.AssetPathToGUID(AssetPath);
                string resolvedPath = AssetDatabase.GUIDToAssetPath(ExpectedGuid);
                if (!string.Equals(currentGuid, ExpectedGuid, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(resolvedPath, AssetPath, StringComparison.Ordinal))
                {
                    error =
                        $"Ownership changed for '{AssetPath}'. Expected GUID '{ExpectedGuid}', " +
                        $"current GUID='{currentGuid}', current path='{resolvedPath}'.";
                    return false;
                }

                error = string.Empty;
                return true;
            }
        }

        private const string LogCategory = "UIWindowCreator";
        private const string DefaultTemplateGuid = "37c32b368ca8d4841b923d1b37cf97b9";
        private const string SettingsFileName = "CycloneGames.UIFramework.WindowCreator.json";
        private const int MaxSettingsBytes = 1024 * 1024;
        private const float StatusBadgeWidth = 104f;
        private const float RequirementBadgeWidth = 72f;
        private const float BadgeSpacing = 6f;
        private const float PreviewLabelWidth = 126f;

        private static readonly Color IdentityColor = new Color(0.30f, 0.45f, 0.72f);
        private static readonly Color OutputColor = new Color(0.25f, 0.57f, 0.50f);
        private static readonly Color RuntimeColor = new Color(0.50f, 0.40f, 0.70f);
        private static readonly Color MvpColor = new Color(0.65f, 0.46f, 0.27f);
        private static readonly Color TemplateColor = new Color(0.38f, 0.55f, 0.39f);
        private static readonly Color ReviewColor = new Color(0.28f, 0.60f, 0.36f);
        private static readonly Color ReadyColor = new Color(0.20f, 0.60f, 0.32f);
        private static readonly Color MissingColor = new Color(0.72f, 0.36f, 0.18f);
        private static readonly Color InvalidColor = new Color(0.72f, 0.22f, 0.20f);
        private static readonly Color OptionalColor = new Color(0.42f, 0.42f, 0.42f);
        private static readonly Color RequiredColor = new Color(0.28f, 0.52f, 0.82f);
        private static readonly Color PendingColor = new Color(0.72f, 0.52f, 0.17f);

        private static GUIStyle _heroTitleStyle;
        private static GUIStyle _heroSubtitleStyle;
        private static GUIStyle _sectionTitleStyle;
        private static GUIStyle _cardTitleStyle;
        private static GUIStyle _helpStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _previewKeyStyle;
        private static GUIStyle _previewValueStyle;
        private static GUIStyle _createTitleStyle;
        private static GUIStyle _createSubtitleStyle;
        private static GUIStyle _alertTitleStyle;
        private static GUIStyle _alertBodyStyle;
        private static bool _stylesInitialized;

        private readonly List<string> _validationErrors = new List<string>(8);
        private readonly List<string> _existingFiles = new List<string>(8);
        private readonly UIWindowTemplateProcessor _templateProcessor = new UIWindowTemplateProcessor();

        private string _windowName = string.Empty;
        private string _namespaceName = string.Empty;
        private DefaultAsset _scriptFolder;
        private DefaultAsset _prefabFolder;
        private DefaultAsset _configFolder;
        private DefaultAsset _presenterFolder;
        private UILayerConfiguration _layer;
        private GameObject _templatePrefab;
        private bool _templateSelectionExplicit;
        private bool _useMvp;
        private UIWindowConfiguration.PrefabSource _sourceMode =
            UIWindowConfiguration.PrefabSource.PrefabReference;
        private bool _autoFillPathLocation = true;
        private string _runtimeLocation = string.Empty;
        private Vector2 _scroll;
        private string _settingsPath;
        private bool _snapshotDirty = true;
        private CreatorSnapshot _snapshot;
        private bool _templateInspectionDirty = true;
        private TemplateInspection _templateInspection;
        private FeedbackKind _feedbackKind;
        private string _feedbackTitle = string.Empty;
        private string _feedbackMessage = string.Empty;

        [MenuItem("Tools/CycloneGames/UI Framework/Window Creator")]
        public static void ShowWindow()
        {
            UIWindowCreatorWindow window = GetWindow<UIWindowCreatorWindow>("UI Window Creator");
            window.minSize = new Vector2(520f, 680f);
            window.Show();
        }

        private void OnEnable()
        {
            UIWindowAssemblyValidator.InvalidateCache();
            _settingsPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "UserSettings",
                SettingsFileName));

            LoadSettings();
            if (!_templateSelectionExplicit)
            {
                UseDefaultTemplate(false);
            }

            EditorApplication.projectChanged += OnProjectChanged;
            UIWindowCreatorPostCompileProcessor.StatusChanged += OnPostCompileStatusChanged;
            MarkSnapshotDirty();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            UIWindowCreatorPostCompileProcessor.StatusChanged -= OnPostCompileStatusChanged;
            SaveSettings();
        }

        private void OnProjectChanged()
        {
            UIWindowAssemblyValidator.InvalidateCache();
            MarkSnapshotDirty();
            _templateInspectionDirty = true;
            Repaint();
        }

        private void OnPostCompileStatusChanged()
        {
            Repaint();
        }

        private void OnGUI()
        {
            InitializeStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawHero();
            DrawPipelineSummary(GetSnapshot());
            DrawIdentitySection();
            DrawOutputSection();
            DrawRuntimeSection();
            DrawMvpSection();
            DrawTemplateSection();
            DrawReviewSection(GetSnapshot());
            DrawFeedback();
            EditorGUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        private static void InitializeStyles()
        {
            if (_stylesInitialized)
            {
                return;
            }

            _stylesInitialized = true;
            _heroTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft
            };
            _heroSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
            _cardTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };
            _helpStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };
            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _previewKeyStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            _previewValueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft
            };
            _createTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _createSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.86f, 0.96f, 0.88f) }
            };
            _alertTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 0.88f, 0.84f)
                        : new Color(0.42f, 0.06f, 0.06f)
                }
            };
            _alertBodyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.94f, 0.84f, 0.82f)
                        : new Color(0.36f, 0.08f, 0.08f)
                }
            };
        }

        private void DrawHero()
        {
            EditorGUILayout.Space(8f);
            Rect rect = EditorGUILayout.GetControlRect(false, 68f);
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.18f, 0.21f)
                : new Color(0.82f, 0.86f, 0.91f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 5f, rect.height), IdentityColor);
            EditorGUI.LabelField(
                new Rect(rect.x + 16f, rect.y + 8f, rect.width - 32f, 24f),
                "UIWindow Creator",
                _heroTitleStyle);
            EditorGUI.LabelField(
                new Rect(rect.x + 16f, rect.y + 34f, rect.width - 32f, 28f),
                "Generate a production-ready window, prefab, configuration, and optional MVP types through a validated, rollback-capable pipeline.",
                _heroSubtitleStyle);
            EditorGUILayout.Space(8f);
        }

        private void DrawPipelineSummary(CreatorSnapshot snapshot)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader(
                "Pipeline Status",
                GetPipelineStatusLabel(snapshot.Status),
                GetPipelineStatusColor(snapshot.Status));

            bool nameValid = UIWindowCreationValidator.IsValidCSharpIdentifier(snapshot.Request.WindowName);
            DrawStatusRow(
                "Window",
                string.IsNullOrEmpty(snapshot.Request.WindowName)
                    ? "Enter a window class and stable ID"
                    : snapshot.Request.WindowName,
                nameValid ? "OK" : string.IsNullOrEmpty(snapshot.Request.WindowName) ? "Need" : "Fix",
                nameValid ? ReadyColor : string.IsNullOrEmpty(snapshot.Request.WindowName) ? MissingColor : InvalidColor);
            DrawStatusRow(
                "Folders",
                AreRequiredFoldersReady() ? GetFolderSummary() : "Select valid Project folders",
                AreRequiredFoldersReady());
            DrawStatusRow(
                "Layer",
                _layer != null ? _layer.LayerName : "Select UILayerConfiguration",
                _layer != null);
            DrawStatusRow(
                "Source",
                GetSourceModeSummary(snapshot),
                IsSourceReady(snapshot.Request));
            DrawStatusRow(
                "Template",
                GetTemplateSummary(),
                snapshot.TemplateValid ? "OK" : "Fix",
                snapshot.TemplateValid ? ReadyColor : InvalidColor);
            DrawStatusRow(
                "Outputs",
                snapshot.ExistingFiles.Length == 0
                    ? snapshot.HasPaths ? "No generated asset conflicts" : "Waiting for output paths"
                    : $"{snapshot.ExistingFiles.Length} generated asset conflict(s)",
                snapshot.ExistingFiles.Length == 0 ? snapshot.HasPaths ? "OK" : "Wait" : "Conflict",
                snapshot.ExistingFiles.Length == 0 ? snapshot.HasPaths ? ReadyColor : OptionalColor : InvalidColor);
            DrawStatusRow(
                "Composition",
                _useMvp ? "Window + typed View + Presenter" : "Window only (DI remains optional)",
                "OK",
                ReadyColor);

            DrawPostCompileSummary();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawIdentitySection()
        {
            DrawSectionHeader(
                "Identity",
                "Define the runtime window ID, generated class name, and optional namespace.",
                IdentityColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string trimmedName = _windowName != null ? _windowName.Trim() : string.Empty;
            bool nameValid = UIWindowCreationValidator.IsValidCSharpIdentifier(trimmedName);
            DrawCardHeader(
                "Window Class / ID",
                string.IsNullOrEmpty(trimmedName) ? "Missing" : nameValid ? "Ready" : "Invalid",
                string.IsNullOrEmpty(trimmedName) ? MissingColor : nameValid ? ReadyColor : InvalidColor,
                true);
            EditorGUILayout.LabelField(
                "This value is used as the generated C# type, prefab name, configuration ID, and default runtime lookup key.",
                _helpStyle);
            string newName = EditorGUILayout.TextField(
                new GUIContent("Name", "Use a stable C# identifier such as InventoryWindow."),
                _windowName);
            if (!string.Equals(newName, _windowName, StringComparison.Ordinal))
            {
                _windowName = newName;
                MarkSnapshotDirty();
            }

            if (!string.IsNullOrEmpty(trimmedName))
            {
                DrawPreviewRow("Script", trimmedName + ".cs");
                DrawPreviewRow("Prefab", trimmedName + ".prefab");
                DrawPreviewRow("Configuration", trimmedName + "_Config.asset");
            }

            if (!string.IsNullOrEmpty(trimmedName) && !nameValid)
            {
                DrawAlert(
                    "Invalid window name",
                    "Use a C# identifier that starts with a letter or underscore and contains only letters, digits, or underscores. Reserved keywords are not accepted.");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string trimmedNamespace = _namespaceName != null ? _namespaceName.Trim() : string.Empty;
            bool namespaceValid = UIWindowCreationValidator.IsValidNamespace(trimmedNamespace);
            DrawCardHeader(
                "Namespace",
                string.IsNullOrEmpty(trimmedNamespace) ? "Optional" : namespaceValid ? "Ready" : "Invalid",
                string.IsNullOrEmpty(trimmedNamespace) ? OptionalColor : namespaceValid ? ReadyColor : InvalidColor,
                false);
            EditorGUILayout.LabelField(
                "Leave empty for the global namespace, or use dot-separated C# identifiers such as MyGame.UI.Windows.",
                _helpStyle);
            string newNamespace = EditorGUILayout.TextField("Namespace", _namespaceName);
            if (!string.Equals(newNamespace, _namespaceName, StringComparison.Ordinal))
            {
                _namespaceName = newNamespace;
                MarkSnapshotDirty();
            }

            if (!string.IsNullOrEmpty(trimmedNamespace))
            {
                DrawPreviewRow("Generated type", trimmedNamespace + "." + GetSafeWindowName("WindowName"));
            }
            if (!namespaceValid)
            {
                DrawAlert(
                    "Invalid namespace",
                    "Each namespace segment must be a valid non-keyword C# identifier.");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawOutputSection()
        {
            DrawSectionHeader(
                "Output Paths",
                "Choose explicit Project folders. Every generated path is previewed before creation.",
                OutputColor);

            DrawFolderCard(
                "Script Folder",
                "Generated UIWindow script and, when enabled, the typed View interface.",
                ref _scriptFolder,
                GetSafeWindowName("WindowName") + ".cs",
                true);
            DrawFolderCard(
                "Prefab Folder",
                "Generated, unpacked UIWindow prefab.",
                ref _prefabFolder,
                GetSafeWindowName("WindowName") + ".prefab",
                true);
            DrawFolderCard(
                "Configuration Folder",
                "Generated UIWindowConfiguration asset. The suffix keeps its location distinct from the prefab.",
                ref _configFolder,
                GetSafeWindowName("WindowName") + "_Config.asset",
                true);

            EditorGUILayout.HelpBox(
                "The creator never overwrites an existing output. Conflicts are listed in the review section and must be resolved explicitly.",
                MessageType.Info);
            EditorGUILayout.Space(8f);
        }

        private void DrawRuntimeSection()
        {
            DrawSectionHeader(
                "Runtime Configuration",
                "Choose the destination layer and the provider-neutral prefab reference contract.",
                RuntimeColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader(
                "UILayer Configuration",
                _layer != null ? "Ready" : "Missing",
                _layer != null ? ReadyColor : MissingColor,
                true);
            EditorGUILayout.LabelField(
                "The selected layer controls where UIService attaches the window and participates in layer ordering.",
                _helpStyle);
            UILayerConfiguration newLayer = (UILayerConfiguration)EditorGUILayout.ObjectField(
                "Layer",
                _layer,
                typeof(UILayerConfiguration),
                false);
            if (newLayer != _layer)
            {
                _layer = newLayer;
                MarkSnapshotDirty(true);
            }
            if (_layer != null)
            {
                DrawPreviewRow("Layer", _layer.LayerName);
                DrawPreviewRow("Asset", AssetDatabase.GetAssetPath(_layer));
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("Prefab Source", _sourceMode.ToString(), ReadyColor, true);
            EditorGUILayout.LabelField(
                "Select how UIWindowConfiguration resolves the generated prefab. Source selection does not add a dependency on a specific asset package.",
                _helpStyle);
            UIWindowConfiguration.PrefabSource newSource =
                (UIWindowConfiguration.PrefabSource)EditorGUILayout.EnumPopup("Source", _sourceMode);
            if (newSource != _sourceMode)
            {
                _sourceMode = newSource;
                MarkSnapshotDirty(true);
            }

            switch (_sourceMode)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    EditorGUILayout.HelpBox(
                        "Stores a direct prefab reference. This is the simplest option for built-in UI and tests. The prefab remains a serialized dependency of the configuration asset.",
                        MessageType.None);
                    DrawPreviewRow("Runtime value", "Direct generated prefab reference");
                    break;

                case UIWindowConfiguration.PrefabSource.AssetReference:
                    EditorGUILayout.HelpBox(
                        "Stores an explicit provider location plus the Editor GUID. Use the exact address expected by the configured IUIWindowAssetProvider.",
                        MessageType.None);
                    DrawRuntimeLocationField("Provider Location", "Provider-specific location or address. This field is required.");
                    DrawPreviewRow("Editor metadata", "Generated prefab GUID");
                    break;

                case UIWindowConfiguration.PrefabSource.PathLocation:
                    bool newAutoFill = EditorGUILayout.ToggleLeft(
                        new GUIContent(
                            "Use generated Unity project asset path",
                            "Enable only when the runtime provider intentionally resolves Assets/... project paths."),
                        _autoFillPathLocation);
                    if (newAutoFill != _autoFillPathLocation)
                    {
                        _autoFillPathLocation = newAutoFill;
                        MarkSnapshotDirty(true);
                    }
                    if (_autoFillPathLocation)
                    {
                        CreatorSnapshot snapshot = GetSnapshot();
                        DrawPreviewRow(
                            "Runtime location",
                            snapshot.HasPaths ? snapshot.Paths.PrefabFilePath : "Generated prefab project path");
                    }
                    else
                    {
                        DrawRuntimeLocationField("Runtime Location", "Location passed unchanged to IUIWindowAssetProvider.");
                    }
                    EditorGUILayout.HelpBox(
                        "PathLocation is intended for providers with an explicit path contract. Do not assume every provider accepts Unity project asset paths.",
                        MessageType.None);
                    break;
            }

            DrawPreviewRow("Contract", GetSourceModeSummary(GetSnapshot()));
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawRuntimeLocationField(string label, string tooltip)
        {
            string newLocation = EditorGUILayout.TextField(
                new GUIContent(label, tooltip),
                _runtimeLocation);
            if (!string.Equals(newLocation, _runtimeLocation, StringComparison.Ordinal))
            {
                _runtimeLocation = newLocation;
                MarkSnapshotDirty();
            }

            string trimmed = _runtimeLocation != null ? _runtimeLocation.Trim() : string.Empty;
            if (string.IsNullOrEmpty(trimmed))
            {
                DrawAlert("Runtime location required", "Enter the exact location understood by the configured window asset provider.");
            }
            else
            {
                DrawPreviewRow("Runtime location", trimmed);
            }
        }

        private void DrawMvpSection()
        {
            DrawSectionHeader(
                "Composition",
                "Generate a minimal UIWindow, or add typed MVP artifacts without binding the module to a DI container.",
                MvpColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader(
                "MVP Structure",
                _useMvp ? "Enabled" : "Optional",
                _useMvp ? ReadyColor : OptionalColor,
                false);
            EditorGUILayout.LabelField(
                "MVP adds an IView interface and Presenter. DI remains optional: register the Presenter in the composition root with UIPresenterBinder or a DI integration.",
                _helpStyle);
            bool newUseMvp = EditorGUILayout.ToggleLeft("Generate typed View and Presenter", _useMvp);
            if (newUseMvp != _useMvp)
            {
                _useMvp = newUseMvp;
                MarkSnapshotDirty(true);
            }

            DrawPreviewRow("Window", "Generated in every mode");
            DrawPreviewRow("DI", "Optional in every mode");
            if (_useMvp)
            {
                DrawPreviewRow("View", "I" + GetSafeWindowName("WindowName") + "View.cs");
                DrawPreviewRow("Presenter", GetSafeWindowName("WindowName") + "Presenter.cs");
            }
            EditorGUILayout.EndVertical();

            if (_useMvp)
            {
                EditorGUILayout.Space(6f);
                DrawFolderCard(
                    "Presenter Folder",
                    "Generated Presenter script. This may be the same folder as the window script.",
                    ref _presenterFolder,
                    GetSafeWindowName("WindowName") + "Presenter.cs",
                    true);
            }
            EditorGUILayout.Space(8f);
        }

        private void DrawTemplateSection()
        {
            DrawSectionHeader(
                "Prefab Template",
                "Clone a project-specific visual template or create a minimal full-screen RectTransform root.",
                TemplateColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            TemplateInspection inspection = GetTemplateInspection();
            DrawCardHeader(
                "Template Prefab",
                _templatePrefab == null ? "Minimal" : inspection.IsValid ? "Ready" : "Invalid",
                _templatePrefab == null ? OptionalColor : inspection.IsValid ? ReadyColor : InvalidColor,
                false);
            EditorGUILayout.LabelField(
                "The selected prefab is cloned and unpacked. Its root UIWindow component is replaced, and a compatible TMP title is updated when present.",
                _helpStyle);

            EditorGUILayout.BeginHorizontal();
            GameObject newTemplate = (GameObject)EditorGUILayout.ObjectField(
                "Template",
                _templatePrefab,
                typeof(GameObject),
                false);
            if (newTemplate != _templatePrefab)
            {
                SetTemplate(newTemplate, true);
            }
            Texture thumbnail = _templatePrefab != null ? AssetPreview.GetMiniThumbnail(_templatePrefab) : null;
            if (thumbnail != null)
            {
                GUILayout.Label(thumbnail, GUILayout.Width(36f), GUILayout.Height(36f));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Package Default"))
            {
                UseDefaultTemplate(true);
            }
            using (new EditorGUI.DisabledScope(_templatePrefab == null))
            {
                if (GUILayout.Button("Ping"))
                {
                    EditorGUIUtility.PingObject(_templatePrefab);
                }
                if (GUILayout.Button("Open Prefab"))
                {
                    AssetDatabase.OpenAsset(_templatePrefab);
                }
                if (GUILayout.Button("Use Minimal"))
                {
                    SetTemplate(null, true);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_templatePrefab == null)
            {
                EditorGUILayout.HelpBox(
                    "A full-screen RectTransform with CanvasGroup and UIWindow placeholder will be created.",
                    MessageType.None);
            }
            else
            {
                DrawPreviewRow("Asset", AssetDatabase.GetAssetPath(_templatePrefab));
                DrawPreviewRow("Generated title", UIWindowTitleFormatter.BuildTemplateTitleText(GetSafeWindowName("Window Name")));
                DrawPreviewRow("Objects / Graphics", inspection.ObjectCount + " / " + inspection.GraphicCount);
                DrawPreviewRow("Selectables / Canvases", inspection.SelectableCount + " / " + inspection.CanvasCount);
                DrawPreviewRow("Layout / Fitters", inspection.LayoutGroupCount + " / " + inspection.ContentSizeFitterCount);
                DrawPreviewRow("Masks / TMP text", inspection.MaskCount + " / " + inspection.TmpTextCount);
                DrawPreviewRow("UIWindow / Missing scripts", inspection.WindowComponentCount + " / " + inspection.MissingScriptCount);

                if (!inspection.IsPrefab)
                {
                    DrawAlert("Invalid template", "Select a prefab asset from the Project window.");
                }
                else if (!inspection.HasRootRectTransform)
                {
                    DrawAlert("Invalid UI root", "The template root must use RectTransform so the generated asset can participate in the UI hierarchy.");
                }
                else if (inspection.MissingScriptCount > 0)
                {
                    DrawAlert(
                        "Missing scripts",
                        $"The template contains {inspection.MissingScriptCount} missing MonoBehaviour reference(s). Repair or remove them before generation.");
                }
                else if (inspection.WindowComponentCount > 1 ||
                         (inspection.WindowComponentCount == 1 && !inspection.HasRootWindowComponent))
                {
                    DrawAlert(
                        "Invalid UIWindow placement",
                        "A template may contain at most one UIWindow placeholder, and it must be attached to the prefab root.");
                }
                else
                {
                    if (!inspection.HasCanvasGroup)
                    {
                        EditorGUILayout.HelpBox(
                            "The template has no root CanvasGroup. The creator will add one before saving the generated prefab.",
                            MessageType.Info);
                    }
                    if (inspection.HasWindowComponent)
                    {
                        EditorGUILayout.HelpBox(
                            "The template UIWindow component is authoring-only and will be replaced by the generated window component.",
                            MessageType.None);
                    }
                    if (inspection.TmpTextCount == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "No TMP text was detected. Template title substitution will be skipped.",
                            MessageType.None);
                    }
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawReviewSection(CreatorSnapshot snapshot)
        {
            DrawSectionHeader(
                "Review & Create",
                "Inspect the exact output set and validation state before the transaction starts.",
                ReviewColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader(
                "Creation Transaction",
                snapshot.CanCreate ? "Ready" : "Blocked",
                snapshot.CanCreate ? ReadyColor : InvalidColor);

            if (snapshot.HasPaths)
            {
                DrawPreviewRow("Script", snapshot.Paths.ScriptFilePath);
                DrawPreviewRow("Prefab", snapshot.Paths.PrefabFilePath);
                DrawPreviewRow("Configuration", snapshot.Paths.ConfigFilePath);
                if (snapshot.Request.UseMvp)
                {
                    DrawPreviewRow("View", snapshot.Paths.ViewInterfaceFilePath);
                    DrawPreviewRow("Presenter", snapshot.Paths.PresenterFilePath);
                }
            }

            if (snapshot.ExistingFiles.Length > 0)
            {
                DrawAlert("Output conflict", "The creator will not overwrite the following assets:");
                for (int i = 0; i < snapshot.ExistingFiles.Length; i++)
                {
                    EditorGUILayout.LabelField(snapshot.ExistingFiles[i], _previewValueStyle);
                }
            }
            else if (!string.IsNullOrEmpty(snapshot.ValidationMessage))
            {
                DrawAlert("Creation blocked", snapshot.ValidationMessage);
            }

            DrawCreateButton(snapshot.CanCreate);
            EditorGUILayout.HelpBox(
                "Preflight runs before writes. Immediate failures remove every asset created by the operation. Script binding is persisted in UserSettings and resumes after compilation or domain reload.",
                MessageType.Info);
            DrawPostCompileControls();
            EditorGUILayout.EndVertical();
        }

        private void DrawCreateButton(bool canCreate)
        {
            EditorGUILayout.Space(6f);
            Rect outer = EditorGUILayout.GetControlRect(false, 52f);
            bool hovered = canCreate && outer.Contains(Event.current.mousePosition);
            Color border = canCreate ? new Color(0.08f, 0.30f, 0.14f) : new Color(0.18f, 0.18f, 0.18f);
            Color fill = canCreate
                ? hovered ? new Color(0.27f, 0.67f, 0.37f) : new Color(0.20f, 0.57f, 0.30f)
                : new Color(0.27f, 0.27f, 0.27f);
            EditorGUI.DrawRect(outer, border);
            Rect inner = new Rect(outer.x + 1f, outer.y + 1f, outer.width - 2f, outer.height - 2f);
            EditorGUI.DrawRect(inner, fill);
            EditorGUI.DrawRect(new Rect(inner.x, inner.y, 5f, inner.height), canCreate ? new Color(0.52f, 0.90f, 0.60f) : OptionalColor);
            EditorGUI.LabelField(
                new Rect(inner.x, inner.y + 7f, inner.width, 20f),
                "Create " + GetSafeWindowName("UIWindow"),
                _createTitleStyle);
            EditorGUI.LabelField(
                new Rect(inner.x, inner.y + 29f, inner.width, 16f),
                canCreate
                    ? "Generate, validate, bind, and select the resulting prefab"
                    : "Resolve the validation messages to enable creation",
                _createSubtitleStyle);

            if (canCreate)
            {
                EditorGUIUtility.AddCursorRect(outer, MouseCursor.Link);
                if (GUI.Button(outer, GUIContent.none, GUIStyle.none))
                {
                    CreateWindow();
                }
            }
        }

        private void DrawFeedback()
        {
            if (_feedbackKind == FeedbackKind.None)
            {
                return;
            }

            Color color;
            MessageType type;
            switch (_feedbackKind)
            {
                case FeedbackKind.Success:
                    color = ReadyColor;
                    type = MessageType.Info;
                    break;
                case FeedbackKind.Pending:
                    color = PendingColor;
                    type = MessageType.Warning;
                    break;
                default:
                    color = InvalidColor;
                    type = MessageType.Error;
                    break;
            }

            EditorGUILayout.Space(8f);
            DrawSectionHeader("Last Operation", _feedbackTitle, color);
            EditorGUILayout.HelpBox(_feedbackMessage, type);
        }

        private void DrawPostCompileSummary()
        {
            UIWindowCreatorPostCompileProcessor.GetStatus(
                out int pendingCount,
                out int failedCount,
                out _);
            if (pendingCount == 0 && failedCount == 0)
            {
                return;
            }

            string value = failedCount > 0
                ? $"{failedCount} failed binding operation(s) require attention"
                : $"{pendingCount} binding operation(s) waiting for compilation";
            DrawStatusRow(
                "Binding",
                value,
                failedCount > 0 ? "Failed" : "Pending",
                failedCount > 0 ? InvalidColor : PendingColor);
        }

        private void DrawPostCompileControls()
        {
            UIWindowCreatorPostCompileProcessor.GetStatus(
                out int pendingCount,
                out int failedCount,
                out string lastError);
            if (pendingCount == 0 && failedCount == 0)
            {
                return;
            }

            EditorGUILayout.Space(6f);
            if (pendingCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{pendingCount} generated window binding operation(s) are waiting for compilation.",
                    MessageType.Info);
            }
            if (failedCount > 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(lastError)
                        ? $"{failedCount} post-compile binding operation(s) failed."
                        : $"{failedCount} post-compile binding operation(s) failed. Last error: {lastError}",
                    MessageType.Error);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Retry Failed Bindings"))
                {
                    UIWindowCreatorPostCompileProcessor.RetryFailed();
                    Repaint();
                }
                if (GUILayout.Button("Remove Failed Records"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Remove Failed Binding Records",
                            "This removes failed queue records only. Generated assets are not deleted.",
                            "Remove",
                            "Cancel"))
                    {
                        UIWindowCreatorPostCompileProcessor.RemoveFailed();
                        Repaint();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFolderCard(
            string title,
            string description,
            ref DefaultAsset folder,
            string fileName,
            bool required)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool valid = IsValidFolder(folder);
            DrawCardHeader(
                title,
                folder == null ? required ? "Missing" : "Optional" : valid ? "Ready" : "Invalid",
                folder == null ? required ? MissingColor : OptionalColor : valid ? ReadyColor : InvalidColor,
                required);
            EditorGUILayout.LabelField(description + " Drag a Project folder here or use the object picker.", _helpStyle);

            DefaultAsset newFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder",
                folder,
                typeof(DefaultAsset),
                false);
            if (newFolder != folder)
            {
                folder = newFolder;
                MarkSnapshotDirty(true);
            }

            if (folder == null)
            {
                DrawPreviewRow("Status", "No folder selected");
            }
            else
            {
                string assetPath = AssetDatabase.GetAssetPath(folder);
                DrawPreviewRow("Folder", assetPath);
                if (valid)
                {
                    DrawPreviewRow("Output", assetPath + "/" + fileName);
                }
                else
                {
                    DrawAlert("Invalid output folder", "Select a folder asset inside the Unity project, not a file asset.");
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        private void DrawSectionHeader(string title, string subtitle, Color color)
        {
            EditorGUILayout.Space(4f);
            Rect rect = EditorGUILayout.GetControlRect(false, 28f);
            EditorGUI.DrawRect(rect, color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.18f));
            EditorGUI.LabelField(
                new Rect(rect.x + 9f, rect.y + 3f, rect.width - 18f, 19f),
                title,
                _sectionTitleStyle);
            if (!string.IsNullOrEmpty(subtitle))
            {
                EditorGUILayout.LabelField(subtitle, _heroSubtitleStyle);
            }
            EditorGUILayout.Space(3f);
        }

        private static void DrawCardHeader(
            string title,
            string status,
            Color statusColor,
            bool? required = null)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 21f);
            float reserved = StatusBadgeWidth;
            if (required.HasValue)
            {
                reserved += RequirementBadgeWidth + BadgeSpacing;
            }

            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width - reserved - 8f, rect.height),
                title,
                _cardTitleStyle);

            Rect statusRect = new Rect(
                rect.xMax - StatusBadgeWidth,
                rect.y + 2f,
                StatusBadgeWidth,
                rect.height - 4f);
            if (required.HasValue)
            {
                Rect requirementRect = new Rect(
                    statusRect.x - RequirementBadgeWidth - BadgeSpacing,
                    statusRect.y,
                    RequirementBadgeWidth,
                    statusRect.height);
                DrawBadge(
                    requirementRect,
                    required.Value ? "Required" : "Optional",
                    required.Value ? RequiredColor : OptionalColor);
            }
            DrawBadge(statusRect, status, statusColor);
        }

        private static void DrawStatusRow(string label, string value, bool ready)
        {
            DrawStatusRow(label, value, ready ? "OK" : "Need", ready ? ReadyColor : MissingColor);
        }

        private static void DrawStatusRow(
            string label,
            string value,
            string status,
            Color statusColor)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 19f);
            const float badgeWidth = 67f;
            const float labelWidth = 88f;
            const float gap = 8f;
            DrawBadge(
                new Rect(rect.x, rect.y + 2f, badgeWidth, rect.height - 4f),
                status,
                statusColor);
            float labelX = rect.x + badgeWidth + gap;
            EditorGUI.LabelField(
                new Rect(labelX, rect.y, labelWidth, rect.height),
                label,
                EditorStyles.miniBoldLabel);
            float valueX = labelX + labelWidth + gap;
            EditorGUI.LabelField(
                new Rect(valueX, rect.y, Mathf.Max(32f, rect.xMax - valueX), rect.height),
                value,
                _previewValueStyle);
        }

        private static void DrawBadge(Rect rect, string label, Color color)
        {
            EditorGUI.DrawRect(rect, color);
            EditorGUI.LabelField(rect, label, _badgeStyle);
        }

        private static void DrawPreviewRow(string label, string value)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 19f);
            float labelWidth = Mathf.Min(PreviewLabelWidth, Mathf.Max(76f, rect.width * 0.36f));
            EditorGUI.LabelField(
                new Rect(rect.x + 4f, rect.y, labelWidth, rect.height),
                label,
                _previewKeyStyle);
            float valueX = rect.x + labelWidth + 12f;
            EditorGUI.LabelField(
                new Rect(valueX, rect.y, Mathf.Max(32f, rect.xMax - valueX), rect.height),
                string.IsNullOrEmpty(value) ? "-" : value,
                _previewValueStyle);
        }

        private static void DrawAlert(string title, string body)
        {
            EditorGUILayout.Space(3f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect accent = GUILayoutUtility.GetRect(1f, 3f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(accent, InvalidColor);
            EditorGUILayout.LabelField(title, _alertTitleStyle);
            EditorGUILayout.LabelField(body, _alertBodyStyle);
            EditorGUILayout.EndVertical();
        }

        private CreatorSnapshot GetSnapshot()
        {
            if (!_snapshotDirty && _snapshot != null)
            {
                return _snapshot;
            }

            UIWindowCreationRequest request = BuildRequest();
            string validation = UIWindowCreationValidator.Validate(request, _validationErrors);
            bool hasPaths = UIWindowCreationValidator.TryBuildPaths(
                request,
                out UIWindowCreationPaths paths,
                out _);
            _existingFiles.Clear();
            UIWindowCreationValidator.GetExistingFiles(request, _existingFiles);

            bool templateValid = GetTemplateInspection().IsValid || _templatePrefab == null;
            PipelineStatus status;
            if (HasMissingInput(request))
            {
                status = PipelineStatus.NeedsInput;
            }
            else if (!HasValidIdentity(request) || !AreRequiredFoldersReady() || !templateValid || !IsSourceReady(request))
            {
                status = PipelineStatus.Invalid;
            }
            else if (_existingFiles.Count > 0)
            {
                status = PipelineStatus.Conflict;
            }
            else
            {
                status = string.IsNullOrEmpty(validation) ? PipelineStatus.Ready : PipelineStatus.Invalid;
            }

            _snapshot = new CreatorSnapshot
            {
                Request = request,
                Paths = paths,
                HasPaths = hasPaths,
                TemplateValid = templateValid,
                CanCreate = string.IsNullOrEmpty(validation) && templateValid,
                Status = status,
                ValidationMessage = validation,
                ExistingFiles = _existingFiles.ToArray()
            };
            _snapshotDirty = false;
            return _snapshot;
        }

        private void MarkSnapshotDirty(bool saveImmediately = false)
        {
            _snapshotDirty = true;
            if (saveImmediately)
            {
                SaveSettings();
            }
            Repaint();
        }

        private static bool HasMissingInput(UIWindowCreationRequest request)
        {
            return string.IsNullOrEmpty(request.WindowName) ||
                   request.ScriptFolder == null ||
                   request.PrefabFolder == null ||
                   request.ConfigFolder == null ||
                   request.Layer == null ||
                   (request.UseMvp && request.PresenterFolder == null) ||
                   !IsSourceReady(request);
        }

        private static bool HasValidIdentity(UIWindowCreationRequest request)
        {
            return UIWindowCreationValidator.IsValidCSharpIdentifier(request.WindowName) &&
                   UIWindowCreationValidator.IsValidNamespace(request.NamespaceName);
        }

        private static bool IsSourceReady(UIWindowCreationRequest request)
        {
            if (request.SourceMode == UIWindowConfiguration.PrefabSource.AssetReference)
            {
                return !string.IsNullOrWhiteSpace(request.RuntimeLocation);
            }

            if (request.SourceMode == UIWindowConfiguration.PrefabSource.PathLocation &&
                !request.AutoFillLocationFromPrefabPath)
            {
                return !string.IsNullOrWhiteSpace(request.RuntimeLocation);
            }

            return true;
        }

        private bool AreRequiredFoldersReady()
        {
            return IsValidFolder(_scriptFolder) &&
                   IsValidFolder(_prefabFolder) &&
                   IsValidFolder(_configFolder) &&
                   (!_useMvp || IsValidFolder(_presenterFolder));
        }

        private static bool IsValidFolder(DefaultAsset folder)
        {
            return folder != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder));
        }

        private string GetFolderSummary()
        {
            return _useMvp
                ? "Script, prefab, configuration, and Presenter folders"
                : "Script, prefab, and configuration folders";
        }

        private static string GetPipelineStatusLabel(PipelineStatus status)
        {
            switch (status)
            {
                case PipelineStatus.Ready:
                    return "Ready";
                case PipelineStatus.NeedsInput:
                    return "Needs Input";
                case PipelineStatus.Invalid:
                    return "Invalid";
                case PipelineStatus.Conflict:
                    return "Conflict";
                default:
                    return "Blocked";
            }
        }

        private static Color GetPipelineStatusColor(PipelineStatus status)
        {
            switch (status)
            {
                case PipelineStatus.Ready:
                    return ReadyColor;
                case PipelineStatus.NeedsInput:
                    return MissingColor;
                case PipelineStatus.Invalid:
                case PipelineStatus.Conflict:
                    return InvalidColor;
                default:
                    return OptionalColor;
            }
        }

        private string GetSourceModeSummary(CreatorSnapshot snapshot)
        {
            switch (_sourceMode)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    return "Direct generated prefab reference";
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    return string.IsNullOrWhiteSpace(_runtimeLocation)
                        ? "Provider location required"
                        : "Provider location: " + _runtimeLocation.Trim();
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    if (_autoFillPathLocation)
                    {
                        return snapshot.HasPaths
                            ? "Project path: " + snapshot.Paths.PrefabFilePath
                            : "Generated prefab project path";
                    }
                    return string.IsNullOrWhiteSpace(_runtimeLocation)
                        ? "Runtime path required"
                        : "Runtime path: " + _runtimeLocation.Trim();
                default:
                    return _sourceMode.ToString();
            }
        }

        private string GetTemplateSummary()
        {
            if (_templatePrefab == null)
            {
                return "Minimal full-screen UI root";
            }

            TemplateInspection inspection = GetTemplateInspection();
            return inspection.IsValid
                ? _templatePrefab.name + " (cloned and processed)"
                : "Selected template requires attention";
        }

        private string GetSafeWindowName(string fallback)
        {
            string trimmed = _windowName != null ? _windowName.Trim() : string.Empty;
            return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
        }

        private UIWindowCreationRequest BuildRequest()
        {
            return new UIWindowCreationRequest(
                _windowName,
                _namespaceName,
                _scriptFolder,
                _prefabFolder,
                _configFolder,
                _presenterFolder,
                _layer,
                _useMvp,
                _sourceMode,
                _autoFillPathLocation,
                _runtimeLocation);
        }

        private TemplateInspection GetTemplateInspection()
        {
            if (!_templateInspectionDirty)
            {
                return _templateInspection;
            }

            _templateInspection = InspectTemplate(_templatePrefab);
            _templateInspectionDirty = false;
            return _templateInspection;
        }

        internal static TemplateInspection InspectTemplate(GameObject template)
        {
            if (template == null)
            {
                return default;
            }

            UIWindow[] windows = template.GetComponentsInChildren<UIWindow>(true);
            Transform[] transforms = template.GetComponentsInChildren<Transform>(true);
            TemplateInspection inspection = new TemplateInspection
            {
                IsPrefab = PrefabUtility.GetPrefabAssetType(template) != PrefabAssetType.NotAPrefab,
                HasRootRectTransform = template.transform is RectTransform,
                HasCanvasGroup = template.GetComponent<CanvasGroup>() != null,
                HasWindowComponent = windows.Length > 0,
                HasRootWindowComponent = template.GetComponent<UIWindow>() != null,
                WindowComponentCount = windows.Length,
                ObjectCount = transforms.Length,
            };

            for (int i = 0; i < transforms.Length; i++)
            {
                inspection.MissingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[i].gameObject);
            }

            Component[] components = template.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (component is Graphic)
                {
                    inspection.GraphicCount++;
                }
                if (component is Selectable)
                {
                    inspection.SelectableCount++;
                }
                if (component is LayoutGroup)
                {
                    inspection.LayoutGroupCount++;
                }
                if (component is ContentSizeFitter)
                {
                    inspection.ContentSizeFitterCount++;
                }
                if (component is Mask || component is RectMask2D)
                {
                    inspection.MaskCount++;
                }
                if (component is Canvas)
                {
                    inspection.CanvasCount++;
                }
                if (IsTmpTextComponent(component.GetType()))
                {
                    inspection.TmpTextCount++;
                }
            }
            return inspection;
        }

        private static bool IsTmpTextComponent(Type type)
        {
            while (type != null)
            {
                if (string.Equals(type.FullName, "TMPro.TMP_Text", StringComparison.Ordinal))
                {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        private void SetTemplate(GameObject template, bool saveImmediately)
        {
            _templatePrefab = template;
            _templateSelectionExplicit = true;
            _templateInspectionDirty = true;
            MarkSnapshotDirty(saveImmediately);
        }

        private void UseDefaultTemplate(bool saveImmediately)
        {
            string templatePath = AssetDatabase.GUIDToAssetPath(DefaultTemplateGuid);
            GameObject defaultTemplate = string.IsNullOrEmpty(templatePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            SetTemplate(defaultTemplate, saveImmediately);
        }

        private void CreateWindow()
        {
            CreatorSnapshot snapshot = GetSnapshot();
            if (!snapshot.CanCreate)
            {
                _feedbackKind = FeedbackKind.Failure;
                _feedbackTitle = "Preflight blocked";
                _feedbackMessage = string.IsNullOrEmpty(snapshot.ValidationMessage)
                    ? "Resolve the highlighted template or output validation issue."
                    : snapshot.ValidationMessage;
                Repaint();
                return;
            }

            UIWindowCreationRequest request = BuildRequest();
            UIWindowAssemblyValidator.InvalidateCache();
            _templateInspectionDirty = true;
            string freshValidation = UIWindowCreationValidator.Validate(request, _validationErrors);
            bool freshTemplateValid = _templatePrefab == null || GetTemplateInspection().IsValid;
            bool pathsBuilt = UIWindowCreationValidator.TryBuildPaths(
                request,
                out UIWindowCreationPaths paths,
                out string pathError);
            if (!freshTemplateValid ||
                !string.IsNullOrEmpty(freshValidation) ||
                !pathsBuilt)
            {
                _feedbackKind = FeedbackKind.Failure;
                _feedbackTitle = "Transaction preflight blocked";
                _feedbackMessage = !freshTemplateValid
                    ? "The selected template changed and no longer passes validation."
                    : !string.IsNullOrEmpty(freshValidation)
                        ? freshValidation
                        : pathError;
                MarkSnapshotDirty();
                return;
            }

            var createdAssets = new List<CreatedAssetRecord>(5);
            CreatedAssetRecord generatedWindowScript = null;
            bool committed = false;
            try
            {
                SaveSettings();
                EditorUtility.DisplayProgressBar("Create UIWindow", "Writing generated scripts...", 0.12f);

                generatedWindowScript = WriteAssetText(
                    paths.ScriptFilePath,
                    BuildWindowScript(request.WindowName, request.NamespaceName, request.UseMvp));
                createdAssets.Add(generatedWindowScript);

                if (request.UseMvp)
                {
                    createdAssets.Add(WriteAssetText(
                        paths.ViewInterfaceFilePath,
                        BuildViewInterface(request.WindowName, request.NamespaceName)));
                    createdAssets.Add(WriteAssetText(
                        paths.PresenterFilePath,
                        BuildPresenter(request.WindowName, request.NamespaceName)));
                }

                EditorUtility.DisplayProgressBar("Create UIWindow", "Importing scripts...", 0.38f);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                EditorUtility.DisplayProgressBar("Create UIWindow", "Creating prefab from template...", 0.56f);
                GameObject prefab = CreatePrefab(
                    paths.PrefabFilePath,
                    request.WindowName,
                    out string prefabGuid);
                createdAssets.Add(new CreatedAssetRecord(paths.PrefabFilePath, prefabGuid));

                EditorUtility.DisplayProgressBar("Create UIWindow", "Creating runtime configuration...", 0.72f);
                string configGuid = UIWindowConfigurationWriter.Create(
                    paths.ConfigFilePath,
                    prefab,
                    request.Layer,
                    request.SourceMode,
                    request.AutoFillLocationFromPrefabPath,
                    request.RuntimeLocation);
                createdAssets.Add(new CreatedAssetRecord(paths.ConfigFilePath, configGuid));

                EditorUtility.DisplayProgressBar("Create UIWindow", "Binding generated window component...", 0.88f);
                Type generatedType = FindLoadedType(request.WindowName, request.NamespaceName);
                bool bound = generatedType != null && UIWindowPrefabScriptBinder.AddScriptComponentToPrefab(
                    paths.PrefabFilePath,
                    generatedType,
                    request.WindowName);
                if (bound && request.SourceMode == UIWindowConfiguration.PrefabSource.PrefabReference)
                {
                    UIWindowConfigurationWriter.UpdatePrefabReference(
                        paths.ConfigFilePath,
                        paths.PrefabFilePath);
                }
                else if (!bound)
                {
                    UIWindowCreatorPostCompileProcessor.Schedule(
                        request.WindowName,
                        request.NamespaceName,
                        paths.ScriptFilePath,
                        generatedWindowScript.ExpectedGuid,
                        paths.PrefabFilePath,
                        prefabGuid,
                        paths.ConfigFilePath,
                        configGuid,
                        request.SourceMode);
                }

                committed = true;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                GameObject generatedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths.PrefabFilePath);
                Selection.activeObject = generatedPrefab;
                if (generatedPrefab != null)
                {
                    EditorGUIUtility.PingObject(generatedPrefab);
                }

                _feedbackKind = bound ? FeedbackKind.Success : FeedbackKind.Pending;
                _feedbackTitle = bound ? "Window created" : "Window created; binding pending";
                _feedbackMessage =
                    $"Script: {paths.ScriptFilePath}\n" +
                    $"Prefab: {paths.PrefabFilePath}\n" +
                    $"Configuration: {paths.ConfigFilePath}\n\n" +
                    (bound
                        ? "The generated component is bound and the prefab is ready for authoring."
                        : "The generated component will be bound automatically after compilation. The operation is persisted across domain reloads.");
                CLogger.LogInfo(
                    $"Created UIWindow '{request.WindowName}' at '{paths.PrefabFilePath}'.",
                    LogCategory);
            }
            catch (Exception exception)
            {
                RollbackResult rollbackResult = default;
                var compensationFailures = new List<string>(4);
                if (exception is UIWindowCreatorAssetCommitException assetCommitException)
                {
                    for (int i = 0; i < assetCommitException.ResidualPaths.Length; i++)
                    {
                        compensationFailures.Add(
                            "Preserved unverified residual: " + assetCommitException.ResidualPaths[i]);
                    }
                }
                if (!committed)
                {
                    try
                    {
                        UIWindowCreatorPostCompileProcessor.Cancel(paths.ConfigFilePath);
                    }
                    catch (Exception cancelException)
                    {
                        compensationFailures.Add(
                            "Pending journal cancellation failed: " + cancelException.Message);
                    }

                    try
                    {
                        rollbackResult = RollbackCreatedPaths(createdAssets);
                    }
                    catch (Exception rollbackException)
                    {
                        compensationFailures.Add(
                            "Rollback orchestration failed: " + rollbackException.Message);
                    }
                }

                _feedbackKind = FeedbackKind.Failure;
                _feedbackTitle = "Window creation failed";
                if (committed)
                {
                    _feedbackMessage = exception.Message +
                        "\n\nGenerated assets were committed before the final failure. Inspect the paths above before retrying.";
                }
                else if (rollbackResult.IsComplete && compensationFailures.Count == 0)
                {
                    _feedbackMessage = exception.Message +
                        "\n\nAll assets created by this operation were rolled back.";
                }
                else
                {
                    var report = new StringBuilder(exception.Message);
                    report.Append("\n\nRollback completed with residual risk.");
                    string[] residualPaths = rollbackResult.ResidualPaths ?? Array.Empty<string>();
                    string[] rollbackFailures = rollbackResult.Failures ?? Array.Empty<string>();
                    for (int i = 0; i < residualPaths.Length; i++)
                    {
                        report.Append("\nResidual: ").Append(residualPaths[i]);
                    }
                    for (int i = 0; i < rollbackFailures.Length; i++)
                    {
                        report.Append("\nRollback error: ").Append(rollbackFailures[i]);
                    }
                    for (int i = 0; i < compensationFailures.Count; i++)
                    {
                        report.Append("\nCompensation error: ").Append(compensationFailures[i]);
                    }
                    _feedbackMessage = report.ToString();
                }
                CLogger.LogError($"Window creation failed: {exception}", LogCategory);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                MarkSnapshotDirty();
            }
        }

        private GameObject CreatePrefab(
            string prefabPath,
            string windowName,
            out string prefabGuid)
        {
            prefabGuid = string.Empty;
            GameObject instance = null;
            string temporaryPath = string.Empty;
            bool movedToFinalPath = false;
            try
            {
                if (_templatePrefab != null)
                {
                    TemplateInspection inspection = GetTemplateInspection();
                    if (!inspection.IsValid)
                    {
                        throw new InvalidOperationException(
                            "The selected template is not a valid UI prefab with a RectTransform root.");
                    }

                    instance = PrefabUtility.InstantiatePrefab(_templatePrefab) as GameObject;
                    if (instance == null)
                    {
                        throw new InvalidOperationException("Template prefab could not be instantiated.");
                    }
                    PrefabUtility.UnpackPrefabInstance(
                        instance,
                        PrefabUnpackMode.OutermostRoot,
                        InteractionMode.AutomatedAction);
                    _templateProcessor.Process(instance, windowName);
                    if (instance.GetComponent<CanvasGroup>() == null)
                    {
                        instance.AddComponent<CanvasGroup>();
                    }
                }
                else
                {
                    instance = new GameObject(windowName, typeof(RectTransform), typeof(CanvasGroup));
                    RectTransform rect = instance.GetComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }

                instance.name = windowName;
                if (instance.GetComponent<UIWindow>() == null)
                {
                    instance.AddComponent<UIWindow>();
                }

                if (!UIWindowCreationValidator.TryEnsureOutputAvailable(
                        prefabPath,
                        ".prefab",
                        out _,
                        out string collisionError))
                {
                    throw new IOException(
                        $"Prefab output became unavailable before creation: {collisionError}");
                }

                temporaryPath = AllocateTemporaryAssetPath(prefabPath, ".prefab");
                GameObject temporaryPrefab = PrefabUtility.SaveAsPrefabAsset(instance, temporaryPath);
                if (temporaryPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to save temporary prefab '{temporaryPath}'.");
                }

                prefabGuid = AssetDatabase.AssetPathToGUID(temporaryPath);
                VerifyUnityAssetIdentity(temporaryPath, prefabGuid, "Temporary prefab");

                if (!UIWindowCreationValidator.TryEnsureOutputAvailable(
                        prefabPath,
                        ".prefab",
                        out _,
                        out collisionError))
                {
                    throw new IOException(
                        $"Prefab output became unavailable before commit: {collisionError}");
                }

                string moveError = AssetDatabase.MoveAsset(temporaryPath, prefabPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    throw new IOException(
                        $"Failed to commit prefab without replacing another asset: {moveError}");
                }

                movedToFinalPath = true;
                VerifyUnityAssetIdentity(prefabPath, prefabGuid, "Committed prefab");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                return prefab != null
                    ? prefab
                    : throw new InvalidOperationException(
                        $"Failed to reload committed prefab '{prefabPath}'.");
            }
            catch (Exception exception)
            {
                string ownedPath = movedToFinalPath ? prefabPath : temporaryPath;
                if (!string.IsNullOrEmpty(ownedPath) &&
                    TryGetExistingAssetFile(ownedPath, out _))
                {
                    string cleanupError = "Prefab GUID was not captured.";
                    if (string.IsNullOrEmpty(prefabGuid) ||
                        !TryDeleteOwnedUnityAsset(ownedPath, prefabGuid, out cleanupError))
                    {
                        throw new UIWindowCreatorAssetCommitException(
                            exception.Message +
                            $" Cleanup preserved unverified prefab residual '{ownedPath}': {cleanupError}",
                            exception,
                            ownedPath);
                    }
                }

                throw;
            }
            finally
            {
                if (instance != null)
                {
                    DestroyImmediate(instance);
                }
            }
        }

        private static string AllocateTemporaryAssetPath(string finalAssetPath, string extension)
        {
            string directory = Path.GetDirectoryName(finalAssetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    $"Asset path '{finalAssetPath}' has no parent folder.");
            }

            for (int attempt = 0; attempt < 32; attempt++)
            {
                string candidate =
                    directory + "/__UIWindowCreator_" + Guid.NewGuid().ToString("N") + extension;
                if (UIWindowCreationValidator.TryEnsureOutputAvailable(
                        candidate,
                        extension,
                        out _,
                        out _))
                {
                    return candidate;
                }
            }

            throw new IOException(
                $"Could not allocate a unique temporary asset beside '{finalAssetPath}'.");
        }

        private static void VerifyUnityAssetIdentity(
            string assetPath,
            string expectedGuid,
            string label)
        {
            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string resolvedPath = AssetDatabase.GUIDToAssetPath(expectedGuid);
            if (string.IsNullOrEmpty(expectedGuid) ||
                !string.Equals(currentGuid, expectedGuid, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolvedPath, assetPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{label} identity validation failed. Expected GUID '{expectedGuid}' at " +
                    $"'{assetPath}', current GUID='{currentGuid}', current path='{resolvedPath}'.");
            }
        }

        private static bool TryDeleteOwnedUnityAsset(
            string assetPath,
            string expectedGuid,
            out string error)
        {
            error = string.Empty;
            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string resolvedPath = AssetDatabase.GUIDToAssetPath(expectedGuid);
            if (string.IsNullOrEmpty(expectedGuid) ||
                !string.Equals(currentGuid, expectedGuid, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolvedPath, assetPath, StringComparison.Ordinal))
            {
                error =
                    $"Ownership mismatch. Expected GUID '{expectedGuid}', current GUID='{currentGuid}', " +
                    $"current path='{resolvedPath}'.";
                return false;
            }

            try
            {
                if (!AssetDatabase.DeleteAsset(assetPath))
                {
                    error = "AssetDatabase refused to delete the owned asset.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (TryGetExistingAssetFile(assetPath, out _) ||
                string.Equals(
                    AssetDatabase.GUIDToAssetPath(expectedGuid),
                    assetPath,
                    StringComparison.Ordinal))
            {
                error = "Owned asset still exists after deletion.";
                return false;
            }

            return true;
        }

        private static bool TryGetExistingAssetFile(string assetPath, out string absolutePath)
        {
            return UIWindowCreationValidator.TryGetAbsoluteAssetPath(
                       assetPath,
                       out absolutePath,
                       out _) &&
                   (File.Exists(absolutePath) || File.Exists(absolutePath + ".meta"));
        }

        private static string BuildWindowScript(string className, string namespaceName, bool useMvp)
        {
            string view = useMvp ? $", I{className}View" : string.Empty;
            string body =
                $"public sealed class {className} : UIWindow{view}\n" +
                "{\n" +
                "    protected override void OnOpened()\n" +
                "    {\n" +
                "        base.OnOpened();\n" +
                "    }\n" +
                "}\n";
            return WrapInNamespace(
                "using CycloneGames.UIFramework.Runtime;\n\n" + body,
                namespaceName);
        }

        private static string BuildViewInterface(string className, string namespaceName)
        {
            return WrapInNamespace($"public interface I{className}View\n{{\n}}\n", namespaceName);
        }

        private static string BuildPresenter(string className, string namespaceName)
        {
            string presenterName = className + "Presenter";
            string body =
                $"public sealed class {presenterName} : UIPresenter<I{className}View>\n" +
                "{\n" +
                "    protected override void OnViewBound()\n" +
                "    {\n" +
                $"        // Register in the composition root with presenterBinder.Register<{presenterName}>(\"{className}\");\n" +
                "    }\n" +
                "}\n";
            return WrapInNamespace(
                "using CycloneGames.UIFramework.Runtime;\n\n" + body,
                namespaceName);
        }

        private static string WrapInNamespace(string source, string namespaceName)
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                return source;
            }

            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            var builder = new StringBuilder(source.Length + namespaceName.Length + 32);
            builder.Append("namespace ").Append(namespaceName).Append("\n{\n");
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    builder.Append("    ").Append(lines[i]);
                }
                builder.Append('\n');
            }
            builder.Append("}\n");
            return builder.ToString();
        }

        internal static CreatedAssetRecord WriteAssetText(
            string assetPath,
            string content,
            bool importForOwnership = true)
        {
            if (!UIWindowCreationValidator.TryEnsureOutputAvailable(
                    assetPath,
                    ".cs",
                    out string absolutePath,
                    out string validationError))
            {
                throw new IOException(
                    $"Generated script output '{assetPath}' is unavailable: {validationError}");
            }

            string directory = Path.GetDirectoryName(absolutePath);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(directory);
            }

            string temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(absolutePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            byte[] bytes = new UTF8Encoding(false).GetBytes(
                (content ?? string.Empty).Replace("\r\n", "\n"));
            string expectedHash = ComputeSha256(bytes);
            bool targetCommitted = false;
            string committedGuid = string.Empty;
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(absolutePath + ".meta"))
                {
                    throw new IOException(
                        $"Generated script output '{assetPath}' acquired an orphan .meta before commit.");
                }

                // File.Move without an overwrite option is the create-new commit point.
                File.Move(temporaryPath, absolutePath);
                targetCommitted = true;
                if (File.Exists(absolutePath + ".meta"))
                {
                    var residuals = new List<string>(2);
                    if (!TryDeleteMatchingFile(absolutePath, expectedHash, out string cleanupError))
                    {
                        residuals.Add(assetPath);
                    }
                    residuals.Add(assetPath + ".meta");
                    throw new UIWindowCreatorAssetCommitException(
                        $"Generated script output '{assetPath}' collided with metadata created during commit. " +
                        $"The unknown metadata was preserved. {cleanupError}",
                        null,
                        residuals.ToArray());
                }

                if (importForOwnership)
                {
                    AssetDatabase.ImportAsset(
                        assetPath,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    committedGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    VerifyUnityAssetIdentity(assetPath, committedGuid, "Generated script");
                }
                return new CreatedAssetRecord(assetPath, committedGuid);
            }
            catch (UIWindowCreatorAssetCommitException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (targetCommitted && File.Exists(absolutePath))
                {
                    var residuals = new List<string>(2);
                    string cleanupError = string.Empty;
                    bool cleaned = !string.IsNullOrEmpty(committedGuid) &&
                                   TryDeleteOwnedUnityAsset(
                                       assetPath,
                                       committedGuid,
                                       out cleanupError);
                    if (!cleaned && !File.Exists(absolutePath + ".meta"))
                    {
                        cleaned = TryDeleteMatchingFile(
                            absolutePath,
                            expectedHash,
                            out cleanupError);
                    }

                    if (!cleaned || File.Exists(absolutePath))
                    {
                        residuals.Add(assetPath);
                    }
                    if (File.Exists(absolutePath + ".meta"))
                    {
                        residuals.Add(assetPath + ".meta");
                    }
                    if (residuals.Count > 0)
                    {
                        throw new UIWindowCreatorAssetCommitException(
                            $"Generated script commit failed. Cleanup preserved unverified residuals. {cleanupError}",
                            exception,
                            residuals.ToArray());
                    }
                }

                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool TryDeleteMatchingFile(
            string absolutePath,
            string expectedHash,
            out string error)
        {
            error = string.Empty;
            if (!File.Exists(absolutePath))
            {
                return true;
            }

            try
            {
                string currentHash = ComputeFileSha256(absolutePath);
                if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
                {
                    error = "The committed file content changed before cleanup and was preserved.";
                    return false;
                }

                File.Delete(absolutePath);
                if (File.Exists(absolutePath))
                {
                    error = "The committed file remained after cleanup.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string ComputeFileSha256(string absolutePath)
        {
            using (FileStream stream = new FileStream(
                       absolutePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }

        private static Type FindLoadedType(string className, string namespaceName)
        {
            string fullName = string.IsNullOrWhiteSpace(namespaceName)
                ? className
                : namespaceName + "." + className;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        internal static RollbackResult RollbackCreatedPaths(
            IReadOnlyList<CreatedAssetRecord> createdAssets)
        {
            var failures = new List<string>(4);
            var residualPaths = new List<string>(4);
            int attemptedCount = 0;
            if (createdAssets == null)
            {
                return new RollbackResult(0, Array.Empty<string>(), Array.Empty<string>());
            }

            for (int i = createdAssets.Count - 1; i >= 0; i--)
            {
                attemptedCount++;
                CreatedAssetRecord record = createdAssets[i];
                string assetPath = record?.AssetPath ?? string.Empty;
                if (record == null || string.IsNullOrEmpty(assetPath))
                {
                    failures.Add("Rollback ownership record or path is empty.");
                    continue;
                }

                string extension = Path.GetExtension(assetPath);
                if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"Refused rollback path '{assetPath}' with unsupported extension '{extension}'.");
                    residualPaths.Add(assetPath);
                    continue;
                }

                if (!record.TryVerifyOwnership(
                        out _,
                        out bool alreadyAbsent,
                        out string ownershipError))
                {
                    failures.Add(
                        $"Refused rollback for unowned or replaced output '{assetPath}': {ownershipError}");
                    residualPaths.Add(assetPath);
                    continue;
                }
                if (alreadyAbsent)
                {
                    continue;
                }

                if (!TryDeleteOwnedUnityAsset(
                        assetPath,
                        record.ExpectedGuid,
                        out string deletionError))
                {
                    failures.Add(
                        $"Owned asset deletion failed for '{assetPath}': {deletionError}");
                    residualPaths.Add(assetPath);
                }
            }

            try
            {
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                failures.Add("AssetDatabase refresh after rollback failed: " + exception.Message);
            }

            return new RollbackResult(
                attemptedCount,
                failures.ToArray(),
                residualPaths.ToArray());
        }

        private void LoadSettings()
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            try
            {
                string json = SystemFileStore.Default.ReadText(_settingsPath, MaxSettingsBytes);
                UIWindowCreatorSettings settings = JsonUtility.FromJson<UIWindowCreatorSettings>(json);
                if (settings == null || settings.schemaVersion != 1)
                {
                    throw new InvalidDataException("Unsupported window creator settings schema.");
                }

                _namespaceName = settings.namespaceName ?? string.Empty;
                _useMvp = settings.useMvp;
                _sourceMode = Enum.IsDefined(
                    typeof(UIWindowConfiguration.PrefabSource),
                    settings.configSourceMode)
                    ? (UIWindowConfiguration.PrefabSource)settings.configSourceMode
                    : UIWindowConfiguration.PrefabSource.PrefabReference;
                _autoFillPathLocation = settings.autoFillLocationFromPrefabPath;
                _runtimeLocation = settings.runtimeLocation ?? string.Empty;
                _scriptFolder = LoadFolder(settings.scriptFolderPath);
                _prefabFolder = LoadFolder(settings.prefabFolderPath);
                _configFolder = LoadFolder(settings.configFolderPath);
                _presenterFolder = LoadFolder(settings.presenterFolderPath);
                _templateSelectionExplicit = settings.hasTemplateSelection;
                if (_templateSelectionExplicit && !string.IsNullOrEmpty(settings.templatePrefabPath))
                {
                    _templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(settings.templatePrefabPath);
                    if (_templatePrefab == null)
                    {
                        CLogger.LogWarning(
                            $"Saved UIWindow template was not found at '{settings.templatePrefabPath}'.",
                            LogCategory);
                    }
                }
            }
            catch (Exception exception)
            {
                CLogger.LogWarning(
                    $"Window creator settings were not loaded: {exception.Message}",
                    LogCategory);
            }
        }

        private void SaveSettings()
        {
            if (string.IsNullOrEmpty(_settingsPath))
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var settings = new UIWindowCreatorSettings
                {
                    schemaVersion = 1,
                    namespaceName = _namespaceName ?? string.Empty,
                    scriptFolderPath = GetAssetPath(_scriptFolder),
                    prefabFolderPath = GetAssetPath(_prefabFolder),
                    configFolderPath = GetAssetPath(_configFolder),
                    presenterFolderPath = GetAssetPath(_presenterFolder),
                    useMvp = _useMvp,
                    configSourceMode = (int)_sourceMode,
                    autoFillLocationFromPrefabPath = _autoFillPathLocation,
                    runtimeLocation = _runtimeLocation ?? string.Empty,
                    hasTemplateSelection = _templateSelectionExplicit,
                    templatePrefabPath = _templatePrefab != null
                        ? AssetDatabase.GetAssetPath(_templatePrefab)
                        : string.Empty
                };
                SystemFileStore.Default.WriteTextAtomically(
                    _settingsPath,
                    JsonUtility.ToJson(settings, true));
            }
            catch (Exception exception)
            {
                CLogger.LogWarning(
                    $"Window creator settings were not saved: {exception.Message}",
                    LogCategory);
            }
        }

        private static DefaultAsset LoadFolder(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
        }

        private static string GetAssetPath(DefaultAsset asset)
        {
            return asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty;
        }

    }
}
