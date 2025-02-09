#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

using AdvancedSceneManager.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdvancedSceneManager.Models.Utility
{

    /// <summary>Specifies a <typeparamref name="T"/> that changes depending on active <see cref="Profile"/>.</summary>
    public class ProfileDependent<T> : ScriptableObject where T : ASMModel
    {

        /// <summary>A dictionary of type <see cref="Profile"/>, <typeparamref name="T"/>.</summary>
        [Serializable]
        public class Dict : SerializableDictionary<Profile, T>
        {

            public Dict()
            { }

            public Dict(IEnumerable<KeyValuePair<Profile, T>> dictionary)
            {
                foreach (var item in dictionary)
                    Add(item.Key, item.Value);
            }

        }

        /// <summary>The list of proxies for this <typeparamref name="T"/>.</summary>
        public Dict list = new Dict();

        /// <summary>Gets if the current state of this <typeparamref name="T"/> is valid.</summary>
        public bool isValid =>
            GetModel(out _);

        /// <summary>Gets the selected scene.</summary>
        /// <remarks>Returns null if scene something went wrong.</remarks>
        public bool GetModel(out T scene)
        {
            _ = list.TryGetValue(Profile.current, out scene);
            return scene;
        }

        /// <summary>Performs an action on the scene.</summary>
        /// <remarks>Does nothing if <see cref="isValid"/> is <see langword="false"/>.</remarks>
        public T2 DoAction<T2>(Func<T, T2> action) =>
            GetModel(out var scene)
            ? action.Invoke(scene)
            : default;

        /// <summary>Performs an action on the scene.</summary>
        /// <remarks>Does nothing if <see cref="isValid"/> is <see langword="false"/>.</remarks>
        public void DoAction(Action<T> action)
        {
            if (GetModel(out var scene))
                action.Invoke(scene);
        }

    }

#if UNITY_EDITOR

    class ProfileDependentEditor<T> : UnityEditor.Editor where T : ASMModel
    {

        void OnEnable() =>
            list = ((ProfileDependent<T>)target).list.ToList();

        static ReorderableList reorderableList;
        static IList<KeyValuePair<Profile, T>> list;

        public override void OnInspectorGUI() =>
            DoList(list => list.DoLayoutList());

        public void IMGUI(Rect position) =>
              DoList(list => list.DoList(position));

        void DoList(Action<ReorderableList> draw)
        {

            if (reorderableList == null)
                reorderableList = new ReorderableList(null, typeof(KeyValuePair<Profile, T>), true, true, true, true);

            reorderableList.onCanRemoveCallback = (_) => true;
            reorderableList.list = (IList)list;
            reorderableList.onAddCallback = (_) =>
            {

                var menu = new GenericMenu();
                foreach (var profile in AvailableProfiles())
                    menu.AddItem(new GUIContent(profile.name), false, Add, userData: profile);

                menu.ShowAsContext();

                void Add(object obj)
                {
                    if (obj is Profile profile && profile)
                    {
                        list.Add(new KeyValuePair<Profile, T>(profile, default));
                        Set();
                    }
                }

            };

            reorderableList.onCanAddCallback = (_) => AvailableProfiles().Count() > 0;
            reorderableList.headerHeight = 1;
            reorderableList.elementHeight = 26;

            reorderableList.drawElementCallback = (Rect pos, int index, bool isActive, bool isFocused) =>
            {

                GUI.Label(new Rect(pos.xMin, pos.yMin + 4, pos.width * 0.25f, pos.height - 8), list[index].Key.name);

                var scene = (T)EditorGUI.ObjectField(new Rect(pos.xMin + (pos.width * 0.25f), pos.yMin + 4, pos.width * 0.75f, pos.height - 8), list[index].Value, typeof(T), allowSceneObjects: false);

                if (scene != list[index].Value)
                    list[index] = new KeyValuePair<Profile, T>(list[index].Key, scene);

            };

            //Remove items pointing to removed profile
            foreach (var item in list.ToArray())
                if (!item.Key)
                    _ = list.Remove(item);

            EditorGUI.BeginChangeCheck();
            draw.Invoke(reorderableList);

            if (EditorGUI.EndChangeCheck())
                Set();

            void Set()
            {
                ((ProfileDependent<T>)target).list = new ProfileDependent<T>.Dict(list);
                EditorUtility.SetDirty(target);
            }

            IEnumerable<Profile> AvailableProfiles() =>
                SceneManager.assets.profiles.Where(p => !list.Any(s => s.Key == p));

        }

    }

    abstract class PropertyDrawer<T, ScriptableObjectT, EditorT> : PropertyDrawer
        where T : ASMModel
        where ScriptableObjectT : ScriptableObject, new()
        where EditorT : ProfileDependentEditor<T>, new()
    {

        ProfileDependentEditor<T> editor;
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {

            _ = EditorGUI.PropertyField(new Rect(position.x, position.y, position.width - 20, EditorGUIUtility.singleLineHeight), property, label);

            if (!property.objectReferenceValue)
            {
                if (property.objectReferenceInstanceIDValue != 0)
                {
                    property.objectReferenceValue = null;
                    _ = property.serializedObject.ApplyModifiedProperties();
                }
                return;
            }

            if (editor == null)
                editor = (ProfileDependentEditor<T>)UnityEditor.Editor.CreateEditor(property.objectReferenceValue, typeof(EditorT));
            editor.IMGUI(new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 4, position.width, position.height - EditorGUIUtility.singleLineHeight));

        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.objectReferenceValue && editor != null)
                return EditorGUIUtility.singleLineHeight * (((ProfileDependent<T>)property.objectReferenceValue).list.Count + 1) + 56;
            else
                return EditorGUIUtility.singleLineHeight;
        }

    }

    #region Scene

    [CustomPropertyDrawer(typeof(ProfileDependentScene))] class SceneDrawer : PropertyDrawer<Scene, ProfileDependentScene, SceneEditor> { }
    [CustomEditor(typeof(ProfileDependentScene))] class SceneEditor : ProfileDependentEditor<Scene> { }

    #endregion
    #region Collection

    [CustomPropertyDrawer(typeof(ProfileDependentCollection))] class CollectionDrawer : PropertyDrawer<SceneCollection, ProfileDependentCollection, CollectionEditor> { }
    [CustomEditor(typeof(ProfileDependentCollection))] class CollectionEditor : ProfileDependentEditor<SceneCollection> { }

    #endregion

#endif

}
