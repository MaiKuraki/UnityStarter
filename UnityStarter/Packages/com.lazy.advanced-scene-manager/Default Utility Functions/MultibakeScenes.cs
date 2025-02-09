using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AdvancedSceneManager.Editor.UI;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.UtilityFunctions.Components;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEngine.Debug;

namespace AdvancedSceneManager.UtilityFunctions
{

    public class MultibakeScenes : ASMUtilityFunction
    {

        public override string Name { get => "Multibake Scenes"; }
        public override string Description { get => "Generates lightmapping for the scenes in an selected collection"; }
        public override string Group { get => "Lightmapping"; }

        // Regex to find the lighting setting data in the metafile.
        private static readonly Regex lightingSettingsRegex = new(@"m_LightingSettings: \{fileID: \d+, guid: ([a-f0-9]+),", RegexOptions.Compiled);

        public override void OnInvoke(ref VisualElement optionsGUI)
        {

            // Create the root container
            var visualElement = new VisualElement();
            visualElement.style.flexDirection = FlexDirection.Column;

            // Create UI elements
            var info = new Label("Create a temporary collection if you want a special bake.");
            ObjectField sceneCollectionField = new() { objectType = typeof(SceneCollection) };

            var infoLighting = new Label("This will only bake scenes in your choosen collection that has a lighting setting assigned.");
            Toggle onlyBakeScenesWithLightingSettings = new("Only Bake Scenes With Lighting Settings Assigned.")
            {
                value = true
            };

            var infoForce = new Label("This will cause the bake to stop, highlighting mismatching lighting settings. Otherwise it will just warn.");
            Toggle forceSameLightmapperToggle = new("Force Same Lighting Setting.")
            {
                value = true
            };

            var warning = new Label("If the lighting setting field is set as missing, it might trick the metadata. delete any missing fields.");
            warning.style.color = new Color(1.0f, 0.8f, 0.0f); // Softer warning yellow color
            warning.style.unityFontStyleAndWeight = FontStyle.Bold; // Bold text

            // Create the bake button
            Button bakeButton = new(() =>
            {
                if((SceneCollection)sceneCollectionField.value == null)
                {
                    LogError("Scene Collection cannot be null.");
                    return;
                }

                BakeLightmaps((SceneCollection)sceneCollectionField.value, onlyBakeScenesWithLightingSettings.value, forceSameLightmapperToggle.value);
            }) { text = "Bake Lightmaps" };

            // Add elements to the root container
            visualElement.Add(info);
            visualElement.Add(sceneCollectionField);
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing

            visualElement.Add(infoLighting);
            visualElement.Add(onlyBakeScenesWithLightingSettings);
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing

            visualElement.Add(infoForce);
            visualElement.Add(forceSameLightmapperToggle);
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing

            visualElement.Add(warning);
            visualElement.Add(SpaceComponent.Create(20));  // Add spacing
            visualElement.Add(bakeButton);

            // Assign the visualElement as the optionsGUI
            optionsGUI = visualElement;

        }

        struct SceneWithGUID
        {
            public string GUID;
            public Scene scene;
        }

        private static void BakeLightmaps(SceneCollection collection, bool onlyBake, bool forceSame)
        {

            // Get filtered scenes based on the 'onlyBake' flag
            var filteredScenes = onlyBake
                ? collection
                    .Select(scene => new SceneWithGUID { GUID = GetLightingSettingsGUID(scene), scene = scene })
                    .Where(s => !string.IsNullOrEmpty(s.GUID)) // Only include scenes with valid lighting settings
                    .Distinct()
                    .ToArray()
                : collection
                    .Select(scene => new SceneWithGUID { GUID = null, scene = scene }) // Include all scenes without filtering
                    .Distinct()
                    .ToArray();

            // Early return if there are no scenes to process
            if (filteredScenes.Length == 0)
            {
                LogWarning("No scenes found to bake lightmaps.");
                return;
            }

            // Check if all GUIDs are the same
            var allGUIDsSame =
                filteredScenes.All(s => s.GUID != null) &&
                filteredScenes.All(s => s.GUID == filteredScenes[0].GUID);

            if (!allGUIDsSame && forceSame)
            {
                LogError("Not all scenes have the same lighting settings. Please check the scenes.");
                return;
            }

            // Extract scene paths for baking
            var scenePaths = filteredScenes.Select(s => s.scene.path);
            Lightmapping.BakeMultipleScenes(scenePaths.ToArray());

            // Log the result
            Log($"Lightmaps generated for: {string.Join(", ", scenePaths)}");

        }

        private static string GetLightingSettingsGUID(Scene scene)
        {

            // Get the metadata
            var sceneFileContent = File.ReadAllText(scene.path);
            // Find the data
            var match = lightingSettingsRegex.Match(sceneFileContent);

            if (!match.Success)
                return string.Empty;

            return match.Groups[1].Value;

        }

    }
}
