using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Foundation2D.Runtime
{
    public interface ISpriteSequenceRenderer
    {
        /// <summary>Initializes renderer-owned caches for the borrowed frame list.</summary>
        void Initialize(IReadOnlyList<Sprite> frames);

        /// <summary>Commits one frame on the Unity main thread.</summary>
        void ApplyFrame(int frameIndex, bool forceRefresh);

        /// <summary>Controls visibility used by loop interval presentation.</summary>
        void SetVisible(bool visible);
    }
}
