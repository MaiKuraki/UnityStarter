using System;
using System.Collections;
using System.Collections.Generic;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;
using static AdvancedSceneManager.Callbacks.Events.EventCallbackUtility;

namespace AdvancedSceneManager.Callbacks.Events
{

    /// <summary>The base class for all scene operation event callbacks.</summary>
    public abstract class SceneOperationEventBase
    {

        /// <summary>The operation that invoked this event callback.</summary>
        public SceneOperation operation { get; set; }

        /// <summary>Specifies when this event callback was invoked, before or after the action it represents. If applicable.</summary>
        public When when { get; set; }

        /// <summary>Specifies a coroutine that <see cref="operation"/> should wait for. It will not proceed until coroutine is done.</summary>
        public List<Func<IEnumerator>> waitFor { get; private set; } = new();

        /// <summary>Specifies a coroutine that the operation should wait for.</summary>
        public void WaitFor(IEnumerator coroutine) => waitFor.Add(() => coroutine);

        /// <inheritdoc cref="WaitFor(IEnumerator)"/>
        public void WaitFor(Func<IEnumerator> coroutine) => waitFor.Add(coroutine);

        /// <inheritdoc cref="WaitFor(IEnumerator)"/>
        public void WaitFor(GlobalCoroutine coroutine) => waitFor.Add(() => coroutine);

        /// <inheritdoc cref="WaitFor(IEnumerator)"/>
        public void WaitFor(Func<GlobalCoroutine> coroutine) => waitFor.Add(coroutine);

#if UNITY_6000_0_OR_NEWER

        /// <inheritdoc cref="WaitFor(IEnumerator)"/>
        public void WaitFor(Awaitable awaitable) => waitFor.Add(() => awaitable);

        /// <inheritdoc cref="WaitFor(IEnumerator)"/>
        public void WaitFor(Func<Awaitable> awaitable) => waitFor.Add(awaitable);

#endif

    }

    #region Scene events

    /// <summary>The base class for scene event callbacks.</summary>
    /// <remarks>See <see cref="SceneOpenEvent"/>, <see cref="SceneCloseEvent"/>.</remarks>
    public abstract class SceneEvent : SceneOperationEventBase
    {
        /// <summary>The scene that this event callback was invoked for.</summary>
        public Scene scene { get; set; }
    }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when a scene is closed.</summary>
    public class SceneCloseEvent : SceneEvent
    { }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when a scene is opened.</summary>
    public class SceneOpenEvent : SceneEvent
    { }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when a scene is preloaded.</summary>
    public class ScenePreloadEvent : SceneEvent
    { }

    #endregion
    #region Collection callbacks

    /// <summary>The base class for collection event callbacks.</summary>
    /// <remarks>See <see cref="CollectionOpenEvent"/>, <see cref="CollectionCloseEvent"/>.</remarks>
    public abstract class CollectionEvent : SceneOperationEventBase
    {
        /// <summary>The collection that this event callback was invoked for.</summary>
        public SceneCollection collection { get; set; }
    }

    [CalledFor(When.NotApplicable)]
    /// <summary>Occurs when a collection is opened.</summary>
    /// <remarks><see cref="When"/> enum is not applicable.</remarks>
    public class CollectionOpenEvent : CollectionEvent
    { }

    [CalledFor(When.NotApplicable)]
    /// <summary>Occurs when a collection is closed.</summary>
    /// <remarks><see cref="When"/> enum is not applicable.</remarks>
    public class CollectionCloseEvent : CollectionEvent
    { }

    #endregion
    #region Phase callbacks

    [CalledFor(When.NotApplicable)]
    /// <summary>Occurs before operation has begun working, but after it has started.</summary>
    /// <remarks>Properties has not been frozen at this point. <see cref="When"/> enum is not applicable.</remarks>
    public class StartPhaseEvent : SceneOperationEventBase
    { }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when a loading screen is opened.</summary>
    /// <remarks>Called regardless if operation actually opens one or not.</remarks>
    public class LoadingScreenOpenPhaseEvent : SceneOperationEventBase
    {
        public Scene loadingScene { get; set; }
        public LoadingScreen openedLoadingScreen { get; set; }
    }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when operation starts and finishes closing scenes.</summary>
    public class SceneClosePhaseEvent : SceneOperationEventBase
    { }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when operation starts and finishes opening scenes.</summary>
    public class SceneOpenPhaseEvent : SceneOperationEventBase
    { }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when operation starts and finishes preloading scenes.</summary>
    public class ScenePreloadPhaseEvent : SceneOperationEventBase
    { }

    [CalledFor(When.Before, When.After)]
    /// <summary>Occurs when a loading screen is closed.</summary>
    /// <remarks>Called regardless if operation actually opens one or not.</remarks>
    public class LoadingScreenClosePhaseEvent : SceneOperationEventBase
    {
        public Scene loadingScene { get; set; }
        public LoadingScreen openedLoadingScreen { get; set; }
    }

    [CalledFor(When.NotApplicable)]
    /// <summary>Occurs before operation has stopped working, but after its practially done.</summary>
    /// <remarks><see cref="When"/> enum is not applicable.</remarks>
    public class EndPhaseEvent : SceneOperationEventBase
    { }

    #endregion

}
