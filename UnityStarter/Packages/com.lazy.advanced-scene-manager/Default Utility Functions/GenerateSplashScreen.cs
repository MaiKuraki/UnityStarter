using System.IO;
using AdvancedSceneManager.Editor.UI;
using AdvancedSceneManager.Utility;
using AdvancedSceneManager.UtilityFunctions.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.UtilityFunctions
{
    public class GenerateSplashScreen : ASMUtilityFunction
    {
        public override string Name { get => "+ SplashScreen"; }
        public override string Description { get => "Generates a new empty splash screen to build off."; }
        public override string Group { get => "Loadingscreen"; }

        public override void OnInvoke(ref VisualElement optionsGUI)
        {
            VisualElement visualElement = new();


            TextField targetName = new ("Name");

            VisualElement pathElement = PathComponent.PathPicker("Target folder", "", "", out var address);

            Button button = new (() => Create(targetName.value, address.value)) { text = "Create" };

            visualElement.Add(targetName);
            visualElement.Add(pathElement);
            visualElement.Add(button);
            optionsGUI = visualElement;
        }

        private void Create(string targetName, string address)
        {
            var fileName = $"{ targetName }SplashScreen";
            var path = $"{address}/{fileName}/{fileName}";
            SceneUtility.CreateAndImport(path);

            GenerateScript(path, fileName);
        }

        private void GenerateScript(string _path, string className)
        {
            string path = $"{_path}.cs";

            if (File.Exists(path))
            {
                Debug.LogWarning($"A script with the name {className} already exists at {path}.");
                return;
            }


            string scriptContent =
                "using UnityEngine;\n" +
                "using System.Collections;\n" +
                "using AdvancedSceneManager.Loading;\n\n" +
                $"public class {className} : SplashScreen\n" +
                "{\n" +
                "    /// <summary>Coroutine called when the splash screen opens.</summary>\n" +
                "    public override IEnumerator OnOpen()\n" +
                "    {\n" +
                "        yield return null;\n" +
                "    }\n\n" +
                "    /// <summary>Coroutine called when the splash screen closes.</summary>\n" +
                "    public override IEnumerator OnClose()\n" +
                "    {\n" +
                "        yield return null;\n" +
                "    }\n" +
                "}";

            File.WriteAllText(path, scriptContent);
            AssetDatabase.Refresh();
        }
    }
}