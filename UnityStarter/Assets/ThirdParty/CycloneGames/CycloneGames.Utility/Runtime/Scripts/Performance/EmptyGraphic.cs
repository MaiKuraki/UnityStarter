using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// EmptyGraphic is a lightweight Graphic implementation that produces no visual output.
    /// 
    /// Advantages:
    /// - No vertices or triangles are generated, which means no GPU overhead and no extra draw calls.
    /// - GC friendly because it avoids vertex allocations that a normal Image would create.
    /// - Still fully integrated with Unity UI system, which means it can block raycasts and 
    ///   receive UI events (e.g. click, drag) when raycastTarget is enabled.
    /// 
    /// Recommended use cases:
    /// - Invisible clickable areas or hot zones in UI.
    /// - Placeholder or container elements that need event interaction but no rendering.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class EmptyGraphic : Graphic
    {
        /// <summary>
        /// Overrides mesh generation and clears the VertexHelper.
        /// This prevents any geometry from being submitted to the CanvasRenderer.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}