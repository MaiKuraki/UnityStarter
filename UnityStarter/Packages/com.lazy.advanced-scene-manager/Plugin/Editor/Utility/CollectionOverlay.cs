#if UNITY_2022_1_OR_NEWER
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace AdvancedSceneManager.Editor.Utility
{

    [InitializeInEditor]
    [Overlay(typeof(SceneView), "Collections", defaultDisplay = true, defaultDockPosition = DockPosition.Bottom, defaultDockZone = DockZone.LeftColumn)]
    public class CollectionOverlay : IMGUIOverlay
    {

        static GUIContent pinContent;
        static GUIContent unpinContent;
        static GUIStyle pinButton;
        static CollectionOverlay() =>
            SceneManager.OnInitialized(() =>
            {
                SceneManager.runtime.sceneOpened += _ => Refresh();
                SceneManager.runtime.sceneClosed += _ => Refresh();
                SceneManager.runtime.collectionOpened += _ => Refresh();
                SceneManager.runtime.collectionClosed += _ => Refresh();
                SceneManager.settings.user.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(SceneManager.settings.user.pinnedOverlayCollections)) Refresh(); };
                Refresh();
            });

        private static void Refresh()
        {

            pinnedCollections = SceneManager.settings.user.pinnedOverlayCollections.ToArray();
            collections = SceneManager.openScenes.Select(s => s.FindCollection()).Except(pinnedCollections).Distinct().NonNull().ToArray();

            EditorApplication.QueuePlayerLoopUpdate();

        }

        static SceneCollection[] pinnedCollections;
        static SceneCollection[] collections;

        public override void OnGUI()
        {

            HandleDragDrop();

            if (pinContent == null) pinContent = new GUIContent(EditorGUIUtility.IconContent("pin").image, "Pin collection");
            if (unpinContent == null) unpinContent = new GUIContent("x", "Unpin collection");
            if (pinButton == null) pinButton = new GUIStyle(EditorStyles.iconButton) { fixedHeight = 20, margin = new(0, 0, 2, 0), alignment = TextAnchor.MiddleCenter };

            if (pinnedCollections?.Any() ?? false)
            {
                GUILayout.Space(12);
                foreach (var collection in pinnedCollections)
                    DrawCollection(collection, true);
            }

            if (collections?.Any() ?? false)
            {

                if (pinnedCollections.Any())
                    DrawSeparator();

                GUILayout.Space(12);
                foreach (var collection in collections)
                    DrawCollection(collection, false);

            }

            if (!(pinnedCollections?.Any() ?? false) && !(collections?.Any() ?? false))
            {

                GUILayout.Space(12);
                GUILayout.BeginHorizontal();

                GUILayout.Space(12);
                GUILayout.Label("No collections open");
                GUILayout.Space(12);

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(12);

        }

        void HandleDragDrop()
        {
            if (Event.current.type is EventType.DragUpdated or EventType.DragPerform)
            {
                var collection = DragAndDrop.objectReferences.OfType<SceneCollection>().FirstOrDefault();
                if (collection)
                {

                    DragAndDrop.AcceptDrag();
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;

                    if (Event.current.type == EventType.DragPerform)
                        SceneManager.settings.user.PinCollectionToOverlay(collection);

                }
            }
        }

        void DrawSeparator()
        {
            GUILayout.Space(12);
            var r = GUILayoutUtility.GetRect(-1, 1);
            r.width *= 0.8f;
            r.x += (r.width * 0.1f);
            EditorGUI.DrawRect(r, new Color(0.35f, 0.35f, 0.35f));
        }

        void DrawCollection(SceneCollection collection, bool isPinned)
        {

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

            GUILayout.Label(collection.title, GUILayout.ExpandHeight(true));

            DrawOpenCloseButton(collection);
            DrawPinButton(collection, isPinned);

            GUILayout.Space(6);
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

        }

        void DrawOpenCloseButton(SceneCollection collection)
        {
            GUILayout.Space(16);
            if (GUILayout.Button(collection.isOpen ? "close" : "open", GUILayout.Width(64), GUILayout.Height(20)))
                if (collection.isOpen)
                    collection.Close();
                else
                    collection.OpenAdditive();
        }

        void DrawPinButton(SceneCollection collection, bool isPinned)
        {
            GUILayout.Space(5);
            if (GUILayout.Button(isPinned ? unpinContent : pinContent, pinButton, GUILayout.Height(20)))
            {
                if (isPinned)
                    SceneManager.settings.user.UnpinCollectionFromOverlay(collection);
                else
                    SceneManager.settings.user.PinCollectionToOverlay(collection);
            }
        }

    }

}
#endif
