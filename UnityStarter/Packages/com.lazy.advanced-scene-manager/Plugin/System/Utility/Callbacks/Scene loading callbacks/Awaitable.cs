using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Callbacks
{

#if UNITY_2023_1_OR_NEWER

    /// <inheritdoc cref="ICollectionExtraDataCallbacks"/>
    public interface ICollectionExtraDataCallbacksAwaitable : ICollectionOpenAwaitable, ICollectionCloseAwaitable
    { }

    /// <inheritdoc cref="ISceneOpen"/>
    public interface ISceneOpenAwaitable : ISceneCallbacks
    {
        /// <inheritdoc cref="ISceneOpen"/>
        UnityEngine.Awaitable OnSceneOpen();
    }

    /// <inheritdoc cref="ISceneClose"/>
    public interface ISceneCloseAwaitable : ISceneCallbacks
    {
        /// <inheritdoc cref="ISceneClose"/>
        UnityEngine.Awaitable OnSceneClose();
    }

    /// <inheritdoc cref="ICollectionOpen"/>
    public interface ICollectionOpenAwaitable : ISceneCallbacks
    {
        /// <inheritdoc cref="ICollectionOpen"/>
        UnityEngine.Awaitable OnCollectionOpen(SceneCollection collection);
    }

    /// <inheritdoc cref="ICollectionClose"/>
    public interface ICollectionCloseAwaitable : ISceneCallbacks
    {
        /// <inheritdoc cref="ICollectionClose"/>
        UnityEngine.Awaitable OnCollectionClose(SceneCollection collection);
    }

#endif
}
