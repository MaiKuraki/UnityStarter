using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Foundation2D.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class FlipbookUVMeshEffect : BaseMeshEffect
    {
        [SerializeField, HideInInspector] private Vector4 baseUVRect = new(0f, 0f, 1f, 1f);
        [SerializeField, HideInInspector] private Vector4 targetUVRect = new(0f, 0f, 1f, 1f);

        public void SetFlipbookRects(in Vector4 baseRect, in Vector4 targetRect)
        {
            if (baseUVRect == baseRect && targetUVRect == targetRect)
            {
                return;
            }

            baseUVRect = baseRect;
            targetUVRect = targetRect;
            EnsureCanvasChannels();
            graphic?.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive())
            {
                return;
            }

            EnsureCanvasChannels();

            int count = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                UIVertex v = default;
                vh.PopulateUIVertex(ref v, i);
                v.uv1 = baseUVRect;
                v.uv2 = targetUVRect;
                vh.SetUIVertex(v, i);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasChannels();
        }

        private void EnsureCanvasChannels()
        {
            if (graphic == null || graphic.canvas == null)
            {
                return;
            }

            var required = AdditionalCanvasShaderChannels.TexCoord1 |
                           AdditionalCanvasShaderChannels.TexCoord2;

            if ((graphic.canvas.additionalShaderChannels & required) != required)
            {
                graphic.canvas.additionalShaderChannels |= required;
            }
        }
    }
}