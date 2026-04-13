using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace CycloneGames.Foundation2D.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class UGUISequenceRenderer : MonoBehaviour, ISpriteSequenceRenderer
    {
        public enum UGUIRenderMode
        {
            SpriteSwap = 0,
            ShaderFlipbookSharedMaterial = 1,
        }

        [SerializeField] private Image image;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UGUIRenderMode renderMode = UGUIRenderMode.SpriteSwap;
        [SerializeField] private Material flipbookSharedMaterial;
        [SerializeField] private FlipbookUVMeshEffect flipbookUvMeshEffect;

        private IReadOnlyList<Sprite> _frames;
        private Vector4[] _targetUvRects;
        private int _targetUvCount;
        private Vector4 _baseUvRect;
        private bool _flipbookReady;

        private void Awake()
        {
            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (flipbookUvMeshEffect == null)
            {
                flipbookUvMeshEffect = GetComponent<FlipbookUVMeshEffect>();
            }
        }

        public void Initialize(IReadOnlyList<Sprite> frames)
        {
            _frames = frames;
            _flipbookReady = false;
            _targetUvCount = 0;

            if (image == null || _frames == null || _frames.Count == 0)
            {
                return;
            }

            if (renderMode != UGUIRenderMode.ShaderFlipbookSharedMaterial)
            {
                return;
            }

            if (flipbookSharedMaterial == null)
            {
                return;
            }

            if (flipbookUvMeshEffect == null)
            {
                flipbookUvMeshEffect = GetComponent<FlipbookUVMeshEffect>();
            }

            if (flipbookUvMeshEffect == null)
            {
                return;
            }

            Sprite first = _frames[0];
            if (first == null || first.texture == null)
            {
                return;
            }

            Texture texture = first.texture;
            EnsureUvCapacity(_frames.Count);
            for (int i = 0; i < _frames.Count; i++)
            {
                Sprite s = _frames[i];
                if (s == null || s.texture == null || s.texture != texture)
                {
                    _targetUvCount = 0;
                    return;
                }

                _targetUvRects[i] = GetNormalizedRect(s);
            }

            _targetUvCount = _frames.Count;
            _baseUvRect = _targetUvRects[0];
            image.material = flipbookSharedMaterial;
            image.sprite = first;
            _flipbookReady = true;
        }

        public void ApplyFrame(int frameIndex, bool forceRefresh)
        {
            if (image == null || _frames == null || _frames.Count == 0)
            {
                return;
            }

            frameIndex = Mathf.Clamp(frameIndex, 0, _frames.Count - 1);
            Sprite sprite = _frames[frameIndex];
            if (sprite == null)
            {
                return;
            }

            if (_flipbookReady && renderMode == UGUIRenderMode.ShaderFlipbookSharedMaterial && frameIndex < _targetUvCount)
            {
                if (forceRefresh || image.sprite == null)
                {
                    image.sprite = _frames[0];
                }

                flipbookUvMeshEffect.SetFlipbookRects(_baseUvRect, _targetUvRects[frameIndex]);
                return;
            }

            if (forceRefresh || image.sprite != sprite)
            {
                image.sprite = sprite;
            }
        }

        public void SetVisible(bool visible)
        {
            if (image != null)
            {
                image.enabled = visible;
            }
        }

        public void SetAlpha(float alpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }
        }

        public void SetScale(Vector3 scale)
        {
            transform.localScale = scale;
        }

        private void EnsureUvCapacity(int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (_targetUvRects != null && _targetUvRects.Length >= count)
            {
                return;
            }

            int capacity = _targetUvRects == null ? 16 : _targetUvRects.Length;
            while (capacity < count)
            {
                capacity <<= 1;
            }

            _targetUvRects = new Vector4[capacity];
        }

        private static Vector4 GetNormalizedRect(Sprite sprite)
        {
            Rect r;
            try
            {
                r = sprite.textureRect;
            }
            catch
            {
                r = sprite.rect;
            }

            Texture tex = sprite.texture;
            float invW = 1f / Mathf.Max(1f, tex.width);
            float invH = 1f / Mathf.Max(1f, tex.height);
            return new Vector4(r.x * invW, r.y * invH, r.width * invW, r.height * invH);
        }
    }
}