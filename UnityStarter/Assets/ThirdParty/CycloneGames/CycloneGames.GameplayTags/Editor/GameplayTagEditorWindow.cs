using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
    public class GameplayTagEditorWindow : EditorWindow
    {
        private ManagerTreeView treeView;
        private TreeViewState treeViewState;
        private int cachedTagCount;

        [MenuItem("Tools/CycloneGames/GameplayTags/Gameplay Tag Manager")]
        public static void ShowWindow()
        {
            GetWindow<GameplayTagEditorWindow>("Gameplay Tag Manager");
        }

        private void OnEnable()
        {
            treeViewState = new TreeViewState();
            treeView = new ManagerTreeView(treeViewState);
            cachedTagCount = GameplayTagManager.GetAllTags().Length;
            GameplayTagManager.OnGameplayTagTreeChanged += OnTreeChanged;
        }

        private void OnDisable()
        {
            GameplayTagManager.OnGameplayTagTreeChanged -= OnTreeChanged;
        }

        private void OnTreeChanged()
        {
            cachedTagCount = GameplayTagManager.GetAllTags().Length;
            treeView?.Reload();
            Repaint();
        }

        private void OnGUI()
        {
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Gameplay Tag Manager", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));

            GUILayout.Label($"{cachedTagCount} tags", EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandWidth(false));

            EditorGUILayout.EndHorizontal();

            // Refresh button
            if (GUILayout.Button("Refresh Tags & Generate Code", GUILayout.Height(24)))
            {
                GameplayTagManager.ReloadTags();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                treeView.Reload();
            }

            // Tree view fills remaining space
            Rect treeRect = GUILayoutUtility.GetRect(0, position.width, 0, position.height, GUILayout.ExpandHeight(true));
            treeView.OnGUI(treeRect);
        }

        private class ManagerTreeView : GameplayTagTreeViewBase
        {
            public ManagerTreeView(TreeViewState state) : base(state)
            {
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                float indent = GetContentIndent(args.item);
                Rect rect = args.rowRect;
                rect.xMin += indent + 2 - (hasSearch ? 14 : 0);

                if (args.item is GameplayTagTreeViewItem item)
                    DoTagRowGUI(rect, item);
            }
        }
    }
}
