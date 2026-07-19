using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Foundation2D.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class FlipbookUVMeshEffect : BaseMeshEffect
    {
        internal const AdditionalCanvasShaderChannels RequiredCanvasChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2;

        [SerializeField, HideInInspector] private Vector4 baseUVRect = new(0f, 0f, 1f, 1f);
        [SerializeField, HideInInspector] private Vector4 targetUVRect = new(0f, 0f, 1f, 1f);

        public void SetFlipbookRects(in Vector4 baseRect, in Vector4 targetRect)
        {
            Graphic targetGraphic = graphic;
            if (targetGraphic == null ||
                (baseUVRect == baseRect && targetUVRect == targetRect))
            {
                return;
            }

            baseUVRect = baseRect;
            targetUVRect = targetRect;
            targetGraphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh == null || !HasRequiredCanvasChannels())
            {
                return;
            }

            int count = vh.currentVertCount;
            UIVertex vertex = default;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.uv1 = baseUVRect;
                vertex.uv2 = targetUVRect;
                vh.SetUIVertex(vertex, i);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            graphic?.SetVerticesDirty();
        }

        internal bool IsReadyFor(Graphic expectedGraphic)
        {
            return expectedGraphic != null &&
                   graphic == expectedGraphic &&
                   HasRequiredCanvasChannels();
        }

        internal bool HasRequiredCanvasChannels()
        {
            Graphic targetGraphic = graphic;
            Canvas canvas = targetGraphic != null ? targetGraphic.canvas : null;
            return canvas != null &&
                   (canvas.additionalShaderChannels & RequiredCanvasChannels) == RequiredCanvasChannels;
        }

        internal void GetFlipbookRects(out Vector4 baseRect, out Vector4 targetRect)
        {
            baseRect = baseUVRect;
            targetRect = targetUVRect;
        }

        internal bool HasFlipbookRects(in Vector4 baseRect, in Vector4 targetRect)
        {
            return Exactly(baseUVRect, baseRect) && Exactly(targetUVRect, targetRect);
        }

        private static bool Exactly(in Vector4 a, in Vector4 b)
        {
            return a.x.Equals(b.x) &&
                   a.y.Equals(b.y) &&
                   a.z.Equals(b.z) &&
                   a.w.Equals(b.w);
        }
    }
}
