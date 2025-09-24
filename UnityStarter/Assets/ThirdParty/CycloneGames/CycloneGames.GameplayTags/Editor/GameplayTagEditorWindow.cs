using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
    public class GameplayTagEditorWindow : EditorWindow
    {
        private SearchField searchField;
        private GameplayTagTreeView treeView;
        private TreeViewState treeViewState;

        [MenuItem("Tools/CycloneGames/Gameplay Tag Manager")]
        public static void ShowWindow()
        {
            GetWindow<GameplayTagEditorWindow>("Gameplay Tag Manager");
        }

        private void OnEnable()
        {
            treeViewState = new TreeViewState();
            treeView = new GameplayTagTreeView(treeViewState);
            searchField = new SearchField();
            searchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;
            ReloadTreeView();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Gameplay Tag Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Refresh Tags & Generate Code"))
            {
                // Step 1: Reload the tags in memory.
                GameplayTagManager.ReloadTags();
                
                // Step 2: Force Unity to recompile, which will trigger the source generator.
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                // Step 3: Reload the tree view to show the latest tags.
                ReloadTreeView();
                
                // Step 4: Inform the user with a clear log and a simple dialog.
                Debug.Log("<b>Gameplay Tag Source Generation Triggered</b>\n" +
                          "The 'AllGameplayTags.g.cs' file is a virtual file generated during compilation. " +
                          "To view its contents, open your project in an IDE like <b>Visual Studio</b> or <b>Rider</b> and navigate to:\n" +
                          "<i>Solution Explorer -> YourUnityProject -> Analyzers -> CycloneGames.GameplayTags.SourceGenerator -> AllGameplayTags.g.cs</i>");

                EditorUtility.DisplayDialog("Process Started", "Tags have been refreshed and a recompile has been triggered to update the 'AllGameplayTags' class. Please check the Console for more details.", "OK");
            }

            EditorGUILayout.Space();
            
            string searchString = searchField.OnGUI(treeView.searchString);
            if (searchString != treeView.searchString)
            {
                treeView.searchString = searchString;
                treeView.Reload();
            }

            Rect rect = GUILayoutUtility.GetLastRect();
            Rect treeRect = new Rect(rect.x, rect.y + EditorGUIUtility.singleLineHeight, position.width, position.height - rect.y - EditorGUIUtility.singleLineHeight * 2);
            treeView.OnGUI(treeRect);
        }

        private void ReloadTreeView()
        {
            if (treeView != null)
            {
                treeView.Reload();
            }
        }
    }
}
