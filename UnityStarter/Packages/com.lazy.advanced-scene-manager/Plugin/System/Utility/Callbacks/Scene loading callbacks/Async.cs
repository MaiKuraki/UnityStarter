using System;

namespace AdvancedSceneManager.Callbacks
{

    [Obsolete("Renamed: Use callbacks suffixed with 'Coroutine' instead.")]
    public interface ICollectionExtraDataCallbacksAsync : ICollectionOpenAsync, ICollectionCloseAsync
    { }

    [Obsolete("Renamed: Use callbacks suffixed with 'Coroutine' instead.")]
    public interface ISceneOpenAsync : ISceneOpenCoroutine
    { }

    [Obsolete("Renamed: Use callbacks suffixed with 'Coroutine' instead.")]
    public interface ISceneCloseAsync : ISceneCloseCoroutine
    { }

    [Obsolete("Renamed: Use callbacks suffixed with 'Coroutine' instead.")]
    public interface ICollectionOpenAsync : ICollectionOpen
    { }

    [Obsolete("Renamed: Use callbacks suffixed with 'Coroutine' instead.")]
    public interface ICollectionCloseAsync : ICollectionCloseCoroutine
    { }

}