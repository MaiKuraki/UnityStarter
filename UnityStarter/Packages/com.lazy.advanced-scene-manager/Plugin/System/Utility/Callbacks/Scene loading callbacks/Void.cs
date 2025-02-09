using AdvancedSceneManager.Models;
using UnityEngine;

namespace AdvancedSceneManager.Callbacks
{

    /// <summary>Callbacks for a <see cref="ScriptableObject"/> that has been set as extra data for a collection.
    /// <br/>
    /// <br/>
    /// See also:
    /// <list type="bullet">
    ///   <item>
    ///     <description><see cref="ICollectionExtraDataCallbacks"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ICollectionExtraDataCallbacksCoroutine"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ICollectionExtraDataCallbacksAwaitable"/></description>
    ///   </item>
    /// </list>
    /// </summary>
    public interface ICollectionExtraDataCallbacks : ICollectionOpen, ICollectionClose
    { }

    /// <summary>
    /// Callback for when the scene that a <see cref="MonoBehaviour"/> is contained within is opened.
    /// See also:
    /// <list type="bullet">
    ///   <item>
    ///     <description><see cref="ISceneOpen"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ISceneOpenCoroutine"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ISceneOpenAwaitable"/></description>
    ///   </item>
    /// </list>
    /// </summary>
    public interface ISceneOpen : ISceneCallbacks
    {
        /// <inheritdoc cref="ISceneOpen"/>
        void OnSceneOpen();
    }

    /// <summary>Callback for when the scene that a <see cref="MonoBehaviour"/> is contained within is closed.</summary>
    /// <remarks>See also:
    /// <br/>
    /// <br/>
    /// <list type="bullet">
    ///   <item>
    ///     <description><see cref="ISceneClose"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ISceneCloseCoroutine"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ISceneCloseAwaitable"/></description>
    ///   </item>
    /// </list>
    /// </summary>
    public interface ISceneClose : ISceneCallbacks
    {
        /// <inheritdoc cref="ISceneClose"/>
        void OnSceneClose();
    }

    /// <summary>
    /// <para>Callback for when a scene, in a collection, that a <see cref="MonoBehaviour"/> is contained within is opened.</para>
    /// <para>Called before loading screen is hidden, if one is defined, or else just when collection has opened.</para>
    /// <br/>
    /// <br/>
    /// See also:
    /// <list type="bullet">
    ///   <item>
    ///     <description><see cref="ICollectionOpen"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ICollectionOpen"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ICollectionOpenAwaitable"/></description>
    ///   </item>
    /// </list>
    /// </summary>
    public interface ICollectionOpen : ISceneCallbacks
    {
        /// <inheritdoc cref="ICollectionOpen"/>
        void OnCollectionOpen(SceneCollection collection);
    }

    /// <summary>
    /// <para>Callback for when a scene, in a collection, that a <see cref="MonoBehaviour"/> is contained within is closed.</para>
    /// <para>Called after loading screen has opened, if one is defined, or else just before collection is closed.</para>
    /// <br/>
    /// <br/>
    /// See also:
    /// <list type="bullet">
    ///   <item>
    ///     <description><see cref="ICollectionClose"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ICollectionCloseCoroutine"/></description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="ICollectionCloseAwaitable"/></description>
    ///   </item>
    /// </list>
    /// </summary>
    public interface ICollectionClose : ISceneCallbacks
    {
        /// <inheritdoc cref="ICollectionClose"/>
        void OnCollectionClose(SceneCollection collection);
    }

}
