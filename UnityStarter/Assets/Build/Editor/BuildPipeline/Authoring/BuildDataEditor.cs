using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Data;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(BuildData))]
    public sealed partial class BuildDataEditor : UnityEditor.Editor
    {
        private const string VersionInfoFileName = "VersionInfoData.asset";

        private SerializedProperty launchScene;
        private SerializedProperty additionalScenes;
        private SerializedProperty applicationVersion;
        private SerializedProperty outputBasePath;
        private SerializedProperty companyName;
        private SerializedProperty productName;
        private SerializedProperty applicationIdentifier;
        private SerializedProperty versionInfoAssetPath;
        private SerializedProperty pipelineSteps;
        private SerializedProperty useHybridCLR;
        private SerializedProperty enablePlayerObfuscation;
        private SerializedProperty cheatBuildMode;
        private SerializedProperty assetContentProviderId;
        private SerializedProperty assetContentConfiguration;
        private SerializedProperty hybridCLRBuildConfig;

        private IReadOnlyList<BuildStepDescriptor> stepDescriptors = Array.Empty<BuildStepDescriptor>();
        private IReadOnlyList<AssetContentProviderDescriptor> providerDescriptors =
            Array.Empty<AssetContentProviderDescriptor>();
        private ReorderableList stepList;
        private string catalogError;
        private string versionInfoTargetOccupationError;

        private void OnEnable()
        {
            launchScene = Find("launchScene");
            additionalScenes = Find("additionalScenes");
            applicationVersion = Find("applicationVersion");
            outputBasePath = Find("outputBasePath");
            companyName = Find("companyName");
            productName = Find("productName");
            applicationIdentifier = Find("applicationIdentifier");
            versionInfoAssetPath = Find("versionInfoAssetPath");
            pipelineSteps = Find("pipelineSteps");
            useHybridCLR = Find("useHybridCLR");
            enablePlayerObfuscation = Find("enablePlayerObfuscation");
            cheatBuildMode = Find("cheatBuildMode");
            assetContentProviderId = Find("assetContentProviderId");
            assetContentConfiguration = Find("assetContentConfiguration");
            hybridCLRBuildConfig = Find("hybridCLRBuildConfig");

            RefreshCatalog();
            CreateStepList();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScenes();
            DrawVersionAndOutput();
            DrawProductIdentity();
            DrawCapabilities();
            DrawAssetContent();
            BuildRecipeAnalysis recipe = DrawPipelineRecipe();

            IReadOnlyList<string> errors = ValidateSerializedProfile(recipe);
            EditorGUILayout.Space(8f);
            if (!string.IsNullOrEmpty(catalogError))
            {
                EditorGUILayout.HelpBox(catalogError, MessageType.Error);
            }

            if (errors.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Error);
            }
            else if (string.IsNullOrEmpty(catalogError))
            {
                EditorGUILayout.HelpBox(
                    "The same stable step and provider IDs are used by this profile and CI. " +
                    "Preflight validates optional packages, dependencies, and output safety before changing Unity build state.",
                    MessageType.Info);
            }

            DrawRunActions(errors, recipe);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScenes()
        {
            DrawSectionHeader("Scenes");
            EditorGUILayout.PropertyField(launchScene);
            EditorGUILayout.PropertyField(additionalScenes, includeChildren: true);
        }

        private void DrawVersionAndOutput()
        {
            DrawSectionHeader("Version and Output");
            EditorGUILayout.PropertyField(applicationVersion);
            BuildAuthoringPathField.DrawProjectRelativeDirectory(
                outputBasePath,
                new GUIContent(
                    "Output Base Directory",
                    $"Project-relative root for all build results. CI may override it with {BuildCommandLineOptionNames.OutputRoot}."),
                fallbackDirectory: "Build",
                allowEmpty: false);
            DrawVersionInfoDestination();
        }

        private void DrawProductIdentity()
        {
            DrawSectionHeader("Product Identity");
            EditorGUILayout.PropertyField(companyName);
            EditorGUILayout.PropertyField(productName);
            EditorGUILayout.PropertyField(applicationIdentifier);
        }

        private void DrawCapabilities()
        {
            DrawSectionHeader("Optional Capabilities");
            EditorGUILayout.PropertyField(useHybridCLR, new GUIContent("Use HybridCLR"));
            if (useHybridCLR.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(hybridCLRBuildConfig);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(enablePlayerObfuscation);
            EditorGUILayout.PropertyField(cheatBuildMode);
            EditorGUILayout.HelpBox(
                "Cheat Build Mode controls the per-build ENABLE_CHEAT symbol for the Player. " +
                "It does not require HybridCLR and is not configured by HybridCLRBuildConfig.",
                MessageType.Info);
        }

        private void DrawAssetContent()
        {
            DrawSectionHeader("Asset Content");

            string currentId = assetContentProviderId.stringValue?.Trim() ?? string.Empty;
            var choices = new List<GUIContent>(providerDescriptors.Count + 2)
            {
                new GUIContent("None", "Do not invoke an external asset-content provider.")
            };
            var ids = new List<string>(providerDescriptors.Count + 2) { string.Empty };
            int selectedIndex = 0;

            for (int index = 0; index < providerDescriptors.Count; index++)
            {
                AssetContentProviderDescriptor descriptor = providerDescriptors[index];
                string availability = descriptor.IsAvailable ? string.Empty : " (Unavailable)";
                choices.Add(new GUIContent(
                    descriptor.DisplayName + availability,
                    descriptor.Description));
                ids.Add(descriptor.ProviderId);
                if (string.Equals(currentId, descriptor.ProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = ids.Count - 1;
                }
            }

            if (!string.IsNullOrEmpty(currentId)
                && !ids.Any(id => string.Equals(id, currentId, StringComparison.OrdinalIgnoreCase)))
            {
                choices.Add(new GUIContent($"Missing Provider [{currentId}]"));
                ids.Add(currentId);
                selectedIndex = ids.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(
                new GUIContent("Provider", "Stable provider ID selected from the authoring catalog."),
                selectedIndex,
                choices.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                string newId = ids[newIndex];
                assetContentProviderId.stringValue = newId;
                AssetContentProviderDescriptor newDescriptor = FindProviderDescriptor(newId);
                if (newDescriptor == null
                    || assetContentConfiguration.objectReferenceValue == null
                    || !newDescriptor.ConfigurationType.IsInstanceOfType(
                        assetContentConfiguration.objectReferenceValue))
                {
                    assetContentConfiguration.objectReferenceValue = null;
                }

                currentId = newId;
            }

            AssetContentProviderDescriptor selected = FindProviderDescriptor(currentId);
            if (selected == null)
            {
                if (!string.IsNullOrEmpty(currentId))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(assetContentConfiguration);
                    }

                    EditorGUILayout.HelpBox(
                        $"Provider '{currentId}' is not declared by an installed authoring integration. " +
                        "The serialized ID and configuration reference are preserved.",
                        MessageType.Error);
                }

                return;
            }

            DrawProviderConfiguration(selected);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    new GUIContent(
                        "CI Provider ID",
                        $"Use this value with {BuildCommandLineOptionNames.Provider}."),
                    selected.ProviderId);
            }

            if (!selected.IsAvailable)
            {
                EditorGUILayout.HelpBox(
                    $"{selected.DisplayName} authoring is available, but its package-compatible build adapter is not. " +
                    "The core pipeline remains compilable; a build selecting this provider will fail preflight.",
                    MessageType.Warning);
            }
        }

        private void DrawProviderConfiguration(AssetContentProviderDescriptor descriptor)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object selected = EditorGUILayout.ObjectField(
                new GUIContent("Configuration", descriptor.ConfigurationType.Name),
                assetContentConfiguration.objectReferenceValue,
                descriptor.ConfigurationType,
                allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                assetContentConfiguration.objectReferenceValue = selected;
            }

            if (GUILayout.Button("Create", GUILayout.Width(58f)))
            {
                CreateProviderConfiguration(descriptor);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void CreateProviderConfiguration(AssetContentProviderDescriptor descriptor)
        {
            string defaultName = descriptor.DisplayName.Replace(" ", string.Empty) + "BuildConfig";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create " + descriptor.DisplayName + " Configuration",
                defaultName,
                "asset",
                "Choose a version-controlled location for this provider configuration.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (IsAssetCreationPathOccupied(path))
            {
                EditorUtility.DisplayDialog(
                    "Configuration Already Exists",
                    $"Refusing to replace the existing asset at '{path}'. Choose a new file name.",
                    "OK");
                return;
            }

            var instance = ScriptableObject.CreateInstance(descriptor.ConfigurationType);
            AssetDatabase.CreateAsset(instance, path);
            Undo.RegisterCreatedObjectUndo(instance, "Create Build Provider Configuration");
            assetContentConfiguration.objectReferenceValue = instance;
            serializedObject.ApplyModifiedProperties();
            Selection.activeObject = instance;
            EditorGUIUtility.PingObject(instance);
        }

        internal static bool IsAssetCreationPathOccupied(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return true;
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                return true;
            }

            string absolutePath = Path.GetFullPath(
                Path.Combine(BuildAuthoringPathField.GetProjectRoot(), assetPath));
            return File.Exists(absolutePath)
                || Directory.Exists(absolutePath)
                || File.Exists(absolutePath + ".meta");
        }

        private void DrawVersionInfoDestination()
        {
            string path = versionInfoAssetPath.stringValue?.Replace('\\', '/') ?? string.Empty;
            string directory = GetAssetDirectory(path);
            UnityEngine.Object targetAsset = AssetDatabase.LoadMainAssetAtPath(path);
            versionInfoTargetOccupationError =
                GetVersionInfoTargetOccupationError(path, targetAsset);

            UnityEngine.Object current = targetAsset;
            if (current == null
                && string.IsNullOrEmpty(versionInfoTargetOccupationError)
                && !string.IsNullOrEmpty(directory))
            {
                current = AssetDatabase.LoadAssetAtPath<DefaultAsset>(directory);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object selected = EditorGUILayout.ObjectField(
                new GUIContent(
                    "Version Info Destination",
                    "Drag an existing VersionInfoData asset or an Assets folder. The generated asset file name is fixed for deterministic runtime loading."),
                current,
                typeof(UnityEngine.Object),
                allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyVersionInfoObject(selected);
            }

            if (GUILayout.Button("Browse", GUILayout.Width(64f)))
            {
                BrowseVersionInfoDirectory(directory);
            }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    new GUIContent(
                        "Generated Asset Path",
                        $"CI may override this with {BuildCommandLineOptionNames.VersionInfo}."),
                    versionInfoAssetPath.stringValue);
            }

            if (current == null
                && string.IsNullOrEmpty(versionInfoTargetOccupationError)
                && !string.IsNullOrEmpty(directory))
            {
                if (GUILayout.Button("Create Destination Folder"))
                {
                    CreateVersionInfoDirectory(directory);
                }
            }
        }

        private static string GetVersionInfoTargetOccupationError(
            string assetPath,
            UnityEngine.Object mainAsset)
        {
            if (string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    assetPath,
                    "Version Info Asset Path");
            }
            catch (ArgumentException)
            {
                return null;
            }

            string absolutePath = Path.GetFullPath(
                Path.Combine(BuildAuthoringPathField.GetProjectRoot(), assetPath));
            bool containsVersionInfoAsset = mainAsset is VersionInfoData;
            string occupyingAssetType = mainAsset == null || containsVersionInfoAsset
                ? null
                : mainAsset.GetType().Name;
            return DescribeVersionInfoTargetOccupation(
                assetPath,
                containsVersionInfoAsset,
                occupyingAssetType,
                File.Exists(absolutePath),
                Directory.Exists(absolutePath),
                File.Exists(absolutePath + ".meta"));
        }

        internal static string DescribeVersionInfoTargetOccupation(
            string assetPath,
            bool containsVersionInfoAsset,
            string occupyingAssetType,
            bool targetFileExists,
            bool targetDirectoryExists,
            bool targetMetaExists)
        {
            if (containsVersionInfoAsset)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(occupyingAssetType))
            {
                return
                    $"Version Info target '{assetPath}' is occupied by a {occupyingAssetType} asset. " +
                    "Select a VersionInfoData asset or another destination folder.";
            }

            if (targetDirectoryExists)
            {
                return
                    $"Version Info target '{assetPath}' is occupied by a directory at the generated asset file path.";
            }

            if (targetFileExists)
            {
                return
                    $"Version Info target '{assetPath}' is occupied by a file that Unity cannot load as VersionInfoData.";
            }

            if (targetMetaExists)
            {
                return
                    $"Version Info target '{assetPath}' is occupied by an orphan .meta file.";
            }

            return null;
        }

        private void ApplyVersionInfoObject(UnityEngine.Object selected)
        {
            if (selected == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected).Replace('\\', '/');
            if (selected is VersionInfoData)
            {
                versionInfoAssetPath.stringValue = path;
                return;
            }

            if (selected is DefaultAsset && AssetDatabase.IsValidFolder(path))
            {
                versionInfoAssetPath.stringValue = path.TrimEnd('/') + "/" + VersionInfoFileName;
                return;
            }

            EditorUtility.DisplayDialog(
                "Invalid Version Info Destination",
                "Select a VersionInfoData asset or a folder below Assets.",
                "OK");
        }

        private void BrowseVersionInfoDirectory(string currentDirectory)
        {
            string projectRoot = BuildAuthoringPathField.GetProjectRoot();
            string current = BuildAuthoringPathField.ResolveExistingDirectory(projectRoot, currentDirectory);
            string selected = EditorUtility.OpenFolderPanel("Version Info Destination", current, string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            if (!BuildAuthoringPathField.TryMakeProjectRelative(projectRoot, selected, out string relative)
                || !(relative.Equals("Assets", StringComparison.Ordinal)
                     || relative.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Version Info Destination",
                    "VersionInfoData must be generated below Assets so Unity can import it.",
                    "OK");
                return;
            }

            versionInfoAssetPath.stringValue = relative.TrimEnd('/') + "/" + VersionInfoFileName;
        }

        private static void CreateVersionInfoDirectory(string directory)
        {
            try
            {
                string projectRoot = BuildAuthoringPathField.GetProjectRoot();
                string absolute = BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                    projectRoot,
                    directory);
                Directory.CreateDirectory(absolute);
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "Unable to Create Destination",
                    exception.Message,
                    "OK");
            }
        }

        private void CreateStepList()
        {
            stepList = new ReorderableList(
                serializedObject,
                pipelineSteps,
                draggable: true,
                displayHeader: true,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight * 2f + 6f,
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Ordered Pipeline Steps"),
                drawElementCallback = DrawStepElement,
                onAddDropdownCallback = ShowAddStepMenu
            };
        }

        private void DrawStepElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = pipelineSteps.GetArrayElementAtIndex(index);
            string currentId = element.stringValue?.Trim() ?? string.Empty;

            var choices = new List<GUIContent>(stepDescriptors.Count + 1);
            var ids = new List<string>(stepDescriptors.Count + 1);
            int selectedIndex = -1;
            for (int descriptorIndex = 0; descriptorIndex < stepDescriptors.Count; descriptorIndex++)
            {
                BuildStepDescriptor descriptor = stepDescriptors[descriptorIndex];
                choices.Add(new GUIContent(
                    $"{descriptor.Category}/{descriptor.DisplayName}",
                    descriptor.Description));
                ids.Add(descriptor.Id);
                if (string.Equals(currentId, descriptor.Id, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = descriptorIndex;
                }
            }

            if (selectedIndex < 0)
            {
                choices.Add(new GUIContent($"Missing Step [{currentId}]"));
                ids.Add(currentId);
                selectedIndex = ids.Count - 1;
            }

            Rect popupRect = new Rect(
                rect.x,
                rect.y + 1f,
                rect.width,
                EditorGUIUtility.singleLineHeight);
            int newIndex = EditorGUI.Popup(popupRect, selectedIndex, choices.ToArray());
            if (newIndex >= 0 && newIndex < ids.Count && newIndex != selectedIndex)
            {
                string selectedId = ids[newIndex];
                if (IsStepConfiguredAtAnotherIndex(selectedId, index))
                {
                    EditorUtility.DisplayDialog(
                        "Step Already Configured",
                        $"The build step '{selectedId}' is already present in this recipe.",
                        "OK");
                }
                else
                {
                    element.stringValue = selectedId;
                }
            }

            Rect idRect = new Rect(
                rect.x + 4f,
                popupRect.yMax + 2f,
                rect.width - 4f,
                EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(idRect, "CI step ID: " + element.stringValue, EditorStyles.miniLabel);
        }

        private void ShowAddStepMenu(Rect buttonRect, ReorderableList list)
        {
            var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < pipelineSteps.arraySize; index++)
            {
                configured.Add(pipelineSteps.GetArrayElementAtIndex(index).stringValue?.Trim() ?? string.Empty);
            }

            var menu = new GenericMenu();
            int availableCount = 0;
            foreach (BuildStepDescriptor descriptor in stepDescriptors)
            {
                GUIContent label = new GUIContent($"{descriptor.Category}/{descriptor.DisplayName}");
                if (configured.Contains(descriptor.Id))
                {
                    menu.AddDisabledItem(label, on: true);
                    continue;
                }

                string id = descriptor.Id;
                menu.AddItem(label, on: false, () => AddStep(id));
                availableCount++;
            }

            if (availableCount == 0)
            {
                menu.AddDisabledItem(new GUIContent("All registered steps are already configured"));
            }

            menu.DropDown(buttonRect);
        }

        private void AddStep(string id)
        {
            serializedObject.Update();
            int index = pipelineSteps.arraySize;
            pipelineSteps.InsertArrayElementAtIndex(index);
            pipelineSteps.GetArrayElementAtIndex(index).stringValue = id;
            serializedObject.ApplyModifiedProperties();
        }

        private bool IsStepConfiguredAtAnotherIndex(string stepId, int currentIndex)
        {
            for (int index = 0; index < pipelineSteps.arraySize; index++)
            {
                if (index != currentIndex
                    && string.Equals(
                        pipelineSteps.GetArrayElementAtIndex(index).stringValue?.Trim(),
                        stepId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshCatalog()
        {
            var errors = new List<string>();
            try
            {
                stepDescriptors = BuildPipelineRegistry.GetBuildStepDescriptors(errors);
            }
            catch (Exception exception)
            {
                stepDescriptors = Array.Empty<BuildStepDescriptor>();
                errors.Add("Build step catalog is invalid: " + exception.Message);
            }

            try
            {
                providerDescriptors = BuildPipelineRegistry.GetAssetContentProviderDescriptors(errors);
            }
            catch (Exception exception)
            {
                providerDescriptors = Array.Empty<AssetContentProviderDescriptor>();
                errors.Add("Asset provider catalog is invalid: " + exception.Message);
            }

            catalogError = errors.Count == 0 ? null : string.Join("\n", errors);
        }

        private SerializedProperty Find(string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"BuildData serialized property '{propertyName}' was not found.");
            }

            return property;
        }

        private static void DrawSectionHeader(string label)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private IReadOnlyList<string> ValidateSerializedProfile(BuildRecipeAnalysis recipe)
        {
            var errors = new List<string>();
            if (recipe.IncludesPlayer && launchScene.objectReferenceValue == null)
            {
                errors.Add("Launch Scene is required when the recipe builds a Player.");
            }

            try
            {
                BuildIdentityPolicy.ValidateApplicationVersion(applicationVersion.stringValue);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }

            ValidateOutputRoot(outputBasePath.stringValue, errors);
            try
            {
                BuildIdentityPolicy.ValidatePlainText(companyName.stringValue, "Company Name", 256);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }

            ValidateRequired(productName.stringValue, "Product Name", errors);
            if (!string.IsNullOrWhiteSpace(productName.stringValue))
            {
                TryValidatePortableFileName(productName.stringValue, "Product Name", errors);
            }

            try
            {
                BuildIdentityPolicy.ValidateApplicationIdentifier(applicationIdentifier.stringValue);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }

            ValidateVersionInfoPath(versionInfoAssetPath.stringValue, errors);
            if (!string.IsNullOrEmpty(versionInfoTargetOccupationError))
            {
                errors.Add(versionInfoTargetOccupationError);
            }
            ValidateStepIds(errors);
            foreach (string issue in recipe.BlockingIssues)
            {
                errors.Add(issue);
            }

            if (useHybridCLR.boolValue
                && recipe.IncludesHotUpdate
                && hybridCLRBuildConfig.objectReferenceValue == null)
            {
                errors.Add(
                    "HybridCLR Build Config is required when the recipe includes an enabled Hot Update step.");
            }

            string providerId = assetContentProviderId.stringValue?.Trim() ?? string.Empty;
            bool hasProvider = providerId.Length > 0;
            bool hasConfiguration = assetContentConfiguration.objectReferenceValue != null;
            bool usesContentBinding = hasProvider
                && (recipe.IncludesAssetContent || recipe.IncludesPlayer);
            if (usesContentBinding && !hasConfiguration)
            {
                errors.Add(
                    "Asset Content Configuration is required because the current recipe uses the configured Provider.");
            }
            else if (usesContentBinding)
            {
                AssetContentProviderDescriptor descriptor = FindProviderDescriptor(providerId);
                if (descriptor == null)
                {
                    errors.Add($"Asset Content Provider '{providerId}' is not declared by an installed authoring integration.");
                }
                else if (!descriptor.ConfigurationType.IsInstanceOfType(
                             assetContentConfiguration.objectReferenceValue))
                {
                    errors.Add(
                        $"{descriptor.DisplayName} requires {descriptor.ConfigurationType.Name}, but the profile references " +
                        $"{assetContentConfiguration.objectReferenceValue.GetType().Name}.");
                }
                else if (!descriptor.IsAvailable)
                {
                    errors.Add(
                        $"{descriptor.DisplayName} is configured, but its package-compatible build adapter is unavailable.");
                }
            }

            return errors;
        }

        private void ValidateStepIds(ICollection<string> errors)
        {
            if (pipelineSteps.arraySize == 0)
            {
                errors.Add("At least one Pipeline Step is required.");
                return;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < pipelineSteps.arraySize; index++)
            {
                string id = pipelineSteps.GetArrayElementAtIndex(index).stringValue?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    errors.Add($"Pipeline Step at index {index} is empty.");
                }
                else if (!ids.Add(id))
                {
                    errors.Add($"Pipeline Step '{id}' is configured more than once.");
                }
                else if (!stepDescriptors.Any(
                             descriptor => string.Equals(
                                 descriptor.Id,
                                 id,
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Pipeline Step '{id}' has no installed implementation.");
                }
            }
        }

        private AssetContentProviderDescriptor FindProviderDescriptor(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return providerDescriptors.FirstOrDefault(
                descriptor => string.Equals(
                    descriptor.ProviderId,
                    providerId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateOutputRoot(string value, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add("Output Base Directory is required.");
                return;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(value, "Output Base Directory");
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void ValidateVersionInfoPath(string value, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Contains("\\")
                || !value.StartsWith("Assets/", StringComparison.Ordinal)
                || !value.EndsWith("/" + VersionInfoFileName, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Version Info Destination must resolve to an Assets folder containing '{VersionInfoFileName}'.");
                return;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(value, "Version Info Asset Path");
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void TryValidatePortableFileName(
            string value,
            string label,
            ICollection<string> errors)
        {
            try
            {
                BuildPathPolicy.ValidatePortableFileName(value, label);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void ValidateRequired(string value, string label, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(label + " is required.");
            }
        }

        private static string GetAssetDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            string directory = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(directory) ? string.Empty : directory.Replace('\\', '/');
        }
    }
}
