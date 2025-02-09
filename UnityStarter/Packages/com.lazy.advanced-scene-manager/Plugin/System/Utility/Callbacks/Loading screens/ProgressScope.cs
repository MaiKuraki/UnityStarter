using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.Loading
{

    /// <summary>Represents a listener for progress that can calculate the total progress of a scene operation.</summary>
    public class ProgressScope : ILoadProgressListener, IDisposable
    {

        /// <summary>Gets or sets whatever the listener should be unregistered in <see cref="Dispose"/>. Default <see langword="true"/></summary>
        public bool stopListenerWhenDisposed { get; set; } = true;

        public bool isRegistered { get; private set; }

        readonly List<Scene> m_scenesExpectedToLoad = new List<Scene>();
        readonly List<Scene> m_scenesExpectedToUnload = new List<Scene>();

        /// <summary>Gets the scenes that are expected to be loaded.</summary>
        public IEnumerable<Scene> scenesExpectedToLoad => m_scenesExpectedToLoad;

        /// <summary>Gets the scenes that are expected to be unloaded.</summary>
        public IEnumerable<Scene> scenesExpectedToUnload => m_scenesExpectedToUnload;

        /// <summary>Gets the calculated total progress of this progress scope.</summary>
        public float totalProgress { get; private set; }

        /// <summary>Gets the amount of scenes that will be either unloaded or loaded.</summary>
        public int operationCount => subProgress.Count;

        float GetProgress()
        {

            var total = subProgress.Count > 0
                ? subProgress.Sum(p => p.Value) / subProgress.Count
                : 0f;

            return !float.IsNaN(total) ? total : 0f;

        }

        void Recalculate()
        {

            var previous = totalProgress;
            totalProgress = GetProgress();

            if (!UnityEngine.Mathf.Approximately(previous, totalProgress))
                InvokeCallbacks();

        }

        #region Expect

        /// <summary>Expect scenes in <paramref name="collection"/>.</summary>
        /// <param name="kind">The kind of operation to expect.</param>
        /// <param name="collection">The collection to expect.</param>
        /// <param name="openAll">Specifies whatever all scenes in collection should be opened. Force opens scenes flagged to not open.</param>
        /// <param name="isAdditive">Can be specified for <see cref="SceneOperationKind.Load"/>, <see cref="SceneCollection.isOpenAdditive"/> will be used for <see cref="SceneOperationKind.Unload"/>.</param>
        public ProgressScope Expect(SceneOperationKind kind, SceneCollection collection, bool openAll = false, bool isAdditive = false)
        {

            if (!collection)
                return this;

            if (kind == SceneOperationKind.Load)
            {

                if (isAdditive)
                {
                    Expect(SceneOperationKind.Load, SceneManager.runtime.EvalScenesToOpen(collection: collection, openAll));
                }
                else
                {
                    Expect(SceneOperationKind.Unload, SceneManager.runtime.EvalScenesToClose(nextCollection: collection));
                    Expect(SceneOperationKind.Load, SceneManager.runtime.EvalScenesToOpen(collection: collection, openAll));
                }

            }
            else if (kind == SceneOperationKind.Unload)
            {

                if (collection.isOpenAdditive)
                {
                    Expect(SceneOperationKind.Unload, SceneManager.runtime.EvalScenesToClose(collection, additiveCloseCollection: collection));
                }
                else
                {
                    Expect(SceneOperationKind.Unload, SceneManager.runtime.EvalScenesToClose(collection));
                }

            }

            return this;

        }

        /// <summary>Expect <paramref name="scene"/>.</summary>
        /// <param name="kind">The kind of operation to expect.</param>
        /// <param name="scene">The scenes to expect.</param>
        public ProgressScope Expect(SceneOperationKind kind, params Scene[] scene) =>
            Expect(kind, scenes: scene);

        /// <summary>Expect <paramref name="scenes"/>.</summary>
        /// <param name="kind">The kind of operation to expect.</param>
        /// <param name="scenes">The scenes to expect.</param>
        public ProgressScope Expect(SceneOperationKind kind, IEnumerable<Scene> scenes)
        {

            scenes = scenes?.NonNull()?.Where(s => kind == SceneOperationKind.Load ? !s.isOpen : s.isOpen).Distinct();

            if (scenes?.Count() == 0)
                return this;

            if (kind == SceneOperationKind.Load)
                m_scenesExpectedToLoad.AddRange(scenes);
            else if (kind == SceneOperationKind.Unload)
                m_scenesExpectedToUnload.AddRange(scenes);

            foreach (var scene in scenes)
                SetSubProgress(kind, scene, 0f, false);
            Recalculate();

            return this;

        }

        #endregion
        #region SubProgress

        Dictionary<(Scene scene, SceneOperationKind kind), float> subProgress = new Dictionary<(Scene scene, SceneOperationKind kind), float>();

        internal void SetSubProgress(SceneOperationKind kind, Scene scene, float progress, bool recalculate = true)
        {

            if (kind == SceneOperationKind.Load && !scenesExpectedToLoad.Contains(scene))
                return;

            if (kind == SceneOperationKind.Unload && !scenesExpectedToUnload.Contains(scene))
                return;

            subProgress.Set((scene, kind), progress);
            if (recalculate)
                Recalculate();

        }

        /// <summary>Gets the progress of a specific scene.</summary>
        /// <param name="kind">The kind of operation that was expected.</param>
        /// <param name="scene">The scene that was expected.</param>
        public float GetSubProgress(SceneOperationKind kind, Scene scene)
        {
            return subProgress.TryGetValue((scene, kind), out var progress) ? progress : 0f;
        }

        #endregion
        #region Listener

        /// <summary>Starts listening to progress reports.</summary>
        public ProgressScope StartListener()
        {
            LoadingScreenUtility.RegisterLoadProgressListener(this);
            isRegistered = true;
            return this;
        }

        /// <summary>Stops listening to progress reports.</summary>
        public ProgressScope StopListener()
        {
            LoadingScreenUtility.UnregisterLoadProgressListener(this);
            isRegistered = true;
            return this;
        }

        /// <inheritdoc cref="StopListener"/>
        public void Dispose()
        {
            if (stopListenerWhenDisposed)
                StopListener();
        }

        void ILoadProgressListener.OnProgressChanged(ILoadProgressData progress)
        {
            if (progress is SceneLoadProgressData sceneLoad)
                SetSubProgress(sceneLoad.operationKind, sceneLoad.scene, sceneLoad.value);
        }

        #endregion
        #region Callbacks

        readonly List<Action<float>> callbacks = new List<Action<float>>();

        /// <summary>Adds a callback when progress changed.</summary>
        public void OnProgressChanged(Action<float> callback) =>
            callbacks.Add(callback);

        /// <summary>Removes a callback when progress changed.</summary>
        public void RemoveOnProgressChangedCallback(Action<float> callback) =>
            callbacks.Remove(callback);

        void InvokeCallbacks()
        {
            foreach (var callback in callbacks)
                callback?.Invoke(totalProgress);
        }

        #endregion

    }

}
