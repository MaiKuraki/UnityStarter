using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    /// <summary>An utility class to manage sort order on canvases.</summary>
    public static class CanvasSortOrderUtility
    {

        static readonly Dictionary<Canvas, (Canvas above, Canvas below)> canvases = new();

        public static Canvas GetTopCanvas =>
            canvases.Keys.FirstOrDefault(c => canvases[c].above == null);

        public static Canvas GetBottomCanvas =>
            canvases.Keys.FirstOrDefault(c => canvases[c].below == null);


        [RuntimeInitializeOnLoadMethod]
        static void Init() => canvases.Clear();

        public static void Remove(Canvas canvas) => Remove(canvas, true);
        
        /// <summary>Removes this canvas from the managed list.</summary>
        static void Remove(Canvas canvas, bool update)
        {
            if (!canvas) return;

            // Iterate through the dictionary and update references directly
            foreach (var key in canvases.Keys.ToList()) // Create a copy of the keys to avoid modification issues while iterating
            {
                var (above, below) = canvases[key];

                if (above == canvas)
                    canvases[key] = (canvases[canvas].above, below);  // Replace above with the one above canvas, glue them together

                if (below == canvas)
                    canvases[key] = (above, canvases[canvas].below);  // Replace below with the one below canvas, glue them together
            }

            canvases.Remove(canvas);

            if(update) UpdateOrder();
        }

        /// <summary>Sets the sort order on this canvas to be on top of all other canvases managed by <see cref="CanvasSortOrderUtility"/>.</summary>
        public static void PutOnTop(this Canvas canvas)
        {
            if (!canvas)
                return;

            if (canvases.ContainsKey(canvas) && canvases[canvas].above == null)
                return;  // No need to change anything if it's already on top

            Remove(canvas, false);

            var topCanvas = GetTopCanvas;

            canvases.Add(canvas, (null, topCanvas));

            if (topCanvas != null)
                canvases[topCanvas] = (canvas, canvases[topCanvas].below);

            UpdateOrder();
        }

        /// <summary>Sets the sort order on this canvas to be on bottom of all other canvases managed by <see cref="CanvasSortOrderUtility"/>.</summary>
        public static void PutAtBottom(this Canvas canvas)
        {
            if (!canvas)
                return;

            if (canvases.ContainsKey(canvas) && canvases[canvas].below == null)
                return;  // No need to change anything if it's already on bottom

            Remove(canvas, false);

            var bottomCanvas = GetBottomCanvas;

            canvases.Add(canvas, (bottomCanvas, null));

            // If there's a current bottom canvas, update it to point to the new bottom canvas
            if (bottomCanvas != null)
                canvases[bottomCanvas] = (canvases[bottomCanvas].above, canvas);

            UpdateOrder();
        }

        /// <summary>Inserts this canvas above target.</summary>
        /// <param name="canvas">The canvas to add.</param>
        /// <param name="target"></param>
        /// <remarks>See parameter comments for more info.</remarks>
        public static void PutAbove(this Canvas canvas, Canvas target = null)
        {
            // Validate parameters
            if (!canvas)
                throw new ArgumentNullException(nameof(canvas), "Canvas cannot be null.");
            if (!target)
                throw new ArgumentNullException(nameof(target), "Target canvas cannot be null.");
            if (canvas == target)
                throw new ArgumentException("Canvas cannot be the same as target.");
            if (!canvases.ContainsKey(target))
                throw new InvalidOperationException("Target canvas is not managed by the canvas system.");


            Remove(canvas);

            // get target
            var (above, below) = canvases[target];
            // get parent
            var (parentAbove, _) = above != null ? canvases[above] : (null, null);

            // replace child on parent
            if(above != null)
            canvases[above] = (parentAbove, canvas);
            // replace parent on target
            canvases[target] = (canvas, below);

            canvases.Add(canvas, (above, target));

            UpdateOrder();
        }

        /// <summary>Inserts this canvas below target.</summary>
        /// <param name="canvas">The canvas to add.</param>
        /// <param name="target"></param>
        /// <remarks>See parameter comments for more info.</remarks>
        public static void PutBelow(this Canvas canvas, Canvas target = null)
        {
            // Validate parameters
            if (!canvas)
                throw new ArgumentNullException(nameof(canvas), "Canvas cannot be null.");
            if (!target)
                throw new ArgumentNullException(nameof(target), "Target canvas cannot be null.");
            if (canvas == target)
                throw new ArgumentException("Canvas cannot be the same as target.");
            if (!canvases.ContainsKey(target))
                throw new InvalidOperationException("Target canvas is not managed by the canvas system.");


            Remove(canvas);

            // get target
            var (above, below) = canvases[target];
            // get child
            var (childBelow, _) = below != null ? canvases[below] : (null, null);

            // replace parent on child
            if(below != null)
            canvases[below] = (canvas, childBelow);
            // replace child on target
            canvases[target] = (above, canvas);

            canvases.Add(canvas, (target, below));

            UpdateOrder();
        }


        static void UpdateOrder()
        {
            UpdateOrderRecursive(GetTopCanvas);
        }

        private static void UpdateOrderRecursive(Canvas next, int index = 0)
        {
            if (next == null) return;
            
            next.sortingOrder = short.MaxValue - index - 200;

            var (_, below) = canvases[next];

            if(below != null)
                UpdateOrderRecursive(below, index + 1);
        }
    }
}
