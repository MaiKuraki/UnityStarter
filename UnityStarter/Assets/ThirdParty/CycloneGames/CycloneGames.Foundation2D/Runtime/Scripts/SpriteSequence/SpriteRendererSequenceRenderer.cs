using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Foundation2D.Runtime
{
    internal enum SpriteFlipbookCompatibilityError
    {
        None = 0,
        MissingFrames,
        OutputBufferTooSmall,
        NullSprite,
        MissingTexture,
        TextureMismatch,
        AlphaTextureMismatch,
        PackedStateMismatch,
        TightPackingUnsupported,
        RotatedPackingUnsupported,
        TextureRectUnavailable,
        InvalidTextureRect,
        RectSizeMismatch,
        TextureRectSizeMismatch,
        TextureRectOffsetMismatch,
        PivotMismatch,
        PixelsPerUnitMismatch,
        BorderMismatch,
        BoundsMismatch,
        UnsupportedGeometry,
        GeometryMismatch,
        UvLayoutMismatch,
        IndexLayoutMismatch,
    }

    internal static class SpriteFlipbookCompatibility
    {
        private const float ComparisonTolerance = 0.0001f;

        internal static bool TryValidateAndBuild(
            IReadOnlyList<Sprite> frames,
            Vector4[] targetUvRects,
            out Vector4 baseUvRect,
            out SpriteFlipbookCompatibilityError error,
            out int errorFrameIndex)
        {
            baseUvRect = default;
            error = SpriteFlipbookCompatibilityError.None;
            errorFrameIndex = -1;

            if (frames == null || frames.Count == 0)
            {
                error = SpriteFlipbookCompatibilityError.MissingFrames;
                return false;
            }

            if (targetUvRects != null && targetUvRects.Length < frames.Count)
            {
                error = SpriteFlipbookCompatibilityError.OutputBufferTooSmall;
                return false;
            }

            Sprite first = frames[0];
            if (first == null)
            {
                error = SpriteFlipbookCompatibilityError.NullSprite;
                errorFrameIndex = 0;
                return false;
            }

            Texture2D texture = first.texture;
            if (texture == null)
            {
                error = SpriteFlipbookCompatibilityError.MissingTexture;
                errorFrameIndex = 0;
                return false;
            }

            if (!TryValidatePacking(first, out error))
            {
                errorFrameIndex = 0;
                return false;
            }

            if (!TryGetTextureData(first, texture, out Rect firstTextureRect, out Vector2 firstTextureRectOffset))
            {
                error = SpriteFlipbookCompatibilityError.TextureRectUnavailable;
                errorFrameIndex = 0;
                return false;
            }

            if (!IsValidTextureRect(firstTextureRect, texture))
            {
                error = SpriteFlipbookCompatibilityError.InvalidTextureRect;
                errorFrameIndex = 0;
                return false;
            }

            Vector2[] firstVertices = first.vertices;
            Vector2[] firstUvs = first.uv;
            ushort[] firstTriangles = first.triangles;
            if (!IsSupportedFullRectGeometry(first, firstTextureRect, texture, firstVertices, firstUvs, firstTriangles))
            {
                error = SpriteFlipbookCompatibilityError.UnsupportedGeometry;
                errorFrameIndex = 0;
                return false;
            }

            Rect firstRect = first.rect;
            Vector2 firstPivot = first.pivot;
            float firstPixelsPerUnit = first.pixelsPerUnit;
            Vector4 firstBorder = first.border;
            Bounds firstBounds = first.bounds;
            bool firstPacked = first.packed;
            Texture2D firstAlphaTexture = first.associatedAlphaSplitTexture;

            baseUvRect = ToNormalizedRect(firstTextureRect, texture);
            if (targetUvRects != null)
            {
                targetUvRects[0] = baseUvRect;
            }

            for (int i = 1; i < frames.Count; i++)
            {
                Sprite sprite = frames[i];
                if (sprite == null)
                {
                    return Fail(SpriteFlipbookCompatibilityError.NullSprite, i, out error, out errorFrameIndex);
                }

                if (sprite.texture == null)
                {
                    return Fail(SpriteFlipbookCompatibilityError.MissingTexture, i, out error, out errorFrameIndex);
                }

                if (sprite.texture != texture)
                {
                    return Fail(SpriteFlipbookCompatibilityError.TextureMismatch, i, out error, out errorFrameIndex);
                }

                if (sprite.associatedAlphaSplitTexture != firstAlphaTexture)
                {
                    return Fail(SpriteFlipbookCompatibilityError.AlphaTextureMismatch, i, out error, out errorFrameIndex);
                }

                if (sprite.packed != firstPacked)
                {
                    return Fail(SpriteFlipbookCompatibilityError.PackedStateMismatch, i, out error, out errorFrameIndex);
                }

                if (!TryValidatePacking(sprite, out error))
                {
                    errorFrameIndex = i;
                    return false;
                }

                if (!TryGetTextureData(sprite, texture, out Rect textureRect, out Vector2 textureRectOffset))
                {
                    return Fail(SpriteFlipbookCompatibilityError.TextureRectUnavailable, i, out error, out errorFrameIndex);
                }

                if (!IsValidTextureRect(textureRect, texture))
                {
                    return Fail(SpriteFlipbookCompatibilityError.InvalidTextureRect, i, out error, out errorFrameIndex);
                }

                Rect rect = sprite.rect;
                if (!Exactly(rect.size, firstRect.size))
                {
                    return Fail(SpriteFlipbookCompatibilityError.RectSizeMismatch, i, out error, out errorFrameIndex);
                }

                if (!Exactly(textureRect.size, firstTextureRect.size))
                {
                    return Fail(SpriteFlipbookCompatibilityError.TextureRectSizeMismatch, i, out error, out errorFrameIndex);
                }

                if (!Exactly(textureRectOffset, firstTextureRectOffset))
                {
                    return Fail(SpriteFlipbookCompatibilityError.TextureRectOffsetMismatch, i, out error, out errorFrameIndex);
                }

                if (!Exactly(sprite.pivot, firstPivot))
                {
                    return Fail(SpriteFlipbookCompatibilityError.PivotMismatch, i, out error, out errorFrameIndex);
                }

                if (!sprite.pixelsPerUnit.Equals(firstPixelsPerUnit))
                {
                    return Fail(SpriteFlipbookCompatibilityError.PixelsPerUnitMismatch, i, out error, out errorFrameIndex);
                }

                if (!Exactly(sprite.border, firstBorder))
                {
                    return Fail(SpriteFlipbookCompatibilityError.BorderMismatch, i, out error, out errorFrameIndex);
                }

                if (!Exactly(sprite.bounds, firstBounds))
                {
                    return Fail(SpriteFlipbookCompatibilityError.BoundsMismatch, i, out error, out errorFrameIndex);
                }

                Vector2[] vertices = sprite.vertices;
                Vector2[] uvs = sprite.uv;
                ushort[] triangles = sprite.triangles;
                if (!IsSupportedFullRectGeometry(sprite, textureRect, texture, vertices, uvs, triangles))
                {
                    return Fail(SpriteFlipbookCompatibilityError.UnsupportedGeometry, i, out error, out errorFrameIndex);
                }

                if (!Exactly(vertices, firstVertices))
                {
                    return Fail(SpriteFlipbookCompatibilityError.GeometryMismatch, i, out error, out errorFrameIndex);
                }

                if (!HasSameNormalizedUvLayout(uvs, textureRect, firstUvs, firstTextureRect, texture))
                {
                    return Fail(SpriteFlipbookCompatibilityError.UvLayoutMismatch, i, out error, out errorFrameIndex);
                }

                if (!Exactly(triangles, firstTriangles))
                {
                    return Fail(SpriteFlipbookCompatibilityError.IndexLayoutMismatch, i, out error, out errorFrameIndex);
                }

                if (targetUvRects != null)
                {
                    targetUvRects[i] = ToNormalizedRect(textureRect, texture);
                }
            }

            return true;
        }

        internal static string GetErrorMessage(SpriteFlipbookCompatibilityError error)
        {
            switch (error)
            {
                case SpriteFlipbookCompatibilityError.None:
                    return "The frames are compatible.";
                case SpriteFlipbookCompatibilityError.MissingFrames:
                    return "No frames were provided.";
                case SpriteFlipbookCompatibilityError.OutputBufferTooSmall:
                    return "The UV output buffer is smaller than the frame collection.";
                case SpriteFlipbookCompatibilityError.NullSprite:
                    return "A frame does not reference a Sprite.";
                case SpriteFlipbookCompatibilityError.MissingTexture:
                    return "A frame does not reference a texture.";
                case SpriteFlipbookCompatibilityError.TextureMismatch:
                    return "All frames must reference the same resolved texture or atlas page.";
                case SpriteFlipbookCompatibilityError.AlphaTextureMismatch:
                    return "All frames must reference the same external alpha texture.";
                case SpriteFlipbookCompatibilityError.PackedStateMismatch:
                    return "Packed and unpacked sprites cannot be mixed.";
                case SpriteFlipbookCompatibilityError.TightPackingUnsupported:
                    return "Tight-packed sprites are not supported by the UV remap path.";
                case SpriteFlipbookCompatibilityError.RotatedPackingUnsupported:
                    return "Rotated atlas sprites are not supported by the UV remap path.";
                case SpriteFlipbookCompatibilityError.TextureRectUnavailable:
                    return "A direct texture rectangle is unavailable; sprite.rect is not a valid atlas UV fallback.";
                case SpriteFlipbookCompatibilityError.InvalidTextureRect:
                    return "A texture rectangle is empty, non-finite, or outside the resolved texture.";
                case SpriteFlipbookCompatibilityError.RectSizeMismatch:
                    return "All frames must have the same source rectangle size.";
                case SpriteFlipbookCompatibilityError.TextureRectSizeMismatch:
                    return "All frames must have the same resolved texture rectangle size.";
                case SpriteFlipbookCompatibilityError.TextureRectOffsetMismatch:
                    return "All frames must have the same texture trimming offset.";
                case SpriteFlipbookCompatibilityError.PivotMismatch:
                    return "All frames must have the same pivot.";
                case SpriteFlipbookCompatibilityError.PixelsPerUnitMismatch:
                    return "All frames must have the same pixels-per-unit value.";
                case SpriteFlipbookCompatibilityError.BorderMismatch:
                    return "All frames must have the same border.";
                case SpriteFlipbookCompatibilityError.BoundsMismatch:
                    return "All frames must have the same local bounds.";
                case SpriteFlipbookCompatibilityError.UnsupportedGeometry:
                    return "Frames must use a non-tight, unrotated rectangular quad mesh.";
                case SpriteFlipbookCompatibilityError.GeometryMismatch:
                    return "All frames must have identical vertex geometry.";
                case SpriteFlipbookCompatibilityError.UvLayoutMismatch:
                    return "All frames must have an identical, axis-aligned UV layout.";
                case SpriteFlipbookCompatibilityError.IndexLayoutMismatch:
                    return "All frames must have identical triangle topology.";
                default:
                    return "The frame collection is not compatible with UV remapping.";
            }
        }

        private static bool TryValidatePacking(Sprite sprite, out SpriteFlipbookCompatibilityError error)
        {
            error = SpriteFlipbookCompatibilityError.None;
            if (!sprite.packed)
            {
                return true;
            }

            if (sprite.packingMode != SpritePackingMode.Rectangle)
            {
                error = SpriteFlipbookCompatibilityError.TightPackingUnsupported;
                return false;
            }

            if (sprite.packingRotation != SpritePackingRotation.None)
            {
                error = SpriteFlipbookCompatibilityError.RotatedPackingUnsupported;
                return false;
            }

            return true;
        }

        private static bool TryGetTextureData(Sprite sprite, Texture2D texture, out Rect textureRect, out Vector2 textureRectOffset)
        {
            textureRect = default;
            textureRectOffset = default;
            try
            {
                textureRect = sprite.textureRect;
                textureRectOffset = sprite.textureRectOffset;
                return sprite.texture == texture;
            }
            catch (UnityException)
            {
                return false;
            }
        }

        private static bool IsSupportedFullRectGeometry(
            Sprite sprite,
            Rect textureRect,
            Texture2D texture,
            Vector2[] vertices,
            Vector2[] uvs,
            ushort[] triangles)
        {
            if (vertices == null || vertices.Length != 4 ||
                uvs == null || uvs.Length != 4 ||
                triangles == null || triangles.Length != 6)
            {
                return false;
            }

            Rect sourceRect = sprite.rect;
            float pixelsPerUnit = sprite.pixelsPerUnit;
            if (!IsFinitePositive(sourceRect.width) || !IsFinitePositive(sourceRect.height) || !IsFinitePositive(pixelsPerUnit))
            {
                return false;
            }

            int cornerMask = 0;
            Vector2 pivot = sprite.pivot;
            for (int i = 0; i < vertices.Length; i++)
            {
                float geometryX = ((vertices[i].x * pixelsPerUnit) + pivot.x) / sourceRect.width;
                float geometryY = ((vertices[i].y * pixelsPerUnit) + pivot.y) / sourceRect.height;
                float uvX = ((uvs[i].x * texture.width) - textureRect.x) / textureRect.width;
                float uvY = ((uvs[i].y * texture.height) - textureRect.y) / textureRect.height;

                if (!TryGetUnitCorner(geometryX, geometryY, out int corner) ||
                    !Approximately(geometryX, uvX) ||
                    !Approximately(geometryY, uvY))
                {
                    return false;
                }

                cornerMask |= 1 << corner;
            }

            if (cornerMask != 0b1111)
            {
                return false;
            }

            int referencedCorners = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                ushort a = triangles[i];
                ushort b = triangles[i + 1];
                ushort c = triangles[i + 2];
                if (a >= 4 || b >= 4 || c >= 4 || a == b || b == c || a == c)
                {
                    return false;
                }

                referencedCorners |= (1 << a) | (1 << b) | (1 << c);
            }

            return referencedCorners == 0b1111;
        }

        private static bool HasSameNormalizedUvLayout(
            Vector2[] uvs,
            Rect textureRect,
            Vector2[] firstUvs,
            Rect firstTextureRect,
            Texture2D texture)
        {
            if (uvs == null || firstUvs == null || uvs.Length != firstUvs.Length)
            {
                return false;
            }

            for (int i = 0; i < uvs.Length; i++)
            {
                float x = ((uvs[i].x * texture.width) - textureRect.x) / textureRect.width;
                float y = ((uvs[i].y * texture.height) - textureRect.y) / textureRect.height;
                float firstX = ((firstUvs[i].x * texture.width) - firstTextureRect.x) / firstTextureRect.width;
                float firstY = ((firstUvs[i].y * texture.height) - firstTextureRect.y) / firstTextureRect.height;
                if (!Approximately(x, firstX) || !Approximately(y, firstY))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidTextureRect(Rect rect, Texture2D texture)
        {
            return IsFinite(rect.x) && IsFinite(rect.y) &&
                   IsFinitePositive(rect.width) && IsFinitePositive(rect.height) &&
                   rect.xMin >= -ComparisonTolerance && rect.yMin >= -ComparisonTolerance &&
                   rect.xMax <= texture.width + ComparisonTolerance &&
                   rect.yMax <= texture.height + ComparisonTolerance;
        }

        private static Vector4 ToNormalizedRect(Rect rect, Texture2D texture)
        {
            float inverseWidth = 1f / texture.width;
            float inverseHeight = 1f / texture.height;
            return new Vector4(
                rect.x * inverseWidth,
                rect.y * inverseHeight,
                rect.width * inverseWidth,
                rect.height * inverseHeight);
        }

        private static bool TryGetUnitCorner(float x, float y, out int corner)
        {
            corner = 0;
            if (!TryGetUnitCoordinate(x, out int cornerX) || !TryGetUnitCoordinate(y, out int cornerY))
            {
                return false;
            }

            corner = cornerX | (cornerY << 1);
            return true;
        }

        private static bool TryGetUnitCoordinate(float value, out int coordinate)
        {
            if (Approximately(value, 0f))
            {
                coordinate = 0;
                return true;
            }

            if (Approximately(value, 1f))
            {
                coordinate = 1;
                return true;
            }

            coordinate = 0;
            return false;
        }

        private static bool Exactly(Vector2 a, Vector2 b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y);
        }

        private static bool Exactly(Vector4 a, Vector4 b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z) && a.w.Equals(b.w);
        }

        private static bool Exactly(Bounds a, Bounds b)
        {
            return Exactly(a.center, b.center) && Exactly(a.size, b.size);
        }

        private static bool Exactly(Vector3 a, Vector3 b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);
        }

        private static bool Exactly(Vector2[] a, Vector2[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!Exactly(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Exactly(ushort[] a, ushort[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Approximately(float a, float b)
        {
            return IsFinite(a) && IsFinite(b) && Mathf.Abs(a - b) <= ComparisonTolerance;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && IsFinite(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Fail(
            SpriteFlipbookCompatibilityError failure,
            int frameIndex,
            out SpriteFlipbookCompatibilityError error,
            out int errorFrameIndex)
        {
            error = failure;
            errorFrameIndex = frameIndex;
            return false;
        }
    }

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

        internal const string FlipbookShaderName = "Sprites/FlipbookRemap";

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private SpriteRenderMode renderMode = SpriteRenderMode.SpriteSwap;
        [SerializeField] private MaterialStrategy materialStrategy = MaterialStrategy.UseRendererDefault;
        [SerializeField] private Material sharedMaterialOverride;
        [SerializeField] private Material flipbookSharedMaterial;

        private IReadOnlyList<Sprite> _frames;
        private Vector4[] _targetUvRects;
        private int _targetUvCount;
        private Vector4 _baseUvRect;
        private Vector4 _currentTargetUvRect;
        private bool _flipbookReady;

        private Material _validatedFlipbookMaterial;
        private Shader _validatedFlipbookShader;

        private SpriteRenderer _renderStateOwner;
        private Material _originalSharedMaterial;
        private Material _ownedMaterial;
        private MaterialPropertyBlock _workingPropertyBlock;
        private Vector4 _originalBaseUvRect;
        private Vector4 _originalTargetUvRect;
        private bool _ownsRenderState;
        private bool _ownsFlipbookProperties;

        private static readonly int FlipbookBaseRectId = Shader.PropertyToID("_FlipbookBaseRect");
        private static readonly int FlipbookTargetRectId = Shader.PropertyToID("_FlipbookTargetRect");

        private void Awake()
        {
            EnsureDependencies();
        }

        private void OnEnable()
        {
            EnsureDependencies();
            RestoreConfiguredRenderState();
        }

        private void OnDisable()
        {
            RestoreOwnedRenderState();
        }

        private void OnDestroy()
        {
            RestoreOwnedRenderState();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureDependencies();
        }
#endif

        public void Initialize(IReadOnlyList<Sprite> frames)
        {
            EnsureDependencies();
            RestoreOwnedRenderState();

            _frames = frames;
            _flipbookReady = false;
            _targetUvCount = 0;
            _validatedFlipbookMaterial = null;
            _validatedFlipbookShader = null;

            if (spriteRenderer == null || frames == null || frames.Count == 0)
            {
                return;
            }

            if (renderMode != SpriteRenderMode.ShaderFlipbookSharedMaterial)
            {
                ApplySpriteSwapMaterialStrategy();
                return;
            }

            if (!IsCompatibleFlipbookMaterial(flipbookSharedMaterial))
            {
                FallbackToSpriteSwap(0, "The assigned material must use the " + FlipbookShaderName + " shader.");
                return;
            }

            EnsureUvCapacity(frames.Count);
            if (!SpriteFlipbookCompatibility.TryValidateAndBuild(
                    frames,
                    _targetUvRects,
                    out _baseUvRect,
                    out SpriteFlipbookCompatibilityError error,
                    out int errorFrameIndex))
            {
                FallbackToSpriteSwap(
                    errorFrameIndex,
                    SpriteFlipbookCompatibility.GetErrorMessage(error));
                return;
            }

            _targetUvCount = frames.Count;
            _currentTargetUvRect = _targetUvRects[0];
            _validatedFlipbookMaterial = flipbookSharedMaterial;
            _validatedFlipbookShader = flipbookSharedMaterial.shader;
            _flipbookReady = true;
            AcquireRenderState(flipbookSharedMaterial, true);
            spriteRenderer.sprite = frames[0];
            ApplyFlipbookRects(_currentTargetUvRect);
        }

        public void ApplyFrame(int frameIndex, bool forceRefresh)
        {
            EnsureDependencies();
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

            if (_flipbookReady &&
                renderMode == SpriteRenderMode.ShaderFlipbookSharedMaterial &&
                frameIndex < _targetUvCount &&
                IsValidatedFlipbookMaterialCurrent())
            {
                AcquireRenderState(flipbookSharedMaterial, true);
                if (forceRefresh || spriteRenderer.sprite != _frames[0])
                {
                    spriteRenderer.sprite = _frames[0];
                }

                _currentTargetUvRect = _targetUvRects[frameIndex];
                ApplyFlipbookRects(_currentTargetUvRect);
                return;
            }

            if (_flipbookReady)
            {
                _flipbookReady = false;
                _targetUvCount = 0;
                _validatedFlipbookMaterial = null;
                _validatedFlipbookShader = null;
                RestoreOwnedRenderState();
            }

            ApplySpriteSwapMaterialStrategy();

            if (forceRefresh || spriteRenderer.sprite != sprite)
            {
                spriteRenderer.sprite = sprite;
            }
        }

        public void SetVisible(bool visible)
        {
            EnsureDependencies();
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = visible;
            }
        }

        private void EnsureDependencies()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (_ownsRenderState && _renderStateOwner != spriteRenderer)
            {
                RestoreOwnedRenderState();
            }
        }

        private void RestoreConfiguredRenderState()
        {
            if (spriteRenderer == null || _frames == null || _frames.Count == 0)
            {
                return;
            }

            if (_flipbookReady && IsValidatedFlipbookMaterialCurrent())
            {
                AcquireRenderState(flipbookSharedMaterial, true);
                spriteRenderer.sprite = _frames[0];
                ApplyFlipbookRects(_currentTargetUvRect);
                return;
            }

            ApplySpriteSwapMaterialStrategy();
        }

        private void ApplySpriteSwapMaterialStrategy()
        {
            if (spriteRenderer == null)
            {
                return;
            }

            if (renderMode == SpriteRenderMode.SpriteSwap &&
                materialStrategy == MaterialStrategy.SharedMaterialOverride &&
                sharedMaterialOverride != null)
            {
                AcquireRenderState(sharedMaterialOverride, false);
                return;
            }

            RestoreOwnedRenderState();
        }

        private void AcquireRenderState(Material material, bool ownsFlipbookProperties)
        {
            if (spriteRenderer == null || material == null)
            {
                return;
            }

            if (_ownsRenderState &&
                (_renderStateOwner != spriteRenderer ||
                 _renderStateOwner.sharedMaterial != _ownedMaterial ||
                 _ownsFlipbookProperties != ownsFlipbookProperties))
            {
                RestoreOwnedRenderState();
            }

            if (!_ownsRenderState)
            {
                _renderStateOwner = spriteRenderer;
                _originalSharedMaterial = spriteRenderer.sharedMaterial;
                if (ownsFlipbookProperties)
                {
                    _workingPropertyBlock ??= new MaterialPropertyBlock();
                    spriteRenderer.GetPropertyBlock(_workingPropertyBlock);
                    _originalBaseUvRect = _workingPropertyBlock.GetVector(FlipbookBaseRectId);
                    _originalTargetUvRect = _workingPropertyBlock.GetVector(FlipbookTargetRectId);
                }

                _ownsFlipbookProperties = ownsFlipbookProperties;
                _ownsRenderState = true;
            }

            if (spriteRenderer.sharedMaterial != material)
            {
                spriteRenderer.sharedMaterial = material;
            }

            _ownedMaterial = material;
        }

        private void RestoreOwnedRenderState()
        {
            if (!_ownsRenderState)
            {
                return;
            }

            if (_renderStateOwner != null && _renderStateOwner.sharedMaterial == _ownedMaterial)
            {
                if (_ownsFlipbookProperties)
                {
                    _workingPropertyBlock ??= new MaterialPropertyBlock();
                    _renderStateOwner.GetPropertyBlock(_workingPropertyBlock);
                    _workingPropertyBlock.SetVector(FlipbookBaseRectId, _originalBaseUvRect);
                    _workingPropertyBlock.SetVector(FlipbookTargetRectId, _originalTargetUvRect);
                    _renderStateOwner.SetPropertyBlock(_workingPropertyBlock);
                }

                _renderStateOwner.sharedMaterial = _originalSharedMaterial;
            }

            ReleaseRenderStateOwnership();
        }

        private void ReleaseRenderStateOwnership()
        {
            _renderStateOwner = null;
            _originalSharedMaterial = null;
            _ownedMaterial = null;
            _originalBaseUvRect = default;
            _originalTargetUvRect = default;
            _ownsRenderState = false;
            _ownsFlipbookProperties = false;
        }

        private void ApplyFlipbookRects(Vector4 targetRect)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            _workingPropertyBlock ??= new MaterialPropertyBlock();
            spriteRenderer.GetPropertyBlock(_workingPropertyBlock);
            _workingPropertyBlock.SetVector(FlipbookBaseRectId, _baseUvRect);
            _workingPropertyBlock.SetVector(FlipbookTargetRectId, targetRect);
            spriteRenderer.SetPropertyBlock(_workingPropertyBlock);
        }

        private void FallbackToSpriteSwap(int frameIndex, string reason)
        {
            _flipbookReady = false;
            _targetUvCount = 0;
            _validatedFlipbookMaterial = null;
            _validatedFlipbookShader = null;
            RestoreOwnedRenderState();

            if (spriteRenderer != null && _frames != null && _frames.Count > 0 && _frames[0] != null)
            {
                spriteRenderer.sprite = _frames[0];
            }

            string location = frameIndex >= 0 ? " Frame index: " + frameIndex + "." : string.Empty;
            Debug.LogWarning(
                "SpriteRenderer flipbook initialization fell back to sprite swapping. " + reason + location,
                this);
        }

        private void EnsureUvCapacity(int count)
        {
            if (_targetUvRects != null && _targetUvRects.Length >= count)
            {
                return;
            }

            int capacity = _targetUvRects == null || _targetUvRects.Length < 16
                ? 16
                : _targetUvRects.Length;
            while (capacity < count && capacity <= (int.MaxValue >> 1))
            {
                capacity <<= 1;
            }

            if (capacity < count)
            {
                capacity = count;
            }

            _targetUvRects = new Vector4[capacity];
        }

        private static bool IsCompatibleFlipbookMaterial(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.name == FlipbookShaderName &&
                   material.HasProperty(FlipbookBaseRectId) &&
                   material.HasProperty(FlipbookTargetRectId);
        }

        private bool IsValidatedFlipbookMaterialCurrent()
        {
            return flipbookSharedMaterial != null &&
                   flipbookSharedMaterial == _validatedFlipbookMaterial &&
                   flipbookSharedMaterial.shader == _validatedFlipbookShader;
        }
    }
}
