using System.Collections;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Callbacks
{

    /// <inheritdoc cref="ICollectionExtraDataCallbacks"/>
    public interface ICollectionExtraDataCallbacksCoroutine : ICollectionOpenCoroutine, ICollectionCloseCoroutine
    { }

    /// <inheritdoc cref="ISceneOpen"/>
    public interface ISceneOpenCoroutine : ISceneCallbacks
    {
        /// <inheritdoc cref="ISceneOpen"/>
        IEnumerator OnSceneOpen();
    }

    /// <inheritdoc cref="ISceneClose"/>
    public interface ISceneCloseCoroutine : ISceneCallbacks
    {
        /// <inheritdoc cref="ISceneClose"/>
        IEnumerator OnSceneClose();
    }

    /// <inheritdoc cref="ICollectionClose"/>
    public interface ICollectionCloseCoroutine : ISceneCallbacks
    {
        /// <inheritdoc cref="ICollectionClose"/>
        IEnumerator OnCollectionClose(SceneCollection collection);
    }

    /// <inheritdoc cref="ICollectionOpenAsync"/>
    public interface ICollectionOpenCoroutine : ISceneCallbacks
    {
        /// <inheritdoc cref="ICollectionOpen"/>
        IEnumerator OnCollectionOpen(SceneCollection collection);
    }

}
