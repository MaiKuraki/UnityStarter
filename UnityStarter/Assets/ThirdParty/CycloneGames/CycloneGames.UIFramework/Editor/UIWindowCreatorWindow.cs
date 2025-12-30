using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework.Runtime;
using Unio;
using Unity.Collections;

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
    }

    public class UIWindowCreatorWindow : EditorWindow
    {
        private const string DEFAULT_TEMPLATE_GUID = "37c32b368ca8d4841b923d1b37cf97b9";
        private const string SETTINGS_FILE_NAME = "UIWindowCreatorSettings.json";

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

        private string settingsPath;

        private static bool _stylesInitialized = false;
        private static GUIStyle _headerStyle;
        private static GUIStyle _sectionStyle;
        
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

        private readonly StringBuilder _pathBuilder = new StringBuilder(256);
        
        private readonly System.Collections.Generic.List<string> _validationErrors = new System.Collections.Generic.List<string>(8);
        private readonly System.Collections.Generic.List<string> _existingFilesList = new System.Collections.Generic.List<string>(8);

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
                fontSize = 12
            };
        }

        [MenuItem("Tools/CycloneGames/UIWindow Creator")]
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

            EditorApplication.update -= OnAsyncScriptCheck;
        }

        private void LoadSettings()
        {
            if (File.Exists(settingsPath))
            {
                try
                {
                    string json = NativeFile.ReadAllText(settingsPath);
                    UIWindowCreatorSettings settings = JsonUtility.FromJson<UIWindowCreatorSettings>(json);

                    if (settings != null)
                    {
                        namespaceName = settings.namespaceName ?? "";
                        useMVP = settings.useMVP;

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
                    Debug.LogWarning($"Failed to load UIWindowCreator settings: {e.Message}");
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
                    useMVP = useMVP
                };

                string json = JsonUtility.ToJson(settings, true);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using var nativeBytes = new NativeArray<byte>(bytes, Allocator.Temp);
                NativeFile.WriteAllBytes(settingsPath, nativeBytes);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to save UIWindowCreator settings: {e.Message}");
            }
        }

        private void OnGUI()
        {
            InitializeStyles();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Header
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(_headerContent, _headerStyle);
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("This tool helps you quickly create UIWindow scripts, prefabs, and configurations. Fill in all required fields below.", MessageType.Info);
            EditorGUILayout.Space(15);

            // Section 1: Basic Information
            DrawSectionHeader("Basic Information");

            // Namespace input
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Namespace (Optional)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("The namespace for the generated UIWindow script. Leave empty if you don't want a namespace. Example: MyGame.UI or MyGame.UI.Windows", MessageType.None);
            string newNamespace = EditorGUILayout.TextField(namespaceName);
            if (newNamespace != namespaceName)
            {
                namespaceName = newNamespace;
                SaveSettings();
            }
            if (!string.IsNullOrEmpty(namespaceName))
            {
                EditorGUILayout.LabelField($"Namespace: {namespaceName}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Window Name input
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Window Name", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("The name of the UIWindow class and prefab. Example: UIWindowHUD, HUDWindow, MainMenuWindow", MessageType.None);
            windowName = EditorGUILayout.TextField(windowName);
            if (!string.IsNullOrEmpty(windowName))
            {
                EditorGUILayout.LabelField($"Class Name: {windowName}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Prefab Name: {windowName}.prefab", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Config Name: {windowName}_Config.asset", EditorStyles.miniLabel);

                // Check for existing files
                CheckAndDisplayExistingFiles();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Section 2: Paths
            DrawSectionHeader("Save Paths");

            // Script Folder path
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Script Save Path", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag and drop a folder where the generated UIWindow script (.cs file) will be saved.", MessageType.None);
            DefaultAsset newScriptFolder = EditorGUILayout.ObjectField(scriptFolder, typeof(DefaultAsset), false) as DefaultAsset;
            if (newScriptFolder != scriptFolder)
            {
                scriptFolder = newScriptFolder;
                SaveSettings();
            }
            if (scriptFolder != null)
            {
                string scriptPath = AssetDatabase.GetAssetPath(scriptFolder);
                if (AssetDatabase.IsValidFolder(scriptPath))
                {
                    EditorGUILayout.LabelField($"Path: {scriptPath}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Full Path: {scriptPath}/{windowName}.cs", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("⚠ Selected path is not a valid folder.", MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            // Prefab Folder path
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Prefab Save Path", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag and drop a folder where the generated UIWindow prefab (.prefab file) will be saved.", MessageType.None);
            DefaultAsset newPrefabFolder = EditorGUILayout.ObjectField(prefabFolder, typeof(DefaultAsset), false) as DefaultAsset;
            if (newPrefabFolder != prefabFolder)
            {
                prefabFolder = newPrefabFolder;
                SaveSettings();
            }
            if (prefabFolder != null)
            {
                string prefabPath = AssetDatabase.GetAssetPath(prefabFolder);
                if (AssetDatabase.IsValidFolder(prefabPath))
                {
                    EditorGUILayout.LabelField($"Path: {prefabPath}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Full Path: {prefabPath}/{windowName}.prefab", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("⚠ Selected path is not a valid folder.", MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            // SO Folder path
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Configuration Save Path", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag and drop a folder where the generated UIWindowConfiguration ScriptableObject (.asset file) will be saved.", MessageType.None);
            EditorGUILayout.HelpBox("ℹ Config files use '_Config' suffix to avoid YooAsset Location conflicts. YooAsset uses filename (without extension) as Location key, so same-named Prefab and ScriptableObject would cause 'Location have existed' warnings.", MessageType.Info);
            DefaultAsset newSoFolder = EditorGUILayout.ObjectField(soFolder, typeof(DefaultAsset), false) as DefaultAsset;
            if (newSoFolder != soFolder)
            {
                soFolder = newSoFolder;
                SaveSettings();
            }
            if (soFolder != null)
            {
                string soPath = AssetDatabase.GetAssetPath(soFolder);
                if (AssetDatabase.IsValidFolder(soPath))
                {
                    EditorGUILayout.LabelField($"Path: {soPath}", EditorStyles.miniLabel);
                    string configName = string.IsNullOrEmpty(windowName) ? "UIWindow_New_Config.asset" : $"{windowName}_Config.asset";
                    EditorGUILayout.LabelField($"Full Path: {soPath}/{configName}", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("⚠ Selected path is not a valid folder.", MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Section 3: Configuration
            DrawSectionHeader("Configuration");

            // UILayer Selection
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("UILayer Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select the UILayerConfiguration ScriptableObject that defines which UI layer this window belongs to (e.g., Menu, Dialogue, Notification).", MessageType.None);
            selectedLayer = EditorGUILayout.ObjectField(selectedLayer, typeof(UILayerConfiguration), false) as UILayerConfiguration;
            if (selectedLayer != null)
            {
                EditorGUILayout.LabelField($"Layer Name: {selectedLayer.LayerName}", EditorStyles.miniLabel);
                string layerPath = AssetDatabase.GetAssetPath(selectedLayer);
                EditorGUILayout.LabelField($"Path: {layerPath}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Section 4: MVP Architecture (Optional)
            DrawSectionHeader("MVP Architecture (Optional)");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Use MVP Pattern", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Enable to generate View interface and Presenter class alongside the UIWindow. " +
                "This follows the Model-View-Presenter pattern for better separation of concerns.", MessageType.None);
            
            bool newUseMVP = EditorGUILayout.Toggle("Generate MVP Structure", useMVP);
            if (newUseMVP != useMVP)
            {
                useMVP = newUseMVP;
                SaveSettings();
            }

            if (useMVP)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("MVP will generate:\n" +
                    $"• I{windowName}View - View interface (in Script folder)\n" +
                    $"• {windowName}Presenter - Presenter class (in Presenter folder)\n" +
                    $"• {windowName} will implement I{windowName}View", MessageType.Info);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Presenter Save Path", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Drag and drop a folder where the Presenter script will be saved. Can be same as Script folder.", MessageType.None);
                DefaultAsset newPresenterFolder = EditorGUILayout.ObjectField(presenterFolder, typeof(DefaultAsset), false) as DefaultAsset;
                if (newPresenterFolder != presenterFolder)
                {
                    presenterFolder = newPresenterFolder;
                    SaveSettings();
                }
                if (presenterFolder != null)
                {
                    string presenterPath = AssetDatabase.GetAssetPath(presenterFolder);
                    if (AssetDatabase.IsValidFolder(presenterPath))
                    {
                        EditorGUILayout.LabelField($"Path: {presenterPath}", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"Presenter: {presenterPath}/{windowName}Presenter.cs", EditorStyles.miniLabel);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("⚠ Selected path is not a valid folder.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("⚠ Presenter folder is required when using MVP.", MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Section 5: Template (Optional)
            DrawSectionHeader("Template (Optional)");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Template Prefab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("(Optional) Drag and drop a UIWindow prefab to use as a template. If not provided, a new prefab will be created from scratch with a RectTransform.", MessageType.None);
            templatePrefab = EditorGUILayout.ObjectField(templatePrefab, typeof(GameObject), false) as GameObject;
            if (templatePrefab == null)
            {
                EditorGUILayout.HelpBox("ℹ No template prefab selected. A new prefab will be created from scratch.", MessageType.Info);
            }
            else
            {
                string templatePath = AssetDatabase.GetAssetPath(templatePrefab);
                PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(templatePrefab);
                if (prefabType == PrefabAssetType.NotAPrefab)
                {
                    EditorGUILayout.HelpBox("⚠ Selected object is not a prefab. Please select a prefab asset.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField($"Path: {templatePath}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Type: {prefabType}", EditorStyles.miniLabel);
                    EditorGUILayout.HelpBox("✓ Template prefab will be used as base for the new prefab.", MessageType.Info);
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(15);

            // Create button
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(!CanCreate());
            if (GUILayout.Button("Create UIWindow", GUILayout.Height(35)))
            {
                CreateUIWindow();
            }
            EditorGUI.EndDisabledGroup();

            if (!CanCreate())
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("⚠ Please fill in all required fields to create UIWindow.", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(title, _sectionStyle);
            EditorGUILayout.Space(3);
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
            // Namespace is optional, so we don't check it
            if (scriptFolder == null)
                return false;
            if (soFolder == null)
                return false;
            if (prefabFolder == null)
                return false;
            if (selectedLayer == null)
                return false;
            if (string.IsNullOrEmpty(windowName))
                return false;

            // MVP requires presenter folder
            if (useMVP && presenterFolder == null)
                return false;

            if (!IsValidCSharpIdentifier(windowName))
                return false;

            string scriptPath = scriptFolder != null ? AssetDatabase.GetAssetPath(scriptFolder) : "";
            string soPath = soFolder != null ? AssetDatabase.GetAssetPath(soFolder) : "";
            string prefabPath = prefabFolder != null ? AssetDatabase.GetAssetPath(prefabFolder) : "";
            string presenterPath = presenterFolder != null ? AssetDatabase.GetAssetPath(presenterFolder) : "";

            if (!string.IsNullOrEmpty(scriptPath) && !AssetDatabase.IsValidFolder(scriptPath))
                return false;
            if (!string.IsNullOrEmpty(soPath) && !AssetDatabase.IsValidFolder(soPath))
                return false;
            if (!string.IsNullOrEmpty(prefabPath) && !AssetDatabase.IsValidFolder(prefabPath))
                return false;
            if (useMVP && !string.IsNullOrEmpty(presenterPath) && !AssetDatabase.IsValidFolder(presenterPath))
                return false;

            if (HasExistingFiles())
                return false;

            return true;
        }

        private bool IsValidCSharpIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (!char.IsLetter(name[0]) && name[0] != '_')
                return false;

            for (int i = 1; i < name.Length; i++)
            {
                if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                    return false;
            }

            return true;
        }

        private bool HasExistingFiles()
        {
            if (string.IsNullOrEmpty(windowName) || scriptFolder == null || prefabFolder == null || soFolder == null)
                return false;

            string scriptPath = AssetDatabase.GetAssetPath(scriptFolder);
            string prefabPath = AssetDatabase.GetAssetPath(prefabFolder);
            string soPath = AssetDatabase.GetAssetPath(soFolder);

            if (!scriptPath.EndsWith("/")) scriptPath += "/";
            if (!prefabPath.EndsWith("/")) prefabPath += "/";
            if (!soPath.EndsWith("/")) soPath += "/";

            string scriptFile = scriptPath + windowName + ".cs";
            string prefabFile = prefabPath + windowName + ".prefab";
            string configFile = soPath + windowName + "_Config.asset";

            return File.Exists(scriptFile) || File.Exists(prefabFile) || File.Exists(configFile);
        }

        private void CheckAndDisplayExistingFiles()
        {
            if (string.IsNullOrEmpty(windowName) || scriptFolder == null || prefabFolder == null || soFolder == null)
                return;

            string scriptPath = AssetDatabase.GetAssetPath(scriptFolder);
            string prefabPath = AssetDatabase.GetAssetPath(prefabFolder);
            string soPath = AssetDatabase.GetAssetPath(soFolder);

            if (!scriptPath.EndsWith("/")) scriptPath += "/";
            if (!prefabPath.EndsWith("/")) prefabPath += "/";
            if (!soPath.EndsWith("/")) soPath += "/";

            string scriptFile = scriptPath + windowName + ".cs";
            string prefabFile = prefabPath + windowName + ".prefab";
            string configFile = soPath + windowName + "_Config.asset";

            _existingFilesList.Clear();
            if (File.Exists(scriptFile))
                _existingFilesList.Add($"Script: {scriptFile}");
            if (File.Exists(prefabFile))
                _existingFilesList.Add($"Prefab: {prefabFile}");
            if (File.Exists(configFile))
                _existingFilesList.Add($"Config: {configFile}");

            if (_existingFilesList.Count > 0)
            {
                EditorGUILayout.HelpBox($"⚠ The following files already exist:\n{string.Join("\n", _existingFilesList)}", MessageType.Warning);
            }
        }

        /// <summary>
        /// Performs comprehensive validation before creating UIWindow.
        /// Checks: folder existence, file conflicts, window name validity.
        /// Returns error message if validation fails, empty string if successful.
        /// </summary>
        private string ValidateBeforeCreate()
        {
            _validationErrors.Clear();

            // === 1. Validate Window Name ===
            if (string.IsNullOrEmpty(windowName))
            {
                _validationErrors.Add("• Window name is required");
            }
            else if (!IsValidCSharpIdentifier(windowName))
            {
                _validationErrors.Add($"• Window name '{windowName}' is not a valid C# identifier");
            }

            // === 2. Validate Folder References ===
            if (scriptFolder == null)
            {
                _validationErrors.Add("• Script folder reference is missing");
            }
            if (prefabFolder == null)
            {
                _validationErrors.Add("• Prefab folder reference is missing");
            }
            if (soFolder == null)
            {
                _validationErrors.Add("• Configuration folder reference is missing");
            }
            if (useMVP && presenterFolder == null)
            {
                _validationErrors.Add("• Presenter folder reference is required when using MVP");
            }
            if (selectedLayer == null)
            {
                _validationErrors.Add("• UILayer configuration is required");
            }

            // Early return if references are missing
            if (_validationErrors.Count > 0)
            {
                return "Missing Required Fields:\n\n" + string.Join("\n", _validationErrors);
            }

            // === 3. Validate Folder Existence ===
            _validationErrors.Clear(); // Reuse for missing folders

            string scriptPath = AssetDatabase.GetAssetPath(scriptFolder);
            string prefabPath = AssetDatabase.GetAssetPath(prefabFolder);
            string soPath = AssetDatabase.GetAssetPath(soFolder);
            string presenterPath = useMVP && presenterFolder != null ? AssetDatabase.GetAssetPath(presenterFolder) : "";

            if (!AssetDatabase.IsValidFolder(scriptPath))
            {
                _validationErrors.Add($"• Script folder: {scriptPath}");
            }
            if (!AssetDatabase.IsValidFolder(prefabPath))
            {
                _validationErrors.Add($"• Prefab folder: {prefabPath}");
            }
            if (!AssetDatabase.IsValidFolder(soPath))
            {
                _validationErrors.Add($"• Config folder: {soPath}");
            }
            if (useMVP && !string.IsNullOrEmpty(presenterPath) && !AssetDatabase.IsValidFolder(presenterPath))
            {
                _validationErrors.Add($"• Presenter folder: {presenterPath}");
            }

            if (_validationErrors.Count > 0)
            {
                return "Folders No Longer Exist:\n\nThe following folders may have been deleted or moved:\n" + string.Join("\n", _validationErrors) + "\n\nPlease re-select the folders.";
            }

            // === 4. Check for Existing Files ===
            _validationErrors.Clear(); // Reuse for existing files

            // Normalize paths
            if (!scriptPath.EndsWith("/")) scriptPath += "/";
            if (!prefabPath.EndsWith("/")) prefabPath += "/";
            if (!soPath.EndsWith("/")) soPath += "/";
            if (useMVP && !string.IsNullOrEmpty(presenterPath) && !presenterPath.EndsWith("/")) presenterPath += "/";

            // Check main files
            string scriptFile = scriptPath + windowName + ".cs";
            string prefabFile = prefabPath + windowName + ".prefab";
            string configFile = soPath + windowName + "_Config.asset";

            if (File.Exists(scriptFile))
                _validationErrors.Add($"• Script: {scriptFile}");
            if (File.Exists(prefabFile))
                _validationErrors.Add($"• Prefab: {prefabFile}");
            if (File.Exists(configFile))
                _validationErrors.Add($"• Config: {configFile}");

            // Check MVP files
            if (useMVP)
            {
                string viewInterfaceFile = scriptPath + "I" + windowName + "View.cs";
                string presenterFile = presenterPath + windowName + "Presenter.cs";

                if (File.Exists(viewInterfaceFile))
                    _validationErrors.Add($"• View Interface: {viewInterfaceFile}");
                if (File.Exists(presenterFile))
                    _validationErrors.Add($"• Presenter: {presenterFile}");
            }

            if (_validationErrors.Count > 0)
            {
                return "Files Already Exist:\n\nThe following files would be overwritten:\n" + string.Join("\n", _validationErrors) + "\n\nPlease delete or rename them first, or choose a different window name.";
            }

            // All validations passed
            return "";
        }

        private void CreateUIWindow()
        {
            try
            {
                // === Pre-creation Safety Validation ===
                // Validate all paths and check for existing files before proceeding
                string validationError = ValidateBeforeCreate();
                if (!string.IsNullOrEmpty(validationError))
                {
                    EditorUtility.DisplayDialog("Validation Failed", validationError, "OK");
                    return;
                }

                // Get paths (Unity API expects paths starting with "Assets/")
                string scriptPath = AssetDatabase.GetAssetPath(scriptFolder);
                string soPath = AssetDatabase.GetAssetPath(soFolder);
                string prefabPath = AssetDatabase.GetAssetPath(prefabFolder);
                string presenterPath = useMVP && presenterFolder != null ? AssetDatabase.GetAssetPath(presenterFolder) : "";

                // Ensure paths start with "Assets/"
                if (!scriptPath.StartsWith("Assets/"))
                    scriptPath = "Assets/" + scriptPath;
                if (!soPath.StartsWith("Assets/"))
                    soPath = "Assets/" + soPath;
                if (!prefabPath.StartsWith("Assets/"))
                    prefabPath = "Assets/" + prefabPath;
                if (useMVP && !string.IsNullOrEmpty(presenterPath) && !presenterPath.StartsWith("Assets/"))
                    presenterPath = "Assets/" + presenterPath;

                // Ensure paths end with "/"
                if (!scriptPath.EndsWith("/"))
                    scriptPath += "/";
                if (!soPath.EndsWith("/"))
                    soPath += "/";
                if (!prefabPath.EndsWith("/"))
                    prefabPath += "/";
                if (useMVP && !string.IsNullOrEmpty(presenterPath) && !presenterPath.EndsWith("/"))
                    presenterPath += "/";

                // Create script name
                string scriptName = windowName;
                string scriptFileName = scriptName + ".cs";
                string fullScriptPath = scriptPath + scriptFileName;

                // Create prefab name
                string prefabName = windowName + ".prefab";
                string fullPrefabPath = prefabPath + prefabName;

                // Create SO name with _Config suffix to avoid YooAsset Location conflicts
                string soName = windowName + "_Config.asset";
                string fullSoPath = soPath + soName;

                // MVP file paths
                string fullViewInterfacePath = useMVP ? scriptPath + "I" + windowName + "View.cs" : "";
                string fullPresenterPath = useMVP ? presenterPath + windowName + "Presenter.cs" : "";

                // Create scripts (including MVP if enabled)
                CreateScript(fullScriptPath, scriptName, namespaceName, useMVP);
                
                if (useMVP)
                {
                    CreateViewInterface(fullViewInterfacePath, scriptName, namespaceName);
                    CreatePresenter(fullPresenterPath, scriptName, namespaceName);
                }
                
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                GameObject prefabInstance = CreatePrefab(fullPrefabPath, scriptName, null);
                CreateConfiguration(fullSoPath, prefabInstance, selectedLayer);

                System.Type scriptType = GetScriptType(scriptName, namespaceName);

                bool scriptAdded = false;
                if (scriptType != null)
                {
                    scriptAdded = AddScriptComponentToPrefab(fullPrefabPath, scriptType, scriptName);

                    if (scriptAdded)
                    {
                        UpdateConfigurationPrefabReference(fullSoPath, fullPrefabPath);
                    }
                }
                else
                {
                    ScheduleAsyncScriptCheck(scriptName, namespaceName, fullPrefabPath, fullSoPath);
                }

                AssetDatabase.Refresh();

                string message = $"UIWindow '{windowName}' created successfully!\n\n" +
                               $"Script: {fullScriptPath}\n" +
                               $"Prefab: {fullPrefabPath}\n" +
                               $"Config: {fullSoPath}\n";

                if (useMVP)
                {
                    message += $"\nMVP Files:\n" +
                               $"View Interface: {fullViewInterfacePath}\n" +
                               $"Presenter: {fullPresenterPath}\n";
                }

                message += "\n";

                if (!scriptAdded)
                {
                    message += $"⚠ Note: The script component was not added automatically because the script is still compiling.\n" +
                              $"Please wait for Unity to finish compiling, then manually add the {scriptName} component to the prefab.";
                }

                EditorUtility.DisplayDialog("Success", message, "OK");

                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(fullPrefabPath);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create UIWindow: {e.Message}\n\n{e.StackTrace}", "OK");
                Debug.LogError($"UIWindow Creator Error: {e}");
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
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            using var nativeBytes = new NativeArray<byte>(bytes, Allocator.Temp);
            NativeFile.WriteAllBytes(scriptPath, nativeBytes);

            AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private string pendingScriptName;
        private string pendingNamespaceName;
        private string pendingPrefabPath;
        private string pendingConfigPath;
        private int asyncCheckAttempts = 0;
        private const int MAX_ASYNC_ATTEMPTS = 100; // Check for up to 20 seconds (100 * 0.2s)

        private void ScheduleAsyncScriptCheck(string scriptName, string namespaceName, string prefabPath, string configPath)
        {
            pendingScriptName = scriptName;
            pendingNamespaceName = namespaceName;
            pendingPrefabPath = prefabPath;
            pendingConfigPath = configPath;
            asyncCheckAttempts = 0;

            EditorApplication.update += OnAsyncScriptCheck;
        }

        private void OnAsyncScriptCheck()
        {
            asyncCheckAttempts++;

            System.Type scriptType = GetScriptType(pendingScriptName, pendingNamespaceName);

            if (scriptType != null)
            {
                EditorApplication.update -= OnAsyncScriptCheck;

                bool scriptAdded = AddScriptComponentToPrefab(pendingPrefabPath, scriptType, pendingScriptName);

                if (scriptAdded)
                {
                    UpdateConfigurationPrefabReference(pendingConfigPath, pendingPrefabPath);
                    Debug.Log($"✓ Script {pendingScriptName} compiled and component added successfully!");
                }

                pendingScriptName = null;
                pendingNamespaceName = null;
                pendingPrefabPath = null;
                pendingConfigPath = null;
                asyncCheckAttempts = 0;
            }
            else if (asyncCheckAttempts >= MAX_ASYNC_ATTEMPTS)
            {
                EditorApplication.update -= OnAsyncScriptCheck;
                Debug.LogWarning($"⚠ Script {pendingScriptName} did not compile within timeout. Please manually add the component to the prefab.");

                pendingScriptName = null;
                pendingNamespaceName = null;
                pendingPrefabPath = null;
                pendingConfigPath = null;
                asyncCheckAttempts = 0;
            }
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
                prefabInstance.name = scriptName;

                PrefabUtility.UnpackPrefabInstance(prefabInstance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

                UIWindow existingWindow = prefabInstance.GetComponent<UIWindow>();
                if (existingWindow != null)
                {
                    DestroyImmediate(existingWindow);
                }
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
                    Debug.Log($"✓ Added {scriptName} component to prefab instance before saving.");
                }
            }
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabInstance, prefabPath);

            DestroyImmediate(prefabInstance);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);

            savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            return savedPrefab;
        }

        private bool AddScriptComponentToPrefab(string prefabPath, System.Type scriptType, string scriptName)
        {
            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (savedPrefab == null || scriptType == null)
                return false;

            if (savedPrefab.GetComponent(scriptType) != null)
            {
                Debug.Log($"✓ {scriptName} component already exists on prefab.");
                return true;
            }

            string prefabPathFull = AssetDatabase.GetAssetPath(savedPrefab);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPathFull);

            if (prefabRoot == null)
            {
                Debug.LogWarning($"⚠ Failed to load prefab contents for {prefabPathFull}");
                return false;
            }

            prefabRoot.AddComponent(scriptType);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPathFull);
            PrefabUtility.UnloadPrefabContents(prefabRoot);

            AssetDatabase.Refresh();

            savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (savedPrefab != null && savedPrefab.GetComponent(scriptType) != null)
            {
                Debug.Log($"✓ Successfully added {scriptName} component to prefab.");
                return true;
            }

            Debug.LogWarning($"⚠ Failed to add {scriptName} component to prefab.");
            return false;
        }

        private void UpdateConfigurationPrefabReference(string configPath, string prefabPath)
        {
            UIWindowConfiguration config = AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(configPath);
            if (config == null)
                return;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return;

            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty prefabProperty = serializedConfig.FindProperty("windowPrefab");
            if (prefabProperty != null && prefabProperty.objectReferenceValue != prefab)
            {
                prefabProperty.objectReferenceValue = prefab;
                serializedConfig.ApplyModifiedProperties();
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                Debug.Log($"✓ Updated prefab reference in configuration.");
            }
        }

        private void CreateConfiguration(string soPath, GameObject prefab, UILayerConfiguration layer)
        {
            string directory = Path.GetDirectoryName(soPath);
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

            if (prefab == null)
            {
                Debug.LogError("Cannot create configuration: Prefab is null!");
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject reloadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (reloadedPrefab == null)
            {
                Debug.LogError($"Cannot create configuration: Failed to reload prefab at {prefabPath}!");
                return;
            }

            UIWindowConfiguration config = ScriptableObject.CreateInstance<UIWindowConfiguration>();

            SerializedObject serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("windowPrefab").objectReferenceValue = reloadedPrefab;
            serializedConfig.FindProperty("source").enumValueIndex = (int)UIWindowConfiguration.PrefabSource.PrefabReference;
            serializedConfig.FindProperty("layer").objectReferenceValue = layer;
            serializedConfig.ApplyModifiedProperties();

            AssetDatabase.CreateAsset(config, soPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UIWindowConfiguration loadedConfig = AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(soPath);
            if (loadedConfig != null && loadedConfig.WindowPrefab == reloadedPrefab)
            {
                Debug.Log($"✓ Successfully created UIWindowConfiguration with prefab reference: {reloadedPrefab.name}");
            }
            else
            {
                Debug.LogWarning($"⚠ UIWindowConfiguration created but prefab reference may not be set correctly.");
            }
        }
    }
}