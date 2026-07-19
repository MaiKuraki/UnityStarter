using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// A UGUI <see cref="Graphic"/> that emits no geometry while remaining available to raycasts.
    /// </summary>
    /// <remarks>
    /// The component still participates in Canvas rebuild and raycast processing. Use it for explicit
    /// invisible interaction regions, not as a claim of zero Canvas or CPU overhead.
    /// </remarks>
    [AddComponentMenu("UI/Empty Graphic (CycloneGames)")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EmptyGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}
