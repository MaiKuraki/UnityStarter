using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Logger;
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
        List<string> QualityLevels { get; }
        void ChangeRenderResolution(int newShortEdgeResolution, ScreenOrientation screenOrientation = ScreenOrientation.Landscape);
        void ChangeApplicationFrameRate(int targetFramerate);
    }

    public class GraphicsSettingService : IGraphicsSettingService
    {
        private const string DEBUG_FLAG = "[GraphicsSetting]";
        private int currentQualityLevel = int.MinValue;
        private CancellationTokenSource cancelChangeResolution;

        public int CurrentQualityLevel
        {
            get
            {
                if (currentQualityLevel == int.MinValue)
                {
                    currentQualityLevel = QualitySettings.GetQualityLevel();
                }
                return currentQualityLevel;
            }
        }

        private List<string> qualitySettingsList;

        public List<string> QualityLevels
        {
            get
            {
                if (qualitySettingsList == null)
                {
                    qualitySettingsList = QualitySettings.names.ToList();
                }
                return qualitySettingsList;
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
                MLogger.LogError($"{DEBUG_FLAG} Invalid quality level: {newQualityLevel}");
                return;
            }

            MLogger.LogInfo($"{DEBUG_FLAG} CurrentQualityLevel: {CurrentQualityLevel}, NewQualityLevel: {newQualityLevel}");
            QualitySettings.SetQualityLevel(newQualityLevel, true);
            currentQualityLevel = newQualityLevel;
        }

        public void ChangeRenderResolution(int newShortEdgeResolution, ScreenOrientation screenOrientation = ScreenOrientation.Landscape)
        {
            // Cancel ongoing resolution change
            CancelResolutionChange();

            cancelChangeResolution = new CancellationTokenSource();
            ChangeScreenResolutionAsync(cancelChangeResolution.Token, newShortEdgeResolution, screenOrientation).Forget();
        }

        public void ChangeApplicationFrameRate(int targetFramerate)
        {
            Application.targetFrameRate = targetFramerate;
            MLogger.LogInfo($"{DEBUG_FLAG} Application target frame rate set to: {targetFramerate}");
        }

        private void CancelResolutionChange()
        {
            if (cancelChangeResolution != null)
            {
                if (cancelChangeResolution.Token.CanBeCanceled)
                {
                    cancelChangeResolution.Cancel();
                }
                cancelChangeResolution.Dispose();
                cancelChangeResolution = null;
            }
        }

        private async UniTask ChangeScreenResolutionAsync(CancellationToken cancelToken, int newShortEdgeResolution, ScreenOrientation screenOrientation = ScreenOrientation.Landscape)
        {
            try
            {
                float aspectRatio = (float)Screen.width / Screen.height;
                int newScreenWidth;
                int newScreenHeight;

                (newScreenWidth, newScreenHeight) = CalculateNewResolution(newShortEdgeResolution, screenOrientation, aspectRatio);

                Screen.SetResolution(newScreenWidth, newScreenHeight, true);
                MLogger.LogInfo($"{DEBUG_FLAG} Changed resolution to: {newScreenWidth}x{newScreenHeight}");

                await UniTask.Delay(500, DelayType.Realtime, PlayerLoopTiming.Update, cancelToken);
                MLogger.LogInfo($"{DEBUG_FLAG} Current resolution after change: {Screen.currentResolution.width}x{Screen.currentResolution.height}");
            }
            catch (OperationCanceledException)
            {
                MLogger.LogInfo($"{DEBUG_FLAG} Resolution change was canceled.");
            }
            catch (Exception ex)
            {
                MLogger.LogError($"{DEBUG_FLAG} An error occurred while changing the resolution: {ex.Message}");
            }
        }

        private (int width, int height) CalculateNewResolution(int newShortEdgeResolution, ScreenOrientation screenOrientation, float aspectRatio)
        {
            int newScreenHeight, newScreenWidth;
            switch (screenOrientation)
            {
                case ScreenOrientation.Landscape:
                    newScreenHeight = newShortEdgeResolution;
                    newScreenWidth = (int)(newScreenHeight * aspectRatio);
                    break;
                case ScreenOrientation.Portrait:
                    newScreenWidth = newShortEdgeResolution;
                    newScreenHeight = (int)(newScreenWidth / aspectRatio);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(screenOrientation), screenOrientation, null);
            }
            return (newScreenWidth, newScreenHeight);
        }
    }
}