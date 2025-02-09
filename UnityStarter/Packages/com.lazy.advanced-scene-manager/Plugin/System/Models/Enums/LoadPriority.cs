using AdvancedSceneManager.Core;
using UnityEngine;

namespace AdvancedSceneManager.Models.Enums
{

    /// <inheritdoc cref="ThreadPriority"/>
    public enum LoadPriority
    {

        /// <summary>Let ASM automatically decide <see cref="ThreadPriority"/>.</summary>
        /// <remarks>
        /// When evaluating the following order will be used:
        /// <list type="number">
        /// <item><see cref="SceneOperation.With(LoadPriority)"/> - highest priority</item>
        /// <item><see cref="Scene.loadPriority"/></item>
        /// <item><see cref="SceneCollection.loadPriority"/> - assuming a collection is being opened</item>
        /// <item><see cref="Application.backgroundLoadingPriority"/> - fallback to current value</item>
        /// </list>
        /// </remarks>
        Auto = -2,

        /// <summary>Lowest thread priority.</summary>
        Low = ThreadPriority.Low,

        /// <summary>Below normal thread priority.</summary>
        BelowNormal = ThreadPriority.BelowNormal,

        /// <summary>Normal thread priority.</summary>
        Normal = ThreadPriority.Normal,

        /// <summary>Highest thread priority.</summary>
        High = ThreadPriority.High,

    }

}
