#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using scene = UnityEngine.SceneManagement.Scene;

namespace AdvancedSceneManager.Editor.Utility
{

    /// <summary>An utility for adding extra icons to scene fields in the hierarchy window.</summary>
    /// <remarks>Only available in editor.</remarks>
    [InitializeInEditor]
    public static class HierarchyGUIUtility
    {

        static HierarchyGUIUtility() =>
            SceneManager.OnInitialized(() =>
            {

                if (SceneManager.settings.user.displayHierarchyIndicators)
                    OnEnable();

                SceneManager.settings.user.PropertyChanged -= User_PropertyChanged;
                SceneManager.settings.user.PropertyChanged += User_PropertyChanged;

            });

        private static void User_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnDisable();
            if (SceneManager.settings.user.displayHierarchyIndicators)
                OnEnable();
        }

        static void OnEnable()
        {
            OnDisable();
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
            Repaint();
        }

        static void OnDisable()
        {
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            Repaint();
        }

        #region Callbacks

        /// <summary>Called after reserving a rect in hierarchy scene field. Return true to indicate that something was drawn, false means that the rect will be re-used for next OnGUI callback.</summary>
        public delegate void HierarchySceneGUI(scene scene);

        /// <summary>Called after reserving a rect in hierarchy game object field. Return true to indicate that something was drawn, false means that the rect will be re-used for next OnGUI callback.</summary>
        public delegate void HierarchyGameObjectGUI(GameObject gameObject);

        static readonly List<Callback> callbacks = new();

        class Callback
        {

            public object onGUI;
            public int index;

            public Callback(object onGUI, int index)
            {
                this.onGUI = onGUI;
                this.index = index;
            }

            public void OnGUI(object obj)
            {
                if (obj is scene scene && this.onGUI is HierarchySceneGUI onGUI)
                    onGUI.Invoke(scene);
                else if (obj is GameObject o && this.onGUI is HierarchyGameObjectGUI onGUI2)
                    onGUI2.Invoke(o);
            }

        }

        /// <summary>Adds a onGUI call for <see cref="AdvancedSceneManager.Models.Scene"/> fields.</summary>
        public static void AddSceneGUI(HierarchySceneGUI onGUI, int index = 0) =>
            Add(onGUI, index);

        /// <summary>Adds a onGUI call for <see cref="GameObject"/> fields.</summary>
        public static void AddGameObjectGUI(HierarchyGameObjectGUI onGUI, int index = 0) =>
            Add(onGUI, index);

        /// <summary>Remove a onGUI call for a <see cref="AdvancedSceneManager.Models.Scene"/>.</summary>
        public static void RemoveSceneGUI(HierarchySceneGUI onGUI) =>
            Remove(onGUI);

        /// <summary>Remove a onGUI call for a <see cref="GameObject"/>.</summary>
        public static void RemoveGameObjectGUI(HierarchyGameObjectGUI onGUI) =>
            Remove(onGUI);

        static void Add(object onGUI, int index)
        {
            if (!callbacks.Any(i => i.onGUI == onGUI))
                callbacks.Add(new Callback(onGUI, index));
            Repaint();
        }

        static void Remove(object onGUI)
        {
            _ = callbacks.RemoveAll(i => i.onGUI == onGUI);
            Repaint();
        }

        #endregion
        #region Hierarchy window

        static bool isResizing;
        static bool isScrollbarVisible;

        static double lastResizeEvent;
        static EditorWindow hierarchyWindow;
        static bool IsResizing()
        {

            if (!hierarchyWindow)
            {

                if (!FindHierarchyWindow(out hierarchyWindow))
                    return false;

                hierarchyWindow.rootVisualElement.RegisterCallback<GeometryChangedEvent>(async e =>
                {
                    lastResizeEvent = EditorApplication.timeSinceStartup;
                    isResizing = true;
                    height = 0;
                    await Task.Delay(250);
                    CheckEndResize();
                    if (!isResizing)
                        Repaint();
                });

            }

            CheckEndResize();
            return isResizing;

        }

        static void CheckEndResize()
        {
            isScrollbarVisible = (hierarchyWindow.rootVisualElement.contentRect.height - 20) <= height;
            isResizing = EditorApplication.timeSinceStartup - lastResizeEvent < 0.25;
        }

        static bool FindHierarchyWindow(out EditorWindow window)
        {
            var type = typeof(EditorApplication).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
            window = (EditorWindow)Resources.FindObjectsOfTypeAll(type).FirstOrDefault();
            return window;
        }

        #endregion

        /// <summary>The default style for text in hierarchy.</summary>
        public static GUIStyle defaultStyle { get; private set; }

        /// <inheritdoc cref="EditorApplication.RepaintHierarchyWindow"/>
        public static void Repaint() =>
            EditorApplication.RepaintHierarchyWindow();

        static float height;
        static void OnHierarchyGUI(int instanceID, Rect position)
        {

            if (position.yMax > height)
                height = position.yMax;

            if (IsResizing())
                return;

            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
                return;

            if (callbacks.Count == 0)
                return;

            if (defaultStyle == null)
                defaultStyle = new GUIStyle() { padding = new RectOffset(), margin = new RectOffset(4, 4, 0, 0), alignment = TextAnchor.MiddleRight, normal = new GUIStyleState() { textColor = EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.6f) : new Color(0, 0, 0, 0.6f) } };

            if (!GetObj(instanceID, out var obj, out var name))
                return;

            var r = GetRect(position, name);

            if (obj is scene)
                r.height -= 1;

            bool didArea = false;
            bool didHorizontal = false;
            try
            {

                GUILayout.BeginArea(r);
                didArea = true;
                GUILayout.BeginHorizontal();
                didHorizontal = true;

                GUILayout.FlexibleSpace();

                foreach (var callback in callbacks.OrderBy(onGUI => onGUI.index).ToArray())
                    callback.OnGUI(obj);

                GUILayout.Space(-SceneManager.settings.user.hierarchyIndicatorsOffset);

            }
            catch (Exception)
            { }
            finally
            {
                if (didHorizontal) GUILayout.EndHorizontal();
                if (didArea) GUILayout.EndArea();
            }

        }

        static Rect GetRect(Rect originalRect, string name, bool isActive = false)
        {
            var content = new GUIContent(name);
            var size = (isActive ? EditorStyles.boldLabel : EditorStyles.label).CalcSize(content);
            var offset = 20 + size.x;
            return new Rect(originalRect.x + offset - (isScrollbarVisible ? 20 : 6), originalRect.y, originalRect.width - offset, originalRect.height);
        }

        static bool GetObj(int instanceID, out object obj, out string name)
        {

            if (EditorUtility.InstanceIDToObject(instanceID) is GameObject o && o)
            {
                obj = o;
                name = o.name;
                return true;
            }
            else if (instanceID == SceneManager.runtime.dontDestroyOnLoadScene.handle)
            {
                obj = SceneManager.runtime.dontDestroyOnLoadScene;
                name = SceneManager.runtime.dontDestroyOnLoadScene.name;
                return true;
            }
            else if (SceneUtility.GetAllOpenUnityScenes().FirstOrDefault(s => s.handle == instanceID) is scene scene && scene != default)
            {
                obj = scene;
                name = scene.name;
                return true;
            }

            obj = null;
            name = null;
            return false;

        }

    }

}
#endif
