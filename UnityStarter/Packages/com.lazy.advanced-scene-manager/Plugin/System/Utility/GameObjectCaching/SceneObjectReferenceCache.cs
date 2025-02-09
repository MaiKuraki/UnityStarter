using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Models;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace AdvancedSceneManager.Utility
{

    [AddComponentMenu("")]
    public class SceneObjectReferenceCache : MonoBehaviour
    {

        public SerializableDictionary<string, Object> references = new SerializableDictionary<string, Object>();

        public static IEnumerable<(string key, Object obj)> Enumerate(Scene scene)
        {

            var cache = GetCache(scene);

            if (!cache || cache.references is null)
                return Enumerable.Empty<(string, Object)>();

            return cache.references.Select(r => (r.Key, r.Value));

        }

        public static bool Get(Scene scene, string key, out Object obj)
        {

            obj = null;

            if (string.IsNullOrEmpty(key))
                return false;

            var cache = GetCache(scene);
            if (!cache || cache.references is null)
                return false;

            return cache.references.TryGetValue(key, out obj);

        }

        public static void Set(Scene scene, string key, Object obj)
        {

            if (string.IsNullOrEmpty(key))
                return;

            if (!obj)
                return;

            var isDirty = scene.internalScene.Value.isDirty;
            Debug.Log(isDirty);

            var cache = GetCache(scene);
            if (!cache)
                cache = CreateCache(scene);

            cache.references.Set(key, obj);

#if UNITY_EDITOR
            //Don't save if user has unsaved changes
            if (!isDirty)
                EditorSceneManager.SaveScene(scene);
#endif

        }

        public static void Remove(Scene scene, string key)
        {

            if (string.IsNullOrEmpty(key))
                return;

            var isDirty = scene.internalScene.Value.isDirty;
            //Debug.Log(isDirty);

            var cache = GetCache(scene);
            if (!cache)
                return;

            if (!cache.references.Remove(key))
                return;

#if UNITY_EDITOR
            //Don't save if user has unsaved changes
            if (!isDirty)
                EditorSceneManager.SaveScene(scene);
#endif

        }

        static SceneObjectReferenceCache GetCache(Scene scene)
        {

            if (!scene || !scene.isOpenInHierarchy)
                return null;

            return scene.FindObject<SceneObjectReferenceCache>();

        }

        static SceneObjectReferenceCache CreateCache(Scene scene)
        {

            if (!scene || !scene.isOpenInHierarchy)
                throw new InvalidOperationException("Scene must be open to cache game objects.");

            var obj = new GameObject("GameObjectCache");
            obj.Move(scene);

            obj.hideFlags = HideFlags.HideInHierarchy;

            return obj.AddComponent<SceneObjectReferenceCache>();

        }

    }

}
