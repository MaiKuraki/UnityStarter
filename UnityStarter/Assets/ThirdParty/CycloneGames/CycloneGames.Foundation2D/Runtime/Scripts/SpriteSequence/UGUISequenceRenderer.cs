using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

        internal const string FlipbookShaderName = "UI/FlipbookRemap";

        [SerializeField] private Image image;
        [SerializeField] private UGUIRenderMode renderMode = UGUIRenderMode.SpriteSwap;
        [SerializeField] private Material flipbookSharedMaterial;
        [SerializeField] private FlipbookUVMeshEffect flipbookUvMeshEffect;

        private IReadOnlyList<Sprite> _frames;
        private Vector4[] _targetUvRects;
        private int _targetUvCount;
        private Vector4 _baseUvRect;
        private Vector4 _currentTargetUvRect;
        private bool _flipbookReady;
        private bool _configurationWarningIssued;

        private Image _materialOwner;
        private Material _originalMaterial;
        private Material _ownedMaterial;
        private bool _originalUsedDefaultMaterial;
        private Material _validatedFlipbookMaterial;
        private Shader _validatedFlipbookShader;
        private bool _ownsMaterial;

        private FlipbookUVMeshEffect _meshEffectOwner;
        private Vector4 _originalEffectBaseUvRect;
        private Vector4 _originalEffectTargetUvRect;
        private Vector4 _ownedEffectBaseUvRect;
        private Vector4 _ownedEffectTargetUvRect;
        private bool _ownsMeshEffectState;

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
            RestoreOwnedMaterial();
            RestoreOwnedMeshEffectState();
        }

        private void OnDestroy()
        {
            RestoreOwnedMaterial();
            RestoreOwnedMeshEffectState();
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
            RestoreOwnedMaterial();
            ResetFlipbookState();

            _frames = frames;
            _configurationWarningIssued = false;

            if (image == null || frames == null || frames.Count == 0)
            {
                return;
            }

            if (renderMode != UGUIRenderMode.ShaderFlipbookSharedMaterial)
            {
                return;
            }

            if (!IsCompatibleFlipbookMaterial(flipbookSharedMaterial))
            {
                FallbackToSpriteSwap(0, "The assigned material must use the " + FlipbookShaderName + " shader.");
                return;
            }

            if (!IsMeshEffectReady())
            {
                FallbackToSpriteSwap(
                    0,
                    "The FlipbookUVMeshEffect must be enabled on the same Image, and its Canvas must explicitly enable TexCoord1 and TexCoord2 shader channels.");
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

            AcquireMaterial(flipbookSharedMaterial);
            image.sprite = frames[0];
            ApplyOwnedMeshEffectRects(_baseUvRect, _currentTargetUvRect);
        }

        public void ApplyFrame(int frameIndex, bool forceRefresh)
        {
            EnsureDependencies();
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

            if (_flipbookReady &&
                renderMode == UGUIRenderMode.ShaderFlipbookSharedMaterial &&
                frameIndex < _targetUvCount &&
                IsValidatedFlipbookMaterialCurrent() &&
                IsMeshEffectReady())
            {
                AcquireMaterial(flipbookSharedMaterial);
                if (forceRefresh || image.sprite != _frames[0])
                {
                    image.sprite = _frames[0];
                }

                _currentTargetUvRect = _targetUvRects[frameIndex];
                ApplyOwnedMeshEffectRects(_baseUvRect, _currentTargetUvRect);
                return;
            }

            if (_flipbookReady)
            {
                DisableFlipbook();
                WarnConfigurationOnce(
                    "UGUI flipbook rendering became unavailable and fell back to sprite swapping. Verify the material, mesh effect, and Canvas TexCoord1/TexCoord2 channels.");
            }

            if (forceRefresh || image.sprite != sprite)
            {
                image.sprite = sprite;
            }
        }

        public void SetVisible(bool visible)
        {
            EnsureDependencies();
            if (image != null)
            {
                image.enabled = visible;
            }
        }

        private void EnsureDependencies()
        {
            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (flipbookUvMeshEffect == null)
            {
                flipbookUvMeshEffect = GetComponent<FlipbookUVMeshEffect>();
            }

            if (_ownsMaterial && _materialOwner != image)
            {
                RestoreOwnedMaterial();
            }

            if (_ownsMeshEffectState && _meshEffectOwner != flipbookUvMeshEffect)
            {
                RestoreOwnedMeshEffectState();
            }
        }

        private void RestoreConfiguredRenderState()
        {
            if (image == null || !_flipbookReady || _frames == null || _frames.Count == 0)
            {
                return;
            }

            if (!IsValidatedFlipbookMaterialCurrent() || !IsMeshEffectReady())
            {
                DisableFlipbook();
                return;
            }

            AcquireMaterial(flipbookSharedMaterial);
            image.sprite = _frames[0];
            ApplyOwnedMeshEffectRects(_baseUvRect, _currentTargetUvRect);
        }

        private void AcquireMaterial(Material material)
        {
            if (image == null || material == null)
            {
                return;
            }

            if (_ownsMaterial &&
                (_materialOwner != image || _materialOwner.material != _ownedMaterial))
            {
                ReleaseMaterialOwnership();
            }

            if (!_ownsMaterial)
            {
                _materialOwner = image;
                _originalMaterial = image.material;
                _originalUsedDefaultMaterial =
                    _originalMaterial == image.defaultMaterial ||
                    _originalMaterial == Image.defaultETC1GraphicMaterial;
                _ownsMaterial = true;
            }

            if (image.material != material)
            {
                image.material = material;
            }

            _ownedMaterial = material;
        }

        private void RestoreOwnedMaterial()
        {
            if (!_ownsMaterial)
            {
                return;
            }

            if (_materialOwner != null && _materialOwner.material == _ownedMaterial)
            {
                _materialOwner.material = _originalUsedDefaultMaterial ? null : _originalMaterial;
            }

            ReleaseMaterialOwnership();
        }

        private void ReleaseMaterialOwnership()
        {
            _materialOwner = null;
            _originalMaterial = null;
            _ownedMaterial = null;
            _originalUsedDefaultMaterial = false;
            _ownsMaterial = false;
        }

        private void ApplyOwnedMeshEffectRects(in Vector4 baseRect, in Vector4 targetRect)
        {
            if (flipbookUvMeshEffect == null)
            {
                return;
            }

            if (_ownsMeshEffectState && _meshEffectOwner != flipbookUvMeshEffect)
            {
                RestoreOwnedMeshEffectState();
            }

            if (!_ownsMeshEffectState)
            {
                _meshEffectOwner = flipbookUvMeshEffect;
                _meshEffectOwner.GetFlipbookRects(
                    out _originalEffectBaseUvRect,
                    out _originalEffectTargetUvRect);
                _ownsMeshEffectState = true;
            }

            _ownedEffectBaseUvRect = baseRect;
            _ownedEffectTargetUvRect = targetRect;
            _meshEffectOwner.SetFlipbookRects(baseRect, targetRect);
        }

        private void RestoreOwnedMeshEffectState()
        {
            if (!_ownsMeshEffectState)
            {
                return;
            }

            if (_meshEffectOwner != null &&
                _meshEffectOwner.HasFlipbookRects(_ownedEffectBaseUvRect, _ownedEffectTargetUvRect))
            {
                _meshEffectOwner.SetFlipbookRects(
                    _originalEffectBaseUvRect,
                    _originalEffectTargetUvRect);
            }

            _meshEffectOwner = null;
            _originalEffectBaseUvRect = default;
            _originalEffectTargetUvRect = default;
            _ownedEffectBaseUvRect = default;
            _ownedEffectTargetUvRect = default;
            _ownsMeshEffectState = false;
        }

        private void DisableFlipbook()
        {
            _flipbookReady = false;
            _targetUvCount = 0;
            _validatedFlipbookMaterial = null;
            _validatedFlipbookShader = null;
            RestoreOwnedMaterial();
            RestoreOwnedMeshEffectState();
        }

        private void ResetFlipbookState()
        {
            DisableFlipbook();
            _baseUvRect = default;
            _currentTargetUvRect = default;
        }

        private void FallbackToSpriteSwap(int frameIndex, string reason)
        {
            DisableFlipbook();
            if (image != null && _frames != null && _frames.Count > 0 && _frames[0] != null)
            {
                image.sprite = _frames[0];
            }

            string location = frameIndex >= 0 ? " Frame index: " + frameIndex + "." : string.Empty;
            WarnConfigurationOnce(
                "UGUI flipbook initialization fell back to sprite swapping. " + reason + location);
        }

        private void WarnConfigurationOnce(string message)
        {
            if (_configurationWarningIssued)
            {
                return;
            }

            _configurationWarningIssued = true;
            Debug.LogWarning(message, this);
        }

        private bool IsMeshEffectReady()
        {
            return flipbookUvMeshEffect != null &&
                   flipbookUvMeshEffect.isActiveAndEnabled &&
                   flipbookUvMeshEffect.IsReadyFor(image);
        }

        private bool IsValidatedFlipbookMaterialCurrent()
        {
            return flipbookSharedMaterial != null &&
                   flipbookSharedMaterial == _validatedFlipbookMaterial &&
                   flipbookSharedMaterial.shader == _validatedFlipbookShader;
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
    }
}
