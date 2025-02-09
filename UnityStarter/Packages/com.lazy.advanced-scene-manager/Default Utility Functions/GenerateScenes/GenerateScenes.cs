using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.UtilityFunctions
{

    public class GenerateScenes : ASMUtilityFunction
    {
        public override string Name => "Generate Scenes";
        public override string Description => "Generates scenes for the selected collection, with prefix";
        public override string Group => "Generation";

        private static readonly string UXMLAddress = $"{SceneManager.package.folder}/Default Utility Functions/GenerateScenes/GenerateScenes.uxml";

        public static Func<SceneAsset[], IEnumerator> OnProcessSceneAssets;

        public override void OnInvoke(ref VisualElement optionsGUI)
        {
            // You can create a UI in UIBuilder and reference it.
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXMLAddress);
            VisualElement root = tree.CloneTree();

            var target = root.Q<ObjectField>("SceneCollectionPicker");
            var folderField = root.Q<TextField>("Folder");
            var prefixField = root.Q<TextField>("Prefix");
            var countField = root.Q<TextField>("Count");
            var import = root.Q<Toggle>("Import");
            var createParent = root.Q<Toggle>("CreateParent");
            var parentPivot = root.Q<Vector3Field>("ParentPivot");
            var pivotOffset = root.Q<Vector3Field>("PivotOffset");

            import.RegisterCallback<ChangeEvent<bool>>((evt) =>
            {
                target.SetEnabled(evt.newValue);

                if (!evt.newValue)
                    target.value = null;
            });

            createParent.RegisterCallback<ChangeEvent<bool>>((evt) =>
            {
                parentPivot.SetEnabled(evt.newValue);
                pivotOffset.SetEnabled(evt.newValue);
            });


            Button generateButton = root.Q<Button>("GenerateBtn");
            generateButton.clicked += () =>
            {
                var options = new GenerateOptions
                {
                    Collection = (SceneCollection)target.value,
                    Folder = folderField.value,
                    Prefix = prefixField.value,
                    Count = countField.value,
                    Import = import.value,
                    CreateParent = createParent.value,
                    ParentPivot = parentPivot.value,
                    PivotOffset = pivotOffset.value,
                };

                EditorUtility.DisplayProgressBar("Generating Scenes", "Processing...", 0f);

                Generate(options).StartCoroutine();
            };

            optionsGUI = root;
        }

        private IEnumerator Generate(GenerateOptions options)
        {

            GeneratePrefixes(options, out string path, out List<(string, Vector3)> prefixes);

            IEnumerable<object> scenes = (options.Import) ?
                SceneUtility.CreateAndImport(Enumerable.Repeat(path, prefixes.Count).Select((path, i) => path + $"{prefixes[i].Item1}.unity")) :
                SceneUtility.Create(Enumerable.Repeat(path, prefixes.Count).Select((path, i) => path + $"{prefixes[i].Item1}.unity"));

            if (scenes.Count() != prefixes.Select(p => p.Item2).Count())
            {
                Debug.LogError("Mismatch between number of scenes and vector3s.");
                yield break;
            }

            SceneAsset[] sceneAssets = GetSceneAssets(scenes);

            if (options.Collection != null)
            {
                EditorUtility.DisplayProgressBar("Generating Scenes", "Adding to Collection...", 0.2f);

                // only imported can be added so we know its this type
                options.Collection.Add(((IEnumerable<Scene>)scenes).ToArray());
            }

            if (options.CreateParent)
            {
                EditorUtility.DisplayProgressBar("Generating Scenes", "Creating parents...", 0.5f);

                yield return AddParentToScenes(sceneAssets, prefixes.Select(p => p.Item2).ToArray());
            }

            EditorUtility.DisplayProgressBar("Generating Scenes", "Awaiting callbacks...", 0.7f);

            yield return OnProcessSceneAssets?.Invoke(sceneAssets);

            EditorUtility.ClearProgressBar();
            Debug.Log($"Generated {prefixes.Count} scenes in '{options.Folder}'. Prefixed with '{options.Prefix}'.");
        }

        private static SceneAsset[] GetSceneAssets(IEnumerable<object> scenes)
        {
            SceneAsset[] sceneAssets;

            if (scenes is IEnumerable<Scene> sceneEnumerable)
            {
                // Extract SceneAsset from each Scene
                sceneAssets = sceneEnumerable.Select(scene => scene.sceneAsset).ToArray();
            }
            else if (scenes is IEnumerable<SceneAsset> assetEnumerable)
            {
                // Handle other cases or convert items directly if possible
                sceneAssets = assetEnumerable.ToArray();
            }
            else
            {
                Debug.LogError("Invalid scene collection.");
                sceneAssets = Array.Empty<SceneAsset>();
            }

            return sceneAssets;
        }

        private static void GeneratePrefixes(GenerateOptions options, out string path, out List<(string, Vector3)> prefixes)
        {
            path = $"{options.Folder.TrimEnd('/', '\\')}/";

            // Step 1: Parse dimensions
            int[] ranges = Array.ConvertAll(options.Count.Split(';'), int.Parse).Take(3).ToArray();

            // Step 2: Generate all combinations
            List<int[]> combinations = new();
            GenerateCombinations(ranges, 0, new int[ranges.Length], combinations);

            prefixes = new();

            // Step 3: Replace placeholders in the prefix
            foreach (var combination in combinations)
            {
                string levelName = options.Prefix;

                for (int i = 0; i < combination.Length; i++)
                {
                    levelName = levelName.Replace($"{{{i + 1}}}", combination[i].ToString());
                }

                // Replace missing placeholders with default values
                for (int i = combination.Length; i < 3; i++)
                {
                    levelName = levelName.Replace($"{{{i + 1}}}", "0");
                }

                // Handle pivot safely, ensuring we account for missing dimensions
                float x = combination.Length > 0 ? combination[0] : 0;
                float y = combination.Length > 1 ? combination[1] : 0;
                float z = combination.Length > 2 ? combination[2] : 0;

                Vector3 pivot = new(x, y, z);
                Vector3 pivotMultiplied = Vector3.Scale(pivot, options.ParentPivot);
                Vector3 pivotOffsetted = pivotMultiplied - options.PivotOffset;

                prefixes.Add((levelName, pivotOffsetted));
            }

            // Recursive function to generate all combinations
            static void GenerateCombinations(int[] ranges, int depth, int[] current, List<int[]> results)
            {
                if (depth == ranges.Length)
                {
                    // Base case: Add a copy of the current combination to the results
                    results.Add((int[])current.Clone());
                    return;
                }

                // Recursive case: Iterate over the range for the current dimension
                for (int i = 1; i <= ranges[depth]; i++)
                {
                    current[depth] = i;
                    GenerateCombinations(ranges, depth + 1, current, results);
                }
            }

        }

        private IEnumerator AddParentToScenes(SceneAsset[] sceneAssets, Vector3[] vector3s)
        {
            for (int i = 0; i < sceneAssets.Length; i++)
            {
                SceneAsset sceneAsset = sceneAssets[i];
                Vector3 position = vector3s[i];

                // Load the scene by its path
                string scenePath = AssetDatabase.GetAssetPath(sceneAsset);
                UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (!scene.IsValid())
                {
                    Debug.LogError($"Failed to load scene: {scenePath}");
                    continue;
                }

                // wait until scene is loaded

                EditorSceneManager.SetActiveScene(scene);
                yield return null;

                // Create a new GameObject and set its position
                GameObject newObject = new GameObject("Pivot");
                newObject.transform.position = position;

                // Mark the scene as dirty to enable saving
                EditorSceneManager.MarkSceneDirty(scene);

                // Save the scene
                EditorSceneManager.SaveScene(scene);

                EditorSceneManager.CloseScene(scene, true);

                yield return null;
            }
        }

        private class GenerateOptions
        {
            public SceneCollection Collection { get; set; }
            public string Folder { get; set; }
            public string Prefix { get; set; }
            public string Count { get; set; }
            public bool Import { get; set; }
            public bool CreateParent { get; set; }
            public Vector3 ParentPivot { get; set; }
            public Vector3 PivotOffset { get; set; }
        }
    }
}
