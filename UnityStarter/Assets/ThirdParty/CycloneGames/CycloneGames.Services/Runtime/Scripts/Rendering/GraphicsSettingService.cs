using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Service
{
    public enum ScreenOrientation
    {
        Landscape = 0,
        Portrait = 1
    }

    public interface IGraphicsSettingService
    {
        void SetQualityLevel(int newQualityLevel);
        int CurrentQualityLevel { get; }
        IReadOnlyList<string> QualityLevels { get; }
        void ChangeRenderResolution(int newShortEdgeResolution, ScreenOrientation screenOrientation = ScreenOrientation.Landscape);
        Vector2Int TargetRenderResolution { get; }
        void ChangeApplicationFrameRate(int targetFramerate);
    }

    public class GraphicsSettingService : IGraphicsSettingService
    {
        private const string DEBUG_FLAG = "[GraphicsSetting]";
        private int _currentQualityLevel = -1;
        private CancellationTokenSource _cancelChangeResolution;
        private IReadOnlyList<string> _qualityLevels;
        private Vector2Int _targetRenderResolution;

        public int CurrentQualityLevel
        {
            get
            {
                if (_currentQualityLevel == -1)
                {
                    _currentQualityLevel = QualitySettings.GetQualityLevel();
                }
                return _currentQualityLevel;
            }
        }

        public IReadOnlyList<string> QualityLevels
        {
            get
            {
                if (_qualityLevels == null)
                {
                    _qualityLevels = QualitySettings.names;
                }
                return _qualityLevels;
            }
        }

        /// <summary>
        /// Gets the target rendering resolution that was last set or attempted by the service.
        /// Note: In the Unity Editor, Screen.width and Screen.height might differ from this value.
        /// This property reflects the resolution passed to Screen.SetResolution().
        /// </summary>
        public Vector2Int TargetRenderResolution => _targetRenderResolution;

        public GraphicsSettingService()
        {
            Initialize();
        }

        public void Initialize()
        {
            // Initialize with the current screen resolution at startup
            _targetRenderResolution = new Vector2Int(Screen.width, Screen.height);
            CLogger.LogInfo($"{DEBUG_FLAG} Initialized. Target Render Resolution set to: {Screen.width}x{Screen.height}");
            // _currentQualityLevel will be fetched on first access
            // _qualityLevels will be fetched on first access
        }

        public void SetQualityLevel(int newQualityLevel)
        {
            if (newQualityLevel < 0 || newQualityLevel >= QualityLevels.Count)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid quality level: {newQualityLevel}");
                return;
            }

            CLogger.LogInfo($"{DEBUG_FLAG} CurrentQualityLevel: {CurrentQualityLevel}, NewQualityLevel: {newQualityLevel}");
            QualitySettings.SetQualityLevel(newQualityLevel, true);
            _currentQualityLevel = newQualityLevel;
        }

        public void ChangeRenderResolution(int newShortEdgeResolution, ScreenOrientation screenOrientation = ScreenOrientation.Landscape)
        {
            CancelResolutionChange();

            _cancelChangeResolution = new CancellationTokenSource();
            ChangeScreenResolutionAsync(_cancelChangeResolution.Token, newShortEdgeResolution, screenOrientation).Forget();
        }

        public void ChangeApplicationFrameRate(int targetFramerate)
        {
            Application.targetFrameRate = targetFramerate;
            CLogger.LogInfo($"{DEBUG_FLAG} Change application target frame rate, current: {Application.targetFrameRate}, target: {targetFramerate}");
        }

        private void CancelResolutionChange()
        {
            if (_cancelChangeResolution != null)
            {
                if (_cancelChangeResolution.Token.CanBeCanceled)
                {
                    _cancelChangeResolution.Cancel();
                }
                _cancelChangeResolution.Dispose();
                _cancelChangeResolution = null;
            }
        }

        private async UniTask ChangeScreenResolutionAsync(CancellationToken cancelToken, int newShortEdgeResolution, ScreenOrientation screenOrientation = ScreenOrientation.Landscape)
        {
            try
            {
                // It's important to get the current aspect ratio from the *actual* screen dimensions
                // if you want the new resolution to maintain the current physical display aspect ratio.
                // However, if the game is in a window, Screen.width/height might be the window size.
                // For calculating resolution based on a fixed aspect ratio (e.g., 16:9), you might use a predefined aspect ratio.
                // Here, we're using the current Screen.width/Screen.height which is typical.
                float aspectRatio = (float)Screen.width / Screen.height;
                if (Screen.height == 0) // Avoid division by zero
                {
                    aspectRatio = 16f / 9f; // Default to a common aspect ratio if height is zero
                    CLogger.LogWarning($"{DEBUG_FLAG} Screen.height is 0. Defaulting aspect ratio to 16:9 for calculation.");
                }

                var (newScreenWidth, newScreenHeight) = CalculateNewResolution(newShortEdgeResolution, screenOrientation, aspectRatio);

                // Update the target resolution before attempting to set it
                _targetRenderResolution = new Vector2Int(newScreenWidth, newScreenHeight);
                CLogger.LogInfo($"{DEBUG_FLAG} Attempting to set render resolution to: {_targetRenderResolution.x}x{_targetRenderResolution.y}");
                CLogger.LogInfo($"{DEBUG_FLAG} Current Screen.width/height before SetResolution: {Screen.width}x{Screen.height}");

                Screen.SetResolution(newScreenWidth, newScreenHeight, Screen.fullScreen); // Using Screen.fullScreen to maintain current mode
                
                // Log what was commanded to Screen.SetResolution
                CLogger.LogInfo($"{DEBUG_FLAG} Screen.SetResolution({newScreenWidth}, {newScreenHeight}, {Screen.fullScreen}) called.");

                // A short delay can sometimes be useful for Screen.width/height to update, though not guaranteed.
                await UniTask.Delay(100, DelayType.Realtime, PlayerLoopTiming.Update, cancelToken);

                // Log the reported Screen.width/height after the attempt.
                // This is what Unity reports, which can differ from _targetRenderResolution, especially in editor.
                CLogger.LogInfo($"{DEBUG_FLAG} Post-change screen resolution, reported by Screen.width/height: {Screen.width}x{Screen.height}. Target was: {_targetRenderResolution.x}x{_targetRenderResolution.y}");
            }
            catch (OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Resolution change was canceled.");
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} An error occurred while changing the resolution: {ex.Message}");
            }
        }

        private (int width, int height) CalculateNewResolution(int newShortEdgeResolution, ScreenOrientation screenOrientation, float aspectRatio)
        {
            if (aspectRatio <= 0) // Safety check for invalid aspect ratio
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid aspect ratio ({aspectRatio}) for resolution calculation. Defaulting to 16:9 aspect ratio for calculation logic.");
                aspectRatio = 16f / 9f; // Fallback to a common aspect ratio
            }

            int calculatedWidth, calculatedHeight;

            switch (screenOrientation)
            {
                case ScreenOrientation.Landscape:
                    // Short edge is height
                    calculatedHeight = newShortEdgeResolution;
                    calculatedWidth = Mathf.RoundToInt(newShortEdgeResolution * aspectRatio);
                    break;
                case ScreenOrientation.Portrait:
                    // Short edge is width
                    calculatedWidth = newShortEdgeResolution;
                    calculatedHeight = Mathf.RoundToInt(newShortEdgeResolution / aspectRatio);
                    break;
                default:
                    CLogger.LogError($"{DEBUG_FLAG} Unknown screen orientation: {screenOrientation}. Defaulting to Landscape calculation.");
                    // Defaulting to Landscape logic as a fallback
                    calculatedHeight = newShortEdgeResolution;
                    calculatedWidth = Mathf.RoundToInt(newShortEdgeResolution * aspectRatio);
                    break; // Or throw new ArgumentOutOfRangeException
            }
            // Ensure non-zero dimensions
            if (calculatedWidth <= 0) calculatedWidth = 1;
            if (calculatedHeight <= 0) calculatedHeight = 1;

            return (calculatedWidth, calculatedHeight);
        }
    }
}