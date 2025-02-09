using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Generic;
using System.IO;

#if UNITY_EDITOR
using UnityEditor.UIElements;
using AdvancedSceneManager.Editor.Utility;
using UnityEditor.Compilation;
using UnityEditor;
#endif

namespace AdvancedSceneManager.Models.Helpers
{

#if ASM_DEV
    [CreateAssetMenu(menuName = "ASM dev/Pre-imported scenes manager")]
#endif
    public class PreimportedScenesManager : ScriptableObject
    {

        public SerializableDictionary<Object, Scene> scenes;

#if UNITY_EDITOR

        [CustomEditor(typeof(PreimportedScenesManager))]
        class Editor : UnityEditor.Editor
        {

            List<(Object, Scene)> scenes;
            public void OnEnable()
            {

                var scenes = ((PreimportedScenesManager)target).scenes;
                var assets = AssetDatabaseUtility.FindAssets<SceneAsset>(AssetDatabase.GetAssetPath(target));

                Debug.Log(AssetDatabase.GetAssetPath(target));

                foreach (var scene in scenes.Keys)
                    if (!assets.Contains(scene))
                        scenes.Remove(scene);

                foreach (var scene in assets)
                    if (!scenes.ContainsKey(scene))
                        scenes.Add(scene, FindScene(scene));

                this.scenes = scenes.Select(kvp => (kvp.Key, kvp.Value)).ToList();

                Scene FindScene(SceneAsset scene)
                {
                    var path = AssetDatabase.GetAssetPath(scene);
                    var folder = Path.GetDirectoryName(path);
                    var name = Path.GetFileNameWithoutExtension(path);
                    return AssetDatabase.LoadAssetAtPath<Scene>(folder + "/" + name + ".asset");
                }

            }

            public override VisualElement CreateInspectorGUI()
            {

                var view = new VisualElement();

                var list = new ListView();

                list.makeItem = MakeItem;
                list.bindItem = BindItem;
                list.unbindItem = UnbindItem;

                list.itemsSource = scenes;

                var button = new Button(CompilationPipeline.RequestScriptCompilation);
                button.text = "Import scenes now";

                view.Add(list);
                view.Add(button);
                view.Add(new Label("Scenes will be automatically imported on recompile, or editor startup."));

                return view;

            }

            VisualElement MakeItem()
            {

                var element = new VisualElement();
                element.style.flexDirection = FlexDirection.Row;

                var assetField = new ObjectField() { name = "picker-asset" };
                var sceneField = new ObjectField() { name = "picker-scene" };

                assetField.objectType = typeof(SceneAsset);
                sceneField.objectType = typeof(Scene);

                assetField.style.flexGrow = 1;
                sceneField.style.flexGrow = 1;

                sceneField.SetEnabled(false);

                element.Add(assetField);
                element.Add(sceneField);

                return element;

            }

            void BindItem(VisualElement element, int i)
            {

                var (asset, scene) = scenes[i];
                element.Q<ObjectField>("picker-asset").SetValueWithoutNotify(asset);
                element.Q<ObjectField>("picker-scene").SetValueWithoutNotify(scene);

            }

            void UnbindItem(VisualElement element, int i)
            {

            }

        }

#endif

    }

}
