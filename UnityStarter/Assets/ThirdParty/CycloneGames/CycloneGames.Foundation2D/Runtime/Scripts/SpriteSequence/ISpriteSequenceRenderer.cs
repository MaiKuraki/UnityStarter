using UnityEngine;
using System.Collections.Generic;

namespace CycloneGames.Foundation2D.Runtime
{
    public interface ISpriteSequenceRenderer
    {
        void Initialize(IReadOnlyList<Sprite> frames);
        void ApplyFrame(int frameIndex, bool forceRefresh);
        void SetVisible(bool visible);
        void SetAlpha(float alpha);
        void SetScale(Vector3 scale);
    }
}