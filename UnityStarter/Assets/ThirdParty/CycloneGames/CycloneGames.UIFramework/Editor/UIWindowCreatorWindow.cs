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
        public string namespaceName = "";
    }

    public class UIWindowCreatorWindow : EditorWindow
    {
        private const string DEFAULT_TEMPLATE_GUID = "37c32b368ca8d4841b923d1b37cf97b9";
        private const string SETTINGS_FILE_NAME = "UIWindowCreatorSettings.json";

        private string namespaceName = "";
        private DefaultAsset scriptFolder;
        private DefaultAsset soFolder;
        private DefaultAsset prefabFolder;
        private UILayerConfiguration selectedLayer;
        private string windowName = "";
        private GameObject templatePrefab;
        private Vector2 scrollPosition;

        private string settingsPath;

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
                    configFolderPath = soFolder != null ? AssetDatabase.GetAssetPath(soFolder) : ""
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
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Header
            EditorGUILayout.Space(10);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.LabelField("UIWindow Creator", headerStyle);
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
                EditorGUILayout.LabelField($"Config Name: {windowName}.asset", EditorStyles.miniLabel);

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
                    string configName = string.IsNullOrEmpty(windowName) ? "UIWindow_New.asset" : $"{windowName}.asset";
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

            // Section 4: Template (Optional)
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
            var sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            EditorGUILayout.LabelField(title, sectionStyle);
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

            if (!IsValidCSharpIdentifier(windowName))
                return false;

            string scriptPath = scriptFolder != null ? AssetDatabase.GetAssetPath(scriptFolder) : "";
            string soPath = soFolder != null ? AssetDatabase.GetAssetPath(soFolder) : "";
            string prefabPath = prefabFolder != null ? AssetDatabase.GetAssetPath(prefabFolder) : "";

            if (!string.IsNullOrEmpty(scriptPath) && !AssetDatabase.IsValidFolder(scriptPath))
                return false;
            if (!string.IsNullOrEmpty(soPath) && !AssetDatabase.IsValidFolder(soPath))
                return false;
            if (!string.IsNullOrEmpty(prefabPath) && !AssetDatabase.IsValidFolder(prefabPath))
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
            string configFile = soPath + windowName + ".asset";

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
            string configFile = soPath + windowName + ".asset";

            System.Collections.Generic.List<string> existingFiles = new System.Collections.Generic.List<string>();
            if (File.Exists(scriptFile))
                existingFiles.Add($"Script: {scriptFile}");
            if (File.Exists(prefabFile))
                existingFiles.Add($"Prefab: {prefabFile}");
            if (File.Exists(configFile))
                existingFiles.Add($"Config: {configFile}");

            if (existingFiles.Count > 0)
            {
                EditorGUILayout.HelpBox($"⚠ The following files already exist:\n{string.Join("\n", existingFiles)}", MessageType.Warning);
            }
        }

        private void CreateUIWindow()
        {
            try
            {
                // Get paths (Unity API expects paths starting with "Assets/")
                string scriptPath = AssetDatabase.GetAssetPath(scriptFolder);
                string soPath = AssetDatabase.GetAssetPath(soFolder);
                string prefabPath = AssetDatabase.GetAssetPath(prefabFolder);

                // Ensure paths start with "Assets/"
                if (!scriptPath.StartsWith("Assets/"))
                    scriptPath = "Assets/" + scriptPath;
                if (!soPath.StartsWith("Assets/"))
                    soPath = "Assets/" + soPath;
                if (!prefabPath.StartsWith("Assets/"))
                    prefabPath = "Assets/" + prefabPath;

                // Ensure paths end with "/"
                if (!scriptPath.EndsWith("/"))
                    scriptPath += "/";
                if (!soPath.EndsWith("/"))
                    soPath += "/";
                if (!prefabPath.EndsWith("/"))
                    prefabPath += "/";

                // Create script name
                string scriptName = windowName;
                string scriptFileName = scriptName + ".cs";
                string fullScriptPath = scriptPath + scriptFileName;

                // Create prefab name
                string prefabName = windowName + ".prefab";
                string fullPrefabPath = prefabPath + prefabName;

                // Create SO name (same as prefab name, just with .asset extension)
                string soName = windowName + ".asset";
                string fullSoPath = soPath + soName;

                CreateScript(fullScriptPath, scriptName, namespaceName);
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
                               $"Config: {fullSoPath}\n\n";

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

        private void CreateScript(string scriptPath, string className, string namespaceName)
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

            // Generate script content (with or without namespace)
            string scriptContent;
            if (string.IsNullOrEmpty(namespaceName))
            {
                scriptContent = $@"using CycloneGames.UIFramework.Runtime;

public class {className} : UIWindow
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
            else
            {
                scriptContent = $@"using CycloneGames.UIFramework.Runtime;

namespace {namespaceName}
{{
    public class {className} : UIWindow
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

            // Write script file (Unity will automatically generate .meta file on import)
            byte[] bytes = Encoding.UTF8.GetBytes(scriptContent);
            using var nativeBytes = new NativeArray<byte>(bytes, Allocator.Temp);
            NativeFile.WriteAllBytes(scriptPath, nativeBytes);

            // Import the asset to ensure meta file is generated
            // Note: This may not trigger compilation if auto-refresh is disabled
            AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);

            // Force a refresh to trigger compilation
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