#if UNITY_EDITOR

using System.IO;
using System.Linq;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using scene = UnityEngine.SceneManagement.Scene;

namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>A window for debugging cross-scene references.</summary>
    public class CrossSceneDebugger : EditorWindow
    {

        [SerializeField] private SerializableStringBoolDict expanded = new SerializableStringBoolDict();

        /// <summary>Opens the cross-scene reference debugger.</summary>
        [MenuItem("Window/Advanced Scene Manager/Cross-scene reference debugger", priority = 3031)]
        public static void Open()
        {

            var window = CreateInstance<CrossSceneDebugger>();
            window.titleContent = new GUIContent("Cross-scene references");
            window.minSize = new Vector2(800, 225);
            window.maxSize = new(800, 1500);
            window.ShowUtility();

            foreach (var obj in Resources.FindObjectsOfTypeAll<CrossSceneDebugger>().Except(window))
                obj.Close();

        }

        void OnEnable()
        {

            OnCrossSceneReferencesSaved();
            OnSceneStatusChanged();
            CrossSceneReferenceUtility.OnSaved += OnCrossSceneReferencesSaved;
            CrossSceneReferenceUtility.OnSceneStatusChanged += OnSceneStatusChanged;

            //Load variables from editor prefs
            var json = EditorPrefs.GetString("AdvancedSceneManager.CrossSceneDebugger", JsonUtility.ToJson(this));
            JsonUtility.FromJsonOverwrite(json, this);

        }

        void OnDisable()
        {

            CrossSceneReferenceUtility.OnSaved -= OnCrossSceneReferencesSaved;
            CrossSceneReferenceUtility.OnSceneStatusChanged -= OnSceneStatusChanged;

            //Save variables to editor prefs
            var json = JsonUtility.ToJson(this);
            EditorPrefs.SetString("AdvancedSceneManager.CrossSceneDebugger", json);

        }

        void OnCrossSceneReferencesSaved() =>
            RefreshScreens();

        void OnSceneStatusChanged() =>
            RefreshScreens();

        void SetStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{SceneManager.package.folder}/System/Editor/UI/SceneManagerWindow/Styles/Base.uss");
            if (style)
                rootVisualElement.styleSheets.Add(style);
            else
                Debug.LogWarning("Could not apply style sheet to cross-scene reference debugger.");
        }

        ScrollView scroll;
        VisualElement notEnabledScreen;
        VisualElement noItemsScreen;
        VisualElement listScreen;
        void CreateGUI()
        {

            SetStyle();

            //var resolveButton = new Button(() => { CrossSceneReferenceUtility.ResetAllScenes(); CrossSceneReferenceUtility.ResolveAllScenes(); }) { text = "Resolve open scenes" };
            //resolveButton.style.alignSelf = Align.FlexEnd;
            //rootVisualElement.Add(resolveButton);

            rootVisualElement.Add(scroll ??= new ScrollView());
            rootVisualElement.AddToClassList("cross-scene");

            scroll.Add(notEnabledScreen ??= new());
            scroll.Add(noItemsScreen ??= new());
            scroll.Add(listScreen ??= new());

            scroll.contentContainer.style.paddingLeft = 16;
            scroll.contentContainer.style.paddingTop = 16;
            scroll.contentContainer.style.paddingRight = 16;
            scroll.contentContainer.style.paddingBottom = 16;

            SetupNotEnabledScreen(notEnabledScreen);
            SetupNoItemsScreen(noItemsScreen);

            RefreshScreens();

        }

        void RefreshScreens()
        {

            if (notEnabledScreen is null || noItemsScreen is null || listScreen is null)
                return;

            notEnabledScreen.style.display = DisplayStyle.None;
            noItemsScreen.style.display = DisplayStyle.None;
            listScreen.style.display = DisplayStyle.None;

            if (!SceneManager.settings.project.enableCrossSceneReferences)
                notEnabledScreen.style.display = DisplayStyle.Flex;
            else if (!SceneManager.assets.scenes.Select(s => s.crossSceneReferences).Any())
                noItemsScreen.style.display = DisplayStyle.Flex;
            else
            {
                SetupListScreen(listScreen);
                listScreen.style.display = DisplayStyle.Flex;
            }

        }

        #region Not enabled screen

        void SetupNotEnabledScreen(VisualElement element)
        {

            element.Clear();
            element.AddToClassList("errorMessage");

            element.Add(new Label("Cross-scene references are not enabled."));
            element.Add(new Button(() => SceneManager.settings.project.enableCrossSceneReferences = true) { text = "Enable" });

        }

        #endregion
        #region No items screen

        void SetupNoItemsScreen(VisualElement element)
        {

            element.Clear();
            element.AddToClassList("errorMessage");

            element.Add(new Label("Cross-scene references are not enabled."));
            element.Add(new Label("You can create some by dragging and dropping some references around from different scenes."));
            element.Add(new Label("They will show up here when scene is saved."));

        }

        #endregion
        #region List screen

        void SetupListScreen(VisualElement element)
        {

            element.Clear();

            foreach (var scene in SceneManager.assets.scenes.Where(s => s.crossSceneReferences.Any()))
            {

                var foldout = new Foldout { text = Path.GetFileNameWithoutExtension(scene.name) };
                element.Add(foldout);

                foreach (var reference in scene.crossSceneReferences)
                {

                    var item = CreateListElement(scene, reference);
                    item.AddToClassList("list-item");

                    foldout.Add(item);

                }

            }

        }

        VisualElement CreateListElement(Scene scene, CrossSceneReference reference)
        {

            var resolved = CrossSceneReferenceUtility.GetResolved(reference);
            var element = new Foldout() { text = GetHeader() };

            element.Add(SetupRemoveButton());
            element.Add(SetupRow1(resolved.variable.resolve));
            element.Add(SetupRow2(resolved.value.resolve));

            string GetHeader() =>
                resolved.variable.resolve.gameObject
                ? new(resolved.variable.resolve.ToString(includeScene: false))
                : new(reference.variable.ToString());

            VisualElement SetupRow1(ResolvedReference reference)
            {

                var element = new VisualElement();

                var field1 = new ObjectField() { label = "Variable: " };
                field1.SetEnabled(false);
                field1.objectType = typeof(GameObject);

                if (reference.gameObject)
                    field1.SetValueWithoutNotify(reference.gameObject);
                else
                    field1.Q<Label>(className: "unity-object-field-display__label").text = " -- " + reference.result + " --";

                element.Add(field1);

                return element;

            }

            VisualElement SetupRow2(ResolvedReference reference)
            {

                var element = new VisualElement();

                var field1 = new ObjectField() { label = "Value: " };
                field1.SetEnabled(false);
                field1.objectType = typeof(GameObject);

                if (reference.gameObject)
                    field1.SetValueWithoutNotify(reference.gameObject);
                else
                    field1.Q<Label>(className: "unity-object-field-display__label").text = " -- Scene not loaded --";

                element.Add(field1);

                return element;

            }

            VisualElement SetupRemoveButton()
            {

                var button = new Button(() => { scene.RemoveCrossSceneReference(reference); RefreshScreens(); });
                button.AddToClassList("button-remove");
                button.AddToClassList("fontAwesome");
                button.text = "";
                button.tooltip = "Remove reference";

                return button;

            }

            return element;

        }

        #endregion

    }

}
#endif
