using UnityEngine;
using System.Collections.Generic;

namespace CycloneGames.Foundation2D.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteRendererSequenceRenderer : MonoBehaviour, ISpriteSequenceRenderer
    {
        public enum SpriteRenderMode
        {
            SpriteSwap = 0,
            ShaderFlipbookSharedMaterial = 1,
        }

        public enum MaterialStrategy
        {
            UseRendererDefault = 0,
            SharedMaterialOverride = 1,
        }

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private SpriteRenderMode renderMode = SpriteRenderMode.SpriteSwap;
        [SerializeField] private MaterialStrategy materialStrategy = MaterialStrategy.UseRendererDefault;
        [SerializeField] private Material sharedMaterialOverride;
        [SerializeField] private Material flipbookSharedMaterial;

        private IReadOnlyList<Sprite> _frames;
        private Material _defaultSharedMaterial;
        private MaterialPropertyBlock _materialPropertyBlock;
        private Vector4[] _targetUvRects;
        private int _targetUvCount;
        private Vector4 _baseUvRect;
        private bool _flipbookReady;

        private static readonly int FlipbookBaseRectId = Shader.PropertyToID("_FlipbookBaseRect");
        private static readonly int FlipbookTargetRectId = Shader.PropertyToID("_FlipbookTargetRect");

        private void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                _defaultSharedMaterial = spriteRenderer.sharedMaterial;
            }

            ApplyMaterialStrategy();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (!Application.isPlaying)
            {
                if (spriteRenderer != null && _defaultSharedMaterial == null)
                {
                    _defaultSharedMaterial = spriteRenderer.sharedMaterial;
                }

                ApplyMaterialStrategy();
            }
        }
#endif

        public void Initialize(IReadOnlyList<Sprite> frames)
        {
            _frames = frames;
            _flipbookReady = false;
            _targetUvCount = 0;

            if (spriteRenderer == null || _frames == null || _frames.Count == 0)
            {
                return;
            }

            if (renderMode != SpriteRenderMode.ShaderFlipbookSharedMaterial)
            {
                ApplyMaterialStrategy();
                return;
            }

            if (flipbookSharedMaterial == null)
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
            spriteRenderer.sharedMaterial = flipbookSharedMaterial;
            spriteRenderer.sprite = first;
            ApplyFlipbookRects(_targetUvRects[0]);
            _flipbookReady = true;
        }

        public void ApplyFrame(int frameIndex, bool forceRefresh)
        {
            if (spriteRenderer == null || _frames == null || _frames.Count == 0)
            {
                return;
            }

            frameIndex = Mathf.Clamp(frameIndex, 0, _frames.Count - 1);
            Sprite sprite = _frames[frameIndex];
            if (sprite == null)
            {
                return;
            }

            if (_flipbookReady && renderMode == SpriteRenderMode.ShaderFlipbookSharedMaterial && frameIndex < _targetUvCount)
            {
                if (forceRefresh || spriteRenderer.sprite == null)
                {
                    spriteRenderer.sprite = _frames[0];
                }

                ApplyFlipbookRects(_targetUvRects[frameIndex]);
                return;
            }

            if (forceRefresh || spriteRenderer.sprite != sprite)
            {
                spriteRenderer.sprite = sprite;
            }
        }

        public void SetVisible(bool visible)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = visible;
            }
        }

        public void SetAlpha(float alpha)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            Color c = spriteRenderer.color;
            c.a = Mathf.Clamp01(alpha);
            spriteRenderer.color = c;
        }

        public void SetScale(Vector3 scale)
        {
            transform.localScale = scale;
        }

        private void ApplyMaterialStrategy()
        {
            if (spriteRenderer == null)
            {
                return;
            }

            if (renderMode == SpriteRenderMode.ShaderFlipbookSharedMaterial)
            {
                if (flipbookSharedMaterial != null)
                {
                    spriteRenderer.sharedMaterial = flipbookSharedMaterial;
                }
                return;
            }

            if (materialStrategy == MaterialStrategy.SharedMaterialOverride)
            {
                if (sharedMaterialOverride != null)
                {
                    spriteRenderer.sharedMaterial = sharedMaterialOverride;
                }
                return;
            }

            if (_defaultSharedMaterial != null)
            {
                spriteRenderer.sharedMaterial = _defaultSharedMaterial;
            }
        }

        private void ApplyFlipbookRects(Vector4 targetRect)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            if (_materialPropertyBlock == null)
            {
                _materialPropertyBlock = new MaterialPropertyBlock();
            }

            spriteRenderer.GetPropertyBlock(_materialPropertyBlock);
            _materialPropertyBlock.SetVector(FlipbookBaseRectId, _baseUvRect);
            _materialPropertyBlock.SetVector(FlipbookTargetRectId, targetRect);
            spriteRenderer.SetPropertyBlock(_materialPropertyBlock);
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