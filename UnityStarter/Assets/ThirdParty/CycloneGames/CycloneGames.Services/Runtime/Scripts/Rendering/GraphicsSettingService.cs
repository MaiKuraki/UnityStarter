using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CycloneGames.Service.Runtime
{
    public enum ScreenOrientation
    {
        Landscape = 0,
        Portrait = 1
    }

    public interface IGraphicsSettingService
    {
        // Quality & Frame Rate
        void SetQualityLevel(int qualityLevel);
        int CurrentQualityLevel { get; }
        IReadOnlyList<string> QualityLevels { get; }
        void SetTargetFrameRate(int targetFramerate);
        void SetVSyncCount(int vSyncCount);

        // Resolution
        void SetRenderResolution(int shortEdgeResolution, ScreenOrientation orientation = ScreenOrientation.Landscape);
        Vector2Int TargetRenderResolution { get; }
        void SetRenderScale(float scale);
        float RenderScale { get; }

        // Anti-Aliasing
        void SetAntiAliasing(int msaaLevel);
        int AntiAliasingLevel { get; }

        // Shadows
        void SetShadowDistance(float distance);

        // Textures
        void SetTextureQuality(int mipmapLimit);
        int TextureQuality { get; }
        void SetAnisotropicFiltering(AnisotropicFiltering mode);
        AnisotropicFiltering AnisotropicFilteringMode { get; }

        // LOD & Rendering Features
        void SetLodBias(float bias);
        float LodBias { get; }
        void SetSoftParticles(bool enabled);
        bool SoftParticlesEnabled { get; }

        // HDR (URP)
        void SetHDR(bool enabled, Camera camera = null);

        // Bulk Apply
        void ApplySettings(in GraphicsSettingsData settings, Camera camera = null);
    }

    public sealed class GraphicsSettingService : IGraphicsSettingService, IDisposable
    {
        private const string DEBUG_FLAG = "[GraphicsSettings]";

        private int _currentQualityLevel = -1;
        private IReadOnlyList<string> _qualityLevels;
        private Vector2Int _targetRenderResolution;
        private CancellationTokenSource _resolutionCts;

        public int CurrentQualityLevel
        {
            get
            {
                if (_currentQualityLevel < 0)
                    _currentQualityLevel = QualitySettings.GetQualityLevel();
                return _currentQualityLevel;
            }
        }

        public IReadOnlyList<string> QualityLevels => _qualityLevels ??= QualitySettings.names;
        public Vector2Int TargetRenderResolution => _targetRenderResolution;
        public float RenderScale => UniversalRenderPipeline.asset?.renderScale ?? 1f;
        public int AntiAliasingLevel => QualitySettings.antiAliasing;
        public int TextureQuality => QualitySettings.globalTextureMipmapLimit;
        public AnisotropicFiltering AnisotropicFilteringMode => QualitySettings.anisotropicFiltering;
        public float LodBias => QualitySettings.lodBias;
        public bool SoftParticlesEnabled => QualitySettings.softParticles;

        public GraphicsSettingService()
        {
            _targetRenderResolution = new Vector2Int(Screen.width, Screen.height);
            CLogger.LogInfo($"{DEBUG_FLAG} Initialized. Resolution: {Screen.width}x{Screen.height}");
        }

        #region Quality & Frame Rate

        public void SetQualityLevel(int qualityLevel)
        {
            if (qualityLevel < 0 || qualityLevel >= QualityLevels.Count)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid quality level: {qualityLevel}");
                return;
            }
            QualitySettings.SetQualityLevel(qualityLevel, true);
            _currentQualityLevel = qualityLevel;
            CLogger.LogInfo($"{DEBUG_FLAG} Quality level set to: {qualityLevel}");
        }

        public void SetTargetFrameRate(int targetFramerate)
        {
            Application.targetFrameRate = targetFramerate;
        }

        public void SetVSyncCount(int vSyncCount)
        {
            QualitySettings.vSyncCount = Mathf.Clamp(vSyncCount, 0, 4);
        }

        #endregion

        #region Resolution

        public void SetRenderResolution(int shortEdgeResolution, ScreenOrientation orientation = ScreenOrientation.Landscape)
        {
            CancelPendingResolutionChange();
            _resolutionCts = new CancellationTokenSource();
            SetResolutionAsync(shortEdgeResolution, orientation, _resolutionCts.Token).Forget();
        }

        public void SetRenderScale(float scale)
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset == null)
            {
                CLogger.LogWarning("URP asset not found, cannot set render scale", DEBUG_FLAG);
                return;
            }
            asset.renderScale = Mathf.Clamp(scale, 0.1f, 2f);
        }

        private async UniTaskVoid SetResolutionAsync(int shortEdge, ScreenOrientation orientation, CancellationToken ct)
        {
            try
            {
                float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
                var (w, h) = CalculateResolution(shortEdge, orientation, aspect);
                _targetRenderResolution = new Vector2Int(w, h);

                Screen.SetResolution(w, h, Screen.fullScreen);
                await UniTask.Delay(100, DelayType.Realtime, PlayerLoopTiming.Update, ct);

                CLogger.LogInfo($"{DEBUG_FLAG} Resolution changed to: {w}x{h}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Resolution change failed: {ex.Message}");
            }
        }

        private static (int width, int height) CalculateResolution(int shortEdge, ScreenOrientation orientation, float aspect)
        {
            if (aspect <= 0) aspect = 16f / 9f;

            int w, h;
            if (orientation == ScreenOrientation.Landscape)
            {
                h = shortEdge;
                w = Mathf.RoundToInt(shortEdge * aspect);
            }
            else
            {
                w = shortEdge;
                h = Mathf.RoundToInt(shortEdge / aspect);
            }
            return (Mathf.Max(1, w), Mathf.Max(1, h));
        }

        private void CancelPendingResolutionChange()
        {
            if (_resolutionCts != null)
            {
                _resolutionCts.Cancel();
                _resolutionCts.Dispose();
                _resolutionCts = null;
            }
        }

        #endregion

        #region Anti-Aliasing

        public void SetAntiAliasing(int msaaLevel)
        {
            // Valid MSAA levels: 0, 2, 4, 8
            int validLevel = msaaLevel switch
            {
                <= 0 => 0,
                <= 2 => 2,
                <= 4 => 4,
                _ => 8
            };
            QualitySettings.antiAliasing = validLevel;

            // Also set on URP asset if available
            var asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                asset.msaaSampleCount = validLevel == 0 ? 1 : validLevel;
            }
        }

        #endregion

        #region Shadows

        /// <summary>
        /// Sets shadow distance. For URP shadow resolution/cascades, modify the URP Asset directly or use Quality Levels.
        /// </summary>
        public void SetShadowDistance(float distance)
        {
            QualitySettings.shadowDistance = Mathf.Max(0, distance);
        }

        #endregion

        #region Textures

        public void SetTextureQuality(int mipmapLimit)
        {
            // 0=Full, 1=Half, 2=Quarter, 3=Eighth
            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(mipmapLimit, 0, 3);
        }

        public void SetAnisotropicFiltering(AnisotropicFiltering mode)
        {
            QualitySettings.anisotropicFiltering = mode;
        }

        #endregion

        #region LOD & Features

        public void SetLodBias(float bias)
        {
            QualitySettings.lodBias = Mathf.Clamp(bias, 0.3f, 2f);
        }

        public void SetSoftParticles(bool enabled)
        {
            QualitySettings.softParticles = enabled;
        }

        #endregion

        #region HDR

        public void SetHDR(bool enabled, Camera camera = null)
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                asset.supportsHDR = enabled;
            }

            // Also set post-processing on specific camera if provided
            if (camera != null)
            {
                var cameraData = camera.GetUniversalAdditionalCameraData();
                if (cameraData != null)
                {
                    cameraData.renderPostProcessing = enabled;
                }
            }
        }

        #endregion

        #region Bulk Apply

        public void ApplySettings(in GraphicsSettingsData settings, Camera camera = null)
        {
            SetQualityLevel(settings.QualityLevel);
            SetTargetFrameRate(settings.TargetFrameRate);
            SetVSyncCount(settings.VSyncCount);
            SetAntiAliasing(settings.AntiAliasingLevel);
            SetShadowDistance(settings.ShadowDistance);
            SetTextureQuality(settings.TextureQuality);
            SetAnisotropicFiltering((AnisotropicFiltering)settings.AnisotropicFiltering);
            SetLodBias(settings.LodBias);
            SetSoftParticles(settings.SoftParticles);
            SetRenderScale(settings.RenderScale);
            SetHDR(settings.HDREnabled, camera);

            if (settings.ShortEdgeResolution > 0)
            {
                SetRenderResolution(settings.ShortEdgeResolution);
            }

            CLogger.LogInfo($"{DEBUG_FLAG} All graphics settings applied");
        }

        #endregion

        public void Dispose()
        {
            CancelPendingResolutionChange();
        }
    }
}