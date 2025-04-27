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
        void ChangeApplicationFrameRate(int targetFramerate);
    }

    public class GraphicsSettingService : IGraphicsSettingService
    {
        private const string DEBUG_FLAG = "[GraphicsSetting]";
        private int _currentQualityLevel = -1;
        private CancellationTokenSource _cancelChangeResolution;
        private IReadOnlyList<string> _qualityLevels;

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

        public GraphicsSettingService()
        {
            Initialize();
        }

        public void Initialize()
        {

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
                float aspectRatio = (float)Screen.width / Screen.height;
                var (newScreenWidth, newScreenHeight) = CalculateNewResolution(newShortEdgeResolution, screenOrientation, aspectRatio);

                Screen.SetResolution(newScreenWidth, newScreenHeight, true);
                CLogger.LogInfo($"{DEBUG_FLAG} Pre-change screen resolution, current: {Screen.width}x{Screen.height}, target: {newScreenWidth}x{newScreenHeight}");

                await UniTask.Delay(100, DelayType.Realtime, PlayerLoopTiming.Update, cancelToken);
                CLogger.LogInfo($"{DEBUG_FLAG} Post-change screen resolution, final result: {Screen.width}x{Screen.height}");
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
            return screenOrientation switch
            {
                ScreenOrientation.Landscape => (width: (int)(newShortEdgeResolution * aspectRatio), height: newShortEdgeResolution),
                ScreenOrientation.Portrait => (width: newShortEdgeResolution, height: (int)(newShortEdgeResolution / aspectRatio)),
                _ => throw new ArgumentOutOfRangeException(nameof(screenOrientation), screenOrientation, null)
            };
        }
    }
}