using System.Reflection;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class DevMenuView : ViewModel, IView
    {

        public override void OnAdded()
        {
            rootVisualElement.Q("button-menu").ContextMenu(AddMenuActions);
        }

        void AddMenuActions(ContextualMenuPopulateEvent e)
        {

            e.menu.AppendAction("View/ASM folder...", _ => ShowFolder(SceneManager.package.folder));
            e.menu.AppendAction("View/Window source...", _ => ShowFolder(WindowPath()));

            e.menu.AppendSeparator("View/");
            e.menu.AppendAction("View/Profiles...", _ => ShowFolder(ProfilePath()));
            e.menu.AppendAction("View/Imported scenes...", _ => ShowFolder(ScenePath()));

            e.menu.AppendSeparator("View/");
            e.menu.AppendAction("View/Settings...", _ =>
            {
                var w = ScriptableObject.CreateInstance<InspectorWindow>();
                w.titleContent = new GUIContent("Project settings");
                w.editor = UnityEditor.Editor.CreateEditor(SceneManager.settings.project);
                w.ShowUtility();
            });
            e.menu.AppendAction("View/User settings...", _ =>
            {
                var w = ScriptableObject.CreateInstance<InspectorWindow>();
                w.titleContent = new GUIContent("Project settings");
                w.editor = UnityEditor.Editor.CreateEditor(SceneManager.settings.user);
                w.ShowUtility();
            });

            e.menu.AppendSeparator();
            e.menu.AppendAction("Tools/Force all notifications visible...", e => notifications.forceAllNotificationsVisible = !notifications.forceAllNotificationsVisible, notifications.forceAllNotificationsVisible ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            e.menu.AppendAction("Tools/Unset profile...", e => Profile.SetProfile(null));
            e.menu.AppendAction("Tools/Reload collection view...", e => collectionView.Reload());
            e.menu.AppendSeparator("Tools");
            e.menu.AppendAction("Tools/Add ASM Defaults collection", e => Profile.current.AddDefaultASMScenes());

            e.menu.AppendSeparator();
            e.menu.AppendAction("Recompile...", _ => UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation());

            e.menu.AppendSeparator();

#if UNITY_2022_1_OR_NEWER

#if ASM_DEV
            e.menu.AppendAction("Enable dev features", (e) => ToggleScriptingDefine("ASM_DEV"), DropdownMenuAction.Status.Checked);
#else
            e.menu.AppendAction("Enable dev features", (e) => ToggleScriptingDefine("ASM_DEV"), DropdownMenuAction.Status.Normal);
#endif

#endif

        }

#if UNITY_2022_1_OR_NEWER

        void ToggleScriptingDefine(string define)
        {
            // Get the current define symbols for the active build target
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup));

            if (defines.Contains(define))
            {
                defines = defines.Replace(define, "");
            }
            else
            {
                if (!EditorUtility.DisplayDialog(
                    "Enable dev features",
                    "This will enable experimental features currently under development.\n" +
                    "The features may not work, are you sure you wish to proceed?\n" +
                    "\n" +
                    "Note that if you receive compilation errors and the window does not work, then you may remove the scripting define #ASM_DEV from project settings to disable manually.", "Continue", "Cancel"))
                    return;

                defines = defines + ";" + define;
            }

            // Apply the new set of defines
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup), defines);
        }
#endif

        string WindowPath() => SceneManager.package.folder + "/Plugin/Editor/UI/SceneManagerWindow";
        string ProfilePath() => Assets.GetFolder<Profile>();
        string ScenePath() => Assets.GetFolder<Scene>();

        class InspectorWindow : EditorWindow
        {

            public UnityEditor.Editor editor;

            GUIStyle style;
            private void OnEnable()
            {
                style = new GUIStyle() { padding = new RectOffset(20, 20, 20, 20) };
            }

            Vector2 scrollPos;
            private void OnGUI()
            {


                scrollPos = GUILayout.BeginScrollView(scrollPos, style);
#if !ASM_DEV
                GUI.enabled = false;
#endif

                EditorGUI.BeginChangeCheck();

                if (editor)
                    editor.DrawDefaultInspector();

                var changed = EditorGUI.EndChangeCheck();

#if ASM_DEV
                if (changed)
                    editor.SaveChanges();
#endif

                GUI.enabled = true;
                GUILayout.EndScrollView();
            }

        }

        #region Open folder in project view

        static void ShowFolder(string path) =>
            ShowFolder(AssetDatabase.LoadAssetAtPath<Object>(path).GetInstanceID());

        /// <summary>
        /// Selects a folder in the project window and shows its content.
        /// Opens a new project window, if none is open yet.
        /// </summary>
        /// <param name="folderInstanceID">The instance of the folder asset to open.</param>
        static void ShowFolder(int folderInstanceID)
        {

            // Find the internal ProjectBrowser class in the editor assembly.
            var editorAssembly = typeof(EditorApplication).Assembly;
            var projectBrowserType = editorAssembly.GetType("UnityEditor.ProjectBrowser");

            // This is the internal method, which performs the desired action.
            // Should only be called if the project window is in two column mode.
            var showFolderContents = projectBrowserType.GetMethod("ShowFolderContents", BindingFlags.Instance | BindingFlags.NonPublic);

            // Find any open project browser windows.
            var projectBrowserInstances = Resources.FindObjectsOfTypeAll(projectBrowserType);

            if (projectBrowserInstances.Length > 0)
            {
                for (int i = 0; i < projectBrowserInstances.Length; i++)
                    ShowFolderInternal(projectBrowserInstances[i], showFolderContents, folderInstanceID);
            }
            else
            {
                var projectBrowser = OpenNewProjectBrowser(projectBrowserType);
                ShowFolderInternal(projectBrowser, showFolderContents, folderInstanceID);
            }

        }

        static void ShowFolderInternal(Object projectBrowser, MethodInfo showFolderContents, int folderInstanceID)
        {

            // Sadly, there is no method to check for the view mode.
            // We can use the serialized object to find the private property.
            var serializedObject = new SerializedObject(projectBrowser);
            var inTwoColumnMode = serializedObject.FindProperty("m_ViewMode").enumValueIndex == 1;

            if (!inTwoColumnMode)
            {
                // If the browser is not in two column mode, we must set it to show the folder contents.
                var setTwoColumns = projectBrowser.GetType().GetMethod("SetTwoColumns", BindingFlags.Instance | BindingFlags.NonPublic);
                setTwoColumns.Invoke(projectBrowser, null);
            }

            var revealAndFrameInFolderTree = true;
            showFolderContents.Invoke(projectBrowser, new object[] { folderInstanceID, revealAndFrameInFolderTree });

        }

        static EditorWindow OpenNewProjectBrowser(System.Type projectBrowserType)
        {

            var projectBrowser = EditorWindow.GetWindow(projectBrowserType);
            projectBrowser.Show();

            // Unity does some special initialization logic, which we must call,
            // before we can use the ShowFolderContents method (else we get a NullReferenceException).
            var init = projectBrowserType.GetMethod("Init", BindingFlags.Instance | BindingFlags.Public);
            init.Invoke(projectBrowser, null);

            return projectBrowser;

        }

        #endregion

    }

}
