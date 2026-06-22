using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.IO.Runtime;
using CycloneGames.Logger;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    [System.Serializable]
    public class UIWindowCreatorSettings
    {
        public string scriptFolderPath = "";
        public string prefabFolderPath = "";
        public string configFolderPath = "";
        public string presenterFolderPath = "";
        public string namespaceName = "";
        public bool useMVP = false;
        public int configSourceMode = (int)UIWindowConfiguration.PrefabSource.PrefabReference;
        public bool autoFillLocationFromPrefabPath = true;
    }

    public class UIWindowCreatorWindow : EditorWindow
    {
        private enum PipelineStatus
        {
            Ready,
            NeedsInput,
            Invalid,
            Conflict
        }

        private const string LOG_CATEGORY = "UIWindowCreator";
        private const string DEFAULT_TEMPLATE_GUID = "37c32b368ca8d4841b923d1b37cf97b9";
        private const string SETTINGS_FILE_NAME = "UIWindowCreatorSettings.json";
        private const string PREFS_KEY_SOURCE_MODE = "CycloneGames.UIFramework.UIWindowCreator.SourceMode";
        private const string PREFS_KEY_AUTOFILL_LOCATION = "CycloneGames.UIFramework.UIWindowCreator.AutoFillLocation";

        private string namespaceName = "";
        private DefaultAsset scriptFolder;
        private DefaultAsset soFolder;
        private DefaultAsset prefabFolder;
        private DefaultAsset presenterFolder;
        private UILayerConfiguration selectedLayer;
        private string windowName = "";
        private GameObject templatePrefab;
        private Vector2 scrollPosition;
        private bool useMVP = false;
        private UIWindowConfiguration.PrefabSource configSourceMode = UIWindowConfiguration.PrefabSource.PrefabReference;
        private bool autoFillLocationFromPrefabPath = true;
        private string settingsPath;

        private static bool _stylesInitialized = false;
        private static GUIStyle _headerStyle;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _cardTitleStyle;
        private static GUIStyle _helpTextStyle;
        private static GUIStyle _previewLabelStyle;
        private static GUIStyle _previewKeyStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _alertTitleStyle;
        private static GUIStyle _alertBodyStyle;
        private static GUIStyle _alertPathLabelStyle;
        private static GUIStyle _alertPathValueStyle;
        private static GUIStyle _alertIconFallbackStyle;
        private static GUIStyle _createButtonStyle;
        private static GUIStyle _createButtonSubStyle;
        private static GUIStyle _createButtonDisabledStyle;
        private static GUIStyle _createButtonDisabledSubStyle;
        private static GUIContent _warningIconContent;

        private static readonly Color SectionBasicColor = new Color(0.32f, 0.46f, 0.70f);
        private static readonly Color SectionPathColor = new Color(0.28f, 0.56f, 0.50f);
        private static readonly Color SectionConfigColor = new Color(0.50f, 0.42f, 0.68f);
        private static readonly Color SectionMvpColor = new Color(0.62f, 0.47f, 0.30f);
        private static readonly Color SectionTemplateColor = new Color(0.42f, 0.52f, 0.42f);
        private static readonly Color SectionReviewColor = new Color(0.34f, 0.58f, 0.36f);
        private static readonly Color ReadyColor = new Color(0.24f, 0.58f, 0.34f);
        private static readonly Color MissingColor = new Color(0.70f, 0.22f, 0.20f);
        private static readonly Color InvalidColor = new Color(0.68f, 0.27f, 0.24f);
        private static readonly Color OptionalColor = new Color(0.42f, 0.42f, 0.42f);
        private static readonly Color RequiredColor = new Color(0.30f, 0.55f, 0.82f);
        private static readonly Color CreateButtonBorderColor = new Color(0.10f, 0.30f, 0.15f);
        private static readonly Color CreateButtonColor = new Color(0.22f, 0.58f, 0.32f);
        private static readonly Color CreateButtonHoverColor = new Color(0.28f, 0.66f, 0.38f);
        private static readonly Color CreateButtonActiveColor = new Color(0.16f, 0.46f, 0.25f);
        private static readonly Color CreateButtonAccentColor = new Color(0.50f, 0.88f, 0.58f);
        private static readonly Color CreateButtonDisabledBorderColor = new Color(0.14f, 0.14f, 0.14f);
        private static readonly Color CreateButtonDisabledColor = new Color(0.24f, 0.24f, 0.24f);
        private static readonly Color CreateButtonDisabledAccentColor = new Color(0.36f, 0.36f, 0.36f);
        private static readonly Color CreateButtonHighlightColor = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color CreateButtonShadowColor = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color AlertWarningBackgroundColor = new Color(0.42f, 0.25f, 0.27f);
        private static readonly Color AlertWarningBorderColor = new Color(0.78f, 0.38f, 0.36f);
        private static readonly Color AlertWarningAccentColor = new Color(0.95f, 0.52f, 0.44f);
        private static readonly Color AlertWarningSeparatorColor = new Color(0.88f, 0.48f, 0.44f, 0.32f);
        private const float StatusBadgeWidth = 112f;
        private const float RequirementBadgeWidth = 76f;
        private const float BadgeSpacing = 6f;
        private const float PreviewLabelWidth = 124f;
        private const float PreviewLabelGap = 8f;
        private const float PreviewLeftInset = 4f;
        private const float PreviewRowHeight = 19f;
        private const float PipelineRowBadgeWidth = 68f;
        private const float PipelineRowLabelWidth = 92f;
        private const float PipelineRowGap = 8f;
        private const float AlertPadding = 8f;
        private const float AlertIconColumnWidth = 38f;
        private const float AlertIconSize = 28f;
        private const float AlertTitleHeight = 18f;
        private const float AlertRowHeight = 19f;
        private const float AlertPathLabelWidth = 112f;
        private const float AlertPathGap = 8f;
        
        private static readonly GUIContent _headerContent = new GUIContent("UIWindow Creator");
        private static readonly GUIContent _namespaceLabel = new GUIContent("Namespace (Optional)");
        private static readonly GUIContent _windowNameLabel = new GUIContent("Window Name");
        private static readonly GUIContent _scriptPathLabel = new GUIContent("Script Save Path");
        private static readonly GUIContent _prefabPathLabel = new GUIContent("Prefab Save Path");
        private static readonly GUIContent _configPathLabel = new GUIContent("Configuration Save Path");
        private static readonly GUIContent _presenterPathLabel = new GUIContent("Presenter Save Path");
        private static readonly GUIContent _layerLabel = new GUIContent("UILayer Configuration");
        private static readonly GUIContent _mvpLabel = new GUIContent("Use MVP Pattern");
        private static readonly GUIContent _mvpToggleLabel = new GUIContent("Generate MVP Structure");
        private static readonly GUIContent _templateLabel = new GUIContent("Template Prefab");
        private static readonly GUIContent _createButtonContent = new GUIContent("Create UIWindow");
        private static readonly GUIContent _autoFillAssetRefLabel = new GUIContent(
            "Auto Fill AssetRef From Prefab Path",
            "Writes the generated prefab asset path and GUID into AssetRef<GameObject>.");
        private static readonly GUIContent _autoFillPathLocationLabel = new GUIContent(
            "Auto Fill PathLocation From Prefab Path",
            "Writes the generated prefab asset path into the plain location string.");

        private readonly StringBuilder _pathBuilder = new StringBuilder(256);
        private static readonly GUIContent _scratchContent = new GUIContent();
        
        private readonly System.Collections.Generic.List<string> _validationErrors = new System.Collections.Generic.List<string>(8);
        private readonly System.Collections.Generic.List<string> _existingFileLabels = new System.Collections.Generic.List<string>(8);
        private readonly System.Collections.Generic.List<string> _existingFilePaths = new System.Collections.Generic.List<string>(8);
        private readonly UIWindowTemplateProcessor _templateProcessor = new UIWindowTemplateProcessor();

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };

            _subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };

            _cardTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };

            _helpTextStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };

            _previewLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };

            _previewKeyStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            Color alertTitleColor = EditorGUIUtility.isProSkin ? new Color(1f, 0.88f, 0.86f) : new Color(0.42f, 0.06f, 0.06f);
            Color alertBodyColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.82f, 0.80f) : new Color(0.36f, 0.08f, 0.08f);

            _alertTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = alertTitleColor }
            };

            _alertBodyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = alertBodyColor }
            };

            _alertPathLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = alertTitleColor }
            };

            _alertPathValueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = alertBodyColor }
            };

            _alertIconFallbackStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = AlertWarningAccentColor }
            };

            _createButtonStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            _createButtonSubStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.86f, 0.96f, 0.88f) }
            };

            _createButtonDisabledStyle = new GUIStyle(_createButtonStyle);
            _createButtonDisabledStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

            _createButtonDisabledSubStyle = new GUIStyle(_createButtonSubStyle);
            _createButtonDisabledSubStyle.normal.textColor = new Color(0.62f, 0.62f, 0.62f);

            _warningIconContent = EditorGUIUtility.IconContent("console.warnicon");
        }

        [MenuItem("Tools/CycloneGames/UI Framework/UIWindow Creator")]
        public static void ShowWindow()
        {
            var window = GetWindow<UIWindowCreatorWindow>("UIWindow Creator");
            window.minSize = new Vector2(500, 700);
            window.Show();
        }

        private void OnEnable()
        {
            string userSettingsDir = Path.Combine(Application.dataPath, "..", "UserSettings");
            if (!Directory.Exists(userSettingsDir))
            {
                Directory.CreateDirectory(userSettingsDir);
            }
            settingsPath = Path.Combine(userSettingsDir, SETTINGS_FILE_NAME);

            LoadSettings();
            LoadEditorPrefsSelection();

            // Try to load default template using GUID (works across different project structures)
            string templatePath = AssetDatabase.GUIDToAssetPath(DEFAULT_TEMPLATE_GUID);
            if (!string.IsNullOrEmpty(templatePath))
            {
                templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            }

            // If GUID lookup fails, try to find by name in the package
            if (templatePrefab == null)
            {
                string[] guids = AssetDatabase.FindAssets("UIWindow_TEMPLATED t:GameObject");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("CycloneGames.UIFramework") && path.EndsWith("UIWindow_TEMPLATED.prefab"))
                    {
                        templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (templatePrefab != null) break;
                    }
                }
            }
        }

        private void OnDisable()
        {
            SaveSettings();
        }

        private void LoadSettings()
        {
            if (File.Exists(settingsPath))
            {
                try
                {
                    string json = FileUtility.ReadAllText(settingsPath);
                    UIWindowCreatorSettings settings = JsonUtility.FromJson<UIWindowCreatorSettings>(json);

                    if (settings != null)
                    {
                        namespaceName = settings.namespaceName ?? "";
                        useMVP = settings.useMVP;
                        configSourceMode = (UIWindowConfiguration.PrefabSource)settings.configSourceMode;
                        autoFillLocationFromPrefabPath = settings.autoFillLocationFromPrefabPath;
                        if (!Enum.IsDefined(typeof(UIWindowConfiguration.PrefabSource), configSourceMode))
                        {
                            configSourceMode = UIWindowConfiguration.PrefabSource.PrefabReference;
                        }

                        if (!string.IsNullOrEmpty(settings.scriptFolderPath))
                        {
                            scriptFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(settings.scriptFolderPath);
                        }
                        if (!string.IsNullOrEmpty(settings.prefabFolderPath))
                        {
                            prefabFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(settings.prefabFolderPath);
                        }
                        if (!string.IsNullOrEmpty(settings.configFolderPath))
                        {
                            soFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(settings.configFolderPath);
                        }
                        if (!string.IsNullOrEmpty(settings.presenterFolderPath))
                        {
                            presenterFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(settings.presenterFolderPath);
                        }
                    }
                }
                catch (Exception e)
                {
                    CLogger.LogWarning($"Failed to load UIWindowCreator settings: {e.Message}", LOG_CATEGORY);
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                UIWindowCreatorSettings settings = new UIWindowCreatorSettings
                {
                    namespaceName = namespaceName ?? "",
                    scriptFolderPath = scriptFolder != null ? AssetDatabase.GetAssetPath(scriptFolder) : "",
                    prefabFolderPath = prefabFolder != null ? AssetDatabase.GetAssetPath(prefabFolder) : "",
                    configFolderPath = soFolder != null ? AssetDatabase.GetAssetPath(soFolder) : "",
                    presenterFolderPath = presenterFolder != null ? AssetDatabase.GetAssetPath(presenterFolder) : "",
                    useMVP = useMVP,
                    configSourceMode = (int)configSourceMode,
                    autoFillLocationFromPrefabPath = autoFillLocationFromPrefabPath
                };

                string json = JsonUtility.ToJson(settings, true);
                FileUtility.WriteAllText(settingsPath, json);
                SaveEditorPrefsSelection();
            }
            catch (Exception e)
            {
                CLogger.LogWarning($"Failed to save UIWindowCreator settings: {e.Message}", LOG_CATEGORY);
            }
        }

        private void LoadEditorPrefsSelection()
        {
            if (EditorPrefs.HasKey(PREFS_KEY_SOURCE_MODE))
            {
                var savedSource = (UIWindowConfiguration.PrefabSource)EditorPrefs.GetInt(
                    PREFS_KEY_SOURCE_MODE,
                    (int)UIWindowConfiguration.PrefabSource.PrefabReference);
                if (Enum.IsDefined(typeof(UIWindowConfiguration.PrefabSource), savedSource))
                {
                    configSourceMode = savedSource;
                }
            }

            if (EditorPrefs.HasKey(PREFS_KEY_AUTOFILL_LOCATION))
            {
                autoFillLocationFromPrefabPath = EditorPrefs.GetBool(PREFS_KEY_AUTOFILL_LOCATION, true);
            }
        }

        private void SaveEditorPrefsSelection()
        {
            EditorPrefs.SetInt(PREFS_KEY_SOURCE_MODE, (int)configSourceMode);
            EditorPrefs.SetBool(PREFS_KEY_AUTOFILL_LOCATION, autoFillLocationFromPrefabPath);
        }

        private void OnGUI()
        {
            InitializeStyles();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawHero();
            DrawPipelineSummary();
            DrawBasicInformationSection();
            DrawSavePathsSection();
            DrawConfigurationSection();
            DrawMvpSection();
            DrawTemplateSection();

            bool canCreate = CanCreate();
            DrawCreationReview(canCreate);

            EditorGUILayout.EndScrollView();
        }

        private void DrawHero()
        {
            EditorGUILayout.Space(8f);
            Rect heroRect = EditorGUILayout.GetControlRect(false, 64f);
            EditorGUI.DrawRect(heroRect, EditorGUIUtility.isProSkin ? new Color(0.18f, 0.20f, 0.22f) : new Color(0.82f, 0.86f, 0.90f));
            EditorGUI.DrawRect(new Rect(heroRect.x, heroRect.y, 4f, heroRect.height), SectionBasicColor);

            Rect titleRect = new Rect(heroRect.x + 14f, heroRect.y + 9f, heroRect.width - 28f, 22f);
            Rect subtitleRect = new Rect(heroRect.x + 14f, heroRect.y + 33f, heroRect.width - 28f, 22f);
            EditorGUI.LabelField(titleRect, _headerContent, _headerStyle);
            EditorGUI.LabelField(subtitleRect, "Create scripts, prefab, configuration, template title, and optional MVP files in one pipeline.", _subtitleStyle);
            EditorGUILayout.Space(8f);
        }

        private void DrawPipelineSummary()
        {
            UIWindowCreationRequest request = BuildCreationRequest();
            bool foldersReady = AreRequiredFoldersReady();
            bool hasExistingFiles = UIWindowCreationValidator.HasExistingFiles(request);
            PipelineStatus pipelineStatus = GetPipelineStatus(request, hasExistingFiles);
            bool windowNameReady = !string.IsNullOrEmpty(request.WindowName) &&
                                   UIWindowCreationValidator.IsValidCSharpIdentifier(request.WindowName);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("Pipeline Status", GetPipelineStatusLabel(pipelineStatus), GetPipelineStatusColor(pipelineStatus));
            DrawStatusRow(
                "Window",
                string.IsNullOrEmpty(request.WindowName) ? "Missing window class name" : request.WindowName,
                windowNameReady ? "OK" : string.IsNullOrEmpty(request.WindowName) ? "Need" : "Fix",
                windowNameReady ? ReadyColor : string.IsNullOrEmpty(request.WindowName) ? MissingColor : InvalidColor);
            DrawStatusRow("Folders", foldersReady ? GetFoldersSummary() : "Select required Project folders", foldersReady);
            DrawStatusRow("Layer", selectedLayer != null ? selectedLayer.LayerName : "Select UILayerConfiguration", selectedLayer != null);
            DrawStatusRow("Source", GetSourceModeSummary(), true);
            DrawOutputStatusRow(request, hasExistingFiles);
            DrawStatusRow("MVP", useMVP ? "View interface and Presenter will be generated" : "Classic UIWindow only", true);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawBasicInformationSection()
        {
            DrawSectionHeader("Basic Information", "Name the generated class and choose an optional namespace.", SectionBasicColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("Namespace", GetNamespaceStatusLabel(), GetNamespaceStatusColor(), false);
            EditorGUILayout.LabelField("Leave empty for the global namespace. Example: MyGame.UI.Windows", _helpTextStyle);
            string newNamespace = EditorGUILayout.TextField(namespaceName);
            if (newNamespace != namespaceName)
            {
                namespaceName = newNamespace;
                SaveSettings();
            }
            if (!string.IsNullOrEmpty(namespaceName))
            {
                DrawPreviewRow("Namespace", namespaceName);
            }
            if (!string.IsNullOrEmpty(namespaceName) && !UIWindowCreationValidator.IsValidNamespace(namespaceName.Trim()))
            {
                DrawAlertBox("Invalid namespace", "Use dot-separated C# identifiers, for example MyGame.UI.Windows.");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("Window Name", GetWindowNameStatusLabel(), GetWindowNameStatusColor(), true);
            EditorGUILayout.LabelField("Use a valid C# class name. This name drives the script, prefab, config, and MVP file names.", _helpTextStyle);
            string newWindowName = EditorGUILayout.TextField(windowName);
            if (newWindowName != windowName)
            {
                windowName = newWindowName;
            }
            if (!string.IsNullOrEmpty(windowName))
            {
                DrawPreviewRow("Class", windowName.Trim());
                DrawPreviewRow("Prefab", windowName.Trim() + ".prefab");
                DrawPreviewRow("Config", windowName.Trim() + "_Config.asset");
            }
            if (!string.IsNullOrEmpty(windowName) && !UIWindowCreationValidator.IsValidCSharpIdentifier(windowName.Trim()))
            {
                DrawAlertBox("Invalid window name", "Use a valid C# class name. It must start with a letter or underscore and contain only letters, digits, or underscores.");
            }
            CheckAndDisplayExistingFiles();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawSavePathsSection()
        {
            DrawSectionHeader("Save Paths", "Drop Project folders into each field. Generated assets are previewed below each path.", SectionPathColor);

            DefaultAsset newScriptFolder = DrawFolderCard(
                "Script Save Path",
                "Folder for the generated UIWindow script.",
                scriptFolder,
                GetWindowFileName(".cs"),
                true);
            if (newScriptFolder != scriptFolder)
            {
                scriptFolder = newScriptFolder;
                SaveSettings();
            }

            DefaultAsset newPrefabFolder = DrawFolderCard(
                "Prefab Save Path",
                "Folder for the generated UIWindow prefab.",
                prefabFolder,
                GetWindowFileName(".prefab"),
                true);
            if (newPrefabFolder != prefabFolder)
            {
                prefabFolder = newPrefabFolder;
                SaveSettings();
            }

            DefaultAsset newSoFolder = DrawFolderCard(
                "Configuration Save Path",
                "Folder for the generated UIWindowConfiguration asset.",
                soFolder,
                GetWindowConfigFileName(),
                true);
            if (newSoFolder != soFolder)
            {
                soFolder = newSoFolder;
                SaveSettings();
            }

            EditorGUILayout.HelpBox("Config files use the '_Config' suffix to avoid YooAsset Location conflicts with same-named prefabs.", MessageType.Info);
            EditorGUILayout.Space(8f);
        }

        private void DrawConfigurationSection()
        {
            DrawSectionHeader("Configuration", "Choose the runtime layer and how the window prefab will be referenced.", SectionConfigColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("UILayer Configuration", selectedLayer != null ? "Ready" : "Missing", selectedLayer != null ? ReadyColor : MissingColor, true);
            EditorGUILayout.LabelField("Select the UILayerConfiguration that decides where this window is attached at runtime.", _helpTextStyle);
            UILayerConfiguration newLayer = EditorGUILayout.ObjectField(selectedLayer, typeof(UILayerConfiguration), false) as UILayerConfiguration;
            if (newLayer != selectedLayer)
            {
                selectedLayer = newLayer;
            }
            if (selectedLayer != null)
            {
                DrawPreviewRow("Layer", selectedLayer.LayerName);
                DrawPreviewRow("Asset", AssetDatabase.GetAssetPath(selectedLayer));
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("UIWindow Source Mode", configSourceMode.ToString(), ReadyColor, true);
            EditorGUILayout.LabelField("Choose how UIWindowConfiguration stores the prefab reference after creation.", _helpTextStyle);
            var newSourceMode = (UIWindowConfiguration.PrefabSource)EditorGUILayout.EnumPopup(configSourceMode);
            if (newSourceMode != configSourceMode)
            {
                configSourceMode = newSourceMode;
                SaveSettings();
            }

            if (UsesLocationSource(configSourceMode))
            {
                GUIContent autoFillLabel = configSourceMode == UIWindowConfiguration.PrefabSource.AssetReference
                    ? _autoFillAssetRefLabel
                    : _autoFillPathLocationLabel;
                bool newAutoFill = EditorGUILayout.ToggleLeft(autoFillLabel, autoFillLocationFromPrefabPath);
                if (newAutoFill != autoFillLocationFromPrefabPath)
                {
                    autoFillLocationFromPrefabPath = newAutoFill;
                    SaveSettings();
                }
            }
            else
            {
                DrawPreviewRow("Location autofill", "Not used by PrefabReference");
            }

            DrawPreviewRow("Runtime contract", GetSourceModeSummary());
            DrawCreatorPerformanceGuidance();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawMvpSection()
        {
            DrawSectionHeader("MVP Architecture", "Optional View interface and Presenter generation for automatic MVP binding.", SectionMvpColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("MVP Pattern", useMVP ? "Enabled" : "Off", useMVP ? ReadyColor : OptionalColor, false);
            EditorGUILayout.LabelField("Enable this when the window should be driven by a Presenter and a typed View interface.", _helpTextStyle);

            bool newUseMVP = EditorGUILayout.ToggleLeft("Generate MVP Structure", useMVP);
            if (newUseMVP != useMVP)
            {
                useMVP = newUseMVP;
                SaveSettings();
            }

            if (useMVP)
            {
                DrawPreviewRow("View interface", "I" + GetSafeWindowName("UIWindow_New") + "View.cs");
                DrawPreviewRow("Presenter", GetSafeWindowName("UIWindow_New") + "Presenter.cs");
                DrawPreviewRow("Binding", "[UIPresenterBind(typeof(" + GetSafeWindowName("UIWindow_New") + "))]");
            }
            else
            {
                EditorGUILayout.HelpBox("MVP is optional. Classic UIWindow scripts are still supported.", MessageType.None);
            }
            EditorGUILayout.EndVertical();

            if (useMVP)
            {
                DefaultAsset newPresenterFolder = DrawFolderCard(
                    "Presenter Save Path",
                    "Folder for the generated Presenter script. It can be the same as the script folder.",
                    presenterFolder,
                    GetSafeWindowName("UIWindow_New") + "Presenter.cs",
                    true);
                if (newPresenterFolder != presenterFolder)
                {
                    presenterFolder = newPresenterFolder;
                    SaveSettings();
                }
            }

            EditorGUILayout.Space(8f);
        }

        private void DrawTemplateSection()
        {
            DrawSectionHeader("Template", "Optional prefab template. The creator can update the title text and remove template UIWindow scripts.", SectionTemplateColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("Template Prefab", GetTemplateStatusLabel(), GetTemplateStatusColor(), false);
            EditorGUILayout.LabelField("Drop a prefab here to clone its layout. If empty, the creator builds a clean RectTransform root.", _helpTextStyle);
            GameObject newTemplatePrefab = EditorGUILayout.ObjectField(templatePrefab, typeof(GameObject), false) as GameObject;
            if (newTemplatePrefab != templatePrefab)
            {
                templatePrefab = newTemplatePrefab;
            }

            if (templatePrefab == null)
            {
                EditorGUILayout.HelpBox("No template selected. A minimal RectTransform prefab will be created.", MessageType.None);
            }
            else
            {
                string templatePath = AssetDatabase.GetAssetPath(templatePrefab);
                PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(templatePrefab);
                DrawPreviewRow("Asset", templatePath);
                DrawPreviewRow("Prefab type", prefabType.ToString());
                if (prefabType == PrefabAssetType.NotAPrefab)
                {
                    DrawAlertBox("Invalid template prefab", "Select a prefab asset from the Project window.");
                }
                else
                {
                    EditorGUILayout.HelpBox("The template will be cloned, unpacked, renamed, and processed for the new window.", MessageType.Info);
                }
            }

            DrawTemplateAuditSummary();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawCreationReview(bool canCreate)
        {
            DrawSectionHeader("Review", "Confirm generated assets before running the pipeline.", SectionReviewColor);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCardHeader("Create UIWindow", canCreate ? "Ready" : "Blocked", canCreate ? ReadyColor : MissingColor);

            UIWindowCreationRequest request = BuildCreationRequest();
            if (UIWindowCreationValidator.TryBuildPaths(request, out UIWindowCreationPaths paths, out _))
            {
                DrawPreviewRow("Script", paths.ScriptFilePath);
                DrawPreviewRow("Prefab", paths.PrefabFilePath);
                DrawPreviewRow("Config", paths.ConfigFilePath);
                if (request.UseMvp)
                {
                    DrawPreviewRow("View", paths.ViewInterfaceFilePath);
                    DrawPreviewRow("Presenter", paths.PresenterFilePath);
                }
            }

            CheckAndDisplayExistingFiles();

            DrawCreateButton(canCreate);

            if (!canCreate)
            {
                DrawAlertBox("Creation is blocked", "Resolve missing required fields, invalid names, or file conflicts before running the pipeline.");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCreateButton(bool canCreate)
        {
            Rect buttonRect = EditorGUILayout.GetControlRect(false, 48f);
            bool isHover = canCreate && buttonRect.Contains(Event.current.mousePosition);
            bool isActive = isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0;

            if (canCreate)
            {
                EditorGUIUtility.AddCursorRect(buttonRect, MouseCursor.Link);
                EditorGUI.DrawRect(buttonRect, CreateButtonBorderColor);
                Rect innerRect = new Rect(buttonRect.x + 1f, buttonRect.y + 1f, buttonRect.width - 2f, buttonRect.height - 2f);
                Color fillColor = isActive ? CreateButtonActiveColor : isHover ? CreateButtonHoverColor : CreateButtonColor;
                EditorGUI.DrawRect(innerRect, fillColor);
                EditorGUI.DrawRect(new Rect(innerRect.x, innerRect.y, 4f, innerRect.height), CreateButtonAccentColor);
                EditorGUI.DrawRect(new Rect(innerRect.x, innerRect.y, innerRect.width, 1f), CreateButtonHighlightColor);
                EditorGUI.DrawRect(new Rect(innerRect.x, innerRect.yMax - 1f, innerRect.width, 1f), CreateButtonShadowColor);
            }
            else
            {
                EditorGUI.DrawRect(buttonRect, CreateButtonDisabledBorderColor);
                Rect innerRect = new Rect(buttonRect.x + 1f, buttonRect.y + 1f, buttonRect.width - 2f, buttonRect.height - 2f);
                EditorGUI.DrawRect(innerRect, CreateButtonDisabledColor);
                EditorGUI.DrawRect(new Rect(innerRect.x, innerRect.y, 4f, innerRect.height), CreateButtonDisabledAccentColor);
            }

            Rect titleRect = new Rect(buttonRect.x, buttonRect.y + 7f, buttonRect.width, 18f);
            Rect subtitleRect = new Rect(buttonRect.x, buttonRect.y + 27f, buttonRect.width, 14f);
            EditorGUI.LabelField(titleRect, "Create " + GetSafeWindowName("UIWindow"), canCreate ? _createButtonStyle : _createButtonDisabledStyle);
            EditorGUI.LabelField(subtitleRect, canCreate ? "Generate script, prefab, and configuration" : "Complete required fields to enable creation", canCreate ? _createButtonSubStyle : _createButtonDisabledSubStyle);

            if (canCreate && GUI.Button(buttonRect, GUIContent.none, GUIStyle.none))
            {
                CreateUIWindow();
            }
        }

        private void DrawSectionHeader(string title, string subtitle, Color color)
        {
            EditorGUILayout.Space(4f);
            Rect rect = EditorGUILayout.GetControlRect(false, 28f);
            EditorGUI.DrawRect(rect, color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Color.black * 0.18f);
            EditorGUI.LabelField(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 18f), title, _sectionStyle);
            if (!string.IsNullOrEmpty(subtitle))
            {
                EditorGUILayout.LabelField(subtitle, _subtitleStyle);
            }
            EditorGUILayout.Space(3f);
        }

        private void DrawCardHeader(string title, string statusLabel, Color statusColor)
        {
            DrawCardHeader(title, statusLabel, statusColor, false, false);
        }

        private void DrawCardHeader(string title, string statusLabel, Color statusColor, bool required)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            DrawCardHeader(title, statusLabel, statusColor, true, required, rect);
        }

        private void DrawCardHeader(string title, string statusLabel, Color statusColor, bool showRequirement, bool required)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            DrawCardHeader(title, statusLabel, statusColor, showRequirement, required, rect);
        }

        private void DrawCardHeader(string title, string statusLabel, Color statusColor, bool showRequirement, bool required, Rect rect)
        {
            float rightWidth = StatusBadgeWidth;
            if (showRequirement)
            {
                rightWidth += RequirementBadgeWidth + BadgeSpacing;
            }

            EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width - rightWidth - 8f, rect.height), title, _cardTitleStyle);

            Rect statusRect = new Rect(rect.xMax - StatusBadgeWidth, rect.y + 2f, StatusBadgeWidth, rect.height - 4f);
            if (showRequirement)
            {
                Rect requirementRect = new Rect(
                    statusRect.x - RequirementBadgeWidth - BadgeSpacing,
                    rect.y + 2f,
                    RequirementBadgeWidth,
                    rect.height - 4f);
                DrawBadge(requirementRect, required ? "Required" : "Optional", required ? RequiredColor : OptionalColor);
            }

            DrawBadge(statusRect, statusLabel, statusColor);
        }

        private void DrawBadge(Rect rect, string label, Color color)
        {
            EditorGUI.DrawRect(rect, color);
            EditorGUI.LabelField(rect, label, _badgeStyle);
        }

        private void DrawStatusRow(string label, string value, bool ready)
        {
            DrawStatusRow(label, value, ready ? "OK" : "Need", ready ? ReadyColor : MissingColor);
        }

        private void DrawStatusRow(string label, string value, string statusLabel, Color statusColor)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            float labelX = rect.x + PipelineRowBadgeWidth + PipelineRowGap;
            float valueX = labelX + PipelineRowLabelWidth + PipelineRowGap;
            DrawBadge(new Rect(rect.x, rect.y + 2f, PipelineRowBadgeWidth, rect.height - 4f), statusLabel, statusColor);
            EditorGUI.LabelField(new Rect(labelX, rect.y, PipelineRowLabelWidth, rect.height), label, EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(new Rect(valueX, rect.y, Mathf.Max(32f, rect.xMax - valueX), rect.height), value, _previewLabelStyle);
        }

        private void DrawOutputStatusRow(UIWindowCreationRequest request, bool hasExistingFiles)
        {
            string statusLabel = GetOutputStatusLabel(request, hasExistingFiles);
            Color statusColor = GetOutputStatusColor(request, hasExistingFiles);
            DrawStatusRow("Output", GetOutputStatusText(request, hasExistingFiles), statusLabel, statusColor);
        }

        private void DrawPreviewRow(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                value = "-";
            }

            Rect rect = EditorGUILayout.GetControlRect(false, PreviewRowHeight);
            float labelWidth = Mathf.Min(PreviewLabelWidth, Mathf.Max(72f, rect.width * 0.42f));
            float labelX = rect.x + PreviewLeftInset;
            float valueX = labelX + labelWidth + PreviewLabelGap;
            float valueWidth = Mathf.Max(32f, rect.xMax - valueX);
            EditorGUI.LabelField(new Rect(labelX, rect.y, labelWidth, rect.height), label, _previewKeyStyle);
            EditorGUI.LabelField(new Rect(valueX, rect.y, valueWidth, rect.height), value, _previewLabelStyle);
        }

        private void DrawAlertBox(string title, string description)
        {
            DrawAlertBox(title, description, null, null);
        }

        private void DrawAlertBox(
            string title,
            string description,
            System.Collections.Generic.List<string> labels,
            System.Collections.Generic.List<string> paths)
        {
            int rowCount = labels != null && paths != null ? Mathf.Min(labels.Count, paths.Count) : 0;
            float availableWidth = Mathf.Max(260f, position.width - 34f);
            float textWidth = Mathf.Max(120f, availableWidth - AlertIconColumnWidth - AlertPadding * 2f);
            float descriptionHeight = string.IsNullOrEmpty(description)
                ? 0f
                : Mathf.Max(AlertRowHeight, CalculateTextHeight(_alertBodyStyle, description, textWidth));
            float rowsHeight = rowCount > 0 ? 6f + rowCount * AlertRowHeight : 0f;
            float contentHeight = AlertTitleHeight + (descriptionHeight > 0f ? descriptionHeight + 4f : 0f) + rowsHeight;
            float height = Mathf.Ceil(Mathf.Max(AlertIconSize, contentHeight) + AlertPadding * 2f + 2f);

            Rect outerRect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.DrawRect(outerRect, GetAlertWarningBorderColor());

            Rect innerRect = new Rect(outerRect.x + 1f, outerRect.y + 1f, outerRect.width - 2f, outerRect.height - 2f);
            EditorGUI.DrawRect(innerRect, GetAlertWarningBackgroundColor());
            EditorGUI.DrawRect(new Rect(innerRect.x, innerRect.y, 4f, innerRect.height), AlertWarningAccentColor);

            Rect iconRect = new Rect(innerRect.x + AlertPadding, innerRect.y + AlertPadding, AlertIconSize, AlertIconSize);
            if (_warningIconContent != null && _warningIconContent.image != null)
            {
                GUI.DrawTexture(iconRect, _warningIconContent.image, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUI.LabelField(iconRect, "!", _alertIconFallbackStyle);
            }

            float textX = innerRect.x + AlertIconColumnWidth;
            float textRight = innerRect.xMax - AlertPadding;
            float y = innerRect.y + AlertPadding;

            EditorGUI.LabelField(new Rect(textX, y, textRight - textX, AlertTitleHeight), title, _alertTitleStyle);
            y += AlertTitleHeight;

            if (!string.IsNullOrEmpty(description))
            {
                Rect descriptionRect = new Rect(textX, y, textRight - textX, descriptionHeight);
                EditorGUI.LabelField(descriptionRect, description, _alertBodyStyle);
                y += descriptionHeight + 4f;
            }

            if (rowCount > 0)
            {
                EditorGUI.DrawRect(new Rect(textX, y + 1f, textRight - textX, 1f), GetAlertWarningSeparatorColor());
                y += 6f;

                for (int i = 0; i < rowCount; i++)
                {
                    DrawAlertPathRow(new Rect(textX, y, textRight - textX, AlertRowHeight), labels[i], paths[i]);
                    y += AlertRowHeight;
                }
            }

            EditorGUILayout.Space(4f);
        }

        private static void DrawAlertPathRow(Rect rect, string label, string path)
        {
            float labelWidth = Mathf.Min(AlertPathLabelWidth, Mathf.Max(72f, rect.width * 0.36f));
            float valueX = rect.x + labelWidth + AlertPathGap;
            float valueWidth = Mathf.Max(32f, rect.xMax - valueX);
            EditorGUI.LabelField(new Rect(rect.x, rect.y, labelWidth, rect.height), label, _alertPathLabelStyle);
            EditorGUI.LabelField(new Rect(valueX, rect.y, valueWidth, rect.height), path, _alertPathValueStyle);
        }

        private static float CalculateTextHeight(GUIStyle style, string text, float width)
        {
            _scratchContent.text = text;
            float height = Mathf.Ceil(style.CalcHeight(_scratchContent, width));
            _scratchContent.text = string.Empty;
            return height;
        }

        private static Color GetAlertWarningBackgroundColor()
        {
            return EditorGUIUtility.isProSkin ? AlertWarningBackgroundColor : new Color(1f, 0.88f, 0.88f);
        }

        private static Color GetAlertWarningBorderColor()
        {
            return EditorGUIUtility.isProSkin ? AlertWarningBorderColor : new Color(0.82f, 0.36f, 0.34f);
        }

        private static Color GetAlertWarningSeparatorColor()
        {
            return EditorGUIUtility.isProSkin ? AlertWarningSeparatorColor : new Color(0.82f, 0.36f, 0.34f, 0.36f);
        }

        private DefaultAsset DrawFolderCard(string title, string description, DefaultAsset folder, string fileName, bool required)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            string statusLabel = GetFolderStatusLabel(folder, required);
            Color statusColor = GetFolderStatusColor(folder, required);
            DrawCardHeader(title, statusLabel, statusColor, required);
            EditorGUILayout.LabelField(description + " Drop a Project folder here or use the object picker.", _helpTextStyle);

            DefaultAsset newFolder = EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false) as DefaultAsset;
            DrawFolderPreview(newFolder, fileName);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
            return newFolder;
        }

        private void DrawFolderPreview(DefaultAsset folder, string fileName)
        {
            if (folder == null)
            {
                DrawPreviewRow("Status", "No folder selected");
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                DrawPreviewRow("Status", "Selected asset is not a folder");
                DrawAlertBox("Invalid save path", "Select a Project folder, not a file asset.");
                return;
            }

            DrawPreviewRow("Folder", folderPath);
            if (!string.IsNullOrEmpty(fileName))
            {
                DrawPreviewRow("Output", folderPath + "/" + fileName);
            }
        }

        private void DrawCreatorPerformanceGuidance()
        {
            if (configSourceMode == UIWindowConfiguration.PrefabSource.PrefabReference)
            {
                EditorGUILayout.HelpBox("Direct Ref is the lowest-friction setup. Best for test scenes, built-in UI, and windows that do not need package-backed loading.", MessageType.None);
            }
            else if (configSourceMode == UIWindowConfiguration.PrefabSource.AssetReference)
            {
                EditorGUILayout.HelpBox("Asset Ref is recommended for Addressables / YooAsset style projects. It aligns best with AssetManagement caching and package ownership.", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Path mode is flexible for custom loaders, but validation depends on your runtime loader contract. Prefer it only when your project intentionally uses path-driven resolution.", MessageType.None);
            }
        }

        private void DrawTemplateAuditSummary()
        {
            if (templatePrefab == null) return;

            var report = UIPerformanceAuditUtility.AuditPrefab(templatePrefab);
            if (report == null) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Template Performance Audit", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Graphics {report.GraphicsCount}  |  Layout {report.LayoutGroupCount}/{report.ContentSizeFitterCount}  |  Masks {report.MaskCount + report.RectMaskCount}  |  Suggested {report.SuggestedSubCanvasPolicy}",
                EditorStyles.miniLabel);

            for (int i = 0; i < report.Issues.Count; i++)
            {
                MessageType type = report.Issues[i].Severity == UIPerformanceAuditUtility.AuditSeverity.Warning
                    ? MessageType.Warning
                    : report.Issues[i].Severity == UIPerformanceAuditUtility.AuditSeverity.Error
                        ? MessageType.Error
                        : MessageType.None;
                EditorGUILayout.HelpBox(report.Issues[i].Message, type);
            }

            Rect buttonRect = EditorGUILayout.GetControlRect(false, 22f);
            buttonRect.xMin = buttonRect.xMax - 176f;
            if (GUI.Button(buttonRect, "Open Performance Auditor"))
            {
                UIPerformanceAuditWindow.ShowWindow();
            }
        }

        private bool AreRequiredFoldersReady()
        {
            return IsValidFolder(scriptFolder) &&
                   IsValidFolder(prefabFolder) &&
                   IsValidFolder(soFolder) &&
                   (!useMVP || IsValidFolder(presenterFolder));
        }

        private static bool IsValidFolder(DefaultAsset folder)
        {
            return folder != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder));
        }

        private string GetFoldersSummary()
        {
            return useMVP
                ? "Script, Prefab, Config, and Presenter folders are selected"
                : "Script, Prefab, and Config folders are selected";
        }

        private static PipelineStatus GetPipelineStatus(UIWindowCreationRequest request, bool hasExistingFiles)
        {
            if (IsPipelineMissingInput(request))
            {
                return PipelineStatus.NeedsInput;
            }

            if (IsPipelineInvalid(request))
            {
                return PipelineStatus.Invalid;
            }

            return hasExistingFiles ? PipelineStatus.Conflict : PipelineStatus.Ready;
        }

        private static bool IsPipelineMissingInput(UIWindowCreationRequest request)
        {
            return request.ScriptFolder == null ||
                   request.ConfigFolder == null ||
                   request.PrefabFolder == null ||
                   request.Layer == null ||
                   string.IsNullOrEmpty(request.WindowName) ||
                   (request.UseMvp && request.PresenterFolder == null);
        }

        private static bool IsPipelineInvalid(UIWindowCreationRequest request)
        {
            if (!UIWindowCreationValidator.IsValidCSharpIdentifier(request.WindowName))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(request.NamespaceName) &&
                !UIWindowCreationValidator.IsValidNamespace(request.NamespaceName))
            {
                return true;
            }

            return !AreOutputFoldersValid(request);
        }

        private static bool HasRequiredOutputInputs(UIWindowCreationRequest request)
        {
            return !string.IsNullOrEmpty(request.WindowName) &&
                   request.ScriptFolder != null &&
                   request.ConfigFolder != null &&
                   request.PrefabFolder != null &&
                   (!request.UseMvp || request.PresenterFolder != null);
        }

        private static bool AreOutputFoldersValid(UIWindowCreationRequest request)
        {
            return IsValidFolder(request.ScriptFolder) &&
                   IsValidFolder(request.PrefabFolder) &&
                   IsValidFolder(request.ConfigFolder) &&
                   (!request.UseMvp || IsValidFolder(request.PresenterFolder));
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
                    return InvalidColor;
                case PipelineStatus.Conflict:
                    return InvalidColor;
                default:
                    return MissingColor;
            }
        }

        private static string GetOutputStatusText(UIWindowCreationRequest request, bool hasExistingFiles)
        {
            if (!HasRequiredOutputInputs(request))
            {
                return "Waiting for window name and output folders";
            }

            if (!UIWindowCreationValidator.IsValidCSharpIdentifier(request.WindowName))
            {
                return "Fix window class name before checking outputs";
            }

            if (!AreOutputFoldersValid(request))
            {
                return "Select valid Project folders";
            }

            return hasExistingFiles ? "Generated asset conflict detected" : "No generated asset conflicts";
        }

        private static string GetOutputStatusLabel(UIWindowCreationRequest request, bool hasExistingFiles)
        {
            if (!HasRequiredOutputInputs(request))
            {
                return "Wait";
            }

            if (!UIWindowCreationValidator.IsValidCSharpIdentifier(request.WindowName) ||
                !AreOutputFoldersValid(request))
            {
                return "Fix";
            }

            return hasExistingFiles ? "Conflict" : "OK";
        }

        private static Color GetOutputStatusColor(UIWindowCreationRequest request, bool hasExistingFiles)
        {
            if (!HasRequiredOutputInputs(request))
            {
                return OptionalColor;
            }

            if (!UIWindowCreationValidator.IsValidCSharpIdentifier(request.WindowName) ||
                !AreOutputFoldersValid(request))
            {
                return InvalidColor;
            }

            return hasExistingFiles ? InvalidColor : ReadyColor;
        }

        private string GetWindowNameStatusLabel()
        {
            if (string.IsNullOrEmpty(windowName)) return "Missing";
            return UIWindowCreationValidator.IsValidCSharpIdentifier(windowName.Trim()) ? "Ready" : "Invalid";
        }

        private Color GetWindowNameStatusColor()
        {
            if (string.IsNullOrEmpty(windowName)) return MissingColor;
            return UIWindowCreationValidator.IsValidCSharpIdentifier(windowName.Trim()) ? ReadyColor : InvalidColor;
        }

        private string GetNamespaceStatusLabel()
        {
            if (string.IsNullOrEmpty(namespaceName)) return "Optional";
            return UIWindowCreationValidator.IsValidNamespace(namespaceName.Trim()) ? "Ready" : "Invalid";
        }

        private Color GetNamespaceStatusColor()
        {
            if (string.IsNullOrEmpty(namespaceName)) return OptionalColor;
            return UIWindowCreationValidator.IsValidNamespace(namespaceName.Trim()) ? ReadyColor : InvalidColor;
        }

        private string GetTemplateStatusLabel()
        {
            if (templatePrefab == null) return "Optional";
            return PrefabUtility.GetPrefabAssetType(templatePrefab) != PrefabAssetType.NotAPrefab ? "Ready" : "Invalid";
        }

        private Color GetTemplateStatusColor()
        {
            if (templatePrefab == null) return OptionalColor;
            return PrefabUtility.GetPrefabAssetType(templatePrefab) != PrefabAssetType.NotAPrefab ? ReadyColor : InvalidColor;
        }

        private static string GetFolderStatusLabel(DefaultAsset folder, bool required)
        {
            if (folder == null) return required ? "Missing" : "Optional";
            return AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder)) ? "Ready" : "Invalid";
        }

        private static Color GetFolderStatusColor(DefaultAsset folder, bool required)
        {
            if (folder == null) return required ? MissingColor : OptionalColor;
            return AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder)) ? ReadyColor : InvalidColor;
        }

        private string GetWindowFileName(string extension)
        {
            return GetSafeWindowName("UIWindow_New") + extension;
        }

        private string GetWindowConfigFileName()
        {
            return GetSafeWindowName("UIWindow_New") + "_Config.asset";
        }

        private string GetSafeWindowName(string fallback)
        {
            string trimmed = windowName != null ? windowName.Trim() : string.Empty;
            return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
        }

        private string GetSourceModeSummary()
        {
            switch (configSourceMode)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    return "Direct UIWindow prefab reference";
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    return autoFillLocationFromPrefabPath
                        ? "AssetRef<GameObject> with prefab path and GUID"
                        : "AssetRef<GameObject> left empty for manual assignment";
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    return autoFillLocationFromPrefabPath
                        ? "Plain location string from prefab path"
                        : "Plain location string left empty for manual assignment";
                default:
                    return configSourceMode.ToString();
            }
        }

        private static bool UsesLocationSource(UIWindowConfiguration.PrefabSource sourceMode)
        {
            return sourceMode == UIWindowConfiguration.PrefabSource.AssetReference ||
                   sourceMode == UIWindowConfiguration.PrefabSource.PathLocation;
        }

        private System.Type GetScriptType(string scriptName, string namespaceName)
        {
            System.Type scriptType = null;
            string fullTypeName = string.IsNullOrEmpty(namespaceName) ? scriptName : $"{namespaceName}.{scriptName}";

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                scriptType = assembly.GetType(fullTypeName);
                if (scriptType != null)
                    break;
            }
            return scriptType;
        }

        private bool CanCreate()
        {
            UIWindowCreationRequest request = BuildCreationRequest();
            return GetPipelineStatus(request, UIWindowCreationValidator.HasExistingFiles(request)) == PipelineStatus.Ready;
        }

        private UIWindowCreationRequest BuildCreationRequest()
        {
            return new UIWindowCreationRequest(
                windowName,
                namespaceName,
                scriptFolder,
                prefabFolder,
                soFolder,
                presenterFolder,
                selectedLayer,
                useMVP,
                configSourceMode,
                autoFillLocationFromPrefabPath);
        }

        private bool HasExistingFiles()
        {
            return UIWindowCreationValidator.HasExistingFiles(BuildCreationRequest());
        }

        private void CheckAndDisplayExistingFiles()
        {
            _existingFileLabels.Clear();
            _existingFilePaths.Clear();

            UIWindowCreationRequest request = BuildCreationRequest();
            if (!UIWindowCreationValidator.TryBuildPaths(request, out UIWindowCreationPaths paths, out _))
            {
                return;
            }

            AddExistingFilePreview("Script", paths.ScriptFilePath);
            AddExistingFilePreview("Prefab", paths.PrefabFilePath);
            AddExistingFilePreview("Config", paths.ConfigFilePath);

            if (request.UseMvp)
            {
                AddExistingFilePreview("View Interface", paths.ViewInterfaceFilePath);
                AddExistingFilePreview("Presenter", paths.PresenterFilePath);
            }

            if (_existingFileLabels.Count > 0)
            {
                DrawAlertBox(
                    "Window output already exists",
                    "The creator will not overwrite these generated assets. Rename the window, delete the old files, or choose different output folders.",
                    _existingFileLabels,
                    _existingFilePaths);
            }
        }

        private void AddExistingFilePreview(string label, string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            _existingFileLabels.Add(label);
            _existingFilePaths.Add(path);
        }

        /// <summary>
        /// Performs comprehensive validation before creating UIWindow.
        /// Checks: folder existence, file conflicts, window name validity.
        /// Returns error message if validation fails, empty string if successful.
        /// </summary>
        private string ValidateBeforeCreate(UIWindowCreationRequest request)
        {
            return UIWindowCreationValidator.Validate(request, _validationErrors);
        }

        private void CreateUIWindow()
        {
            try
            {
                UIWindowCreationRequest request = BuildCreationRequest();
                string validationError = ValidateBeforeCreate(request);
                if (!string.IsNullOrEmpty(validationError))
                {
                    EditorUtility.DisplayDialog("Validation Failed", validationError, "OK");
                    return;
                }

                if (!UIWindowCreationValidator.TryBuildPaths(request, out UIWindowCreationPaths creationPaths, out string pathError))
                {
                    EditorUtility.DisplayDialog("Validation Failed", pathError, "OK");
                    return;
                }

                string scriptName = request.WindowName;
                string scriptNamespace = request.NamespaceName;
                string fullScriptPath = creationPaths.ScriptFilePath;
                string fullPrefabPath = creationPaths.PrefabFilePath;
                string fullSoPath = creationPaths.ConfigFilePath;
                string fullViewInterfacePath = request.UseMvp ? creationPaths.ViewInterfaceFilePath : "";
                string fullPresenterPath = request.UseMvp ? creationPaths.PresenterFilePath : "";

                CLogger.LogInfo($"Creating UIWindow '{scriptName}'. Source={request.SourceMode}, MVP={request.UseMvp}, Prefab='{fullPrefabPath}', Config='{fullSoPath}'.", LOG_CATEGORY);

                CreateScript(fullScriptPath, scriptName, scriptNamespace, request.UseMvp);
                
                if (request.UseMvp)
                {
                    CreateViewInterface(fullViewInterfacePath, scriptName, scriptNamespace);
                    CreatePresenter(fullPresenterPath, scriptName, scriptNamespace);
                }
                
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                GameObject prefabInstance = CreatePrefab(fullPrefabPath, scriptName, null);
                UIWindowConfigurationWriter.Create(
                    fullSoPath,
                    prefabInstance,
                    request.Layer,
                    request.SourceMode,
                    request.AutoFillLocationFromPrefabPath);

                System.Type scriptType = GetScriptType(scriptName, scriptNamespace);

                bool scriptAdded = false;
                if (scriptType != null)
                {
                    scriptAdded = UIWindowPrefabScriptBinder.AddScriptComponentToPrefab(fullPrefabPath, scriptType, scriptName);

                    if (scriptAdded && request.SourceMode == UIWindowConfiguration.PrefabSource.PrefabReference)
                    {
                        UIWindowConfigurationWriter.UpdatePrefabReference(fullSoPath, fullPrefabPath);
                    }
                }
                else
                {
                    UIWindowCreatorPostCompileProcessor.Schedule(scriptName, scriptNamespace, fullPrefabPath, fullSoPath, request.SourceMode);
                }

                AssetDatabase.Refresh();

                string message = $"UIWindow '{windowName}' created successfully!\n\n" +
                               $"Script: {fullScriptPath}\n" +
                               $"Prefab: {fullPrefabPath}\n" +
                               $"Config: {fullSoPath}\n";

                if (request.UseMvp)
                {
                    message += $"\nMVP Files:\n" +
                               $"View Interface: {fullViewInterfacePath}\n" +
                               $"Presenter: {fullPresenterPath}\n";
                }

                message += "\n";

                if (!scriptAdded)
                {
                    message += "Info: Unity is still compiling the generated script.\n" +
                               $"The creator will automatically attach {scriptName} to the prefab after compilation. " +
                               "If it times out, add the component manually from the Inspector.";
                }

                EditorUtility.DisplayDialog("Success", message, "OK");

                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(fullPrefabPath);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create UIWindow: {e.Message}\n\n{e.StackTrace}", "OK");
                CLogger.LogError($"UIWindow Creator Error: {e}", LOG_CATEGORY);
            }
        }

        private void CreateScript(string scriptPath, string className, string namespaceName, bool useMVP = false)
        {
            EnsureDirectoryExists(scriptPath);

            // Generate script content based on MVP mode
            string scriptContent;
            string viewInterface = useMVP ? $"I{className}View" : "";
            string baseClass = "UIWindow";
            string implementsView = useMVP ? $", {viewInterface}" : "";

            if (string.IsNullOrEmpty(namespaceName))
            {
                if (useMVP)
                {
                    scriptContent = $@"using CycloneGames.UIFramework.Runtime;

public class {className} : {baseClass}{implementsView}
{{
    // Add your UI element references here
    // Example:
    // [SerializeField] private Button closeButton;
    
    protected override void Awake()
    {{
        base.Awake();
        // Initialize your UI elements here
    }}

    #region {viewInterface} Implementation

    // TODO: Implement your View interface methods here
    // Example:
    // public void ShowLoading(bool show) {{ loadingPanel.SetActive(show); }}

    #endregion
}}";
                }
                else
                {
                    scriptContent = $@"using CycloneGames.UIFramework.Runtime;

public class {className} : {baseClass}
{{
    // Add your UI element references here
    // Example:
    // [SerializeField] private Button closeButton;
    
    protected override void Awake()
    {{
        base.Awake();
        // Initialize your UI elements here
    }}
}}";
                }
            }
            else
            {
                if (useMVP)
                {
                    scriptContent = $@"using CycloneGames.UIFramework.Runtime;

namespace {namespaceName}
{{
    public class {className} : {baseClass}{implementsView}
    {{
        // Add your UI element references here
        // Example:
        // [SerializeField] private Button closeButton;
        
        protected override void Awake()
        {{
            base.Awake();
            // Initialize your UI elements here
        }}

        #region {viewInterface} Implementation

        // TODO: Implement your View interface methods here
        // Example:
        // public void ShowLoading(bool show) {{ loadingPanel.SetActive(show); }}

        #endregion
    }}
}}";
                }
                else
                {
                    scriptContent = $@"using CycloneGames.UIFramework.Runtime;

namespace {namespaceName}
{{
    public class {className} : {baseClass}
    {{
        // Add your UI element references here
        // Example:
        // [SerializeField] private Button closeButton;
        
        protected override void Awake()
        {{
            base.Awake();
            // Initialize your UI elements here
        }}
    }}
}}";
                }
            }

            WriteScriptFile(scriptPath, scriptContent);
        }

        private void CreateViewInterface(string scriptPath, string className, string namespaceName)
        {
            EnsureDirectoryExists(scriptPath);

            string interfaceName = $"I{className}View";
            string scriptContent;

            if (string.IsNullOrEmpty(namespaceName))
            {
                scriptContent = $@"/// <summary>
/// View interface for {className}.
/// Define all UI-related methods that the Presenter can call.
/// The {className} class implements this interface.
/// </summary>
public interface {interfaceName}
{{
    // TODO: Add your view methods here
    // Example:
    // void ShowLoading(bool show);
    // void SetTitle(string title);
    // void SetButtonInteractable(bool interactable);
}}";
            }
            else
            {
                scriptContent = $@"namespace {namespaceName}
{{
    /// <summary>
    /// View interface for {className}.
    /// Define all UI-related methods that the Presenter can call.
    /// The {className} class implements this interface.
    /// </summary>
    public interface {interfaceName}
    {{
        // TODO: Add your view methods here
        // Example:
        // void ShowLoading(bool show);
        // void SetTitle(string title);
        // void SetButtonInteractable(bool interactable);
    }}
}}";
            }

            WriteScriptFile(scriptPath, scriptContent);
        }

        private void CreatePresenter(string scriptPath, string className, string namespaceName)
        {
            EnsureDirectoryExists(scriptPath);

            string presenterName = $"{className}Presenter";
            string viewInterface = $"I{className}View";
            string scriptContent;

            if (string.IsNullOrEmpty(namespaceName))
            {
                scriptContent = $@"using CycloneGames.UIFramework.Runtime;

/// <summary>
/// Presenter for {className}.
/// Handles business logic and communicates with the View through {viewInterface}.
/// </summary>
[UIPresenterBind(typeof({className}))]
public class {presenterName} : UIPresenter<{viewInterface}>
{{
    protected override void OnViewBound()
    {{
        // Called when View is first bound (during UIWindow.Awake)
        // Use for early initialization
    }}

    public override void OnViewOpening()
    {{
        // Called when window starts opening
        // Use for preparing data or starting loading operations
    }}

    public override void OnViewOpened()
    {{
        // Called when window is fully opened and interactive
        // Use for populating UI with data
    }}

    public override void OnViewClosing()
    {{
        // Called when window starts closing
        // Use for saving state or cancelling ongoing operations
    }}

    public override void OnViewClosed()
    {{
        // Called when window finishes closing
        // Use for final cleanup before destruction
    }}

    public override void Dispose()
    {{
        // Cleanup resources
        base.Dispose();
    }}
}}";
            }
            else
            {
                scriptContent = $@"using CycloneGames.UIFramework.Runtime;

namespace {namespaceName}
{{
    /// <summary>
    /// Presenter for {className}.
    /// Handles business logic and communicates with the View through {viewInterface}.
    /// </summary>
    [UIPresenterBind(typeof({className}))]
    public class {presenterName} : UIPresenter<{viewInterface}>
    {{
        protected override void OnViewBound()
        {{
            // Called when View is first bound (during UIWindow.Awake)
            // Use for early initialization
        }}

        public override void OnViewOpening()
        {{
            // Called when window starts opening
            // Use for preparing data or starting loading operations
        }}

        public override void OnViewOpened()
        {{
            // Called when window is fully opened and interactive
            // Use for populating UI with data
        }}

        public override void OnViewClosing()
        {{
            // Called when window starts closing
            // Use for saving state or cancelling ongoing operations
        }}

        public override void OnViewClosed()
        {{
            // Called when window finishes closing
            // Use for final cleanup before destruction
        }}

        public override void Dispose()
        {{
            // Cleanup resources
            base.Dispose();
        }}
    }}
}}";
            }

            WriteScriptFile(scriptPath, scriptContent);
        }

        private void EnsureDirectoryExists(string scriptPath)
        {
            string directory = Path.GetDirectoryName(scriptPath);
            if (!Directory.Exists(directory))
            {
                string parentDir = Path.GetDirectoryName(directory);
                string dirName = Path.GetFileName(directory);
                if (!string.IsNullOrEmpty(parentDir) && AssetDatabase.IsValidFolder(parentDir))
                {
                    AssetDatabase.CreateFolder(parentDir, dirName);
                }
                else
                {
                    Directory.CreateDirectory(directory);
                }
                AssetDatabase.Refresh();
            }
        }

        private void WriteScriptFile(string scriptPath, string content)
        {
            FileUtility.WriteAllText(scriptPath, content);

            AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            CLogger.LogInfo($"Generated script file '{scriptPath}'.", LOG_CATEGORY);
        }

        private GameObject CreatePrefab(string prefabPath, string scriptName, System.Type scriptType)
        {
            string directory = Path.GetDirectoryName(prefabPath);
            if (!Directory.Exists(directory))
            {
                string parentDir = Path.GetDirectoryName(directory);
                string dirName = Path.GetFileName(directory);
                if (!string.IsNullOrEmpty(parentDir) && AssetDatabase.IsValidFolder(parentDir))
                {
                    AssetDatabase.CreateFolder(parentDir, dirName);
                }
                else
                {
                    Directory.CreateDirectory(directory);
                }
                AssetDatabase.Refresh();
            }

            GameObject prefabInstance;

            if (templatePrefab != null && PrefabUtility.GetPrefabAssetType(templatePrefab) != PrefabAssetType.NotAPrefab)
            {
                prefabInstance = PrefabUtility.InstantiatePrefab(templatePrefab) as GameObject;
                if (prefabInstance == null)
                {
                    throw new InvalidOperationException($"Failed to instantiate template prefab: {AssetDatabase.GetAssetPath(templatePrefab)}");
                }

                prefabInstance.name = scriptName;

                PrefabUtility.UnpackPrefabInstance(prefabInstance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

                _templateProcessor.Process(prefabInstance, scriptName);
            }
            else
            {
                prefabInstance = new GameObject(scriptName);

                RectTransform rectTransform = prefabInstance.AddComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.sizeDelta = Vector2.zero;
                rectTransform.anchoredPosition = Vector2.zero;
            }

            if (scriptType != null)
            {
                if (prefabInstance.GetComponent(scriptType) == null)
                {
                    prefabInstance.AddComponent(scriptType);
                    CLogger.LogInfo($"Added {scriptName} component to prefab instance before saving.", LOG_CATEGORY);
                }
            }
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabInstance, prefabPath);
            if (savedPrefab == null)
            {
                throw new InvalidOperationException($"Failed to save UIWindow prefab at '{prefabPath}'.");
            }

            CLogger.LogInfo($"Saved UIWindow prefab '{prefabPath}'.", LOG_CATEGORY);

            DestroyImmediate(prefabInstance);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);

            savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            return savedPrefab;
        }

    }
}
