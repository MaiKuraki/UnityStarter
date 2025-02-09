using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Callbacks.Events;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.Core
{

    partial class SceneOperation
    {

        static readonly Dictionary<Type, List<(Delegate callback, When? when)>> globalRegisteredCallbacks = new();
        readonly Dictionary<Type, List<(Delegate callback, When? when)>> registeredCallbacks = new();

        #region Register

        /// <summary>Registers an event callback for when an event occurs in a operation.</summary>
        /// <typeparam name="TEventType">The event callback type.</typeparam>
        /// <param name="callback">The callback to be invoked.</param>
        /// <param name="when">Specifies that the callback should only be called either only for that time. If <see langword="null"/> then callback will be called both times. For events using <see cref="When.NotApplicable"/>, this is ignored.</param>
        public SceneOperation RegisterCallback<TEventType>(EventCallback<TEventType> callback, When? when = null) where TEventType : SceneOperationEventBase, new()
        {

            if (registeredCallbacks.ContainsKey(typeof(TEventType)))
                registeredCallbacks[typeof(TEventType)].Add((callback, when));
            else
                registeredCallbacks.Add(typeof(TEventType), new() { (callback, when) });

            return this;

        }

        /// <inheritdoc cref="RegisterCallback{TEventType}(EventCallback{TEventType}, When?)"/>
        public static void RegisterGlobalCallback<TEventType>(EventCallback<TEventType> callback, When? when = null) where TEventType : SceneOperationEventBase, new()
        {

            if (globalRegisteredCallbacks.ContainsKey(typeof(TEventType)))
                globalRegisteredCallbacks[typeof(TEventType)].Add(((callback, when)));
            else
                globalRegisteredCallbacks.Add(typeof(TEventType), new() { (callback, when) });

        }

        #endregion
        #region Unregister

        /// <summary>Unregisters a registered event callback.</summary>
        /// <typeparam name="TEventType">The event callback type.</typeparam>
        /// <param name="callback">The callback that was to be invoked.</param>
        public SceneOperation UnregisterCallback<TEventType>(EventCallback<TEventType> callback, When? when = null) where TEventType : SceneOperationEventBase, new()
        {
            if (registeredCallbacks.ContainsKey(typeof(TEventType)))
                registeredCallbacks[typeof(TEventType)].RemoveAll(c => c.callback.Equals(callback) && c.when == when);
            return this;
        }

        /// <inheritdoc cref="UnregisterCallback{TEventType}(EventCallback{TEventType}, When?)"/>
        public static void UnregisterGlobalCallback<TEventType>(EventCallback<TEventType> callback, When? when = null) where TEventType : SceneOperationEventBase, new()
        {
            if (globalRegisteredCallbacks.ContainsKey(typeof(TEventType)))
                globalRegisteredCallbacks[typeof(TEventType)].RemoveAll(c => c.callback.Equals(callback) && c.when == when);
        }

        #endregion
        #region Invoke

        internal static IEnumerator InvokeGlobalCallback<TEventType>(TEventType e, When when = When.NotApplicable, SceneOperation operation = null) where TEventType : SceneOperationEventBase, new()
        {

            e.operation = operation ?? done;
            e.when = when;

            var waitFor = new List<Func<IEnumerator>>();

            if (globalRegisteredCallbacks.TryGetValue(typeof(TEventType), out var globalCallbacks))
                foreach (var (callback, _when) in globalCallbacks)
                {

                    if (when != When.NotApplicable)
                        if (_when.HasValue && _when != when)
                            continue;

                    callback.DynamicInvoke(e);
                    waitFor.AddRange(e.waitFor);

                }

            if (SceneManager.settings.project.allowLoadingScenesInParallel)
                yield return CoroutineUtility.WaitAll(waitFor.Select(c => c.Invoke().StartCoroutine()));
            else
                yield return CoroutineUtility.Chain(waitFor.Select(c => c.Invoke().StartCoroutine()));

        }

        internal IEnumerator InvokeCallback<TEventType>(TEventType e, When when = When.NotApplicable) where TEventType : SceneOperationEventBase, new()
        {

            if (!flags.HasFlag(SceneOperationFlags.EventCallbacks))
                yield break;

            e.operation = this;
            e.when = when;

            var waitFor = new List<Func<IEnumerator>>();

            if (globalRegisteredCallbacks.TryGetValue(typeof(TEventType), out var globalCallbacks))
                foreach (var (callback, _when) in globalCallbacks)
                {

                    if (when != When.NotApplicable)
                        if (_when.HasValue && _when != when)
                            continue;

                    callback.DynamicInvoke(e);
                    waitFor.AddRange(e.waitFor);

                }

            if (registeredCallbacks.TryGetValue(typeof(TEventType), out var callbacks))
                foreach (var (callback, _when) in callbacks)
                {

                    if (when != When.NotApplicable)
                        if (_when.HasValue && _when != when)
                            continue;

                    callback.DynamicInvoke(e);
                    waitFor.AddRange(e.waitFor);

                }

            //Debug.Log($"Invoke {typeof(TEventType).Name} ({when}): {waitFor.Count} callbacks.");
            if (SceneManager.settings.project.allowLoadingScenesInParallel)
                yield return CoroutineUtility.WaitAll(waitFor.Select(c => c.Invoke().StartCoroutine()));
            else
                yield return CoroutineUtility.Chain(waitFor.Select(c => c.Invoke().StartCoroutine()));

        }

        #endregion

    }

}
