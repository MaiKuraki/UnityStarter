using UnityEngine;

namespace AdvancedSceneManager.Callbacks
{

    /// <summary>Base interface for <see cref="MonoBehaviour"/> callbacks. Just implement any of the following to have ASM call them during scene operations.
    /// <list type="bullet">
    ///   <item>
    ///     <description><see cref="ISceneOpen"/>, <see cref="ISceneClose"/>, <see cref="ICollectionOpen"/>, <see cref="ICollectionClose"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ISceneOpenCoroutine"/>, <see cref="ISceneCloseCoroutine"/>, <see cref="ICollectionOpen"/>, <see cref="ICollectionCloseCoroutine"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ISceneOpenAwaitable"/>, <see cref="ISceneCloseAwaitable"/>, <see cref="ICollectionOpenAwaitable"/>, <see cref="ICollectionCloseAwaitable"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ICollectionExtraDataCallbacks"/>, <see cref="ICollectionExtraDataCallbacksCoroutine"/>, <see cref="ICollectionExtraDataCallbacksAwaitable"/></description>
    ///   </item>
    /// </list>
    /// </summary>
    public interface ISceneCallbacks
    { }

}
