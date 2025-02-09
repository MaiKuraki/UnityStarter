using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace AdvancedSceneManager.Callbacks
{

    /// <summary>An utility class that invokes callbacks (defined in interfaces based on <see cref="ISceneCallbacks"/>).</summary>
    public static class CallbackUtility
    {

        #region Find objects

        [SuppressMessage("TypeSafety", "UNT0014:Invalid type for call to GetComponent", Justification = nameof(ISceneCallbacks) + " is an interface meant to be implemented on " + nameof(MonoBehaviour) + ".")]
        static IEnumerable<T> Get<T>(Object obj) where T : ISceneCallbacks
        {
            if (obj is ScriptableObject so && so is T t)
            {
                yield return t;
            }
            else if (obj is Scene scene)
            {
                foreach (var component in scene.FindObjects<T>())
                    yield return component;
            }
            else if (obj is GameObject go)
            {
                foreach (var item in go.GetComponentsInChildren<T>())
                    yield return item;
            }
        }

        static readonly Dictionary<Object, ISceneCallbacks[]> cache = new();
        static IEnumerable<T> GetCached<T>(Object obj) where T : ISceneCallbacks
        {

            // Clear out unloaded objects
            foreach (var key in cache.Keys.Where(k => !k).ToArray())
                cache.Remove(key);

            if (!obj || (obj is Scene scene && !scene.isOpenInHierarchy))
                return Enumerable.Empty<T>();

            if (cache.TryGetValue(obj, out var callbacks))
                return callbacks.OfType<T>().ToArray();

            var items = Get<ISceneCallbacks>(obj).ToArray();

            //Debug.Log($"Caching {items.Length} items of type {typeof(T).Name} for object {obj.name}");

            cache[obj] = items;

            return items.OfType<T>();

        }

#if UNITY_EDITOR
        [InitializeInEditorMethod]
        static void InitializeCache()
        {
            cache.Clear();
            EditorSceneManager.sceneDirtied += SceneCallback;
            EditorSceneManager.sceneSaved += SceneCallback;
        }
#endif

        [InitializeInEditorMethod]
        [RuntimeInitializeOnLoadMethod]
        static void IntitializeCacheRuntime()
        {
            cache.Clear();
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += SceneCallback;
        }

        private static void SceneCallback(UnityEngine.SceneManagement.Scene scene)
        {
            if (scene.ASMScene(out var s))
                _ = cache.Remove(s);
        }

        #endregion
        #region Known callbacks

        delegate IEnumerator Callback(object obj, object param);

        static readonly Dictionary<Type, Callback> knownCallbacks = new()
        {

            { typeof(ISceneOpen),            (o, p) => Call(() => ((ISceneOpen)o).OnSceneOpen()) },
            { typeof(ISceneClose),           (o, p) => Call(() => ((ISceneClose)o).OnSceneClose()) },
            { typeof(ICollectionOpen),       (o, p) => Call(() => ((ICollectionOpen)o).OnCollectionOpen(p as SceneCollection)) },
            { typeof(ICollectionClose),      (o, p) => Call(() => ((ICollectionClose)o).OnCollectionClose(p as SceneCollection)) },

            { typeof(ISceneOpenCoroutine),       (o, p) =>  ((ISceneOpenCoroutine)o).OnSceneOpen() },
            { typeof(ISceneCloseCoroutine),      (o, p) =>  ((ISceneCloseCoroutine)o).OnSceneClose() },
            { typeof(ICollectionOpenCoroutine),  (o, p) =>  ((ICollectionOpenCoroutine)o).OnCollectionOpen(p as SceneCollection) },
            { typeof(ICollectionCloseCoroutine), (o, p) =>  ((ICollectionCloseCoroutine)o).OnCollectionClose(p as SceneCollection) },

#if UNITY_2023_1_OR_NEWER
            { typeof(ISceneOpenAwaitable),       (o, p) =>  ((ISceneOpenAwaitable)o).OnSceneOpen() },
            { typeof(ISceneCloseAwaitable),      (o, p) =>  ((ISceneCloseAwaitable)o).OnSceneClose() },
            { typeof(ICollectionOpenAwaitable),  (o, p) =>  ((ICollectionOpenAwaitable)o).OnCollectionOpen(p as SceneCollection) },
            { typeof(ICollectionCloseAwaitable), (o, p) =>  ((ICollectionCloseAwaitable)o).OnCollectionClose(p as SceneCollection) },
#endif

        };

        static IEnumerator Call(Action action)
        {
            action.LogInvoke();
            yield break;
        }

        static IEnumerator KnownCallback(Type t, object obj, object param = null) =>
            typeof(ISceneCallbacks).IsAssignableFrom(t)
                ? knownCallbacks.GetValue(t)?.Invoke(obj, param)
                : null;

        public static IEnumerator DoSceneOpenCallbacks(Scene scene) =>
            CoroutineUtility.WaitAll(
                Invoke<ISceneOpen>().On(scene),
                Invoke<ISceneOpenCoroutine>().On(scene)

#if UNITY_2023_1_OR_NEWER
                , Invoke<ISceneOpenAwaitable>().On(scene)
#endif

                );

        public static IEnumerator DoSceneCloseCallbacks(Scene scene) =>
            CoroutineUtility.WaitAll(
                Invoke<ISceneClose>().On(scene),
                Invoke<ISceneCloseCoroutine>().On(scene)

#if UNITY_2023_1_OR_NEWER
                , Invoke<ISceneCloseAwaitable>().On(scene)
#endif

                );

        public static IEnumerator DoCollectionOpenCallbacks(SceneCollection collection)
        {

            if (collection && collection.userData)
                yield return CoroutineUtility.WaitAll(
                    Invoke<ICollectionOpen>().WithParam(collection).On(collection.userData),
                    Invoke<ICollectionOpenCoroutine>().WithParam(collection).On(collection.userData)

#if UNITY_2023_1_OR_NEWER
                    , Invoke<ICollectionOpenAwaitable>().WithParam(collection).On(collection.userData)
#endif

                    );

            if (collection)
                yield return CoroutineUtility.WaitAll(
                    Invoke<ICollectionOpen>().WithParam(collection).On(collection),
                    Invoke<ICollectionOpenCoroutine>().WithParam(collection).On(collection)

#if UNITY_2023_1_OR_NEWER
                    , Invoke<ICollectionOpenAwaitable>().WithParam(collection).On(collection)
#endif

                    );

        }

        public static IEnumerator DoCollectionCloseCallbacks(SceneCollection collection)
        {

            if (collection && collection.userData)
                yield return CoroutineUtility.WaitAll(
                    Invoke<ICollectionClose>().WithParam(collection).On(collection.userData),
                    Invoke<ICollectionCloseCoroutine>().WithParam(collection).On(collection.userData)

#if UNITY_2023_1_OR_NEWER
                    , Invoke<ICollectionCloseAwaitable>().WithParam(collection).On(collection.userData)
#endif

                    );

            if (collection)
                yield return CoroutineUtility.WaitAll(
                    Invoke<ICollectionClose>().WithParam(collection).On(collection),
                    Invoke<ICollectionCloseCoroutine>().WithParam(collection).On(collection)

#if UNITY_2023_1_OR_NEWER
                    , Invoke<ICollectionCloseAwaitable>().WithParam(collection).On(collection)
#endif

                    );

        }

        #endregion
        #region Invoke

        public static FluentInvokeAPI<T> Invoke<T>() where T : ISceneCallbacks =>
            new();

        static IEnumerator Invoke<T>(FluentInvokeAPI<T>.Callback invoke, object param, params Object[] obj) where T : ISceneCallbacks
        {

            var callbackObjects = obj.
                SelectMany(o => GetCached<T>(o)).
                ToArray();

            if (!callbackObjects.Any())
                yield break;

            foreach (var callback in callbackObjects)
                yield return Add(callback);

            IEnumerator Add(T callback)
            {
                var isEnabled = (callback is MonoBehaviour mb && mb && mb.isActiveAndEnabled) || callback is ScriptableObject;
                yield return invoke.Invoke(callback, isEnabled);
            }

        }

        /// <summary>An helper class to facilitate a fluent api.</summary>
        /// <remarks>Usage: <see cref="Invoke{T}"/></remarks>
        public sealed class FluentInvokeAPI<T> where T : ISceneCallbacks
        {

            public delegate IEnumerator Callback(T obj, bool isEnabled);
            Callback callback;
            object param;

            /// <summary>Gets whatever <typeparamref name="T"/> has a default callback. All callbacks inheriting from <see cref="ISceneCallbacks"/> should have one.</summary>
            public bool hasDefaultCallback =>
                knownCallbacks.ContainsKey(typeof(T));

            /// <summary>Specify a callback, this should point to the interface method which provides a <see cref="IEnumerator"/>.</summary>
            /// <remarks>This is not needed for callback interfaces inheriting from <see cref="ISceneCallbacks"/>.</remarks>
            public FluentInvokeAPI<T> WithCallback(Callback callback) =>
                Set(() => this.callback = callback);

            /// <summary>Specify a parameter to use when invoking the callback.</summary>
            public FluentInvokeAPI<T> WithParam(object param) =>
                Set(() => this.param = param);

            /// <summary>Specify the collection scenes to run this callback on and start execution.</summary>
            public IEnumerator On(SceneCollection collection, params Scene[] additionalScenes) =>
                On(collection.scenes.Concat(additionalScenes).ToArray());

            /// <summary>Specify the collection scenes to run this callback on and start execution..</summary>
            public IEnumerator OnAllOpenScenes() =>
                On(SceneManager.runtime.openScenes.ToArray());

            /// <summary>Specify the scenes to run this callback on and start execution.</summary>
            public IEnumerator On(params Scene[] scenes)
            {

                scenes = scenes.NonNull().ToArray();
                if (scenes.Length == 0)
                    yield break;

                if (hasDefaultCallback && callback is null)
                    callback = (c, isEnabled) => KnownCallback(typeof(T), c, param);

                if (callback is null)
                {
                    Debug.LogError($"No callback specified for a callback of type '{typeof(T).Name}'");
                    yield break;
                }

                yield return Invoke(callback, param, scenes);

            }

            /// <summary>Specify the scenes to run this callback on and start execution.</summary>
            public IEnumerator On(params ScriptableObject[] scriptableObjects)
            {

                scriptableObjects = scriptableObjects.Where(s => s).ToArray();
                if (scriptableObjects.Length == 0)
                    yield break;

                if (hasDefaultCallback && callback is null)
                    callback = (c, isEnabled) => KnownCallback(typeof(T), c, param);

                if (callback is null)
                {
                    Debug.LogError($"No callback specified for a callback of type '{typeof(T).Name}'");
                    yield break;
                }

                yield return Invoke(callback, param, scriptableObjects.Where(s => s).ToArray());

            }

            FluentInvokeAPI<T> Set(Action action)
            {
                action?.Invoke();
                return this;
            }

        }

        #endregion

    }

}
